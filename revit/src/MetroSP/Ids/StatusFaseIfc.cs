using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MetroSP.Ifc
{
    /// <summary>
    /// Grava o status de fase de cada elemento do Revit, na fase exportada, em duas propriedades:
    /// <list type="bullet">
    /// <item>Status do Pset_&lt;Classe&gt;Common (NEW, EXISTING, DEMOLISH, TEMPORARY; ISO 16739, PEnum_ElementStatus),
    /// só nas classes cujo Pset_*Common tem Status;</item>
    /// <item>Estado do Pset Metro (Novo, Existente, A demolir, Temporário), nas classes que têm o Pset Metro no .txt
    /// de property sets da exportação.</item>
    /// </list>
    /// O elemento do IFC é ligado ao do Revit pelo Tag (o ElementId, que o exportador da Autodesk grava). Só IFC4 e
    /// IFC 4.3. Um valor que já exista no property set é trocado pelo da fase.
    /// </summary>
    public static class StatusFaseIfc
    {
        public const string Propriedade = "Status";
        public const string PsetMetro = "Metro", PropriedadeMetro = "Estado";

        /// <summary>Valor do Estado (Pset Metro) para o Status do IFC.</summary>
        public static string Estado(string status)
        {
            switch (status)
            {
                case "NEW": return "Novo";
                case "EXISTING": return "Existente";
                case "DEMOLISH": return "A demolir";
                case "TEMPORARY": return "Temporário";
                default: return null;
            }
        }

        /// <summary>Classes (em maiúsculas) que têm o Pset Metro no .txt de property sets (linhas "PropertySet: Metro I IfcX,IfcY").</summary>
        public static HashSet<string> ClassesComPsetMetro(string arquivoPsets)
        {
            var r = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(arquivoPsets) || !System.IO.File.Exists(arquivoPsets)) return r;
            foreach (string linha in System.IO.File.ReadAllLines(arquivoPsets))
            {
                string[] c = linha.Split('\t').Select(x => x.Trim()).ToArray();
                if (c.Length < 4 || !c[0].Equals("PropertySet:", StringComparison.OrdinalIgnoreCase) || !c[1].Equals(PsetMetro, StringComparison.Ordinal)) continue;
                foreach (string classe in c[3].Split(',').Select(x => x.Trim()).Where(x => x.StartsWith("Ifc", StringComparison.OrdinalIgnoreCase)))
                {
                    string k = classe.ToUpperInvariant();
                    if (k.EndsWith("TYPE")) continue;
                    r.Add(k);
                    r.Add(k + "STANDARDCASE");
                    r.Add(k + "ELEMENTEDCASE");
                }
            }
            return r;
        }

        /// <summary>Classe (em maiúsculas, como no STEP) → nome do property set. Inclui as *StandardCase do IFC4.</summary>
        private static readonly Dictionary<string, string> Psets = Montar(
            "Beam", "BuildingElementProxy", "Chimney", "Column", "Covering", "CurtainWall", "Door", "Footing", "Member",
            "Pile", "Plate", "Railing", "Ramp", "RampFlight", "Roof", "ShadingDevice", "Slab", "Stair", "StairFlight",
            "Wall", "Window");

        private static Dictionary<string, string> Montar(params string[] classes)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string c in classes)
            {
                string pset = "Pset_" + c + "Common";
                d["IFC" + c.ToUpperInvariant()] = pset;
                d["IFC" + c.ToUpperInvariant() + "STANDARDCASE"] = pset;
                d["IFC" + c.ToUpperInvariant() + "ELEMENTEDCASE"] = pset;
            }
            return d;
        }

        /// <summary>
        /// statusDoElemento: ElementId do Revit → NEW, EXISTING, DEMOLISH, TEMPORARY ou null (não grava).
        /// classesMetro: classes com o Pset Metro (ClassesComPsetMetro). Retorna quantos elementos ficaram com o Status
        /// e quantos com o Estado.
        /// </summary>
        public static (int Status, int Estado) Aplicar(StepModel m, Func<long, string> statusDoElemento, HashSet<string> classesMetro, Action<string> log)
        {
            log ??= _ => { };
            if (m.Schema != null && m.Schema.StartsWith("IFC2X3", StringComparison.OrdinalIgnoreCase))
            {
                log("Status da fase: esquema IFC 2x3, não foi gravado.");
                return (0, 0);
            }
            var cache = new Dictionary<long, string>();
            string StatusDe(long id)
            {
                if (!cache.TryGetValue(id, out string s)) cache[id] = s = statusDoElemento(id);
                return s;
            }
            int status = Gravar(m, t => Psets.TryGetValue(t, out string p) ? p : null, StatusDe, Propriedade,
                v => ("IFCPROPERTYENUMERATEDVALUE", "(IFCLABEL('" + v + "'))"));
            int estado = classesMetro == null || classesMetro.Count == 0 ? 0
                : Gravar(m, t => classesMetro.Contains(t) ? PsetMetro : null, id => Estado(StatusDe(id)), PropriedadeMetro,
                    v => ("IFCPROPERTYSINGLEVALUE", "IFCLABEL(" + Texto(v) + ")"));
            return (status, estado);
        }

        /// <summary>
        /// Grava a propriedade no property set de cada elemento (o que já existe, ou um novo). psetDoTipo: tipo STEP →
        /// nome do property set (null = não grava). valor: ElementId → valor (null = não grava).
        /// entidade: valor → (tipo da IfcProperty, argumento do valor).
        /// </summary>
        private static int Gravar(StepModel m, Func<string, string> psetDoTipo, Func<long, string> valorDoElemento, string Propriedade,
            Func<string, (string Tipo, string Valor)> entidade)
        {
            // Elementos do IFC → (property set, valor).
            var alvo = new Dictionary<int, (string Pset, string Status)>();
            foreach (int id in m.Ids.ToList())
            {
                string pset = psetDoTipo(m.TypeOf(id) ?? "");
                if (pset == null) continue;
                StepEntity e = m.Get(id);
                if (e == null || e.Args.Count < 8) continue;
                if (!long.TryParse(StepModel.Text(e.Args[7])?.Trim(), out long elementId)) continue;
                string valor = valorDoElemento(elementId);
                if (valor != null) alvo[id] = (pset, valor);
            }
            if (alvo.Count == 0) return 0;

            // Property sets com o nome certo já ligados a esses elementos, e todos os objetos de cada property set.
            List<StepEntity> rels = m.OfType("IFCRELDEFINESBYPROPERTIES").ToList();
            string historico = rels.Select(r => r.Args[1]).FirstOrDefault(a => a != "$") ?? "$";
            var psetDoObjeto = new Dictionary<int, int>();
            var objetosDoPset = new Dictionary<int, List<int>>();
            foreach (StepEntity rel in rels)
            {
                int def = StepModel.Ref(rel.Args[5]);
                if (m.TypeOf(def) != "IFCPROPERTYSET") continue;
                List<int> objs = StepModel.RefList(rel.Args[4]);
                if (!objetosDoPset.TryGetValue(def, out List<int> lista)) objetosDoPset[def] = lista = new List<int>();
                lista.AddRange(objs);
                string nome = null;
                foreach (int o in objs)
                {
                    if (!alvo.TryGetValue(o, out var a)) continue;
                    nome ??= StepModel.Text(m.Get(def).Args[2]);
                    if (string.Equals(nome, a.Pset, StringComparison.OrdinalIgnoreCase)) psetDoObjeto[o] = def;
                }
            }

            var propriedade = new Dictionary<string, int>();
            int Prop(string valor)
            {
                if (!propriedade.TryGetValue(valor, out int p))
                {
                    var (tipo, arg) = entidade(valor);
                    propriedade[valor] = p = m.Add(tipo, Texto(Propriedade), "$", arg, "$");
                }
                return p;
            }
            List<int> SemStatus(int pset) => StepModel.RefList(m.Get(pset).Args[4])
                .Where(p => !string.Equals(StepModel.Text(m.Get(p)?.Args[0]), Propriedade, StringComparison.OrdinalIgnoreCase)).ToList();
            int NovoPset(string nome, IEnumerable<int> props, List<int> objs, string status)
            {
                string chave = nome + "|" + Propriedade + "|" + status + "|" + string.Join(",", objs);
                int pset = m.Add("IFCPROPERTYSET", Guid("pset|" + chave), historico, Texto(nome), "$", StepModel.RefListText(props));
                m.Add("IFCRELDEFINESBYPROPERTIES", Guid("rel|" + chave), historico, "$", "$", StepModel.RefListText(objs), "#" + pset);
                return pset;
            }

            int gravados = 0;
            var mover = new Dictionary<int, HashSet<int>>();   // property set antigo → objetos que saem dele
            foreach (var g in alvo.Where(a => psetDoObjeto.ContainsKey(a.Key)).GroupBy(a => psetDoObjeto[a.Key]))
            {
                int pset = g.Key;
                List<int> todos = objetosDoPset[pset].Distinct().ToList();
                var porStatus = g.GroupBy(a => a.Value.Status).ToList();
                if (porStatus.Count == 1 && todos.All(alvo.ContainsKey))
                {
                    // Todos os objetos do property set com o mesmo status: troca no próprio property set.
                    List<int> props = SemStatus(pset);
                    props.Add(Prop(porStatus[0].Key));
                    m.SetArg(pset, 4, StepModel.RefListText(props));
                    gravados += todos.Count;
                    continue;
                }
                // Property set compartilhado (o exportador reaproveita os iguais) por elementos de status diferentes:
                // uma cópia para cada status, e os elementos saem do original.
                string nome = StepModel.Text(m.Get(pset).Args[2]);
                foreach (var s in porStatus)
                {
                    List<int> objs = s.Select(a => a.Key).ToList();
                    List<int> props = SemStatus(pset);
                    props.Add(Prop(s.Key));
                    NovoPset(nome, props, objs, s.Key);
                    if (!mover.TryGetValue(pset, out var sai)) mover[pset] = sai = new HashSet<int>();
                    sai.UnionWith(objs);
                    gravados += objs.Count;
                }
            }
            foreach (StepEntity rel in rels)
            {
                int def = StepModel.Ref(rel.Args[5]);
                if (!mover.TryGetValue(def, out var sai)) continue;
                List<int> ficam = StepModel.RefList(rel.Args[4]).Where(o => !sai.Contains(o)).ToList();
                if (ficam.Count > 0) m.SetArg(rel.Id, 4, StepModel.RefListText(ficam));
                else m.Remove(rel.Id);
            }
            foreach (int pset in mover.Keys)
                if (!m.OfType("IFCRELDEFINESBYPROPERTIES").Any(r => StepModel.Ref(r.Args[5]) == pset)) m.Remove(pset);

            // Elementos sem o property set: um novo para cada (property set, status).
            foreach (var g in alvo.Where(a => !psetDoObjeto.ContainsKey(a.Key)).GroupBy(a => a.Value))
            {
                List<int> objs = g.Select(a => a.Key).OrderBy(i => i).ToList();
                NovoPset(g.Key.Pset, new[] { Prop(g.Key.Status) }, objs, g.Key.Status);
                gravados += objs.Count;
            }
            return gravados;
        }

        /// <summary>Texto STEP: aspas duplicadas, barra invertida escapada, não-ASCII em \X2\.</summary>
        private static string Texto(string text)
        {
            var sb = new StringBuilder("'");
            foreach (char c in text ?? "")
            {
                if (c == '\'') sb.Append("''");
                else if (c == '\\') sb.Append("\\\\");
                else if (c >= 32 && c <= 126) sb.Append(c);
                else sb.Append("\\X2\\").Append(((int)c).ToString("X4")).Append("\\X0\\");
            }
            return sb.Append('\'').ToString();
        }

        private static string Guid(string key) =>
            "'" + CompressGuid(MD5.HashData(Encoding.UTF8.GetBytes("MetroSP|status-fase|" + key))) + "'";

        private const string Base64Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

        /// <summary>16 bytes no formato de 22 caracteres do IFC.</summary>
        private static string CompressGuid(byte[] bytes16)
        {
            var n = Compat.InteiroSemSinal(bytes16);
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
