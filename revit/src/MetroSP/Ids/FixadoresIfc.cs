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
        /// <summary>Em quantas peças o item do Revit foi separado (cantoneiras de um perfil, parafusos de um grupo).</summary>
        public int PecasNoItem { get; set; } = 1;
        /// <summary>Volume da peça em m³ (dos sólidos dela), para o peso quando o Revit só dá o peso do item inteiro.</summary>
        public double Volume { get; set; }
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
            public int Pecas, Ligacoes, Conjuntos, Parafusos, Chapas, Soldas, Outras, ComPeso, Materiais;
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
                    string tipoIfc = TipoFixador(p.Peca);
                    if (tipoIfc != null && (e.Args[10] == "$" || e.Args[10] == ".NOTDEFINED.")) m.SetArg(e.Id, 10, tipoIfc);
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
            double fatorMassa = FatorMassa(m);
            var pesoDaEntidade = new Dictionary<int, double>();
            foreach (var kv in entidadeDaPeca.OrderBy(k => k.Key))
            {
                PecaAco p = kv.Value;
                var metro = new List<(string, string)>();
                void Add(string nome, string valor) { if (valor != null && !metro.Any(x => string.Equals(x.Item1, nome, StringComparison.OrdinalIgnoreCase))) metro.Add((nome, valor)); }
                // Só o que o padrão IFC não cobre: diâmetro e comprimento vão nos atributos do IfcMechanicalFastener,
                // espessura e pesos no Qto, a norma e a classe também no material.
                Add("Peca", Label(p.Peca));
                Add("Ligacao", Label(p.Ligacao));
                Add("Norma", Label(p.Norma));
                Add("Classe", Label(p.Classe));
                Add("Descricao", Label(Pegar(p, "Descricao Complementar", "Descrição Complementar", "Description", "Descrição")));
                Add("Perfil", Label(Pegar(p, "Profile Type", "Tipo de perfil", "Perfil")));
                if (!p.EhParafuso) Add("Tipo", Label(Pegar(p, "Type", "Tipo") ?? p.Tipo));
                if (!p.EhParafuso) Add("Material", Label(Pegar(p, "Structural Material", "Material estrutural", "Material")));
                string revestimento = Pegar(p, "Coating", "Revestimento");
                if (revestimento != null && revestimento.Trim('-', ' ').Length > 0) Add("Revestimento", Label(revestimento));
                if (p.EhParafuso && Numero(Pegar(p, "Grip Length", "Comprimento de aperto")) is double aperto && aperto > 0)
                    Add("ComprimentoDeAperto", "IFCPOSITIVELENGTHMEASURE(" + Num(aperto / 1000 * fator) + ")");
                double? peso = Peso(p);
                if (peso > 0)
                {
                    pesoDaEntidade[kv.Key] = peso.Value;
                    Add("Peso", "IFCMASSMEASURE(" + Num(peso.Value * fatorMassa) + ")");
                    r.ComPeso++;
                }
                Ligar(Pset, metro, kv.Key);

                if (p.EhParafuso && p.Diametro > 0)
                    Ligar("Pset_MechanicalFastenerBolt", new List<(string, string)> { ("ThreadDiameter", "IFCPOSITIVELENGTHMEASURE(" + Num(p.Diametro.Value * fator) + ")") }, kv.Key);
            }
            CompletarQto(m, entidadeDaPeca, pesoDaEntidade, fator, fatorMassa, historico);
            r.Materiais = MaterialDosParafusos(m, entidadeDaPeca, historico);

            // Cada peça agregada à sua ligação, quando a ligação está no IFC. A peça deixa de estar contida no pavimento
            // (no IFC, a parte segue a estrutura espacial do todo).
            // Quando a ligação não está no IFC (o exportador não leva a ligação do Revit), as peças dela são agrupadas
            // num IfcElementAssembly novo, contido no pavimento onde as peças estavam.
            var agregadas = new HashSet<int>();
            var contencaoDe = new Dictionary<int, int>();
            foreach (StepEntity rel in m.OfType("IFCRELCONTAINEDINSPATIALSTRUCTURE"))
                foreach (int i in StepModel.RefList(rel.Args[4])) if (!contencaoDe.ContainsKey(i)) contencaoDe[i] = rel.Id;
            foreach (var g in entidadeDaPeca.GroupBy(k => k.Value.HostId))
            {
                List<int> partes = g.Select(k => k.Key).OrderBy(i => i).ToList();
                bool novo = false;
                if (!entidadeDoHost.TryGetValue(g.Key, out int host))
                {
                    PecaAco p0 = g.First().Value;
                    if (partes.Count < 2 && string.IsNullOrWhiteSpace(p0.Ligacao)) continue;
                    host = CriarConjunto(m, g.Key, p0.Ligacao, partes[0], historico);
                    if (contencaoDe.TryGetValue(partes[0], out int relPav))
                        m.SetArg(relPav, 4, StepModel.RefListText(StepModel.RefList(m.Get(relPav).Args[4]).Concat(new[] { host })));
                    Ligar(Pset, new List<(string, string)> { ("Peca", Label("Ligação")), ("Ligacao", Label(p0.Ligacao)) }.Where(x => x.Item2 != null).ToList(), host);
                    novo = true;
                    r.Conjuntos++;
                }
                m.Add("IFCRELAGGREGATES", Guid("agrega|" + g.First().Value.Chave), historico, S("Peças da ligação"), "$",
                    "#" + host, StepModel.RefListText(partes));
                agregadas.UnionWith(partes);
                if (!novo && g.Any(k => k.Value.TirarCorpoDoHost) && m.Get(host).Args.Count > 6) m.SetArg(host, 6, "$");
                if (!novo) r.Ligacoes++;
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
            foreach (var ps in psets)
                m.Add("IFCRELDEFINESBYPROPERTIES", Guid("rel|" + ps.Key), historico, "$", "$",
                    StepModel.RefListText(ps.Value.Objetos), "#" + ps.Value.Pset);

            var partesTexto = new List<string>();
            if (r.Parafusos > 0) partesTexto.Add($"{r.Parafusos} parafuso(s) e fixador(es)");
            if (r.Chapas > 0) partesTexto.Add($"{r.Chapas} chapa(s)");
            if (r.Soldas > 0) partesTexto.Add($"{r.Soldas} solda(s)");
            if (r.Outras > 0) partesTexto.Add($"{r.Outras} outra(s) peça(s)");
            int grupos = r.Ligacoes + r.Conjuntos;
            log("Ligações metálicas: " + string.Join(", ", partesTexto) + " como elementos próprios" +
                (grupos > 0 ? $", agrupados em {grupos} ligação(ões) (IfcElementAssembly)." : "."));
            if (r.ComPeso > 0) log($"Ligações metálicas: peso em {r.ComPeso} peça(s) (Qto e Pset {Pset}).");
            if (r.Materiais > 0) log($"Ligações metálicas: material dos parafusos pela norma e classe em {r.Materiais} parafuso(s).");
            int faltaram = pecas.Count - entidadeDaPeca.Count;
            if (faltaram > 0) log($"Ligações metálicas: {faltaram} peça(s) preparada(s) não saíram no IFC.");
            return r;
        }

        private static string TipoFixador(string peca)
        {
            switch (peca)
            {
                case "Parafuso": return ".BOLT.";
                case "Chumbador": return ".ANCHORBOLT.";
                case "Conector de cisalhamento": return ".SHEARCONNECTOR.";
                default: return null;
            }
        }

        /// <summary>Primeiro parâmetro do Revit, entre os nomes dados, que tem valor.</summary>
        private static string Pegar(PecaAco p, params string[] nomes)
        {
            foreach (string n in nomes)
                if (p.Propriedades.TryGetValue(n, out string v) && !string.IsNullOrWhiteSpace(v)) return v.Trim();
            return null;
        }

        /// <summary>Número de um texto do Revit ("2,2416", "12,5 mm", "1.234,5"), sem a unidade.</summary>
        internal static double? Numero(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return null;
            var mt = System.Text.RegularExpressions.Regex.Match(texto.Trim(), @"^-?[0-9][0-9.,]*");
            if (!mt.Success) return null;
            string n = mt.Value;
            if (n.Contains(',') && n.Contains('.')) n = n.Replace(".", "");
            n = n.Replace(',', '.');
            return double.TryParse(n, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : (double?)null;
        }

        private const double DensidadeAco = 7850;   // kg/m³

        /// <summary>
        /// Peso de uma peça, em kg. Parafuso: peso total do grupo dividido pelo número de parafusos. Chapa e perfil:
        /// o peso do Revit quando o item é uma peça só; quando o item foi separado (perfil com 2 ou 4 cantoneiras),
        /// o volume da peça vezes 7850 kg/m³, porque o peso do Revit não diz a qual cantoneira se refere.
        /// </summary>
        internal static double? Peso(PecaAco p)
        {
            if (p.EhParafuso)
            {
                double? total = Numero(Pegar(p, "Total Weight", "Peso total"));
                double? n = Numero(Pegar(p, "Number", "Número", "Quantidade"));
                if (total > 0 && n >= 1 && p.PecasNoItem > 1) return total / n;
                if (total > 0 && p.PecasNoItem <= 1) return total;
            }
            else if (p.PecasNoItem <= 1)
            {
                double? w = Numero(Pegar(p, "Exact Weight", "Peso exato", "Weight", "Peso"));
                if (w > 0) return w;
            }
            return p.Volume > 0 ? p.Volume * DensidadeAco : (double?)null;
        }

        /// <summary>Quantos kg há na unidade de massa do IFC (1 para kg, 1000 para g).</summary>
        private static double FatorMassa(StepModel m)
        {
            foreach (StepEntity u in m.OfType("IFCSIUNIT"))
                if (u.Args.Count >= 4 && u.Args[1] == ".MASSUNIT.") return u.Args[2] == ".KILO." ? 1 : u.Args[2] == "$" ? 1000 : 1;
            return 1;
        }

        /// <summary>
        /// Qto_PlateBaseQuantities (Width = espessura, GrossWeight, NetWeight) e Qto_MemberBaseQuantities (pesos)
        /// das peças. Um Qto compartilhado com outro elemento é copiado antes de mudar.
        /// </summary>
        private static void CompletarQto(StepModel m, Dictionary<int, PecaAco> pecas, Dictionary<int, double> pesos,
            double fator, double fatorMassa, string historico)
        {
            var qtoDe = new Dictionary<int, (int Rel, int Qto)>();
            var objetosDoRel = new Dictionary<int, int>();
            foreach (StepEntity rel in m.OfType("IFCRELDEFINESBYPROPERTIES"))
            {
                int qto = StepModel.Ref(rel.Args[5]);
                if (qto <= 0 || m.TypeOf(qto) != "IFCELEMENTQUANTITY") continue;
                string nome = StepModel.Text(m.Get(qto).Args[2]) ?? "";
                if (!nome.StartsWith("Qto_", StringComparison.OrdinalIgnoreCase)) continue;
                List<int> objs = StepModel.RefList(rel.Args[4]);
                foreach (int o in objs) if (pecas.ContainsKey(o) && !qtoDe.ContainsKey(o)) qtoDe[o] = (rel.Id, qto);
            }
            var usoDoQto = qtoDe.Values.GroupBy(x => x.Qto).ToDictionary(g => g.Key, g => g.Count());

            foreach (var kv in pecas.OrderBy(k => k.Key))
            {
                string tipo = m.TypeOf(kv.Key);
                bool chapa = tipo == "IFCPLATE" || tipo == "IFCPLATESTANDARDCASE";
                bool perfil = tipo == "IFCMEMBER" || tipo == "IFCMEMBERSTANDARDCASE";
                if (!chapa && !perfil) continue;
                var novas = new List<(string Nome, string Entidade, string Valor)>();
                if (chapa && Numero(Pegar(kv.Value, "Thickness", "Espessura")) is double esp && esp > 0)
                    novas.Add(("Width", "IFCQUANTITYLENGTH", Num(esp / 1000 * fator)));
                if (pesos.TryGetValue(kv.Key, out double peso))
                {
                    novas.Add(("GrossWeight", "IFCQUANTITYWEIGHT", Num(peso * fatorMassa)));
                    novas.Add(("NetWeight", "IFCQUANTITYWEIGHT", Num(peso * fatorMassa)));
                }
                if (novas.Count == 0) continue;

                List<int> atuais;
                int qtoId;
                if (qtoDe.TryGetValue(kv.Key, out var q))
                {
                    StepEntity qto = m.Get(q.Qto);
                    atuais = StepModel.RefList(qto.Args[5]);
                    var nomes = new HashSet<string>(atuais.Select(i => StepModel.Text(m.Get(i).Args[0]) ?? ""), StringComparer.OrdinalIgnoreCase);
                    novas = novas.Where(x => !nomes.Contains(x.Nome)).ToList();
                    if (novas.Count == 0) continue;
                    var ids = atuais.Concat(novas.Select(x => m.Add(x.Entidade, S(x.Nome), "$", "$", x.Valor, "$"))).ToList();
                    if (usoDoQto[q.Qto] == 1) { m.SetArg(q.Qto, 5, StepModel.RefListText(ids)); continue; }
                    // Qto compartilhado: esta peça ganha uma cópia.
                    List<int> objs = StepModel.RefList(m.Get(q.Rel).Args[4]).Where(o => o != kv.Key).ToList();
                    m.SetArg(q.Rel, 4, StepModel.RefListText(objs));
                    usoDoQto[q.Qto]--;
                    qtoId = m.Add("IFCELEMENTQUANTITY", Guid("qto|" + kv.Value.Chave), historico, qto.Args[2], "$", qto.Args[4], StepModel.RefListText(ids));
                }
                else
                {
                    var ids = novas.Select(x => m.Add(x.Entidade, S(x.Nome), "$", "$", x.Valor, "$")).ToList();
                    qtoId = m.Add("IFCELEMENTQUANTITY", Guid("qto|" + kv.Value.Chave), historico,
                        S(chapa ? "Qto_PlateBaseQuantities" : "Qto_MemberBaseQuantities"), "$", S("BaseQuantities"), StepModel.RefListText(ids));
                }
                m.Add("IFCRELDEFINESBYPROPERTIES", Guid("relqto|" + kv.Value.Chave), historico, "$", "$", "(#" + kv.Key + ")", "#" + qtoId);
            }
        }

        /// <summary>
        /// Material dos parafusos pela norma e classe do Revit (ex.: "ASTM A325 - 8.8"), no lugar do material da peça
        /// ligada que o exportador costuma dar. Devolve quantos parafusos mudaram.
        /// </summary>
        private static int MaterialDosParafusos(StepModel m, Dictionary<int, PecaAco> pecas, string historico)
        {
            var porMaterial = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            foreach (var kv in pecas.OrderBy(k => k.Key))
            {
                if (!kv.Value.EhParafuso) continue;
                string nome = string.Join(" - ", new[] { kv.Value.Norma, kv.Value.Classe }
                    .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase));
                if (nome.Length == 0) continue;
                if (!porMaterial.TryGetValue(nome, out var lista)) porMaterial[nome] = lista = new List<int>();
                lista.Add(kv.Key);
            }
            if (porMaterial.Count == 0) return 0;
            var mudam = new HashSet<int>(porMaterial.Values.SelectMany(x => x));
            foreach (StepEntity rel in m.OfType("IFCRELASSOCIATESMATERIAL").ToList())
            {
                List<int> objs = StepModel.RefList(rel.Args[4]);
                if (!objs.Any(mudam.Contains)) continue;
                List<int> ficam = objs.Where(o => !mudam.Contains(o)).ToList();
                if (ficam.Count == 0) m.Remove(rel.Id);
                else m.SetArg(rel.Id, 4, StepModel.RefListText(ficam));
            }
            var existentes = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (StepEntity mat in m.OfType("IFCMATERIAL"))
            {
                string n = StepModel.Text(mat.Args[0]);
                if (n != null && !existentes.ContainsKey(n)) existentes[n] = mat.Id;
            }
            foreach (var kv in porMaterial)
            {
                if (!existentes.TryGetValue(kv.Key, out int mat))
                    mat = m.Add("IFCMATERIAL", S(kv.Key), "$", S("Steel"));
                m.Add("IFCRELASSOCIATESMATERIAL", Guid("material|" + kv.Key), historico, "$", "$", StepModel.RefListText(kv.Value), "#" + mat);
            }
            return mudam.Count;
        }

        /// <summary>IfcElementAssembly da ligação, posicionado como a primeira peça dela.</summary>
        private static int CriarConjunto(StepModel m, long hostId, string ligacao, int primeiraPeca, string historico)
        {
            string local = "$";
            int lp = StepModel.Ref(m.Get(primeiraPeca).Args[5]);
            if (lp > 0 && m.TypeOf(lp) == "IFCLOCALPLACEMENT")
            {
                StepEntity pl = m.Get(lp);
                local = "#" + m.Add("IFCLOCALPLACEMENT", pl.Args[0], pl.Args[1]);
            }
            string tag = hostId.ToString(CultureInfo.InvariantCulture);
            return m.Add("IFCELEMENTASSEMBLY", Guid("ligacao|" + tag), historico, S(string.IsNullOrWhiteSpace(ligacao) ? "Ligação" : ligacao.Trim()),
                "$", S("Ligação metálica"), local, "$", S(tag), ".NOTDEFINED.", ".USERDEFINED.");
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
