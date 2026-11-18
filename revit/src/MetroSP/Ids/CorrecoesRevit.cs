using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Structure;
using MetroSP.Ifc;

namespace MetroSP.Ids
{
    /// <summary>
    /// O lado do Revit das correções do IFC (CorrecoesIfc): quantidades, classe pedida, telas soldadas, sistemas MEP e
    /// materiais de cada elemento, lidos só quando o IFC precisa. A geometria só é lida quando o parâmetro não dá o valor.
    /// </summary>
    internal static class CorrecoesRevit
    {
        private const double Pe = 0.3048;
        private const double Pe2 = Pe * Pe;
        private const double Pe3 = Pe * Pe * Pe;

        public static FonteRevit Fonte(Document doc, IReadOnlyDictionary<long, string> classesPedidas, string esquema,
                                       IReadOnlyDictionary<string, IReadOnlyList<string>> metroQto)
        {
            return new FonteRevit
            {
                Quantidades = (id, precisa, conferir) => Quantidades(doc, id, precisa, conferir),
                ClassesPedidas = classesPedidas,
                TipoValido = (classe, tipo) => EsquemaIfc.Tipos(esquema, classe).Contains(tipo, StringComparer.OrdinalIgnoreCase),
                Tela = id => Tela(doc, id),
                Sistemas = id => Sistemas(doc, id),
                Materiais = id => Materiais(doc, id),
                MetroQto = metroQto,
            };
        }

        // ------------------------------------------------------------------ classe pedida (dentro da transação)

        /// <summary>
        /// "Exportar para IFC como" (e o tipo pré-definido) dos elementos que saíram como proxy, lidos ainda na transação da
        /// exportação: as classes do arquivo de coordenação só existem nela.
        /// </summary>
        public static Dictionary<long, string> ClassesPedidas(Document doc, IEnumerable<long> tags)
        {
            var d = new Dictionary<long, string>();
            foreach (long tag in tags)
            {
                try
                {
                    Element e = doc.GetElement(Compat.NovoId(tag));
                    if (e == null) continue;
                    Element tipo = doc.GetElement(e.GetTypeId());
                    string classe = Texto(Compat.ExportarComo(e, false)) ?? Texto(e.LookupParameter("IfcExportAs")) ??
                                    Texto(Compat.ExportarComo(tipo, true)) ?? Texto(tipo?.LookupParameter("IfcExportAs[Type]")) ??
                                    Texto(tipo?.LookupParameter("IfcExportAs"));
                    if (classe == null) continue;
                    if (!classe.Contains("."))
                    {
                        string predef = Texto(e.LookupParameter("IfcExportType")) ?? Texto(tipo?.LookupParameter("IfcExportType[Type]")) ??
                                        Texto(tipo?.LookupParameter("IfcExportType")) ?? PredefinidoInterno(e, tipo);
                        if (predef != null) classe += "." + predef;
                    }
                    d[tag] = classe;
                }
                catch { /* elemento que não dá para ler: fica como o exportador gravou */ }
            }
            return d;
        }

        private static string PredefinidoInterno(Element e, Element tipo)
        {
#if REVIT_ANTES_2023
            return null;
#else
            return Texto(e.get_Parameter(BuiltInParameter.IFC_EXPORT_PREDEFINEDTYPE)) ??
                   Texto(tipo?.get_Parameter(BuiltInParameter.IFC_EXPORT_PREDEFINEDTYPE_TYPE));
#endif
        }

        private static string Texto(Parameter p)
        {
            if (p == null || !p.HasValue) return null;
            string t = p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
            if (string.IsNullOrWhiteSpace(t)) return null;
            t = t.Trim();
            return t.Equals("By Type", StringComparison.OrdinalIgnoreCase) || t.Equals("Por tipo", StringComparison.OrdinalIgnoreCase) ||
                   t.Equals("Default", StringComparison.OrdinalIgnoreCase) || t.Equals("Padrão", StringComparison.OrdinalIgnoreCase) ? null : t;
        }

        // ------------------------------------------------------------------ quantidades

