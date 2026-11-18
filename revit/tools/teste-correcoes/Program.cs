using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using MetroSP.Ifc;

// IFC de exemplo como o exportador do Revit grava, com uma falha de cada tipo:
//   Tag 1: proxy com "Exportar para IFC como" = IfcCovering.CLADDING (e o tipo IfcBuildingElementProxyType só dele);
//   Tag 2: rampa (IfcRamp) com um IfcSlab que é lance e outro que é patamar;
//   Tag 3: lance de escada (IfcStairFlight) sem Qto e com MetroQto sem Volume;
//   Tag 4: parede com Qto sem NetVolume e com Length zerado;
//   Tag 5: tela soldada (IfcReinforcingMesh) sem dimensões;
//   Tag 6: tubo fora de sistema; Tag 7: tubo já num sistema;
//   Tag 8: viga sem material.
int falhas = 0;
void Confere(bool ok, string o)
{
    Console.WriteLine((ok ? "  ok    " : "  FALHA ") + o);
    if (!ok) falhas++;
}

FonteRevit Fonte() => new FonteRevit
{
    ClassesPedidas = new Dictionary<long, string> { [1] = "IfcCovering.CLADDING" },
    TipoValido = (c, t) => c == "IfcCovering" && (t == "CLADDING" || t == "USERDEFINED"),
    Quantidades = (id, precisa, conferir) => id switch
    {
        3 => new QuantidadesRevit { Volume = 1.8, Comprimento = 3.6, Largura = 1.2, Altura = 2.0 },
        4 => new QuantidadesRevit { Volume = 2.4, Comprimento = 4.0, Espessura = 0.2, Area = 12.0, Altura = 3.0, Largura = 0.2 },
        2 => new QuantidadesRevit { Volume = 5.0, Comprimento = 8.0, Largura = 1.5, Area = 12.0 },
        8 => new QuantidadesRevit { Volume = 0.3, Comprimento = 5.0 },
        1 => new QuantidadesRevit { Area = 6.0, Espessura = 0.02 },
        _ => null,
    },
    Tela = id => id == 5 ? new TelaRevit { Comprimento = 6, Largura = 2.45, DiametroLongitudinal = 0.008, DiametroTransversal = 0.006,
                                           EspacamentoLongitudinal = 0.15, EspacamentoTransversal = 0.15, Peso = 45.2 } : null,
    Sistemas = id => id == 6 ? new List<SistemaRevit> { new() { Id = 900, Nome = "AF 1", Tipo = "Água fria", Predefinido = "DOMESTICCOLDWATER" } } : null,
    Materiais = id => id == 8 ? new List<MaterialRevit> { new() { Nome = "Concreto C40", Fracao = 0.9 }, new() { Nome = "Aço CA-50", Fracao = 0.1 } } : null,
    MetroQto = new Dictionary<string, IReadOnlyList<string>> { ["IFCSTAIRFLIGHT"] = new[] { "Volume", "Largura", "Comprimento" } },
};

