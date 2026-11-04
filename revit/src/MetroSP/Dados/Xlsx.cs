using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using System.Xml.Linq;

namespace MetroSP.Dados
{
    /// <summary>
    /// Planilha .xlsx mínima, sem depender do Excel: grava abas com texto (cabeçalho em negrito, colunas travadas em
    /// cinza, filtro e primeira linha congelada) e lê todas as abas de volta como texto.
    /// </summary>
    public static class Xlsx
    {
        public class Aba
        {
            public string Nome { get; set; }
            public List<string> Cabecalho { get; set; } = new List<string>();
            public List<List<string>> Linhas { get; set; } = new List<List<string>>();
            /// <summary>Colunas que não devem ser editadas (ficam em cinza).</summary>
            public HashSet<int> Travadas { get; set; } = new HashSet<int>();
            /// <summary>Largura de cada coluna (caracteres); vazio = calculada.</summary>
            public Dictionary<int, double> Larguras { get; set; } = new Dictionary<int, double>();
            public bool Oculta { get; set; }
        }

        private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace Pkg = "http://schemas.openxmlformats.org/package/2006/relationships";

        public static void Gravar(string caminho, IList<Aba> abas)
        {
            string temp = caminho + ".tmp";
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                void Escrever(string nome, string texto)
                {
                    ZipArchiveEntry e = zip.CreateEntry(nome, CompressionLevel.Optimal);
                    using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(texto);
                }
                var tipos = new StringBuilder();
                tipos.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
                tipos.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>");
                tipos.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
                tipos.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
                for (int i = 0; i < abas.Count; i++)
                    tipos.Append($"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
                tipos.Append("</Types>");
                Escrever("[Content_Types].xml", tipos.ToString());
                Escrever("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");

                var wb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
                var rels = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
                for (int i = 0; i < abas.Count; i++)
                {
                    wb.Append($"<sheet name=\"{X(NomeAba(abas[i].Nome))}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"{(abas[i].Oculta ? " state=\"hidden\"" : "")}/>");
                    rels.Append($"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>");
                }
                wb.Append("</sheets>");
                // Filtro da primeira aba (nome definido que o Excel exige para o autofiltro).
                if (abas.Count > 0 && abas[0].Cabecalho.Count > 0)
                    wb.Append($"<definedNames><definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"0\" hidden=\"1\">'{X(NomeAba(abas[0].Nome))}'!$A$1:${Coluna(abas[0].Cabecalho.Count - 1)}${abas[0].Linhas.Count + 1}</definedName></definedNames>");
                wb.Append("</workbook>");
                rels.Append($"<Relationship Id=\"rId{abas.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
                Escrever("xl/workbook.xml", wb.ToString());
                Escrever("xl/_rels/workbook.xml.rels", rels.ToString());
                // Estilos: 0 normal, 1 cabeçalho (negrito, fundo laranja claro), 2 travada (fundo cinza), 3 cabeçalho travado.
                Escrever("xl/styles.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                    "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
                    "<fills count=\"5\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>" +
                    "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFFE6BF\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
                    "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFEDEDED\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
                    "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFD9D9D9\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
                    "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                    "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                    "<cellXfs count=\"4\"><xf numFmtId=\"49\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
                    "<xf numFmtId=\"49\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyNumberFormat=\"1\"/>" +
                    "<xf numFmtId=\"49\" fontId=\"0\" fillId=\"3\" borderId=\"0\" xfId=\"0\" applyFill=\"1\" applyNumberFormat=\"1\"/>" +
                    "<xf numFmtId=\"49\" fontId=\"1\" fillId=\"4\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyNumberFormat=\"1\"/></cellXfs>" +
                    "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>");

                for (int i = 0; i < abas.Count; i++) Escrever($"xl/worksheets/sheet{i + 1}.xml", Planilha(abas[i], i == 0));
            }
            if (File.Exists(caminho)) File.Delete(caminho);
            File.Move(temp, caminho);
        }

        private static string Planilha(Aba a, bool filtro)
        {
            var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            sb.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
            int colunas = Math.Max(a.Cabecalho.Count, a.Linhas.Count == 0 ? 0 : a.Linhas.Max(l => l.Count));
            if (colunas > 0)
            {
                sb.Append("<cols>");
                for (int c = 0; c < colunas; c++)
                {
                    double w = a.Larguras.TryGetValue(c, out double lw) ? lw
                        : Math.Min(60, Math.Max(10, Math.Max((c < a.Cabecalho.Count ? a.Cabecalho[c]?.Length ?? 0 : 0) + 3,
                            a.Linhas.Take(500).Select(l => c < l.Count ? l[c]?.Length ?? 0 : 0).DefaultIfEmpty(0).Max() + 2)));
                    sb.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{w.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
                }
                sb.Append("</cols>");
            }
            sb.Append("<sheetData>");
            void Linha(int r, IList<string> valores, bool cab)
            {
                sb.Append($"<row r=\"{r}\">");
                for (int c = 0; c < valores.Count; c++)
                {
                    string v = valores[c];
                    int estilo = cab ? (a.Travadas.Contains(c) ? 3 : 1) : (a.Travadas.Contains(c) ? 2 : 0);
                    if (string.IsNullOrEmpty(v)) { if (estilo != 0) sb.Append($"<c r=\"{Coluna(c)}{r}\" s=\"{estilo}\"/>"); continue; }
                    sb.Append($"<c r=\"{Coluna(c)}{r}\" t=\"inlineStr\" s=\"{estilo}\"><is><t xml:space=\"preserve\">{X(Limpo(v))}</t></is></c>");
                }
                sb.Append("</row>");
            }
            if (a.Cabecalho.Count > 0) Linha(1, a.Cabecalho, true);
            for (int i = 0; i < a.Linhas.Count; i++) Linha(i + 2, a.Linhas[i], false);
            sb.Append("</sheetData>");
            if (filtro && a.Cabecalho.Count > 0) sb.Append($"<autoFilter ref=\"A1:{Coluna(a.Cabecalho.Count - 1)}{a.Linhas.Count + 1}\"/>");
            sb.Append("</worksheet>");
            return sb.ToString();
        }

        /// <summary>Todas as abas: nome → linhas (a primeira linha é o cabeçalho).</summary>
        public static Dictionary<string, List<List<string>>> Ler(string caminho)
        {
            var r = new Dictionary<string, List<List<string>>>(StringComparer.OrdinalIgnoreCase);
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
                    foreach (XElement si in ss.Root.Elements(Ns + "si"))
                        compartilhadas.Add(string.Concat(si.Descendants(Ns + "t").Select(t => t.Value)));
                XDocument wb = Xml("xl/workbook.xml") ?? throw new InvalidDataException("O arquivo não é uma planilha .xlsx.");
                XDocument rels = Xml("xl/_rels/workbook.xml.rels");
                foreach (XElement sheet in wb.Root.Element(Ns + "sheets")?.Elements(Ns + "sheet") ?? Enumerable.Empty<XElement>())
                {
                    string rid = sheet.Attribute(Rel + "id")?.Value;
                    string alvo = rels?.Root.Elements(Pkg + "Relationship").FirstOrDefault(x => x.Attribute("Id")?.Value == rid)?.Attribute("Target")?.Value;
                    if (alvo == null) continue;
                    alvo = alvo.TrimStart('/');
                    if (!alvo.StartsWith("xl/")) alvo = "xl/" + alvo;
                    XDocument dados = Xml(alvo);
                    if (dados == null) continue;
                    var linhas = new List<List<string>>();
                    foreach (XElement row in dados.Descendants(Ns + "row"))
                    {
                        int indice = int.TryParse(row.Attribute("r")?.Value, out int ri) ? ri - 1 : linhas.Count;
                        while (linhas.Count < indice) linhas.Add(new List<string>());
                        var l = new List<string>();
                        foreach (XElement c in row.Elements(Ns + "c"))
                        {
                            int col = IndiceColuna(c.Attribute("r")?.Value) ?? l.Count;
                            while (l.Count < col) l.Add(null);
                            string t = c.Attribute("t")?.Value;
                            string v = c.Element(Ns + "v")?.Value;
                            l.Add(t == "s" && int.TryParse(v, out int idx) && idx < compartilhadas.Count ? compartilhadas[idx]
                                : t == "inlineStr" ? string.Concat(c.Descendants(Ns + "t").Select(x => x.Value))
                                : (t == null || t == "n") && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double num)
                                    ? num.ToString("0.###############", System.Globalization.CultureInfo.CurrentCulture) // número digitado no Excel
                                : v);
                        }
                        linhas.Add(l);
                    }
                    r[sheet.Attribute("name")?.Value ?? ("Aba" + (r.Count + 1))] = linhas;
                }
            }
            return r;
        }

        public static string Coluna(int i)
        {
            string s = "";
            for (i++; i > 0; i = (i - 1) / 26) s = (char)('A' + (i - 1) % 26) + s;
            return s;
        }

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

        private static string NomeAba(string nome)
        {
            string n = new string((nome ?? "Planilha").Where(ch => "[]:*?/\\".IndexOf(ch) < 0).ToArray()).Trim();
            if (n.Length == 0) n = "Planilha";
            return n.Length > 31 ? n.Substring(0, 31) : n;
        }

        // Caracteres de controle não são aceitos no XML da planilha.
        private static string Limpo(string s) => new string(s.Where(ch => ch == '\t' || ch == '\n' || ch == '\r' || ch >= ' ').ToArray());

        private static string X(string s) => SecurityElement.Escape(s) ?? "";
    }
}