        public static QuantidadesRevit Quantidades(Document doc, long id, FonteQto precisa, FonteQto conferir)
        {
            Element e;
            try { e = doc.GetElement(Compat.NovoId(id)); }
            catch { return null; }
            if (e == null) return null;
            var q = new QuantidadesRevit();
            try
            {
                // Só lê o que foi pedido: numa exportação grande são milhares de elementos.
                FonteQto todas = precisa | conferir;
                bool Quer(FonteQto f) => (todas & f) != 0;
                Element tipo = doc.GetElement(e.GetTypeId());
                if (Quer(FonteQto.Volume)) q.Volume = Par(e, BuiltInParameter.HOST_VOLUME_COMPUTED, Pe3);
                if (Quer(FonteQto.Area)) q.Area = Par(e, BuiltInParameter.HOST_AREA_COMPUTED, Pe2);
                if (Quer(FonteQto.Comprimento)) q.Comprimento = Comprimento(e);
                if (Quer(FonteQto.Espessura | FonteQto.Largura)) q.Espessura = Espessura(e, tipo);
                if (Quer(FonteQto.Largura)) q.Largura = e is Wall ? q.Espessura : Largura(e, tipo);
                if (Quer(FonteQto.Altura)) q.Altura = Altura(e, tipo);
                if (Quer(FonteQto.Peso)) q.Peso = Peso(doc, e);

                // Componentes de escada: o volume e o comprimento vêm das partes.
                if (e is Stairs escada && Quer(FonteQto.Comprimento | FonteQto.Largura))
                {
                    q.Comprimento = q.Comprimento ?? Soma(escada.GetStairsRuns().Select(r => doc.GetElement(r) as StairsRun), ComprimentoDoLance);
                    q.Largura = q.Largura ?? escada.GetStairsRuns().Select(r => (doc.GetElement(r) as StairsRun)?.ActualRunWidth * Pe).Max();
                }
                else if (e is StairsRun lance && Quer(FonteQto.Comprimento | FonteQto.Largura))
                {
                    q.Comprimento = q.Comprimento ?? ComprimentoDoLance(lance);
                    q.Largura = q.Largura ?? lance.ActualRunWidth * Pe;
                }

                // Geometria só para o que falta no IFC e o parâmetro não deu.
                bool vol = (precisa & FonteQto.Volume) != 0 && !(q.Volume > 0);
                bool area = (precisa & FonteQto.Area) != 0 && !(q.Area > 0);
                bool sup = (precisa & FonteQto.AreaSuperficie) != 0;
                if (vol || area || sup)
                {
                    var (v, aTopo, aTotal) = Geometria(doc, e);
                    if (vol && v > 0) q.Volume = v * Pe3;
                    if (area && aTopo > 0) q.Area = aTopo * Pe2;
                    if (sup && aTotal > 0) q.AreaSuperficie = aTotal * Pe2;
                }
                // Espessura de patamar, laje sem parâmetro etc.: volume ÷ área.
                if (!(q.Espessura > 0) && (precisa & FonteQto.Espessura) != 0 && q.Volume > 0 && q.Area > 0) q.Espessura = q.Volume / q.Area;
            }
            catch { /* o que já foi lido vale */ }
            return q;
        }

        private static double? Par(Element e, BuiltInParameter bip, double fator)
        {
            try
            {
                Parameter p = e?.get_Parameter(bip);
                if (p == null || !p.HasValue || p.StorageType != StorageType.Double) return null;
                double v = p.AsDouble() * fator;
                return v > 0 ? v : (double?)null;
            }
            catch { return null; }
        }

        private static double? Primeiro(Element e, Element tipo, params BuiltInParameter[] bips)
        {
            foreach (BuiltInParameter b in bips)
            {
                double? v = Par(e, b, Pe) ?? Par(tipo, b, Pe);
                if (v != null) return v;
            }
            return null;
        }

        private static double? Comprimento(Element e)
        {
            double? v = Par(e, BuiltInParameter.STRUCTURAL_FRAME_CUT_LENGTH, Pe) ?? Par(e, BuiltInParameter.CURVE_ELEM_LENGTH, Pe) ??
                        Par(e, BuiltInParameter.INSTANCE_LENGTH_PARAM, Pe) ?? Par(e, BuiltInParameter.STRUCTURAL_FOUNDATION_LENGTH, Pe);
            if (v != null) return v;
            try
            {
                if (e is Railing gc) return gc.GetPath().Sum(c => c.Length) * Pe;
                if (e.Location is LocationCurve lc) return lc.Curve.Length * Pe;
            }
            catch { }
            return null;
        }

        private static double? Espessura(Element e, Element tipo)
        {
            if (e is Wall w) return w.Width * Pe;
            return Primeiro(e, tipo, BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM, BuiltInParameter.ROOF_ATTR_THICKNESS_PARAM,
                BuiltInParameter.CEILING_THICKNESS, BuiltInParameter.STRUCTURAL_FOUNDATION_THICKNESS);
        }

