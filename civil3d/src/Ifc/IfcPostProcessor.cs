using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MetroSP.Alinhamento;

namespace MetroSP.Ifc
{
    /// <summary>
    /// Ajusta o IFC exportado pelo Civil 3D (IFC Extension):
    ///  - substitui o eixo de cada IfcAlignment por um eixo 3D com as cotas do perfil;
    ///  - acrescenta o IfcAlignmentVertical quando o arquivo não tem;
    ///  - aplica os dados de projeto, terreno (IfcSite) e edificações (IfcBuilding) da configuração MetroSP.
    /// </summary>
    public class IfcPostProcessor
    {
        readonly StepModel _m;
        readonly Action<string> _log;

        public IfcPostProcessor(StepModel model, Action<string> log)
        {
            _m = model;
            _log = log ?? (_ => { });
        }

        public static void ProcessFile(string ifcPath, IList<AlignmentModel> alignments, SpatialConfig spatial,
                                       IDictionary<string, string> projectProperties, Action<string> log,
                                       string sourceFile = null, Config.PsetsOptions psets = null,
                                       IList<Terreno.SurfaceModel> surfaces = null)
        {
            var model = StepModel.Load(ifcPath);
            var pp = new IfcPostProcessor(model, log) { SourceFile = sourceFile };
            if (alignments != null && alignments.Count > 0)
            {
                pp.ApplyAlignments(alignments);
                try { pp.ApplyStationEquations(alignments); }
                catch (Exception ex) { log?.Invoke("equações de estaca não foram gravadas: " + ex.Message); }
            }
            var ptl = spatial == null || spatial.CoordenadasPtl;
            if (ptl) pp.ApplyPtl(spatial?.Terreno);
            if (spatial != null) pp.ApplySpatial(spatial);
            if (projectProperties != null && projectProperties.Count > 0) pp.ApplyProjectProperties(projectProperties);
            if (surfaces != null && surfaces.Count > 0)
            {
                try { pp.ApplySurfaces(surfaces, ptl); }
                catch (Exception ex) { log?.Invoke("superfícies não foram gravadas: " + ex.Message); }
            }
            // Antes da limpeza dos Psets: os dados dos pontos COGO estão em Psets fora do padrão (C3D_Attributes...).
            try { pp.ApplyCogoPoints(); }
            catch (Exception ex) { log?.Invoke("dados dos pontos COGO não foram gravados no MetroSP_Ponto: " + ex.Message); }
            try { IfcClassificacao.Aplicar(model, RegraIfc.DosPadroes(), log); }
            catch (Exception ex) { log?.Invoke("classificação não foi gravada como IfcClassificationReference: " + ex.Message); }
            // Depois da classificação (ela lê os Psets de classificação) e antes da marca MetroSP_Projeto.
            try { LimpezaPsets.Aplicar(model, psets?.Permitidos, psets == null || psets.ApagarForaDoPadrao, log); }
            catch (Exception ex) { log?.Invoke("os property sets fora do padrão não foram apagados: " + ex.Message); }
            // Por último: Plugin e VersaoPlugin no Pset MetroSP_Projeto e a marca no cabeçalho.
            try { OrigemIfc.Marcar(model, "MetroSP Civil 3D", log); }
            catch (Exception ex) { log?.Invoke("origem MetroSP não foi gravada no IFC: " + ex.Message); }

            var backup = Path.ChangeExtension(ifcPath, ".autodesk.ifc");
            File.Copy(ifcPath, backup, overwrite: true);
            model.Save(ifcPath);
            log?.Invoke("original da Autodesk guardado em " + Path.GetFileName(backup));
        }

        // ================================================================== alinhamentos

        public int ApplyAlignments(IList<AlignmentModel> alignments)
        {
            if (!_m.Schema.StartsWith("IFC4X3"))
                _log($"aviso: esquema {_m.Schema}; IfcAlignmentVertical só existe no IFC 4.3, só o eixo 3D será aplicado.");

            var byName = alignments.GroupBy(a => Norm(a.Name)).ToDictionary(g => g.Key, g => g.First());
            int axisCtx = AxisContext();
            int done = 0;

            // A IFC 4.3 Extension grava cada perfil como um IfcAlignment filho do alinhamento (agregado a ele),
            // com horizontal compartilhado e vertical próprio. Os filhos ficam como vieram: só os de nível
            // superior são casados com o desenho.
            var children = ChildAlignments();
            var ifcAlignments = _m.OfType("IFCALIGNMENT").Where(al => !children.ContainsKey(al.Id) || children[al.Id] == 0).ToList();
            var used = new HashSet<AlignmentModel>();
            var empty = new List<(StepEntity al, AlignmentModel a)>();
            double[] knownShift = null;
            foreach (var al in ifcAlignments)
            {
                var name = StepModel.Text(al.Args[2]);
                var a = Match(name, byName, ifcAlignments.Count);
                if (a != null) used.Add(a);
                if (a == null)
                {
                    _log($"IfcAlignment \"{name}\" sem alinhamento correspondente no modelo; mantido como veio.");
                    continue;
                }

                var toLocal = PlacementMatrix(StepModel.Ref(al.Args[5])).Inverse();
                var hasHorizontal = LayoutSegments(al.Id, "IFCALIGNMENTHORIZONTAL").Any();
                var shift = hasHorizontal ? DetectShift(al.Id, a, toLocal) : new double[3];
                if (knownShift == null && hasHorizontal && a.Horizontal.Count > 0) knownShift = shift;

                var profiles = children.Where(kv => kv.Value == al.Id && HasLayout(kv.Key, "IFCALIGNMENTVERTICAL"))
                    .Select(kv => StepModel.Text(_m.Get(kv.Key).Args[2]) ?? "").ToList();
                if (HasLayout(al.Id, "IFCALIGNMENTVERTICAL")) profiles.Add(name);
                if (profiles.Count > 0)
                {
                    var chosen = a.ProfileName != null && profiles.Any(p => p.IndexOf(a.ProfileName, StringComparison.OrdinalIgnoreCase) >= 0);
                    _log($"{a.Name}: a Autodesk já gravou o 3D ({profiles.Count} perfil(is)" +
                         (a.ProfileName == null ? "" : chosen ? $", entre eles {a.ProfileName}" : $"; {a.ProfileName} não está entre eles") + "); mantido como veio.");
                    done++;
                    continue;
                }
                if (!hasHorizontal)
                {
                    if (a.Horizontal.Count > 0) empty.Add((al, a));
                    else _log($"{a.Name}: veio sem geometria e o modelo não tem horizontal; mantido como veio.");
                    continue;
                }
                if (a.Axis3D.Count < 2)
                {
                    _log($"{a.Name}: sem perfil, mantido como veio.");
                    continue;
                }

                double[] Local(double[] p) => toLocal.Apply(p[0], p[1], p[2], shift);

                ReplaceAxis(al, a, axisCtx, Local);
                if (_m.Schema.StartsWith("IFC4X3")) EnsureVertical(al, a, Local);
                done++;
                _log($"{a.Name}: eixo 3D aplicado ({a.Axis3D.Count} pontos, perfil {a.ProfileName}).");
            }

            _knownShift = knownShift;
            foreach (var (al, a) in empty) done += Refill(al, a, axisCtx, knownShift);

            var missing = alignments.Where(a => !used.Contains(a) && a.Horizontal.Count > 0).ToList();
            if (missing.Count > 0) done += AddMissing(missing, axisCtx, knownShift);
            return done;
        }

        /// <summary>Correção de coordenadas medida nos alinhamentos da Autodesk (desenho → IFC), ou null.</summary>
        double[] _knownShift;

        /// <summary>Nome do DWG, gravado no Pset MetroSP_Alinhamento dos alinhamentos acrescentados.</summary>
        public string SourceFile { get; set; }

        /// <summary>
        /// Alinhamentos do desenho que o exportador da Autodesk não gravou: entram completos (horizontal,
        /// vertical, eixo 3D), agregados ao IfcProject e referenciados no IfcSite, como no IFC 4.3.
        /// </summary>
        /// <summary>IfcAlignment filho → IfcAlignment pai (pai 0 = agregado a outra coisa).</summary>
        Dictionary<int, int> ChildAlignments()
        {
            var map = new Dictionary<int, int>();
            foreach (var r in _m.OfType("IFCRELAGGREGATES"))
            {
                var parent = StepModel.Ref(r.Args[4]);
                if (_m.TypeOf(parent) != "IFCALIGNMENT") continue;
                foreach (var c in StepModel.RefList(r.Args[5]))
                    if (_m.TypeOf(c) == "IFCALIGNMENT") map[c] = parent;
            }
            return map;
        }

        bool HasLayout(int alignmentId, string layoutType) =>
            _m.OfType("IFCRELNESTS").Any(n => StepModel.Ref(n.Args[4]) == alignmentId &&
                                              StepModel.RefList(n.Args[5]).Any(r => _m.TypeOf(r) == layoutType));

