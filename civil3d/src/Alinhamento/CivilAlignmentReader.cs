using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil;
using Autodesk.Civil.DatabaseServices;

namespace MetroSP.Alinhamento
{
    /// <summary>Lê alinhamento + perfil do Civil 3D e monta o <see cref="AlignmentModel"/>.</summary>
    public static class CivilAlignmentReader
    {
        const double Delta = 1e-3;      // passo para derivadas numéricas (m)

        public static AlignmentModel Read(Alignment al, Profile profile, double sampleStep)
        {
            var m = new AlignmentModel
            {
                Name = al.Name,
                Description = al.Description,
                ProfileName = profile?.Name,
                Handle = al.Handle.ToString(),
                StartStation = al.StartingStation,
                EndStation = al.EndingStation
            };

            ReadHorizontal(al, m);
            if (profile != null)
            {
                ReadVertical(al, profile, m);
                SampleAxis(al, profile, m, sampleStep);
            }
            return m;
        }

        // ------------------------------------------------------------------ horizontal

        static void ReadHorizontal(Alignment al, AlignmentModel m)
        {
            var entities = al.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                var ent = entities.GetEntityByOrder(i);
                for (int j = 0; j < ent.SubEntityCount; j++)
                {
                    var sub = ent[j];
                    if (sub.Length <= 1e-9) continue;

                    var seg = new HorizontalSegment
                    {
                        StartX = sub.StartPoint.X,
                        StartY = sub.StartPoint.Y,
                        Length = sub.Length,
                        StartStation = sub.StartStation
                    };

                    switch (sub)
                    {
                        case AlignmentSubEntityLine:
                            seg.Type = "LINE";
                            seg.StartDirection = Math.Atan2(sub.EndPoint.Y - sub.StartPoint.Y, sub.EndPoint.X - sub.StartPoint.X);
                            break;

                        case AlignmentSubEntityArc arc:
                        {
                            seg.Type = "CIRCULARARC";
                            var rx = sub.StartPoint.X - arc.CenterPoint.X;
                            var ry = sub.StartPoint.Y - arc.CenterPoint.Y;
                            // tangente = raio girado 90°: horário (direita) ou anti-horário (esquerda)
                            seg.StartDirection = arc.Clockwise ? Math.Atan2(-rx, ry) : Math.Atan2(rx, -ry);
                            var r = Math.Abs(arc.Radius) * (arc.Clockwise ? -1 : 1);
                            seg.StartRadius = r;
                            seg.EndRadius = r;
                            break;
                        }

                        case AlignmentSubEntitySpiral sp:
                        {
                            seg.Type = SpiralType(sp.SpiralDefinition);
                            var s0 = sub.StartStation;
                            var s1 = sub.EndStation;
                            // lado da curva pelo giro total da espiral
                            var turn = NormalizeAngle(Direction(al, s1 - Delta, s1) - Direction(al, s0, s0 + Delta));
                            var sign = turn >= 0 ? 1.0 : -1.0;
                            seg.StartRadius = Finite(sp.RadiusIn) ? sign * Math.Abs(sp.RadiusIn) : 0.0;
                            seg.EndRadius = Finite(sp.RadiusOut) ? sign * Math.Abs(sp.RadiusOut) : 0.0;
                            // direção da corda de 1 mm corrigida pela curvatura inicial
                            var k0 = seg.StartRadius == 0 ? 0 : 1.0 / seg.StartRadius;
                            seg.StartDirection = Direction(al, s0, s0 + Delta) - k0 * Delta / 2;
                            break;
                        }

                        default:
                            throw new NotSupportedException("Subentidade de alinhamento não suportada: " + sub.GetType().Name);
                    }
                    m.Horizontal.Add(seg);
                }
            }

            // Segmento final de comprimento zero (regra do IFC 4.3 ADD2): fixa ponto e direção finais.
            if (m.Horizontal.Count > 0)
            {
                double x = 0, y = 0;
                al.PointLocation(al.EndingStation, 0, ref x, ref y);
                m.Horizontal.Add(new HorizontalSegment
                {
                    Type = "LINE",
                    StartX = x,
                    StartY = y,
                    StartDirection = Direction(al, al.EndingStation - Delta, al.EndingStation),
                    StartStation = al.EndingStation
                });
            }
        }

        static string SpiralType(Autodesk.Civil.SpiralType t)
        {
            switch (t)
            {
                case Autodesk.Civil.SpiralType.Clothoid:
                case Autodesk.Civil.SpiralType.OffsetClothoid:
                    return "CLOTHOID";
                case Autodesk.Civil.SpiralType.CubicParabola:
                case Autodesk.Civil.SpiralType.OffsetCubicParabola:
                case Autodesk.Civil.SpiralType.JapaneseCubic:
                case Autodesk.Civil.SpiralType.OffsetJapaneseCubic:
                case Autodesk.Civil.SpiralType.NSWCubic:
                    return "CUBIC";
                case Autodesk.Civil.SpiralType.Bloss:
                case Autodesk.Civil.SpiralType.OffsetBloss:
                    return "BLOSSCURVE";
                case Autodesk.Civil.SpiralType.BiQuadratic:
                case Autodesk.Civil.SpiralType.OffsetBiQuadratic:
                    return "HELMERTCURVE";
                case Autodesk.Civil.SpiralType.SineHalfWave:
                case Autodesk.Civil.SpiralType.OffsetHalfWaveLenDimnTangent:
                case Autodesk.Civil.SpiralType.OffsetHalfWaveLenDimnTangent2:
                    return "COSINECURVE";
                case Autodesk.Civil.SpiralType.Sinusoidal:
                case Autodesk.Civil.SpiralType.OffsetSinusoidal:
                    return "SINECURVE";
                default:
                    return "CLOTHOID";
            }
        }

