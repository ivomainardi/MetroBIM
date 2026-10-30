using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MetroSP.Ids
{
    /// <summary>
    /// Lê as configurações de IFC direto do Autodesk Forma (Autodesk Docs) pela internet, sem o Desktop Connector.
    ///
    /// Usa o app do MetroSP na Autodesk (Client ID em IFC\forma-online.ini ou em %AppData%\MetroSP\forma-online.ini),
    /// com login PKCE: o navegador abre uma vez, o usuário entra com a conta Autodesk e o token fica guardado
    /// criptografado para o usuário do Windows. A pasta escolhida no Forma é copiada para um cache local e, a cada
    /// abertura do Exportar IFC, só os arquivos com versão nova são baixados de novo.
    /// </summary>
    public static class FormaOnline
    {
        private const string Base = "https://developer.api.autodesk.com";
        public const int Porta = 47312;
        public static string Callback => $"http://localhost:{Porta}/metrosp/";
        private const string Escopo = "data:read";

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        private static string PastaApp =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MetroSP");
        private static string ArquivoToken => Path.Combine(PastaApp, "forma-online.token");
        private static string ArquivoPasta => Path.Combine(PastaApp, "forma-online-pasta.ini");

        /// <summary>Cópia local da pasta do Forma (mesma estrutura: Setups, PropertySets, Mapeamentos).</summary>
        public static string PastaCache =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MetroSP", "FormaOnline");

        // ---------- configuração ----------

        /// <summary>Client ID do app na Autodesk: o do usuário vale sobre o instalado. null = não configurado.</summary>
        public static string ClientId =>
            LerIni(Path.Combine(PastaApp, "forma-online.ini"), "client_id") ??
            LerIni(Path.Combine(ClassesIfc.PastaPadrao, "forma-online.ini"), "client_id");

        public static bool Configurado => !string.IsNullOrWhiteSpace(ClientId);

        private static string ArquivoClientId => Path.Combine(PastaApp, "forma-online.ini");

        /// <summary>Client ID informado na janela do plugin (vale para este usuário neste computador).</summary>
        public static string ClientIdDoUsuario => LerIni(ArquivoClientId, "client_id");

        /// <summary>Client IDs da Autodesk têm só letras e números (hoje, 32 caracteres).</summary>
        public static bool ClientIdValido(string id) =>
            !string.IsNullOrWhiteSpace(id) && System.Text.RegularExpressions.Regex.IsMatch(id.Trim(), "^[A-Za-z0-9]{16,64}$");

        /// <summary>Grava o Client ID do usuário; vazio apaga e volta ao instalado (se houver). Trocar o app desfaz o login.</summary>
        public static void GravarClientId(string id)
        {
            id = id?.Trim() ?? "";
            if (id == (ClientId ?? "")) return;
            Directory.CreateDirectory(PastaApp);
            if (id.Length == 0) { if (File.Exists(ArquivoClientId)) File.Delete(ArquivoClientId); }
            else File.WriteAllLines(ArquivoClientId, new[] { "# Client ID do app do MetroSP na Autodesk, informado no plugin.", "client_id=" + id });
            _token = null;
            try { File.Delete(ArquivoToken); } catch { }
        }
        public static bool Conectado => Configurado && File.Exists(ArquivoToken);

        /// <summary>Pasta do Forma escolhida para as configurações.</summary>
        public class Pasta
        {
            public string ProjetoId { get; set; }
            public string PastaId { get; set; }
            /// <summary>Caminho para mostrar: "Hub › Projeto › Pasta".</summary>
            public string Descricao { get; set; }
        }

        public static Pasta PastaEscolhida()
        {
            string p = LerIni(ArquivoPasta, "projeto"), f = LerIni(ArquivoPasta, "pasta");
            if (string.IsNullOrEmpty(p) || string.IsNullOrEmpty(f)) return null;
            return new Pasta { ProjetoId = p, PastaId = f, Descricao = LerIni(ArquivoPasta, "descricao") ?? "" };
        }

        public static void EscolherPasta(Pasta p)
        {
            Directory.CreateDirectory(PastaApp);
            if (p == null)
            {
                File.Delete(ArquivoPasta);
                LimparCache();
                return;
            }
            File.WriteAllLines(ArquivoPasta, new[] { "projeto=" + p.ProjetoId, "pasta=" + p.PastaId, "descricao=" + p.Descricao });
            LimparCache();
        }

        public static void Sair()
        {
            try { File.Delete(ArquivoToken); } catch { }
            EscolherPasta(null);
        }

        private static void LimparCache()
        {
            try { if (Directory.Exists(PastaCache)) Directory.Delete(PastaCache, true); } catch { /* arquivo em uso */ }
        }

        private static string LerIni(string arquivo, string chave)
        {
            try
            {
                if (!File.Exists(arquivo)) return null;
                foreach (string l in File.ReadAllLines(arquivo))
                {
                    int i = l.IndexOf('=');
                    if (i > 0 && string.Equals(l.Substring(0, i).Trim(), chave, StringComparison.OrdinalIgnoreCase))
                    {
                        string v = l.Substring(i + 1).Trim();
                        return v.Length > 0 ? v : null;
                    }
                }
            }
            catch { }
            return null;
        }

        // ---------- login ----------

        private class Token
        {
            public string Acesso;
            public string Renovacao;
            public DateTime Expira;
        }

        private static Token _token;

        /// <summary>
        /// Abre o navegador para o usuário entrar com a conta Autodesk e espera a volta em localhost.
        /// Bloqueia até concluir, cancelar ou passar o tempo.
        /// </summary>
        public static void Entrar(Func<bool> cancelado)
        {
            if (!Configurado) throw new InvalidOperationException("O Client ID do app do MetroSP na Autodesk não está configurado (forma-online.ini).");

            string verificador = Base64Url(RandomNumberGenerator.GetBytes(32));
            string desafio = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verificador)));
            string estado = Base64Url(RandomNumberGenerator.GetBytes(16));

            string url = $"{Base}/authentication/v2/authorize?response_type=code&client_id={Uri.EscapeDataString(ClientId)}" +
                         $"&redirect_uri={Uri.EscapeDataString(Callback)}&scope={Uri.EscapeDataString(Escopo)}" +
                         $"&code_challenge={desafio}&code_challenge_method=S256&state={estado}";

            string codigo;
            using (var ouvinte = new HttpListener())
            {
                ouvinte.Prefixes.Add(Callback);
                ouvinte.Start();
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

                Task<HttpListenerContext> espera = ouvinte.GetContextAsync();
                var limite = Stopwatch.StartNew();
                while (!espera.Wait(200))
                {
                    if (cancelado?.Invoke() == true) throw new OperationCanceledException("Login cancelado.");
                    if (limite.Elapsed > TimeSpan.FromMinutes(5)) throw new TimeoutException("O login no navegador não foi concluído em 5 minutos.");
                }
                HttpListenerContext ctx = espera.Result;
                var q = System.Web.HttpUtility.ParseQueryString(ctx.Request.Url.Query);
                codigo = q["code"];
                string erro = q["error_description"] ?? q["error"];
                bool ok = codigo != null && q["state"] == estado;
                Responder(ctx, ok ? "Login concluído. Pode fechar esta aba e voltar ao Revit."
                                  : "Não foi possível entrar" + (erro != null ? ": " + erro : "."));
                if (!ok) throw new InvalidOperationException("A Autodesk não concluiu o login" + (erro != null ? ": " + erro : "."));
            }

            Pedir(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = codigo,
                ["client_id"] = ClientId,
                ["code_verifier"] = verificador,
                ["redirect_uri"] = Callback,
            });
        }

        private static void Responder(HttpListenerContext ctx, string texto)
        {
            try
            {
                byte[] b = Encoding.UTF8.GetBytes($"<html><head><meta charset=\"utf-8\"><title>MetroSP</title></head>" +
                    $"<body style=\"font-family:Segoe UI,sans-serif;padding:40px\"><h2>MetroSP</h2><p>{WebUtility.HtmlEncode(texto)}</p></body></html>");
                ctx.Response.ContentType = "text/html; charset=utf-8";
                ctx.Response.OutputStream.Write(b, 0, b.Length);
                ctx.Response.Close();
            }
            catch { /* navegador já fechou */ }
        }

        private static void Pedir(Dictionary<string, string> campos)
        {
            using (var r = Http.PostAsync($"{Base}/authentication/v2/token", new FormUrlEncodedContent(campos)).GetAwaiter().GetResult())
            {
                string corpo = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!r.IsSuccessStatusCode) throw new InvalidOperationException($"A Autodesk recusou o login ({(int)r.StatusCode}): {corpo}");
                using (JsonDocument j = JsonDocument.Parse(corpo))
                {
                    JsonElement e = j.RootElement;
                    _token = new Token
                    {
                        Acesso = e.GetProperty("access_token").GetString(),
                        Renovacao = e.TryGetProperty("refresh_token", out JsonElement rt) ? rt.GetString() : _token?.Renovacao,
                        Expira = DateTime.UtcNow.AddSeconds(e.TryGetProperty("expires_in", out JsonElement ex) ? ex.GetInt32() - 60 : 3000),
                    };
                }
            }
            Directory.CreateDirectory(PastaApp);
            File.WriteAllBytes(ArquivoToken, Dpapi.Proteger(Encoding.UTF8.GetBytes(_token.Renovacao ?? "")));
        }

        /// <summary>Token válido, renovando com o guardado. Lança se for preciso entrar de novo.</summary>
        private static string Acesso()
        {
            if (_token != null && _token.Expira > DateTime.UtcNow) return _token.Acesso;
            string renovacao = _token?.Renovacao;
            if (renovacao == null && File.Exists(ArquivoToken))
            {
                try { renovacao = Encoding.UTF8.GetString(Dpapi.Abrir(File.ReadAllBytes(ArquivoToken))); }
                catch { renovacao = null; }
            }
            if (string.IsNullOrEmpty(renovacao)) throw new PrecisaEntrarException();
            try
            {
                Pedir(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = renovacao,
                    ["client_id"] = ClientId,
                    ["scope"] = Escopo,
                });
            }
            catch (InvalidOperationException)
            {
                // Token vencido ou revogado: apaga e pede o login de novo.
                try { File.Delete(ArquivoToken); } catch { }
                _token = null;
                throw new PrecisaEntrarException();
            }
            return _token.Acesso;
        }

        public class PrecisaEntrarException : Exception
        {
            public PrecisaEntrarException() : base("Entre com a conta Autodesk para ler o Forma online.") { }
        }

        // ---------- Data Management ----------

        /// <summary>Hub, projeto, pasta ou arquivo do Forma.</summary>
        public class Item
        {
            public string Id { get; set; }
            public string Nome { get; set; }
            public string Tipo { get; set; }
            public string HubId { get; set; }
            public string ProjetoId { get; set; }
            public override string ToString() => Nome;
        }

        private static JsonDocument Get(string caminho)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, caminho.StartsWith("http") ? caminho : Base + caminho))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Acesso());
                using (HttpResponseMessage r = Http.SendAsync(req).GetAwaiter().GetResult())
                {
                    string corpo = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    if (r.StatusCode == HttpStatusCode.Unauthorized) { _token = null; throw new PrecisaEntrarException(); }
                    if (!r.IsSuccessStatusCode)
                        throw new InvalidOperationException($"Forma respondeu {(int)r.StatusCode} em {caminho}: {Resumo(corpo)}");
                    return JsonDocument.Parse(corpo);
                }
            }
        }

        private static string Resumo(string s) => s == null ? "" : s.Length > 300 ? s.Substring(0, 300) + "..." : s;

        /// <summary>Todas as páginas de uma lista do Data Management.</summary>
        private static IEnumerable<JsonElement> Lista(string caminho)
        {
            string proximo = caminho;
            while (proximo != null)
            {
                using (JsonDocument j = Get(proximo))
                {
                    foreach (JsonElement e in j.RootElement.GetProperty("data").EnumerateArray()) yield return e.Clone();
                    proximo = j.RootElement.TryGetProperty("links", out JsonElement l) && l.TryGetProperty("next", out JsonElement n) &&
                              n.ValueKind == JsonValueKind.Object ? n.GetProperty("href").GetString() : null;
                }
            }
        }

        private static string Nome(JsonElement e)
        {
            JsonElement a = e.GetProperty("attributes");
            if (a.TryGetProperty("displayName", out JsonElement d) && d.ValueKind == JsonValueKind.String) return d.GetString();
            return a.TryGetProperty("name", out JsonElement n) ? n.GetString() : e.GetProperty("id").GetString();
        }

        public static List<Item> Hubs() =>
            Lista("/project/v1/hubs").Select(e => new Item { Id = e.GetProperty("id").GetString(), Nome = Nome(e), Tipo = "hub" })
                .OrderBy(i => i.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();

        public static List<Item> Projetos(Item hub) =>
            Lista($"/project/v1/hubs/{hub.Id}/projects").Select(e => new Item { Id = e.GetProperty("id").GetString(), Nome = Nome(e), Tipo = "projeto", HubId = hub.Id, ProjetoId = e.GetProperty("id").GetString() })
                .OrderBy(i => i.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();

        public static List<Item> PastasRaiz(Item projeto) =>
            Lista($"/project/v1/hubs/{projeto.HubId}/projects/{projeto.Id}/topFolders").Select(e => new Item { Id = e.GetProperty("id").GetString(), Nome = Nome(e), Tipo = "pasta", HubId = projeto.HubId, ProjetoId = projeto.Id })
                .OrderBy(i => i.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();

        /// <summary>Subpastas e arquivos de uma pasta.</summary>
        public static List<Item> Conteudo(string projetoId, string pastaId) =>
            Lista($"/data/v1/projects/{projetoId}/folders/{Uri.EscapeDataString(pastaId)}/contents")
                .Select(e => new Item
                {
                    Id = e.GetProperty("id").GetString(),
                    Nome = Nome(e),
                    Tipo = e.GetProperty("type").GetString() == "folders" ? "pasta" : "arquivo",
                    ProjetoId = projetoId,
                })
                .OrderBy(i => i.Tipo != "pasta").ThenBy(i => i.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();

        // ---------- sincronização ----------

        public class ResultadoSync
        {
            public int Baixados, Atuais, Removidos;
            public string Erro;
            public bool PrecisaEntrar;
            public override string ToString() =>
                Erro ?? $"{Baixados} arquivo(s) atualizado(s), {Atuais} já em dia" + (Removidos > 0 ? $", {Removidos} removido(s)" : "") + ".";
        }

        /// <summary>
        /// Copia para o cache os .json/.txt da pasta escolhida: das subpastas Setups, PropertySets e Mapeamentos
        /// ou, se não houver subpastas, os .json como setups e os .txt como property sets (Nomenclatura.txt fica na raiz).
        /// Só baixa o que tem versão nova. Erros de rede não apagam o cache: a última cópia continua valendo.
        /// </summary>
        public static ResultadoSync Sincronizar()
        {
            var r = new ResultadoSync();
            Pasta pasta = PastaEscolhida();
            if (pasta == null || !Configurado) return r;
            try
            {
                Dictionary<string, string> versoes = LerVersoes();
                var novas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                List<Item> raiz = Conteudo(pasta.ProjetoId, pasta.PastaId);
                var subpastas = new[] { FormaPaths.SubSetups, FormaPaths.SubPropertySets, FormaPaths.SubMapeamentos };
                bool estruturada = raiz.Any(i => i.Tipo == "pasta" && subpastas.Contains(i.Nome, StringComparer.OrdinalIgnoreCase));

                var arquivos = new List<(Item Item, string Destino)>();
                foreach (Item i in raiz.Where(i => i.Tipo == "arquivo"))
                {
                    string ext = Path.GetExtension(i.Nome).ToLowerInvariant();
                    if (string.Equals(i.Nome, ClassesIfc.ArquivoNomenclatura, StringComparison.OrdinalIgnoreCase)) arquivos.Add((i, i.Nome));
                    else if (!estruturada && ext == ".json") arquivos.Add((i, Path.Combine(FormaPaths.SubSetups, i.Nome)));
                    else if (!estruturada && ext == ".txt") arquivos.Add((i, Path.Combine(FormaPaths.SubPropertySets, i.Nome)));
                }
                if (estruturada)
                    foreach (Item sub in raiz.Where(i => i.Tipo == "pasta" && subpastas.Contains(i.Nome, StringComparer.OrdinalIgnoreCase)))
                    {
                        string nomeSub = subpastas.First(s => string.Equals(s, sub.Nome, StringComparison.OrdinalIgnoreCase));
                        string ext = nomeSub == FormaPaths.SubSetups ? ".json" : ".txt";
                        foreach (Item i in Conteudo(pasta.ProjetoId, sub.Id).Where(i => i.Tipo == "arquivo" && i.Nome.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                            arquivos.Add((i, Path.Combine(nomeSub, i.Nome)));
                    }

                foreach (var (item, destino) in arquivos)
                {
                    (string versao, string storage) = UltimaVersao(pasta.ProjetoId, item.Id);
                    string local = Path.Combine(PastaCache, destino);
                    novas[destino] = versao;
                    if (versoes.TryGetValue(destino, out string tinha) && tinha == versao && File.Exists(local)) { r.Atuais++; continue; }
                    Baixar(storage, local);
                    r.Baixados++;
                }
                // Arquivos apagados no Forma saem do cache.
                foreach (string antigo in versoes.Keys.Where(k => !novas.ContainsKey(k)))
                {
                    try { File.Delete(Path.Combine(PastaCache, antigo)); r.Removidos++; } catch { }
                }
                GravarVersoes(novas);
            }
            catch (PrecisaEntrarException ex) { r.Erro = ex.Message; r.PrecisaEntrar = true; }
            catch (Exception ex) { r.Erro = "Não foi possível atualizar do Forma online (usando a última cópia): " + ex.Message; }
            return r;
        }

        /// <summary>Id da versão mais recente e o objeto de armazenamento dela.</summary>
        private static (string Versao, string Storage) UltimaVersao(string projetoId, string itemId)
        {
            using (JsonDocument j = Get($"/data/v1/projects/{projetoId}/items/{Uri.EscapeDataString(itemId)}/tip"))
            {
                JsonElement d = j.RootElement.GetProperty("data");
                string versao = d.GetProperty("id").GetString();
                string storage = d.GetProperty("relationships").GetProperty("storage").GetProperty("data").GetProperty("id").GetString();
                return (versao, storage);
            }
        }

        /// <summary>Baixa um objeto "urn:adsk.objects:os.object:bucket/chave" pela URL assinada do S3.</summary>
        private static void Baixar(string storage, string destino)
        {
            string resto = storage.Substring(storage.LastIndexOf(':') + 1);
            int barra = resto.IndexOf('/');
            string bucket = resto.Substring(0, barra), chave = resto.Substring(barra + 1);
            string url;
            using (JsonDocument j = Get($"/oss/v2/buckets/{bucket}/objects/{Uri.EscapeDataString(chave)}/signeds3download"))
                url = j.RootElement.GetProperty("url").GetString();
            byte[] dados = Http.GetByteArrayAsync(url).GetAwaiter().GetResult();
            Directory.CreateDirectory(Path.GetDirectoryName(destino));
            string temp = destino + ".baixando";
            File.WriteAllBytes(temp, dados);
            File.Copy(temp, destino, overwrite: true);
            File.Delete(temp);
        }

        private static string ArquivoVersoes => Path.Combine(PastaCache, "versoes.txt");

        private static Dictionary<string, string> LerVersoes()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(ArquivoVersoes))
                    foreach (string l in File.ReadAllLines(ArquivoVersoes))
                    {
                        string[] p = l.Split('\t');
                        if (p.Length == 2) d[p[0]] = p[1];
                    }
            }
            catch { }
            return d;
        }

        private static void GravarVersoes(Dictionary<string, string> d)
        {
            Directory.CreateDirectory(PastaCache);
            File.WriteAllLines(ArquivoVersoes, d.Select(kv => kv.Key + "\t" + kv.Value));
        }

        private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        /// <summary>Criptografia do Windows ligada ao usuário (DPAPI), sem pacote extra.</summary>
        private static class Dpapi
        {
            [StructLayout(LayoutKind.Sequential)]
            private struct Blob { public int Tamanho; public IntPtr Dados; }

            [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            private static extern bool CryptProtectData(ref Blob entrada, string descricao, IntPtr entropia, IntPtr reservado, IntPtr prompt, int flags, ref Blob saida);

            [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            private static extern bool CryptUnprotectData(ref Blob entrada, IntPtr descricao, IntPtr entropia, IntPtr reservado, IntPtr prompt, int flags, ref Blob saida);

            [DllImport("kernel32.dll")]
            private static extern IntPtr LocalFree(IntPtr p);

            public static byte[] Proteger(byte[] dados) => Chamar(dados, proteger: true);
            public static byte[] Abrir(byte[] dados) => Chamar(dados, proteger: false);

            private static byte[] Chamar(byte[] dados, bool proteger)
            {
                var entrada = new Blob { Tamanho = dados.Length, Dados = Marshal.AllocHGlobal(Math.Max(dados.Length, 1)) };
                var saida = new Blob();
                try
                {
                    Marshal.Copy(dados, 0, entrada.Dados, dados.Length);
                    bool ok = proteger
                        ? CryptProtectData(ref entrada, "MetroSP", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, ref saida)
                        : CryptUnprotectData(ref entrada, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, ref saida);
                    if (!ok) throw new CryptographicException(Marshal.GetLastWin32Error());
                    var r = new byte[saida.Tamanho];
                    Marshal.Copy(saida.Dados, r, 0, saida.Tamanho);
                    return r;
                }
                finally
                {
                    Marshal.FreeHGlobal(entrada.Dados);
                    if (saida.Dados != IntPtr.Zero) LocalFree(saida.Dados);
                }
            }
        }
    }
}
