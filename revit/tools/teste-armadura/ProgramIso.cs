using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MetroSP.Ifc;

// Testes dos dados padrão das barras (ArmaduraIsoIfc), sem Revit.
static class TesteIso
{
    static int _falhas;
    static void Confere(bool ok, string o)
    {
        Console.WriteLine((ok ? "  ok    " : "  FALHA ") + o);
        if (!ok) _falhas++;
    }

    static Dictionary<string, string> Props(StepModel m, int obj, string pset)
    {
        var d = new Dictionary<string, string>();
        foreach (StepEntity rel in m.OfType("IFCRELDEFINESBYPROPERTIES"))
        {
            if (!StepModel.RefList(rel.Args[4]).Contains(obj)) continue;
            StepEntity p = m.Get(StepModel.Ref(rel.Args[5]));
            if (p.Type != "IFCPROPERTYSET" || StepModel.Text(p.Args[2]) != pset) continue;
            foreach (int x in StepModel.RefList(p.Args[4])) d[StepModel.Text(m.Get(x).Args[0])] = m.Get(x).Args[2];
        }
        return d;
    }

    // Um conjunto variável (Tag 100, 2 barras), um anel fechado (Tag 200, placement girado 90°) e uma barra reta (Tag 300).
    public static int Sintetico()
    {
        Console.WriteLine("Dados padrão (ArmaduraIsoIfc)");
        string ifc = @"ISO-10303-21;
HEADER;FILE_DESCRIPTION(('ViewDefinition'),'2;1');FILE_NAME('t.ifc','2026-01-01T00:00:00',(''),(''),'','','');FILE_SCHEMA(('IFC4'));ENDSEC;
DATA;
#2=IFCOWNERHISTORY($,$,$,$,$,$,$,0);
#5=IFCSIUNIT(*,.LENGTHUNIT.,$,.METRE.);
#6=IFCSIUNIT(*,.MASSUNIT.,.KILO.,.GRAM.);
#7=IFCUNITASSIGNMENT((#5,#6));
#8=IFCCARTESIANPOINT((0.,0.,0.));
#9=IFCDIRECTION((0.,0.,1.));
#3=IFCDIRECTION((0.,1.,0.));
#4=IFCAXIS2PLACEMENT3D(#8,#9,#3);
#1=IFCLOCALPLACEMENT($,#4);
#50=IFCCARTESIANPOINTLIST3D(((0.,0.,0.),(2.,0.,0.)));
#51=IFCINDEXEDPOLYCURVE(#50,(IFCLINEINDEX((1,2))),.F.);
#52=IFCSWEPTDISKSOLID(#51,0.005,$,0.,1.);
#53=IFCSHAPEREPRESENTATION($,'Body','AdvancedSweptSolid',(#52));
#54=IFCPRODUCTDEFINITIONSHAPE($,$,(#53));
#60=IFCCARTESIANPOINTLIST3D(((5.,0.,0.),(-5.,0.,0.)));
#61=IFCINDEXEDPOLYCURVE(#60,(IFCARCINDEX((1,2,1))),.F.);
#62=IFCSWEPTDISKSOLID(#61,0.008,$,0.,360.);
#63=IFCSHAPEREPRESENTATION($,'Body','AdvancedSweptSolid',(#62));
#64=IFCPRODUCTDEFINITIONSHAPE($,$,(#63));
#10=IFCREINFORCINGBAR('0000000000000000000010',#2,'Barra 1',$,$,#1,#54,'100',$,$,$,$,$,$);
#11=IFCREINFORCINGBAR('0000000000000000000011',#2,'Barra 2',$,$,#1,#54,'100',$,$,$,$,$,$);
#20=IFCREINFORCINGBAR('0000000000000000000020',#2,'Anel',$,$,#1,#64,'200',$,$,$,$,.NOTDEFINED.,$);
#30=IFCREINFORCINGBAR('0000000000000000000030',#2,'Reta',$,$,#1,#54,'300',$,$,$,$,$,$);
#70=IFCREINFORCINGBARTYPE('0000000000000000000070',#2,'Ø10',$,$,$,$,$,$,.NOTDEFINED.,$,$,$,$,$,$);
#71=IFCRELDEFINESBYTYPE('0000000000000000000071',#2,$,$,(#10,#11,#30),#70);
#80=IFCMATERIAL('Rebar - ASTM A615M - Grade 420',$,'Metal');
#81=IFCRELASSOCIATESMATERIAL('0000000000000000000081',#2,$,$,(#10,#11,#20,#30),#80);
#82=IFCCLASSIFICATIONREFERENCE($,'4.142.00','A\X2\00E7\X0\o CA - 50 - fornecimento',$,$,$);
#83=IFCRELASSOCIATESCLASSIFICATION('0000000000000000000083',#2,$,$,(#10,#11,#20,#30),#82);
#90=IFCPROPERTYSINGLEVALUE('Quantidade',$,IFCINTEGER(2),$);
#91=IFCPROPERTYSINGLEVALUE('PesoDoConjunto',$,IFCREAL(3.),$);
#97=IFCPROPERTYSINGLEVALUE('PesoLinear',$,IFCREAL(0.617),$);
#92=IFCPROPERTYSET('0000000000000000000092',#2,'MetroQto',$,(#90,#91,#97));
#93=IFCRELDEFINESBYPROPERTIES('0000000000000000000093',#2,$,$,(#10,#11),#92);
#94=IFCPROPERTYSINGLEVALUE('Reference',$,IFCIDENTIFIER('10'),$);
#95=IFCPROPERTYSET('0000000000000000000095',#2,'Pset_ReinforcingBarCommon',$,(#94));
#96=IFCRELDEFINESBYPROPERTIES('0000000000000000000096',#2,$,$,(#10),#95);
ENDSEC;
END-ISO-10303-21;
";
        string arq = Path.Combine(Path.GetTempPath(), "teste-armadura-iso.ifc");
        File.WriteAllText(arq, ifc);
        StepModel m = StepModel.Load(arq);
        var barras = new Dictionary<long, List<BarraRevit>>
        {
            [100] = new() { new() { Comprimento = 2.0, Peso = 1.2 }, new() { Comprimento = 3.0, Peso = 1.8 } },
            [200] = new() { new() { Comprimento = 31.4159, Peso = 12.4 } },
            [300] = new() { new() { Comprimento = 2.0, Peso = 1.2 } },
        };
        var dados = new Dictionary<long, ArmaduraDadosRevit>
        {
            [100] = new() { Diametro = 0.01, Nervurada = true, Forma = "Rebar Shape 25" },
            // Revit: anel horizontal; o diâmetro no Revit aponta em X, no IFC (placement girado 90°) também sai em X local.
            [200] = new() { Diametro = 0.016, Nervurada = true, Forma = "21", NormalAnel = new[] { 0.0, 0, 1 }, DiametroAnel = new[] { 0.0, -1, 0 } },
            [300] = new() { Diametro = 0.01, Nervurada = true, Estribo = true, Forma = "Rebar Shape 1" },
        };
        ArmaduraIfc.Aplicar(m, id => barras.TryGetValue(id, out var b) ? b : null);
        ArmaduraIsoIfc.Resultado r = ArmaduraIsoIfc.Aplicar(m, id => dados.TryGetValue(id, out var d) ? d : null,
            id => barras.TryGetValue(id, out var b) ? b : null);
        m.Save(arq);
        m = StepModel.Load(arq);
        foreach (string l in r.Linhas()) Console.WriteLine("        " + l);

        StepEntity b10 = m.Get(10);
        Confere(b10.Args[8] == "'CA-50'" && b10.Args[9] == "0.01" && b10.Args[10] == "7.9E-05".Replace("7.9E-05", b10.Args[10]) &&
                Math.Abs(StepModel.Real(b10.Args[10]) - Math.PI * 0.0001 / 4) < 1e-8 && b10.Args[11] == "2." && b10.Args[12] == ".MAIN." && b10.Args[13] == ".TEXTURED.",
            "atributos da barra variável 1: " + string.Join(",", b10.Args.Skip(8)));
        Confere(m.Get(11).Args[11] == "3.", "BarLength da barra variável 2: " + m.Get(11).Args[11]);
        Confere(m.Get(30).Args[12] == ".LIGATURE.", "estribo = LIGATURE: " + m.Get(30).Args[12]);
        Confere(m.Get(20).Args[12] == ".RING.", "anel = RING: " + m.Get(20).Args[12]);

        var q10 = Props(m, 10, "MetroQto");
        var q11 = Props(m, 11, "MetroQto");
        Confere(q10["Quantidade"] == "IFCINTEGER(1)" && q10["PesoDoConjunto"] == "IFCMASSMEASURE(1.2)" && q10["PesoLinear"] == "IFCMASSPERLENGTHMEASURE(0.617)" && q10["Comprimento"] == "IFCLENGTHMEASURE(2.)",
            "MetroQto barra 1: " + string.Join(" ", q10.Select(k => k.Key + "=" + k.Value)));
        Confere(q11["Quantidade"] == "IFCINTEGER(1)" && q11["PesoDoConjunto"] == "IFCMASSMEASURE(1.8)" && q11["Comprimento"] == "IFCLENGTHMEASURE(3.)",
            "MetroQto barra 2 (pset compartilhado separado): " + string.Join(" ", q11.Select(k => k.Key + "=" + k.Value)));

        var p10 = Props(m, 10, "Pset_ReinforcingBarCommon");
        Confere(p10.ContainsKey("Reference") && p10["NominalDiameter"] == "IFCPOSITIVELENGTHMEASURE(0.01)" && p10["BarLength"] == "IFCPOSITIVELENGTHMEASURE(2.)" &&
                p10["SteelGrade"] == "IFCLABEL('CA-50')" && p10["BarSurface"] == "(IFCLABEL('TEXTURED'))" && p10["BarCount"] == "IFCCOUNTMEASURE(1)" && p10.ContainsKey("CrossSectionArea"),
            "Pset_ReinforcingBarCommon: " + string.Join(" ", p10.Select(k => k.Key + "=" + k.Value)));
        var iso = Props(m, 20, ArmaduraIsoIfc.PsetIso);
        Confere(iso.TryGetValue("BarShapeCode", out string cod) && cod == "IFCLABEL('21')", "código ISO 3766 do anel (forma \"21\"): " + cod);
        Confere(Props(m, 10, ArmaduraIsoIfc.PsetIso).Count == 0, "forma \"Rebar Shape 25\" fica sem código ISO");

        StepEntity tipo = m.Get(70);
        Confere(tipo.Args[10] == "0.01" && tipo.Args[13] == ".TEXTURED." && tipo.Args[11] != "$", "tipo: " + string.Join(",", tipo.Args.Skip(9)));

        StepEntity mat = m.Get(80);
        Confere(StepModel.Text(mat.Args[0]) == "Rebar - ASTM A615M - Grade 420",
            "material do Revit mantido: " + StepModel.Text(mat.Args[0]));

        // Anel: dois arcos de 3 pontos, no plano horizontal (z = 0), raio 5.
        StepEntity curva = m.Get(61);
        StepEntity lista = m.Get(60);
        List<List<double>> pts = StepModel.SplitArgs(lista.Args[0].Substring(1, lista.Args[0].Length - 2)).Select(StepModel.RealList).ToList();
        bool plano = pts.All(p => Math.Abs(p[2]) < 1e-9) && pts.All(p => Math.Abs(Math.Sqrt(p[0] * p[0] + p[1] * p[1]) - 5) < 1e-9);
        Confere(curva.Args[1] == "(IFCARCINDEX((1,3,2)),IFCARCINDEX((2,4,1)))" && pts.Count == 4 && plano,
            "anel: " + curva.Args[1] + " " + lista.Args[0]);
        Confere(r.Aneis == 1 && r.AneisEstimados == 0, $"anéis: {r.Aneis} refeitos, {r.AneisEstimados} estimados");

        // Rodar de novo não muda nada (o anel já tem 3 pontos e os valores já estão lá).
        ArmaduraIsoIfc.Resultado r2 = ArmaduraIsoIfc.Aplicar(m, id => dados.TryGetValue(id, out var d) ? d : null,
            id => barras.TryGetValue(id, out var b) ? b : null);
        Confere(!r2.Alterou, $"segunda passada não altera: atributos {r2.Atributos}, psets {r2.Psets}, MetroQto {r2.MetroQtoPorBarra}, anéis {r2.Aneis}, material {r2.MaterialDivergente}");
        File.Delete(arq);
        return _falhas;
    }