        /// <summary>
        /// IfcAlignment que a Autodesk gravou vazio (horizontal sem segmentos, sem perfis): recebe horizontal,
        /// vertical e eixo 3D do desenho no próprio lugar, mantendo GUID, nome, propriedades e estacas.
        /// </summary>
        int Refill(StepEntity al, AlignmentModel a, int axisCtx, double[] knownShift)
        {
            var site = _m.OfType("IFCSITE").FirstOrDefault();
            var placement = StepModel.Ref(al.Args[5]);
            var toLocal = (placement != 0 ? PlacementMatrix(placement) : Matrix.Identity).Inverse();
            var shift = knownShift ?? new double[3];
            double[] Local(double[] p) => toLocal.Apply(p[0], p[1], p.Length > 2 ? p[2] : 0, shift);
            var o = Local(new[] { 0.0, 0.0, 0.0 });
            var x = Local(new[] { 1.0, 0.0, 0.0 });
            var rotation = Math.Atan2(x[1] - o[1], x[0] - o[0]);
            if (knownShift == null)
                _log($"aviso: {a.Name}: nenhum alinhamento da Autodesk para conferir o deslocamento de coordenadas.");

            // Sai o horizontal vazio (e a polilinha 2D dele); o resto do alinhamento fica.
            foreach (var n in _m.OfType("IFCRELNESTS").Where(n => StepModel.Ref(n.Args[4]) == al.Id).ToList())
            {
                var related = StepModel.RefList(n.Args[5]);
                var layouts = related.Where(r => _m.TypeOf(r) == "IFCALIGNMENTHORIZONTAL").ToList();
                if (layouts.Count == 0) continue;
                foreach (var h in layouts)
                {
                    RemoveShape(StepModel.Ref(_m.Get(h).Args[6]));
                    DetachProperties(h);
                    _m.Remove(h);
                }
                var rest = related.Except(layouts).ToList();
                if (rest.Count == 0) _m.Remove(n.Id);
                else _m.SetArg(n.Id, 5, StepModel.RefListText(rest));
            }
            RemoveShape(StepModel.Ref(al.Args[6]));

            var gid = StepModel.Text(al.Args[0]) ?? a.Name;
            var first = _m.LastId + 1;
            var writer = new IfcAlignmentWriter(new IfcExportInfo { SourceFile = SourceFile ?? "", Seed = "refill|" + gid }, _m.AddRaw);
            var temp = writer.WriteAlignment(a, placement != 0 ? "#" + placement : "$", axisCtx, Local, rotation);

            // O que o escritor pendurou no alinhamento novo passa para o da Autodesk.
            var token = "#" + temp;
            for (int id = first; id <= _m.LastId; id++)
            {
                var e = _m.Get(id);
                if (e == null || id == temp) continue;
                for (int i = 0; i < e.Args.Count; i++)
                    if (e.Args[i].Contains(token)) _m.SetArg(id, i, ReplaceRef(e.Args[i], temp, al.Id));
            }
            _m.SetArg(al.Id, 6, _m.Get(temp).Args[6]);
            _m.Remove(temp);
            _log($"{a.Name}: veio vazio da Autodesk; preenchido com o modelo ({a.Horizontal.Count} segmento(s) horizontais, " +
                 $"{a.Vertical.Count} vertical(is), {a.Axis3D.Count} pontos no eixo 3D, perfil {a.ProfileName}).");
            return 1;
        }

        static string ReplaceRef(string arg, int from, int to) =>
            System.Text.RegularExpressions.Regex.Replace(arg, "#" + from + @"(?!\d)", "#" + to);

        /// <summary>Tira a entidade dos IfcRelDefinesByProperties; relação vazia sai, e o pset junto se ninguém mais o usa.</summary>
        void DetachProperties(int id)
        {
            var rels = _m.OfType("IFCRELDEFINESBYPROPERTIES").ToList();
            foreach (var r in rels)
            {
                var objs = StepModel.RefList(r.Args[4]);
                if (!objs.Remove(id)) continue;
                if (objs.Count > 0) { _m.SetArg(r.Id, 4, StepModel.RefListText(objs)); continue; }
                var pset = StepModel.Ref(r.Args[5]);
                _m.Remove(r.Id);
                if (pset != 0 && !rels.Any(o => o.Id != r.Id && StepModel.Ref(o.Args[5]) == pset)) _m.Remove(pset);
            }
        }

        /// <summary>
        /// Remove um IfcProductDefinitionShape e as representações dele. As curvas ficam: a Autodesk as
        /// referencia também em estilos e layers.
        /// </summary>
        void RemoveShape(int pds)
        {
            if (pds == 0 || _m.TypeOf(pds) != "IFCPRODUCTDEFINITIONSHAPE") return;
            foreach (var rep in StepModel.RefList(_m.Get(pds).Args[2]))
                if (_m.TypeOf(rep) == "IFCSHAPEREPRESENTATION") _m.Remove(rep);
            _m.Remove(pds);
        }

        int AddMissing(List<AlignmentModel> missing, int axisCtx, double[] knownShift)
        {
            var project = _m.OfType("IFCPROJECT").FirstOrDefault();
            var site = _m.OfType("IFCSITE").FirstOrDefault();
            if (project == null)
            {
                _log("aviso: IFC sem IfcProject; alinhamentos que faltam não foram acrescentados.");
                return 0;
            }

            // Mesmo sistema dos outros elementos: relativo ao IfcSite, com o deslocamento já visto no arquivo.
            var sitePlacement = site != null ? StepModel.Ref(site.Args[5]) : 0;
            var toLocal = (sitePlacement != 0 ? PlacementMatrix(sitePlacement) : Matrix.Identity).Inverse();
            var shift = knownShift ?? new double[3];
            double[] Local(double[] p) => toLocal.Apply(p[0], p[1], p.Length > 2 ? p[2] : 0, shift);
            var o = Local(new[] { 0.0, 0.0, 0.0 });
            var x = Local(new[] { 1.0, 0.0, 0.0 });
            var rotation = Math.Atan2(x[1] - o[1], x[0] - o[0]);
            if (knownShift == null && sitePlacement != 0)
                _log("aviso: nenhum alinhamento da Autodesk para conferir o deslocamento de coordenadas; usando a colocação do IfcSite.");

            var seed = StepModel.Text(project.Args[0]) ?? "";
            var writer = new IfcAlignmentWriter(new IfcExportInfo { SourceFile = SourceFile ?? "", Seed = "posproc|" + seed }, _m.AddRaw);
            var ids = new List<int>();
            foreach (var a in missing)
            {
                ids.Add(writer.WriteAlignment(a, sitePlacement != 0 ? "#" + sitePlacement : "$", axisCtx, Local, rotation));
                _log($"{a.Name}: não veio no IFC da Autodesk; acrescentado completo ({a.Horizontal.Count} segmento(s) horizontais, " +
                     $"{a.Vertical.Count} vertical(is), {a.Axis3D.Count} pontos no eixo 3D).");
            }

            _m.Add("IFCRELAGGREGATES", Guid(seed, "metrosp-project-alignments"), "$", "$", "$", "#" + project.Id, StepModel.RefListText(ids));
            if (site != null)
                _m.Add("IFCRELREFERENCEDINSPATIALSTRUCTURE", Guid(seed, "metrosp-site-alignments"), "$", "$", "$", StepModel.RefListText(ids), "#" + site.Id);
            return ids.Count;
        }

        /// <summary>
        /// Confere o ponto inicial do horizontal da Autodesk contra o do Civil 3D já levado ao sistema local.
        /// Se a Autodesk tiver deslocado as coordenadas de outro jeito, devolve a translação que falta.
        /// </summary>
        double[] DetectShift(int alignmentId, AlignmentModel a, Matrix toLocal)
        {
            var zero = new double[3];
            var hSeg = LayoutSegments(alignmentId, "IFCALIGNMENTHORIZONTAL").FirstOrDefault();
            if (hSeg == 0 || a.Horizontal.Count == 0) return zero;

            var dp = _m.Get(StepModel.Ref(_m.Get(hSeg).Args[7]));
            if (dp == null || dp.Type != "IFCALIGNMENTHORIZONTALSEGMENT") return zero;
            var sp = _m.Get(StepModel.Ref(dp.Args[2]));
            var ifc = StepModel.RealList(sp.Args[0]);
            var mine = toLocal.Apply(a.Horizontal[0].StartX, a.Horizontal[0].StartY, 0, zero);
            var dx = ifc[0] - mine[0];
            var dy = ifc[1] - mine[1];
            if (Math.Abs(dx) < 1e-3 && Math.Abs(dy) < 1e-3) return zero;

            _log($"{a.Name}: coordenadas do IFC deslocadas em ({dx:0.###}; {dy:0.###}) em relação ao modelo; deslocamento aplicado.");
            return new[] { dx, dy, 0.0 };
        }

        void ReplaceAxis(StepEntity al, AlignmentModel a, int axisCtx, Func<double[], double[]> local)
        {
            var coords = string.Join(",", a.Axis3D.Select(p =>
            {
                var q = local(p);
                return "(" + IfcAlignmentWriter.F(q[0]) + "," + IfcAlignmentWriter.F(q[1]) + "," + IfcAlignmentWriter.F(q[2]) + ")";
            }));
            var pts = _m.Add("IFCCARTESIANPOINTLIST3D", "(" + coords + ")", "$");
            var curve = _m.Add("IFCINDEXEDPOLYCURVE", "#" + pts, "$", ".F.");
            var rep = _m.Add("IFCSHAPEREPRESENTATION", "#" + axisCtx, "'Axis'", "'Curve3D'", "(#" + curve + ")");

            var shapeId = StepModel.Ref(al.Args[6]);
            if (shapeId == 0)
            {
                var pds = _m.Add("IFCPRODUCTDEFINITIONSHAPE", "$", "$", "(#" + rep + ")");
                _m.SetArg(al.Id, 6, "#" + pds);
                return;
            }

            // Mantém as outras representações (ex.: FootPrint 2D) e troca só o eixo.
            var shape = _m.Get(shapeId);
            var all = StepModel.RefList(shape.Args[2]);
            var oldAxis = all.Where(r => StepModel.Text(_m.Get(r).Args[1]) == "Axis").ToList();
            var reps = all.Except(oldAxis).ToList();
            reps.Add(rep);
            _m.SetArg(shapeId, 2, StepModel.RefListText(reps));
            // A representação antiga ficaria órfã (regra do IFC): sai do arquivo. A curva em si fica,
            // porque pode ser a base de outros elementos (ex.: IfcLinearPlacement).
            foreach (var r in oldAxis) _m.Remove(r);
        }