        // ------------------------------------------------------------------ vertical

        static void ReadVertical(Alignment al, Profile p, AlignmentModel m)
        {
            var start = al.StartingStation;
            var entities = p.Entities.Cast<ProfileEntity>()
                .Where(e => e.Length > 1e-9)
                .OrderBy(e => e.StartStation)
                .ToList();

            foreach (var e in entities)
            {
                switch (e.EntityType)
                {
                    case ProfileEntityType.Tangent:
                    {
                        var g = (e.EndElevation - e.StartElevation) / e.Length;
                        m.Vertical.Add(new VerticalSegment
                        {
                            Type = "CONSTANTGRADIENT",
                            StartDistAlong = e.StartStation - start,
                            HorizontalLength = e.Length,
                            StartHeight = e.StartElevation,
                            StartGradient = g,
                            EndGradient = g
                        });
                        break;
                    }

                    case ProfileEntityType.ParabolaAsymmetric:
                    {
                        // Parábola assimétrica = duas parábolas que se encontram na estaca do PIV.
                        var asym = (ProfileParabolaAsymmetric)e;
                        var mid = asym.PVIStation;
                        m.Vertical.Add(Curve(p, "PARABOLICARC", e.StartStation, mid, start));
                        m.Vertical.Add(Curve(p, "PARABOLICARC", mid, e.EndStation, start));
                        break;
                    }

                    case ProfileEntityType.ParabolaSymmetric:
                        m.Vertical.Add(Curve(p, "PARABOLICARC", e.StartStation, e.EndStation, start));
                        break;

                    case ProfileEntityType.Circular:
                    {
                        var seg = Curve(p, "CIRCULARARC", e.StartStation, e.EndStation, start);
                        var r = Math.Abs(((ProfileCircular)e).Radius);
                        seg.Radius = seg.EndGradient >= seg.StartGradient ? r : -r;
                        m.Vertical.Add(seg);
                        break;
                    }
                }
            }

            // Segmento final de comprimento zero com cota e rampa finais.
            if (m.Vertical.Count > 0)
            {
                var last = m.Vertical[m.Vertical.Count - 1];
                var endDist = last.StartDistAlong + last.HorizontalLength;
                m.Vertical.Add(new VerticalSegment
                {
                    Type = "CONSTANTGRADIENT",
                    StartDistAlong = endDist,
                    StartHeight = p.ElevationAt(endDist + start),
                    StartGradient = last.EndGradient,
                    EndGradient = last.EndGradient
                });
            }
        }

        static VerticalSegment Curve(Profile p, string type, double s0, double s1, double alignmentStart)
        {
            var g0 = Grade(p, s0);
            var g1 = Grade(p, s1);
            var len = s1 - s0;
            return new VerticalSegment
            {
                Type = type,
                StartDistAlong = s0 - alignmentStart,
                HorizontalLength = len,
                StartHeight = p.ElevationAt(s0),
                StartGradient = g0,
                EndGradient = g1,
                // raio da parábola: L / (g1 - g0); positivo = côncava (rampa crescente)
                Radius = Math.Abs(g1 - g0) < 1e-12 ? 0 : len / (g1 - g0)
            };
        }

        /// <summary>
        /// Rampa no ponto. Nas extremidades de curvas o perfil é contínuo em rampa (C1), então a diferença
        /// central é exata; nas pontas do perfil usa diferença unilateral.
        /// </summary>
        static double Grade(Profile p, double s)
        {
            var a = Math.Max(s - Delta, p.StartingStation);
            var b = Math.Min(s + Delta, p.EndingStation);
            return (p.ElevationAt(b) - p.ElevationAt(a)) / (b - a);
        }

        // ------------------------------------------------------------------ eixo 3D

        static void SampleAxis(Alignment al, Profile p, AlignmentModel m, double step)
        {
            var s0 = Math.Max(al.StartingStation, p.StartingStation);
            var s1 = Math.Min(al.EndingStation, p.EndingStation);
            if (s1 <= s0) return;
            if (!(step > 0)) step = 1.0;

            // estacas: passo regular + todos os pontos notáveis (início/fim de cada segmento)
            var stations = new SortedSet<double>();
            for (var s = s0; s < s1; s += step) stations.Add(Math.Round(s, 6));
            stations.Add(s1);
            foreach (var h in m.Horizontal) if (h.StartStation > s0 && h.StartStation < s1) stations.Add(Math.Round(h.StartStation, 6));
            foreach (var v in m.Vertical)
            {
                var s = v.StartDistAlong + al.StartingStation;
                if (s > s0 && s < s1) stations.Add(Math.Round(s, 6));
            }

            double last = double.NegativeInfinity;
            foreach (var s in stations)
            {
                if (s - last < 1e-4) continue;
                last = s;
                double x = 0, y = 0;
                al.PointLocation(s, 0, ref x, ref y);
                m.Axis3D.Add(new[] { x, y, p.ElevationAt(s) });
            }
        }

        // ------------------------------------------------------------------ utilitários

        static double Direction(Alignment al, double sa, double sb)
        {
            double xa = 0, ya = 0, xb = 0, yb = 0;
            al.PointLocation(sa, 0, ref xa, ref ya);
            al.PointLocation(sb, 0, ref xb, ref yb);
            return Math.Atan2(yb - ya, xb - xa);
        }

        static double NormalizeAngle(double a)
        {
            while (a > Math.PI) a -= 2 * Math.PI;
            while (a < -Math.PI) a += 2 * Math.PI;
            return a;
        }

        static bool Finite(double r) => !double.IsInfinity(r) && !double.IsNaN(r) && r > 1e-9 && r < 1e9;
    }
}