string Ifc(string schema) => $@"ISO-10303-21;
HEADER;FILE_DESCRIPTION(('ViewDefinition [ReferenceView]'),'2;1');FILE_NAME('t.ifc','2026-01-01T00:00:00',(''),(''),'','','');FILE_SCHEMA(('{schema}'));ENDSEC;
DATA;
#1=IFCPERSON($,$,'',$,$,$,$,$);
#3=IFCORGANIZATION($,'x',$,$,$);
#4=IFCPERSONANDORGANIZATION(#1,#3,$);
#5=IFCAPPLICATION(#3,'1','Revit','Revit');
#2=IFCOWNERHISTORY(#4,#5,$,.NOCHANGE.,$,$,$,0);
#6=IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.);
#7=IFCSIUNIT(*,.AREAUNIT.,$,.SQUARE_METRE.);
#8=IFCSIUNIT(*,.VOLUMEUNIT.,$,.CUBIC_METRE.);
#9=IFCSIUNIT(*,.MASSUNIT.,.KILO.,.GRAM.);
#10=IFCUNITASSIGNMENT((#6,#7,#8,#9));
#11=IFCPROJECT('0Proj00000000000000000',#2,'P',$,$,$,$,$,#10);
#12=IFCBUILDING('0Bldg00000000000000000',#2,'B',$,$,$,$,$,.ELEMENT.,$,$,$);
#13=IFCRELAGGREGATES('0Agg100000000000000000',#2,$,$,#11,(#12));
#20=IFCBUILDINGELEMENTPROXY('0Prox00000000000000000',#2,'Sanca',$,$,$,$,'1',.NOTDEFINED.);
#21=IFCBUILDINGELEMENTPROXYTYPE('0ProT00000000000000000',#2,'Sanca',$,$,$,$,'101',$,.NOTDEFINED.);
#22=IFCRELDEFINESBYTYPE('0RTyp00000000000000000',#2,$,$,(#20),#21);
#30=IFCRAMP('0Ramp00000000000000000',#2,'Rampa',$,$,$,$,'2',.NOTDEFINED.);
#31=IFCSLAB('0Sla100000000000000000',#2,'Rampa',$,$,$,$,'2',.NOTDEFINED.);
#32=IFCSLAB('0Sla200000000000000000',#2,'Rampa',$,$,$,$,'2',.LANDING.);
#33=IFCRELAGGREGATES('0Agg200000000000000000',#2,$,$,#30,(#31,#32));
#40=IFCSTAIRFLIGHT('0Flt100000000000000000',#2,'Lance',$,$,$,$,'3',12,11,0.175,0.28,.STRAIGHT.);
#41=IFCPROPERTYSINGLEVALUE('Altura',$,IFCLENGTHMEASURE(2000.),$);
#42=IFCPROPERTYSET('0MQto00000000000000000',#2,'MetroQto',$,(#41));
#43=IFCRELDEFINESBYPROPERTIES('0RMQt00000000000000000',#2,$,$,(#40),#42);
#50=IFCWALL('0Wall00000000000000000',#2,'Parede',$,$,$,$,'4',.STANDARD.);
#51=IFCQUANTITYLENGTH('Length',$,$,0.,$);
#52=IFCQUANTITYLENGTH('Width',$,$,200.,$);
#53=IFCELEMENTQUANTITY('0WQto00000000000000000',#2,'Qto_WallBaseQuantities',$,$,(#51,#52));
#54=IFCRELDEFINESBYPROPERTIES('0RWQt00000000000000000',#2,$,$,(#50),#53);
#60=IFCREINFORCINGMESH('0Mesh00000000000000000',#2,'Tela',$,$,$,$,'5',$,$,$,$,$,$,$,$,$,.NOTDEFINED.);
#70=IFCPIPESEGMENT('0Pip100000000000000000',#2,'Tubo',$,$,$,$,'6',.RIGIDSEGMENT.);
#71=IFCPIPESEGMENT('0Pip200000000000000000',#2,'Tubo',$,$,$,$,'7',.RIGIDSEGMENT.);
#72=IFCDISTRIBUTIONSYSTEM('0Sist00000000000000000',#2,'AF 2',$,$,$,.DOMESTICCOLDWATER.);
#73=IFCRELASSIGNSTOGROUP('0Grp100000000000000000',#2,$,$,(#71),$,#72);
#80=IFCBEAM('0Beam00000000000000000',#2,'Viga',$,$,$,#81,'8',.BEAM.);
#81=IFCPRODUCTDEFINITIONSHAPE($,$,());
ENDSEC;
END-ISO-10303-21;
";

if (args.Length > 0 && args[0] == "grande") { Grande(); return 0; }
if (args.Length > 1 && args[0] == "arquivo")
{
    // Tempo num IFC real (sem Revit: as consultas devolvem vazio).
    var sw = Stopwatch.StartNew();
    var mr = StepModel.Load(args[1]);
    var tl = sw.Elapsed; sw.Restart();
    var rr = CorrecoesIfc.Aplicar(mr, new FonteRevit { Quantidades = (i, p, c) => null, Sistemas = i => null, Materiais = i => null, Tela = i => null });
    Console.WriteLine($"{new FileInfo(args[1]).Length / 1e6:0.0} MB · leitura {tl.TotalSeconds:0.00} s · correções {sw.Elapsed.TotalSeconds:0.00} s");
    Console.WriteLine("  " + string.Join(" · ", rr.Tempos.Select(x => $"{x.Etapa} {x.Tempo.TotalSeconds:0.00} s")));
    return 0;
}
string saida = args.Length > 0 ? args[0] : null;