    /// <summary>
    /// Roda num IFC real, sem Revit: as barras de cada conjunto saem do Qto que já está no arquivo e o diâmetro do raio
    /// do sólido. Grava o resultado em saida (ou só mostra o resumo).
    /// </summary>
    public static int ArquivoReal(string arquivo, string saida)
    {
        var relogio = System.Diagnostics.Stopwatch.StartNew();
        StepModel m = StepModel.Load(arquivo);
        Console.WriteLine($"Carregado em {relogio.Elapsed.TotalSeconds:0.0} s");
        var porTag = new Dictionary<long, List<(int Id, int Count, double L, double W)>>();
        var qtoDe = new Dictionary<int, Dictionary<string, double>>();
        foreach (StepEntity rel in m.OfType("IFCRELDEFINESBYPROPERTIES"))
        {
            StepEntity q = m.Get(StepModel.Ref(rel.Args[5]));
            if (q == null || q.Type != "IFCELEMENTQUANTITY" || StepModel.Text(q.Args[2]) != ArmaduraIfc.Qto) continue;
            var v = new Dictionary<string, double>();
            foreach (int x in StepModel.RefList(q.Args[5]))
            {
                StepEntity e = m.Get(x);
                v[StepModel.Text(e.Args[0])] = StepModel.Real(e.Args[3]);
            }
            foreach (int o in StepModel.RefList(rel.Args[4])) qtoDe[o] = v;
        }
        foreach (StepEntity b in m.OfType("IFCREINFORCINGBAR"))
        {
            if (!long.TryParse(StepModel.Text(b.Args[7]), out long tag) || !qtoDe.TryGetValue(b.Id, out var v)) continue;
            if (!porTag.TryGetValue(tag, out var l)) porTag[tag] = l = new();
            l.Add((b.Id, (int)v.GetValueOrDefault("Count"), v.GetValueOrDefault("Length"), v.GetValueOrDefault("Weight")));
        }
        IReadOnlyList<BarraRevit> Barras(long tag)
        {
            if (!porTag.TryGetValue(tag, out var l)) return null;
            if (l.Count > 1) return l.OrderBy(x => x.Id).Select(x => new BarraRevit { Comprimento = x.L, Peso = x.W }).ToList();
            var u = l[0];
            return Enumerable.Range(0, Math.Max(1, u.Count)).Select(_ => new BarraRevit { Comprimento = u.L / Math.Max(1, u.Count), Peso = u.W / Math.Max(1, u.Count) }).ToList();
        }
        relogio.Restart();
        ArmaduraIsoIfc.Resultado r = ArmaduraIsoIfc.Aplicar(m, _ => null, Barras);
        Console.WriteLine($"Aplicado em {relogio.Elapsed.TotalSeconds:0.0} s");
        foreach (string l in r.Linhas()) Console.WriteLine("  " + l);
        if (saida != null) { m.Save(saida); Console.WriteLine("Gravado: " + saida); }
        return 0;
    }
}
