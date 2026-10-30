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
            var antigos = new List<ElementId>();
            foreach (string nome in IFCUserDefinedPropertySet.ListPropertySetNames(doc))
            {
                IFCUserDefinedPropertySet pset = IFCUserDefinedPropertySet.FindPropertySetByName(doc, nome);
                if (pset != null) antigos.Add(pset.Id);
            }
            if (antigos.Count > 0) doc.Delete(antigos);

            // No modelo cada property set é guardado pelo nome: blocos repetidos (um "Metro" por entidade) viram um só.
            string[] unido = UnirBlocosRepetidos(File.ReadAllLines(txt), out int juntados);
            string arquivo = txt;
            if (juntados > 0)
            {
                arquivo = Path.Combine(Path.GetTempPath(), "MetroSP-psets-" + Guid.NewGuid().ToString("N") + ".txt");
                File.WriteAllLines(arquivo, unido, new UTF8Encoding(true));
            }
            IList<IFCUserDefinedPropertySet> importados;
            bool valido;
            try { importados = IFCUserDefinedPropertySet.ImportFromFile(doc, arquivo, out valido); }
            finally { if (arquivo != txt) try { File.Delete(arquivo); } catch { } }
            if (!valido)
                throw new InvalidOperationException("O Revit considerou o .txt de property sets inválido: " + txt);
            int n = importados?.Count ?? 0;
            return $"{n} property sets do .txt carregados no modelo para a exportação" +
                   (juntados > 0 ? $", com {juntados} blocos de mesmo nome juntados (ex.: um Metro por entidade)" : "") +
                   (antigos.Count > 0 ? $" (substituindo {antigos.Count} que já estavam no modelo)." : ".");
#else
            return null;
#endif
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
