using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MetroSP.Ifc
{
    /// <summary>
    /// Vigas, pilares e barras (IfcBeam, IfcColumn, IfcMember) que o exportador do Revit grava com o corpo extrudado no
    /// dobro do comprimento, centrado no eixo: a extrusão volta ao comprimento do Revit, no mesmo centro, e o Qto Length,
    /// GrossVolume e OuterSurfaceArea, que vieram do corpo dobrado, caem pela metade.
    /// </summary>
    /// <summary>Barra do Revit, em metros: comprimento da locação e trecho do corpo ao longo dela, a partir do meio.</summary>
    public sealed class BarraRevit
    {
        public double Comprimento;
        /// <summary>Início e fim do corpo de verdade (com os recortes das ligações), medidos do meio da barra ao longo de Dir.</summary>
        public double Ini = double.NaN, Fim = double.NaN;
        /// <summary>Direção da barra nas coordenadas internas do Revit (início → fim, ou para cima no pilar).</summary>
        public double[] Dir;
    }

    public static class PerfisIfc
    {
        public sealed class Resultado
        {
            public int Corrigidos, Extrusoes, Quantidades, Divergentes, Aparados;
            public readonly Dictionary<string, int> PorClasse = new Dictionary<string, int>(StringComparer.Ordinal);

            public override string ToString()
            {
                if (Corrigidos == 0 && Divergentes == 0) return null;
                string s = $"Perfis metálicos: {Corrigidos} elemento(s) com o corpo no dobro do comprimento do Revit voltaram ao comprimento certo (" +
                           string.Join(", ", PorClasse.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}")) +
                           $"); {Extrusoes} extrusão(ões) e {Quantidades} quantidade(s) ajustadas" +
                           (Aparados > 0 ? $", e {Aparados} com o corpo recortado nas ligações como no Revit." : ".");
                if (Divergentes > 0) s += $" {Divergentes} extrusão(ões) compartilhada(s) por elementos de comprimentos diferentes ficaram como estavam.";
                return s;
            }
        }

        private static readonly string[] Classes = { "IFCBEAM", "IFCCOLUMN", "IFCMEMBER" };

        /// <param name="barraRevit">A barra no Revit pelo ElementId (Tag), ou null.</param>
        public static Resultado Aplicar(StepModel m, Func<long, BarraRevit> barraRevit)
        {
            var r = new Resultado();
            double fator = FixadoresIfc.FatorComprimento(m);
            // Extrusão → elementos que a usam e se, para cada um, o corpo está no dobro do Revit.
            var usos = new Dictionary<int, List<(int Elem, bool Dobro)>>();
            var extrusoesDoElemento = new Dictionary<int, List<(int Ext, double Escala)>>();
            var classeDoElemento = new Dictionary<int, string>();
            var barras = new Dictionary<int, BarraRevit>();
            foreach (string classe in Classes)
                foreach (StepEntity e in m.OfType(classe))
                {
                    if (e.Args.Count < 8 || !long.TryParse(StepModel.Text(e.Args[7]), NumberStyles.Integer, CultureInfo.InvariantCulture, out long tag)) continue;
                    var exts = new List<(int, double)>();
                    StepEntity pds = m.Get(StepModel.Ref(e.Args[6]));
                    if (pds == null || pds.Args.Count < 3) continue;
                    foreach (int rep in StepModel.RefList(pds.Args[2]))
                    {
                        StepEntity sr = m.Get(rep);
                        if (sr == null || sr.Args.Count < 4 || !string.Equals(StepModel.Text(sr.Args[1]), "Body", StringComparison.OrdinalIgnoreCase)) continue;
                        foreach (int it in StepModel.RefList(sr.Args[3])) Extrusoes(m, it, 1, exts, 0);
                    }
                    if (exts.Count == 0) continue;
                    BarraRevit barra = barraRevit(tag);
                    double? lr = barra?.Comprimento;
                    foreach (var (ext, escala) in exts)
                    {
                        double prof = Prof(m, ext) * escala / fator;
                        bool dobro = lr > 0.01 && Math.Abs(prof / lr.Value - 2) < 0.03;
                        if (!usos.TryGetValue(ext, out var l)) usos[ext] = l = new List<(int, bool)>();
                        l.Add((e.Id, dobro));
                    }
                    extrusoesDoElemento[e.Id] = exts;
                    classeDoElemento[e.Id] = classe;
                    if (barra != null) barras[e.Id] = barra;
                }

            // Corrige cada extrusão uma vez, só se todos os elementos que a usam estão no dobro.
            var corrigidas = new HashSet<int>();
            foreach (var kv in usos)
            {
                if (!kv.Value.Any(u => u.Dobro)) continue;
                if (kv.Value.Any(u => !u.Dobro)) { r.Divergentes++; continue; }
                Encurtar(m, kv.Key);
                corrigidas.Add(kv.Key);
                r.Extrusoes++;
            }
            if (corrigidas.Count == 0) return r;

            // Elementos cujo corpo inteiro foi corrigido: Qto de comprimento, volume bruto e superfície pela metade.
            var corrigidos = new Dictionary<int, double>();   // elemento → profundidade antiga (unidade do IFC)
            foreach (var kv in extrusoesDoElemento)
            {
                if (!kv.Value.All(x => corrigidas.Contains(x.Ext))) continue;
                corrigidos[kv.Key] = kv.Value.Max(x => Prof(m, x.Ext) * 2 * x.Escala);
                string nome = classeDoElemento[kv.Key];
                nome = "Ifc" + nome.Substring(3, 1) + nome.Substring(4).ToLowerInvariant();
                r.PorClasse[nome] = (r.PorClasse.TryGetValue(nome, out int n) ? n : 0) + 1;
                r.Corrigidos++;
            }
            r.Quantidades = Quantidades(m, corrigidos);
            // O corpo do tipo é a barra inteira; cada elemento corrigido ganha o seu corpo com os recortes do Revit.
            var geo = new ConferenciaGeometriaIfc.Geo(m);
            int placementSite = 0;
            StepEntity site = m.OfType("IFCSITE").FirstOrDefault();
            if (site != null && site.Args.Count > 5) placementSite = StepModel.Ref(site.Args[5]);
            var estilos = new Dictionary<int, StepEntity>();
            foreach (StepEntity st in m.OfType("IFCSTYLEDITEM"))
                if (st.Args.Count >= 3 && st.Args[0].StartsWith("#")) estilos[StepModel.Ref(st.Args[0])] = st;
            var usoPds = new Dictionary<int, int>();
            foreach (int el in extrusoesDoElemento.Keys)
            {
                int pds = StepModel.Ref(m.Get(el).Args[6]);
                usoPds[pds] = (usoPds.TryGetValue(pds, out int u) ? u : 0) + 1;
            }
            foreach (int el in corrigidos.Keys)
            {
                if (!barras.TryGetValue(el, out BarraRevit b) || double.IsNaN(b.Ini) || double.IsNaN(b.Fim) || b.Fim - b.Ini < 0.01) continue;
                try { if (Aparar(m, geo, el, b, fator, placementSite, estilos, usoPds)) r.Aparados++; }
                catch { /* fica com a barra inteira */ }
            }
            return r;
        }

        /// <summary>
        /// Troca o corpo mapeado do elemento por uma extrusão própria do trecho do Revit (com os recortes nas ligações),
        /// no mesmo eixo. Só quando o corpo é uma extrusão só, sem escala.
        /// </summary>
        private static bool Aparar(StepModel m, ConferenciaGeometriaIfc.Geo geo, int el, BarraRevit b, double fator, int placementSite,
            Dictionary<int, StepEntity> estilos, Dictionary<int, int> usoPds)
        {
            StepEntity e = m.Get(el);
            int pdsId = StepModel.Ref(e.Args[6]);
            StepEntity pds = m.Get(pdsId);
            var reps = StepModel.RefList(pds.Args[2]);
            int srId = reps.FirstOrDefault(x => string.Equals(StepModel.Text(m.Get(x)?.Args[1]), "Body", StringComparison.OrdinalIgnoreCase));
            StepEntity sr = srId == 0 ? null : m.Get(srId);
            if (sr == null || reps.Count(x => string.Equals(StepModel.Text(m.Get(x)?.Args[1]), "Body", StringComparison.OrdinalIgnoreCase)) != 1) return false;
            var itens = StepModel.RefList(sr.Args[3]);
            if (itens.Count != 1) return false;
            StepEntity it = m.Get(itens[0]);
            double[,] Tm = ConferenciaGeometriaIfc.Geo.Id();
            StepEntity ext = it;
            if (it.Type == "IFCMAPPEDITEM")
            {
                StepEntity mapa = m.Get(StepModel.Ref(it.Args[0]));
                StepEntity rep = m.Get(StepModel.Ref(mapa.Args[1]));
                var its = StepModel.RefList(rep.Args[3]);
                if (its.Count != 1) return false;
                ext = m.Get(its[0]);
                Tm = ConferenciaGeometriaIfc.Geo.Mul(geo.Operador(it.Args[1]), geo.Axis3(mapa.Args[0]));
            }
            if (ext == null || ext.Type != "IFCEXTRUDEDAREASOLID") return false;
            double[,] F = ConferenciaGeometriaIfc.Geo.Mul(Tm, geo.Axis3(ext.Args[1]));
            for (int j = 0; j < 3; j++)
            {
                double n = Math.Sqrt(F[0, j] * F[0, j] + F[1, j] * F[1, j] + F[2, j] * F[2, j]);
                if (Math.Abs(n - 1) > 1e-6) return false;   // com escala: não mexe
            }
            double[] d = geo.Vet(ext.Args[2]) ?? new[] { 0.0, 0, 1 };
            double dn = Math.Sqrt(d[0] * d[0] + d[1] * d[1] + d[2] * d[2]);
            double prof = StepModel.Real(ext.Args[3]);
            var S = new double[3]; var u = new double[3];
            for (int i = 0; i < 3; i++)
            {
                S[i] = F[i, 3];
                u[i] = (F[i, 0] * d[0] + F[i, 1] * d[1] + F[i, 2] * d[2]) / dn;
            }
            // Direção da barra no sistema do IfcSite, para saber de que lado fica o Ini e o Fim do Revit.
            double lo, hi;
            double[,] P = geo.Placement(StepModel.Ref(e.Args[5]), placementSite);
            double[] uw = { P[0, 0] * u[0] + P[0, 1] * u[1] + P[0, 2] * u[2], P[1, 0] * u[0] + P[1, 1] * u[1] + P[1, 2] * u[2], P[2, 0] * u[0] + P[2, 1] * u[1] + P[2, 2] * u[2] };
            double dot = b.Dir == null ? 0 : uw[0] * b.Dir[0] + uw[1] * b.Dir[1] + uw[2] * b.Dir[2];
            if (Math.Abs(dot) > 0.99) { lo = dot > 0 ? b.Ini : -b.Fim; hi = dot > 0 ? b.Fim : -b.Ini; }
            else { lo = -(b.Fim - b.Ini) / 2; hi = -lo; }
            lo *= fator; hi *= fator;
            if (Math.Abs(lo + prof / 2) < 1 * fator / 1000 && Math.Abs(hi - prof / 2) < 1 * fator / 1000) return false;   // já igual
            if (lo < -prof / 2 * 1.05 || hi > prof / 2 * 1.05 || hi - lo < prof * 0.2) return false;   // o Revit não bate: deixa inteira
            var o = new double[3];
            for (int i = 0; i < 3; i++) o[i] = S[i] + u[i] * (prof / 2 + lo);
            int ponto = m.Add("IFCCARTESIANPOINT", "(" + string.Join(",", o.Select(Num)) + ")");
            int eixoZ = m.Add("IFCDIRECTION", "(" + Num(F[0, 2]) + "," + Num(F[1, 2]) + "," + Num(F[2, 2]) + ")");
            int eixoX = m.Add("IFCDIRECTION", "(" + Num(F[0, 0]) + "," + Num(F[1, 0]) + "," + Num(F[2, 0]) + ")");
            int pos = m.Add("IFCAXIS2PLACEMENT3D", "#" + ponto, "#" + eixoZ, "#" + eixoX);
            int novo = m.Add("IFCEXTRUDEDAREASOLID", ext.Args[0], "#" + pos, ext.Args[2], Num(hi - lo));
            if (estilos.TryGetValue(ext.Id, out StepEntity st)) m.Add("IFCSTYLEDITEM", "#" + novo, st.Args[1], st.Args[2]);
            int novoSr = m.Add("IFCSHAPEREPRESENTATION", sr.Args[0], sr.Args[1], "'SweptSolid'", "(#" + novo + ")");
            var novaLista = reps.Select(x => x == srId ? novoSr : x).ToList();
            if (usoPds.TryGetValue(pdsId, out int uso) && uso > 1)
            {
                int novoPds = m.Add("IFCPRODUCTDEFINITIONSHAPE", pds.Args[0], pds.Args[1], StepModel.RefListText(novaLista));
                m.SetArg(el, 6, "#" + novoPds);
                usoPds[pdsId] = uso - 1;
            }
            else m.SetArg(pdsId, 2, StepModel.RefListText(novaLista));
            return true;
        }

        private static void Extrusoes(StepModel m, int id, double escala, List<(int, double)> saida, int nivel)
        {
            if (nivel > 4) return;
            StepEntity x = m.Get(id);
            if (x == null) return;
            switch (x.Type)
            {
                case "IFCEXTRUDEDAREASOLID":
                    if (x.Args.Count >= 4) saida.Add((id, escala));
                    break;
                case "IFCBOOLEANRESULT":
                case "IFCBOOLEANCLIPPINGRESULT":
                    if (x.Args.Count >= 2) Extrusoes(m, StepModel.Ref(x.Args[1]), escala, saida, nivel + 1);
                    break;
                case "IFCMAPPEDITEM":
                {
                    StepEntity mapa = m.Get(StepModel.Ref(x.Args[0]));
                    StepEntity alvo = m.Get(StepModel.Ref(x.Args[1]));
                    double s = escala;
                    if (alvo != null && alvo.Type == "IFCCARTESIANTRANSFORMATIONOPERATOR3D" && alvo.Args.Count >= 4 && alvo.Args[3] != "$")
                        s *= StepModel.Real(alvo.Args[3]);
                    else if (alvo != null && alvo.Type != "IFCCARTESIANTRANSFORMATIONOPERATOR3D") return;   // escala não uniforme: não mexe
                    StepEntity rep = mapa == null ? null : m.Get(StepModel.Ref(mapa.Args[1]));
                    if (rep == null || rep.Args.Count < 4) return;
                    foreach (int it in StepModel.RefList(rep.Args[3])) Extrusoes(m, it, s, saida, nivel + 1);
                    break;
                }
            }
        }

        private static double Prof(StepModel m, int ext) => StepModel.Real(m.Get(ext).Args[3]);

        /// <summary>Metade da profundidade, com o início avançado um quarto ao longo da direção: mesmo centro.</summary>
        private static void Encurtar(StepModel m, int ext)
        {
            StepEntity x = m.Get(ext);
            double prof = StepModel.Real(x.Args[3]);
            StepEntity pos = m.Get(StepModel.Ref(x.Args[1]));
            var posArgs = pos != null && pos.Args.Count >= 3 ? new List<string>(pos.Args) : new List<string> { "$", "$", "$" };
            double[] o = Vetor(m, posArgs[0]) ?? new double[3];
            double[] z = Unit(Vetor(m, posArgs[1])) ?? new[] { 0.0, 0, 1 };
            double[] rx = Unit(Vetor(m, posArgs[2])) ?? new[] { 1.0, 0, 0 };
            double d0 = Dot(rx, z);
            double[] xx = Unit(new[] { rx[0] - d0 * z[0], rx[1] - d0 * z[1], rx[2] - d0 * z[2] });
            if (xx == null) xx = Unit(Math.Abs(z[0]) < 0.9 ? new[] { 1 - z[0] * z[0], -z[0] * z[1], -z[0] * z[2] } : new[] { -z[1] * z[0], 1 - z[1] * z[1], -z[1] * z[2] });
            double[] yy = { z[1] * xx[2] - z[2] * xx[1], z[2] * xx[0] - z[0] * xx[2], z[0] * xx[1] - z[1] * xx[0] };
            double[] dir = Unit(Vetor(m, x.Args[2])) ?? new[] { 0.0, 0, 1 };
            double k = prof / 4;
            var novo = new double[3];
            for (int i = 0; i < 3; i++) novo[i] = o[i] + k * (dir[0] * xx[i] + dir[1] * yy[i] + dir[2] * z[i]);
            int ponto = m.Add("IFCCARTESIANPOINT", "(" + string.Join(",", novo.Select(Num)) + ")");
            posArgs[0] = "#" + ponto;
            int novaPos = m.Add("IFCAXIS2PLACEMENT3D", posArgs.ToArray());
            m.SetArg(ext, 1, "#" + novaPos);
            m.SetArg(ext, 3, Num(prof / 2));
        }

        private static int Quantidades(StepModel m, Dictionary<int, double> corrigidos)
        {
            // Quem cita cada quantidade: só mexe na que é de um único IfcElementQuantity.
            var citacoes = new Dictionary<int, int>();
            foreach (StepEntity q in m.OfType("IFCELEMENTQUANTITY"))
                if (q.Args.Count >= 6)
                    foreach (int qi in StepModel.RefList(q.Args[5]))
                        citacoes[qi] = (citacoes.TryGetValue(qi, out int c) ? c : 0) + 1;
            var feitas = new HashSet<int>();
            int n = 0;
            foreach (StepEntity rel in m.OfType("IFCRELDEFINESBYPROPERTIES"))
            {
                if (rel.Args.Count < 6) continue;
                StepEntity q = m.Get(StepModel.Ref(rel.Args[5]));
                if (q == null || q.Type != "IFCELEMENTQUANTITY" || q.Args.Count < 6) continue;
                var elems = StepModel.RefList(rel.Args[4]);
                if (elems.Count == 0 || !elems.All(corrigidos.ContainsKey)) continue;
                double antigo = elems.Max(e => corrigidos[e]);
                var itens = StepModel.RefList(q.Args[5]).Select(m.Get).Where(i => i != null && i.Args.Count >= 4).ToList();
                StepEntity comp = itens.FirstOrDefault(i => i.Type == "IFCQUANTITYLENGTH" && StepModel.Text(i.Args[0]) == "Length");
                if (comp == null || !double.TryParse(comp.Args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double lv) ||
                    Math.Abs(lv - antigo) > 0.02 * antigo) continue;   // o Qto não veio do corpo dobrado
                foreach (StepEntity i in itens)
                {
                    string nome = StepModel.Text(i.Args[0]);
                    if (nome != "Length" && nome != "GrossVolume" && nome != "OuterSurfaceArea") continue;
                    if (feitas.Contains(i.Id) || (citacoes.TryGetValue(i.Id, out int c) && c > 1)) continue;
                    if (!double.TryParse(i.Args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) continue;
                    m.SetArg(i.Id, 3, Num(v / 2));
                    feitas.Add(i.Id);
                    n++;
                }
            }
            return n;
        }

        private static double[] Vetor(StepModel m, string arg)
        {
            if (arg == null || !arg.StartsWith("#")) return null;
            StepEntity p = m.Get(StepModel.Ref(arg));
            if (p == null || p.Args.Count == 0) return null;
            string lista = p.Type == "IFCDIRECTION" || p.Type == "IFCCARTESIANPOINT" ? p.Args[0] : null;
            if (lista == null) return null;
            var v = StepModel.RealList(lista);
            while (v.Count < 3) v.Add(0);
            return v.Take(3).ToArray();
        }

        private static double[] Unit(double[] v)
        {
            if (v == null) return null;
            double n = Math.Sqrt(Dot(v, v));
            return n < 1e-9 ? null : new[] { v[0] / n, v[1] / n, v[2] / n };
        }

        private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

        private static string Num(double v)
        {
            if (Math.Abs(v) < 1e-12) v = 0;
            string s = v.ToString("R", CultureInfo.InvariantCulture);
            int e = s.IndexOfAny(new[] { 'E', 'e' });
            if (s.IndexOf('.') >= 0) return s;
            return e < 0 ? s + "." : s.Substring(0, e) + "." + s.Substring(e);
        }
    }
}
