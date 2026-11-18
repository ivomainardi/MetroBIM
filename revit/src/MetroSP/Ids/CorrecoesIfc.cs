using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MetroSP.Ifc
{
    /// <summary>De onde sai cada quantidade calculada pelo Revit.</summary>
    [Flags]
    public enum FonteQto
    {
        Nenhuma = 0,
        Volume = 1, Area = 2, AreaSuperficie = 4, Comprimento = 8, Largura = 16, Altura = 32, Espessura = 64, Peso = 128,
        /// <summary>Área da seção: volume ÷ comprimento.</summary>
        Secao = 256,
    }

    /// <summary>Quantidades de um elemento do Revit, em unidades SI (m, m², m³, kg). null = não deu para saber.</summary>
    public sealed class QuantidadesRevit
    {
        public double? Volume, Area, AreaSuperficie, Comprimento, Largura, Altura, Espessura, Peso;

        public double? Valor(FonteQto f)
        {
            switch (f)
            {
                case FonteQto.Volume: return Volume;
                case FonteQto.Area: return Area;
                case FonteQto.AreaSuperficie: return AreaSuperficie;
                case FonteQto.Comprimento: return Comprimento;
                case FonteQto.Largura: return Largura;
                case FonteQto.Altura: return Altura;
                case FonteQto.Espessura: return Espessura;
                case FonteQto.Peso: return Peso;
                case FonteQto.Secao: return Volume > 0 && Comprimento > 0 ? Volume / Comprimento : null;
                default: return null;
            }
        }
    }

    /// <summary>Tela soldada (FabricSheet) do Revit, em m, m² e kg.</summary>
    public sealed class TelaRevit
    {
        public double Comprimento, Largura, DiametroLongitudinal, DiametroTransversal, EspacamentoLongitudinal, EspacamentoTransversal, Peso;
    }

    /// <summary>Sistema MEP do Revit (tubulação, dutos ou circuito elétrico).</summary>
    public sealed class SistemaRevit
    {
        public long Id;
        public string Nome;
        /// <summary>Nome do tipo de sistema (vai no ObjectType e no LongName).</summary>
        public string Tipo;
        /// <summary>Valor do IfcDistributionSystemEnum (null = USERDEFINED).</summary>
        public string Predefinido;
    }

    /// <summary>Material do Revit e a fração do volume do elemento.</summary>
    public sealed class MaterialRevit
    {
        public string Nome;
        public double Fracao;
    }

    /// <summary>O que o pós-processamento pergunta ao Revit. Cada função recebe o ElementId (Tag do IFC).</summary>
    public sealed class FonteRevit
    {
        /// <summary>
        /// Quantidades do elemento. precisa: as que faltam no IFC (pode ler a geometria se o parâmetro não der);
        /// conferir: as que já estão no IFC e só são comparadas (só pelos parâmetros, sem ler a geometria).
        /// </summary>
        public Func<long, FonteQto, FonteQto, QuantidadesRevit> Quantidades;
        /// <summary>Classe pedida para os elementos que saíram como IfcBuildingElementProxy: Tag → "IfcCovering.CLADDING".</summary>
        public IReadOnlyDictionary<long, string> ClassesPedidas;
        /// <summary>(classe, PredefinedType) vale no esquema do arquivo?</summary>
        public Func<string, string, bool> TipoValido;
        public Func<long, TelaRevit> Tela;
        public Func<long, IReadOnlyList<SistemaRevit>> Sistemas;
        public Func<long, IReadOnlyList<MaterialRevit>> Materiais;
        /// <summary>Propriedades do MetroQto pedidas para cada classe IFC (maiúsculas, sem o PredefinedType).</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> MetroQto;
    }

    /// <summary>
    /// Correções do IFC exportado pelo Revit que não dependem de preparar o modelo antes da exportação:
    /// <list type="number">
    /// <item>Classe IFC: o que saiu como IfcBuildingElementProxy apesar do "Exportar para IFC como" volta para a classe pedida.</item>
    /// <item>Escadas e rampas: componentes que saíram como IfcSlab (sem ser patamar) dentro de IfcStair/IfcRamp viram
    /// IfcStairFlight/IfcRampFlight.</item>
    /// <item>Telas soldadas: dimensões, diâmetros e espaçamentos do IfcReinforcingMesh e Qto de peso, pelo Revit.</item>
    /// <item>Quantidades base: Qto_*BaseQuantities que faltam ou saíram zerados, pelas quantidades do Revit.</item>
    /// <item>MetroQto: as mesmas quantidades nas propriedades do MetroQto que o MetroDATA pede e o exportador não gravou.</item>
    /// <item>Sistemas MEP: elementos de distribuição fora de qualquer sistema entram no IfcDistributionSystem do Revit.</item>
    /// <item>Materiais: elementos sem material (nem no tipo) recebem o material do Revit.</item>
    /// </list>
    /// Tudo numa passada pelo arquivo já carregado: o Revit só é consultado nos elementos que precisam de correção.
    /// </summary>
    public static partial class CorrecoesIfc
    {
        public sealed class Resultado
        {
            public int Classes, Lances, Telas, QtoCompletados, QtoCorrigidos, MetroQto, Sistemas, NoSistema, Materiais;
            public readonly List<string> SemClasse = new List<string>();
            public readonly Dictionary<string, int> QtoPorClasse = new Dictionary<string, int>(StringComparer.Ordinal);
            /// <summary>Valores trocados (classe, Tag, quantidade, antes, depois), para o registro.</summary>
            public readonly List<string> Trocas = new List<string>();
            public readonly List<string> Avisos = new List<string>();
            /// <summary>Tempo de cada etapa, para o registro e para medir.</summary>
            public readonly List<(string Etapa, TimeSpan Tempo)> Tempos = new List<(string, TimeSpan)>();
            public bool Alterou => Classes + Lances + Telas + QtoCompletados + QtoCorrigidos + MetroQto + NoSistema + Materiais > 0;

            public IEnumerable<string> Linhas()
            {
                if (Classes > 0) yield return $"Classe IFC: {Classes} elemento(s) que saíram como IfcBuildingElementProxy voltaram para a classe do \"Exportar para IFC como\".";
                if (SemClasse.Count > 0) yield return $"Classe IFC: {SemClasse.Count} proxy(s) com classe pedida que o MetroSP não troca ({string.Join(", ", SemClasse.Distinct().Take(4))}).";
                if (Lances > 0) yield return $"Escadas e rampas: {Lances} lance(s) que saíram como IfcSlab viraram IfcStairFlight/IfcRampFlight.";
                if (Telas > 0) yield return $"Telas soldadas: dimensões, diâmetros, espaçamentos e peso em {Telas} IfcReinforcingMesh.";
                if (QtoCompletados + QtoCorrigidos > 0)
                    yield return $"Quantidades base: {QtoCompletados} valor(es) completados e {QtoCorrigidos} corrigido(s) pelo Revit (" +
                                 string.Join(", ", QtoPorClasse.OrderByDescending(k => k.Value).Take(6).Select(k => $"{k.Key} {k.Value}")) + ").";
                if (MetroQto > 0) yield return $"MetroQto: {MetroQto} valor(es) completados com as quantidades do Revit.";
                if (NoSistema > 0) yield return $"Sistemas MEP: {NoSistema} elemento(s) colocados no sistema do Revit ({Sistemas} IfcDistributionSystem novo(s)).";
                if (Materiais > 0) yield return $"Materiais: material do Revit em {Materiais} elemento(s) que saíram sem material.";
                foreach (string a in Avisos) yield return a;
            }
        }

        // ------------------------------------------------------------------ regras de quantidades

        private sealed class RegraQto
        {
            public string Qto;
            public (string Nome, char Tipo, FonteQto Fonte)[] Itens;
        }

        // Tipo: L comprimento, A área, V volume, W peso. Só se calcula o que o Revit dá com segurança: os valores "Net"
        // (com aberturas) vêm do volume e da área do Revit; os "Gross" só nas classes que não têm aberturas.
        private static readonly Dictionary<string, RegraQto> Regras = new Dictionary<string, RegraQto>(StringComparer.Ordinal)
        {
            ["IFCWALL"] = R("Qto_WallBaseQuantities", ("Length", 'L', FonteQto.Comprimento), ("Width", 'L', FonteQto.Espessura),
                ("Height", 'L', FonteQto.Altura), ("NetSideArea", 'A', FonteQto.Area), ("NetVolume", 'V', FonteQto.Volume)),
            ["IFCSLAB"] = R("Qto_SlabBaseQuantities", ("Width", 'L', FonteQto.Espessura), ("NetArea", 'A', FonteQto.Area),
                ("NetVolume", 'V', FonteQto.Volume)),
            ["IFCROOF"] = R("Qto_RoofBaseQuantities", ("NetArea", 'A', FonteQto.Area)),
            ["IFCCOLUMN"] = R("Qto_ColumnBaseQuantities", ("Length", 'L', FonteQto.Comprimento), ("CrossSectionArea", 'A', FonteQto.Secao),
                ("OuterSurfaceArea", 'A', FonteQto.AreaSuperficie), ("GrossVolume", 'V', FonteQto.Volume), ("NetVolume", 'V', FonteQto.Volume)),
            ["IFCBEAM"] = R("Qto_BeamBaseQuantities", ("Length", 'L', FonteQto.Comprimento), ("CrossSectionArea", 'A', FonteQto.Secao),
                ("OuterSurfaceArea", 'A', FonteQto.AreaSuperficie), ("GrossVolume", 'V', FonteQto.Volume), ("NetVolume", 'V', FonteQto.Volume)),
            ["IFCMEMBER"] = R("Qto_MemberBaseQuantities", ("Length", 'L', FonteQto.Comprimento), ("CrossSectionArea", 'A', FonteQto.Secao),
                ("OuterSurfaceArea", 'A', FonteQto.AreaSuperficie), ("GrossVolume", 'V', FonteQto.Volume), ("NetVolume", 'V', FonteQto.Volume)),
            ["IFCPLATE"] = R("Qto_PlateBaseQuantities", ("Width", 'L', FonteQto.Espessura), ("NetArea", 'A', FonteQto.Area),
                ("NetVolume", 'V', FonteQto.Volume)),
            ["IFCFOOTING"] = R("Qto_FootingBaseQuantities", ("Length", 'L', FonteQto.Comprimento), ("Width", 'L', FonteQto.Largura),
                ("Height", 'L', FonteQto.Altura), ("OuterSurfaceArea", 'A', FonteQto.AreaSuperficie),
                ("GrossVolume", 'V', FonteQto.Volume), ("NetVolume", 'V', FonteQto.Volume)),
            ["IFCPILE"] = R("Qto_PileBaseQuantities", ("Length", 'L', FonteQto.Comprimento), ("CrossSectionArea", 'A', FonteQto.Secao),
                ("OuterSurfaceArea", 'A', FonteQto.AreaSuperficie), ("GrossVolume", 'V', FonteQto.Volume), ("NetVolume", 'V', FonteQto.Volume)),
            ["IFCSTAIR"] = R("Qto_StairBaseQuantities", ("Length", 'L', FonteQto.Comprimento), ("GrossVolume", 'V', FonteQto.Volume),
                ("NetVolume", 'V', FonteQto.Volume)),
            ["IFCSTAIRFLIGHT"] = R("Qto_StairFlightBaseQuantities", ("Length", 'L', FonteQto.Comprimento),
                ("GrossVolume", 'V', FonteQto.Volume), ("NetVolume", 'V', FonteQto.Volume)),
            ["IFCRAMP"] = R("Qto_RampBaseQuantities", ("Length", 'L', FonteQto.Comprimento), ("Width", 'L', FonteQto.Largura),
                ("NetArea", 'A', FonteQto.Area), ("GrossVolume", 'V', FonteQto.Volume), ("NetVolume", 'V', FonteQto.Volume)),
            ["IFCRAMPFLIGHT"] = R("Qto_RampFlightBaseQuantities", ("Length", 'L', FonteQto.Comprimento), ("Width", 'L', FonteQto.Largura),
                ("NetArea", 'A', FonteQto.Area), ("GrossVolume", 'V', FonteQto.Volume), ("NetVolume", 'V', FonteQto.Volume)),
            ["IFCCOVERING"] = R("Qto_CoveringBaseQuantities", ("Width", 'L', FonteQto.Espessura), ("NetArea", 'A', FonteQto.Area)),
            ["IFCRAILING"] = R("Qto_RailingBaseQuantities", ("Length", 'L', FonteQto.Comprimento)),
            ["IFCCURTAINWALL"] = R("Qto_CurtainWallQuantities", ("Length", 'L', FonteQto.Comprimento), ("Height", 'L', FonteQto.Altura),
                ("NetSideArea", 'A', FonteQto.Area)),
            ["IFCDOOR"] = R("Qto_DoorBaseQuantities", ("Width", 'L', FonteQto.Largura), ("Height", 'L', FonteQto.Altura)),
            ["IFCWINDOW"] = R("Qto_WindowBaseQuantities", ("Width", 'L', FonteQto.Largura), ("Height", 'L', FonteQto.Altura)),
            ["IFCBUILDINGELEMENTPROXY"] = R("Qto_BuildingElementProxyQuantities", ("NetSurfaceArea", 'A', FonteQto.AreaSuperficie),
                ("NetVolume", 'V', FonteQto.Volume)),
            ["IFCPIPESEGMENT"] = R("Qto_PipeSegmentBaseQuantities", ("Length", 'L', FonteQto.Comprimento)),
            ["IFCDUCTSEGMENT"] = R("Qto_DuctSegmentBaseQuantities", ("Length", 'L', FonteQto.Comprimento)),
            ["IFCCABLECARRIERSEGMENT"] = R("Qto_CableCarrierSegmentBaseQuantities", ("Length", 'L', FonteQto.Comprimento)),
            ["IFCCABLESEGMENT"] = R("Qto_CableSegmentBaseQuantities", ("Length", 'L', FonteQto.Comprimento)),
        };

        private static RegraQto R(string qto, params (string, char, FonteQto)[] itens) => new RegraQto { Qto = qto, Itens = itens };

        /// <summary>Propriedade do MetroQto → quantidade do Revit (nomes da planilha MetroDATA).</summary>
        private static readonly Dictionary<string, (FonteQto Fonte, char Tipo)> MetroQtoFontes =
            new Dictionary<string, (FonteQto, char)>(StringComparer.OrdinalIgnoreCase)
            {
                ["Volume"] = (FonteQto.Volume, 'V'),
                ["Area"] = (FonteQto.Area, 'A'),
                ["AreaSuperficie"] = (FonteQto.AreaSuperficie, 'A'),
                ["AreaSecao"] = (FonteQto.Secao, 'A'),
                ["Secao"] = (FonteQto.Secao, 'A'),
                ["Comprimento"] = (FonteQto.Comprimento, 'L'),
                ["ComprimentoTotal"] = (FonteQto.Comprimento, 'L'),
                ["Largura"] = (FonteQto.Largura, 'L'),
                ["Altura"] = (FonteQto.Altura, 'L'),
                ["Espessura"] = (FonteQto.Espessura, 'L'),
                ["EspessuraTotal"] = (FonteQto.Espessura, 'L'),
                ["Peso"] = (FonteQto.Peso, 'W'),
            };

        /// <summary>Propriedades do MetroQto que o MetroSP sabe calcular.</summary>
        public static bool MetroQtoCalculavel(string propriedade) => propriedade != null && MetroQtoFontes.ContainsKey(propriedade);

        /// <summary>Classes com Qto calculado (para a janela de explicação e os testes).</summary>
        public static IEnumerable<string> ClassesComQto => Regras.Keys;

        // ------------------------------------------------------------------ índice do arquivo

        private sealed class Indice
        {
            public readonly Dictionary<int, long> Tag = new Dictionary<int, long>();
            public readonly List<int> Elementos = new List<int>();
            public readonly Dictionary<int, List<(int Rel, int Def)>> Qtos = new Dictionary<int, List<(int, int)>>();
            public readonly Dictionary<int, List<(int Rel, int Def)>> Psets = new Dictionary<int, List<(int, int)>>();
            public readonly Dictionary<int, int> UsoDef = new Dictionary<int, int>();
            public readonly Dictionary<int, (int Rel, int Tipo)> TipoDe = new Dictionary<int, (int, int)>();
            public readonly Dictionary<int, int> UsoTipo = new Dictionary<int, int>();
            public readonly HashSet<int> ComMaterial = new HashSet<int>();
            public readonly HashSet<int> EmSistema = new HashSet<int>();
            public readonly Dictionary<string, int> RelDoSistema = new Dictionary<string, int>(StringComparer.Ordinal);
            public readonly Dictionary<int, int> Pai = new Dictionary<int, int>();
            public readonly Dictionary<string, int> MaterialPorNome = new Dictionary<string, int>(StringComparer.Ordinal);
            public string Historico = "$";
            /// <summary>O exportador gravou algum Qto_*BaseQuantities (o setup exporta as quantidades base).</summary>
            public bool TemQtoBase;
        }

        private static HashSet<string> _elementos, _distribuicao;

        private static Indice Indexar(StepModel m)
        {
            if (_elementos == null)
            {
                _distribuicao = new HashSet<string>(Distribuicao, StringComparer.Ordinal);
                _elementos = new HashSet<string>(Elementos, StringComparer.Ordinal);
                foreach (string x in new[] { "IFCOPENINGELEMENT", "IFCOPENINGSTANDARDCASE", "IFCVIRTUALELEMENT", "IFCELEMENTASSEMBLY",
                                             "IFCVOIDINGFEATURE", "IFCSURFACEFEATURE", "IFCPROJECTIONELEMENT" })
                    _elementos.Remove(x);
            }
            var ix = new Indice();
            var defs = new List<StepEntity>();
            foreach (int id in m.Ids)
            {
                string t = m.TypeOf(id);
                if (_elementos.Contains(t))
                {
                    StepEntity e = m.Get(id);
                    if (e.Args.Count < 8) continue;
                    ix.Elementos.Add(id);
                    if (long.TryParse(StepModel.Text(e.Args[7])?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long tag))
                        ix.Tag[id] = tag;
                }
                else if (t == "IFCRELDEFINESBYPROPERTIES") defs.Add(m.Get(id));
                else if (t == "IFCRELDEFINESBYTYPE")
                {
                    StepEntity r = m.Get(id);
                    int tipo = StepModel.Ref(r.Args[5]);
                    List<int> objs = StepModel.RefList(r.Args[4]);
                    ix.UsoTipo[tipo] = (ix.UsoTipo.TryGetValue(tipo, out int u) ? u : 0) + objs.Count;
                    foreach (int o in objs) ix.TipoDe[o] = (id, tipo);
                }
                else if (t == "IFCRELASSOCIATESMATERIAL")
                    foreach (int o in StepModel.RefList(m.Get(id).Args[4])) ix.ComMaterial.Add(o);
                else if (t == "IFCRELASSIGNSTOGROUP")
                {
                    StepEntity r = m.Get(id);
                    int g = StepModel.Ref(r.Args[6]);
                    string tg = m.TypeOf(g);
                    if (tg != "IFCSYSTEM" && tg != "IFCDISTRIBUTIONSYSTEM" && tg != "IFCDISTRIBUTIONCIRCUIT" && tg != "IFCBUILDINGSYSTEM") continue;
                    foreach (int o in StepModel.RefList(r.Args[4])) ix.EmSistema.Add(o);
                    string nome = StepModel.Text(m.Get(g).Args[2]);
                    if (nome != null && !ix.RelDoSistema.ContainsKey(nome)) ix.RelDoSistema[nome] = id;
                }
                else if (t == "IFCRELAGGREGATES")
                {
                    StepEntity r = m.Get(id);
                    int pai = StepModel.Ref(r.Args[4]);
                    foreach (int o in StepModel.RefList(r.Args[5])) ix.Pai[o] = pai;
                }
                else if (t == "IFCMATERIAL")
                {
                    string nome = StepModel.Text(m.Get(id).Args[0]);
                    if (nome != null && !ix.MaterialPorNome.ContainsKey(nome)) ix.MaterialPorNome[nome] = id;
                }
                else if (t == "IFCOWNERHISTORY" && ix.Historico == "$") ix.Historico = "#" + id;
            }
            foreach (StepEntity rel in defs)
            {
                int def = StepModel.Ref(rel.Args[5]);
                string td = m.TypeOf(def);
                bool qto = td == "IFCELEMENTQUANTITY";
                if (!qto && td != "IFCPROPERTYSET") continue;
                if (!qto && !string.Equals(StepModel.Text(m.Get(def).Args[2]), "MetroQto", StringComparison.OrdinalIgnoreCase)) continue;
                List<int> objs = StepModel.RefList(rel.Args[4]);
                ix.UsoDef[def] = (ix.UsoDef.TryGetValue(def, out int u) ? u : 0) + objs.Count;
                if (qto && !ix.TemQtoBase)
                {
                    string nq = StepModel.Text(m.Get(def).Args[2]) ?? "";
                    ix.TemQtoBase = nq.StartsWith("Qto_", StringComparison.OrdinalIgnoreCase) && nq.EndsWith("BaseQuantities", StringComparison.OrdinalIgnoreCase) &&
                                    nq != ArmaduraIfc.Qto && nq != "Qto_SpaceBaseQuantities";
                }
                Dictionary<int, List<(int, int)>> alvo = qto ? ix.Qtos : ix.Psets;
                foreach (int o in objs)
                {
                    if (!alvo.TryGetValue(o, out var l)) alvo[o] = l = new List<(int, int)>();
                    l.Add((rel.Id, def));
                }
            }
            if (ix.Historico == "$") ix.Historico = defs.Select(d => d.Args[1]).FirstOrDefault(a => a != "$") ?? "$";
            return ix;
        }

        // ------------------------------------------------------------------ aplicar

        public static Resultado Aplicar(StepModel m, FonteRevit fonte)
        {
            var r = new Resultado();
            if (m.Schema != null && m.Schema.StartsWith("IFC2X3", StringComparison.OrdinalIgnoreCase))
            {
                r.Avisos.Add("Correções de classe, quantidades, sistemas e materiais: só no IFC4 e no IFC 4.3 (este arquivo é IFC 2x3).");
                return r;
            }
            bool ifc43 = m.Schema != null && m.Schema.StartsWith("IFC4X3", StringComparison.OrdinalIgnoreCase);
            var relogio = System.Diagnostics.Stopwatch.StartNew();
            Indice ix = Indexar(m);
            r.Tempos.Add(("índice", relogio.Elapsed));
            var u = new Unidades(m);
            var c = new Contexto { M = m, Ix = ix, U = u, R = r, Fonte = fonte, Ifc43 = ifc43 };

            Etapa(r, "classe IFC", () => TrocarProxies(c));
            Etapa(r, "escadas e rampas", () => Lances(c));
            Etapa(r, "telas soldadas", () => Telas(c));
            Etapa(r, "quantidades base", () => Quantidades(c));
            Etapa(r, "MetroQto", () => MetroQto(c));
            Etapa(r, "sistemas MEP", () => Sistemas(c));
            Etapa(r, "materiais", () => Materiais(c));
            return r;
        }

        private static void Etapa(Resultado r, string nome, Action a)
        {
            var relogio = System.Diagnostics.Stopwatch.StartNew();
            try { a(); }
            catch (Exception ex) { r.Avisos.Add($"Não foi possível corrigir {nome}: {ex.Message}"); }
            r.Tempos.Add((nome, relogio.Elapsed));
        }

        private sealed class Contexto
        {
            public StepModel M;
            public Indice Ix;
            public Unidades U;
            public Resultado R;
            public FonteRevit Fonte;
            public bool Ifc43;
            public readonly Dictionary<long, QuantidadesRevit> Cache = new Dictionary<long, QuantidadesRevit>();
            public readonly Dictionary<long, FonteQto> Pedidas = new Dictionary<long, FonteQto>();

            /// <summary>Quantidades do Revit, pedindo de novo só se faltar alguma fonte ainda não pedida.</summary>
            public QuantidadesRevit Q(long tag, FonteQto precisa, FonteQto conferir = FonteQto.Nenhuma)
            {
                if (Fonte.Quantidades == null) return null;
                Pedidas.TryGetValue(tag, out FonteQto ja);
                if (Cache.TryGetValue(tag, out QuantidadesRevit q) && ((precisa | conferir) & ~ja) == 0) return q;
                QuantidadesRevit novo = Fonte.Quantidades(tag, precisa | ja, conferir & ~(precisa | ja));
                Cache[tag] = novo;
                Pedidas[tag] = precisa | ja;
                return novo;
            }
        }

        // ------------------------------------------------------------------ 1. classe IFC

        private static HashSet<string> _simples4, _simples43;

        private static bool Simples(bool ifc43, string classe)
        {
            if (_simples4 == null)
            {
                _simples4 = new HashSet<string>(Simples4, StringComparer.OrdinalIgnoreCase);
                _simples43 = new HashSet<string>(Simples4x3, StringComparer.OrdinalIgnoreCase);
            }
            return (ifc43 ? _simples43 : _simples4).Contains(classe);
        }

        private static void TrocarProxies(Contexto c)
        {
            if (c.Fonte.ClassesPedidas == null || c.Fonte.ClassesPedidas.Count == 0) return;
            foreach (int id in c.Ix.Elementos)
            {
                if (c.M.TypeOf(id) != "IFCBUILDINGELEMENTPROXY" || !c.Ix.Tag.TryGetValue(id, out long tag)) continue;
                if (!c.Fonte.ClassesPedidas.TryGetValue(tag, out string pedida) || string.IsNullOrWhiteSpace(pedida)) continue;
                string[] p = pedida.Trim().Split('.');
                string classe = p[0].Trim();
                if (classe.EndsWith("Type", StringComparison.OrdinalIgnoreCase) && classe.Length > 7) classe = classe.Substring(0, classe.Length - 4);
                string tipo = p.Length > 1 ? p[1].Trim().ToUpperInvariant() : null;
                if (classe.Equals("IfcBuildingElementProxy", StringComparison.OrdinalIgnoreCase)) continue;
                if (TrocarClasse(c, id, classe, tipo)) c.R.Classes++;
                else c.R.SemClasse.Add(classe);
            }
        }

        /// <summary>
        /// Troca a classe do elemento (e do tipo dele, se o tipo só serve a elementos trocados) mantendo os argumentos.
        /// Vale para as classes "simples" (8 atributos + PredefinedType) e para IfcStairFlight.
        /// </summary>
        private static bool TrocarClasse(Contexto c, int id, string classe, string tipo)
        {
            bool lance = classe.Equals("IfcStairFlight", StringComparison.OrdinalIgnoreCase);
            if (!lance && !Simples(c.Ifc43, classe)) return false;
            StepEntity e = c.M.Get(id);
            if (e.Args.Count != 9) return false;
            string predef = ".NOTDEFINED.";
            string objectType = e.Args[4];
            if (!string.IsNullOrEmpty(tipo) && tipo != "NOTDEFINED")
            {
                if (c.Fonte.TipoValido == null || c.Fonte.TipoValido(classe, tipo)) predef = "." + tipo + ".";
                else if (c.Fonte.TipoValido(classe, "USERDEFINED")) { predef = ".USERDEFINED."; objectType = Texto(tipo); }
            }
            if (predef == ".USERDEFINED." && objectType == "$") predef = ".NOTDEFINED.";
            var args = e.Args.Take(8).ToList();
            args[4] = objectType;
            if (lance) args.AddRange(new[] { "$", "$", "$", "$" });
            args.Add(predef);
            c.M.TrocarTipo(id, classe, args);

            // Tipo: troca junto se só este elemento (ou só elementos já trocados para a mesma classe) o usam; senão sai do tipo.
            if (c.Ix.TipoDe.TryGetValue(id, out var rt))
            {
                StepEntity t = c.M.Get(rt.Tipo);
                string classeTipo = (classe + "Type").ToUpperInvariant();
                if (t == null || t.Type == classeTipo) return true;
                List<int> objs = StepModel.RefList(c.M.Get(rt.Rel).Args[4]);
                bool todos = objs.All(o => o == id || string.Equals(c.M.TypeOf(o), classe, StringComparison.OrdinalIgnoreCase));
                if (todos && t.Args.Count == 10)
                {
                    var ta = t.Args.ToList();
                    ta[9] = predef == ".USERDEFINED." && ta[8] == "$" ? ".NOTDEFINED." : predef;
                    c.M.TrocarTipo(rt.Tipo, classeTipo, ta);
                }
                else if (!todos)
                {
                    List<int> ficam = objs.Where(o => o != id).ToList();
                    c.M.SetArg(rt.Rel, 4, StepModel.RefListText(ficam));
                    c.Ix.TipoDe.Remove(id);
                }
            }
            return true;
        }

        // ------------------------------------------------------------------ 2. escadas e rampas

        private static void Lances(Contexto c)
        {
            foreach (int id in c.Ix.Elementos)
            {
                if (c.M.TypeOf(id) != "IFCSLAB" || !c.Ix.Pai.TryGetValue(id, out int pai)) continue;
                string tp = c.M.TypeOf(pai);
                if (tp != "IFCRAMP" && tp != "IFCSTAIR") continue;
                StepEntity e = c.M.Get(id);
                if (e.Args.Count != 9 || string.Equals(e.Args[8], ".LANDING.", StringComparison.OrdinalIgnoreCase)) continue;
                if (TrocarClasse(c, id, tp == "IFCRAMP" ? "IfcRampFlight" : "IfcStairFlight", null)) c.R.Lances++;
            }
        }

        // ------------------------------------------------------------------ 3. telas soldadas

        private static void Telas(Contexto c)
        {
            if (c.Fonte.Tela == null) return;
            foreach (StepEntity e in c.M.OfType("IFCREINFORCINGMESH").ToList())
            {
                if (e.Args.Count < 17 || !c.Ix.Tag.TryGetValue(e.Id, out long tag)) continue;
                TelaRevit t = c.Fonte.Tela(tag);
                if (t == null) continue;
                bool mudou = false;
                void Grava(int i, double v)
                {
                    if (!(v > 0)) return;
                    string atual = e.Args[i];
                    if (atual != "$" && double.TryParse(atual, NumberStyles.Float, CultureInfo.InvariantCulture, out double a) &&
                        (Num(a) == Num(v) || Math.Abs(a - v) <= 1e-5 * Math.Max(Math.Abs(a), Math.Abs(v)))) return;
                    c.M.SetArg(e.Id, i, Num(v));
                    mudou = true;
                }
                double Secao(double d) => Math.PI * d * d / 4;
                Grava(9, c.U.L(t.Comprimento));
                Grava(10, c.U.L(t.Largura));
                Grava(11, c.U.L(t.DiametroLongitudinal));
                Grava(12, c.U.L(t.DiametroTransversal));
                Grava(13, c.U.A(Secao(t.DiametroLongitudinal)));
                Grava(14, c.U.A(Secao(t.DiametroTransversal)));
                Grava(15, c.U.L(t.EspacamentoLongitudinal));
                Grava(16, c.U.L(t.EspacamentoTransversal));
                var q = new QuantidadesRevit { Peso = t.Peso > 0 ? t.Peso : (double?)null };
                var regra = R(ArmaduraIfc.Qto, ("Weight", 'W', FonteQto.Peso));
                mudou |= GravarQto(c, e.Id, regra, q, "IFCREINFORCINGMESH", tag);
                if (!c.Cache.ContainsKey(tag)) { c.Cache[tag] = q; c.Pedidas[tag] = FonteQto.Peso; }
                if (mudou) c.R.Telas++;
            }
        }

        // ------------------------------------------------------------------ 4. quantidades base

        private static void Quantidades(Contexto c)
        {
            if (c.Fonte.Quantidades == null) return;
            if (!c.Ix.TemQtoBase)
            {
                c.R.Avisos.Add("Quantidades base: o setup não exporta as quantidades base (Qto), então o MetroSP não as completa. " +
                               "Para ter volume, área e comprimento no IFC, ligue \"Exportar quantidades base\" no setup.");
                return;
            }
            foreach (int id in c.Ix.Elementos)
            {
                string tipo = c.M.TypeOf(id);
                if (!Regras.TryGetValue(tipo, out RegraQto regra) || !c.Ix.Tag.TryGetValue(id, out long tag)) continue;
                Dictionary<string, double> atuais = ValoresQto(c, id, regra.Qto);
                FonteQto falta = FonteQto.Nenhuma, conferir = FonteQto.Nenhuma;
                foreach (var it in regra.Itens)
                {
                    if (!atuais.TryGetValue(it.Nome, out double v) || v <= 0) falta |= it.Fonte;
                    else if (it.Nome.StartsWith("Net", StringComparison.Ordinal) && (it.Tipo == 'V' || it.Tipo == 'A')) conferir |= it.Fonte;
                }
                if (falta == FonteQto.Nenhuma && conferir == FonteQto.Nenhuma) continue;
                QuantidadesRevit q = c.Q(tag, Dependencias(falta), Dependencias(conferir));
                if (q != null) GravarQto(c, id, regra, q, tipo, tag);
            }
        }

        /// <summary>A área da seção sai do volume e do comprimento.</summary>
        private static FonteQto Dependencias(FonteQto f) =>
            (f & FonteQto.Secao) != 0 ? f | FonteQto.Volume | FonteQto.Comprimento : f;

        /// <summary>Quantidades já gravadas no Qto (nome → valor, na unidade do arquivo).</summary>
        private static Dictionary<string, double> ValoresQto(Contexto c, int id, string qto)
        {
            var d = new Dictionary<string, double>(StringComparer.Ordinal);
            if (!c.Ix.Qtos.TryGetValue(id, out var l)) return d;
            foreach (var (_, def) in l)
            {
                StepEntity q = c.M.Get(def);
                if (!string.Equals(StepModel.Text(q.Args[2]), qto, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (int qi in StepModel.RefList(q.Args[5]))
                {
                    StepEntity x = c.M.Get(qi);
                    if (x == null || x.Args.Count < 4) continue;
                    string nome = StepModel.Text(x.Args[0]);
                    if (nome != null && double.TryParse(x.Args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) d[nome] = v;
                }
            }
            return d;
        }

        /// <summary>
        /// Completa (ou cria) o Qto do elemento: grava as quantidades que faltam ou estão zeradas e troca os valores "Net"
        /// que diferem mais de 10% do Revit. O Qto compartilhado com outros elementos é copiado antes de mudar.
        /// </summary>
        private static bool GravarQto(Contexto c, int id, RegraQto regra, QuantidadesRevit q, string classe, long tag)
        {
            int qtoId = 0, relId = 0;
            if (c.Ix.Qtos.TryGetValue(id, out var l))
                foreach (var (rel, def) in l)
                    if (string.Equals(StepModel.Text(c.M.Get(def).Args[2]), regra.Qto, StringComparison.OrdinalIgnoreCase)) { qtoId = def; relId = rel; break; }

            List<int> lista = qtoId == 0 ? new List<int>() : StepModel.RefList(c.M.Get(qtoId).Args[5]);
            var porNome = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (int qi in lista)
            {
                string n = StepModel.Text(c.M.Get(qi)?.Args[0]);
                if (n != null && !porNome.ContainsKey(n)) porNome[n] = qi;
            }
            bool mudou = false;
            string nomeClasse = NomeClasse(classe);
            foreach (var it in regra.Itens)
            {
                double? si = q.Valor(it.Fonte);
                if (si == null || si <= 0 || double.IsNaN(si.Value) || double.IsInfinity(si.Value)) continue;
                double valor = c.U.Converter(it.Tipo, si.Value);
                if (porNome.TryGetValue(it.Nome, out int atual))
                {
                    StepEntity x = c.M.Get(atual);
                    double.TryParse(x.Args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double v);
                    bool zerado = v <= 0;
                    bool diferente = !zerado && it.Nome.StartsWith("Net", StringComparison.Ordinal) && (it.Tipo == 'V' || it.Tipo == 'A') &&
                                     Math.Abs(v - valor) > 0.10 * Math.Max(v, valor);
                    if (!zerado && !diferente) continue;
                    int novo = Quantidade(c, it.Tipo, it.Nome, valor);
                    lista[lista.IndexOf(atual)] = novo;
                    porNome[it.Nome] = novo;
                    if (zerado) c.R.QtoCompletados++; else c.R.QtoCorrigidos++;
                    if (c.R.Trocas.Count < 200)
                        c.R.Trocas.Add($"{nomeClasse} {tag}: {regra.Qto}.{it.Nome} {Num(v)} → {Num(valor)}");
                }
                else
                {
                    int novo = Quantidade(c, it.Tipo, it.Nome, valor);
                    lista.Add(novo);
                    porNome[it.Nome] = novo;
                    c.R.QtoCompletados++;
                }
                c.R.QtoPorClasse[nomeClasse] = (c.R.QtoPorClasse.TryGetValue(nomeClasse, out int n2) ? n2 : 0) + 1;
                mudou = true;
            }
            if (!mudou) return false;
            if (qtoId != 0 && c.Ix.UsoDef.TryGetValue(qtoId, out int uso) && uso <= 1)
            {
                c.M.SetArg(qtoId, 5, StepModel.RefListText(lista));
                return true;
            }
            if (qtoId != 0) Desligar(c, relId, id, qtoId);
            int nq = c.M.Add("IFCELEMENTQUANTITY", Guid("qto|" + id + "|" + regra.Qto), c.Ix.Historico, Texto(regra.Qto),
                "$", "$", StepModel.RefListText(lista));
            int nr = c.M.Add("IFCRELDEFINESBYPROPERTIES", Guid("relqto|" + id + "|" + regra.Qto), c.Ix.Historico, "$", "$", "(#" + id + ")", "#" + nq);
            if (!c.Ix.Qtos.TryGetValue(id, out var nl)) c.Ix.Qtos[id] = nl = new List<(int, int)>();
            nl.Add((nr, nq));
            c.Ix.UsoDef[nq] = 1;
            return true;
        }

        /// <summary>Tira o objeto da relação (ele vai ganhar uma cópia própria da definição).</summary>
        private static void Desligar(Contexto c, int rel, int obj, int def)
        {
            List<int> objs = StepModel.RefList(c.M.Get(rel).Args[4]);
            objs.Remove(obj);
            if (objs.Count > 0) c.M.SetArg(rel, 4, StepModel.RefListText(objs));
            else c.M.Remove(rel);
            if (c.Ix.UsoDef.TryGetValue(def, out int u)) c.Ix.UsoDef[def] = u - 1;
            if (c.Ix.Qtos.TryGetValue(obj, out var l)) l.RemoveAll(x => x.Rel == rel);
            if (c.Ix.Psets.TryGetValue(obj, out var p)) p.RemoveAll(x => x.Rel == rel);
        }

        private static int Quantidade(Contexto c, char tipo, string nome, double valor)
        {
            string ent = tipo == 'L' ? "IFCQUANTITYLENGTH" : tipo == 'A' ? "IFCQUANTITYAREA" : tipo == 'V' ? "IFCQUANTITYVOLUME" : "IFCQUANTITYWEIGHT";
            return c.M.Add(ent, Texto(nome), "$", "$", Num(valor), "$");
        }

        private static Dictionary<string, string> _nomes;

        private static string NomeClasse(string tipo)
        {
            if (string.IsNullOrEmpty(tipo)) return "?";
            string t = tipo.ToUpperInvariant();
            if (_nomes == null)
            {
                var d = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string s in Simples4x3.Concat(Simples4)) d[s.ToUpperInvariant()] = s;
                _nomes = d;
            }
            if (_nomes.TryGetValue(t, out string nome)) return nome;
            switch (t)
            {
                case "IFCSTAIRFLIGHT": return "IfcStairFlight";
                case "IFCDOOR": return "IfcDoor";
                case "IFCWINDOW": return "IfcWindow";
                case "IFCSTAIR": return "IfcStair";
                case "IFCRAMP": return "IfcRamp";
                case "IFCREINFORCINGMESH": return "IfcReinforcingMesh";
                default: return "Ifc" + t.Substring(3, 1) + t.Substring(4).ToLowerInvariant();
            }
        }

        // ------------------------------------------------------------------ 5. MetroQto

        private static void MetroQto(Contexto c)
        {
            if (c.Fonte.MetroQto == null || c.Fonte.MetroQto.Count == 0) return;
            foreach (int id in c.Ix.Elementos)
            {
                string tipo = c.M.TypeOf(id);
                if (!c.Fonte.MetroQto.TryGetValue(tipo, out IReadOnlyList<string> props) || !c.Ix.Tag.TryGetValue(id, out long tag)) continue;
                var calc = props.Where(MetroQtoCalculavel).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (calc.Count == 0) continue;

                int psetId = 0, relId = 0;
                if (c.Ix.Psets.TryGetValue(id, out var l) && l.Count > 0) (relId, psetId) = l[0];
                List<int> lista = psetId == 0 ? new List<int>() : StepModel.RefList(c.M.Get(psetId).Args[4]);
                var porNome = new Dictionary<string, (int Id, bool Vazio)>(StringComparer.OrdinalIgnoreCase);
                foreach (int pi in lista)
                {
                    StepEntity p = c.M.Get(pi);
                    if (p == null || p.Type != "IFCPROPERTYSINGLEVALUE") continue;
                    string n = StepModel.Text(p.Args[0]);
                    if (n == null || porNome.ContainsKey(n)) continue;
                    porNome[n] = (pi, Vazio(p.Args[2]));
                }
                var faltam = calc.Where(n => !porNome.TryGetValue(n, out var x) || x.Vazio).ToList();
                if (faltam.Count == 0) continue;
                FonteQto precisa = faltam.Aggregate(FonteQto.Nenhuma, (a, n) => a | MetroQtoFontes[n].Fonte);
                QuantidadesRevit q = c.Q(tag, Dependencias(precisa));
                if (q == null) continue;
                bool mudou = false;
                foreach (string n in faltam)
                {
                    var (f, t) = MetroQtoFontes[n];
                    double? si = q.Valor(f);
                    if (si == null || si <= 0) continue;
                    string medida = t == 'L' ? "IFCLENGTHMEASURE" : t == 'A' ? "IFCAREAMEASURE" : t == 'V' ? "IFCVOLUMEMEASURE" : "IFCMASSMEASURE";
                    int nova = c.M.Add("IFCPROPERTYSINGLEVALUE", Texto(n), "$", medida + "(" + Num(c.U.Converter(t, si.Value)) + ")", "$");
                    if (porNome.TryGetValue(n, out var antiga)) lista[lista.IndexOf(antiga.Id)] = nova;
                    else lista.Add(nova);
                    porNome[n] = (nova, false);
                    c.R.MetroQto++;
                    mudou = true;
                }
                if (!mudou) continue;
                if (psetId != 0 && c.Ix.UsoDef.TryGetValue(psetId, out int uso) && uso <= 1)
                {
                    c.M.SetArg(psetId, 4, StepModel.RefListText(lista));
                    continue;
                }
                if (psetId != 0) Desligar(c, relId, id, psetId);
                int np = c.M.Add("IFCPROPERTYSET", Guid("metroqto|" + id), c.Ix.Historico, Texto("MetroQto"), "$", StepModel.RefListText(lista));
                c.M.Add("IFCRELDEFINESBYPROPERTIES", Guid("relmetroqto|" + id), c.Ix.Historico, "$", "$", "(#" + id + ")", "#" + np);
            }
        }

        private static bool Vazio(string valor)
        {
            if (valor == null || valor == "$") return true;
            int p = valor.IndexOf('(');
            string v = p >= 0 ? valor.Substring(p + 1).TrimEnd(')').Trim() : valor.Trim();
            if (v.Length == 0 || v == "''") return true;
            return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d == 0;
        }

        // ------------------------------------------------------------------ 6. sistemas MEP

        private static void Sistemas(Contexto c)
        {
            if (c.Fonte.Sistemas == null) return;
            var enumValido = new HashSet<string>(c.Ifc43 ? Sistemas4x3 : Sistemas4, StringComparer.OrdinalIgnoreCase);
            var porSistema = new Dictionary<long, (SistemaRevit S, List<int> Objs)>();
            foreach (int id in c.Ix.Elementos)
            {
                if (!_distribuicao.Contains(c.M.TypeOf(id)) || c.Ix.EmSistema.Contains(id) || !c.Ix.Tag.TryGetValue(id, out long tag)) continue;
                IReadOnlyList<SistemaRevit> ss = c.Fonte.Sistemas(tag);
                if (ss == null) continue;
                foreach (SistemaRevit s in ss)
                {
                    if (s == null || string.IsNullOrWhiteSpace(s.Nome)) continue;
                    if (!porSistema.TryGetValue(s.Id, out var x)) porSistema[s.Id] = x = (s, new List<int>());
                    if (!x.Objs.Contains(id)) x.Objs.Add(id);
                }
            }
            if (porSistema.Count == 0) return;
            int predio = c.M.OfType("IFCBUILDING").Select(b => b.Id).FirstOrDefault();
            foreach (var (s, objs) in porSistema.Values)
            {
                c.R.NoSistema += objs.Count;
                if (c.Ix.RelDoSistema.TryGetValue(s.Nome, out int relExistente))
                {
                    List<int> atuais = StepModel.RefList(c.M.Get(relExistente).Args[4]);
                    atuais.AddRange(objs.Where(o => !atuais.Contains(o)));
                    c.M.SetArg(relExistente, 4, StepModel.RefListText(atuais));
                    continue;
                }
                string predef = s.Predefinido != null && enumValido.Contains(s.Predefinido) ? s.Predefinido.ToUpperInvariant() : "USERDEFINED";
                string objType = string.IsNullOrWhiteSpace(s.Tipo) ? (predef == "USERDEFINED" ? Texto(s.Nome) : "$") : Texto(s.Tipo);
                int sis = c.M.Add("IFCDISTRIBUTIONSYSTEM", Guid("sistema|" + s.Id), c.Ix.Historico, Texto(s.Nome), "$", objType,
                    string.IsNullOrWhiteSpace(s.Tipo) ? "$" : Texto(s.Tipo), "." + predef + ".");
                int rel = c.M.Add("IFCRELASSIGNSTOGROUP", Guid("grupo|" + s.Id), c.Ix.Historico, "$", "$", StepModel.RefListText(objs), "$", "#" + sis);
                if (predio != 0)
                    c.M.Add("IFCRELSERVICESBUILDINGS", Guid("servico|" + s.Id), c.Ix.Historico, "$", "$", "#" + sis, "(#" + predio + ")");
                c.Ix.RelDoSistema[s.Nome] = rel;
                c.R.Sistemas++;
            }
        }

        // ------------------------------------------------------------------ 7. materiais

        private static void Materiais(Contexto c)
        {
            if (c.Fonte.Materiais == null) return;
            var porConjunto = new Dictionary<string, (IReadOnlyList<MaterialRevit> Mats, List<int> Objs)>(StringComparer.Ordinal);
            foreach (int id in c.Ix.Elementos)
            {
                if (c.Ix.ComMaterial.Contains(id)) continue;
                if (c.Ix.TipoDe.TryGetValue(id, out var rt) && c.Ix.ComMaterial.Contains(rt.Tipo)) continue;
                StepEntity e = c.M.Get(id);
                if (e.Args.Count < 7 || e.Args[6] == "$" || !c.Ix.Tag.TryGetValue(id, out long tag)) continue;
                IReadOnlyList<MaterialRevit> mats = c.Fonte.Materiais(tag);
                if (mats == null || mats.Count == 0) continue;
                string chave = string.Join("|", mats.Select(x => x.Nome + ":" + Math.Round(x.Fracao, 3).ToString(CultureInfo.InvariantCulture)));
                if (!porConjunto.TryGetValue(chave, out var g)) porConjunto[chave] = g = (mats, new List<int>());
                g.Objs.Add(id);
            }
            foreach (var kv in porConjunto)
            {
                var (mats, objs) = kv.Value;
                string relacionado;
                if (mats.Count == 1) relacionado = "#" + Material(c, mats[0].Nome);
                else
                {
                    var partes = mats.Select(x => "#" + c.M.Add("IFCMATERIALCONSTITUENT", Texto(x.Nome), "$", "#" + Material(c, x.Nome),
                        x.Fracao > 0 ? Num(x.Fracao) : "$", "$")).ToList();
                    relacionado = "#" + c.M.Add("IFCMATERIALCONSTITUENTSET", "$", "$", "(" + string.Join(",", partes) + ")");
                }
                c.M.Add("IFCRELASSOCIATESMATERIAL", Guid("material|" + kv.Key + "|" + objs[0]), c.Ix.Historico, "$", "$",
                    StepModel.RefListText(objs), relacionado);
                c.R.Materiais += objs.Count;
            }
        }

        private static int Material(Contexto c, string nome)
        {
            if (c.Ix.MaterialPorNome.TryGetValue(nome, out int id)) return id;
            id = c.M.Add("IFCMATERIAL", Texto(nome), "$", "$");
            c.Ix.MaterialPorNome[nome] = id;
            return id;
        }

        // ------------------------------------------------------------------ varredura antes de desfazer a transação

        /// <summary>
        /// Tags dos IfcBuildingElementProxy do arquivo, sem carregar o modelo: o exportador do Revit grava uma entidade por
        /// linha. Usado ainda dentro da transação da exportação, quando o "Exportar para IFC como" aplicado só nela
        /// (arquivo de coordenação) ainda está no modelo.
        /// </summary>
        public static HashSet<long> TagsDeProxies(string arquivo)
        {
            var tags = new HashSet<long>();
            foreach (string linha in System.IO.File.ReadLines(arquivo))
            {
                int p = linha.IndexOf("=IFCBUILDINGELEMENTPROXY(", StringComparison.Ordinal);
                if (p < 0) continue;
                int ini = p + "=IFCBUILDINGELEMENTPROXY(".Length;
                int fim = linha.LastIndexOf(')');
                if (fim <= ini) continue;
                List<string> args = StepModel.SplitArgs(linha.Substring(ini, fim - ini));
                if (args.Count >= 8 && long.TryParse(StepModel.Text(args[7])?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long tag))
                    tags.Add(tag);
            }
            return tags;
        }

        // ------------------------------------------------------------------ unidades e texto

        private sealed class Unidades
        {
            private readonly double _l, _a, _v, _w;
            public Unidades(StepModel m)
            {
                _l = ArmaduraIfc.FatorUnidade(m, "LENGTHUNIT", 1);
                _a = FatorPotencia(m, "AREAUNIT", 2);
                _v = FatorPotencia(m, "VOLUMEUNIT", 3);
                _w = ArmaduraIfc.FatorUnidade(m, "MASSUNIT", 1);
            }
            public double L(double m) => m / _l;
            public double A(double m2) => m2 / _a;
            public double Converter(char tipo, double si) =>
                tipo == 'L' ? si / _l : tipo == 'A' ? si / _a : tipo == 'V' ? si / _v : si / _w;

            /// <summary>Unidade de área ou volume: o prefixo SI vale elevado à potência (mm² = 1e-6 m²).</summary>
            private static double FatorPotencia(StepModel m, string tipo, int potencia)
            {
                string enumTipo = "." + tipo + ".";
                foreach (StepEntity a in m.OfType("IFCUNITASSIGNMENT"))
                    foreach (int u in StepModel.RefList(a.Args[0]))
                    {
                        StepEntity e = m.Get(u);
                        if (e == null || e.Args.Count < 2 || !string.Equals(e.Args[1], enumTipo, StringComparison.OrdinalIgnoreCase)) continue;
                        if (e.Type == "IFCSIUNIT")
                        {
                            return Math.Pow(Prefixo(e.Args[2]), potencia);
                        }
                        return ArmaduraIfc.FatorUnidade(m, tipo, 1);
                    }
                return 1;
            }

            private static double Prefixo(string arg)
            {
                switch ((arg ?? "").Trim('.').ToUpperInvariant())
                {
                    case "KILO": return 1e3;
                    case "HECTO": return 1e2;
                    case "DECA": return 1e1;
                    case "DECI": return 1e-1;
                    case "CENTI": return 1e-2;
                    case "MILLI": return 1e-3;
                    default: return 1;
                }
            }
        }

        /// <summary>Número real no formato STEP (ponto decimal), com até 10 casas (seções de barra em m² precisam).</summary>
        private static string Num(double v)
        {
            string t = Math.Round(v, 10).ToString("0.0#########", CultureInfo.InvariantCulture);
            return t.EndsWith(".0") ? t.Substring(0, t.Length - 1) : t;
        }

        private static string Texto(string text)
        {
            var sb = new StringBuilder("'");
            foreach (char ch in text ?? "")
            {
                if (ch == '\'') sb.Append("''");
                else if (ch == '\\') sb.Append("\\\\");
                else if (ch >= 32 && ch <= 126) sb.Append(ch);
                else sb.Append("\\X2\\").Append(((int)ch).ToString("X4")).Append("\\X0\\");
            }
            return sb.Append('\'').ToString();
        }

        private static MD5 _md5;

        /// <summary>GlobalId estável (o mesmo a cada exportação): MD5 da chave em base64 do IFC (22 caracteres).</summary>
        private static string Guid(string key)
        {
            if (_md5 == null) _md5 = MD5.Create();
            byte[] h = _md5.ComputeHash(Encoding.UTF8.GetBytes("MetroSP|correcoes|" + key));
            const string chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";
            ulong hi = 0, lo = 0;
            for (int i = 0; i < 8; i++) { hi = (hi << 8) | h[i]; lo = (lo << 8) | h[i + 8]; }
            var ch = new char[22];
            for (int i = 21; i >= 0; i--)
            {
                ch[i] = chars[(int)(lo & 63)];
                lo = (lo >> 6) | ((hi & 63) << 58);
                hi >>= 6;
            }
            return "'" + new string(ch) + "'";
        }
    }
}
