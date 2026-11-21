using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MetroSP.Ifc
{
    /// <summary>Uma barra de um conjunto de armadura do Revit, na ordem das posições do conjunto.</summary>
    public sealed class BarraRevit
    {
        /// <summary>Comprimento da barra, em metros.</summary>
        public double Comprimento;
        /// <summary>Peso da barra, em kg (0 = desconhecido, não grava o Weight).</summary>
        public double Peso;
    }

    /// <summary>
    /// Quantidades das barras de armadura (IfcReinforcingBar), que o exportador da Autodesk não grava:
    /// <list type="bullet">
    /// <item>O Qto_ReinforcingElementBaseQuantities não sai: o Count e o Weight não têm cálculo no exportador e o
    /// Length procura um parâmetro "Length", que a armadura não tem.</item>
    /// <item>Nos conjuntos de comprimento variável cada barra vira um IfcReinforcingBar, mas os psets leem o "Bar Length"
    /// do conjunto, que fica vazio.</item>
    /// <item>No IFC 2x3 o atributo BarLength das barras variáveis sai em pés (sem conversão de unidade).</item>
    /// </list>
    /// O IfcReinforcingBar é ligado ao Revit pelo Tag (o ElementId). Um conjunto exportado barra a barra (variável, ou
    /// com a opção de separar as barras) tem um IfcReinforcingBar por barra, na ordem das posições; um conjunto uniforme
    /// sai como um IfcReinforcingBar só, com todas as barras (Count = número de barras, Length e Weight = totais).
    /// </summary>
    public static class ArmaduraIfc
    {
        public const string Qto = "Qto_ReinforcingElementBaseQuantities";
        private static readonly string[] Nossas = { "Count", "Length", "Weight" };

        /// <summary>Resultado: quantos IfcReinforcingBar receberam as quantidades e quantos ficaram sem.</summary>
        public sealed class Resultado
        {
            public int Barras, Gravadas, SemRevit, Diferentes, BarLength2x3;
            public override string ToString()
            {
                if (Barras == 0) return null;
                string s = $"Armaduras: Count, Length e Weight ({Qto}) em {Gravadas} de {Barras} IfcReinforcingBar.";
                if (BarLength2x3 > 0) s += $" BarLength corrigido em {BarLength2x3}.";
                if (SemRevit > 0) s += $" {SemRevit} sem a armadura correspondente no Revit.";
                if (Diferentes > 0) s += $" {Diferentes} com número de barras diferente do Revit (não gravados).";
                return s;
            }
        }

        /// <summary>
        /// Grava as quantidades. barrasDoElemento: ElementId do Revit → barras do conjunto, na ordem das posições
        /// (null ou vazio = não é armadura). Um Qto_ReinforcingElementBaseQuantities que já exista no elemento é
        /// trocado (as outras quantidades dele ficam).
        /// </summary>
        public static Resultado Aplicar(StepModel m, Func<long, IReadOnlyList<BarraRevit>> barrasDoElemento)
        {
            var r = new Resultado();
            List<StepEntity> barras = m.OfType("IFCREINFORCINGBAR").Where(e => e.Args.Count >= 8).ToList();
            r.Barras = barras.Count;
            if (barras.Count == 0) return r;

            bool ifc2x3 = m.Schema != null && m.Schema.StartsWith("IFC2X3", StringComparison.OrdinalIgnoreCase);
            double metrosPorUnidade = FatorUnidade(m, "LENGTHUNIT", 1.0);
            double kgPorUnidade = FatorUnidade(m, "MASSUNIT", 1.0);

            var alvo = Valores(barras, barrasDoElemento, r);
            if (alvo.Count == 0) return r;

            // IFC 2x3: BarLength (atributo 11) com o comprimento da barra, na unidade do arquivo.
            if (ifc2x3)
            {
                foreach (var a in alvo)
                {
                    StepEntity e = m.Get(a.Key);
                    if (e.Args.Count <= 11 || a.Value.BarLength <= 0) continue;
                    m.SetArg(a.Key, 11, Real(a.Value.BarLength / metrosPorUnidade));
                    r.BarLength2x3++;
                }
            }

            // Qto que já exista nesses elementos: guarda as outras quantidades e tira o elemento dele.
            List<StepEntity> rels = m.OfType("IFCRELDEFINESBYPROPERTIES").ToList();
            string historico = rels.Select(x => x.Args[1]).FirstOrDefault(x => x != "$")
                ?? m.OfType("IFCOWNERHISTORY").Select(x => "#" + x.Id).FirstOrDefault() ?? "$";
            var outrasDoObjeto = new Dictionary<int, List<int>>();
            var qtosAntigos = new HashSet<int>();
            foreach (StepEntity rel in rels)
            {
                int def = StepModel.Ref(rel.Args[5]);
                if (m.TypeOf(def) != "IFCELEMENTQUANTITY") continue;
                StepEntity qto = m.Get(def);
                if (!string.Equals(StepModel.Text(qto.Args[2]), Qto, StringComparison.OrdinalIgnoreCase)) continue;
                List<int> objs = StepModel.RefList(rel.Args[4]);
                if (!objs.Any(alvo.ContainsKey)) continue;
                List<int> outras = StepModel.RefList(qto.Args[5])
                    .Where(q => !Nossas.Contains(StepModel.Text(m.Get(q)?.Args[0]), StringComparer.OrdinalIgnoreCase)).ToList();
                foreach (int o in objs.Where(alvo.ContainsKey)) outrasDoObjeto[o] = outras;
                List<int> ficam = objs.Where(o => !alvo.ContainsKey(o)).ToList();
                if (ficam.Count > 0) m.SetArg(rel.Id, 4, StepModel.RefListText(ficam));
                else { m.Remove(rel.Id); qtosAntigos.Add(def); }
            }
            foreach (int def in qtosAntigos)
                if (!m.Referenciado(def)) m.Remove(def);   // as quantidades dele podem ter ido para o novo

            // Um Qto para cada combinação de valores (barras iguais compartilham o mesmo).
            bool ifc4 = !ifc2x3;
            string Quantidade(string tipo, string nome, string valor) =>
                ifc4 ? "#" + m.Add(tipo, Texto(nome), "$", "$", valor, "$") : "#" + m.Add(tipo, Texto(nome), "$", "$", valor);
            foreach (var g in alvo.GroupBy(a =>
            {
                string outras = outrasDoObjeto.TryGetValue(a.Key, out var o) ? string.Join(",", o) : "";
                return (a.Value.Count, Real(a.Value.Comprimento / metrosPorUnidade),
                        a.Value.Peso > 0 ? Real(a.Value.Peso / kgPorUnidade) : null, outras);
            }))
            {
                var q = new List<string>
                {
                    // IfcCountMeasure é inteiro no IFC 4.3 (NUMBER no 4 e no 2x3, que também aceita inteiro).
                    Quantidade("IFCQUANTITYCOUNT", "Count", g.Key.Count.ToString(CultureInfo.InvariantCulture)),
                    Quantidade("IFCQUANTITYLENGTH", "Length", g.Key.Item2),
                };
                if (g.Key.Item3 != null) q.Add(Quantidade("IFCQUANTITYWEIGHT", "Weight", g.Key.Item3));
                if (g.Key.outras.Length > 0) q.InsertRange(0, g.Key.outras.Split(',').Select(x => "#" + x));
                List<int> objs = g.Select(a => a.Key).OrderBy(i => i).ToList();
                string chave = string.Join("|", g.Key.Count, g.Key.Item2, g.Key.Item3, g.Key.outras, string.Join(",", objs));
                int qto = m.Add("IFCELEMENTQUANTITY", Guid("qto|" + chave), historico, Texto(Qto), "$", "$", "(" + string.Join(",", q) + ")");
                m.Add("IFCRELDEFINESBYPROPERTIES", Guid("rel|" + chave), historico, "$", "$", StepModel.RefListText(objs), "#" + qto);
                r.Gravadas += objs.Count;
            }
            return r;
        }

        /// <summary>
        /// Valores de cada IfcReinforcingBar pelo Revit: Count, comprimento total (m), peso total (kg, 0 = desconhecido) e
        /// comprimento de uma barra (m). PorBarra = o conjunto saiu barra a barra (um IfcReinforcingBar por barra).
        /// </summary>
        public static Dictionary<int, (int Count, double Comprimento, double Peso, double BarLength, bool PorBarra)> Valores(
            StepModel m, Func<long, IReadOnlyList<BarraRevit>> barrasDoElemento) =>
            Valores(m.OfType("IFCREINFORCINGBAR").Where(e => e.Args.Count >= 8).ToList(), barrasDoElemento, new Resultado());

        private static Dictionary<int, (int Count, double Comprimento, double Peso, double BarLength, bool PorBarra)> Valores(
            List<StepEntity> barras, Func<long, IReadOnlyList<BarraRevit>> barrasDoElemento, Resultado r)
        {
            // IfcReinforcingBar → (Count, Length em m, Weight em kg, BarLength em m)
            var alvo = new Dictionary<int, (int Count, double Comprimento, double Peso, double BarLength, bool PorBarra)>();
            foreach (var g in barras.GroupBy(e => long.TryParse(StepModel.Text(e.Args[7])?.Trim(), out long id) ? id : -1))
            {
                List<StepEntity> doConjunto = g.OrderBy(e => e.Id).ToList();
                IReadOnlyList<BarraRevit> doRevit = g.Key < 0 ? null : barrasDoElemento(g.Key);
                if (doRevit == null || doRevit.Count == 0) { r.SemRevit += doConjunto.Count; continue; }
                if (doConjunto.Count == doRevit.Count)
                {
                    for (int i = 0; i < doConjunto.Count; i++)
                        alvo[doConjunto[i].Id] = (1, doRevit[i].Comprimento, doRevit[i].Peso, doRevit[i].Comprimento, doConjunto.Count > 1);
                }
                else if (doConjunto.Count == 1)
                {
                    double total = doRevit.Sum(b => b.Comprimento);
                    double peso = doRevit.Any(b => b.Peso <= 0) ? 0 : doRevit.Sum(b => b.Peso);
                    alvo[doConjunto[0].Id] = (doRevit.Count, total, peso, total / doRevit.Count, false);
                }
                else r.Diferentes += doConjunto.Count;
            }
            return alvo;
        }

        /// <summary>
        /// Quantas unidades SI de base (metro, kg) vale a unidade do tipo (LENGTHUNIT, MASSUNIT) declarada no IfcProject.
        /// Sem a unidade no arquivo, padrao.
        /// </summary>
        public static double FatorUnidade(StepModel m, string tipo, double padrao)
        {
            string enumTipo = "." + tipo + ".";
            foreach (StepEntity a in m.OfType("IFCUNITASSIGNMENT"))
                foreach (int u in StepModel.RefList(a.Args[0]))
                {
                    StepEntity e = m.Get(u);
                    if (e == null || e.Args.Count < 2 || !string.Equals(e.Args[1], enumTipo, StringComparison.OrdinalIgnoreCase)) continue;
                    double f = Fator(m, u);
                    if (f > 0) return f;
                }
            return padrao;
        }

        private static double Fator(StepModel m, int id, int nivel = 0)
        {
            StepEntity e = m.Get(id);
            if (e == null || nivel > 4) return 0;
            if (e.Type == "IFCSIUNIT" && e.Args.Count >= 4)
            {
                double prefixo = Prefixo(e.Args[2]);
                // A unidade SI de base da massa é o quilograma: GRAM sem prefixo vale 0,001 kg.
                return string.Equals(e.Args[3], ".GRAM.", StringComparison.OrdinalIgnoreCase) ? prefixo / 1000 : prefixo;
            }
            if (e.Type == "IFCCONVERSIONBASEDUNIT" && e.Args.Count >= 4)
            {
                StepEntity mw = m.Get(StepModel.Ref(e.Args[3]));   // IfcMeasureWithUnit(valor, unidade)
                if (mw == null || mw.Args.Count < 2) return 0;
                string v = mw.Args[0];
                int p = v.IndexOf('(');
                if (p >= 0) v = v.Substring(p + 1).TrimEnd(')');
                if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double valor)) return 0;
                double baseUnidade = Fator(m, StepModel.Ref(mw.Args[1]), nivel + 1);
                return baseUnidade > 0 ? valor * baseUnidade : 0;
            }
            return 0;
        }

        private static double Prefixo(string arg)
        {
            switch ((arg ?? "").Trim('.').ToUpperInvariant())
            {
                case "MEGA": return 1e6;
                case "KILO": return 1e3;
                case "HECTO": return 1e2;
                case "DECA": return 1e1;
                case "DECI": return 1e-1;
                case "CENTI": return 1e-2;
                case "MILLI": return 1e-3;
                case "MICRO": return 1e-6;
                default: return 1;
            }
        }

        /// <summary>Número real no formato STEP (sempre com ponto decimal), arredondado a 6 casas.</summary>
        public static string Real(double v)
        {
            string s = Math.Round(v, 6).ToString("0.0#####", CultureInfo.InvariantCulture);
            return s.EndsWith(".0") ? s.Substring(0, s.Length - 1) : s;
        }

        /// <summary>Texto STEP: aspas duplicadas, barra invertida escapada, não-ASCII em \X2\.</summary>
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
            using (var md5 = MD5.Create()) h = md5.ComputeHash(Encoding.UTF8.GetBytes("MetroSP|armadura|" + key));
            const string chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";
            var n = Compat.InteiroSemSinal(h);
            var c = new char[22];
            for (int i = 21; i >= 0; i--) { c[i] = chars[(int)(n & 63)]; n >>= 6; }
            return "'" + new string(c) + "'";
        }
    }
}
