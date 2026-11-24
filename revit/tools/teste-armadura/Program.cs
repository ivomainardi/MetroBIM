using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MetroSP.Ifc;

// Um IFC de exemplo com três conjuntos de armadura, como o exportador da Autodesk grava:
//   Tag 100: conjunto variável, um IfcReinforcingBar por barra (2,5 m, 3,0 m, 3,0 m), a primeira com um Qto antigo;
//   Tag 200: conjunto uniforme de 5 barras de 1,2 m exportado como um IfcReinforcingBar só;
//   Tag 999: sem armadura correspondente no Revit.
if (args.Length > 2 && args[0] == "tipos") return TesteAco.Tipos(args[1], args[2]);
if (args.Length > 2 && args[0] == "aco") return TesteAco.ArquivoReal(args[1], args[2]);
if (args.Length > 0) return TesteIso.ArquivoReal(args[0], args.Length > 1 ? args[1] : null);
int falhas = 0;
void Confere(bool ok, string o)
{
    Console.WriteLine((ok ? "  ok    " : "  FALHA ") + o);
    if (!ok) falhas++;
}

var revit = new Dictionary<long, List<BarraRevit>>
{
    [100] = new() { new() { Comprimento = 2.5, Peso = 1.54 }, new() { Comprimento = 3.0, Peso = 1.85 }, new() { Comprimento = 3.0, Peso = 1.85 } },
    [200] = Enumerable.Range(0, 5).Select(_ => new BarraRevit { Comprimento = 1.2, Peso = 0.74 }).ToList(),
};

foreach (string schema in new[] { "IFC4X3_ADD2", "IFC4", "IFC2X3" })
{
    Console.WriteLine(schema);
    bool x3 = schema == "IFC2X3";
    string Barra(int id, string tag) => x3
        ? $"#{id}=IFCREINFORCINGBAR('0000000000000000000{id}',#2,'Barra',$,$,$,$,'{tag}',$,10.,78.5,9.84,.MAIN.,$);"
        : $"#{id}=IFCREINFORCINGBAR('0000000000000000000{id}',#2,'Barra',$,$,$,$,'{tag}',$,$,$,$,.MAIN.,$);";
    string fim = x3 ? ");" : ",$);";
    string ifc = $@"ISO-10303-21;
HEADER;FILE_DESCRIPTION(('ViewDefinition'),'2;1');FILE_NAME('t.ifc','2026-01-01T00:00:00',(''),(''),'','','');FILE_SCHEMA(('{schema}'));ENDSEC;
DATA;
#2=IFCOWNERHISTORY($,$,$,$,$,$,$,0);
#5=IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.);
#6=IFCSIUNIT(*,.MASSUNIT.,.KILO.,.GRAM.);
#7=IFCUNITASSIGNMENT((#5,#6));
{Barra(10, "100")}
{Barra(11, "100")}
{Barra(12, "100")}
{Barra(20, "200")}
{Barra(30, "999")}
#41=IFCQUANTITYLENGTH('Length',$,$,0.{fim}
#42=IFCQUANTITYAREA('Area',$,$,5.{fim}
#43=IFCELEMENTQUANTITY('0000000000000000000043',#2,'Qto_ReinforcingElementBaseQuantities',$,$,(#41,#42));
#44=IFCRELDEFINESBYPROPERTIES('0000000000000000000044',#2,$,$,(#10,#30),#43);
ENDSEC;
END-ISO-10303-21;
";
    string arq = Path.Combine(Path.GetTempPath(), "teste-armadura-" + schema + ".ifc");
    File.WriteAllText(arq, ifc);
    StepModel m = StepModel.Load(arq);
    ArmaduraIfc.Resultado r = ArmaduraIfc.Aplicar(m, id => revit.TryGetValue(id, out var b) ? b : null);
    m.Save(arq);
    m = StepModel.Load(arq);

    Confere(r.Gravadas == 4 && r.SemRevit == 1 && r.Diferentes == 0, $"resultado: {r}");

    // Quantidades de cada barra pelo IfcRelDefinesByProperties
    Dictionary<string, string> Qto(int barra)
    {
        var d = new Dictionary<string, string>();
        foreach (StepEntity rel in m.OfType("IFCRELDEFINESBYPROPERTIES"))
        {
            if (!StepModel.RefList(rel.Args[4]).Contains(barra)) continue;
            StepEntity q = m.Get(StepModel.Ref(rel.Args[5]));
            if (StepModel.Text(q.Args[2]) != ArmaduraIfc.Qto) continue;
            foreach (int p in StepModel.RefList(q.Args[5])) d[StepModel.Text(m.Get(p).Args[0])] = m.Get(p).Args[3];
        }
        return d;
    }
    string Txt(Dictionary<string, string> d) => string.Join(" ", d.OrderBy(k => k.Key).Select(k => k.Key + "=" + k.Value));
    Confere(Txt(Qto(10)) == "Area=5. Count=1 Length=2500. Weight=1.54", "barra variável 1 (mantém a Area do Qto antigo): " + Txt(Qto(10)));
    Confere(Txt(Qto(11)) == "Count=1 Length=3000. Weight=1.85", "barra variável 2: " + Txt(Qto(11)));
    Confere(Txt(Qto(12)) == "Count=1 Length=3000. Weight=1.85", "barra variável 3: " + Txt(Qto(12)));
    Confere(Txt(Qto(20)) == "Count=5 Length=6000. Weight=3.7", "conjunto uniforme numa peça só: " + Txt(Qto(20)));
    Confere(Txt(Qto(30)) == "Area=5. Length=0.", "sem Revit fica como estava: " + Txt(Qto(30)));
    if (x3)
    {
        Confere(m.Get(10).Args[11] == "2500." && m.Get(20).Args[11] == "1200." && m.Get(30).Args[11] == "9.84",
            $"BarLength 2x3: {m.Get(10).Args[11]}, {m.Get(20).Args[11]}, {m.Get(30).Args[11]}");
    }
    File.Delete(arq);
}

// Unidades: pés e libras (IfcConversionBasedUnit)
{
    string ifc = @"ISO-10303-21;
HEADER;FILE_SCHEMA(('IFC4'));ENDSEC;
DATA;
#1=IFCSIUNIT(*,.LENGTHUNIT.,$,.METRE.);
#2=IFCMEASUREWITHUNIT(IFCLENGTHMEASURE(0.3048),#1);
#3=IFCDIMENSIONALEXPONENTS(1,0,0,0,0,0,0);
#4=IFCCONVERSIONBASEDUNIT(#3,.LENGTHUNIT.,'FOOT',#2);
#5=IFCUNITASSIGNMENT((#4));
ENDSEC;
END-ISO-10303-21;
";
    string arq = Path.Combine(Path.GetTempPath(), "teste-armadura-pes.ifc");
    File.WriteAllText(arq, ifc);
    double f = ArmaduraIfc.FatorUnidade(StepModel.Load(arq), "LENGTHUNIT", 1);
    Confere(Math.Abs(f - 0.3048) < 1e-9, "pé = " + f + " m");
    File.Delete(arq);
}

falhas += TesteIso.Sintetico();
Console.WriteLine(falhas == 0 ? "Todos os testes passaram." : falhas + " falha(s).");
return falhas == 0 ? 0 : 1;
