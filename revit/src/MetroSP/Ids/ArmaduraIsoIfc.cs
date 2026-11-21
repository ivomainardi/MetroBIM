using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MetroSP.Ifc
{
    /// <summary>O que o Revit sabe de uma armadura (conjunto), para os dados padrão da barra no IFC.</summary>
    public sealed class ArmaduraDadosRevit
    {
        /// <summary>Diâmetro nominal da barra, em metros (0 = desconhecido).</summary>
        public double Diametro;
        /// <summary>true = nervurada (Deformed), false = lisa (Plain), null = desconhecido.</summary>
        public bool? Nervurada;
        /// <summary>Estribo ou grampo (RebarStyle StirrupTie).</summary>
        public bool Estribo;
        /// <summary>Nome da forma (RebarShape) no Revit.</summary>
        public string Forma;
        /// <summary>Nome do material da armadura no Revit.</summary>
        public string Material;
        /// <summary>
        /// Anel fechado (círculo completo): normal do plano e direção do diâmetro (do ponto inicial ao ponto do meio), no
        /// sistema do Revit. null = não é anel ou não deu para ler.
        /// </summary>
        public double[] NormalAnel, DiametroAnel;
    }

    /// <summary>
    /// Dados padrão das barras de armadura (IfcReinforcingBar) que o exportador da Autodesk deixa vazios ou errados:
    /// <list type="number">
    /// <item>Anéis fechados: o exportador grava o círculo como IfcArcIndex((1,2,1)), com só 2 pontos, que não define o
    /// plano do círculo (geometria inválida, some nos visualizadores). Vira dois meios-círculos de 3 pontos, no plano do
    /// anel no Revit.</item>
    /// <item>Conjuntos exportados barra a barra (comprimento variável): o MetroQto repete em cada barra a Quantidade e o
    /// PesoDoConjunto do conjunto inteiro e fica sem o Comprimento. Passa a valer a barra (1, peso e comprimento dela).</item>
    /// <item>Aço: a categoria (CA-25, CA-50, CA-60) vem do material ou da classificação. Se o material do Revit não diz a
    /// categoria (ex.: o "Rebar - ASTM A615M - Grade 420" padrão), o material da armadura no IFC passa a ser "Aço CA-50".</item>
    /// <item>Atributos da barra e do tipo (NominalDiameter, CrossSectionArea, BarLength, BarSurface, SteelGrade e a função
    /// em PredefinedType) e o Pset_ReinforcingBarCommon completo. O código de forma ISO 3766 só sai se a forma do Revit
    /// tiver o código no nome (ex.: "ISO 3766 21" ou "21"): o Revit não guarda essa correspondência.</item>
    /// </list>
    /// Só IFC4 e IFC 4.3. Os atributos da ocorrência (NominalDiameter etc.) estão marcados como "deprecated" no IFC4,
    /// mas continuam válidos e são os que muitos visualizadores mostram; o lugar oficial é o tipo e o Pset.
    /// </summary>
    public static class ArmaduraIsoIfc
    {
        public const string Pset = "Pset_ReinforcingBarCommon";
        public const string PsetIso = "Pset_ReinforcingBarBendingsISOCD3766Common";

        public sealed class Resultado
        {
            public int Barras, Atributos, Tipos, Psets, MetroQtoPorBarra, Aneis, AneisEstimados, AneisSemPlano, ComAco, MaterialTrocado, FormaIso, SemFormaIso;
            public string Aco, MaterialAntigo;
            public readonly List<string> Avisos = new List<string>();
            public bool Alterou => Atributos + Tipos + Psets + MetroQtoPorBarra + Aneis + MaterialTrocado + FormaIso > 0;

            public IEnumerable<string> Linhas()
            {
                if (Barras == 0) yield break;
                if (Atributos + Psets > 0)
                    yield return $"Armaduras (dados padrão): diâmetro, área da seção, comprimento, superfície, aço e função em {Atributos} IfcReinforcingBar, " +
                                 $"{Tipos} IfcReinforcingBarType e {Psets} {Pset}.";
                if (Aneis > 0)
                    yield return $"Armaduras: {Aneis} anel(is) fechado(s) refeitos com arcos de 3 pontos (o exportador gravou o círculo com 2 pontos)" +
                                 (AneisEstimados > 0 ? $"; {AneisEstimados} sem o plano no Revit, considerados horizontais." : ".");
                if (AneisSemPlano > 0)
                    yield return $"Armaduras: {AneisSemPlano} anel(is) fechado(s) ficaram como o exportador gravou (não deu para saber o plano do círculo).";
                if (MetroQtoPorBarra > 0)
                    yield return $"Armaduras de comprimento variável: Quantidade, PesoDoConjunto e Comprimento do MetroQto da própria barra em {MetroQtoPorBarra} IfcReinforcingBar (antes repetiam o total do conjunto).";
                if (MaterialTrocado > 0)
                    yield return $"Armaduras: o material do Revit \"{MaterialAntigo}\" não é {Aco}; no IFC ficou \"Aço {Aco}\" em {MaterialTrocado} barra(s). Corrija o material no Revit.";
                if (FormaIso > 0) yield return $"Armaduras: código de forma ISO 3766 em {FormaIso} barra(s).";
                foreach (string a in Avisos) yield return a;
            }
        }

        private static readonly Regex RxAco = new Regex(@"\bCA\s*-?\s*(25|50|60)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex RxIso = new Regex(@"^\s*(?:(?:ISO|BS)\s*-?\s*(?:3766|8666)?\s*[-:]?\s*)?(\d{2}[A-Z]?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex RxArco = new Regex(@"IFCARCINDEX\(\((\d+),(\d+),(\d+)\)\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Categoria do aço (ex.: "CA-50") escrita no texto, ou null.</summary>
        public static string Aco(string texto)
        {
            if (string.IsNullOrEmpty(texto)) return null;
            Match x = RxAco.Match(texto);
            return x.Success ? "CA-" + x.Groups[1].Value : null;
        }

        /// <summary>Código de forma ISO 3766 / BS 8666 no nome da forma do Revit, ou null.</summary>
        public static string CodigoForma(string forma)
        {
            if (string.IsNullOrWhiteSpace(forma)) return null;
            Match x = RxIso.Match(forma);
            return x.Success ? x.Groups[1].Value.ToUpperInvariant() : null;
        }

        /// <param name="dados">Tag (ElementId) → dados da armadura no Revit (null = não é armadura ou sem Revit).</param>
        /// <param name="barras">Tag → barras do conjunto (as mesmas do Qto), para o comprimento e o peso de cada barra.</param>
        public static Resultado Aplicar(StepModel m, Func<long, ArmaduraDadosRevit> dados, Func<long, IReadOnlyList<BarraRevit>> barras)
        {
            var r = new Resultado();
            List<StepEntity> lista = m.OfType("IFCREINFORCINGBAR").Where(e => e.Args.Count >= 14).ToList();
            r.Barras = lista.Count;
            if (lista.Count == 0) return r;
            if (m.Schema != null && m.Schema.StartsWith("IFC2X3", StringComparison.OrdinalIgnoreCase))
            {
                r.Avisos.Add("Armaduras: dados padrão (diâmetro, aço, superfície, anéis) só no IFC4 e no IFC 4.3.");
                return r;
            }
            double fL = ArmaduraIfc.FatorUnidade(m, "LENGTHUNIT", 1.0);
            double fA = FatorArea(m, fL);
            double fW = ArmaduraIfc.FatorUnidade(m, "MASSUNIT", 1.0);

            var tag = new Dictionary<int, long>();
            foreach (StepEntity e in lista)
                if (long.TryParse(StepModel.Text(e.Args[7])?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long t)) tag[e.Id] = t;
            var cache = new Dictionary<long, ArmaduraDadosRevit>();
            ArmaduraDadosRevit D(int id)
            {
                if (!tag.TryGetValue(id, out long t)) return null;
                if (!cache.TryGetValue(t, out var d)) cache[t] = d = dados?.Invoke(t);
                return d;
            }
            var valores = barras == null ? new Dictionary<int, (int Count, double Comprimento, double Peso, double BarLength, bool PorBarra)>()
                                         : ArmaduraIfc.Valores(m, barras);

            // Índice: psets, material, classificação e tipo de cada barra.
            var alvo = new HashSet<int>(lista.Select(e => e.Id));
            var psets = new Dictionary<int, List<(int Rel, int Def)>>();
            var usoDef = new Dictionary<int, int>();
            var material = new Dictionary<int, (int Rel, int Mat)>();
            var classes = new Dictionary<int, List<string>>();
            var tipoDe = new Dictionary<int, int>();
            string historico = m.OfType("IFCOWNERHISTORY").Select(x => "#" + x.Id).FirstOrDefault() ?? "$";
            foreach (StepEntity rel in m.OfType("IFCRELDEFINESBYPROPERTIES"))
            {
                int def = StepModel.Ref(rel.Args[5]);
                List<int> objs = StepModel.RefList(rel.Args[4]);
                usoDef[def] = (usoDef.TryGetValue(def, out int u) ? u : 0) + objs.Count;
                foreach (int o in objs.Where(alvo.Contains))
                {
                    if (!psets.TryGetValue(o, out var l)) psets[o] = l = new List<(int, int)>();
                    l.Add((rel.Id, def));
                }
            }
            foreach (StepEntity rel in m.OfType("IFCRELASSOCIATESMATERIAL"))
                foreach (int o in StepModel.RefList(rel.Args[4]).Where(alvo.Contains)) material[o] = (rel.Id, StepModel.Ref(rel.Args[5]));
            foreach (StepEntity rel in m.OfType("IFCRELASSOCIATESCLASSIFICATION"))
            {
                StepEntity c = m.Get(StepModel.Ref(rel.Args[5]));
                if (c == null || c.Args.Count < 3) continue;
                string txt = StepModel.Text(c.Args[1]) + " " + StepModel.Text(c.Args[2]);
                foreach (int o in StepModel.RefList(rel.Args[4]).Where(alvo.Contains))
                {
                    if (!classes.TryGetValue(o, out var l)) classes[o] = l = new List<string>();
                    l.Add(txt);
                }
            }
            foreach (StepEntity rel in m.OfType("IFCRELDEFINESBYTYPE"))
            {
                int tipo = StepModel.Ref(rel.Args[5]);
                if (m.TypeOf(tipo) != "IFCREINFORCINGBARTYPE") continue;
                foreach (int o in StepModel.RefList(rel.Args[4]).Where(alvo.Contains)) tipoDe[o] = tipo;
            }

            string NomeMaterial(int id)
            {
                if (!material.TryGetValue(id, out var x)) return null;
                StepEntity mat = m.Get(x.Mat);
                return mat != null && mat.Type == "IFCMATERIAL" ? StepModel.Text(mat.Args[0]) : null;
            }
            string AcoDaBarra(int id)
            {
                string a = Aco(NomeMaterial(id)) ?? Aco(D(id)?.Material);
                if (a != null) return a;
                if (classes.TryGetValue(id, out var cl)) foreach (string c in cl) if ((a = Aco(c)) != null) return a;
                if (psets.TryGetValue(id, out var ps))
                    foreach (var (_, def) in ps)
                    {
                        StepEntity p = m.Get(def);
                        if (p == null || p.Type != "IFCPROPERTYSET") continue;
                        foreach (int pi in StepModel.RefList(p.Args[4]))
                        {
                            StepEntity v = m.Get(pi);
                            if (v != null && v.Type == "IFCPROPERTYSINGLEVALUE" && (a = Aco(StepModel.Text(v.Args[2]))) != null) return a;
                        }
                    }
                return null;
            }

            // 1. Anéis fechados.
            var geo = new ConferenciaGeometriaIfc.Geo(m);
            foreach (StepEntity e in lista) Aneis(m, geo, e, D(e.Id), r);

            // 2. Atributos da ocorrência, Pset_ReinforcingBarCommon e MetroQto.
            var materiaisTrocar = new Dictionary<int, List<int>>();   // material antigo → barras
            var porTipo = new Dictionary<int, (double Diametro, bool? Nervurada)>();
            var conflitoTipo = new HashSet<int>();
            foreach (StepEntity e in lista)
            {
                int id = e.Id;
                ArmaduraDadosRevit d = D(id);
                double raio = RaioDoCorpo(m, e);
                double diam = d != null && d.Diametro > 0 ? d.Diametro : raio > 0 ? 2 * raio * fL : 0;   // m
                double area = diam > 0 ? Math.PI * diam * diam / 4 : 0;                                  // m²
                valores.TryGetValue(id, out var v);
                bool temV = valores.ContainsKey(id);
                string aco = AcoDaBarra(id);
                bool? nerv = d?.Nervurada ?? (aco == "CA-50" ? true : aco == "CA-25" ? false : (bool?)null);
                string superficie = nerv == null ? null : nerv.Value ? "TEXTURED" : "PLAIN";
                bool anel = EAnel(m, e);
                string funcao = anel ? "RING" : d == null ? null : d.Estribo ? "LIGATURE" : "MAIN";
                bool mudou = false;
                void Arg(int i, string valor)
                {
                    if (valor == null || e.Args.Count <= i || e.Args[i] != "$" && !(i == 12 && e.Args[i] == ".NOTDEFINED.")) return;
                    m.SetArg(id, i, valor);
                    mudou = true;
                }
                if (aco != null) { Arg(8, Texto(aco)); r.ComAco++; r.Aco = r.Aco ?? aco; }
                if (diam > 0) { Arg(9, Real(diam / fL)); Arg(10, Area(area / fA)); }
                if (temV && v.BarLength > 0) Arg(11, Real(v.BarLength / fL));
                if (funcao != null) Arg(12, "." + funcao + ".");
                if (superficie != null) Arg(13, "." + superficie + ".");
                if (mudou) r.Atributos++;

                // Pset_ReinforcingBarCommon
                var props = new List<(string Nome, string Valor)>();
                if (diam > 0)
                {
                    props.Add(("NominalDiameter", "IFCPOSITIVELENGTHMEASURE(" + Real(diam / fL) + ")"));
                    props.Add(("CrossSectionArea", "IFCAREAMEASURE(" + Area(area / fA) + ")"));
                }
                if (temV && v.BarLength > 0) props.Add(("BarLength", "IFCPOSITIVELENGTHMEASURE(" + Real(v.BarLength / fL) + ")"));
                if (aco != null) props.Add(("SteelGrade", "IFCLABEL(" + Texto(aco) + ")"));
                if (superficie != null) props.Add(("BarSurface", "ENUM:" + superficie));
                if (temV && v.Count > 0) props.Add(("BarCount", "IFCCOUNTMEASURE(" + v.Count.ToString(CultureInfo.InvariantCulture) + ")"));
                if (props.Count > 0 && GravarPset(m, id, Pset, props, true, psets, usoDef, historico)) r.Psets++;

                // Código de forma ISO 3766
                string codigo = CodigoForma(d?.Forma);
                if (codigo != null)
                {
                    if (GravarPset(m, id, PsetIso, new List<(string, string)> { ("BarShapeCode", "IFCLABEL(" + Texto(codigo) + ")") }, true, psets, usoDef, historico))
                        r.FormaIso++;
                }
                else if (d != null) r.SemFormaIso++;

                // MetroQto das barras de conjuntos exportados barra a barra
                if (temV && v.PorBarra && MetroQtoPorBarra(m, id, v.Comprimento / fL, v.Peso / fW, psets, usoDef, historico)) r.MetroQtoPorBarra++;

                // Material sem a categoria do aço
                if (aco != null && material.TryGetValue(id, out var mt) && m.TypeOf(mt.Mat) == "IFCMATERIAL" && Aco(NomeMaterial(id)) == null)
                {
                    if (!materiaisTrocar.TryGetValue(mt.Mat, out var l)) materiaisTrocar[mt.Mat] = l = new List<int>();
                    l.Add(id);
                }

                // Dados do tipo
                if (tipoDe.TryGetValue(id, out int tp) && diam > 0)
                {
                    // Superfície desconhecida numa barra não briga com a das outras; diâmetro ou superfície diferentes, sim.
                    if (!porTipo.TryGetValue(tp, out var pt)) porTipo[tp] = (diam, nerv);
                    else if (Math.Abs(pt.Diametro - diam) > 1e-6 || pt.Nervurada != null && nerv != null && pt.Nervurada != nerv) conflitoTipo.Add(tp);
                    else if (pt.Nervurada == null && nerv != null) porTipo[tp] = (pt.Diametro, nerv);
                }
            }

            // 3. Tipos: NominalDiameter, CrossSectionArea e BarSurface, quando todas as barras do tipo concordam.
            foreach (var kv in porTipo)
            {
                if (conflitoTipo.Contains(kv.Key)) continue;
                StepEntity t = m.Get(kv.Key);
                if (t == null || t.Args.Count < 14) continue;
                bool mudou = false;
                void Arg(int i, string valor)
                {
                    if (valor == null || t.Args[i] != "$") return;
                    m.SetArg(t.Id, i, valor);
                    mudou = true;
                }
                double d = kv.Value.Diametro;
                Arg(10, Real(d / fL));
                Arg(11, Area(Math.PI * d * d / 4 / fA));
                if (kv.Value.Nervurada != null) Arg(13, kv.Value.Nervurada.Value ? ".TEXTURED." : ".PLAIN.");
                if (mudou) r.Tipos++;
            }

            // 4. Material: renomeia o material do Revit quando só armaduras usam; senão, um material novo para as barras.
            foreach (var kv in materiaisTrocar)
            {
                string aco = AcoDaBarra(kv.Value[0]);
                if (aco == null) continue;
                StepEntity mat = m.Get(kv.Key);
                string antigo = StepModel.Text(mat.Args[0]);
                string novo = "Aço " + aco;
                r.MaterialAntigo = r.MaterialAntigo ?? antigo;
                r.Aco = aco;
                // Renomeia só se todos os objetos com esse material são barras com a mesma categoria de aço.
                bool todos = m.OfType("IFCRELASSOCIATESMATERIAL").Where(x => StepModel.Ref(x.Args[5]) == kv.Key)
                    .SelectMany(x => StepModel.RefList(x.Args[4])).All(o => alvo.Contains(o) && AcoDaBarra(o) == aco);
                if (todos)
                {
                    m.SetArg(kv.Key, 0, Texto(novo));
                    if (mat.Args.Count > 1 && mat.Args[1] == "$") m.SetArg(kv.Key, 1, Texto("Revit: " + antigo));
                    r.MaterialTrocado += m.OfType("IFCRELASSOCIATESMATERIAL").Where(x => StepModel.Ref(x.Args[5]) == kv.Key)
                        .Sum(x => StepModel.RefList(x.Args[4]).Count);
                    continue;
                }
                var porAco = kv.Value.GroupBy(AcoDaBarra).Where(g => g.Key != null);
                foreach (var g in porAco)
                {
                    int nm = m.Add("IFCMATERIAL", Texto("Aço " + g.Key), Texto("Revit: " + antigo), mat.Args.Count > 2 ? mat.Args[2] : "$");
                    var objs = g.ToList();
                    foreach (var rel in objs.Select(o => material[o].Rel).Distinct().ToList())
                    {
                        List<int> ficam = StepModel.RefList(m.Get(rel).Args[4]).Where(o => !objs.Contains(o)).ToList();
                        if (ficam.Count > 0) m.SetArg(rel, 4, StepModel.RefListText(ficam));
                        else m.Remove(rel);
                    }
                    m.Add("IFCRELASSOCIATESMATERIAL", Guid("material|" + g.Key + "|" + objs[0]), historico, "$", "$", StepModel.RefListText(objs), "#" + nm);
                    r.MaterialTrocado += objs.Count;
                }
            }
            if (r.SemFormaIso > 0 && r.FormaIso == 0)
                r.Avisos.Add("Armaduras: sem código de forma ISO 3766 (as formas do Revit não trazem o código no nome).");
            return r;
        }

        // ------------------------------------------------------------------ anéis

        private static IEnumerable<StepEntity> Discos(StepModel m, StepEntity barra)
        {
            StepEntity pds = m.Get(StepModel.Ref(barra.Args[6]));
            if (pds == null || pds.Args.Count < 3) yield break;
            foreach (int rep in StepModel.RefList(pds.Args[2]))
            {
                StepEntity sr = m.Get(rep);
                if (sr == null || sr.Args.Count < 4 || !string.Equals(StepModel.Text(sr.Args[1]), "Body", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (int it in StepModel.RefList(sr.Args[3]))
                {
                    StepEntity x = m.Get(it);
                    if (x != null && x.Type == "IFCSWEPTDISKSOLID") yield return x;
                }
            }
        }

        private static double RaioDoCorpo(StepModel m, StepEntity barra)
        {
            StepEntity d = Discos(m, barra).FirstOrDefault();
            return d != null && double.TryParse(d.Args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double raio) ? raio : 0;
        }

        /// <summary>A barra é um anel fechado (o eixo é um círculo completo)?</summary>
        private static bool EAnel(StepModel m, StepEntity barra)
        {
            foreach (StepEntity d in Discos(m, barra))
            {
                StepEntity c = m.Get(StepModel.Ref(d.Args[0]));
                if (c == null || c.Type != "IFCINDEXEDPOLYCURVE" || c.Args.Count < 2) return false;
                MatchCollection arcos = RxArco.Matches(c.Args[1]);
                if (arcos.Count == 0 || StepModel.SplitArgs(c.Args[1].Substring(1, c.Args[1].Length - 2)).Count != arcos.Count) return false;
                if (StepModel.Ref("#" + arcos[0].Groups[1].Value) != StepModel.Ref("#" + arcos[arcos.Count - 1].Groups[3].Value)) return false;
                return true;
            }
            return false;
        }

        private static void Aneis(StepModel m, ConferenciaGeometriaIfc.Geo geo, StepEntity barra, ArmaduraDadosRevit d, Resultado r)
        {
            double[,] T = null;
            foreach (StepEntity disco in Discos(m, barra).ToList())
            {
                StepEntity c = m.Get(StepModel.Ref(disco.Args[0]));
                if (c == null || c.Type != "IFCINDEXEDPOLYCURVE" || c.Args.Count < 2 || c.Args[1] == "$") continue;
                List<string> segs = StepModel.SplitArgs(c.Args[1].Substring(1, c.Args[1].Length - 2));
                if (segs.Count != 1) continue;
                Match a = RxArco.Match(segs[0]);
                if (!a.Success || a.Groups[1].Value != a.Groups[3].Value) continue;
                StepEntity pl = m.Get(StepModel.Ref(c.Args[0]));
                if (pl == null || pl.Type != "IFCCARTESIANPOINTLIST3D") { r.AneisSemPlano++; continue; }
                string lista = pl.Args[0];
                List<string> pts = StepModel.SplitArgs(lista.Substring(1, lista.Length - 2));
                int i1 = int.Parse(a.Groups[1].Value, CultureInfo.InvariantCulture), i2 = int.Parse(a.Groups[2].Value, CultureInfo.InvariantCulture);
                if (i1 < 1 || i2 < 1 || i1 > pts.Count || i2 > pts.Count) { r.AneisSemPlano++; continue; }
                double[] p1 = StepModel.RealList(pts[i1 - 1]).ToArray(), p2 = StepModel.RealList(pts[i2 - 1]).ToArray();
                double[] u = Sub(p1, p2);
                double dist = Norma(u);
                if (dist < 1e-9) { r.AneisSemPlano++; continue; }

                // Rotação do placement da barra (sistema local → global do IFC).
                if (T == null)
                {
                    try { T = geo.Placement(StepModel.Ref(barra.Args[5]), 0); }
                    catch { T = ConferenciaGeometriaIfc.Geo.Id(); }
                }
                double[] dLocal = Un(Sub(p2, p1));
                double[] dGlobal = Rot(T, dLocal);
                double[] nGlobal = null;
                bool estimado = false;
                if (d?.NormalAnel != null && d.DiametroAnel != null)
                {
                    double[] nr = Un(d.NormalAnel), dr = Un(d.DiametroAnel);
                    if (nr != null && dr != null)
                    {
                        if (Math.Abs(nr[2]) > 0.999) nGlobal = new[] { 0.0, 0, 1 };
                        else if (Math.Sqrt(dr[0] * dr[0] + dr[1] * dr[1]) > 0.1 && Math.Sqrt(dGlobal[0] * dGlobal[0] + dGlobal[1] * dGlobal[1]) > 0.1)
                        {
                            // Revit → IFC só gira em torno de Z: o ângulo sai do diâmetro (nos dois sentidos, fica o que
                            // deixa a normal perpendicular ao diâmetro).
                            double th = Math.Atan2(dGlobal[1], dGlobal[0]) - Math.Atan2(dr[1], dr[0]);
                            double[] c1 = RotZ(nr, th), c2 = RotZ(nr, th + Math.PI);
                            nGlobal = Math.Abs(Dot(c1, dGlobal)) <= Math.Abs(Dot(c2, dGlobal)) ? c1 : c2;
                        }
                    }
                }
                if (nGlobal == null && Math.Abs(dGlobal[2]) < 0.1) { nGlobal = new[] { 0.0, 0, 1 }; estimado = true; }
                if (nGlobal == null) { r.AneisSemPlano++; continue; }
                double[] n = RotT(T, nGlobal);
                // Normal perpendicular ao diâmetro (tira a componente na direção do diâmetro).
                n = Un(Sub(n, Mul(dLocal, Dot(n, dLocal))));
                if (n == null) { r.AneisSemPlano++; continue; }
                double[] ctr = Mul(Soma(p1, p2), 0.5);
                double raio = dist / 2;
                double[] v = Mul(Cross(n, dLocal), raio);
                double[] p3 = Soma(ctr, v), p4 = Sub(ctr, v);
                int k = pts.Count;
                string novaLista = lista.Substring(0, lista.Length - 1) + "," + Ponto(p3) + "," + Ponto(p4) + ")";
                m.SetArg(pl.Id, 0, novaLista);
                m.SetArg(c.Id, 1, $"(IFCARCINDEX(({i1},{k + 1},{i2})),IFCARCINDEX(({i2},{k + 2},{i1})))");
                r.Aneis++;
                if (estimado) r.AneisEstimados++;
            }
        }

        private static string Ponto(double[] p) => "(" + string.Join(",", p.Select(Real)) + ")";
        private static double[] Sub(double[] a, double[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
        private static double[] Soma(double[] a, double[] b) => new[] { a[0] + b[0], a[1] + b[1], a[2] + b[2] };
        private static double[] Mul(double[] a, double k) => new[] { a[0] * k, a[1] * k, a[2] * k };
        private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        private static double Norma(double[] a) => Math.Sqrt(Dot(a, a));
        private static double[] Un(double[] a) { double n = Norma(a); return n < 1e-12 ? null : Mul(a, 1 / n); }
        private static double[] Cross(double[] a, double[] b) =>
            new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
        private static double[] RotZ(double[] a, double th) =>
            new[] { a[0] * Math.Cos(th) - a[1] * Math.Sin(th), a[0] * Math.Sin(th) + a[1] * Math.Cos(th), a[2] };
        private static double[] Rot(double[,] T, double[] a) =>
            new[] { T[0, 0] * a[0] + T[0, 1] * a[1] + T[0, 2] * a[2], T[1, 0] * a[0] + T[1, 1] * a[1] + T[1, 2] * a[2], T[2, 0] * a[0] + T[2, 1] * a[1] + T[2, 2] * a[2] };
        private static double[] RotT(double[,] T, double[] a) =>
            new[] { T[0, 0] * a[0] + T[1, 0] * a[1] + T[2, 0] * a[2], T[0, 1] * a[0] + T[1, 1] * a[1] + T[2, 1] * a[2], T[0, 2] * a[0] + T[1, 2] * a[1] + T[2, 2] * a[2] };

        // ------------------------------------------------------------------ psets

        /// <summary>
        /// Põe as propriedades no pset da barra (troca as de mesmo nome; cria o pset se não houver). Um pset usado por
        /// outros objetos é copiado só para esta barra. Valor "ENUM:X" grava um IfcPropertyEnumeratedValue.
        /// </summary>
        private static bool GravarPset(StepModel m, int id, string nome, List<(string Nome, string Valor)> props, bool trocar,
            Dictionary<int, List<(int Rel, int Def)>> psets, Dictionary<int, int> usoDef, string historico)
        {
            int relId = 0, psetId = 0;
            if (psets.TryGetValue(id, out var l))
                foreach (var (rel, def) in l)
                {
                    StepEntity p = m.Get(def);
                    if (p != null && p.Type == "IFCPROPERTYSET" && string.Equals(StepModel.Text(p.Args[2]), nome, StringComparison.OrdinalIgnoreCase))
                    { relId = rel; psetId = def; break; }
                }
            List<int> lista = psetId == 0 ? new List<int>() : StepModel.RefList(m.Get(psetId).Args[4]);
            var porNome = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (int pi in lista)
            {
                string n = StepModel.Text(m.Get(pi)?.Args[0]);
                if (n != null && !porNome.ContainsKey(n)) porNome[n] = pi;
            }
            bool mudou = false;
            foreach (var (n, valor) in props)
            {
                if (porNome.TryGetValue(n, out int antiga))
                {
                    StepEntity pa = m.Get(antiga);
                    string atual = pa.Type == "IFCPROPERTYSINGLEVALUE" ? pa.Args[2] : pa.Type == "IFCPROPERTYENUMERATEDVALUE" ? "ENUM:" + StepModel.Text(pa.Args[2]?.Trim('(', ')')) : null;
                    if (!trocar || atual == valor || MesmoNumero(atual, valor)) continue;
                }
                int nova = valor.StartsWith("ENUM:", StringComparison.Ordinal)
                    ? m.Add("IFCPROPERTYENUMERATEDVALUE", Texto(n), "$", "(IFCLABEL(" + Texto(valor.Substring(5)) + "))", "$")
                    : m.Add("IFCPROPERTYSINGLEVALUE", Texto(n), "$", valor, "$");
                if (porNome.TryGetValue(n, out int velha)) lista[lista.IndexOf(velha)] = nova;
                else lista.Add(nova);
                porNome[n] = nova;
                mudou = true;
            }
            if (!mudou) return false;
            int uso = psetId != 0 && usoDef.TryGetValue(psetId, out int u) ? u : 0;
            if (psetId != 0 && uso <= 1)
            {
                m.SetArg(psetId, 4, StepModel.RefListText(lista));
                return true;
            }
            if (psetId != 0)
            {
                // Pset compartilhado: tira esta barra do relacionamento antigo.
                List<int> objs = StepModel.RefList(m.Get(relId).Args[4]).Where(o => o != id).ToList();
                if (objs.Count > 0) m.SetArg(relId, 4, StepModel.RefListText(objs));
                else m.Remove(relId);
                usoDef[psetId] = Math.Max(0, uso - 1);
                l.RemoveAll(x => x.Rel == relId && x.Def == psetId);
            }
            int np = m.Add("IFCPROPERTYSET", Guid(nome + "|" + id), historico, Texto(nome), "$", StepModel.RefListText(lista));
            int nr = m.Add("IFCRELDEFINESBYPROPERTIES", Guid("rel|" + nome + "|" + id), historico, "$", "$", "(#" + id + ")", "#" + np);
            usoDef[np] = 1;
            if (l == null) psets[id] = l = new List<(int, int)>();
            l.Add((nr, np));
            return true;
        }

        private static bool MesmoNumero(string a, string b)
        {
            double? x = Numero(a), y = Numero(b);
            return x != null && y != null && Math.Abs(x.Value - y.Value) <= 1e-6 * Math.Max(1, Math.Abs(y.Value));
        }

        private static double? Numero(string valor)
        {
            if (valor == null) return null;
            int p = valor.IndexOf('(');
            string v = p >= 0 ? valor.Substring(p + 1).TrimEnd(')') : valor;
            return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : (double?)null;
        }

        /// <summary>MetroQto da barra de um conjunto exportado barra a barra: Quantidade 1, PesoDoConjunto e Comprimento dela.</summary>
        private static bool MetroQtoPorBarra(StepModel m, int id, double comprimento, double peso,
            Dictionary<int, List<(int Rel, int Def)>> psets, Dictionary<int, int> usoDef, string historico)
        {
            if (!psets.TryGetValue(id, out var l)) return false;
            int psetId = l.Where(x => m.TypeOf(x.Def) == "IFCPROPERTYSET" &&
                                     string.Equals(StepModel.Text(m.Get(x.Def).Args[2]), "MetroQto", StringComparison.OrdinalIgnoreCase))
                          .Select(x => x.Def).FirstOrDefault();
            if (psetId == 0) return false;
            var atuais = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (int pi in StepModel.RefList(m.Get(psetId).Args[4]))
            {
                StepEntity p = m.Get(pi);
                if (p != null && p.Type == "IFCPROPERTYSINGLEVALUE" && StepModel.Text(p.Args[0]) is string n) atuais[n] = p.Args[2];
            }
            string Mesmo(string atual, string numero)
            {
                // Mantém o tipo do valor que o exportador gravou (IFCINTEGER, IFCREAL, IFCMASSMEASURE...).
                int p = atual == null ? -1 : atual.IndexOf('(');
                return p > 0 ? atual.Substring(0, p) + "(" + numero + ")" : null;
            }
            var props = new List<(string, string)>();
            if (atuais.TryGetValue("Quantidade", out string q))
            {
                string s = q.StartsWith("IFCINTEGER", StringComparison.OrdinalIgnoreCase) || q.StartsWith("IFCCOUNTMEASURE", StringComparison.OrdinalIgnoreCase)
                    ? Mesmo(q, "1") : Mesmo(q, "1.");
                if (s != null) props.Add(("Quantidade", s));
            }
            if (peso > 0 && atuais.TryGetValue("PesoDoConjunto", out string w) && Mesmo(w, Real(peso)) is string sw) props.Add(("PesoDoConjunto", sw));
            if (comprimento > 0)
            {
                if (atuais.TryGetValue("Comprimento", out string c)) { if (Mesmo(c, Real(comprimento)) is string sc) props.Add(("Comprimento", sc)); }
                else props.Add(("Comprimento", "IFCLENGTHMEASURE(" + Real(comprimento) + ")"));
            }
            return props.Count > 0 && GravarPset(m, id, "MetroQto", props, true, psets, usoDef, historico);
        }

        // ------------------------------------------------------------------ util

        private static double FatorArea(StepModel m, double fL)
        {
            foreach (StepEntity a in m.OfType("IFCUNITASSIGNMENT"))
                foreach (int u in StepModel.RefList(a.Args[0]))
                {
                    StepEntity e = m.Get(u);
                    if (e == null || e.Args.Count < 2 || !string.Equals(e.Args[1], ".AREAUNIT.", StringComparison.OrdinalIgnoreCase)) continue;
                    if (e.Type == "IFCSIUNIT")
                    {
                        double p = ArmaduraIfc.FatorUnidade(m, "AREAUNIT", 1.0);
                        return p * p;   // MILLI em área = 1e-6 m²
                    }
                    return ArmaduraIfc.FatorUnidade(m, "AREAUNIT", fL * fL);
                }
            return fL * fL;
        }

        private static string Real(double v) => ArmaduraIfc.Real(v);

        /// <summary>Área com 12 casas (em m², a seção da barra tem a 5ª casa significativa).</summary>
        private static string Area(double v)
        {
            string s = Math.Round(v, 12).ToString("0.0###########", CultureInfo.InvariantCulture);
            return s.EndsWith(".0") ? s.Substring(0, s.Length - 1) : s;
        }

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

        private static string Guid(string key)
        {
            byte[] h;
            using (var md5 = MD5.Create()) h = md5.ComputeHash(Encoding.UTF8.GetBytes("MetroSP|armadura-iso|" + key));
            const string chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";
            var n = Compat.InteiroSemSinal(h);
            var c = new char[22];
            for (int i = 21; i >= 0; i--) { c[i] = chars[(int)(n & 63)]; n >>= 6; }
            return "'" + new string(c) + "'";
        }
    }
}
