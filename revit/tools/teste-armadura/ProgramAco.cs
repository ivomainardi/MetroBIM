using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MetroSP.Ifc;

/// <summary>
/// Teste das ligações metálicas num IFC já exportado pela MetroSP: refaz as peças a partir do Pset MetroSP_LigacaoMetalica
/// antigo (como se viessem do Revit), apaga esse Pset e roda FixadoresIfc de novo.
/// Rodar: dotnet run -c Release -- aco entrada.ifc saida.ifc
/// </summary>
public static class TesteAco
{
    public static int ArquivoReal(string entrada, string saida)
    {
        StepModel m = StepModel.Load(entrada);
        var pecas = new Dictionary<long, PecaAco>();
        long proximo = 900000000;
        var psetDe = new Dictionary<int, int>();
        foreach (StepEntity rel in m.OfType("IFCRELDEFINESBYPROPERTIES").ToList())
        {
            int ps = StepModel.Ref(rel.Args[5]);
            if (m.TypeOf(ps) != "IFCPROPERTYSET") continue;
            string nome = StepModel.Text(m.Get(ps).Args[2]);
            if (nome != FixadoresIfc.Pset && nome != "Pset_MechanicalFastenerBolt") continue;
            if (nome == FixadoresIfc.Pset) foreach (int o in StepModel.RefList(rel.Args[4])) psetDe[o] = ps;
            m.Remove(rel.Id);
        }
        foreach (var kv in psetDe)
        {
            StepEntity e = m.Get(kv.Key);
            var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (int pid in StepModel.RefList(m.Get(kv.Value).Args[4]))
            {
                StepEntity p = m.Get(pid);
                string v = p.Args[2];
                int a = v.IndexOf('('), b = v.LastIndexOf(')');
                if (a > 0) v = v.Substring(a + 1, b - a - 1);
                props[StepModel.Text(p.Args[0])] = v.StartsWith("'") ? StepModel.Text(v) : v;
            }
            int extrusoes = StepModel.RefList(m.Get(StepModel.RefList(m.Get(StepModel.Ref(e.Args[6])).Args[2])[0]).Args[3]).Count;
            double vol = 0;
            var peca = new PecaAco
            {
                HostId = long.Parse(StepModel.Text(e.Args[7])),
                Chave = "t" + kv.Key,
                Peca = props.GetValueOrDefault("Peca"),
                Ligacao = props.GetValueOrDefault("Ligacao"),
                Norma = props.GetValueOrDefault("Norma"),
                Classe = props.GetValueOrDefault("Classe"),
                EhParafuso = e.Type == "IFCMECHANICALFASTENER",
                Nome = StepModel.Text(e.Args[2]),
                Propriedades = props,
                PecasNoItem = e.Type == "IFCMECHANICALFASTENER" ? 2 : extrusoes,
                Volume = vol,
                Uc = "UC-TESTE",
                Galvanizado = true,
            };
            if (double.TryParse(props.GetValueOrDefault("Diameter"), NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) peca.Diametro = d / 1000;
            if (e.Args.Count > 9 && e.Args[9] != "$") peca.Comprimento = StepModel.Real(e.Args[9]) / 1000;
            long tag = proximo++;
            m.SetArg(e.Id, 7, "'" + tag + "'");
            pecas[tag] = peca;
        }
        // Volume de uma cantoneira (GrossVolume do Qto) para os perfis com mais de uma.
        foreach (StepEntity rel in m.OfType("IFCRELDEFINESBYPROPERTIES"))
        {
            int q = StepModel.Ref(rel.Args[5]);
            if (m.TypeOf(q) != "IFCELEMENTQUANTITY") continue;
            foreach (int o in StepModel.RefList(rel.Args[4]))
            {
                if (!long.TryParse(StepModel.Text(m.Get(o).Args[7]) ?? "", out long tag) || !pecas.TryGetValue(tag, out var p)) continue;
                foreach (int qi in StepModel.RefList(m.Get(q).Args[5]))
                    if (StepModel.Text(m.Get(qi).Args[0]) == "GrossVolume") p.Volume = StepModel.Real(m.Get(qi).Args[3]);
            }
        }
        Console.WriteLine($"{pecas.Count} peças refeitas");
        FixadoresIfc.Aplicar(m, pecas, Console.WriteLine);
        m.Save(saida);
        return 0;
    }
}
