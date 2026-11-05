using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace MetroSP.Planilha
{
    /// <summary>Planilha lida como texto: abas na ordem do arquivo, cada uma com suas linhas e células.</summary>
    public class Tabela
    {
        public string Arquivo { get; set; }
        public List<string> NomesAbas { get; } = new List<string>();
        public Dictionary<string, List<List<string>>> Abas { get; } = new Dictionary<string, List<List<string>>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Linhas da aba (null ou inexistente = primeira aba).</summary>
        public List<List<string>> Linhas(string aba)
        {
            if (!string.IsNullOrEmpty(aba) && Abas.TryGetValue(aba, out var l)) return l;
            return NomesAbas.Count > 0 ? Abas[NomesAbas[0]] : new List<List<string>>();
        }
    }

    /// <summary>
    /// Lê .xlsx (todas as abas, sem depender do Excel, mesmo com o arquivo aberto no Excel) e .csv/.txt
    /// (separador ; , ou tabulação detectado na primeira linha; UTF-8 ou Latin-1). Igual no Revit e no Civil 3D.
    /// </summary>
    public static class LeitorPlanilha
    {
        public static string Filtro => "Planilhas (*.xlsx;*.csv;*.txt)|*.xlsx;*.csv;*.txt|Todos os arquivos (*.*)|*.*";

        public static Tabela Ler(string caminho)
        {
            if (string.IsNullOrEmpty(caminho) || !File.Exists(caminho)) throw new FileNotFoundException("Planilha não encontrada.", caminho);
            var t = new Tabela { Arquivo = caminho };
            string ext = Path.GetExtension(caminho).ToLowerInvariant();
            if (ext == ".xlsx" || ext == ".xlsm") LerXlsx(caminho, t);
            else
            {
                t.NomesAbas.Add(Path.GetFileNameWithoutExtension(caminho));
                t.Abas[t.NomesAbas[0]] = LerCsv(caminho);
            }
            return t;
        }

        // ------------------------------------------------------------------ csv

        public static List<List<string>> LerCsv(string caminho)
        {
            byte[] bytes;
            using (var fs = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var ms = new MemoryStream()) { fs.CopyTo(ms); bytes = ms.ToArray(); }
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

        // ------------------------------------------------------------------ xlsx

        private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace Pkg = "http://schemas.openxmlformats.org/package/2006/relationships";

        private static void LerXlsx(string caminho, Tabela t)
        {
            // Somente leitura e compartilhado: a planilha pode estar aberta no Excel.
            using (var fs = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                XDocument Xml(string nome)
                {
                    ZipArchiveEntry e = zip.GetEntry(nome) ?? zip.Entries.FirstOrDefault(x => string.Equals(x.FullName, nome, StringComparison.OrdinalIgnoreCase));
                    if (e == null) return null;
                    using (Stream s = e.Open()) return XDocument.Load(s);
                }

                var compartilhadas = new List<string>();
                XDocument ss = Xml("xl/sharedStrings.xml");
                if (ss != null)
                    foreach (XElement si in ss.Root.Elements(Ns + "si"))
                        compartilhadas.Add(string.Concat(si.Descendants(Ns + "t").Where(x => x.Parent?.Name != Ns + "rPh").Select(x => x.Value)));

                XDocument wb = Xml("xl/workbook.xml") ?? throw new InvalidDataException("Arquivo .xlsx sem workbook.");
                var rels = Xml("xl/_rels/workbook.xml.rels")?.Root.Elements(Pkg + "Relationship")
                    .ToDictionary(r => r.Attribute("Id")?.Value ?? "", r => r.Attribute("Target")?.Value ?? "") ?? new Dictionary<string, string>();
                int n = 0;
                foreach (XElement sheet in wb.Root.Element(Ns + "sheets")?.Elements(Ns + "sheet") ?? Enumerable.Empty<XElement>())
                {
                    n++;
                    string nome = sheet.Attribute("name")?.Value ?? "Planilha" + n;
                    string rid = sheet.Attribute(Rel + "id")?.Value ?? "";
                    string alvo = rels.TryGetValue(rid, out string a) ? a : $"worksheets/sheet{n}.xml";
                    alvo = alvo.StartsWith("/") ? alvo.TrimStart('/') : "xl/" + alvo;
                    XDocument ws = Xml(alvo);
                    if (ws == null || t.Abas.ContainsKey(nome)) continue;
                    t.NomesAbas.Add(nome);
                    t.Abas[nome] = LerAba(ws, compartilhadas);
                }
                if (t.NomesAbas.Count == 0) throw new InvalidDataException("Planilha vazia ou formato não reconhecido.");
            }
        }

        private static List<List<string>> LerAba(XDocument ws, List<string> compartilhadas)
        {
            var linhas = new List<List<string>>();
            foreach (XElement row in ws.Descendants(Ns + "row"))
            {
                // Linhas vazias no meio (sem <row>) viram linhas vazias, para o número da linha bater com o Excel.
                if (int.TryParse(row.Attribute("r")?.Value, out int r))
                    while (linhas.Count < r - 1) linhas.Add(new List<string>());
                var l = new List<string>();
                foreach (XElement c in row.Elements(Ns + "c"))
                {
                    int col = IndiceReferencia(c.Attribute("r")?.Value) ?? l.Count;
                    while (l.Count < col) l.Add(null);
                    string tipo = c.Attribute("t")?.Value;
                    string v = c.Element(Ns + "v")?.Value;
                    string valor = tipo == "s" && int.TryParse(v, out int idx) && idx < compartilhadas.Count ? compartilhadas[idx]
                        : tipo == "inlineStr" ? string.Concat(c.Descendants(Ns + "t").Select(x => x.Value))
                        : tipo == "b" ? (v == "1" ? "Sim" : "Não")
                        : tipo == null || tipo == "n" ? Numero(v)
                        : v;
                    l.Add(valor);
                }
                linhas.Add(l);
            }
            return linhas;
        }

        /// <summary>Número como o Excel mostraria sem formato: "12" e não "12.0", "0,1" e não "0.1000000000001".</summary>
        private static string Numero(string v)
        {
            if (string.IsNullOrEmpty(v) || !double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return v;
            if (Math.Abs(d - Math.Round(d)) < 1e-9 && Math.Abs(d) < 1e15) return ((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture);
            return d.ToString("0.##########", CultureInfo.InvariantCulture);
        }

        /// <summary>"C12" → 2.</summary>
        private static int? IndiceReferencia(string referencia)
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
