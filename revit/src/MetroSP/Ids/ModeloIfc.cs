using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace MetroSP.Ids
{
    /// <summary>
    /// Retrato do modelo aberto para conferir property sets: a entidade IFC de cada elemento e, para cada
    /// parâmetro, em quantos elementos de uma entidade ele tem valor. Lê o modelo só na primeira consulta de
    /// cada combinação e guarda o resultado; nada é gravado.
    /// </summary>
    public class ModeloIfc
    {
        /// <summary>Entidades genéricas: um bloco com elas vale para qualquer elemento.</summary>
        public static readonly HashSet<string> Genericas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "IfcElement", "IfcProduct", "IfcObject", "IfcBuildingElement", "IfcBuiltElement", "IfcDistributionElement",
        };

        /// <summary>Quantos elementos a regra alcança e em quantos o parâmetro tem valor.</summary>
        public struct Contagem
        {
            public int Elementos;
            public int ComValor;
            /// <summary>Elementos em que o parâmetro nem existe (nem na instância nem no tipo).</summary>
            public int SemParametro;
            public bool Completo => Elementos > 0 && ComValor == Elementos;
        }

        private readonly Document _doc;
        private readonly Dictionary<string, List<Element>> _porEntidade = new Dictionary<string, List<Element>>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Element> _todos = new List<Element>();
        private readonly Dictionary<string, Contagem> _contagens = new Dictionary<string, Contagem>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Nomes de parâmetros presentes nos tipos e numa instância de cada tipo.</summary>
        public HashSet<string> Parametros { get; }

        public string Titulo => _doc.Title;

        public ModeloIfc(Document doc, HashSet<string> parametros = null)
        {
            _doc = doc;
            Parametros = parametros ?? NomesDosParametros(doc);
            foreach (Element e in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                Category c = e.Category;
                if (c == null || c.CategoryType != CategoryType.Model || e.ViewSpecific) continue;
                if (e is ElementType) continue;
                string ent = EntidadeDe(doc, e);
                // Sem entidade conhecida o elemento só entra nas regras de entidades genéricas.
                if (ent == null && !TemGeometria(e)) continue;
                _todos.Add(e);
                if (ent == null) continue;
                if (!_porEntidade.TryGetValue(ent, out List<Element> lista)) _porEntidade[ent] = lista = new List<Element>();
                lista.Add(e);
            }
        }

        /// <summary>Entidades encontradas no modelo, com a quantidade de elementos.</summary>
        public IEnumerable<(string Entidade, int Elementos)> Entidades =>
            _porEntidade.Select(p => (p.Key, p.Value.Count)).OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase);

        /// <summary>Elementos que uma regra com estas entidades alcança.</summary>
        public int Elementos(IEnumerable<string> entidades) => Alcance(entidades).Count;

        /// <summary>Contagem do parâmetro nos elementos das entidades (de tipo: lido no tipo de cada elemento).</summary>
        public Contagem Contar(IEnumerable<string> entidades, string parametro, bool deTipo) =>
            Contar(entidades, new[] { parametro }, deTipo);

        /// <summary>
        /// Como <see cref="Contar(IEnumerable{string}, string, bool)"/>, mas com várias origens para a mesma propriedade:
        /// o elemento conta como preenchido se qualquer uma tiver valor (é o que a exportação faz).
        /// </summary>
        public Contagem Contar(IEnumerable<string> entidades, IEnumerable<string> parametros, bool deTipo)
        {
            List<string> ents = (entidades ?? Enumerable.Empty<string>()).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            List<string> nomes = (parametros ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            string chave = string.Join(",", ents.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) + "|" + (deTipo ? "T" : "I") + "|" + string.Join("|", nomes);
            if (_contagens.TryGetValue(chave, out Contagem guardada)) return guardada;

            var c = new Contagem();
            if (nomes.Count > 0)
            {
                var tipos = new Dictionary<ElementId, Element>();
                foreach (Element e in Alcance(ents))
                {
                    c.Elementos++;
                    Element tipo = null;
                    ElementId idTipo = e.GetTypeId();
                    if (idTipo != null && idTipo != ElementId.InvalidElementId && !tipos.TryGetValue(idTipo, out tipo))
                        tipos[idTipo] = tipo = _doc.GetElement(idTipo);
                    bool existe = false, valor = false;
                    foreach (string nome in nomes)
                    {
                        Parameter p = deTipo ? (tipo != null ? Procurar(tipo, nome) : null)
                                             : Procurar(e, nome) ?? (tipo != null ? Procurar(tipo, nome) : null);
                        if (p == null) continue;
                        existe = true;
                        if (TemValor(p)) { valor = true; break; }
                    }
                    if (!existe) c.SemParametro++;
                    else if (valor) c.ComValor++;
                }
            }
            else c.Elementos = Alcance(ents).Count;
            _contagens[chave] = c;
            return c;
        }

        /// <summary>
        /// Parâmetros do modelo com nome parecido com o pedido (ou com o nome da propriedade), do mais parecido ao
        /// menos, com a contagem nos elementos das entidades. Só entram os que têm valor em algum elemento.
        /// </summary>
        public List<(string Parametro, Contagem Contagem)> Sugestoes(IEnumerable<string> entidades, bool deTipo, string parametro, string propriedade, int maximo = 4)
        {
            List<string> ents = (entidades ?? Enumerable.Empty<string>()).ToList();
            var alvos = new[] { parametro, propriedade }.Where(a => !string.IsNullOrWhiteSpace(a)).Select(Normalizar).Distinct().ToList();
            if (alvos.Count == 0) return new List<(string, Contagem)>();
            var candidatos = new List<(string Nome, int Nota)>();
            foreach (string nome in Parametros)
            {
                if (string.Equals(nome, parametro, StringComparison.OrdinalIgnoreCase)) continue;
                string n = Normalizar(nome);
                if (n.Length == 0) continue;
                int melhor = int.MaxValue;
                foreach (string a in alvos)
                {
                    int nota;
                    if (n == a) nota = 0;
                    else if (a.Length >= 3 && (n.Contains(a) || a.Contains(n) && n.Length >= 3)) nota = 1 + Math.Abs(n.Length - a.Length) / 4;
                    else
                    {
                        int d = Distancia(n, a);
                        nota = d <= Math.Max(2, a.Length / 3) ? 3 + d : int.MaxValue;
                    }
                    melhor = Math.Min(melhor, nota);
                }
                if (melhor != int.MaxValue) candidatos.Add((nome, melhor));
            }
            var r = new List<(string, Contagem)>();
            foreach (var (nome, _) in candidatos.OrderBy(x => x.Nota).ThenBy(x => x.Nome, StringComparer.CurrentCultureIgnoreCase).Take(25))
            {
                Contagem c = Contar(ents, nome, deTipo);
                if (c.ComValor > 0) r.Add((nome, c));
                if (r.Count >= maximo) break;
            }
            return r;
        }

        private List<Element> Alcance(IEnumerable<string> entidades)
        {
            List<string> ents = entidades?.ToList() ?? new List<string>();
            if (ents.Count == 0 || ents.Any(Genericas.Contains)) return _todos;
            if (ents.Count == 1) return _porEntidade.TryGetValue(ents[0], out List<Element> uma) ? uma : new List<Element>();
            var r = new List<Element>();
            foreach (string e in ents.Distinct(StringComparer.OrdinalIgnoreCase))
                if (_porEntidade.TryGetValue(e, out List<Element> lista)) r.AddRange(lista);
            return r;
        }

        private static bool TemGeometria(Element e)
        {
            try { return e.get_BoundingBox(null) != null; } catch { return false; }
        }

        /// <summary>Minúsculas, sem acento e só letras e números ("Nível_Base" → "nivelbase").</summary>
        private static string Normalizar(string s)
        {
            var sb = new StringBuilder();
            foreach (char ch in s.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            }
            return sb.ToString();
        }

        private static int Distancia(string a, string b)
        {
            if (Math.Abs(a.Length - b.Length) > 6) return 99;
            var d = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) d[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                int anterior = d[0];
                d[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int guardado = d[j];
                    d[j] = Math.Min(Math.Min(d[j] + 1, d[j - 1] + 1), anterior + (a[i - 1] == b[j - 1] ? 0 : 1));
                    anterior = guardado;
                }
            }
            return d[b.Length];
        }

        // ---------- ajudantes de leitura (usados também pelos parâmetros auxiliares do Revit 2027) ----------

        /// <summary>Nomes de parâmetros presentes nos tipos e numa instância de cada tipo.</summary>
        public static HashSet<string> NomesDosParametros(Document doc)
        {
            var nomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Coletar(Element e)
            {
                foreach (Parameter p in e.Parameters)
                {
                    string n = p.Definition?.Name;
                    if (!string.IsNullOrEmpty(n)) nomes.Add(n);
                }
            }
            foreach (Element t in new FilteredElementCollector(doc).WhereElementIsElementType()) Coletar(t);
            var tipos = new HashSet<ElementId>();
            foreach (Element e in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                if (e.Category == null) continue;
                ElementId tipo = e.GetTypeId();
                if (tipo == null || tipo == ElementId.InvalidElementId || tipos.Add(tipo)) Coletar(e);
            }
            if (doc.ProjectInformation != null) Coletar(doc.ProjectInformation);
            return nomes;
        }

        /// <summary>Com valor de verdade: referência vazia (material "Por categoria") e texto vazio não contam.</summary>
        public static bool TemValor(Parameter p)
        {
            if (!p.HasValue) return false;
            switch (p.StorageType)
            {
                case StorageType.ElementId: return p.AsElementId() != ElementId.InvalidElementId;
                case StorageType.String: return !string.IsNullOrWhiteSpace(p.AsString());
                case StorageType.None: return false;
                default: return true;
            }
        }

        /// <summary>Parâmetro pelo nome, ou "BuiltInParameter.X" como o exportador aceita.</summary>
        public static Parameter Procurar(Element e, string nome)
        {
            const string prefixo = "BuiltInParameter.";
            if (nome.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase) &&
                Enum.TryParse(nome.Substring(prefixo.Length), true, out BuiltInParameter bip))
                return e.get_Parameter(bip);
            return e.LookupParameter(nome);
        }

        /// <summary>
        /// Entidade IFC do elemento: a indicada em "Exportar para IFC como" (instância ou tipo) ou a padrão da
        /// categoria. null quando não dá para saber com segurança.
        /// </summary>
        public static string EntidadeDe(Document doc, Element e)
        {
            Element tipo = doc.GetElement(e.GetTypeId());
            string valor = Texto(e.get_Parameter(BuiltInParameter.IFC_EXPORT_ELEMENT_AS)) ?? Texto(e.LookupParameter("IfcExportAs")) ??
                           Texto(tipo?.get_Parameter(BuiltInParameter.IFC_EXPORT_ELEMENT_TYPE_AS)) ?? Texto(tipo?.LookupParameter("IfcExportAs"));
            if (valor != null)
            {
                string ent = valor.Split('.')[0].Trim();
                if (ent.EndsWith("Type", StringComparison.OrdinalIgnoreCase) && ent.Length > 7) ent = ent.Substring(0, ent.Length - 4);
                return ent;
            }
            if (e.Category == null) return null;
            switch (e.Category.BuiltInCategory)
            {
                case BuiltInCategory.OST_Walls: return "IfcWall";
                case BuiltInCategory.OST_Floors: return "IfcSlab";
                case BuiltInCategory.OST_Roofs: return "IfcRoof";
                case BuiltInCategory.OST_Ceilings: return "IfcCovering";
                case BuiltInCategory.OST_Columns:
                case BuiltInCategory.OST_StructuralColumns: return "IfcColumn";
                case BuiltInCategory.OST_StructuralFraming: return "IfcBeam";
                case BuiltInCategory.OST_StructuralFoundation: return "IfcFooting";
                case BuiltInCategory.OST_Doors: return "IfcDoor";
                case BuiltInCategory.OST_Windows: return "IfcWindow";
                case BuiltInCategory.OST_Stairs: return "IfcStair";
                case BuiltInCategory.OST_StairsRuns: return "IfcStairFlight";
                case BuiltInCategory.OST_Ramps: return "IfcRamp";
                case BuiltInCategory.OST_StairsRailing: return "IfcRailing";
                case BuiltInCategory.OST_CurtainWallPanels: return "IfcPlate";
                case BuiltInCategory.OST_CurtainWallMullions: return "IfcMember";
                case BuiltInCategory.OST_Rooms:
                case BuiltInCategory.OST_MEPSpaces: return "IfcSpace";
                case BuiltInCategory.OST_Furniture: return "IfcFurniture";
                case BuiltInCategory.OST_PipeCurves: return "IfcPipeSegment";
                case BuiltInCategory.OST_PipeFitting: return "IfcPipeFitting";
                case BuiltInCategory.OST_DuctCurves: return "IfcDuctSegment";
                case BuiltInCategory.OST_DuctFitting: return "IfcDuctFitting";
                case BuiltInCategory.OST_Rebar: return "IfcReinforcingBar";
                default: return null; // depende da tabela de classes do setup
            }
        }

        private static string Texto(Parameter p)
        {
            string t = p == null ? null : p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
            return string.IsNullOrWhiteSpace(t) || t.Equals("By Type", StringComparison.OrdinalIgnoreCase) || t.Equals("Por tipo", StringComparison.OrdinalIgnoreCase) ? null : t;
        }
    }

    /// <summary>
    /// Conferência de um .txt de property sets no modelo: que parâmetros não existem e que propriedades ficam
    /// vazias em parte dos elementos. Propriedades repetidas no mesmo bloco (várias origens) contam juntas.
    /// </summary>
    public class ConferenciaPsets
    {
        public class Propriedade
        {
            public string PropertySet { get; set; }
            public bool DeTipo { get; set; }
            public List<string> Entidades { get; set; }
            public string Nome { get; set; }
            public List<string> Parametros { get; set; }
            public bool Existe { get; set; }
            /// <summary>null sem modelo para conferir.</summary>
            public ModeloIfc.Contagem? Contagem { get; set; }
            public bool SemValor => Contagem is ModeloIfc.Contagem c && c.Elementos > 0 && c.ComValor < c.Elementos;
        }

        public class Pset
        {
            public string Nome { get; set; }
            public string Entidades { get; set; }
            public int Propriedades { get; set; }
            public int Encontradas { get; set; }
            public int SemValor { get; set; }
            public string NoModelo => $"{Encontradas} de {Propriedades}";
            public bool Completo => Encontradas == Propriedades && SemValor == 0;
        }

        public int Linhas { get; private set; }
        public List<Propriedade> Propriedades { get; } = new List<Propriedade>();
        public List<Pset> Psets { get; } = new List<Pset>();
        public List<string> Ausentes { get; } = new List<string>();
        public int SemValor => Propriedades.Count(p => p.SemValor);
        public List<string> Avisos { get; private set; }
        public bool ConferiuValores { get; private set; }

        public static ConferenciaPsets Fazer(string arquivo, ISet<string> parametros, ModeloIfc modelo)
        {
            var mapa = new ExportMapping();
            mapa.LoadUserDefinedPropertySets(arquivo);
            var r = new ConferenciaPsets { Linhas = mapa.Properties.Count, Avisos = mapa.Warnings, ConferiuValores = modelo != null };
            var nomes = parametros ?? modelo?.Parametros ?? new HashSet<string>();
            foreach (var g in mapa.Properties.GroupBy(p => (p.PropertySet ?? "", p.OnType, string.Join(",", p.Entities), p.Property ?? ""), ChaveIgual.Instancia))
            {
                PropertyMapping primeira = g.First();
                var origens = g.Select(p => string.IsNullOrWhiteSpace(p.RevitParameter) ? p.Property : p.RevitParameter).ToList();
                var prop = new Propriedade
                {
                    PropertySet = primeira.PropertySet,
                    DeTipo = primeira.OnType,
                    Entidades = primeira.Entities.ToList(),
                    Nome = primeira.Property,
                    Parametros = origens,
                    Existe = origens.Any(o => nomes.Contains(o) || o.StartsWith("BuiltInParameter.", StringComparison.OrdinalIgnoreCase)),
                };
                if (modelo != null && prop.Existe) prop.Contagem = modelo.Contar(prop.Entidades, origens, prop.DeTipo);
                r.Propriedades.Add(prop);
            }
            r.Ausentes.AddRange(mapa.Properties.Select(p => string.IsNullOrWhiteSpace(p.RevitParameter) ? p.Property : p.RevitParameter)
                .Where(p => !string.IsNullOrEmpty(p) && !nomes.Contains(p) && !p.StartsWith("BuiltInParameter.", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase));
            foreach (var g in r.Propriedades.GroupBy(p => p.PropertySet, StringComparer.OrdinalIgnoreCase))
            {
                List<string> ents = g.SelectMany(p => p.Entidades).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                r.Psets.Add(new Pset
                {
                    Nome = g.Key,
                    Entidades = ents.Count == 0 ? "todas" : ents.Count <= 3 ? string.Join(", ", ents) : string.Join(", ", ents.Take(2)) + $"… ({ents.Count})",
                    Propriedades = g.Count(),
                    Encontradas = g.Count(p => p.Existe),
                    SemValor = g.Count(p => p.SemValor),
                });
            }
            return r;
        }

        private class ChaveIgual : IEqualityComparer<(string, bool, string, string)>
        {
            public static readonly ChaveIgual Instancia = new ChaveIgual();
            public bool Equals((string, bool, string, string) a, (string, bool, string, string) b) =>
                string.Equals(a.Item1, b.Item1, StringComparison.OrdinalIgnoreCase) && a.Item2 == b.Item2 &&
                string.Equals(a.Item3, b.Item3, StringComparison.OrdinalIgnoreCase) && string.Equals(a.Item4, b.Item4, StringComparison.OrdinalIgnoreCase);
            public int GetHashCode((string, bool, string, string) k) =>
                StringComparer.OrdinalIgnoreCase.GetHashCode(k.Item1 + "|" + k.Item3 + "|" + k.Item4) ^ k.Item2.GetHashCode();
        }
    }
}
