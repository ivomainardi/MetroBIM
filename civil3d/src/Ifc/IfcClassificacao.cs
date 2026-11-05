using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MetroSP.Classificacao;

namespace MetroSP.Ifc
{
    /// <summary>Uma classificação procurada nos property sets do IFC exportado.</summary>
    public partial class RegraIfc
    {
        /// <summary>Nome do property set; null = qualquer.</summary>
        public string Pset { get; set; }
        public string PropCodigo { get; set; }
        /// <summary>Só no formato separado; null = o código traz "Código:Descrição".</summary>
        public string PropDescricao { get; set; }
        public FormatoClassificacao Formato { get; set; }
        public string Nome { get; set; }
        public string Edicao { get; set; }
    }

    /// <summary>
    /// Transforma a classificação gravada em property sets (padrões de Verificar classificação e os nomes da Autodesk:
    /// ClassificationCode, ClassificationCode(2)..., Classification.X.Number/Description) em IfcClassification,
    /// IfcClassificationReference e IfcRelAssociatesClassification, como o exportador IFC do Revit faz.
    /// O property set continua no IFC.
    /// </summary>
    public static class IfcClassificacao
    {
        private static readonly Regex CodigoIfc = new Regex(@"^ClassificationCode(?:\((?<n>\d+)\))?$", RegexOptions.IgnoreCase);
        private static readonly Regex Manager = new Regex(@"^Classification\.(?<t>.+)\.Number$", RegexOptions.IgnoreCase);

