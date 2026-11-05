using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;

namespace MetroSP.Padroes
{
    /// <summary>Um tipo de padrão que o plugin guarda em arquivos (classificação, confronto, exportação IFC...).</summary>
    public class TipoPadrao
    {
        /// <summary>Identificador estável (nome da pasta dentro do pacote .zip).</summary>
        public string Id { get; set; }
        public string Nome { get; set; }
        public string Pasta { get; set; }
        public string Filtro { get; set; } = "*.json";
        /// <summary>Onde se cria e edita esse tipo (aparece na dica da janela).</summary>
        public string Ferramenta { get; set; }
        /// <summary>Abre o editor do padrão; null = só pela ferramenta.</summary>
        public Action<Window, ItemPadrao> Editar { get; set; }
    }

    public class ItemPadrao
    {
        public TipoPadrao Tipo { get; set; }
        public string Arquivo { get; set; }
        public string TipoNome => Tipo.Nome;
        public string Nome { get; set; }
        public DateTime Modificado { get; set; }
        public string ModificadoTexto => Modificado.ToString("dd/MM/yyyy HH:mm");
        public List<string> Planilhas { get; set; } = new List<string>();
        public string PlanilhasTexto => Planilhas.Count == 0 ? "" : string.Join("; ", Planilhas.Select(Path.GetFileName));
        public bool FaltaPlanilha => Planilhas.Any(p => !File.Exists(p));
        public string Situacao => Planilhas.Count == 0 ? "" : FaltaPlanilha ? "Planilha não encontrada" : "OK";
    }

    /// <summary>Uma planilha usada por um ou mais padrões.</summary>
    public class PlanilhaUsada
    {
        public string Caminho { get; set; }
        public string Nome => Path.GetFileName(Caminho);
        public string Pasta => Path.GetDirectoryName(Caminho);
        public bool Existe => File.Exists(Caminho);
        public string Situacao => Existe ? "OK" : "Não encontrada";
        public List<ItemPadrao> Padroes { get; set; } = new List<ItemPadrao>();
        public string PadroesTexto => string.Join("; ", Padroes.Select(p => p.Tipo.Nome + ": " + p.Nome));
        public string ModificadoTexto => Existe ? File.GetLastWriteTime(Caminho).ToString("dd/MM/yyyy HH:mm") : "";
    }

    /// <summary>
    /// Lista, exporta e importa os padrões salvos de todas as ferramentas, e as planilhas que eles usam.
    /// As planilhas são achadas nos .json pelo texto de qualquer campo que termine em .xlsx, .xlsm, .xls ou .csv.
    /// </summary>
    public static class GerenciadorPadroes
    {
        public static readonly string[] Extensoes = { ".xlsx", ".xlsm", ".xls", ".csv" };

        private static readonly JsonSerializerOptions Escrita = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private static readonly JsonDocumentOptions Leitura = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        };

        public static string PastaPlanilhasImportadas =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MetroSP", "Planilhas");

