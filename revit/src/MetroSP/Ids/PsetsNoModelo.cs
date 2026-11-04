using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace MetroSP.Ids
{
    /// <summary>
    /// A partir do Revit 2027 o exportador IFC lê os property sets definidos pelo usuário do próprio modelo
    /// (elementos IFCUserDefinedPropertySet, os mesmos que a janela nativa cria ao importar o .txt) e ignora
    /// o .txt passado nas opções. Por isso o .txt escolhido é carregado no modelo antes de exportar, dentro da
    /// transação da exportação: sem "gravar GUID" ela é desfeita no fim e o modelo fica como estava.
    /// </summary>
    public static class PsetsNoModelo
    {
        /// <summary>
        /// Troca os property sets do usuário guardados no modelo pelos do .txt. Precisa de transação aberta.
        /// Retorna um texto para o relatório, ou null quando a versão do Revit ainda usa o .txt direto.
        /// </summary>
        public static string Carregar(Document doc, string txt)
        {
#if REVIT2027_OR_GREATER
            // Os que já estão no modelo sairiam junto no IFC: o .txt escolhido é a referência.
            int antigos = ApagarPsetsDoModelo(doc);

            // No modelo cada property set é guardado pelo nome e cada propriedade aceita um só parâmetro.
            // Os blocos de mesmo nome são juntados, e a propriedade que muda de parâmetro conforme a entidade
            // (ou que só existe em parte dos blocos) passa por um parâmetro auxiliar preenchido elemento a elemento.
            Plano plano = Planejar(File.ReadAllLines(txt));
            int preenchidos = ParametrosAuxiliares.Criar(doc, plano);

            string arquivo = Path.Combine(Path.GetTempPath(), "MetroSP-psets-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllLines(arquivo, plano.Txt, new UTF8Encoding(true));
            IList<IFCUserDefinedPropertySet> importados;
            bool valido;
            try { importados = IFCUserDefinedPropertySet.ImportFromFile(doc, arquivo, out valido); }
            finally { try { File.Delete(arquivo); } catch { } }
            if (!valido)
                throw new InvalidOperationException("O Revit considerou o .txt de property sets inválido: " + txt);
            int n = importados?.Count ?? 0;
            return $"{n} property sets do .txt carregados no modelo para a exportação" +
                   (plano.Juntados > 0 ? $", com {plano.Juntados} blocos de mesmo nome juntados" : "") +
                   (plano.Auxiliares.Count > 0 ? $"; {plano.Auxiliares.Count} propriedades que mudam de parâmetro conforme a entidade saíram por parâmetros auxiliares ({preenchidos} valores)" : "") +
                   (antigos > 0 ? $" (substituindo {antigos} que já estavam no modelo)." : ".");
#else
            return null;
#endif
        }
    
        /// <summary>
        /// Chamado antes de confirmar a transação (setup que grava o GUID): tira os parâmetros auxiliares e deixa no
        /// modelo os property sets do .txt com os blocos juntados, sem referência aos auxiliares.
        /// </summary>
        public static void Finalizar(Document doc, string txt)
        {
#if REVIT2027_OR_GREATER
            if (!ParametrosAuxiliares.Remover(doc)) return;
            ApagarPsetsDoModelo(doc);
            string arquivo = Path.Combine(Path.GetTempPath(), "MetroSP-psets-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllLines(arquivo, UnirBlocosRepetidos(File.ReadAllLines(txt), out _), new UTF8Encoding(true));
            try { IFCUserDefinedPropertySet.ImportFromFile(doc, arquivo, out _); }
            finally { try { File.Delete(arquivo); } catch { } }
#endif
        }

#if REVIT2027_OR_GREATER
        private static int ApagarPsetsDoModelo(Document doc)
        {
            var antigos = new List<ElementId>();
            foreach (string nome in IFCUserDefinedPropertySet.ListPropertySetNames(doc))
            {
                IFCUserDefinedPropertySet pset = IFCUserDefinedPropertySet.FindPropertySetByName(doc, nome);
                if (pset != null) antigos.Add(pset.Id);
            }
            if (antigos.Count > 0) doc.Delete(antigos);
            return antigos.Count;
        }
#endif

        // ---------- plano: blocos juntados e propriedades que precisam de parâmetro auxiliar ----------

        /// <summary>Uma propriedade que, no mesmo property set, vem de parâmetros diferentes conforme a entidade.</summary>
        public class Auxiliar
        {
            public string Parametro;      // nome do parâmetro auxiliar criado na exportação
            public string PropertySet;
            public string Propriedade;
            public string Tipo;           // tipo de dado IFC da primeira linha
            public bool DeTipo;           // bloco T: o valor fica no tipo
            public List<string> EntidadesDoGrupo = new List<string>();
            /// <summary>Por bloco: entidades e parâmetros de origem, na ordem do .txt.</summary>
            public List<(List<string> Entidades, List<string> Origens)> Regras = new List<(List<string>, List<string>)>();
        }

        public class Plano
        {
            public string[] Txt;
            public int Juntados;
            public List<Auxiliar> Auxiliares = new List<Auxiliar>();
        }

        /// <summary>
        /// Lê o .txt e monta o arquivo para o modelo: um bloco por nome (entidades unidas). A propriedade com um só
        /// parâmetro em todos os blocos do grupo fica como está; a que muda de parâmetro, ou que falta em algum bloco
        /// (e então não deve aparecer nas outras entidades), aponta para um parâmetro auxiliar.
        /// </summary>
        public static Plano Planejar(string[] linhas)
        {
            var blocos = new List<(string Nome, string Aplicacao, List<string> Entidades, List<(string Prop, string Tipo, string Param, string Linha)> Props)>();
            (string, string, List<string>, List<(string, string, string, string)>)? atual = null;
            foreach (string linha in linhas)
            {
                string[] c = linha.Split('\t');
                if (c[0].Trim() == "PropertySet:" && c.Length >= 4)
                {
                    var b = (c[1].Trim(), c[2].Trim(), c[3].Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(e => e.Trim()).ToList(),
                             new List<(string, string, string, string)>());
                    blocos.Add(b);
                    atual = b;
                    continue;
                }
                if (linha.TrimStart().StartsWith("#") || linha.Trim().Length == 0) continue;
                if (atual != null && c.Length >= 2 && c[0].Trim().Length == 0)
                {
                    string prop = c[1].Trim();
                    string tipo = c.Length > 2 ? c[2].Trim() : "Text";
                    string param = c.Length > 3 && c[3].Trim().Length > 0 ? c[3].Trim() : prop;
                    atual.Value.Item4.Add((prop, tipo, param, linha));
                }
                else atual = null;
            }

            var plano = new Plano();
            var saida = new List<string> { "# Gerado pelo MetroSP para a exportação (blocos de mesmo nome juntados)." };
            var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var grupo in blocos.GroupBy(b => b.Nome + "\t" + b.Aplicacao.ToUpperInvariant(), StringComparer.Ordinal))
            {
                var lista = grupo.ToList();
                plano.Juntados += lista.Count - 1;
                var entidades = new List<string>();
                foreach (var b in lista)
                    foreach (string e in b.Entidades)
                        if (!entidades.Contains(e, StringComparer.OrdinalIgnoreCase)) entidades.Add(e);
                saida.Add($"PropertySet:\t{lista[0].Nome}\t{lista[0].Aplicacao}\t{string.Join(",", entidades)}");

                var nomes = new List<string>();
                foreach (var b in lista)
                    foreach (var p in b.Props)
                        if (!nomes.Contains(p.Prop, StringComparer.OrdinalIgnoreCase)) nomes.Add(p.Prop);

                foreach (string prop in nomes)
                {
                    var linhasProp = lista.SelectMany(b => b.Props.Where(p => string.Equals(p.Prop, prop, StringComparison.OrdinalIgnoreCase))).ToList();
                    bool variosParametros = linhasProp.Select(p => p.Param).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
                    bool faltaEmAlgum = lista.Any(b => !b.Props.Any(p => string.Equals(p.Prop, prop, StringComparison.OrdinalIgnoreCase)));
                    if (!variosParametros && !faltaEmAlgum)
                    {
                        saida.Add(linhasProp[0].Linha);
                        continue;
                    }
                    var aux = new Auxiliar
                    {
                        Parametro = NomeAuxiliar(lista[0].Nome, prop, usados),
                        PropertySet = lista[0].Nome,
                        Propriedade = prop,
                        Tipo = linhasProp[0].Tipo,
                        DeTipo = lista[0].Aplicacao.Trim().ToUpperInvariant().StartsWith("T"),
                        EntidadesDoGrupo = entidades,
                    };
                    foreach (var b in lista)
                    {
                        List<string> origens = b.Props.Where(p => string.Equals(p.Prop, prop, StringComparison.OrdinalIgnoreCase))
                            .Select(p => p.Param).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        if (origens.Count > 0) aux.Regras.Add((b.Entidades, origens));
                    }
                    plano.Auxiliares.Add(aux);
                    saida.Add($"\t{prop}\t{aux.Tipo}\t{aux.Parametro}");
                }
            }
            plano.Txt = saida.ToArray();
            return plano;
        }

        private static string NomeAuxiliar(string pset, string prop, HashSet<string> usados)
        {
            string Limpo(string t) => new string(t.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
            string nome = "MetroSP_" + Limpo(pset) + "_" + Limpo(prop);
            string unico = nome;
            for (int i = 2; !usados.Add(unico); i++) unico = nome + "_" + i;
            return unico;
        }

        /// <summary>
        /// Junta os blocos "PropertySet:" de mesmo nome e mesmo I/T num só, no lugar do primeiro: as entidades
        /// são unidas e as propriedades também (a mesma linha repetida entra uma vez). Uma propriedade com o mesmo
        /// nome e parâmetros diferentes fica com todas as linhas, como já acontece num bloco só: o exportador usa a
        /// que o elemento tiver. Comentários e a ordem do resto do arquivo ficam como estão.
        /// </summary>
        public static string[] UnirBlocosRepetidos(string[] linhas, out int juntados)
        {
            juntados = 0;
            var blocos = new List<Bloco>();
            var saida = new List<object>(); // string (linha solta) ou Bloco
            var porChave = new Dictionary<string, Bloco>(StringComparer.Ordinal);
            Bloco atual = null;
            foreach (string linha in linhas)
            {
                string[] c = linha.Split('\t');
                if (c[0].Trim() == "PropertySet:" && c.Length >= 4)
                {
                    string chave = c[1].Trim() + "\t" + c[2].Trim().ToUpperInvariant();
                    if (porChave.TryGetValue(chave, out atual)) juntados++;
                    else
                    {
                        atual = new Bloco { Nome = c[1].Trim(), Aplicacao = c[2].Trim() };
                        porChave[chave] = atual;
                        blocos.Add(atual);
                        saida.Add(atual);
                    }
                    foreach (string e in c[3].Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                        if (!atual.Entidades.Contains(e.Trim(), StringComparer.OrdinalIgnoreCase)) atual.Entidades.Add(e.Trim());
                    continue;
                }
                bool propriedade = atual != null && c.Length >= 2 && c[0].Trim().Length == 0 && !linha.TrimStart().StartsWith("#");
                if (propriedade)
                {
                    string chave = string.Join("\t", c.Skip(1).Select(x => x.Trim()));
                    if (atual.Vistas.Add(chave)) atual.Linhas.Add(linha);
                    continue;
                }
                if (linha.TrimStart().StartsWith("#") || linha.Trim().Length == 0) { saida.Add(linha); continue; }
                atual = null; // linha estranha fora de bloco: mantém e encerra o bloco
                saida.Add(linha);
            }
            if (juntados == 0) return linhas;
            var r = new List<string>();
            foreach (object o in saida)
            {
                if (o is Bloco b)
                {
                    r.Add($"PropertySet:\t{b.Nome}\t{b.Aplicacao}\t{string.Join(",", b.Entidades)}");
                    r.AddRange(b.Linhas);
                }
                else r.Add((string)o);
            }
            return r.ToArray();
        }

        private class Bloco
        {
            public string Nome, Aplicacao;
            public readonly List<string> Entidades = new List<string>();
            public readonly List<string> Linhas = new List<string>();
            public readonly HashSet<string> Vistas = new HashSet<string>(StringComparer.Ordinal);
        }
    }
}
