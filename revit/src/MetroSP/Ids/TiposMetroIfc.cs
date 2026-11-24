using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MetroSP.Ifc
{
    /// <summary>
    /// Tipo de dado das propriedades dos Psets do Metrô (Metro, MetroQto...) que o exportador grava como número sem
    /// unidade (IfcReal), porque o arquivo de Psets do Revit não tem tipo de massa: Peso em kg (IfcMassMeasure),
    /// MassaLinear em kg/m (IfcMassPerLengthMeasure), MassaPorArea em kg/m² (IfcAreaDensityMeasure), densidade em kg/m³
    /// (IfcMassDensityMeasure) e ModuloElasticidade em Pa (IfcModulusOfElasticityMeasure; valor abaixo de 100 000 é lido
    /// em GPa). Só muda o tipo do que saiu sem unidade: o que já tem tipo fica como está.
    /// </summary>
    public static class TiposMetroIfc
    {
        private static readonly Dictionary<string, string> Tipos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Peso"] = "IFCMASSMEASURE",
            ["PesoTotal"] = "IFCMASSMEASURE",
            ["PesoDoConjunto"] = "IFCMASSMEASURE",
            ["PesoNominal"] = "IFCMASSMEASURE",
            ["Massa"] = "IFCMASSMEASURE",
            ["MassaLinear"] = "IFCMASSPERLENGTHMEASURE",
            ["PesoLinear"] = "IFCMASSPERLENGTHMEASURE",
            ["PesoPorMetro"] = "IFCMASSPERLENGTHMEASURE",
            ["MassaPorArea"] = "IFCAREADENSITYMEASURE",
            ["Densidade"] = "IFCMASSDENSITYMEASURE",
            ["MassaEspecifica"] = "IFCMASSDENSITYMEASURE",
            ["ModuloElasticidade"] = "IFCMODULUSOFELASTICITYMEASURE",
        };

        private static readonly HashSet<string> SemUnidade = new HashSet<string>(StringComparer.Ordinal)
        {
            "IFCREAL", "IFCNUMERICMEASURE", "IFCPOSITIVERATIOMEASURE", "IFCRATIOMEASURE",
        };

        /// <summary>Devolve quantas propriedades mudaram de tipo.</summary>
        public static int Aplicar(StepModel m)
        {
            if (m.Schema != null && m.Schema.StartsWith("IFC2X3", StringComparison.OrdinalIgnoreCase)) return 0;
            var props = new HashSet<int>();
            foreach (StepEntity ps in m.OfType("IFCPROPERTYSET"))
            {
                string nome = StepModel.Text(ps.Args[2]);
                if (nome == null || !nome.StartsWith("Metro", StringComparison.OrdinalIgnoreCase)) continue;
                props.UnionWith(StepModel.RefList(ps.Args[4]));
            }
            int n = 0;
            foreach (int id in props.OrderBy(i => i))
            {
                StepEntity p = m.Get(id);
                if (p == null || p.Type != "IFCPROPERTYSINGLEVALUE" || p.Args.Count < 3) continue;
                string nome = StepModel.Text(p.Args[0]);
                if (nome == null || !Tipos.TryGetValue(nome, out string tipo)) continue;
                string valor = p.Args[2];
                int a = valor.IndexOf('('), b = valor.LastIndexOf(')');
                if (a <= 0 || b <= a || !SemUnidade.Contains(valor.Substring(0, a).Trim())) continue;
                if (!double.TryParse(valor.Substring(a + 1, b - a - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) continue;
                if (tipo == "IFCMODULUSOFELASTICITYMEASURE" && v > 0 && v < 1e5) v *= 1e9;
                m.SetArg(id, 2, tipo + "(" + Numero(v) + ")");
                n++;
            }
            return n;
        }

        private static string Numero(double v)
        {
            string s = v.ToString("R", CultureInfo.InvariantCulture);
            if (s.IndexOfAny(new[] { '.', 'E', 'e' }) < 0) s += ".";
            return s;
        }
    }
}