        public static int Aplicar(StepModel m, IList<RegraIfc> regras, Action<string> log)
        {
            log ??= _ => { };
            if (m.Schema != null && m.Schema.StartsWith("IFC2X3", StringComparison.OrdinalIgnoreCase))
            {
                log("classificação: esquema IFC 2x3, IfcClassificationReference não foi gravada.");
                return 0;
            }

            // Objetos de cada property set.
            var objetos = new Dictionary<int, List<int>>();
            foreach (StepEntity r in m.OfType("IFCRELDEFINESBYPROPERTIES"))
            {
                int def = StepModel.Ref(r.Args[5]);
                if (!objetos.TryGetValue(def, out var l)) objetos[def] = l = new List<int>();
                l.AddRange(StepModel.RefList(r.Args[4]));
            }

            // O que já está classificado (para não repetir) e as IfcClassification que já existem.
            var classes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (StepEntity c in m.OfType("IFCCLASSIFICATION"))
            {
                string n = StepModel.Text(c.Args[3]);
                if (n != null && !classes.ContainsKey(n)) classes[n] = c.Id;
            }
            // Pelo código, sem olhar o nome da classificação: o exportador da Autodesk dá nomes próprios a ela.
            var existentes = new HashSet<(int, string)>();
            foreach (StepEntity rel in m.OfType("IFCRELASSOCIATESCLASSIFICATION"))
            {
                StepEntity rf = m.Get(StepModel.Ref(rel.Args[5]));
                if (rf == null || rf.Type != "IFCCLASSIFICATIONREFERENCE") continue;
                string cod = StepModel.Text(rf.Args[1]);
                if (string.IsNullOrEmpty(cod)) continue;
                foreach (int o in StepModel.RefList(rel.Args[4])) existentes.Add((o, cod.ToLowerInvariant()));
            }

            // (classificação, código) → descrição e objetos.
            var achados = new Dictionary<(string Sis, string Cod), (string Desc, string Edicao, HashSet<int> Objs)>();
            foreach (StepEntity pset in m.OfType("IFCPROPERTYSET"))
            {
                if (!objetos.TryGetValue(pset.Id, out var objs) || objs.Count == 0) continue;
                string nomePset = StepModel.Text(pset.Args[2]) ?? "";
                var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (int pid in StepModel.RefList(pset.Args[4]))
                {
                    StepEntity p = m.Get(pid);
                    if (p == null || p.Type != "IFCPROPERTYSINGLEVALUE") continue;
                    string nome = StepModel.Text(p.Args[0]);
                    if (nome != null && !props.ContainsKey(nome)) props[nome] = StepModel.Text(p.Args[2]);
                }
                foreach (var (sis, edicao, cod, desc) in Extrair(nomePset, props, regras))
                {
                    var k = (sis, cod);
                    if (!achados.TryGetValue(k, out var a)) achados[k] = a = (desc, edicao, new HashSet<int>());
                    else if (string.IsNullOrEmpty(a.Desc) && !string.IsNullOrEmpty(desc)) achados[k] = a = (desc, a.Edicao ?? edicao, a.Objs);
                    foreach (int o in objs)
                        if (!existentes.Contains((o, cod.ToLowerInvariant()))) a.Objs.Add(o);
                }
            }

            int rels = 0;
            foreach (var g in achados.Where(a => a.Value.Objs.Count > 0).GroupBy(a => a.Key.Sis))
            {
                if (!classes.TryGetValue(g.Key, out int cls))
                {
                    string edicao = g.Select(x => x.Value.Edicao).FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));
                    cls = m.Add("IFCCLASSIFICATION", "$", Opt(edicao), "$", S(g.Key), "$", "$", "$");
                    classes[g.Key] = cls;
                }
                foreach (var a in g.OrderBy(x => x.Key.Cod, StringComparer.OrdinalIgnoreCase))
                {
                    int rf = m.Add("IFCCLASSIFICATIONREFERENCE", "$", S(a.Key.Cod), Opt(a.Value.Desc), "#" + cls, "$", "$");
                    m.Add("IFCRELASSOCIATESCLASSIFICATION", Guid(g.Key + "|" + a.Key.Cod), "$", S("MetroSP classificação"), "$",
                        StepModel.RefListText(a.Value.Objs.OrderBy(i => i)), "#" + rf);
                    rels++;
                }
                log($"classificação {g.Key}: {g.Count()} código(s) em {g.SelectMany(x => x.Value.Objs).Distinct().Count()} objeto(s) como IfcClassificationReference.");
            }
            return rels;
        }

        /// <summary>Classificações de um property set: pelas regras dos padrões e, para o que sobrar, pelos nomes da Autodesk.</summary>
        private static IEnumerable<(string Sis, string Edicao, string Cod, string Desc)> Extrair(string pset, Dictionary<string, string> props, IList<RegraIfc> regras)
        {
            var usadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RegraIfc r in regras ?? new List<RegraIfc>())
            {
                if (r.Pset != null && !string.Equals(r.Pset, pset, StringComparison.OrdinalIgnoreCase)) continue;
                if (!props.TryGetValue(r.PropCodigo, out string valor)) continue;
                usadas.Add(r.PropCodigo);
                if (r.PropDescricao != null) usadas.Add(r.PropDescricao);
                if (string.IsNullOrWhiteSpace(valor)) continue;
                if (r.Formato == FormatoClassificacao.Separados)
                {
                    string desc = r.PropDescricao != null && props.TryGetValue(r.PropDescricao, out string d) ? d : null;
                    yield return (r.Nome, r.Edicao, valor.Trim(), desc?.Trim());
                }
                else
                {
                    ValorClassificacao v = ValorClassificacao.Ler(valor);
                    yield return v.Valido ? (v.Sistema ?? r.Nome, r.Edicao, v.Codigo, v.Descricao) : (r.Nome, r.Edicao, valor.Trim(), null);
                }
            }
            foreach (var kv in props)
            {
                if (usadas.Contains(kv.Key) || string.IsNullOrWhiteSpace(kv.Value)) continue;
                Match mc = CodigoIfc.Match(kv.Key);
                if (mc.Success)
                {
                    string padrao = mc.Groups["n"].Success ? "Classificação " + mc.Groups["n"].Value : "Classificação";
                    ValorClassificacao v = ValorClassificacao.Ler(kv.Value);
                    yield return v.Valido ? (v.Sistema ?? padrao, null, v.Codigo, v.Descricao) : (padrao, null, kv.Value.Trim(), null);
                    continue;
                }
                Match mm = Manager.Match(kv.Key);
                if (mm.Success)
                {
                    string tabela = mm.Groups["t"].Value;
                    props.TryGetValue($"Classification.{tabela}.Description", out string desc);
                    yield return (tabela.Replace('.', ' '), null, kv.Value.Trim(), desc?.Trim());
                }
            }
        }

        private static string Opt(string s) => string.IsNullOrWhiteSpace(s) ? "$" : S(s);

        private static string Guid(string key) =>
            S(CompressGuid(MD5.HashData(Encoding.UTF8.GetBytes("MetroSP|classificacao|" + key))));

        /// <summary>Texto STEP: aspas duplicadas, barra invertida escapada, não-ASCII em \X2\.</summary>
        private static string S(string text)
        {
            if (text == null) return "$";
            var sb = new StringBuilder("'");
            foreach (char c in text)
            {
                if (c == '\'') sb.Append("''");
                else if (c == '\\') sb.Append("\\\\");
                else if (c >= 32 && c <= 126) sb.Append(c);
                else sb.Append("\\X2\\").Append(((int)c).ToString("X4")).Append("\\X0\\");
            }
            return sb.Append('\'').ToString();
        }

        private const string Base64Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

        /// <summary>16 bytes no formato de 22 caracteres do IFC.</summary>
        private static string CompressGuid(byte[] bytes16)
        {
            var n = new System.Numerics.BigInteger(bytes16, isUnsigned: true, isBigEndian: true);
            var chars = new char[22];
            for (int i = 21; i >= 0; i--)
            {
                chars[i] = Base64Chars[(int)(n & 63)];
                n >>= 6;
            }
            return new string(chars);
        }
    }
}