        private static double? Largura(Element e, Element tipo) =>
            Primeiro(e, tipo, BuiltInParameter.DOOR_WIDTH, BuiltInParameter.WINDOW_WIDTH, BuiltInParameter.STRUCTURAL_FOUNDATION_WIDTH,
                BuiltInParameter.FAMILY_WIDTH_PARAM);

        private static double? Altura(Element e, Element tipo) =>
            Primeiro(e, tipo, BuiltInParameter.WALL_USER_HEIGHT_PARAM, BuiltInParameter.DOOR_HEIGHT, BuiltInParameter.WINDOW_HEIGHT,
                BuiltInParameter.STRUCTURAL_FOUNDATION_THICKNESS, BuiltInParameter.FAMILY_HEIGHT_PARAM);

        private static double? Peso(Document doc, Element e)
        {
            if (!(e is Rebar) && !(e is RebarInSystem) && !(e is RebarContainer)) return null;
            IReadOnlyList<BarraRevit> barras = ArmaduraRevit.Barras(doc, Compat.Numero(e.Id));
            if (barras == null || barras.Count == 0 || barras.Any(b => b.Peso <= 0)) return null;
            return barras.Sum(b => b.Peso);
        }

        private static double? ComprimentoDoLance(StairsRun r)
        {
            try { return r?.GetStairsPath().Sum(c => c.Length) * Pe; }
            catch { return null; }
        }

        private static double? Soma<T>(IEnumerable<T> itens, Func<T, double?> f)
        {
            double s = 0;
            foreach (T x in itens) s += f(x) ?? 0;
            return s > 0 ? s : (double?)null;
        }

        /// <summary>Volume, área das faces voltadas para cima e área de todas as faces, em unidades internas (pés).</summary>
        private static (double Volume, double AreaTopo, double AreaTotal) Geometria(Document doc, Element e)
        {
            double v = 0, topo = 0, total = 0;
            var opcoes = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = false };
            var elementos = new List<Element> { e };
            if (e is Stairs s)
            {
                elementos.AddRange(s.GetStairsRuns().Select(doc.GetElement));
                elementos.AddRange(s.GetStairsLandings().Select(doc.GetElement));
                elementos.AddRange(s.GetStairsSupports().Select(doc.GetElement));
            }
            foreach (Element x in elementos.Where(x => x != null))
            {
                GeometryElement g;
                try { g = x.get_Geometry(opcoes); }
                catch { continue; }
                if (g != null) Somar(g, ref v, ref topo, ref total, 0);
            }
            return (v, topo, total);
        }

        private static void Somar(GeometryElement g, ref double v, ref double topo, ref double total, int nivel)
        {
            if (nivel > 4) return;
            foreach (GeometryObject o in g)
            {
                if (o is Solid sol && sol.Volume > 0)
                {
                    v += sol.Volume;
                    foreach (Face f in sol.Faces)
                    {
                        total += f.Area;
                        if (f is PlanarFace pf && pf.FaceNormal.Z > 0.7) topo += f.Area;
                    }
                }
                else if (o is GeometryInstance gi)
                {
                    GeometryElement ig = gi.GetInstanceGeometry();
                    if (ig != null) Somar(ig, ref v, ref topo, ref total, nivel + 1);
                }
            }
        }

        // ------------------------------------------------------------------ telas soldadas

