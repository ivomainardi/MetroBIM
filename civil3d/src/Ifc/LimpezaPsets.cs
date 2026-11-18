using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MetroSP.Ifc
{
    /// <summary>
    /// Tira do IFC os property sets que não são padrão: o IFCINFRAEXPORT grava Psets das propriedades internas do
    /// Civil 3D (General, Properties, Geometry...), do LandXML importado e dos property sets criados à mão nos desenhos.
    /// Ficam só os da ISO 16739 (Pset_ e Qto_), os do MetroDATA, os do plugin (MetroSP_, Classification) e os que o
    /// perfil lista em Psets.Permitidos.
    /// </summary>
    public static class LimpezaPsets
    {
        /// <summary>Psets do MetroDATA (planilha do Airtable) e do plugin.</summary>
        public static readonly string[] Padrao = { "Metro", "MetroLoc", "MetroQto", "MetroClassificacao", "Classification",
            // Do próprio exportador, no IfcProject; com o PTL ligado o pós-processamento já o troca pelo IfcProjectedCRS.
            "Coordinate Reference System" };

        /// <summary>
        /// Dados úteis que o exportador grava em Psets próprios e que passam para o MetroDATA antes da limpeza
        /// (só nos objetos que ainda não têm a propriedade de destino).
        /// </summary>
        public static readonly (string Pset, string Prop, string NovoPset, string NovaProp)[] Converter =
        {
            ("Quantities", "ComputedVolume", "MetroQto", "Volume"),
            ("Quantities", "ComputedLength", "MetroQto", "Comprimento"),
            ("Corridor Shape Information", "AssemblyStartStation", "MetroLoc", "InicioEstaca"),
            ("Corridor Shape Information", "AssemblyEndStation", "MetroLoc", "FimEstaca"),
        };

        public static bool Permitido(string nome, ICollection<string> extras)
        {
            if (string.IsNullOrWhiteSpace(nome)) return true;   // sem nome: não é nosso decidir
            nome = nome.Trim();
            if (nome.StartsWith("Pset_", StringComparison.Ordinal) || nome.StartsWith("Qto_", StringComparison.Ordinal) ||
                nome.StartsWith("MetroSP_", StringComparison.OrdinalIgnoreCase)) return true;
            if (Padrao.Any(p => string.Equals(p, nome, StringComparison.OrdinalIgnoreCase))) return true;
            return extras != null && extras.Any(p => string.Equals(p?.Trim(), nome, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Apaga os IfcPropertySet fora do padrão, as relações e as propriedades que só eles usavam. Devolve quantos saíram.</summary>
        public static int Aplicar(StepModel m, ICollection<string> extras, Action<string> log)
        {
            try { Converte(m, log); }
            catch (Exception ex) { log?.Invoke("os dados do exportador não passaram para o MetroQto/MetroLoc: " + ex.Message); }

            var fora = new HashSet<int>();
            var nomes = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var ps in m.OfType("IFCPROPERTYSET"))
            {
                string nome = ps.Args.Count > 2 ? StepModel.Text(ps.Args[2]) : null;
                if (Permitido(nome, extras)) continue;
                fora.Add(ps.Id);
                nomes[nome] = nomes.TryGetValue(nome, out int n) ? n + 1 : 1;
            }
            if (fora.Count == 0) return 0;

            // Propriedades dos Psets que saem (candidatas a sair junto, se ninguém mais usa).
            var props = new HashSet<int>();
            foreach (int id in fora)
                foreach (int p in StepModel.RefList(m.Get(id).Args[4])) props.Add(p);

            // Quem cita os Psets: relações (saem ou perdem o Pset) e tipos (HasPropertySets perde o Pset).
            foreach (var kv in m.QuemCita(fora))
            {
                var e = m.Get(kv.Key);
                if (e == null) continue;
                if (e.Type == "IFCRELDEFINESBYPROPERTIES")
                {
                    var defs = StepModel.RefList(e.Args[5]);
                    if (defs.Count == 0 || defs.All(fora.Contains)) { m.Remove(e.Id); continue; }
                    if (e.Args[5].TrimStart().StartsWith("("))
                        m.SetArg(e.Id, 5, StepModel.RefListText(defs.Where(d => !fora.Contains(d))));
                    continue;
                }
                for (int i = 0; i < e.Args.Count; i++)
                {
                    string a = e.Args[i].Trim();
                    if (!a.StartsWith("(") || !a.Contains("#")) continue;
                    var refs = StepModel.RefList(a);
                    if (!refs.Any(fora.Contains)) continue;
                    var resto = refs.Where(r => !fora.Contains(r)).ToList();
                    m.SetArg(e.Id, i, resto.Count == 0 ? "$" : StepModel.RefListText(resto));
                }
            }
            foreach (int id in fora) m.Remove(id);

            // Propriedades que ficaram sem ninguém.
            var usadas = m.QuemCita(props).Values.SelectMany(v => v).ToHashSet();
            int soltas = 0;
            foreach (int p in props.Where(p => !usadas.Contains(p))) { m.Remove(p); soltas++; }

            log?.Invoke($"{fora.Count} property set(s) fora do padrão (ISO, MetroDATA e MetroSP) saíram do IFC, com {soltas} propriedade(s): " +
                        string.Join(", ", nomes.Select(kv => kv.Value > 1 ? $"{kv.Key} ({kv.Value})" : kv.Key)) + ".");
            return fora.Count;
        }

        /// <summary>Copia as propriedades da tabela <see cref="Converter"/> para Psets do MetroDATA (novo Pset por relação).</summary>
        static void Converte(StepModel m, Action<string> log)
        {
            var origens = new HashSet<string>(Converter.Select(c => c.Pset), StringComparer.OrdinalIgnoreCase);
            var destinos = new HashSet<string>(Converter.Select(c => c.NovoPset), StringComparer.OrdinalIgnoreCase);

            // Objeto → "Pset.Propriedade" que ele já tem nos Psets de destino.
            var tem = new Dictionary<int, HashSet<string>>();
            var rels = new List<(StepEntity Rel, StepEntity Pset)>();
            foreach (var r in m.OfType("IFCRELDEFINESBYPROPERTIES").ToList())
            {
                var ps = m.Get(StepModel.Ref(r.Args[5]));
                if (ps == null || ps.Type != "IFCPROPERTYSET") continue;
                string nome = StepModel.Text(ps.Args[2]) ?? "";
                if (origens.Contains(nome)) rels.Add((r, ps));
                if (!destinos.Contains(nome)) continue;
                foreach (int pid in StepModel.RefList(ps.Args[4]))
                {
                    var pe = m.Get(pid);
                    if (pe == null) continue;
                    string chave = nome + "." + StepModel.Text(pe.Args[0]);
                    foreach (int o in StepModel.RefList(r.Args[4]))
                    {
                        if (!tem.TryGetValue(o, out var h)) tem[o] = h = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        h.Add(chave);
                    }
                }
            }

            int criados = 0;
            foreach (var (rel, ps) in rels)
            {
                string nome = StepModel.Text(ps.Args[2]);
                var objs = StepModel.RefList(rel.Args[4]);
                // Valores desta relação, agrupados pelo Pset de destino.
                var porDestino = new Dictionary<string, List<(string Prop, string Valor)>>(StringComparer.OrdinalIgnoreCase);
                foreach (int pid in StepModel.RefList(ps.Args[4]))
                {
                    var pe = m.Get(pid);
                    if (pe == null || pe.Type != "IFCPROPERTYSINGLEVALUE" || pe.Args.Count < 3 || pe.Args[2] == "$") continue;
                    string prop = StepModel.Text(pe.Args[0]);
                    foreach (var c in Converter.Where(c => string.Equals(c.Pset, nome, StringComparison.OrdinalIgnoreCase) &&
                                                           string.Equals(c.Prop, prop, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (!porDestino.TryGetValue(c.NovoPset, out var l)) porDestino[c.NovoPset] = l = new List<(string, string)>();
                        l.Add((c.NovaProp, pe.Args[2]));
                    }
                }
                foreach (var kv in porDestino)
                {
                    // Só os objetos que ainda não têm nenhuma dessas propriedades no Pset de destino.
                    var alvo = objs.Where(o => !tem.TryGetValue(o, out var h) || !kv.Value.Any(v => h.Contains(kv.Key + "." + v.Prop))).ToList();
                    if (alvo.Count == 0) continue;
                    var props = kv.Value.Select(v => m.Add("IFCPROPERTYSINGLEVALUE", Txt(v.Prop), "$", v.Valor, "$")).ToList();
                    string semente = StepModel.Text(rel.Args[0]) + "|" + kv.Key;
                    int novo = m.Add("IFCPROPERTYSET", Guid(semente + "|pset"), "$", Txt(kv.Key), "$", StepModel.RefListText(props));
                    m.Add("IFCRELDEFINESBYPROPERTIES", Guid(semente + "|rel"), "$", "$", "$", StepModel.RefListText(alvo), "#" + novo);
                    foreach (int o in alvo)
                    {
                        if (!tem.TryGetValue(o, out var h)) tem[o] = h = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var v in kv.Value) h.Add(kv.Key + "." + v.Prop);
                    }
                    criados++;
                }
            }
            if (criados > 0)
                log?.Invoke($"{criados} Pset(s) MetroQto/MetroLoc criados com o volume, o comprimento e as estacas que o exportador gravou em Quantities e Corridor Shape Information.");
        }

        static string Txt(string s) => "'" + (s ?? "").Replace("'", "''") + "'";

        const string Base64 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

        /// <summary>GlobalId estável (a mesma exportação gera os mesmos ids).</summary>
        static string Guid(string semente)
        {
            var n = new System.Numerics.BigInteger(MD5.HashData(Encoding.UTF8.GetBytes("MetroSP|psets|" + semente)), isUnsigned: true, isBigEndian: true);
            var c = new char[22];
            for (int i = 21; i >= 0; i--) { c[i] = Base64[(int)(n & 63)]; n >>= 6; }
            return "'" + new string(c) + "'";
        }
    }
}