        void EnsureVertical(StepEntity al, AlignmentModel a, Func<double[], double[]> local)
        {
            if (a.Vertical.Count == 0) return;
            var nest = _m.OfType("IFCRELNESTS").FirstOrDefault(n => StepModel.Ref(n.Args[4]) == al.Id);
            var related = nest == null ? new List<int>() : StepModel.RefList(nest.Args[5]);
            if (related.Any(r => _m.TypeOf(r) == "IFCALIGNMENTVERTICAL"))
                return;   // a Autodesk já gravou o vertical: não mexe na semântica

            // Cotas no sistema do alinhamento: só a translação em Z importa.
            var dz = local(new[] { 0.0, 0.0, 0.0 })[2];
            var gid = StepModel.Text(al.Args[0]) ?? a.Name;

            var vertical = _m.Add("IFCALIGNMENTVERTICAL", Guid(gid, "v"), "$", IfcAlignmentWriter.S(a.Name + " - Vertical (" + a.ProfileName + ")"), "$", "$", al.Args[5], "$");
            var segs = new List<int>();
            for (int i = 0; i < a.Vertical.Count; i++)
            {
                var v = a.Vertical[i];
                var dp = _m.Add("IFCALIGNMENTVERTICALSEGMENT", "$", "$",
                    IfcAlignmentWriter.F(v.StartDistAlong), IfcAlignmentWriter.F(v.HorizontalLength),
                    IfcAlignmentWriter.F(v.StartHeight + dz), IfcAlignmentWriter.F(v.StartGradient), IfcAlignmentWriter.F(v.EndGradient),
                    v.Radius == 0 ? "$" : IfcAlignmentWriter.F(v.Radius), "." + v.Type + ".");
                segs.Add(_m.Add("IFCALIGNMENTSEGMENT", Guid(gid, "v" + i), "$", IfcAlignmentWriter.S($"V{i + 1} {v.Type}"), "$", "$", al.Args[5], "$", "#" + dp));
            }
            _m.Add("IFCRELNESTS", Guid(gid, "vn"), "$", "$", "$", "#" + vertical, StepModel.RefListText(segs));

            if (nest == null)
            {
                _m.Add("IFCRELNESTS", Guid(gid, "n"), "$", "$", "$", "#" + al.Id, "(#" + vertical + ")");
            }
            else
            {
                // ordem exigida: horizontal, vertical, superelevação
                var h = related.FindIndex(r => _m.TypeOf(r) == "IFCALIGNMENTHORIZONTAL");
                related.Insert(h + 1, vertical);
                _m.SetArg(nest.Id, 5, StepModel.RefListText(related));
            }
            _log($"{a.Name}: IfcAlignmentVertical acrescentado ({a.Vertical.Count} segmentos).");
        }

        IEnumerable<int> LayoutSegments(int alignmentId, string layoutType)
        {
            var nests = _m.OfType("IFCRELNESTS").ToList();
            var layout = nests.Where(n => StepModel.Ref(n.Args[4]) == alignmentId)
                .SelectMany(n => StepModel.RefList(n.Args[5]))
                .FirstOrDefault(r => _m.TypeOf(r) == layoutType);
            if (layout == 0) return Enumerable.Empty<int>();
            return nests.Where(n => StepModel.Ref(n.Args[4]) == layout).SelectMany(n => StepModel.RefList(n.Args[5]));
        }

        int AxisContext()
        {
            foreach (var c in _m.OfType("IFCGEOMETRICREPRESENTATIONSUBCONTEXT"))
                if (StepModel.Text(c.Args[0]) == "Axis") return c.Id;

            var model = _m.OfType("IFCGEOMETRICREPRESENTATIONCONTEXT").FirstOrDefault(c => StepModel.Text(c.Args[1]) == "Model")
                        ?? _m.OfType("IFCGEOMETRICREPRESENTATIONCONTEXT").First();
            return _m.Add("IFCGEOMETRICREPRESENTATIONSUBCONTEXT", "'Axis'", "'Model'", "*", "*", "*", "*", "#" + model.Id, "$", ".MODEL_VIEW.", "$");
        }

        // ================================================================== equações de estaca

        /// <summary>
        /// Equações de estaca do desenho no Pset_Stationing dos IfcReferent (padrão IFC 4.3): no ponto da equação,
        /// Station = estaca adiante e IncomingStation = estaca atrás. Usa o IfcReferent da Autodesk que estiver na
        /// mesma distância ao longo do eixo; se não houver, acrescenta um, sobre a mesma curva dos outros.
        /// </summary>
        public int ApplyStationEquations(IList<AlignmentModel> alignments)
        {
            var comEquacao = alignments.Where(a => a.Equations.Count > 0).ToList();
            if (comEquacao.Count == 0) return 0;
            var byName = alignments.GroupBy(a => Norm(a.Name)).ToDictionary(g => g.Key, g => g.First());
            var children = ChildAlignments();
            var tops = _m.OfType("IFCALIGNMENT").Where(al => !children.ContainsKey(al.Id) || children[al.Id] == 0).ToList();
            int feitas = 0;
            foreach (var al in tops)
            {
                var a = Match(StepModel.Text(al.Args[2]), byName, tops.Count);
                if (a == null || a.Equations.Count == 0) continue;

                // IfcReferent pendurados no alinhamento e a distância de cada um
                var referentes = _m.OfType("IFCRELNESTS").Where(n => StepModel.Ref(n.Args[4]) == al.Id)
                    .SelectMany(n => StepModel.RefList(n.Args[5])).Where(r => _m.TypeOf(r) == "IFCREFERENT")
                    .Select(r => (id: r, dist: DistanciaAoLongo(r))).Where(t => t.dist.HasValue).ToList();
                // só os de estaca (.STATION.): um marco (REFERENCEMARKER) no mesmo ponto não vira equação
                var estacas = referentes.Where(t => _m.Get(t.id).Args.Count > 7 && _m.Get(t.id).Args[7].Trim() == ".STATION.").ToList();

                foreach (var eq in a.Equations)
                {
                    var r = estacas.Where(t => Math.Abs(t.dist.Value - eq.DistAlong) < 1e-3).Select(t => t.id).FirstOrDefault();
                    if (r == 0) r = NovoReferente(al, eq, referentes);
                    if (r == 0)
                    {
                        _log($"aviso: {a.Name}: equação de estaca {eq.StationBack:0.000} = {eq.StationAhead:0.000} sem IfcReferent de referência no IFC; não gravada.");
                        continue;
                    }
                    Stationing(r, eq);
                    feitas++;
                }
                _log($"{a.Name}: {a.Equations.Count} equação(ões) de estaca no Pset_Stationing.");
            }
            return feitas;
        }

        /// <summary>Distância ao longo do eixo de um IfcReferent posicionado por IfcLinearPlacement.</summary>
        double? DistanciaAoLongo(int referente)
        {
            var lp = _m.Get(StepModel.Ref(_m.Get(referente).Args[5]));
            if (lp == null || lp.Type != "IFCLINEARPLACEMENT") return null;
            var ax = _m.Get(StepModel.Ref(lp.Args[1]));
            if (ax == null || ax.Type != "IFCAXIS2PLACEMENTLINEAR") return null;
            var pt = _m.Get(StepModel.Ref(ax.Args[0]));
            if (pt == null || pt.Type != "IFCPOINTBYDISTANCEEXPRESSION") return null;
            var mm = System.Text.RegularExpressions.Regex.Match(pt.Args[0], @"-?\d+(\.\d*)?([eE][-+]?\d+)?");
            return mm.Success ? StepModel.Real(mm.Value) : (double?)null;
        }

        /// <summary>IfcReferent .STATION. novo na distância da equação, sobre a mesma curva e colocação do modelo.</summary>
        int NovoReferente(StepEntity al, StationEquationModel eq, List<(int id, double? dist)> referentes)
        {
            if (referentes.Count == 0) return 0;
            var modelo = referentes[0].id;
            var lp = _m.Get(StepModel.Ref(_m.Get(modelo).Args[5]));
            var ax = _m.Get(StepModel.Ref(lp.Args[1]));
            var pt = _m.Get(StepModel.Ref(ax.Args[0]));
            var gid = (StepModel.Text(al.Args[0]) ?? "") + "|eq|" + IfcAlignmentWriter.F(eq.DistAlong);
            var dist = _m.Add("IFCPOINTBYDISTANCEEXPRESSION", "IFCLENGTHMEASURE(" + IfcAlignmentWriter.F(eq.DistAlong) + ")", "$", "$", "$", pt.Args[4]);
            var axis = _m.Add("IFCAXIS2PLACEMENTLINEAR", "#" + dist, ax.Args[1], ax.Args[2]);
            var place = _m.Add("IFCLINEARPLACEMENT", lp.Args[0], "#" + axis, PosicaoInterpolada(referentes, eq.DistAlong));
            var r = _m.Add("IFCREFERENT", Guid(gid, "referente"), "$", IfcAlignmentWriter.S(eq.StationAhead.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)),
                IfcAlignmentWriter.S($"Equação de estaca: {eq.StationBack:0.000} = {eq.StationAhead:0.000}"), "$", "#" + place, "$", ".STATION.");
            _m.Add("IFCRELNESTS", Guid(gid, "nest"), "$", "'AlignmentStationEquation'", "$", "#" + al.Id, "(#" + r + ")");
            return r;
        }

        /// <summary>
        /// CartesianPosition do IfcLinearPlacement (visualizadores como o web-ifc usam essa posição): interpolada
        /// entre os dois referentes vizinhos da Autodesk (marcos a cada 20 m, erro desprezível em curva). "$" se não der.
        /// </summary>
        string PosicaoInterpolada(List<(int id, double? dist)> referentes, double dist)
        {
            double[] Pos(int r)
            {
                var lp = _m.Get(StepModel.Ref(_m.Get(r).Args[5]));
                var cp = lp.Args.Count > 2 ? _m.Get(StepModel.Ref(lp.Args[2])) : null;
                if (cp == null || cp.Type != "IFCAXIS2PLACEMENT3D") return null;
                var q = StepModel.RealList(_m.Get(StepModel.Ref(cp.Args[0])).Args[0]);
                while (q.Count < 3) q.Add(0);
                return q.ToArray();
            }
            var com = referentes.Select(t => (t.dist.Value, p: Pos(t.id))).Where(t => t.p != null).OrderBy(t => t.Item1).ToList();
            var antes = com.LastOrDefault(t => t.Item1 <= dist);
            var depois = com.FirstOrDefault(t => t.Item1 >= dist);
            if (antes.p == null || depois.p == null) return "$";
            var span = depois.Item1 - antes.Item1;
            var k = span < 1e-9 ? 0 : (dist - antes.Item1) / span;
            string F(double v) => IfcAlignmentWriter.F(v);
            var pt = _m.Add("IFCCARTESIANPOINT", "(" + F(antes.p[0] + k * (depois.p[0] - antes.p[0])) + "," +
                F(antes.p[1] + k * (depois.p[1] - antes.p[1])) + "," + F(antes.p[2] + k * (depois.p[2] - antes.p[2])) + ")");
            return "#" + _m.Add("IFCAXIS2PLACEMENT3D", "#" + pt, "$", "$");
        }