foreach (string schema in new[] { "IFC4", "IFC4X3_ADD2" })
{
    Console.WriteLine(schema);
    string arq = Path.Combine(Path.GetTempPath(), "teste-correcoes-" + schema + ".ifc");
    File.WriteAllText(arq, Ifc(schema));
    Confere(CorrecoesIfc.TagsDeProxies(arq).SetEquals(new long[] { 1 }), "proxies lidos sem carregar o modelo (Tag 1)");
    var m = StepModel.Load(arq);
    var r = CorrecoesIfc.Aplicar(m, Fonte());
    foreach (string l in r.Linhas()) Console.WriteLine("        " + l);
    m.Save(arq);
    if (saida != null) File.Copy(arq, Path.Combine(saida, "corrigido-" + schema + ".ifc"), true);
    var t = StepModel.Load(arq);
    string Txt(int id) => t.Get(id) is StepEntity e ? e.Type + "(" + string.Join(",", e.Args) + ")" : "";

    Confere(t.TypeOf(20) == "IFCCOVERING" && Txt(20).EndsWith(".CLADDING.)"), "proxy virou IfcCovering.CLADDING");
    Confere(t.TypeOf(21) == "IFCCOVERINGTYPE" && Txt(21).EndsWith(".CLADDING.)"), "tipo do proxy virou IfcCoveringType");
    Confere(t.TypeOf(31) == "IFCRAMPFLIGHT", "lance da rampa: IfcSlab → IfcRampFlight");
    Confere(t.TypeOf(32) == "IFCSLAB", "patamar da rampa continua IfcSlab");
    Confere(t.OfType("IFCELEMENTQUANTITY").Any(q => q.Args[2] == "'Qto_StairFlightBaseQuantities'"), "lance de escada ganhou Qto_StairFlightBaseQuantities");
    var volLance = t.OfType("IFCQUANTITYVOLUME").FirstOrDefault(q => q.Args[0] == "'NetVolume'" && q.Args[3] == "1.8");
    Confere(volLance != null, "volume do lance de escada = 1,8 m³");
    var wq = t.OfType("IFCELEMENTQUANTITY").First(q => q.Args[2] == "'Qto_WallBaseQuantities'");
    var nomes = StepModel.RefList(wq.Args[5]).Select(i => t.Get(i)).ToDictionary(e => StepModel.Text(e.Args[0]), e => e.Args[3]);
    Confere(nomes["Length"] == "4000.", "parede: Length zerado → 4000 mm (unidade do arquivo)");
    Confere(nomes["Width"] == "200.", "parede: Width do exportador fica");
    Confere(nomes.TryGetValue("NetVolume", out var nv) && nv == "2.4", "parede: NetVolume que faltava = 2,4 m³");
    Confere(wq.Id == 53, "parede: Qto só dela é completado no lugar (sem cópia)");
    var props = StepModel.RefList(t.Get(42).Args[4]).Select(i => t.Get(i)).ToDictionary(e => StepModel.Text(e.Args[0]), e => e.Args[2]);
    Confere(props.TryGetValue("Volume", out var pv) && pv == "IFCVOLUMEMEASURE(1.8)", "MetroQto.Volume do lance = 1,8");
    Confere(props.TryGetValue("Comprimento", out var pc) && pc == "IFCLENGTHMEASURE(3600.)", "MetroQto.Comprimento do lance = 3600 mm");
    Confere(props["Altura"] == "IFCLENGTHMEASURE(2000.)", "MetroQto.Altura do exportador fica");
    var mesh = t.Get(60);
    Confere(mesh.Args[9] == "6000." && mesh.Args[11] == "8." && mesh.Args[15] == "150.", "tela: comprimento, diâmetro e espaçamento em mm");
    Confere(Math.Abs(StepModel.Real(mesh.Args[13]) - Math.PI * 0.008 * 0.008 / 4) < 1e-9, "tela: área da seção em m²");
    Confere(t.OfType("IFCQUANTITYWEIGHT").Any(q => q.Args[3] == "45.2"), "tela: Weight 45,2 kg");
    var grupos = t.OfType("IFCRELASSIGNSTOGROUP").ToList();
    Confere(grupos.Count == 2 && grupos.Any(g => g.Args[4] == "(#70)" && t.TypeOf(StepModel.Ref(g.Args[6])) == "IFCDISTRIBUTIONSYSTEM"), "tubo fora de sistema entrou no sistema AF 1");
    Confere(t.OfType("IFCDISTRIBUTIONSYSTEM").Any(s => s.Args[2] == "'AF 1'" && s.Args[6] == ".DOMESTICCOLDWATER."), "sistema novo com o tipo DOMESTICCOLDWATER");
    Confere(t.OfType("IFCRELSERVICESBUILDINGS").Count() == 1, "sistema novo ligado ao IfcBuilding");
    var mat = t.OfType("IFCRELASSOCIATESMATERIAL").SingleOrDefault();
    Confere(mat != null && mat.Args[4] == "(#80)" && t.TypeOf(StepModel.Ref(mat.Args[5])) == "IFCMATERIALCONSTITUENTSET", "viga sem material ganhou os dois materiais");
    Confere(t.OfType("IFCMATERIALCONSTITUENT").Count() == 2, "dois IfcMaterialConstituent");
    Confere(r.Classes == 1 && r.Lances == 1 && r.Telas == 1 && r.NoSistema == 1 && r.Materiais == 1, "contagens do resumo");

    // De novo no arquivo já corrigido: nada muda.
    var r2 = CorrecoesIfc.Aplicar(t, Fonte());
    Confere(!r2.Alterou, "segunda passada não muda nada" + (r2.Alterou ? ": " + string.Join(" | ", r2.Linhas()) : ""));
}
Console.WriteLine(falhas == 0 ? "Tudo certo." : $"{falhas} falha(s).");
return falhas == 0 ? 0 : 1;

