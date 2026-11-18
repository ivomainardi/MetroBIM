using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MetroSP.Ifc
{
    /// <summary>
    /// Confere as medidas de todos os objetos do IFC com o Revit: a caixa do corpo (Body) de cada elemento, no sistema do
    /// IfcSite, contra a caixa do elemento no Revit. Objeto com largura, profundidade ou altura bem diferente do Revit vai
    /// para o registro (corpo esticado, encolhido ou faltando pedaço). Só confere; não muda o IFC.
    /// </summary>
    public static class ConferenciaGeometriaIfc
    {
        public sealed class Divergencia
        {
            public string Classe, Nome;
            public long Tag;
            public double[] Ifc, Revit;   // dimensões X, Y, Z em metros
        }

        public sealed class Resultado
        {
            public int Conferidos, SemCaixa;
            public bool SistemaDiferente;
            public readonly List<Divergencia> Divergencias = new List<Divergencia>();

            public IEnumerable<string> Linhas()
            {
                if (Conferidos == 0) yield break;
                if (SistemaDiferente)
                {
                    yield return $"Medidas dos objetos: a maioria dos {Conferidos} objetos não bate com o Revit em X/Y; o IFC deve estar girado em " +
                                 "relação às coordenadas internas, e a conferência foi pulada.";
                    yield break;
                }
                if (Divergencias.Count == 0)
                {
                    yield return $"Medidas dos objetos: {Conferidos} objeto(s) conferidos com o Revit, todos com as mesmas dimensões.";
                    yield break;
                }
                yield return $"Medidas dos objetos: {Divergencias.Count} de {Conferidos} objeto(s) com dimensões diferentes do Revit (" +
                             string.Join(", ", Divergencias.GroupBy(d => d.Classe).OrderByDescending(g => g.Count()).Take(6)
                                 .Select(g => $"{g.Key} {g.Count()}")) + "):";
                foreach (Divergencia d in Divergencias.Take(30))
                    yield return $"  {d.Classe} {d.Tag} \"{d.Nome}\": IFC {Dim(d.Ifc)} · Revit {Dim(d.Revit)}";
                if (Divergencias.Count > 30) yield return $"  ... e mais {Divergencias.Count - 30}.";
            }

            private static string Dim(double[] v) =>
                string.Join(" × ", v.Select(x => x.ToString("0.000", CultureInfo.InvariantCulture))) + " m";
        }

        private static readonly HashSet<string> Fora = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "IFCPROJECT", "IFCSITE", "IFCBUILDING", "IFCBUILDINGSTOREY", "IFCSPACE", "IFCFACILITY", "IFCFACILITYPART",
            "IFCRAILWAY", "IFCROAD", "IFCBRIDGE", "IFCMARINEFACILITY", "IFCEXTERNALSPATIALELEMENT", "IFCSPATIALZONE",
            "IFCOPENINGELEMENT", "IFCOPENINGSTANDARDCASE", "IFCVOIDINGFEATURE", "IFCGRID", "IFCANNOTATION", "IFCVIRTUALELEMENT",
            "IFCZONE", "IFCSYSTEM", "IFCDISTRIBUTIONPORT", "IFCALIGNMENT", "IFCREFERENT",
        };

        /// <param name="caixaRevit">Caixa do elemento no Revit pelo ElementId (Tag): mínimo e máximo, em metros, nas coordenadas internas.</param>
        public static Resultado Aplicar(StepModel m, Func<long, (double[] Min, double[] Max)?> caixaRevit)
        {
            var r = new Resultado();
            double fator = FixadoresIfc.FatorComprimento(m);
            int placementSite = 0;
            StepEntity site = m.OfType("IFCSITE").FirstOrDefault();
            if (site != null && site.Args.Count > 5) placementSite = StepModel.Ref(site.Args[5]);
            var geo = new Geo(m);
            int xyRuins = 0;
            // Elementos: o que está contido nos pavimentos/locais, agregado ou aninhado em outro elemento.
            var elementos = new SortedSet<int>();
            foreach (StepEntity rel in m.OfType("IFCRELCONTAINEDINSPATIALSTRUCTURE"))
                if (rel.Args.Count > 4) elementos.UnionWith(StepModel.RefList(rel.Args[4]));
            foreach (string t in new[] { "IFCRELAGGREGATES", "IFCRELNESTS" })
                foreach (StepEntity rel in m.OfType(t))
                    if (rel.Args.Count > 5) elementos.UnionWith(StepModel.RefList(rel.Args[5]));
            // Peças das ligações levam no Tag o id da ligação: Tag repetido não é conferido.
            var porTag = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (int id in elementos)
            {
                StepEntity e = m.Get(id);
                if (e == null || e.Args.Count < 8) continue;
                string t = StepModel.Text(e.Args[7]) ?? "";
                porTag[t] = (porTag.TryGetValue(t, out int n) ? n : 0) + 1;
            }
            foreach (int id in elementos)
            {
                string tipo = m.TypeOf(id);
                if (tipo == null || Fora.Contains(tipo)) continue;
                StepEntity e = m.Get(id);
                if (e.Args.Count < 8 || !e.Args[6].StartsWith("#") || !e.Args[5].StartsWith("#")) continue;
                if (m.TypeOf(StepModel.Ref(e.Args[6])) != "IFCPRODUCTDEFINITIONSHAPE") continue;
                if (!long.TryParse(StepModel.Text(e.Args[7]), NumberStyles.Integer, CultureInfo.InvariantCulture, out long tag)) continue;
                if (porTag.TryGetValue(StepModel.Text(e.Args[7]), out int vezes) && vezes > 1) continue;
                var cr = caixaRevit(tag);
                if (cr == null) continue;
                double[,] P;
                try { P = geo.Placement(StepModel.Ref(e.Args[5]), placementSite); }
                catch { continue; }
                var caixa = new Caixa();
                bool ok = true;
                foreach (int rep in StepModel.RefList(m.Get(StepModel.Ref(e.Args[6])).Args[2]))
                {
                    StepEntity sr = m.Get(rep);
                    if (sr == null || sr.Args.Count < 4 || !string.Equals(StepModel.Text(sr.Args[1]), "Body", StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (int it in StepModel.RefList(sr.Args[3]))
                        ok &= geo.Item(it, P, caixa, 0);
                }
                if (!ok || caixa.Vazia) { r.SemCaixa++; continue; }
                double[] di = new double[3], dr = new double[3];
                for (int k = 0; k < 3; k++)
                {
                    di[k] = (caixa.Max[k] - caixa.Min[k]) / fator;
                    dr[k] = cr.Value.Max[k] - cr.Value.Min[k];
                }
                r.Conferidos++;
                bool[] dif = new bool[3];
                for (int k = 0; k < 3; k++) dif[k] = Math.Abs(di[k] - dr[k]) > Math.Max(0.02, 0.05 * Math.Max(di[k], dr[k]));
                if (dif[0] || dif[1])
                {
                    // Girado de 90°? (X e Y trocados) conta como sistema diferente, não como erro do objeto.
                    bool trocado = Math.Abs(di[0] - dr[1]) <= Math.Max(0.02, 0.05 * dr[1]) && Math.Abs(di[1] - dr[0]) <= Math.Max(0.02, 0.05 * dr[0]);
                    if (trocado) { xyRuins++; continue; }
                }
                if (dif.Any(x => x))
                {
                    if (dif[0] || dif[1]) xyRuins++;
                    r.Divergencias.Add(new Divergencia
                    {
                        Classe = "Ifc" + tipo.Substring(3, 1) + tipo.Substring(4).ToLowerInvariant(),
                        Nome = StepModel.Text(e.Args[2]) ?? "",
                        Tag = tag, Ifc = di, Revit = dr,
                    });
                }
            }
            if (r.Conferidos >= 10 && xyRuins * 2 > r.Conferidos) { r.SistemaDiferente = true; r.Divergencias.Clear(); }
            return r;
        }

        internal sealed class Caixa
        {
            public readonly double[] Min = { double.MaxValue, double.MaxValue, double.MaxValue };
            public readonly double[] Max = { double.MinValue, double.MinValue, double.MinValue };
            public bool Vazia => Min[0] > Max[0];

            public void Add(double[,] T, double x, double y, double z)
            {
                for (int i = 0; i < 3; i++)
                {
                    double v = T[i, 0] * x + T[i, 1] * y + T[i, 2] * z + T[i, 3];
                    if (v < Min[i]) Min[i] = v;
                    if (v > Max[i]) Max[i] = v;
                }
            }
        }

        /// <summary>Leitura da geometria do IFC: placements, itens de representação e pontos.</summary>
        internal sealed class Geo
        {
            private readonly StepModel _m;
            public Geo(StepModel m) { _m = m; }

            public double[,] Placement(int id, int parar)
            {
                double[,] T = Id();
                int guarda = 0;
                while (id != 0 && id != parar && guarda++ < 50)
                {
                    StepEntity lp = _m.Get(id);
                    if (lp == null || lp.Type != "IFCLOCALPLACEMENT") throw new InvalidOperationException();
                    T = Mul(Axis3(lp.Args[1]), T);
                    id = lp.Args[0].StartsWith("#") ? StepModel.Ref(lp.Args[0]) : 0;
                }
                return T;
            }

            /// <summary>Soma o item à caixa. false se o item é de um tipo que a conferência não sabe medir.</summary>
            public bool Item(int id, double[,] T, Caixa c, int nivel)
            {
                if (nivel > 6) return false;
                StepEntity x = _m.Get(id);
                if (x == null) return false;
                switch (x.Type)
                {
                    case "IFCMAPPEDITEM":
                    {
                        StepEntity mapa = _m.Get(StepModel.Ref(x.Args[0]));
                        StepEntity rep = mapa == null ? null : _m.Get(StepModel.Ref(mapa.Args[1]));
                        if (rep == null || rep.Args.Count < 4) return false;
                        // Os itens ficam no sistema da origem do mapa (MappingOrigin), posto pelo alvo (MappingTarget).
                        double[,] Tm = Mul(T, Mul(Operador(x.Args[1]), Axis3(mapa.Args[0])));
                        bool ok = true;
                        foreach (int it in StepModel.RefList(rep.Args[3])) ok &= Item(it, Tm, c, nivel + 1);
                        return ok;
                    }
                    case "IFCEXTRUDEDAREASOLID":
                    case "IFCEXTRUDEDAREASOLIDTAPERED":
                    {
                        var pts = Perfil(x.Args[0]);
                        if (pts == null || pts.Count == 0) return false;
                        double[,] Tp = Mul(T, Axis3(x.Args[1]));
                        double[] d = Vet(x.Args[2]) ?? new[] { 0.0, 0, 1 };
                        double n = Math.Sqrt(d[0] * d[0] + d[1] * d[1] + d[2] * d[2]);
                        double prof = StepModel.Real(x.Args[3]);
                        foreach (var p in pts)
                        {
                            c.Add(Tp, p[0], p[1], 0);
                            c.Add(Tp, p[0] + d[0] / n * prof, p[1] + d[1] / n * prof, d[2] / n * prof);
                        }
                        return true;
                    }
                    case "IFCBOOLEANRESULT":
                    case "IFCBOOLEANCLIPPINGRESULT":
                    {
                        // Diferença e interseção: a caixa do primeiro operando (o recorte só diminui). União: os dois.
                        bool ok = Item(StepModel.Ref(x.Args[1]), T, c, nivel + 1);
                        if (x.Args[0] == ".UNION.") ok &= Item(StepModel.Ref(x.Args[2]), T, c, nivel + 1);
                        return ok;
                    }
                    case "IFCTRIANGULATEDFACESET":
                    case "IFCPOLYGONALFACESET":
                        return Lista3(x.Args[0], T, c);
                    case "IFCFACETEDBREP":
                    case "IFCFACETEDBREPWITHVOIDS":
                    case "IFCADVANCEDBREP":
                    case "IFCSHELLBASEDSURFACEMODEL":
                    case "IFCFACEBASEDSURFACEMODEL":
                    case "IFCSWEPTDISKSOLID":
                    {
                        int antes = Pontos(id, T, c, 0, new HashSet<int>());
                        return antes > 0;
                    }
                    default:
                        return false;
                }
            }

            /// <summary>Todos os IfcCartesianPoint (3D) e listas de pontos abaixo do item.</summary>
            private int Pontos(int id, double[,] T, Caixa c, int nivel, HashSet<int> vistos)
            {
                if (nivel > 8 || !vistos.Add(id)) return 0;
                StepEntity x = _m.Get(id);
                if (x == null) return 0;
                if (x.Type == "IFCCARTESIANPOINT")
                {
                    var v = StepModel.RealList(x.Args[0]);
                    if (v.Count < 3) return 0;
                    c.Add(T, v[0], v[1], v[2]);
                    return 1;
                }
                if (x.Type == "IFCCARTESIANPOINTLIST3D") return Lista3("#" + id, T, c) ? 1 : 0;
                int n = 0;
                foreach (string a in x.Args)
                    foreach (int r in Refs(a)) n += Pontos(r, T, c, nivel + 1, vistos);
                return n;
            }

            private bool Lista3(string arg, double[,] T, Caixa c)
            {
                StepEntity l = _m.Get(StepModel.Ref(arg));
                if (l == null || l.Type != "IFCCARTESIANPOINTLIST3D") return false;
                foreach (string p in StepModel.SplitArgs(l.Args[0].Substring(1, l.Args[0].Length - 2)))
                {
                    var v = StepModel.RealList(p);
                    if (v.Count >= 3) c.Add(T, v[0], v[1], v[2]);
                }
                return true;
            }

            /// <summary>Pontos 2D do contorno externo do perfil, já na posição do perfil.</summary>
            private List<double[]> Perfil(string arg)
            {
                StepEntity p = _m.Get(StepModel.Ref(arg));
                if (p == null) return null;
                var pts = new List<double[]>();
                switch (p.Type)
                {
                    case "IFCARBITRARYCLOSEDPROFILEDEF":
                    case "IFCARBITRARYPROFILEDEFWITHVOIDS":
                        Curva(StepModel.Ref(p.Args[2]), pts, 0);
                        return pts;
                    case "IFCDERIVEDPROFILEDEF":
                        return Perfil(p.Args[2]);
                }
                // Perfis parametrizados: caixa centrada na posição (IFC4).
                double a, b;
                try
                {
                    switch (p.Type)
                    {
                        case "IFCRECTANGLEPROFILEDEF": case "IFCRECTANGLEHOLLOWPROFILEDEF": case "IFCROUNDEDRECTANGLEPROFILEDEF":
                        case "IFCISHAPEPROFILEDEF": case "IFCASYMMETRICISHAPEPROFILEDEF":
                            a = StepModel.Real(p.Args[3]); b = StepModel.Real(p.Args[4]); break;
                        case "IFCUSHAPEPROFILEDEF": case "IFCLSHAPEPROFILEDEF": case "IFCTSHAPEPROFILEDEF":
                        case "IFCCSHAPEPROFILEDEF": case "IFCZSHAPEPROFILEDEF":
                            b = StepModel.Real(p.Args[3]); a = StepModel.Real(p.Args[4]);
                            if (p.Type == "IFCLSHAPEPROFILEDEF" && p.Args[4] == "$") a = b;
                            break;
                        case "IFCCIRCLEPROFILEDEF": case "IFCCIRCLEHOLLOWPROFILEDEF":
                            a = b = 2 * StepModel.Real(p.Args[3]); break;
                        case "IFCELLIPSEPROFILEDEF":
                            a = 2 * StepModel.Real(p.Args[3]); b = 2 * StepModel.Real(p.Args[4]); break;
                        default: return null;
                    }
                }
                catch { return null; }
                double[,] T2 = Axis2(p.Args[2]);
                foreach (var (x, y) in new[] { (-a / 2, -b / 2), (a / 2, -b / 2), (a / 2, b / 2), (-a / 2, b / 2) })
                    pts.Add(new[] { T2[0, 0] * x + T2[0, 1] * y + T2[0, 3], T2[1, 0] * x + T2[1, 1] * y + T2[1, 3] });
                return pts;
            }

            private void Curva(int id, List<double[]> pts, int nivel)
            {
                StepEntity c = _m.Get(id);
                if (c == null || nivel > 5) return;
                switch (c.Type)
                {
                    case "IFCINDEXEDPOLYCURVE":
                    {
                        StepEntity l = _m.Get(StepModel.Ref(c.Args[0]));
                        if (l == null) return;
                        foreach (string p in StepModel.SplitArgs(l.Args[0].Substring(1, l.Args[0].Length - 2)))
                        {
                            var v = StepModel.RealList(p);
                            if (v.Count >= 2) pts.Add(new[] { v[0], v[1] });
                        }
                        return;
                    }
                    case "IFCPOLYLINE":
                        foreach (int pi in StepModel.RefList(c.Args[0]))
                        {
                            var v = StepModel.RealList(_m.Get(pi).Args[0]);
                            if (v.Count >= 2) pts.Add(new[] { v[0], v[1] });
                        }
                        return;
                    case "IFCCOMPOSITECURVE":
                        foreach (int s in StepModel.RefList(c.Args[0]))
                        {
                            StepEntity seg = _m.Get(s);
                            if (seg != null && seg.Args.Count >= 3) Curva(StepModel.Ref(seg.Args[2]), pts, nivel + 1);
                        }
                        return;
                    case "IFCTRIMMEDCURVE":
                        Curva(StepModel.Ref(c.Args[0]), pts, nivel + 1);
                        return;
                    case "IFCCIRCLE":
                    {
                        double rr = StepModel.Real(c.Args[1]);
                        double[,] T2 = Axis2(c.Args[0]);
                        foreach (var (x, y) in new[] { (-rr, -rr), (rr, -rr), (rr, rr), (-rr, rr) })
                            pts.Add(new[] { T2[0, 0] * x + T2[0, 1] * y + T2[0, 3], T2[1, 0] * x + T2[1, 1] * y + T2[1, 3] });
                        return;
                    }
                    case "IFCLINE":
                    {
                        var v = StepModel.RealList(_m.Get(StepModel.Ref(c.Args[0])).Args[0]);
                        if (v.Count >= 2) pts.Add(new[] { v[0], v[1] });
                        return;
                    }
                }
            }

            private static IEnumerable<int> Refs(string a)
            {
                for (int i = a.IndexOf('#'); i >= 0; i = a.IndexOf('#', i + 1))
                {
                    int j = i + 1;
                    while (j < a.Length && char.IsDigit(a[j])) j++;
                    if (j > i + 1) yield return int.Parse(a.Substring(i + 1, j - i - 1), CultureInfo.InvariantCulture);
                }
            }

            public double[] Vet(string arg)
            {
                if (arg == null || !arg.StartsWith("#")) return null;
                StepEntity p = _m.Get(StepModel.Ref(arg));
                if (p == null || (p.Type != "IFCDIRECTION" && p.Type != "IFCCARTESIANPOINT")) return null;
                var v = StepModel.RealList(p.Args[0]);
                while (v.Count < 3) v.Add(0);
                return v.Take(3).ToArray();
            }

            public double[,] Axis3(string arg)
            {
                StepEntity a = arg != null && arg.StartsWith("#") ? _m.Get(StepModel.Ref(arg)) : null;
                if (a == null) return Id();
                if (a.Type == "IFCAXIS2PLACEMENT2D") return Axis2(arg);
                double[] o = Vet(a.Args[0]) ?? new double[3];
                double[] z = Un(a.Args.Count > 1 ? Vet(a.Args[1]) : null) ?? new[] { 0.0, 0, 1 };
                double[] rx = Un(a.Args.Count > 2 ? Vet(a.Args[2]) : null) ?? (Math.Abs(z[2]) > 0.9 ? new[] { 1.0, 0, 0 } : Un(new[] { -z[1], z[0], 0 }));
                return Base(o, rx, z);
            }

            private double[,] Axis2(string arg)
            {
                StepEntity a = arg != null && arg.StartsWith("#") ? _m.Get(StepModel.Ref(arg)) : null;
                double[,] T = Id();
                if (a == null) return T;
                double[] o = Vet(a.Args[0]) ?? new double[3];
                double[] x = Un(a.Args.Count > 1 ? Vet(a.Args[1]) : null) ?? new[] { 1.0, 0, 0 };
                T[0, 0] = x[0]; T[1, 0] = x[1]; T[0, 1] = -x[1]; T[1, 1] = x[0];
                T[0, 3] = o[0]; T[1, 3] = o[1];
                return T;
            }

            /// <summary>IfcCartesianTransformationOperator3D (Axis1, Axis2, LocalOrigin, Scale, Axis3[, Scale2, Scale3]).</summary>
            public double[,] Operador(string arg)
            {
                StepEntity t = arg != null && arg.StartsWith("#") ? _m.Get(StepModel.Ref(arg)) : null;
                if (t == null) return Id();
                double[] x = Un(Vet(t.Args[0])) ?? new[] { 1.0, 0, 0 };
                double[] z = Un(t.Args.Count > 4 ? Vet(t.Args[4]) : null) ?? new[] { 0.0, 0, 1 };
                double[] o = Vet(t.Args[2]) ?? new double[3];
                double s = t.Args.Count > 3 && t.Args[3] != "$" ? StepModel.Real(t.Args[3]) : 1;
                double sy = t.Type.EndsWith("NONUNIFORM") && t.Args.Count > 5 && t.Args[5] != "$" ? StepModel.Real(t.Args[5]) : s;
                double sz = t.Type.EndsWith("NONUNIFORM") && t.Args.Count > 6 && t.Args[6] != "$" ? StepModel.Real(t.Args[6]) : s;
                double[,] T = Base(o, x, z);
                for (int i = 0; i < 3; i++) { T[i, 0] *= s; T[i, 1] *= sy; T[i, 2] *= sz; }
                return T;
            }

            private static double[,] Base(double[] o, double[] rx, double[] z)
            {
                double d = rx[0] * z[0] + rx[1] * z[1] + rx[2] * z[2];
                double[] x = Un(new[] { rx[0] - d * z[0], rx[1] - d * z[1], rx[2] - d * z[2] }) ?? new[] { 1.0, 0, 0 };
                double[] y = { z[1] * x[2] - z[2] * x[1], z[2] * x[0] - z[0] * x[2], z[0] * x[1] - z[1] * x[0] };
                var T = Id();
                for (int i = 0; i < 3; i++) { T[i, 0] = x[i]; T[i, 1] = y[i]; T[i, 2] = z[i]; T[i, 3] = o[i]; }
                return T;
            }

            private static double[] Un(double[] v)
            {
                if (v == null) return null;
                double n = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
                return n < 1e-12 ? null : new[] { v[0] / n, v[1] / n, v[2] / n };
            }

            public static double[,] Id()
            {
                var T = new double[4, 4];
                for (int i = 0; i < 4; i++) T[i, i] = 1;
                return T;
            }

            public static double[,] Mul(double[,] A, double[,] B)
            {
                var C = new double[4, 4];
                for (int i = 0; i < 4; i++)
                    for (int j = 0; j < 4; j++)
                    {
                        double s = 0;
                        for (int k = 0; k < 4; k++) s += A[i, k] * B[k, j];
                        C[i, j] = s;
                    }
                return C;
            }
        }
    }
}