        /// <summary>Station, IncomingStation e HasIncreasingStation no Pset_Stationing do referente.</summary>
        void Stationing(int referente, StationEquationModel eq)
        {
            var valores = new Dictionary<string, string>
            {
                ["Station"] = "IFCLENGTHMEASURE(" + IfcAlignmentWriter.F(eq.StationAhead) + ")",
                ["IncomingStation"] = "IFCLENGTHMEASURE(" + IfcAlignmentWriter.F(eq.StationBack) + ")",
                ["HasIncreasingStation"] = "IFCBOOLEAN(" + (eq.Increasing ? ".T." : ".F.") + ")",
            };
            var rel = _m.OfType("IFCRELDEFINESBYPROPERTIES").FirstOrDefault(r =>
                StepModel.RefList(r.Args[4]).Contains(referente) &&
                _m.TypeOf(StepModel.Ref(r.Args[5])) == "IFCPROPERTYSET" &&
                StepModel.Text(_m.Get(StepModel.Ref(r.Args[5])).Args[2]) == "Pset_Stationing");
            var novas = new List<int>();
            if (rel != null)
            {
                var pset = _m.Get(StepModel.Ref(rel.Args[5]));
                foreach (var pid in StepModel.RefList(pset.Args[4]))
                {
                    var p = _m.Get(pid);
                    var nome = p != null && p.Type == "IFCPROPERTYSINGLEVALUE" ? StepModel.Text(p.Args[0]) : null;
                    if (nome != null && valores.TryGetValue(nome, out var v)) { _m.SetArg(pid, 2, v); valores.Remove(nome); }
                    novas.Add(pid);
                }
                foreach (var kv in valores) novas.Add(_m.Add("IFCPROPERTYSINGLEVALUE", IfcAlignmentWriter.S(kv.Key), "$", kv.Value, "$"));
                _m.SetArg(pset.Id, 4, StepModel.RefListText(novas));
                return;
            }
            var seed = StepModel.Text(_m.Get(referente).Args[0]) ?? referente.ToString();
            foreach (var kv in valores) novas.Add(_m.Add("IFCPROPERTYSINGLEVALUE", IfcAlignmentWriter.S(kv.Key), "$", kv.Value, "$"));
            var ps = _m.Add("IFCPROPERTYSET", Guid(seed, "pset-stationing"), "$", "'Pset_Stationing'", "$", StepModel.RefListText(novas));
            _m.Add("IFCRELDEFINESBYPROPERTIES", Guid(seed, "pset-stationing-rel"), "$", "$", "$", "(#" + referente + ")", "#" + ps);
        }

        // ================================================================== pontos COGO

        static readonly (string de, string para)[] CamposPonto =
        {
            ("PointNumber", "Numero"), ("PointName", "Nome"), ("RawDescription", "DescricaoBruta"),
            ("FullDescription", "DescricaoCompleta"), ("Easting", "Leste"), ("Northing", "Norte"), ("Elevation", "Cota"),
            ("Latitude", "Latitude"), ("Longitude", "Longitude"),
        };

        /// <summary>Valor da propriedade da Autodesk como vai no MetroSP_Ponto; latitude e longitude vêm em radianos e vão em graus.</summary>
        static string ValorPonto(string campo, string valor)
        {
            if (valor.EndsWith("('')", StringComparison.Ordinal)) return null;   // texto vazio
            if (campo != "Latitude" && campo != "Longitude") return valor;
            var mm = System.Text.RegularExpressions.Regex.Match(valor, @"-?\d+(\.\d*)?([eE][-+]?\d+)?");
            if (!mm.Success) return null;
            var v = StepModel.Real(mm.Value);
            var graus = Math.Abs(v) <= Math.PI + 1e-9 ? v * 180 / Math.PI : v;
            return "IFCREAL(" + IfcAlignmentWriter.F(Math.Round(graus, 9)) + ")";
        }

        /// <summary>
        /// Pontos COGO (IfcAnnotation "CogoPoint" da Autodesk): passam a .SURVEY. (ponto de levantamento, IFC 4.3) e
        /// ganham o Pset MetroSP_Ponto com número, nome, descrições e coordenadas, copiados dos Psets da Autodesk, que
        /// a limpeza dos Psets fora do padrão apaga. Os grupos de pontos já vêm como IfcGroup.
        /// </summary>
        public int ApplyCogoPoints()
        {
            var pontos = _m.OfType("IFCANNOTATION").Where(a => StepModel.Text(a.Args[4]) == "CogoPoint").ToList();
            if (pontos.Count == 0) return 0;
            var ids = new HashSet<int>(pontos.Select(p => p.Id));
            var psetsDe = new Dictionary<int, List<int>>();
            foreach (var r in _m.OfType("IFCRELDEFINESBYPROPERTIES"))
            {
                var ps = StepModel.Ref(r.Args[5]);
                foreach (var o in StepModel.RefList(r.Args[4]))
                    if (ids.Contains(o)) { if (!psetsDe.TryGetValue(o, out var l)) psetsDe[o] = l = new List<int>(); l.Add(ps); }
            }
            int n = 0;
            foreach (var a in pontos)
            {
                var valores = new Dictionary<string, string>(StringComparer.Ordinal);
                if (psetsDe.TryGetValue(a.Id, out var lista))
                    foreach (var ps in lista)
                    {
                        var e = _m.Get(ps);
                        if (e == null || e.Type != "IFCPROPERTYSET" || StepModel.Text(e.Args[2]) == "MetroSP_Ponto") continue;
                        foreach (var pid in StepModel.RefList(e.Args[4]))
                        {
                            var p = _m.Get(pid);
                            if (p == null || p.Type != "IFCPROPERTYSINGLEVALUE" || p.Args[2] == "$") continue;
                            var nome = StepModel.Text(p.Args[0]);
                            if (nome != null && !valores.ContainsKey(nome)) valores[nome] = p.Args[2];
                        }
                    }
                var props = CamposPonto.Where(c => valores.ContainsKey(c.de))
                    .Select(c => (c.para, v: ValorPonto(c.de, valores[c.de]))).Where(t => t.v != null)
                    .Select(t => _m.Add("IFCPROPERTYSINGLEVALUE", IfcAlignmentWriter.S(t.para == "Latitude" || t.para == "Longitude" ? t.para + "Graus" : t.para), "$", t.v, "$")).ToList();
                if (a.Args.Count > 7 && a.Args[7] == "$") _m.SetArg(a.Id, 7, ".SURVEY.");
                if (props.Count == 0) continue;
                var seed = StepModel.Text(a.Args[0]) ?? a.Id.ToString();
                var pset = _m.Add("IFCPROPERTYSET", Guid(seed, "pset-ponto"), "$", "'MetroSP_Ponto'", "$", StepModel.RefListText(props));
                _m.Add("IFCRELDEFINESBYPROPERTIES", Guid(seed, "pset-ponto-rel"), "$", "$", "$", "(#" + a.Id + ")", "#" + pset);
                n++;
            }
            _log($"pontos COGO: {pontos.Count} como ponto de levantamento (SURVEY), {n} com o Pset MetroSP_Ponto.");
            return n;
        }

        // ================================================================== superfícies (terreno)