        public static List<ItemPadrao> Listar(IEnumerable<TipoPadrao> tipos)
        {
            var r = new List<ItemPadrao>();
            foreach (TipoPadrao t in tipos)
            {
                if (string.IsNullOrEmpty(t.Pasta) || !Directory.Exists(t.Pasta)) continue;
                foreach (string f in Directory.GetFiles(t.Pasta, t.Filtro).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
                {
                    var item = new ItemPadrao { Tipo = t, Arquivo = f, Nome = Path.GetFileNameWithoutExtension(f), Modificado = File.GetLastWriteTime(f) };
                    try
                    {
                        JsonNode raiz = LerJson(f);
                        if (raiz != null)
                        {
                            if (raiz is JsonObject o && o.TryGetPropertyValue("Nome", out JsonNode n) && n is JsonValue v &&
                                v.TryGetValue(out string nome) && !string.IsNullOrWhiteSpace(nome)) item.Nome = nome;
                            item.Planilhas = Textos(raiz).Where(EhPlanilha).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        }
                    }
                    catch { /* .txt ou .json estragado: aparece sem planilha */ }
                    r.Add(item);
                }
            }
            return r;
        }

        public static List<PlanilhaUsada> Planilhas(IEnumerable<ItemPadrao> itens)
        {
            var r = new Dictionary<string, PlanilhaUsada>(StringComparer.OrdinalIgnoreCase);
            foreach (ItemPadrao i in itens)
                foreach (string p in i.Planilhas)
                {
                    if (!r.TryGetValue(p, out PlanilhaUsada u)) r[p] = u = new PlanilhaUsada { Caminho = p };
                    u.Padroes.Add(i);
                }
            return r.Values.OrderBy(p => p.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public static bool EhPlanilha(string s) =>
            !string.IsNullOrWhiteSpace(s) && s.Length < 400 && s.IndexOfAny(Path.GetInvalidPathChars()) < 0 &&
            Extensoes.Any(e => s.Trim().EndsWith(e, StringComparison.OrdinalIgnoreCase)) &&
            (s.Contains('\\') || s.Contains('/'));

        /// <summary>Troca o caminho da planilha em todos os padrões que a usam. Devolve quantos arquivos mudaram.</summary>
        public static int TrocarPlanilha(IEnumerable<ItemPadrao> itens, string antiga, string nova)
        {
            int n = 0;
            foreach (ItemPadrao i in itens.Where(x => x.Planilhas.Any(p => MesmoCaminho(p, antiga))))
                if (Substituir(i.Arquivo, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [antiga] = nova })) n++;
            return n;
        }

        // ------------------------------------------------------------------ pacote .zip

        private const string Manifesto = "metrosp-padroes.json";

        private class ConteudoPacote
        {
            public string Criado { get; set; }
            public string Programa { get; set; }
            public List<ArquivoPacote> Padroes { get; set; } = new List<ArquivoPacote>();
            /// <summary>Caminho original da planilha → entrada no .zip.</summary>
            public Dictionary<string, string> Planilhas { get; set; } = new Dictionary<string, string>();
        }

        private class ArquivoPacote
        {
            public string Tipo { get; set; }
            public string Entrada { get; set; }
        }

        /// <summary>Grava os padrões (e, se pedido, as planilhas que existem) num .zip para a equipe.</summary>
        public static string ExportarPacote(IList<ItemPadrao> itens, string zip, bool comPlanilhas, string programa)
        {
            var c = new ConteudoPacote { Criado = DateTime.Now.ToString("s"), Programa = programa };
            int faltando = 0;
            if (File.Exists(zip)) File.Delete(zip);
            using (ZipArchive z = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                foreach (ItemPadrao i in itens)
                {
                    string entrada = i.Tipo.Id + "/" + Path.GetFileName(i.Arquivo);
                    z.CreateEntryFromFile(i.Arquivo, entrada);
                    c.Padroes.Add(new ArquivoPacote { Tipo = i.Tipo.Id, Entrada = entrada });
                    if (!comPlanilhas) continue;
                    foreach (string p in i.Planilhas)
                    {
                        if (c.Planilhas.ContainsKey(p)) continue;
                        if (!File.Exists(p)) { faltando++; continue; }
                        string nome = Path.GetFileName(p), e = "planilhas/" + nome;
                        for (int k = 2; c.Planilhas.ContainsValue(e); k++) e = $"planilhas/{Path.GetFileNameWithoutExtension(nome)} ({k}){Path.GetExtension(nome)}";
                        using (var origem = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (Stream destino = z.CreateEntry(e).Open())
                            origem.CopyTo(destino);
                        c.Planilhas[p] = e;
                    }
                }
                using (var w = new StreamWriter(z.CreateEntry(Manifesto).Open(), new UTF8Encoding(false)))
                    w.Write(JsonSerializer.Serialize(c, Escrita));
            }
            string r = $"{itens.Count} padrão(ões)" + (comPlanilhas ? $" e {c.Planilhas.Count} planilha(s)" : "") + " em " + Path.GetFileName(zip) + ".";
            if (faltando > 0) r += $" {faltando} planilha(s) não foram encontradas neste computador e ficaram de fora.";
            return r;
        }

        /// <summary>
        /// Copia os padrões de um pacote para as pastas de cada tipo. As planilhas do pacote vão para
        /// <see cref="PastaPlanilhasImportadas"/> e os padrões passam a apontar para elas.
        /// </summary>
        public static string ImportarPacote(string zip, IList<TipoPadrao> tipos, Func<string, bool> sobrescrever)
        {
            int copiados = 0, pulados = 0, planilhas = 0;
            var ignorados = new List<string>();
            using (ZipArchive z = ZipFile.OpenRead(zip))
            {
                ZipArchiveEntry m = z.GetEntry(Manifesto) ?? throw new InvalidDataException("O arquivo não é um pacote de padrões do MetroSP.");
                ConteudoPacote c;
                using (var r = new StreamReader(m.Open(), Encoding.UTF8))
                    c = JsonSerializer.Deserialize<ConteudoPacote>(r.ReadToEnd()) ?? new ConteudoPacote();

                var novos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in c.Planilhas)
                {
                    ZipArchiveEntry e = z.GetEntry(kv.Value);
                    if (e == null) continue;
                    Directory.CreateDirectory(PastaPlanilhasImportadas);
                    string destino = Path.Combine(PastaPlanilhasImportadas, e.Name);
                    e.ExtractToFile(destino, overwrite: true);
                    novos[kv.Key] = destino;
                    planilhas++;
                }

                foreach (ArquivoPacote a in c.Padroes)
                {
                    TipoPadrao t = tipos.FirstOrDefault(x => string.Equals(x.Id, a.Tipo, StringComparison.OrdinalIgnoreCase));
                    ZipArchiveEntry e = z.GetEntry(a.Entrada);
                    if (t == null || e == null) { ignorados.Add(a.Entrada); continue; }
                    Directory.CreateDirectory(t.Pasta);
                    string destino = Path.Combine(t.Pasta, e.Name);
                    if (File.Exists(destino) && !sobrescrever($"{t.Nome}: {Path.GetFileNameWithoutExtension(e.Name)}")) { pulados++; continue; }
                    e.ExtractToFile(destino, overwrite: true);
                    if (novos.Count > 0) Substituir(destino, novos);
                    copiados++;
                }
            }
            string s = $"{copiados} padrão(ões) importado(s)";
            if (planilhas > 0) s += $", {planilhas} planilha(s) copiada(s) para {PastaPlanilhasImportadas}";
            if (pulados > 0) s += $", {pulados} mantido(s) como estava(m)";
            if (ignorados.Count > 0) s += $". {ignorados.Count} de outro programa ficaram de fora: " + string.Join(", ", ignorados.Take(5));
            return s + ".";
        }

        // ------------------------------------------------------------------ json

        private static JsonNode LerJson(string arquivo)
        {
            if (!arquivo.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return null;
            return JsonNode.Parse(File.ReadAllText(arquivo, Encoding.UTF8), documentOptions: Leitura);
        }

        private static IEnumerable<string> Textos(JsonNode n)
        {
            switch (n)
            {
                case JsonObject o:
                    foreach (var kv in o) if (kv.Value != null) foreach (string s in Textos(kv.Value)) yield return s;
                    break;
                case JsonArray a:
                    foreach (JsonNode x in a) if (x != null) foreach (string s in Textos(x)) yield return s;
                    break;
                case JsonValue v:
                    if (v.TryGetValue(out string t)) yield return t;
                    break;
            }
        }

        /// <summary>Troca, no .json, todo texto igual a uma chave pelo valor. Devolve se mudou algo.</summary>
        private static bool Substituir(string arquivo, IDictionary<string, string> trocas)
        {
            JsonNode raiz = LerJson(arquivo);
            if (raiz == null) return false;
            bool mudou = false;
            void Visitar(JsonNode n)
            {
                if (n is JsonObject o)
                {
                    foreach (string k in o.Select(kv => kv.Key).ToList())
                    {
                        JsonNode x = o[k];
                        if (x is JsonValue v && v.TryGetValue(out string s) && Achar(trocas, s, out string novo)) { o[k] = novo; mudou = true; }
                        else if (x != null) Visitar(x);
                    }
                }
                else if (n is JsonArray a)
                {
                    for (int i = 0; i < a.Count; i++)
                    {
                        JsonNode x = a[i];
                        if (x is JsonValue v && v.TryGetValue(out string s) && Achar(trocas, s, out string novo)) { a[i] = novo; mudou = true; }
                        else if (x != null) Visitar(x);
                    }
                }
            }
            Visitar(raiz);
            if (mudou) File.WriteAllText(arquivo, raiz.ToJsonString(Escrita), new UTF8Encoding(false));
            return mudou;
        }

        private static bool Achar(IDictionary<string, string> trocas, string s, out string novo)
        {
            foreach (var kv in trocas)
                if (MesmoCaminho(kv.Key, s)) { novo = kv.Value; return true; }
            novo = null;
            return false;
        }

        public static bool MesmoCaminho(string a, string b)
        {
            if (a == null || b == null) return false;
            try { return string.Equals(Path.GetFullPath(a.Trim()), Path.GetFullPath(b.Trim()), StringComparison.OrdinalIgnoreCase); }
            catch { return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase); }
        }
    }
}
