using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MetroSP.Ifc
{
    /// <summary>Uma peça de ligação metálica (parafuso, chapa, solda...) levada ao IFC como elemento próprio.</summary>
    public class PecaAco
    {
        /// <summary>ElementId do elemento do Revit de onde a peça veio (a ligação ou o elemento de fabricação avulso).</summary>
        public long HostId { get; set; }
        /// <summary>Chave estável da peça (UniqueId do subelemento + número da peça), para o GlobalId não mudar a cada exportação.</summary>
        public string Chave { get; set; }
        /// <summary>Parafuso, Chumbador, Conector de cisalhamento, Chapa, Solda, Perfil.</summary>
        public string Peca { get; set; }
        public string Nome { get; set; }
        /// <summary>Tipo (ObjectType): nome do tipo no Revit ou norma.</summary>
        public string Tipo { get; set; }
        /// <summary>Nome do tipo da ligação, quando a peça vem de uma ligação.</summary>
        public string Ligacao { get; set; }
        public string Norma { get; set; }
        public string Classe { get; set; }
        /// <summary>Diâmetro nominal em metros.</summary>
        public double? Diametro { get; set; }
        /// <summary>Comprimento nominal em metros.</summary>
        public double? Comprimento { get; set; }
        /// <summary>true para parafuso e chumbador (Pset_MechanicalFastenerBolt).</summary>
        public bool EhParafuso { get; set; }
        /// <summary>true se a ligação foi toda separada em peças: o corpo único dela sai do IFC, para não duplicar.</summary>
        public bool TirarCorpoDoHost { get; set; }
        /// <summary>Demais parâmetros do Revit (subelemento e tipo), já como texto.</summary>
        public Dictionary<string, string> Propriedades { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Completa no IFC as peças das ligações metálicas que a MetroSP mandou ao exportador como formas temporárias
    /// (ver Ids.AcoIfc): GlobalId estável, nome, diâmetro e comprimento nominais do IfcMechanicalFastener,
    /// Pset_MechanicalFastenerBolt e Pset MetroSP_LigacaoMetalica, e cada peça agregada à sua ligação (IfcRelAggregates).
    /// As peças são achadas pelo Tag (o ElementId da forma temporária, que o exportador grava).
    /// </summary>
    public static class FixadoresIfc
    {
        public const string Pset = "MetroSP_LigacaoMetalica";

        public class Resultado
        {
            public int Pecas, Ligacoes, Parafusos, Chapas, Soldas, Outras;
        }

        private static readonly string[] Produtos =
        {
            "IFCMECHANICALFASTENER", "IFCFASTENER", "IFCPLATE", "IFCPLATESTANDARDCASE", "IFCMEMBER", "IFCMEMBERSTANDARDCASE",
            "IFCELEMENTASSEMBLY", "IFCBUILDINGELEMENTPROXY", "IFCDISCRETEACCESSORY", "IFCBEAM", "IFCBEAMSTANDARDCASE",
            "IFCCOLUMN", "IFCCOLUMNSTANDARDCASE",
        };

        public static Resultado Aplicar(StepModel m, IReadOnlyDictionary<long, PecaAco> pecas, Action<string> log)
        {
            log ??= _ => { };
            var r = new Resultado();
            if (pecas == null || pecas.Count == 0) return r;
            if (m.Schema != null && m.Schema.StartsWith("IFC2X3", StringComparison.OrdinalIgnoreCase)) return r;
            double fator = FatorComprimento(m);

            // Elementos do IFC pelo Tag: as peças (Tag = forma temporária) e as ligações (Tag = ElementId do Revit).
            var hostIds = new HashSet<long>(pecas.Values.Select(p => p.HostId));
            var entidadeDaPeca = new Dictionary<int, PecaAco>();
            var entidadeDoHost = new Dictionary<long, int>();
            foreach (string tipo in Produtos)
                foreach (StepEntity e in m.OfType(tipo))
                {
                    if (e.Args.Count < 8 || !long.TryParse(StepModel.Text(e.Args[7])?.Trim(), out long tag)) continue;
                    if (pecas.TryGetValue(tag, out PecaAco p)) entidadeDaPeca[e.Id] = p;
                    else if (hostIds.Contains(tag) && !entidadeDoHost.ContainsKey(tag)) entidadeDoHost[tag] = e.Id;
                }
            if (entidadeDaPeca.Count == 0)
            {
                log($"Ligações metálicas: as {pecas.Count} peças preparadas não saíram no IFC (confira a fase e a vista da exportação).");
                return r;
            }

            string historico = entidadeDaPeca.Keys.Select(id => m.Get(id).Args[1]).FirstOrDefault(a => a != "$") ?? "$";

            // Atributos de cada peça
            foreach (var kv in entidadeDaPeca)
            {
                StepEntity e = m.Get(kv.Key);
                PecaAco p = kv.Value;
                m.SetArg(e.Id, 0, Guid("peca|" + p.Chave));
                m.SetArg(e.Id, 2, S(p.Nome));
                m.SetArg(e.Id, 4, Opt(p.Tipo));
                m.SetArg(e.Id, 7, S(p.HostId.ToString(CultureInfo.InvariantCulture)));
                if (e.Type == "IFCMECHANICALFASTENER" && e.Args.Count >= 11)
                {
                    if (p.Diametro > 0) m.SetArg(e.Id, 8, Num(p.Diametro.Value * fator));
                    if (p.Comprimento > 0) m.SetArg(e.Id, 9, Num(p.Comprimento.Value * fator));
                }
                switch (e.Type)
                {
                    case "IFCMECHANICALFASTENER": r.Parafusos++; break;
                    case "IFCPLATE": case "IFCPLATESTANDARDCASE": r.Chapas++; break;
                    case "IFCFASTENER": r.Soldas++; break;
                    default: r.Outras++; break;
                }
                r.Pecas++;
            }

            // Property sets: propriedades e property sets iguais são compartilhados (milhares de parafusos iguais).
            var propriedades = new Dictionary<string, int>(StringComparer.Ordinal);
            int Prop(string nome, string valor)
            {
                string k = nome + "\u0001" + valor;
                if (!propriedades.TryGetValue(k, out int id))
                    propriedades[k] = id = m.Add("IFCPROPERTYSINGLEVALUE", S(nome), "$", valor, "$");
                return id;
            }
            var psets = new Dictionary<string, (int Pset, List<int> Objetos)>(StringComparer.Ordinal);
            void Ligar(string nomePset, List<(string Nome, string Valor)> props, int objeto)
            {
                if (props.Count == 0) return;
                string k = nomePset + "\u0002" + string.Join("\u0002", props.Select(x => x.Nome + "\u0001" + x.Valor));
                if (!psets.TryGetValue(k, out var ps))
                {
                    var ids = props.Select(x => Prop(x.Nome, x.Valor)).ToList();
                    int pset = m.Add("IFCPROPERTYSET", Guid("pset|" + k), historico, S(nomePset), "$", StepModel.RefListText(ids));
                    psets[k] = ps = (pset, new List<int>());
                }
                ps.Objetos.Add(objeto);
            }
            foreach (var kv in entidadeDaPeca.OrderBy(k => k.Key))
            {
                PecaAco p = kv.Value;
                var metro = new List<(string, string)>();
                void Add(string nome, string valor) { if (valor != null && !metro.Any(x => string.Equals(x.Item1, nome, StringComparison.OrdinalIgnoreCase))) metro.Add((nome, valor)); }
                Add("Peca", Label(p.Peca));
                Add("Ligacao", Label(p.Ligacao));
                Add("Norma", Label(p.Norma));
                Add("Classe", Label(p.Classe));
                if (p.Diametro > 0) Add("Diametro", "IFCPOSITIVELENGTHMEASURE(" + Num(p.Diametro.Value * fator) + ")");
                if (p.Comprimento > 0) Add("Comprimento", "IFCPOSITIVELENGTHMEASURE(" + Num(p.Comprimento.Value * fator) + ")");
                foreach (var x in p.Propriedades.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                    if (!string.IsNullOrWhiteSpace(x.Value)) Add(x.Key, Label(x.Value));
                Ligar(Pset, metro, kv.Key);

                if (p.EhParafuso && p.Diametro > 0)
                    Ligar("Pset_MechanicalFastenerBolt", new List<(string, string)> { ("ThreadDiameter", "IFCPOSITIVELENGTHMEASURE(" + Num(p.Diametro.Value * fator) + ")") }, kv.Key);
            }
            foreach (var ps in psets)
                m.Add("IFCRELDEFINESBYPROPERTIES", Guid("rel|" + ps.Key), historico, "$", "$",
                    StepModel.RefListText(ps.Value.Objetos), "#" + ps.Value.Pset);

            // Cada peça agregada à sua ligação, quando a ligação está no IFC. A peça deixa de estar contida no pavimento
            // (no IFC, a parte segue a estrutura espacial do todo).
            var agregadas = new HashSet<int>();
            foreach (var g in entidadeDaPeca.GroupBy(k => k.Value.HostId))
            {
                if (!entidadeDoHost.TryGetValue(g.Key, out int host)) continue;
                List<int> partes = g.Select(k => k.Key).OrderBy(i => i).ToList();
                m.Add("IFCRELAGGREGATES", Guid("agrega|" + g.First().Value.Chave), historico, S("Peças da ligação"), "$",
                    "#" + host, StepModel.RefListText(partes));
                agregadas.UnionWith(partes);
                if (g.Any(k => k.Value.TirarCorpoDoHost) && m.Get(host).Args.Count > 6) m.SetArg(host, 6, "$");
                r.Ligacoes++;
            }
            if (agregadas.Count > 0)
                foreach (StepEntity rel in m.OfType("IFCRELCONTAINEDINSPATIALSTRUCTURE").ToList())
                {
                    List<int> elems = StepModel.RefList(rel.Args[4]);
                    if (!elems.Any(agregadas.Contains)) continue;
                    List<int> ficam = elems.Where(i => !agregadas.Contains(i)).ToList();
                    if (ficam.Count == 0) m.Remove(rel.Id);
                    else m.SetArg(rel.Id, 4, StepModel.RefListText(ficam));
                }

            var partesTexto = new List<string>();
            if (r.Parafusos > 0) partesTexto.Add($"{r.Parafusos} parafuso(s) e fixador(es)");
            if (r.Chapas > 0) partesTexto.Add($"{r.Chapas} chapa(s)");
            if (r.Soldas > 0) partesTexto.Add($"{r.Soldas} solda(s)");
            if (r.Outras > 0) partesTexto.Add($"{r.Outras} outra(s) peça(s)");
            log("Ligações metálicas: " + string.Join(", ", partesTexto) + " como elementos próprios" +
                (r.Ligacoes > 0 ? $", agrupados em {r.Ligacoes} ligação(ões)." : "."));
            int faltaram = pecas.Count - entidadeDaPeca.Count;
            if (faltaram > 0) log($"Ligações metálicas: {faltaram} peça(s) preparada(s) não saíram no IFC.");
            return r;
        }

        /// <summary>Quantas unidades de comprimento do IFC há em um metro (IfcSIUnit com prefixo, ou pé/polegada).</summary>
        public static double FatorComprimento(StepModel m)
        {
            foreach (StepEntity u in m.OfType("IFCSIUNIT"))
            {
                if (u.Args.Count < 4 || u.Args[1] != ".LENGTHUNIT.") continue;
                switch (u.Args[2])
                {
                    case ".MILLI.": return 1000;
                    case ".CENTI.": return 100;
                    case ".DECI.": return 10;
                    case ".KILO.": return 0.001;
                    default: return 1;
                }
            }
            foreach (StepEntity u in m.OfType("IFCCONVERSIONBASEDUNIT"))
            {
                if (u.Args.Count < 4 || u.Args[1] != ".LENGTHUNIT.") continue;
                string nome = (StepModel.Text(u.Args[2]) ?? "").ToUpperInvariant();
                if (nome.Contains("INCH")) return 1 / 0.0254;
                if (nome.Contains("FOOT") || nome.Contains("FEET")) return 1 / 0.3048;
            }
            return 1000;
        }

        private static string Label(string s) => string.IsNullOrWhiteSpace(s) ? null : "IFCLABEL(" + S(s.Trim()) + ")";

        private static string Num(double v)
        {
            string s = Math.Round(v, 6).ToString("0.0#####", CultureInfo.InvariantCulture);
            return s;
        }

        private static string Opt(string s) => string.IsNullOrWhiteSpace(s) ? "$" : S(s);

        private static string Guid(string key)
        {
            using (var md5 = MD5.Create())
                return S(CompressGuid(md5.ComputeHash(Encoding.UTF8.GetBytes("MetroSP|aco|" + key))));
        }

        /// <summary>Texto STEP: aspas duplicadas, barra invertida escapada, não-ASCII em \X2\.</summary>
        internal static string S(string text)
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

        /// <summary>16 bytes no formato de 22 caracteres do IFC (primeiro caractere de 2 bits, depois 6 bits cada).</summary>
        private static string CompressGuid(byte[] b)
        {
            var chars = new char[22];
            // 128 bits: 2 bits no primeiro caractere e 126 bits em 21 caracteres de 6 bits.
            ulong hi = 0, lo = 0;
            for (int i = 0; i < 8; i++) hi = (hi << 8) | b[i];
            for (int i = 8; i < 16; i++) lo = (lo << 8) | b[i];
            for (int i = 21; i >= 0; i--)
            {
                chars[i] = Base64Chars[(int)(lo & 63)];
                lo = (lo >> 6) | ((hi & 63) << 58);
                hi >>= 6;
            }
            return new string(chars);
        }
    }
}