        /// <summary>
        /// Superfícies TIN do desenho como IfcGeographicElement.TERRAIN (IFC 4.3), com malha de triângulos
        /// (IfcTriangulatedFaceSet, que o web-ifc do Iris desenha), cor de terreno, contidas no IfcSite e com o
        /// Pset MetroSP_Superficie (pontos, triângulos, cotas e áreas). Superfície que a Autodesk já gravou
        /// (mesmo nome num IfcGeographicElement) fica como veio.
        /// </summary>
        public int ApplySurfaces(IList<Terreno.SurfaceModel> surfaces, bool ptl)
        {
            var site = _m.OfType("IFCSITE").FirstOrDefault();
            if (site == null)
            {
                _log("aviso: IFC sem IfcSite; superfícies não gravadas.");
                return 0;
            }
            var existentes = new HashSet<string>(_m.OfType("IFCGEOGRAPHICELEMENT").Select(g => Norm(StepModel.Text(g.Args[2]))));

            // Mesmo sistema do IfcSite: no PTL as coordenadas absolutas do IFC são as do desenho; sem PTL, vale a
            // correção medida nos alinhamentos da Autodesk (como nos alinhamentos acrescentados).
            var sitePlacement = StepModel.Ref(site.Args[5]);
            var toLocal = (sitePlacement != 0 ? PlacementMatrix(sitePlacement) : Matrix.Identity).Inverse();
            var shift = ptl ? new double[3] : _knownShift ?? new double[3];
            if (!ptl && _knownShift == null && sitePlacement != 0)
                _log("aviso: nenhum alinhamento da Autodesk para conferir o deslocamento de coordenadas das superfícies; usando a colocação do IfcSite.");

            var body = BodyContext();
            var seed0 = StepModel.Text(site.Args[0]) ?? "";
            var estilo = 0;
            var ids = new List<int>();
            foreach (var s in surfaces)
            {
                if (existentes.Contains(Norm(s.Name)))
                {
                    _log($"superfície {s.Name}: a Autodesk já gravou; mantida como veio.");
                    continue;
                }
                var seed = seed0 + "|" + (SourceFile ?? "") + "|" + s.Handle;

                var sb = new StringBuilder(s.Points.Count * 40);
                sb.Append('(');
                for (int i = 0; i < s.Points.Count; i++)
                {
                    var q = toLocal.Apply(s.Points[i][0], s.Points[i][1], s.Points[i][2], shift);
                    if (i > 0) sb.Append(',');
                    sb.Append('(').Append(F3(q[0])).Append(',').Append(F3(q[1])).Append(',').Append(F3(q[2])).Append(')');
                }
                sb.Append(')');
                var pts = _m.Add("IFCCARTESIANPOINTLIST3D", sb.ToString(), "$");

                sb.Clear().Append('(');
                for (int i = 0; i < s.Triangles.Count; i++)
                {
                    var t = s.Triangles[i];
                    if (i > 0) sb.Append(',');
                    sb.Append('(').Append(t[0] + 1).Append(',').Append(t[1] + 1).Append(',').Append(t[2] + 1).Append(')');
                }
                sb.Append(')');
                var malha = _m.Add("IFCTRIANGULATEDFACESET", "#" + pts, "$", ".F.", sb.ToString(), "$");

                if (estilo == 0)
                {
                    var cor = _m.Add("IFCCOLOURRGB", "'Terreno'", "0.62", "0.55", "0.42");
                    var sombra = _m.Add("IFCSURFACESTYLESHADING", "#" + cor, "0.");
                    estilo = _m.Add("IFCSURFACESTYLE", "'MetroSP Terreno'", ".BOTH.", "(#" + sombra + ")");
                }
                _m.Add("IFCSTYLEDITEM", "#" + malha, "(#" + estilo + ")", "$");

                var rep = _m.Add("IFCSHAPEREPRESENTATION", "#" + body, "'Body'", "'Tessellation'", "(#" + malha + ")");
                var pds = _m.Add("IFCPRODUCTDEFINITIONSHAPE", "$", "$", "(#" + rep + ")");
                var o = _m.Add("IFCCARTESIANPOINT", "(0.,0.,0.)");
                var place = _m.Add("IFCLOCALPLACEMENT", sitePlacement != 0 ? "#" + sitePlacement : "$", "#" + _m.Add("IFCAXIS2PLACEMENT3D", "#" + o, "$", "$"));
                var ge = _m.Add("IFCGEOGRAPHICELEMENT", Guid(seed, "terreno"), "$", IfcAlignmentWriter.S(s.Name), Opt(s.Description),
                    IfcAlignmentWriter.S("Superfície TIN"), "#" + place, "#" + pds, "$", ".TERRAIN.");

                var props = new List<int>
                {
                    Prop("Pontos", "IFCCOUNTMEASURE(" + s.Points.Count + ")"),
                    Prop("Triangulos", "IFCCOUNTMEASURE(" + s.Triangles.Count + ")"),
                    Prop("CotaMinima", "IFCLENGTHMEASURE(" + IfcAlignmentWriter.F(Math.Round(s.MinZ, 3)) + ")"),
                    Prop("CotaMaxima", "IFCLENGTHMEASURE(" + IfcAlignmentWriter.F(Math.Round(s.MaxZ, 3)) + ")"),
                    Prop("AreaPlanta", "IFCAREAMEASURE(" + IfcAlignmentWriter.F(Math.Round(s.Area2D, 2)) + ")"),
                    Prop("Area3D", "IFCAREAMEASURE(" + IfcAlignmentWriter.F(Math.Round(s.Area3D, 2)) + ")"),
                };
                if (!string.IsNullOrWhiteSpace(s.Layer)) props.Add(Prop("Layer", "IFCLABEL(" + IfcAlignmentWriter.S(s.Layer) + ")"));
                if (!string.IsNullOrWhiteSpace(SourceFile)) props.Add(Prop("ArquivoOrigem", "IFCLABEL(" + IfcAlignmentWriter.S(SourceFile) + ")"));
                var pset = _m.Add("IFCPROPERTYSET", Guid(seed, "pset-superficie"), "$", "'MetroSP_Superficie'", "$", StepModel.RefListText(props));
                _m.Add("IFCRELDEFINESBYPROPERTIES", Guid(seed, "pset-superficie-rel"), "$", "$", "$", "(#" + ge + ")", "#" + pset);
                ids.Add(ge);
                _log($"superfície {s.Name}: IfcGeographicElement.TERRAIN com {s.Triangles.Count} triângulos (cotas {s.MinZ:0.00} a {s.MaxZ:0.00}).");
            }
            if (ids.Count > 0)
                _m.Add("IFCRELCONTAINEDINSPATIALSTRUCTURE", Guid(seed0, "metrosp-site-terreno|" + string.Join(",", ids)), "$", "'Terreno'", "$",
                    StepModel.RefListText(ids), "#" + site.Id);
            return ids.Count;
        }

        int Prop(string nome, string valor) => _m.Add("IFCPROPERTYSINGLEVALUE", IfcAlignmentWriter.S(nome), "$", valor, "$");

