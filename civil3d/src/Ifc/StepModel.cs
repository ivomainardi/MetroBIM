using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MetroSP.Ifc
{
    /// <summary>Uma linha "#id=TIPO(args);" do arquivo STEP.</summary>
    public class StepEntity
    {
        public int Id;
        public string Type;            // sempre em maiúsculas
        public List<string> Args;      // argumentos de primeiro nível, como texto STEP
    }

    /// <summary>
    /// Leitura e edição mínima de arquivos IFC (STEP físico, ISO 10303-21): o suficiente para
    /// pós-processar o IFC gerado pela Autodesk sem depender de bibliotecas externas.
    /// Preserva a ordem e o texto das entidades que não são alteradas.
    /// </summary>
    public class StepModel
    {
        public string Header;          // tudo até "DATA;" (exclusive)
        public string Schema;
        // Em modelos grandes o IFC tem milhões de entidades: o tipo de cada uma fica indexado na leitura (OfType não
        // percorre o arquivo inteiro) e Remove só marca a entidade (sem deslocar a lista toda a cada remoção).
        readonly List<int> _order = new List<int>();
        readonly Dictionary<int, StepEntity> _byId = new Dictionary<int, StepEntity>();
        readonly Dictionary<int, string> _raw = new Dictionary<int, string>();   // texto original, para quem não mudou
        readonly Dictionary<int, string> _type = new Dictionary<int, string>();
        readonly Dictionary<string, List<int>> _byType = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        readonly HashSet<int> _dirty = new HashSet<int>();
        readonly HashSet<int> _removed = new HashSet<int>();
        int _maxId;

        public static StepModel Load(string path)
        {
            var text = File.ReadAllText(path);
            var m = new StepModel();
            var dataStart = IndexOfKeyword(text, "DATA;", 0);
            if (dataStart < 0) throw new InvalidDataException("Arquivo IFC sem seção DATA.");
            m.Header = text.Substring(0, dataStart);
            var sm = Regex.Match(m.Header, @"FILE_SCHEMA\s*\(\s*\(\s*'([^']*)'", RegexOptions.IgnoreCase);
            m.Schema = sm.Success ? sm.Groups[1].Value.ToUpperInvariant() : "";

            int start = dataStart + "DATA;".Length;
            bool inString = false, quebra = false;
            for (int i = start; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '\'') inString = !inString;   // '' dentro de texto alterna duas vezes: ok
                else if (inString) continue;
                else if (c == '\r' || c == '\n') quebra = true;
                else if (c == ';')
                {
                    var stmt = Statement(text, start, i, quebra);
                    start = i + 1;
                    quebra = false;
                    if (stmt.Equals("ENDSEC", StringComparison.OrdinalIgnoreCase)) break;
                    if (stmt.Length == 0) continue;
                    m.AddParsed(stmt);
                }
            }
            return m;
        }

        /// <summary>Texto da entidade entre start e fim, com as quebras de linha fora dos textos trocadas por espaço.</summary>
        static string Statement(string text, int start, int fim, bool quebra)
        {
            if (!quebra) return text.Substring(start, fim - start).Trim();
            var sb = new StringBuilder(fim - start);
            bool inString = false;
            for (int i = start; i < fim; i++)
            {
                var c = text[i];
                if (c == '\'') inString = !inString;
                if (!inString && (c == '\r' || c == '\n')) { sb.Append(' '); continue; }
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        void AddParsed(string stmt)
        {
            // #id = TIPO ( args )
            int i = 0, n = stmt.Length;
            bool ok = n > 0 && stmt[0] == '#';
            int idIni = ++i;
            while (ok && i < n && char.IsDigit(stmt[i])) i++;
            ok &= i > idIni;
            int idFim = i;
            while (ok && i < n && char.IsWhiteSpace(stmt[i])) i++;
            ok &= i < n && stmt[i] == '=';
            i++;
            while (ok && i < n && char.IsWhiteSpace(stmt[i])) i++;
            int tipoIni = i;
            while (ok && i < n && (char.IsLetterOrDigit(stmt[i]) || stmt[i] == '_') && stmt[i] < 128) i++;
            ok &= i > tipoIni;
            int tipoFim = i;
            while (ok && i < n && char.IsWhiteSpace(stmt[i])) i++;
            ok &= i < n && stmt[i] == '(' && stmt[n - 1] == ')' && n - 1 > i;
            if (!ok) throw new InvalidDataException("Entidade STEP não reconhecida: " + Truncate(stmt));

            var id = int.Parse(stmt.Substring(idIni, idFim - idIni), CultureInfo.InvariantCulture);
            _order.Add(id);
            _raw[id] = stmt;
            _byId[id] = null;          // argumentos só são separados quando alguém pedir (arquivos grandes)
            Index(id, stmt.Substring(tipoIni, tipoFim - tipoIni).ToUpperInvariant());
            _maxId = Math.Max(_maxId, id);
        }

        void Index(int id, string type)
        {
            type = string.Intern(type);
            _type[id] = type;
            if (!_byType.TryGetValue(type, out var l)) _byType[type] = l = new List<int>();
            l.Add(id);
        }

        /// <summary>Ids das entidades, na ordem do arquivo.</summary>
        public IEnumerable<int> Ids
        {
            get
            {
                foreach (var id in _order)
                    if (_removed.Count == 0 || !_removed.Contains(id)) yield return id;
            }
        }

        /// <summary>Maior id em uso; o próximo Add recebe LastId + 1.</summary>
        public int LastId => _maxId;

        public StepEntity Get(int id)
        {
            if (!_byId.TryGetValue(id, out var e)) return null;
            if (e != null) return e;
            var raw = _raw[id];
            int open = raw.IndexOf('(', raw.IndexOf('='));
            e = new StepEntity { Id = id, Type = _type[id], Args = SplitArgs(raw.Substring(open + 1, raw.Length - open - 2)) };
            _byId[id] = e;
            return e;
        }

        /// <summary>Tipo da entidade sem separar os argumentos.</summary>
        public string TypeOf(int id)
        {
            if (_byId.TryGetValue(id, out var e) && e != null) return e.Type;
            return _type.TryGetValue(id, out var t) ? t : null;
        }

        public IEnumerable<StepEntity> OfType(string type)
        {
            type = type.ToUpperInvariant();
            if (!_byType.TryGetValue(type, out var l)) yield break;
            foreach (var id in l.ToArray())
                if (!_removed.Contains(id) && TypeOf(id) == type) yield return Get(id);
        }

        public int Add(string type, params string[] args)
        {
            var id = ++_maxId;
            var e = new StepEntity { Id = id, Type = type.ToUpperInvariant(), Args = args.ToList() };
            _order.Add(id);
            _byId[id] = e;
            Index(id, e.Type);
            _dirty.Add(id);
            return id;
        }

        /// <summary>Acrescenta uma entidade escrita por extenso, ex.: "IFCCARTESIANPOINT((0.,0.))".</summary>
        public int AddRaw(string entity)
        {
            var open = entity.IndexOf('(');
            var close = entity.LastIndexOf(')');
            return Add(entity.Substring(0, open).Trim(), SplitArgs(entity.Substring(open + 1, close - open - 1)).ToArray());
        }

        /// <summary>Remove a entidade. Quem a referenciava precisa ter sido ajustado antes.</summary>
        public void Remove(int id)
        {
            if (!_byId.ContainsKey(id)) return;
            _removed.Add(id);
            _byId.Remove(id);
            _raw.Remove(id);
            _type.Remove(id);
            _dirty.Remove(id);
        }

        /// <summary>Troca o tipo da entidade (os argumentos ficam; ajuste-os antes de salvar).</summary>
        public void SetType(int id, string type)
        {
            var e = Get(id);
            if (e == null) return;
            type = type.ToUpperInvariant();
            if (e.Type == type) return;
            e.Type = type;
            _type[id] = type;
            Index(id, type);
            _dirty.Add(id);
        }

        public void SetArg(int id, int index, string value)
        {
            var e = Get(id);
            e.Args[index] = value;
            _dirty.Add(id);
        }

        /// <summary>
        /// true se outra entidade (fora a exceção) cita #id. Lê o texto de cada entidade sem guardar os argumentos
        /// separados, para não dobrar a memória num IFC grande.
        /// </summary>
        public bool Referenciado(int id, int exceto = 0)
        {
            string r = "#" + id;
            foreach (var x in Ids)
            {
                if (x == id || x == exceto) continue;
                var e = _byId[x];
                if (e != null)
                {
                    foreach (var a in e.Args) if (ContemRef(a, r, 0)) return true;
                }
                else
                {
                    var raw = _raw[x];
                    if (ContemRef(raw, r, raw.IndexOf('='))) return true;
                }
            }
            return false;
        }

        static readonly Regex RefRx = new Regex(@"#(\d+)", RegexOptions.Compiled);

        /// <summary>
        /// Numa passada só pelo arquivo: as entidades (fora as próprias) que citam algum dos alvos, com os alvos que
        /// cada uma cita. Não separa os argumentos de quem não cita nada.
        /// </summary>
        public Dictionary<int, List<int>> QuemCita(ICollection<int> alvos)
        {
            var r = new Dictionary<int, List<int>>();
            if (alvos.Count == 0) return r;
            var set = alvos as HashSet<int> ?? new HashSet<int>(alvos);
            foreach (var x in Ids)
            {
                if (set.Contains(x)) continue;
                var e = _byId[x];
                string texto = e != null ? string.Join(",", e.Args) : _raw[x].Substring(_raw[x].IndexOf('='));
                foreach (Match m in RefRx.Matches(texto))
                {
                    int id = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    if (!set.Contains(id)) continue;
                    if (!r.TryGetValue(x, out var l)) r[x] = l = new List<int>();
                    if (!l.Contains(id)) l.Add(id);
                }
            }
            return r;
        }

        static bool ContemRef(string texto, string r, int de)
        {
            int p = texto.IndexOf(r, de, StringComparison.Ordinal);
            while (p >= 0)
            {
                int fim = p + r.Length;
                if (fim >= texto.Length || !char.IsDigit(texto[fim])) return true;
                p = texto.IndexOf(r, fim, StringComparison.Ordinal);
            }
            return false;
        }

        public void Save(string path)
        {
            using var w = new StreamWriter(path, false, new UTF8Encoding(false), 1 << 16);
            w.Write(Header);
            w.Write("DATA;\n");
            foreach (var id in Ids)
            {
                if (_dirty.Contains(id))
                {
                    var e = _byId[id];
                    w.Write("#" + id + "=" + e.Type + "(" + string.Join(",", e.Args) + ");\n");
                }
                else { w.Write(_raw[id]); w.Write(";\n"); }
            }
            w.Write("ENDSEC;\nEND-ISO-10303-21;\n");
        }

        // ------------------------------------------------------------------ helpers de valores

        public static List<string> SplitArgs(string s)
        {
            var list = new List<string>();
            int depth = 0, start = 0;
            bool inString = false;
            for (int i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c == '\'') { inString = !inString; continue; }
                if (inString) continue;
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (c == ',' && depth == 0)
                {
                    list.Add(s.Substring(start, i - start).Trim());
                    start = i + 1;
                }
            }
            list.Add(s.Substring(start).Trim());
            return list;
        }

        public static int Ref(string arg) =>
            arg != null && arg.StartsWith("#") ? int.Parse(arg.Substring(1), CultureInfo.InvariantCulture) : 0;

        public static List<int> RefList(string arg)
        {
            if (arg == null || arg == "$" || !arg.StartsWith("(")) return new List<int>();
            return SplitArgs(arg.Substring(1, arg.Length - 2)).Where(a => a.StartsWith("#")).Select(Ref).ToList();
        }

        public static string RefListText(IEnumerable<int> ids) => "(" + string.Join(",", ids.Select(i => "#" + i)) + ")";

        public static double Real(string arg) =>
            double.Parse(arg.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);

        public static List<double> RealList(string arg) =>
            SplitArgs(arg.Trim().TrimStart('(').TrimEnd(')')).Select(Real).ToList();

        /// <summary>Decodifica texto STEP ('' e \X2\...\X0\). "$" vira null.</summary>
        public static string Text(string arg)
        {
            if (arg == null || arg == "$" || arg == "*") return null;
            var s = arg.Trim();
            if (s.StartsWith("IFC", StringComparison.OrdinalIgnoreCase) && s.Contains("("))   // valor tipado: IFCLABEL('x')
                s = s.Substring(s.IndexOf('(') + 1).TrimEnd(')');
            if (!s.StartsWith("'")) return s;
            s = s.Substring(1, s.Length - 2).Replace("''", "'");
            s = Regex.Replace(s, @"\\X2\\((?:[0-9A-Fa-f]{4})+)\\X0\\", m =>
            {
                var hex = m.Groups[1].Value;
                var sb = new StringBuilder();
                for (int i = 0; i < hex.Length; i += 4) sb.Append((char)Convert.ToInt32(hex.Substring(i, 4), 16));
                return sb.ToString();
            });
            s = Regex.Replace(s, @"\\X\\([0-9A-Fa-f]{2})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
            return s.Replace("\\\\", "\\");
        }

        static int IndexOfKeyword(string text, string kw, int from)
        {
            var rx = new Regex(@"(^|[\r\n;])\s*" + Regex.Escape(kw), RegexOptions.IgnoreCase);
            var m = rx.Match(text, from);
            return m.Success ? m.Index + m.Value.IndexOf(kw[0]) : -1;
        }

        static string Truncate(string s) => s.Length > 120 ? s.Substring(0, 120) + "..." : s;
    }
}
