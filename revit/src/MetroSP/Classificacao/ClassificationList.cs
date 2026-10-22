using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace MetroSP.Classificacao
{
    /// <summary>Um item da classificação (equivalente a um IfcClassificationReference).</summary>
    public class ClassItem
    {
        public string Codigo { get; set; }
        public string Descricao { get; set; }
        public string CodigoPai { get; set; }
        /// <summary>Categorias do Revit em que o item vale (vazio = todas).</summary>
        public List<string> Categorias { get; } = new List<string>();
        public ClassItem Pai { get; set; }
        public List<ClassItem> Filhos { get; } = new List<ClassItem>();

        public string Caminho
        {
            get
            {
                var partes = new List<string>();
                for (ClassItem i = Pai; i != null; i = i.Pai) partes.Insert(0, i.Descricao);
                return string.Join(" › ", partes);
            }
        }

        public bool ValeParaCategoria(string categoria) =>
            Categorias.Count == 0 || Categorias.Any(c => string.Equals(c, categoria, StringComparison.CurrentCultureIgnoreCase));

        public override string ToString() => $"{Codigo}  {Descricao}";
    }

    /// <summary>
    /// Lista de classificação lida de uma planilha (.xlsx ou .csv), no espírito do IfcClassification:
    /// colunas Código, Descrição e, opcionais, Código pai, Categorias e Edição.
    /// Sem "Código pai", a hierarquia sai do próprio código (EST.02.01 é filho de EST.02).
    /// </summary>
    public class ClassificationList
    {
        public string Arquivo { get; private set; }
        public string Nome { get; set; }
        public string Edicao { get; set; }
        public List<ClassItem> Itens { get; } = new List<ClassItem>();
        public List<ClassItem> Raizes { get; } = new List<ClassItem>();
        public Dictionary<string, ClassItem> PorCodigo { get; } = new Dictionary<string, ClassItem>(StringComparer.OrdinalIgnoreCase);
        public List<string> Avisos { get; } = new List<string>();

        public static ClassificationList Ler(string caminho)
        {
            List<List<string>> linhas = Path.GetExtension(caminho).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
                ? LerXlsx(caminho)
                : LerCsv(caminho);

            var lista = new ClassificationList { Arquivo = caminho, Nome = Path.GetFileNameWithoutExtension(caminho) };

            // Cabeçalho: primeira linha com uma coluna de código.
            int h = linhas.FindIndex(l => l.Any(c => EhColuna(c, "código", "codigo", "code", "identification")));
            if (h < 0) throw new InvalidDataException("Não encontrei a coluna \"Código\" na planilha.");
            List<string> cab = linhas[h];
            int cCod = Coluna(cab, "código", "codigo", "code", "identification");
            int cDesc = Coluna(cab, "descrição", "descricao", "description", "nome", "name", "título", "titulo");
            int cPai = Coluna(cab, "código pai", "codigo pai", "pai", "parent", "referencedsource");
            int cCat = Coluna(cab, "categorias", "categoria", "categorias revit", "categories");
            int cEd = Coluna(cab, "edição", "edicao", "edition", "versão", "versao");
            if (cDesc < 0) throw new InvalidDataException("Não encontrei a coluna \"Descrição\" na planilha.");

            for (int i = h + 1; i < linhas.Count; i++)
            {
                List<string> l = linhas[i];
                string cod = Celula(l, cCod);
                if (string.IsNullOrWhiteSpace(cod)) continue;
                var item = new ClassItem { Codigo = cod.Trim(), Descricao = (Celula(l, cDesc) ?? "").Trim(), CodigoPai = Celula(l, cPai)?.Trim() };
                string cats = Celula(l, cCat);
                if (!string.IsNullOrWhiteSpace(cats))
                    item.Categorias.AddRange(cats.Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim()));
                if (lista.Edicao == null && cEd >= 0) lista.Edicao = Celula(l, cEd);

                if (lista.PorCodigo.ContainsKey(item.Codigo))
                {
                    lista.Avisos.Add($"Código repetido na planilha: {item.Codigo} (linha {i + 1}); ficou a primeira ocorrência.");
                    continue;
                }
                lista.PorCodigo[item.Codigo] = item;
                lista.Itens.Add(item);
            }

            lista.MontarArvore();
            return lista;
        }

        private void MontarArvore()
        {
            foreach (ClassItem i in Itens)
            {
                ClassItem pai = null;
                if (!string.IsNullOrEmpty(i.CodigoPai)) PorCodigo.TryGetValue(i.CodigoPai, out pai);
                if (pai == null)
                {
                    // Maior código que é prefixo deste, seguido de separador.
                    pai = Itens.Where(o => o != i && o.Codigo.Length < i.Codigo.Length &&
                                           i.Codigo.StartsWith(o.Codigo, StringComparison.OrdinalIgnoreCase) &&
                                           ".- _/".IndexOf(i.Codigo[o.Codigo.Length]) >= 0)
                               .OrderByDescending(o => o.Codigo.Length).FirstOrDefault();
                }
                i.Pai = pai;
                if (pai != null) pai.Filhos.Add(i); else Raizes.Add(i);
            }
        }

        private static bool EhColuna(string c, params string[] nomes) =>
            c != null && nomes.Any(n => string.Equals(c.Trim(), n, StringComparison.CurrentCultureIgnoreCase));

        private static int Coluna(List<string> cab, params string[] nomes)
        {
            // Nome exato primeiro ("Código pai" não pode ser confundido com "Código").
            for (int i = 0; i < cab.Count; i++) if (EhColuna(cab[i], nomes)) return i;
            return -1;
        }

        private static string Celula(List<string> l, int i) => i >= 0 && i < l.Count ? l[i] : null;

        // ------------------------------------------------------------------ leitura

        private static List<List<string>> LerCsv(string caminho)
        {
            byte[] bytes = File.ReadAllBytes(caminho);
            string texto;
            try { texto = new UTF8Encoding(false, true).GetString(bytes).TrimStart('﻿'); }
            catch (DecoderFallbackException) { texto = Encoding.Latin1.GetString(bytes); }

            string primeira = texto.Split('\n')[0];
            char sep = primeira.Count(c => c == ';') >= primeira.Count(c => c == ',') ? ';' : ',';
            if (primeira.Count(c => c == '\t') > primeira.Count(c => c == sep)) sep = '\t';

            var linhas = new List<List<string>>();
            var atual = new List<string>();
            var campo = new StringBuilder();
            bool aspas = false;
            for (int i = 0; i < texto.Length; i++)
            {
                char ch = texto[i];
                if (aspas)
                {
                    if (ch == '"' && i + 1 < texto.Length && texto[i + 1] == '"') { campo.Append('"'); i++; }
                    else if (ch == '"') aspas = false;
                    else campo.Append(ch);
                }
                else if (ch == '"') aspas = true;
                else if (ch == sep) { atual.Add(campo.ToString()); campo.Clear(); }
                else if (ch == '\n' || ch == '\r')
                {
                    if (ch == '\r' && i + 1 < texto.Length && texto[i + 1] == '\n') i++;
                    atual.Add(campo.ToString()); campo.Clear();
                    linhas.Add(atual); atual = new List<string>();
                }
                else campo.Append(ch);
            }
            if (campo.Length > 0 || atual.Count > 0) { atual.Add(campo.ToString()); linhas.Add(atual); }
            return linhas;
        }

        /// <summary>Lê a primeira planilha de um .xlsx (zip com XML), sem depender do Excel.</summary>
        private static List<List<string>> LerXlsx(string caminho)
        {
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace pkg = "http://schemas.openxmlformats.org/package/2006/relationships";

            // Abre como somente leitura e compartilhado: a planilha pode estar aberta no Excel.
            using (var fs = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                XDocument Xml(string nome)
                {
                    ZipArchiveEntry e = zip.GetEntry(nome);
                    if (e == null) return null;
                    using (Stream s = e.Open()) return XDocument.Load(s);
                }

                var compartilhadas = new List<string>();
                XDocument ss = Xml("xl/sharedStrings.xml");
                if (ss != null)
                    foreach (XElement si in ss.Root.Elements(ns + "si"))
                        compartilhadas.Add(string.Concat(si.Descendants(ns + "t").Select(t => t.Value)));

                // Primeira aba do workbook → arquivo da planilha.
                XDocument wb = Xml("xl/workbook.xml");
                string rid = wb?.Root.Element(ns + "sheets")?.Elements(ns + "sheet").FirstOrDefault()?.Attribute(rel + "id")?.Value;
                string alvo = Xml("xl/_rels/workbook.xml.rels")?.Root.Elements(pkg + "Relationship")
                    .FirstOrDefault(r => r.Attribute("Id")?.Value == rid)?.Attribute("Target")?.Value ?? "worksheets/sheet1.xml";
                alvo = alvo.TrimStart('/');
                if (!alvo.StartsWith("xl/")) alvo = "xl/" + alvo;
                XDocument sheet = Xml(alvo) ?? throw new InvalidDataException("Planilha vazia ou formato não reconhecido.");

                var linhas = new List<List<string>>();
                foreach (XElement row in sheet.Descendants(ns + "row"))
                {
                    var l = new List<string>();
                    foreach (XElement c in row.Elements(ns + "c"))
                    {
                        int col = IndiceColuna(c.Attribute("r")?.Value) ?? l.Count;
                        while (l.Count < col) l.Add(null);
                        string t = c.Attribute("t")?.Value;
                        string v = c.Element(ns + "v")?.Value;
                        string valor = t == "s" && int.TryParse(v, out int idx) && idx < compartilhadas.Count ? compartilhadas[idx]
                            : t == "inlineStr" ? string.Concat(c.Descendants(ns + "t").Select(x => x.Value))
                            : v;
                        l.Add(valor);
                    }
                    linhas.Add(l);
                }
                return linhas;
            }
        }

        /// <summary>"C12" → 2.</summary>
        private static int? IndiceColuna(string referencia)
        {
            if (string.IsNullOrEmpty(referencia)) return null;
            int n = 0;
            foreach (char ch in referencia)
            {
                if (!char.IsLetter(ch)) break;
                n = n * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
            }
            return n == 0 ? (int?)null : n - 1;
        }
    }
}
