using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MetroSP.Conferencia
{
    /// <summary>
    /// Planilha do MetroDATA exportada do Airtable (.xlsx). Lê a aba "Propriedades x Objetos": uma linha por propriedade
    /// e por objeto que a usa, com a disciplina, a classe IFC do objeto, o Pset e a propriedade IFC, o parâmetro do Revit,
    /// a classe da propriedade (1 a 4) e as fases (etapas) em que ela é pedida.
    /// A planilha muda com o tempo: o plugin lê o arquivo escolhido a cada conferência e não guarda nada dela.
    /// </summary>
    public class MetroData
    {
        public const string AbaPrincipal = "Propriedades x Objetos";
        public const string Todas = "Todas";

        public class Linha
        {
            public string Disciplina { get; set; }
            public string Objeto { get; set; }
            /// <summary>Classes IFC do objeto (base, sem o tipo pré-definido), vazia se a planilha não diz.</summary>
            public List<string> ClassesIfc { get; } = new List<string>();
            public string Propriedade { get; set; }
            public string Origem { get; set; }
            public string Mapeamento { get; set; }
            public string Pset { get; set; }
            public string IfcPropriedade { get; set; }
            public string TipoDado { get; set; }
            public string RevitParametro { get; set; }
            public string RevitShared { get; set; }
            public Guid? Guid { get; set; }
            /// <summary>Número da classe da propriedade (1 a 4; 0 = a planilha não diz).</summary>
            public int Classe { get; set; }
            /// <summary>Etapas em que a propriedade é pedida (vazio = a planilha não diz: vale para todas).</summary>
            public HashSet<string> Etapas { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            /// <summary>Pset.Propriedade, como sai no IFC.</summary>
            public string Campo => string.IsNullOrWhiteSpace(Pset) ? (IfcPropriedade ?? Propriedade) : $"{Pset}.{IfcPropriedade ?? Propriedade}";

            /// <summary>Parâmetro do Revit de onde o campo sai: o compartilhado, senão o nativo, senão o nome da propriedade.</summary>
            public string Parametro => !string.IsNullOrWhiteSpace(RevitShared) ? RevitShared
                                     : !string.IsNullOrWhiteSpace(RevitParametro) ? RevitParametro
                                     : Propriedade;

            /// <summary>
            /// O exportador calcula (Base Quantities, Classification, Material) ou o parâmetro é nativo do Revit, cujo nome muda
            /// com o idioma: sem o parâmetro no elemento, não vira pendência. Só os parâmetros do MetroShared precisam existir.
            /// </summary>
            public bool Opcional =>
                string.Equals(Mapeamento, "Exportador", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Origem, "MetroShared", StringComparison.OrdinalIgnoreCase);
        }

        public string Arquivo { get; private set; }
        public List<Linha> Linhas { get; } = new List<Linha>();

        public static MetroData Ler(string caminho)
        {
            Dictionary<string, List<List<string>>> abas = Dados.Xlsx.Ler(caminho);
            if (!abas.TryGetValue(AbaPrincipal, out List<List<string>> linhas) || linhas.Count == 0)
                throw new InvalidDataException($"A planilha não tem a aba \"{AbaPrincipal}\" (exportação do MetroDATA).");

            List<string> cab = linhas[0].Select(c => (c ?? "").Trim()).ToList();
            int Col(params string[] nomes)
            {
                foreach (string n in nomes)
                {
                    int i = cab.FindIndex(c => string.Equals(c, n, StringComparison.OrdinalIgnoreCase));
                    if (i >= 0) return i;
                }
                return -1;
            }
            int cBase = Col("Base"), cDisc = Col("Disciplina"), cObj = Col("Objeto"), cIfc = Col("Classe IFC do objeto"),
                cProp = Col("Propriedade"), cOrig = Col("Origem"), cMap = Col("Mapeamento"), cPset = Col("IFC_PropertySet"),
                cIfcProp = Col("IFC_Propriedade"), cTipo = Col("IFC_TipoDado"), cRvt = Col("Revit_Parametro"),
                cShared = Col("Revit_Shared"), cGuid = Col("GUID"), cClasse = Col("Classe"), cFases = Col("Fases");
            if (cObj < 0 || cProp < 0)
                throw new InvalidDataException($"A aba \"{AbaPrincipal}\" não tem as colunas Objeto e Propriedade.");

            var md = new MetroData { Arquivo = caminho };
            for (int i = 1; i < linhas.Count; i++)
            {
                List<string> l = linhas[i];
                string V(int c) => c >= 0 && c < l.Count && !string.IsNullOrWhiteSpace(l[c]) ? l[c].Trim() : null;
                string objeto = V(cObj), prop = V(cProp);
                if (objeto == null || prop == null) continue;
                var x = new Linha
                {
                    Disciplina = NomeDisciplina(V(cBase), V(cDisc)),
                    Objeto = objeto,
                    Propriedade = prop,
                    Origem = V(cOrig),
                    Mapeamento = V(cMap),
                    Pset = PrimeiroItem(V(cPset)),
                    IfcPropriedade = V(cIfcProp),
                    TipoDado = V(cTipo),
                    RevitParametro = V(cRvt),
                    RevitShared = PrimeiroItem(V(cShared)),
                    Classe = NumeroClasse(V(cClasse)),
                };
                string guid = PrimeiroItem(V(cGuid));
                if (guid != null && System.Guid.TryParse(guid, out Guid g)) x.Guid = g;
                foreach (string c in Itens(V(cIfc)))
                {
                    string b = ClasseBase(c);
                    if (b != null && !x.ClassesIfc.Contains(b, StringComparer.OrdinalIgnoreCase)) x.ClassesIfc.Add(b);
                }
                foreach (string f in Itens(V(cFases))) x.Etapas.Add(NormalizarEtapa(f));
                md.Linhas.Add(x);
            }
            if (md.Linhas.Count == 0) throw new InvalidDataException($"A aba \"{AbaPrincipal}\" está vazia.");
            return md;
        }

        // ------------------------------------------------------------------ listas para escolher

        public List<string> Disciplinas() =>
            Linhas.Select(l => l.Disciplina).Where(d => d != null).Distinct(StringComparer.OrdinalIgnoreCase)
                  .OrderBy(d => d, StringComparer.CurrentCultureIgnoreCase).ToList();

        /// <summary>Etapas na ordem natural: Básico e Executivo primeiro, depois os lotes.</summary>
        public List<string> Etapas() =>
            Linhas.SelectMany(l => l.Etapas).Distinct(StringComparer.OrdinalIgnoreCase)
                  .OrderBy(e => e.Equals("Básico", StringComparison.OrdinalIgnoreCase) ? 0 : e.Equals("Executivo", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
                  .ThenBy(e => e, StringComparer.CurrentCultureIgnoreCase).ToList();

        /// <summary>Nome de cada classe (o mais usado na planilha), por número.</summary>
        public SortedDictionary<int, string> Classes() => new SortedDictionary<int, string>(
            Linhas.Where(l => l.Classe > 0).GroupBy(l => l.Classe).ToDictionary(g => g.Key, g => "Classe " + g.Key));

        public List<string> Objetos(string disciplina) =>
            DaDisciplina(disciplina).Select(l => l.Objeto).Distinct(StringComparer.OrdinalIgnoreCase)
                                    .OrderBy(o => o, StringComparer.CurrentCultureIgnoreCase).ToList();

        // ------------------------------------------------------------------ filtros

        public IEnumerable<Linha> DaDisciplina(string disciplina) =>
            EhTodas(disciplina) ? Linhas : Linhas.Where(l => string.Equals(l.Disciplina, disciplina, StringComparison.OrdinalIgnoreCase));

        /// <summary>Propriedades pedidas para o objeto na etapa e até a classe escolhidas.</summary>
        public List<Linha> Exigidas(string disciplina, string objeto, string etapa, int classeAte) =>
            DaDisciplina(disciplina)
                .Where(l => string.Equals(l.Objeto, objeto, StringComparison.OrdinalIgnoreCase))
                .Where(l => EhTodas(etapa) || l.Etapas.Count == 0 || l.Etapas.Contains(NormalizarEtapa(etapa)))
                .Where(l => classeAte <= 0 || l.Classe == 0 || l.Classe <= classeAte)
                .ToList();

        /// <summary>Objetos da disciplina cuja classe IFC é a do elemento.</summary>
        public List<string> ObjetosDaClasse(string disciplina, string entidade)
        {
            string b = ClasseBase(entidade);
            if (b == null) return new List<string>();
            return DaDisciplina(disciplina).Where(l => l.ClassesIfc.Contains(b, StringComparer.OrdinalIgnoreCase))
                                           .Select(l => l.Objeto).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        // ------------------------------------------------------------------ textos da planilha

        public static bool EhTodas(string s) => string.IsNullOrWhiteSpace(s) || s.Equals(Todas, StringComparison.OrdinalIgnoreCase);

        /// <summary>"Arquitetura / Arquitetura" vira "Arquitetura"; os demais ficam "Base / Disciplina".</summary>
        public static string NomeDisciplina(string base_, string disciplina)
        {
            if (string.IsNullOrWhiteSpace(disciplina)) return base_;
            if (string.IsNullOrWhiteSpace(base_) || base_.Equals(disciplina, StringComparison.OrdinalIgnoreCase)) return disciplina;
            return $"{base_} / {disciplina}";
        }

        /// <summary>"4-Basico" e "Básico" são a mesma etapa; "6-Executivo" e "Executivo" também.</summary>
        public static string NormalizarEtapa(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;
            string t = Regex.Replace(s.Trim(), @"^\d+\s*-\s*", "");
            string sem = t.ToLowerInvariant().Replace("á", "a");
            if (sem == "basico") return "Básico";
            if (sem == "executivo") return "Executivo";
            return t;
        }

        /// <summary>" Classe 2 - Especificação", "Classe 2- Condicionante" → 2.</summary>
        public static int NumeroClasse(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            Match m = Regex.Match(s, @"(\d+)");
            return m.Success && int.TryParse(m.Groups[1].Value, out int n) ? n : 0;
        }

        /// <summary>"IfcCoveringType.FLOORING" → "IfcCovering"; "IfcCourse.PredefinedType.PAVEMENT" → "IfcCourse".</summary>
        public static string ClasseBase(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            string t = s.Trim().Split('.')[0].Trim();
            if (!t.StartsWith("Ifc", StringComparison.OrdinalIgnoreCase)) return null;
            if (t.EndsWith("Type", StringComparison.OrdinalIgnoreCase) && t.Length > 7) t = t.Substring(0, t.Length - 4);
            return t;
        }

        private static IEnumerable<string> Itens(string s) =>
            string.IsNullOrWhiteSpace(s) ? Enumerable.Empty<string>()
            : s.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0);

        private static string PrimeiroItem(string s) => Itens(s).FirstOrDefault();
    }
}
