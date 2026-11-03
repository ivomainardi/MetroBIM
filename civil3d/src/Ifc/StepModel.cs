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
        readonly List<int> _order = new List<int>();
        readonly Dictionary<int, StepEntity> _byId = new Dictionary<int, StepEntity>();
        readonly Dictionary<int, string> _raw = new Dictionary<int, string>();   // texto original, para quem não mudou
        readonly HashSet<int> _dirty = new HashSet<int>();
        int _maxId;

        static readonly Regex EntityRx = new Regex(@"^#(\d+)\s*=\s*([A-Za-z0-9_]+)\s*\((.*)\)\s*$", RegexOptions.Singleline | RegexOptions.Compiled);

        public static StepModel Load(string path)
        {
            var text = File.ReadAllText(path);
            var m = new StepModel();
            var dataStart = IndexOfKeyword(text, "DATA;", 0);
            if (dataStart < 0) throw new InvalidDataException("Arquivo IFC sem seção DATA.");
            m.Header = text.Substring(0, dataStart);
            var sm = Regex.Match(m.Header, @"FILE_SCHEMA\s*\(\s*\(\s*'([^']*)'", RegexOptions.IgnoreCase);
            m.Schema = sm.Success ? sm.Groups[1].Value.ToUpperInvariant() : "";

            int i = dataStart + "DATA;".Length;
            var sb = new StringBuilder();
            bool inString = false;
            for (; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '\'') inString = !inString;   // '' dentro de texto alterna duas vezes: ok
                if (!inString && c == ';')
                {
                    var stmt = sb.ToString().Trim();
                    sb.Clear();
                    if (stmt.Equals("ENDSEC", StringComparison.OrdinalIgnoreCase)) break;
                    if (stmt.Length == 0) continue;
                    m.AddParsed(stmt);
                    continue;
                }
                if (!inString && (c == '\r' || c == '\n')) { sb.Append(' '); continue; }
                sb.Append(c);
            }
            return m;
        }

        void AddParsed(string stmt)
        {
            var mm = EntityRx.Match(stmt);
            if (!mm.Success) throw new InvalidDataException("Entidade STEP não reconhecida: " + Truncate(stmt));
            var id = int.Parse(mm.Groups[1].Value, CultureInfo.InvariantCulture);
            _order.Add(id);
            _raw[id] = stmt;
            _byId[id] = null;          // argumentos só são separados quando alguém pedir (arquivos grandes)
            _maxId = Math.Max(_maxId, id);
        }

        public IEnumerable<int> Ids => _order;

        public StepEntity Get(int id)
        {
            if (!_byId.TryGetValue(id, out var e)) return null;
            if (e != null) return e;
            var mm = EntityRx.Match(_raw[id]);
            e = new StepEntity { Id = id, Type = mm.Groups[2].Value.ToUpperInvariant(), Args = SplitArgs(mm.Groups[3].Value) };
            _byId[id] = e;
            return e;
        }

        /// <summary>Tipo da entidade sem separar os argumentos.</summary>
        public string TypeOf(int id)
        {
            if (_byId.TryGetValue(id, out var e) && e != null) return e.Type;
            if (!_raw.TryGetValue(id, out var raw)) return null;
            var p = raw.IndexOf('=');
            var q = raw.IndexOf('(', p);
            return raw.Substring(p + 1, q - p - 1).Trim().ToUpperInvariant();
        }

        public IEnumerable<StepEntity> OfType(string type)
        {
            type = type.ToUpperInvariant();
            foreach (var id in _order.ToList())
                if (TypeOf(id) == type) yield return Get(id);
        }

        public int Add(string type, params string[] args)
        {
            var id = ++_maxId;
            var e = new StepEntity { Id = id, Type = type.ToUpperInvariant(), Args = args.ToList() };
            _order.Add(id);
            _byId[id] = e;
            _dirty.Add(id);
            return id;
        }

        /// <summary>Remove a entidade. Quem a referenciava precisa ter sido ajustado antes.</summary>
        public void Remove(int id)
        {
            _order.Remove(id);
            _byId.Remove(id);
            _raw.Remove(id);
            _dirty.Remove(id);
        }

        public void SetArg(int id, int index, string value)
        {
            var e = Get(id);
            e.Args[index] = value;
            _dirty.Add(id);
        }

        public void Save(string path)
        {
            using var w = new StreamWriter(path, false, new UTF8Encoding(false));
            w.Write(Header);
            w.Write("DATA;\n");
            foreach (var id in _order)
            {
                if (_dirty.Contains(id))
                {
                    var e = _byId[id];
                    w.Write("#" + id + "=" + e.Type + "(" + string.Join(",", e.Args) + ");\n");
                }
                else w.Write(_raw[id] + ";\n");
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
