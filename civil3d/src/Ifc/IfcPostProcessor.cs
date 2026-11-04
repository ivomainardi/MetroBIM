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
                                       string sourceFile = null)
        {
            var model = StepModel.Load(ifcPath);
            var pp = new IfcPostProcessor(model, log) { SourceFile = sourceFile };
            if (alignments != null && alignments.Count > 0) pp.ApplyAlignments(alignments);
            if (spatial != null) pp.ApplySpatial(spatial);
            if (projectProperties != null && projectProperties.Count > 0) pp.ApplyProjectProperties(projectProperties);

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

            var ifcAlignments = _m.OfType("IFCALIGNMENT").ToList();
            var used = new HashSet<AlignmentModel>();
            double[] knownShift = null;
            foreach (var al in ifcAlignments)
            {
                var name = StepModel.Text(al.Args[2]);
                var a = Match(name, byName, ifcAlignments.Count);
                if (a != null) used.Add(a);
                if (a == null)
                {
                    _log($"IfcAlignment \"{name}\" sem alinhamento correspondente no desenho; mantido como veio.");
                    continue;
                }
                if (a.Axis3D.Count < 2)
                {
                    _log($"{a.Name}: sem perfil, mantido como veio.");
                    continue;
                }

                var toLocal = PlacementMatrix(StepModel.Ref(al.Args[5])).Inverse();
                var shift = DetectShift(al.Id, a, toLocal);
                if (knownShift == null && a.Horizontal.Count > 0) knownShift = shift;

                double[] Local(double[] p) => toLocal.Apply(p[0], p[1], p[2], shift);

                ReplaceAxis(al, a, axisCtx, Local);
                if (_m.Schema.StartsWith("IFC4X3")) EnsureVertical(al, a, Local);
                done++;
                _log($"{a.Name}: eixo 3D aplicado ({a.Axis3D.Count} pontos, perfil {a.ProfileName}).");
            }

            var missing = alignments.Where(a => !used.Contains(a) && a.Horizontal.Count > 0).ToList();
            if (missing.Count > 0) done += AddMissing(missing, axisCtx, knownShift);
            return done;
        }

        /// <summary>Nome do DWG, gravado no Pset MetroSP_Alinhamento dos alinhamentos acrescentados.</summary>
        public string SourceFile { get; set; }

        /// <summary>
        /// Alinhamentos do desenho que o exportador da Autodesk não gravou: entram completos (horizontal,
        /// vertical, eixo 3D), agregados ao IfcProject e referenciados no IfcSite, como no IFC 4.3.
        /// </summary>
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

            _log($"{a.Name}: coordenadas do IFC deslocadas em ({dx:0.###}; {dy:0.###}) em relação ao desenho; deslocamento aplicado.");
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
                if (s.Latitude.HasValue) _m.SetArg(site.Id, 9, CompoundAngle(s.Latitude.Value));
                if (s.Longitude.HasValue) _m.SetArg(site.Id, 10, CompoundAngle(s.Longitude.Value));
                if (s.Elevacao.HasValue) _m.SetArg(site.Id, 11, IfcAlignmentWriter.F(s.Elevacao.Value));
                SetText(site, 12, s.Matricula);
            }

            var facilities = cfg.AllFacilities().Where(f => !string.IsNullOrWhiteSpace(f.Nome)).ToList();
            if (facilities.Count > 0)
            {
                var siteGid = StepModel.Text(site.Args[0]) ?? "site";
                var existing = FacilityTypes.SelectMany(t => _m.OfType(t)).Select(b => StepModel.Text(b.Args[2]))
                    .Where(n => n != null).ToHashSet(StringComparer.OrdinalIgnoreCase);
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
                        args.AddRange(new[] { f.ElevacaoReferencia.HasValue ? IfcAlignmentWriter.F(f.ElevacaoReferencia.Value) : "$", "$", "$" });
                    else
                        args.Add(".NOTDEFINED.");   // PredefinedType de IfcRailway, IfcRoad e IfcBridge
                    created.Add(_m.Add(type, args.ToArray()));
                    _log($"{f.Nome}: {(FacilityTypes.Contains((f.Tipo ?? "").ToUpperInvariant()) ? f.Tipo : FacilityInfo.DefaultType)} acrescentado ao IfcSite.");
                }
                if (created.Count > 0)
                    _m.Add("IFCRELAGGREGATES", Guid(siteGid, "fac:" + string.Join("|", created)), "$", "'Instalações MetroSP'", "$", "#" + site.Id, StepModel.RefListText(created));
            }

            if (!string.IsNullOrWhiteSpace(cfg.SistemaCoordenadas) && !_m.OfType("IFCMAPCONVERSION").Any())
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
                _log($"IfcAlignment \"{ifcName}\" é o único do arquivo; usando {only.Name}, o único do desenho.");
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