        public static TelaRevit Tela(Document doc, long id)
        {
            try
            {
                if (!(doc.GetElement(Compat.NovoId(id)) is FabricSheet fs) || !(doc.GetElement(fs.GetTypeId()) is FabricSheetType t)) return null;
                double D(ElementId w) => doc.GetElement(w) is FabricWireType fw ? fw.WireDiameter * Pe : 0;
                double massa = 0;
                try { massa = UnitUtils.ConvertFromInternalUnits(fs.CutSheetMass, UnitTypeId.Kilograms); } catch { }
                return new TelaRevit
                {
                    Comprimento = fs.CutOverallLength * Pe,
                    Largura = fs.CutOverallWidth * Pe,
                    DiametroLongitudinal = D(t.MajorDirectionWireType),
                    DiametroTransversal = D(t.MinorDirectionWireType),
                    EspacamentoLongitudinal = t.MajorSpacing * Pe,
                    EspacamentoTransversal = t.MinorSpacing * Pe,
                    Peso = massa,
                };
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------ sistemas MEP

        public static IReadOnlyList<SistemaRevit> Sistemas(Document doc, long id)
        {
            var lista = new List<SistemaRevit>();
            try
            {
                Element e = doc.GetElement(Compat.NovoId(id));
                var vistos = new HashSet<long>();
                void Add(MEPSystem s)
                {
                    if (s == null || !vistos.Add(Compat.Numero(s.Id))) return;
                    lista.Add(new SistemaRevit
                    {
                        Id = Compat.Numero(s.Id),
                        Nome = s.Name,
                        Tipo = doc.GetElement(s.GetTypeId())?.Name,
                        Predefinido = Predefinido(s),
                    });
                }
                if (e is MEPCurve mc) Add(mc.MEPSystem);
                if (e is FamilyInstance fi && fi.MEPModel != null)
                {
                    ConnectorSet cs = fi.MEPModel.ConnectorManager?.Connectors;
                    if (cs != null)
                        foreach (Connector c in cs)
                        {
                            try { Add(c.MEPSystem); } catch { }
                        }
                    try { foreach (ElectricalSystem es in fi.MEPModel.GetElectricalSystems()) Add(es); } catch { }
                }
            }
            catch { /* sem sistema */ }
            return lista;
        }

        private static string Predefinido(MEPSystem s)
        {
            switch (s)
            {
                case PipingSystem p:
                    switch (p.SystemType)
                    {
                        case PipeSystemType.DomesticHotWater: return "DOMESTICHOTWATER";
                        case PipeSystemType.DomesticColdWater: return "DOMESTICCOLDWATER";
                        case PipeSystemType.Sanitary: return "DRAINAGE";
                        case PipeSystemType.Vent: return "VENT";
                        case PipeSystemType.FireProtectWet:
                        case PipeSystemType.FireProtectDry:
                        case PipeSystemType.FireProtectPreaction:
                        case PipeSystemType.FireProtectOther: return "FIREPROTECTION";
                        case PipeSystemType.SupplyHydronic:
                        case PipeSystemType.ReturnHydronic: return "HEATING";
                        default: return PorNome(s);
                    }
                case MechanicalSystem m:
                    switch (m.SystemType)
                    {
                        case DuctSystemType.SupplyAir:
                        case DuctSystemType.ReturnAir: return "VENTILATION";
                        case DuctSystemType.ExhaustAir: return "EXHAUST";
                        default: return PorNome(s);
                    }
                case ElectricalSystem el:
                    switch (el.SystemType)
                    {
                        case ElectricalSystemType.PowerCircuit: return "ELECTRICAL";
                        case ElectricalSystemType.Data: return "DATA";
                        case ElectricalSystemType.Telephone: return "TELEPHONE";
                        case ElectricalSystemType.Security: return "SECURITY";
                        case ElectricalSystemType.FireAlarm: return "FIREPROTECTION";
                        case ElectricalSystemType.Controls: return "CONTROL";
                        case ElectricalSystemType.Communication:
                        case ElectricalSystemType.NurseCall: return "COMMUNICATION";
                        default: return PorNome(s);
                    }
                default: return PorNome(s);
            }
        }

        /// <summary>Sistemas "Outro": pelo nome (águas pluviais, esgoto, gás, incêndio).</summary>
        private static string PorNome(MEPSystem s)
        {
            string n = ((s.Name ?? "") + " " + (s.Document.GetElement(s.GetTypeId())?.Name ?? "")).ToLowerInvariant();
            if (n.Contains("pluvia") || n.Contains("storm") || n.Contains("rain")) return "STORMWATER";
            if (n.Contains("esgoto") || n.Contains("sewage")) return "SEWAGE";
            if (n.Contains("gás") || n.Contains("gas ")) return "GAS";
            if (n.Contains("incêndio") || n.Contains("incendio") || n.Contains("hidrante") || n.Contains("sprinkler")) return "FIREPROTECTION";
            if (n.Contains("drenagem") || n.Contains("drain")) return "DRAINAGE";
            return null;
        }

        // ------------------------------------------------------------------ materiais

        public static IReadOnlyList<MaterialRevit> Materiais(Document doc, long id)
        {
            try
            {
                Element e = doc.GetElement(Compat.NovoId(id));
                if (e == null) return null;
                var vols = new List<(string Nome, double Vol)>();
                foreach (ElementId m in e.GetMaterialIds(false))
                {
                    string nome = doc.GetElement(m)?.Name;
                    if (string.IsNullOrWhiteSpace(nome)) continue;
                    double v = 0;
                    try { v = e.GetMaterialVolume(m); } catch { }
                    vols.Add((nome, v));
                }
                if (vols.Count == 0)
                {
                    Element tipo = doc.GetElement(e.GetTypeId());
                    foreach (BuiltInParameter b in new[] { BuiltInParameter.STRUCTURAL_MATERIAL_PARAM, BuiltInParameter.MATERIAL_ID_PARAM, BuiltInParameter.RBS_PIPE_MATERIAL_PARAM })
                    {
                        ElementId m = e.get_Parameter(b)?.AsElementId() ?? tipo?.get_Parameter(b)?.AsElementId();
                        string nome = m != null && m != ElementId.InvalidElementId ? doc.GetElement(m)?.Name : null;
                        if (nome != null) { vols.Add((nome, 0)); break; }
                    }
                }
                if (vols.Count == 0) return null;
                double total = vols.Sum(x => x.Vol);
                // Pintura e acabamentos sem volume saem quando há material com volume.
                if (total > 0) vols = vols.Where(x => x.Vol > 0).ToList();
                return vols.OrderByDescending(x => x.Vol)
                    .Select(x => new MaterialRevit { Nome = x.Nome, Fracao = total > 0 ? x.Vol / total : 0 })
                    .ToList();
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------ MetroQto (MetroDATA ou property sets)

        private static (string Chave, IReadOnlyDictionary<string, IReadOnlyList<string>> Valor) _cacheMetroQto;

        /// <summary>
        /// Propriedades do MetroQto que o MetroSP sabe calcular, por classe IFC: da planilha MetroDATA da Entrega (se houver)
        /// e do .txt de property sets da exportação. Fica guardado enquanto os arquivos não mudam.
        /// </summary>
        public static IReadOnlyDictionary<string, IReadOnlyList<string>> MetroQto(string metroData, string propertySets)
        {
            string Assinatura(string f) => !string.IsNullOrEmpty(f) && File.Exists(f) ? f + "@" + File.GetLastWriteTimeUtc(f).Ticks : "";
            string chave = Assinatura(metroData) + "|" + Assinatura(propertySets);
            if (_cacheMetroQto.Chave == chave && _cacheMetroQto.Valor != null) return _cacheMetroQto.Valor;

            var d = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            void Add(string classe, string prop)
            {
                if (string.IsNullOrWhiteSpace(classe) || !CorrecoesIfc.MetroQtoCalculavel(prop)) return;
                string c = classe.Trim().Split('.')[0].ToUpperInvariant();
                if (c.EndsWith("TYPE")) return;   // pset de tipo: o valor é por elemento
                if (!d.TryGetValue(c, out var s)) d[c] = s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                s.Add(prop.Trim());
            }
            if (!string.IsNullOrEmpty(metroData) && File.Exists(metroData))
            {
                try
                {
                    foreach (Conferencia.MetroData.Linha l in Conferencia.MetroData.Ler(metroData).Linhas)
                        if (string.Equals(l.Pset, "MetroQto", StringComparison.OrdinalIgnoreCase))
                            foreach (string c in l.ClassesIfc) Add(c, l.IfcPropriedade ?? l.Propriedade);
                }
                catch { /* planilha que não deu para ler: fica o .txt */ }
            }
            if (!string.IsNullOrEmpty(propertySets) && File.Exists(propertySets))
            {
                try
                {
                    List<string> classes = null;
                    foreach (string linha in File.ReadAllLines(propertySets))
                    {
                        if (linha.StartsWith("#")) continue;
                        string[] p = linha.Split('\t');
                        if (p[0].StartsWith("PropertySet:", StringComparison.OrdinalIgnoreCase))
                        {
                            classes = p.Length >= 4 && string.Equals(p[1].Trim(), "MetroQto", StringComparison.OrdinalIgnoreCase) &&
                                      p[2].Trim().Equals("I", StringComparison.OrdinalIgnoreCase)
                                ? p[3].Split(',').Select(x => x.Trim()).ToList() : null;
                            continue;
                        }
                        if (classes != null && p.Length >= 3 && p[0].Length == 0)
                            foreach (string c in classes) Add(c, p[1]);
                    }
                }
                catch { /* .txt que não deu para ler */ }
            }
            IReadOnlyDictionary<string, IReadOnlyList<string>> valor =
                d.ToDictionary(k => k.Key, k => (IReadOnlyList<string>)k.Value.ToList(), StringComparer.OrdinalIgnoreCase);
            _cacheMetroQto = (chave, valor);
            return valor;
        }
    }
}