// IFC sintético com n elementos de cada tipo, para medir o custo das correções perto do da leitura do arquivo.
void Grande()
{
    const int n = 60000;
    var sb = new StringBuilder(Ifc("IFC4").Replace("ENDSEC;\nEND-ISO-10303-21;\n", ""));
    int id = 1000;
    string[] coisas = { "IFCWALL", "IFCSLAB", "IFCBEAM", "IFCCOLUMN", "IFCPIPESEGMENT", "IFCCOVERING" };
    for (int i = 0; i < n; i++)
    {
        string tipo = coisas[i % coisas.Length];
        int e = id++, ql = id++, qv = id++, q = id++, rel = id++, pv = id++, ps = id++, rp = id++, rm = id++;
        sb.Append($"#{e}={tipo}('{i:D22}',#2,'E',$,$,$,#81,'{100000 + i}',.NOTDEFINED.);\n");
        sb.Append($"#{ql}=IFCQUANTITYLENGTH('Length',$,$,1000.,$);\n#{qv}=IFCQUANTITYVOLUME('NetVolume',$,$,1.,$);\n");
        string qn = "Qto_" + tipo.Substring(3, 1) + tipo.Substring(4).ToLowerInvariant().Replace("segment", "Segment") + "BaseQuantities";
        sb.Append($"#{q}=IFCELEMENTQUANTITY('{i:D21}Q',#2,'{qn}',$,$,(#{ql},#{qv}));\n#{rel}=IFCRELDEFINESBYPROPERTIES('{i:D21}R',#2,$,$,(#{e}),#{q});\n");
        sb.Append($"#{pv}=IFCPROPERTYSINGLEVALUE('A',$,IFCLABEL('x'),$);\n#{ps}=IFCPROPERTYSET('{i:D21}P',#2,'Pset_X',$,(#{pv}));\n");
        sb.Append($"#{rp}=IFCRELDEFINESBYPROPERTIES('{i:D21}S',#2,$,$,(#{e}),#{ps});\n");
        sb.Append($"#{rm}=IFCRELASSOCIATESMATERIAL('{i:D21}M',#2,$,$,(#{e}),#82);\n");
    }
    sb.Append("#82=IFCMATERIAL('Concreto',$,$);\nENDSEC;\nEND-ISO-10303-21;\n");
    string arq = Path.Combine(Path.GetTempPath(), "teste-correcoes-grande.ifc");
    File.WriteAllText(arq, sb.ToString());
    Console.WriteLine($"IFC sintético: {new FileInfo(arq).Length / 1e6:0.0} MB, {n} elementos");
    var relogio = Stopwatch.StartNew();
    var m = StepModel.Load(arq);
    var tLoad = relogio.Elapsed;
    int chamadas = 0;
    var f = Fonte();
    var fq = f.Quantidades;
    f.Quantidades = (i, p, c) => { chamadas++; return new QuantidadesRevit { Volume = 1, Comprimento = 1, Area = 1, Espessura = 0.1 }; };
    f.Sistemas = i => { chamadas++; return null; };
    f.Materiais = i => { chamadas++; return null; };
    relogio.Restart();
    var r = CorrecoesIfc.Aplicar(m, f);
    var tCor = relogio.Elapsed;
    relogio.Restart();
    m.Save(arq);
    var tSave = relogio.Elapsed;
    Console.WriteLine($"Leitura {tLoad.TotalSeconds:0.00} s · correções {tCor.TotalSeconds:0.00} s · gravação {tSave.TotalSeconds:0.00} s · {chamadas} consultas ao Revit");
    Console.WriteLine("  " + string.Join(" · ", r.Tempos.Select(x => $"{x.Etapa} {x.Tempo.TotalSeconds:0.00} s")));
    foreach (string l in r.Linhas()) Console.WriteLine("  " + l);
}