        /// <summary>Coordenada com 3 casas (milímetro): mantém o arquivo menor que o "R" completo.</summary>
        static string F3(double v)
        {
            var s = Math.Round(v, 3).ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture);
            return s == "-0.0" ? "0.0" : s;
        }

        int BodyContext()
        {
            foreach (var c in _m.OfType("IFCGEOMETRICREPRESENTATIONSUBCONTEXT"))
                if (StepModel.Text(c.Args[0]) == "Body") return c.Id;
            var model = _m.OfType("IFCGEOMETRICREPRESENTATIONCONTEXT").FirstOrDefault(c => StepModel.Text(c.Args[1]) == "Model")
                        ?? _m.OfType("IFCGEOMETRICREPRESENTATIONCONTEXT").First();
            return _m.Add("IFCGEOMETRICREPRESENTATIONSUBCONTEXT", "'Body'", "'Model'", "*", "*", "*", "*", "#" + model.Id, "$", ".MODEL_VIEW.", "$");
        }

        // ================================================================== projeto, terreno, edificações

        public void ApplySpatial(SpatialConfig cfg)
        {
            var project = _m.OfType("IFCPROJECT").FirstOrDefault();
            if (project != null)
            {
                SetText(project, 2, cfg.Projeto?.Nome);
                SetText(project, 3, cfg.Projeto?.Descricao);
                SetText(project, 5, cfg.Projeto?.NomeLongo);
                SetText(project, 6, cfg.Projeto?.Fase);
            }

            var site = _m.OfType("IFCSITE").FirstOrDefault();
            if (site == null)
            {
                _log("aviso: IFC sem IfcSite; dados do terreno não aplicados.");
                return;
            }
            var s = cfg.Terreno;
            if (s != null)
            {
                SetText(site, 2, s.Nome);
                SetText(site, 3, s.Descricao);
                SetText(site, 7, s.NomeLongo);
                // Valor fora do intervalo (ex.: "-23,63" salvo como -2363 em versões antigas) fica de fora: vale o da Autodesk.
                if (s.Latitude.HasValue)
                {
                    if (Math.Abs(s.Latitude.Value) <= 90) _m.SetArg(site.Id, 9, CompoundAngle(s.Latitude.Value));
                    else _log($"aviso: latitude {s.Latitude.Value} fora de -90 a 90; mantida a da Autodesk. Corrija em Configurar IFC > Projeto.");
                }
                if (s.Longitude.HasValue)
                {
                    if (Math.Abs(s.Longitude.Value) <= 180) _m.SetArg(site.Id, 10, CompoundAngle(s.Longitude.Value));
                    else _log($"aviso: longitude {s.Longitude.Value} fora de -180 a 180; mantida a da Autodesk. Corrija em Configurar IFC > Projeto.");
                }
                if (s.Elevacao.HasValue) _m.SetArg(site.Id, 11, IfcAlignmentWriter.F(s.Elevacao.Value));
                SetText(site, 12, s.Matricula);
                if (Address(site, 13, s.Endereco)) _log("IfcSite: endereço gravado.");
            }

            var facilities = cfg.AllFacilities().Where(f => !string.IsNullOrWhiteSpace(f.Nome)).ToList();
            var principal = facilities.FirstOrDefault(f => f.Principal);
            if (principal != null)
            {
                // A instalação que a Autodesk gravou (a que tem os objetos) recebe o tipo e os dados da principal.
                var ent = FacilityTypes.SelectMany(t => _m.OfType(t)).OrderBy(b => b.Id).FirstOrDefault()
                          ?? _m.OfType("IFCFACILITY").FirstOrDefault();
                if (ent != null)
                {
                    var tipo = (principal.Tipo ?? FacilityInfo.DefaultType).Trim().ToUpperInvariant();
                    bool ifc43 = _m.Schema == null || _m.Schema.StartsWith("IFC4X3", StringComparison.OrdinalIgnoreCase);
                    if (!FacilityTypes.Contains(tipo)) tipo = ent.Type;
                    if (!ifc43 && tipo != "IFCBUILDING") tipo = ent.Type;   // no IFC4 só existe IfcBuilding
                    if (tipo != ent.Type)
                    {
                        while (ent.Args.Count > 9) ent.Args.RemoveAt(ent.Args.Count - 1);
                        while (ent.Args.Count < 9) ent.Args.Add("$");
                        if (tipo == "IFCBUILDING") ent.Args.AddRange(new[] { "$", "$", "$" });
                        else ent.Args.Add(".NOTDEFINED.");
                        _m.SetType(ent.Id, tipo);
                        _log($"instalação principal: {ent.Type} no lugar do que a Autodesk gravou.");
                    }
                    _m.SetArg(ent.Id, 2, IfcAlignmentWriter.S(principal.Nome.Trim()));
                    SetText(ent, 3, principal.Descricao);
                    SetText(ent, 7, principal.NomeLongo);
                    if (tipo == "IFCBUILDING")
                    {
                        if (principal.ElevacaoReferencia.HasValue) _m.SetArg(ent.Id, 9, IfcAlignmentWriter.F(principal.ElevacaoReferencia.Value));
                        Address(ent, 11, principal.Endereco);
                    }
                    _log($"instalação principal: {principal.Nome}.");
                }
                facilities.Remove(principal);
            }
            if (facilities.Count > 0)
            {
                var siteGid = StepModel.Text(site.Args[0]) ?? "site";
                var existing = FacilityTypes.SelectMany(t => _m.OfType(t)).Select(b => StepModel.Text(b.Args[2]))
                    .Where(n => n != null).ToHashSet(StringComparer.OrdinalIgnoreCase);
                // As que a Autodesk já gravou (mesmo nome) recebem nome longo, descrição e endereço.
                foreach (var f in facilities.Where(f => existing.Contains(f.Nome)))
                {
                    var ent = FacilityTypes.SelectMany(t => _m.OfType(t))
                        .FirstOrDefault(b => string.Equals(StepModel.Text(b.Args[2]), f.Nome, StringComparison.OrdinalIgnoreCase));
                    if (ent == null) continue;
                    SetText(ent, 3, f.Descricao);
                    SetText(ent, 7, f.NomeLongo);
                    if (ent.Type == "IFCBUILDING") Address(ent, 11, f.Endereco);
                }
                var created = new List<int>();
                foreach (var f in facilities.Where(f => !existing.Contains(f.Nome)))
                {
                    var type = (f.Tipo ?? FacilityInfo.DefaultType).Trim().ToUpperInvariant();
                    if (!FacilityTypes.Contains(type)) type = "IFCRAILWAY";
                    var origin = _m.Add("IFCCARTESIANPOINT", "(0.,0.,0.)");
                    var axis = _m.Add("IFCAXIS2PLACEMENT3D", "#" + origin, "$", "$");
                    var placement = _m.Add("IFCLOCALPLACEMENT", site.Args[5], "#" + axis);
                    var args = new List<string> { Guid(siteGid, "fac:" + type + ":" + f.Nome), "$", IfcAlignmentWriter.S(f.Nome),
                        Opt(f.Descricao), "$", "#" + placement, "$", Opt(f.NomeLongo), ".ELEMENT." };
                    if (type == "IFCBUILDING")
                        args.AddRange(new[] { f.ElevacaoReferencia.HasValue ? IfcAlignmentWriter.F(f.ElevacaoReferencia.Value) : "$", "$",
                            string.IsNullOrWhiteSpace(f.Endereco) ? "$" : "#" + PostalAddress(f.Endereco) });
                    else
                        args.Add(".NOTDEFINED.");   // PredefinedType de IfcRailway, IfcRoad e IfcBridge
                    created.Add(_m.Add(type, args.ToArray()));
                    _log($"{f.Nome}: {(FacilityTypes.Contains((f.Tipo ?? "").ToUpperInvariant()) ? f.Tipo : FacilityInfo.DefaultType)} acrescentado ao IfcSite.");
                }
                if (created.Count > 0)
                    _m.Add("IFCRELAGGREGATES", Guid(siteGid, "fac:" + string.Join("|", created)), "$", "'Instalações MetroSP'", "$", "#" + site.Id, StepModel.RefListText(created));
            }

            if (!cfg.CoordenadasPtl && !string.IsNullOrWhiteSpace(cfg.SistemaCoordenadas) && !_m.OfType("IFCMAPCONVERSION").Any())
            {
                var ctx = _m.OfType("IFCGEOMETRICREPRESENTATIONCONTEXT").FirstOrDefault(c => StepModel.Text(c.Args[1]) == "Model");
                if (ctx != null)
                {
                    var crs = _m.Add("IFCPROJECTEDCRS", IfcAlignmentWriter.S(cfg.SistemaCoordenadas), "$", "$", "$", "$", "$", "$");
                    _m.Add("IFCMAPCONVERSION", "#" + ctx.Id, "#" + crs, "0.", "0.", "0.", "1.", "0.", "1.");
                    _log("IfcMapConversion acrescentado (" + cfg.SistemaCoordenadas + ").");
                }
            }
        }

        // ================================================================== coordenadas no PTL

        /// <summary>
        /// Critério do plugin do Revit (Shared Coordinates, EPSG em branco): a Autodesk grava a geometria perto da
        /// origem e o deslocamento no IfcMapConversion; aqui o deslocamento passa para a colocação raiz, então as
        /// coordenadas absolutas do IFC voltam a ser as do desenho (PTL do Metrô). Saem IfcMapConversion,
        /// IfcProjectedCRS e o Pset "Coordinate Reference System". O IfcSite fica na origem do PTL (150.000; 250.000)
        /// com a latitude e a longitude dessa origem, a não ser que a configuração traga as suas.
        /// </summary>
        public void ApplyPtl(SiteInfo terreno)
        {
            var maps = _m.OfType("IFCMAPCONVERSION").ToList();
            if (maps.Count > 0)
            {
                var a = maps[0].Args;
                double Num(string s, double padrao) => s == "$" ? padrao : StepModel.Real(s);
                double e = Num(a[2], 0), n = Num(a[3], 0), h = Num(a[4], 0);
                double xa = a.Count > 5 ? Num(a[5], 1) : 1, xo = a.Count > 6 ? Num(a[6], 0) : 0, scale = a.Count > 7 ? Num(a[7], 1) : 1;
                if (maps.Skip(1).Any(m => m.Args[2] != a[2] || m.Args[3] != a[3] || m.Args[4] != a[4]))
                    _log("aviso: o IFC tem mais de um IfcMapConversion com valores diferentes; usado o do contexto 3D.");
                if (Math.Abs(scale - 1) > 1e-9)
                    _log($"aviso: IfcMapConversion com escala {scale}; a escala não foi aplicada.");

                // mapa = translação (E, N, H) + rotação no plano (XAxisAbscissa, XAxisOrdinate)
                var map = Matrix.FromAxes(new[] { e, n, h }, new[] { 0.0, 0, 1 }, new[] { xa, xo, 0 });
                foreach (var root in _m.OfType("IFCLOCALPLACEMENT").Where(p => p.Args[0] == "$").ToList())
                {
                    var m = map.Multiply(Axis2Placement(StepModel.Ref(root.Args[1])));
                    var origin = _m.Add("IFCCARTESIANPOINT", "(" + IfcAlignmentWriter.F(m.T[0]) + "," + IfcAlignmentWriter.F(m.T[1]) + "," + IfcAlignmentWriter.F(m.T[2]) + ")");
                    var z = _m.Add("IFCDIRECTION", "(" + IfcAlignmentWriter.F(m.R[2]) + "," + IfcAlignmentWriter.F(m.R[5]) + "," + IfcAlignmentWriter.F(m.R[8]) + ")");
                    var x = _m.Add("IFCDIRECTION", "(" + IfcAlignmentWriter.F(m.R[0]) + "," + IfcAlignmentWriter.F(m.R[3]) + "," + IfcAlignmentWriter.F(m.R[6]) + ")");
                    var axis = _m.Add("IFCAXIS2PLACEMENT3D", "#" + origin, "#" + z, "#" + x);
                    _m.SetArg(root.Id, 1, "#" + axis);
                }

                var crsIds = maps.Select(m => StepModel.Ref(m.Args[1])).Distinct().ToList();
                foreach (var m in maps) _m.Remove(m.Id);
                foreach (var c in crsIds) if (c != 0 && _m.TypeOf(c) == "IFCPROJECTEDCRS") _m.Remove(c);
                // texto WKT do UTM que a Autodesk grava solto (e sem o CRS, inválido)
                foreach (var w in _m.OfType("IFCWELLKNOWNTEXT").ToList()) _m.Remove(w.Id);
                foreach (var ps in _m.OfType("IFCPROPERTYSET").Where(ps => StepModel.Text(ps.Args[2]) == "Coordinate Reference System").ToList())
                {
                    foreach (var r in _m.OfType("IFCRELDEFINESBYPROPERTIES").Where(r => StepModel.Ref(r.Args[5]) == ps.Id).ToList()) _m.Remove(r.Id);
                    _m.Remove(ps.Id);
                }
                _log($"coordenadas no PTL do Metrô: deslocamento ({e:0.###}; {n:0.###}; {h:0.###}) do IfcMapConversion levado à colocação; IFC sem EPSG.");
                AlinhamentosAbsolutos();
            }

            // IfcSite na origem do PTL (150.000; 250.000), onde a latitude e a longitude são as da própria origem
            // (exatas, sem conversão). A configuração, se trouxer latitude e longitude, vale depois.
            var site = _m.OfType("IFCSITE").FirstOrDefault();
            if (site == null) return;
            SiteNaOrigemPtl(site);
            if (terreno?.Latitude != null && terreno?.Longitude != null) return;
            _m.SetArg(site.Id, 9, CompoundAngle(PtlMetroSP.Lat0));
            _m.SetArg(site.Id, 10, CompoundAngle(PtlMetroSP.Lon0));
            _log($"latitude e longitude do IfcSite = origem do PTL: {PtlMetroSP.Lat0:0.0000000}; {PtlMetroSP.Lon0:0.0000000}.");
        }

        /// <summary>
        /// Põe a colocação do IfcSite na origem do PTL (X0; Y0; 0), sem rotação, e compensa as colocações que eram
        /// relativas a ele, para nenhum objeto mudar de lugar. Produtos que usavam a mesma colocação do site ganham
        /// uma cópia da antiga.
        /// </summary>
        void SiteNaOrigemPtl(StepEntity site)
        {
            string F(double v) => IfcAlignmentWriter.F(v);
            int Axis(Matrix m)
            {
                var o = _m.Add("IFCCARTESIANPOINT", "(" + F(m.T[0]) + "," + F(m.T[1]) + "," + F(m.T[2]) + ")");
                var z = _m.Add("IFCDIRECTION", "(" + F(m.R[2]) + "," + F(m.R[5]) + "," + F(m.R[8]) + ")");
                var x = _m.Add("IFCDIRECTION", "(" + F(m.R[0]) + "," + F(m.R[3]) + "," + F(m.R[6]) + ")");
                return _m.Add("IFCAXIS2PLACEMENT3D", "#" + o, "#" + z, "#" + x);
            }

            int antigo = StepModel.Ref(site.Args[5]);
            var novo = Matrix.FromAxes(new[] { PtlMetroSP.X0, PtlMetroSP.Y0, 0.0 }, new[] { 0.0, 0, 1 }, new[] { 1.0, 0, 0 });
            if (antigo == 0 || _m.TypeOf(antigo) != "IFCLOCALPLACEMENT")
            {
                _m.SetArg(site.Id, 5, "#" + _m.Add("IFCLOCALPLACEMENT", "$", "#" + Axis(novo)));
                _log("IfcSite na origem do PTL (sem colocação antes).");
                return;
            }
            var velho = PlacementMatrix(antigo);
            if (Igual(velho, novo)) return;

            // outros produtos com a mesma colocação do site ficam onde estavam
            var copia = 0;
            foreach (var e in _m.Ids.Select(_m.Get).Where(e => e != null && e.Id != site.Id && e.Args.Count > 5 && e.Args[5].Trim() == "#" + antigo).ToList())
            {
                if (copia == 0) copia = _m.Add("IFCLOCALPLACEMENT", "$", "#" + Axis(velho));
                _m.SetArg(e.Id, 5, "#" + copia);
            }
            // colocações relativas ao site: nova relativa = novo⁻¹ · velho · relativa
            var ajuste = novo.Inverse().Multiply(velho);
            int n = 0;
            foreach (var lp in _m.OfType("IFCLOCALPLACEMENT").Where(l => l.Id != antigo && StepModel.Ref(l.Args[0]) == antigo).ToList())
            {
                _m.SetArg(lp.Id, 1, "#" + Axis(ajuste.Multiply(Axis2Placement(StepModel.Ref(lp.Args[1])))));
                n++;
            }
            _m.SetArg(antigo, 0, "$");
            _m.SetArg(antigo, 1, "#" + Axis(novo));
            _log($"IfcSite na origem do PTL ({PtlMetroSP.X0:0}; {PtlMetroSP.Y0:0}; 0); {n} colocação(ões) compensada(s), nada mudou de lugar.");
        }

        static readonly string[] AlignmentFamily = { "IFCALIGNMENT", "IFCALIGNMENTHORIZONTAL", "IFCALIGNMENTVERTICAL", "IFCALIGNMENTCANT", "IFCALIGNMENTSEGMENT" };

        /// <summary>
        /// Leva o alinhamento para coordenadas absolutas do PTL, com colocação identidade (como o MSP_IFCALINHAMENTO
        /// grava). Visualizadores que leem o IfcAlignment sem aplicar a colocação (o web-ifc, usado no Iris) passam a
        /// mostrar o eixo junto dos sólidos. Muda só o que está no plano do alinhamento: a colocação de cada trecho do
        /// horizontal (e o StartPoint/StartDirection do trecho), a cota dos trechos do vertical e o eixo 3D por pontos.
        /// As curvas canônicas dos trechos e a superelevação ficam como vieram.
        /// </summary>
        void AlinhamentosAbsolutos()
        {
            var produtos = AlignmentFamily.SelectMany(t => _m.OfType(t)).Where(p => StepModel.Ref(p.Args[5]) != 0).ToList();
            if (produtos.Count == 0) return;
            var placements = new HashSet<int>(produtos.Select(p => StepModel.Ref(p.Args[5])));
            var mats = placements.Select(PlacementMatrix).ToList();
            var A = mats[0];
            if (mats.Any(m => !Igual(m, A)))
            {
                _log("aviso: os alinhamentos têm colocações diferentes; ficaram relativos à colocação, como vieram.");
                return;
            }
            if (Igual(A, Matrix.Identity)) return;
            if (Math.Abs(A.R[8] - 1) > 1e-9)
            {
                _log("aviso: a colocação dos alinhamentos não é horizontal; ficaram relativos à colocação, como vieram.");
                return;
            }
            double c = A.R[0], s = A.R[3], tz = A.T[2], theta = Math.Atan2(s, c);
            string F(double v) => IfcAlignmentWriter.F(v);

            // curvas do alinhamento, pela representação dos produtos
            var horizontais = new HashSet<int>();
            var verticais = new HashSet<int>();
            var porPontos = new HashSet<int>();
            var segH = new HashSet<int>();
            var segV = new HashSet<int>();
            void Curva(int id, int guard = 0)
            {
                if (id == 0 || guard > 5) return;
                switch (_m.TypeOf(id))
                {
                    case "IFCCOMPOSITECURVE": horizontais.Add(id); break;
                    case "IFCGRADIENTCURVE": verticais.Add(id); Curva(StepModel.Ref(_m.Get(id).Args[2]), guard + 1); break;
                    case "IFCSEGMENTEDREFERENCECURVE": Curva(StepModel.Ref(_m.Get(id).Args[2]), guard + 1); break;
                    case "IFCINDEXEDPOLYCURVE": porPontos.Add(id); break;
                }
            }
            foreach (var p in produtos)
            {
                var shape = _m.Get(StepModel.Ref(p.Args[6]));
                if (shape == null || shape.Type != "IFCPRODUCTDEFINITIONSHAPE") continue;
                var tipoTrecho = p.Type == "IFCALIGNMENTSEGMENT" ? _m.TypeOf(StepModel.Ref(p.Args[7])) : null;
                foreach (var rep in StepModel.RefList(shape.Args[2]))
                {
                    var r = _m.Get(rep);
                    if (r == null || r.Type != "IFCSHAPEREPRESENTATION") continue;
                    foreach (var it in StepModel.RefList(r.Args[3]))
                    {
                        if (_m.TypeOf(it) != "IFCCURVESEGMENT") { Curva(it); continue; }
                        if (tipoTrecho == "IFCALIGNMENTHORIZONTALSEGMENT") segH.Add(it);
                        else if (tipoTrecho == "IFCALIGNMENTVERTICALSEGMENT") segV.Add(it);
                    }
                }
            }
            foreach (var cc in horizontais) foreach (var sg in StepModel.RefList(_m.Get(cc).Args[0])) segH.Add(sg);
            foreach (var gc in verticais) foreach (var sg in StepModel.RefList(_m.Get(gc).Args[0])) segV.Add(sg);
            segV.ExceptWith(segH);

            // pontos e colocações novos (os antigos podem ser compartilhados com outras coisas)
            var pontos = new Dictionary<int, int>();
            int PontoPlano(int id)
            {
                if (pontos.TryGetValue(id, out var novo)) return novo;
                var q = StepModel.RealList(_m.Get(id).Args[0]);
                double x = q[0], y = q.Count > 1 ? q[1] : 0;
                var w = A.Apply(x, y, q.Count > 2 ? q[2] : 0, new double[3]);
                novo = _m.Add("IFCCARTESIANPOINT", q.Count > 2 ? "(" + F(w[0]) + "," + F(w[1]) + "," + F(w[2]) + ")" : "(" + F(w[0]) + "," + F(w[1]) + ")");
                pontos[id] = novo;
                return novo;
            }
            var direcoes = new Dictionary<int, int>();
            int Direcao(int id)
            {
                if (direcoes.TryGetValue(id, out var novo)) return novo;
                var q = StepModel.RealList(_m.Get(id).Args[0]);
                novo = _m.Add("IFCDIRECTION", "(" + F(c * q[0] - s * q[1]) + "," + F(s * q[0] + c * q[1]) + ")");
                direcoes[id] = novo;
                return novo;
            }
            var colocacoes = new Dictionary<int, int>();
            int n2 = 0;
            foreach (var sg in segH.Concat(segV))
            {
                var e = _m.Get(sg);
                var pl = StepModel.Ref(e.Args[1]);
                var key = segH.Contains(sg) ? pl : -pl;
                if (!colocacoes.TryGetValue(key, out var novo))
                {
                    var a2 = _m.Get(pl);
                    if (a2 == null || a2.Type != "IFCAXIS2PLACEMENT2D") continue;
                    var pt = StepModel.Ref(a2.Args[0]);
                    if (segH.Contains(sg))
                    {
                        var dir = a2.Args[1] == "$" ? _m.Add("IFCDIRECTION", "(" + F(c) + "," + F(s) + ")") : Direcao(StepModel.Ref(a2.Args[1]));
                        novo = _m.Add("IFCAXIS2PLACEMENT2D", "#" + PontoPlano(pt), "#" + dir);
                    }
                    else
                    {
                        if (Math.Abs(tz) < 1e-12) continue;
                        var q = StepModel.RealList(_m.Get(pt).Args[0]);
                        var np = _m.Add("IFCCARTESIANPOINT", "(" + F(q[0]) + "," + F(q[1] + tz) + ")");
                        novo = _m.Add("IFCAXIS2PLACEMENT2D", "#" + np, a2.Args[1]);
                    }
                    colocacoes[key] = novo;
                }
                _m.SetArg(sg, 1, "#" + novo);
                n2++;
            }

            // parâmetros de projeto dos trechos
            foreach (var hs in _m.OfType("IFCALIGNMENTHORIZONTALSEGMENT").ToList())
            {
                var pt = StepModel.Ref(hs.Args[2]);
                if (pt != 0) _m.SetArg(hs.Id, 2, "#" + PontoPlano(pt));
                if (Math.Abs(theta) > 1e-12 && hs.Args[3] != "$") _m.SetArg(hs.Id, 3, F(StepModel.Real(hs.Args[3]) + theta));
            }
            if (Math.Abs(tz) > 1e-12)
                foreach (var vs in _m.OfType("IFCALIGNMENTVERTICALSEGMENT").ToList())
                    if (vs.Args[4] != "$") _m.SetArg(vs.Id, 4, F(StepModel.Real(vs.Args[4]) + tz));

            // eixo 3D por pontos
            foreach (var pc in porPontos)
            {
                var lista = _m.Get(StepModel.Ref(_m.Get(pc).Args[0]));
                if (lista == null || lista.Type != "IFCCARTESIANPOINTLIST3D") continue;
                var coords = lista.Args[0].Trim();
                var pts = StepModel.SplitArgs(coords.Substring(1, coords.Length - 2)).Select(t =>
                {
                    var q = StepModel.RealList(t);
                    var w = A.Apply(q[0], q[1], q[2], new double[3]);
                    return "(" + F(w[0]) + "," + F(w[1]) + "," + F(w[2]) + ")";
                });
                var nova = new List<string>(lista.Args);
                nova[0] = "(" + string.Join(",", pts) + ")";
                var id = _m.Add("IFCCARTESIANPOINTLIST3D", nova.ToArray());
                _m.SetArg(pc, 0, "#" + id);
            }

            // colocação identidade para os produtos do alinhamento e para as colocações lineares sobre ele
            var o = _m.Add("IFCCARTESIANPOINT", "(0.,0.,0.)");
            var raiz = _m.Add("IFCLOCALPLACEMENT", "$", "#" + _m.Add("IFCAXIS2PLACEMENT3D", "#" + o, "$", "$"));
            foreach (var p in produtos) _m.SetArg(p.Id, 5, "#" + raiz);
            int nl = 0;
            foreach (var lp in _m.OfType("IFCLINEARPLACEMENT").Where(l => placements.Contains(StepModel.Ref(l.Args[0]))).ToList())
            {
                _m.SetArg(lp.Id, 0, "#" + raiz);
                if (lp.Args.Count > 2 && StepModel.Ref(lp.Args[2]) != 0)
                {
                    var m = A.Multiply(Axis2Placement(StepModel.Ref(lp.Args[2])));
                    var op = _m.Add("IFCCARTESIANPOINT", "(" + F(m.T[0]) + "," + F(m.T[1]) + "," + F(m.T[2]) + ")");
                    var z = _m.Add("IFCDIRECTION", "(" + F(m.R[2]) + "," + F(m.R[5]) + "," + F(m.R[8]) + ")");
                    var x = _m.Add("IFCDIRECTION", "(" + F(m.R[0]) + "," + F(m.R[3]) + "," + F(m.R[6]) + ")");
                    _m.SetArg(lp.Id, 2, "#" + _m.Add("IFCAXIS2PLACEMENT3D", "#" + op, "#" + z, "#" + x));
                }
                nl++;
            }
            _log($"alinhamentos em coordenadas absolutas do PTL: {produtos.Count} objeto(s), {n2} trecho(s) de curva, {porPontos.Count} eixo(s) 3D, {nl} colocação(ões) linear(es).");
        }

        static bool Igual(Matrix a, Matrix b)
        {
            for (int i = 0; i < 9; i++) if (Math.Abs(a.R[i] - b.R[i]) > 1e-9) return false;
            for (int i = 0; i < 3; i++) if (Math.Abs(a.T[i] - b.T[i]) > 1e-6) return false;
            return true;
        }

        /// <summary>Pset "MetroSP_Projeto" no IfcProject (substitui o anterior, se houver).</summary>
        public void ApplyProjectProperties(IDictionary<string, string> props)
        {
            var project = _m.OfType("IFCPROJECT").FirstOrDefault();
            if (project == null) return;
            var gid = StepModel.Text(project.Args[0]) ?? "project";
            var values = props.Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                .Select(kv => _m.Add("IFCPROPERTYSINGLEVALUE", IfcAlignmentWriter.S(kv.Key), "$",
                    "IFCLABEL(" + IfcAlignmentWriter.S(kv.Value ?? "") + ")", "$"))
                .ToList();
            if (values.Count == 0) return;
            var pset = _m.Add("IFCPROPERTYSET", Guid(gid, "pset-projeto"), "$", "'MetroSP_Projeto'", "$", StepModel.RefListText(values));
            _m.Add("IFCRELDEFINESBYPROPERTIES", Guid(gid, "pset-projeto-rel"), "$", "$", "$", "(#" + project.Id + ")", "#" + pset);
            _log($"Pset MetroSP_Projeto com {values.Count} propriedade(s) no IfcProject.");
        }

        /// <summary>IfcPostalAddress só com a linha de endereço.</summary>
        int PostalAddress(string text) =>
            _m.Add("IFCPOSTALADDRESS", "$", "$", "$", "$", "(" + IfcAlignmentWriter.S(text.Trim()) + ")", "$", "$", "$", "$", "$");

        /// <summary>Grava o endereço se a entidade ainda não tem (não troca o que a Autodesk gravou).</summary>
        bool Address(StepEntity e, int index, string text)
        {
            if (string.IsNullOrWhiteSpace(text) || index >= e.Args.Count || e.Args[index] != "$") return false;
            _m.SetArg(e.Id, index, "#" + PostalAddress(text));
            return true;
        }

        void SetText(StepEntity e, int index, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) _m.SetArg(e.Id, index, IfcAlignmentWriter.S(value));
        }

        static string Opt(string s) => string.IsNullOrWhiteSpace(s) ? "$" : IfcAlignmentWriter.S(s);

        /// <summary>Graus decimais → IfcCompoundPlaneAngleMeasure (graus, minutos, segundos, milionésimos).</summary>
        static string CompoundAngle(double deg)
        {
            var sign = deg < 0 ? -1 : 1;
            var a = Math.Abs(deg);
            var d = (int)a; a = (a - d) * 60;
            var m = (int)a; a = (a - m) * 60;
            var s = (int)a; var u = (int)Math.Round((a - s) * 1e6);
            if (u >= 1000000) { u -= 1000000; s++; }
            return $"({sign * d},{sign * m},{sign * s},{sign * u})";
        }

        // ================================================================== posicionamento

        Matrix PlacementMatrix(int placementId)
        {
            var result = Matrix.Identity;
            var guard = 0;
            while (placementId != 0 && guard++ < 50)
            {
                var p = _m.Get(placementId);
                if (p == null || p.Type != "IFCLOCALPLACEMENT")
                {
                    if (p != null) _log($"aviso: posicionamento {p.Type} não suportado; usado como origem.");
                    break;
                }
                result = Axis2Placement(StepModel.Ref(p.Args[1])).Multiply(result);
                placementId = StepModel.Ref(p.Args[0]);
            }
            return result;
        }

        Matrix Axis2Placement(int id)
        {
            var e = _m.Get(id);
            if (e == null) return Matrix.Identity;
            var loc = StepModel.RealList(_m.Get(StepModel.Ref(e.Args[0])).Args[0]);
            while (loc.Count < 3) loc.Add(0);
            double[] z = { 0, 0, 1 }, x = { 1, 0, 0 };
            if (e.Type == "IFCAXIS2PLACEMENT3D")
            {
                if (e.Args[1] != "$") z = Dir(StepModel.Ref(e.Args[1]));
                if (e.Args[2] != "$") x = Dir(StepModel.Ref(e.Args[2]));
            }
            return Matrix.FromAxes(loc.ToArray(), z, x);
        }

        double[] Dir(int id)
        {
            var r = StepModel.RealList(_m.Get(id).Args[0]);
            while (r.Count < 3) r.Add(0);
            return r.ToArray();
        }

        // ================================================================== utilitários

        static readonly string[] FacilityTypes = { "IFCRAILWAY", "IFCBUILDING", "IFCBRIDGE", "IFCROAD" };

        static string Norm(string s) => (s ?? "").Trim().ToUpperInvariant();

        /// <summary>
        /// Casa o IfcAlignment com o alinhamento do desenho: nome igual; senão um único nome que contenha o
        /// outro (o exportador pode acrescentar prefixo ou sufixo); senão, se houver um só de cada lado, esse.
        /// </summary>
        AlignmentModel Match(string ifcName, Dictionary<string, AlignmentModel> byName, int ifcCount)
        {
            var n = Norm(ifcName);
            if (n.Length > 0 && byName.TryGetValue(n, out var exact)) return exact;

            if (n.Length > 0)
            {
                var partial = byName.Where(kv => kv.Key.Length > 0 && (n.Contains(kv.Key) || kv.Key.Contains(n))).ToList();
                if (partial.Count == 1)
                {
                    _log($"IfcAlignment \"{ifcName}\" casado por semelhança com {partial[0].Value.Name}.");
                    return partial[0].Value;
                }
            }

            if (ifcCount == 1 && byName.Count == 1)
            {
                var only = byName.Values.First();
                _log($"IfcAlignment \"{ifcName}\" é o único do arquivo; usando {only.Name}, o único do modelo.");
                return only;
            }
            return null;
        }

        static string Guid(string seed, string key) =>
            IfcAlignmentWriter.S(IfcAlignmentWriter.CompressGuid(MD5.HashData(Encoding.UTF8.GetBytes("MetroSP|" + seed + "|" + key))));
    }

    /// <summary>Transformação rígida (rotação + translação) em precisão dupla.</summary>
    public struct Matrix
    {
        public double[] R;   // 3x3, por linhas
        public double[] T;

        public static Matrix Identity => new Matrix { R = new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }, T = new double[3] };

        public static Matrix FromAxes(double[] origin, double[] z, double[] x)
        {
            z = Normalize(z);
            // x ortogonalizado em relação a z (regra do IfcAxis2Placement3D)
            var d = x[0] * z[0] + x[1] * z[1] + x[2] * z[2];
            x = Normalize(new[] { x[0] - d * z[0], x[1] - d * z[1], x[2] - d * z[2] });
            var y = new[] { z[1] * x[2] - z[2] * x[1], z[2] * x[0] - z[0] * x[2], z[0] * x[1] - z[1] * x[0] };
            // colunas = eixos locais expressos no sistema pai
            return new Matrix { R = new[] { x[0], y[0], z[0], x[1], y[1], z[1], x[2], y[2], z[2] }, T = (double[])origin.Clone() };
        }

        /// <summary>this ∘ other (aplica other primeiro).</summary>
        public Matrix Multiply(Matrix o)
        {
            var r = new double[9];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    r[i * 3 + j] = R[i * 3] * o.R[j] + R[i * 3 + 1] * o.R[3 + j] + R[i * 3 + 2] * o.R[6 + j];
            var t = new double[3];
            for (int i = 0; i < 3; i++)
                t[i] = R[i * 3] * o.T[0] + R[i * 3 + 1] * o.T[1] + R[i * 3 + 2] * o.T[2] + T[i];
            return new Matrix { R = r, T = t };
        }

        public Matrix Inverse()
        {
            var r = new double[] { R[0], R[3], R[6], R[1], R[4], R[7], R[2], R[5], R[8] };   // transposta
            var t = new double[3];
            for (int i = 0; i < 3; i++) t[i] = -(r[i * 3] * T[0] + r[i * 3 + 1] * T[1] + r[i * 3 + 2] * T[2]);
            return new Matrix { R = r, T = t };
        }

        public double[] Apply(double x, double y, double z, double[] shift)
        {
            return new[]
            {
                R[0] * x + R[1] * y + R[2] * z + T[0] + shift[0],
                R[3] * x + R[4] * y + R[5] * z + T[1] + shift[1],
                R[6] * x + R[7] * y + R[8] * z + T[2] + shift[2]
            };
        }

        static double[] Normalize(double[] v)
        {
            var n = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            return new[] { v[0] / n, v[1] / n, v[2] / n };
        }
    }
}
