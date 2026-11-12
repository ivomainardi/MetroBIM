using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace MetroSP
{
    /// <summary>Um arquivo encontrado na pasta do Forma.</summary>
    public class RemoteFile
    {
        public string Name;
        public string ItemId;
        public string VersionId;
        public int VersionNumber;
        public string LastModified;
        public string LastModifiedBy;
        public string StorageUrn;   // urn:adsk.objects:os.object:{bucket}/{objectKey}
    }

    /// <summary>
    /// Acesso aos arquivos do Autodesk Forma (Autodesk Docs) pela
    /// APS Authentication v2 (3-legged com PKCE) + Data Management API.
    /// </summary>
    public class FormaClient
    {
        const string Base = "https://developer.api.autodesk.com";
        const string Scope = "data:read";

        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        static readonly string TokenFile = Path.Combine(Settings.UserDir, "token.bin");

        readonly Settings _cfg;
        readonly Action<string> _log;
        string _accessToken;
        DateTime _expiresUtc;

        public FormaClient(Settings cfg, Action<string> log)
        {
            _cfg = cfg;
            _log = log ?? (_ => { });
        }

        // ------------------------------------------------------------------ autenticação

        public async Task EnsureLoginAsync(CancellationToken ct)
        {
            if (_accessToken != null && DateTime.UtcNow < _expiresUtc) return;

            var refresh = LoadRefreshToken();
            if (refresh != null)
            {
                try
                {
                    await RequestTokenAsync(new Dictionary<string, string>
                    {
                        ["grant_type"] = "refresh_token",
                        ["refresh_token"] = refresh,
                        ["client_id"] = _cfg.ClientId,
                        ["scope"] = Scope
                    }, ct);
                    return;
                }
                catch (Exception ex)
                {
                    _log("Sessão expirada, será necessário entrar de novo (" + ex.Message + ").");
                }
            }
            await InteractiveLoginAsync(ct);
        }

        public static void Logout()
        {
            if (File.Exists(TokenFile)) File.Delete(TokenFile);
        }

        async Task InteractiveLoginAsync(CancellationToken ct)
        {
            var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
            var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            var state = Base64Url(RandomNumberGenerator.GetBytes(16));

            var redirect = new Uri(_cfg.CallbackUrl);
            var prefix = $"{redirect.Scheme}://{redirect.Authority}{redirect.AbsolutePath.TrimEnd('/')}/";

            using var listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            listener.Start();

            var url = Base + "/authentication/v2/authorize" +
                      "?response_type=code" +
                      "&client_id=" + Uri.EscapeDataString(_cfg.ClientId) +
                      "&redirect_uri=" + Uri.EscapeDataString(_cfg.CallbackUrl) +
                      "&scope=" + Uri.EscapeDataString(Scope) +
                      "&state=" + state +
                      "&code_challenge=" + challenge +
                      "&code_challenge_method=S256";

            _log("Abrindo o navegador para login na Autodesk...");
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

            var ctxTask = listener.GetContextAsync();
            var done = await Task.WhenAny(ctxTask, Task.Delay(TimeSpan.FromMinutes(3), ct));
            if (done != ctxTask) throw new TimeoutException("Login não concluído em 3 minutos.");

            var ctx = ctxTask.Result;
            var query = System.Web.HttpUtility.ParseQueryString(ctx.Request.Url.Query);
            var ok = query["code"] != null && query["state"] == state;

            var html = ok
                ? "<html><body style='font-family:sans-serif'><h2>MetroSP: login concluído.</h2>Pode fechar esta aba e voltar ao Civil 3D.</body></html>"
                : "<html><body style='font-family:sans-serif'><h2>MetroSP: falha no login.</h2>" + WebUtility.HtmlEncode(query["error_description"] ?? query["error"] ?? "") + "</body></html>";
            var bytes = Encoding.UTF8.GetBytes(html);
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length, ct);
            ctx.Response.Close();

            if (!ok) throw new InvalidOperationException("Login recusado: " + (query["error_description"] ?? query["error"] ?? "state inválido"));

            await RequestTokenAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = query["code"],
                ["client_id"] = _cfg.ClientId,
                ["code_verifier"] = verifier,
                ["redirect_uri"] = _cfg.CallbackUrl
            }, ct);
        }

        async Task RequestTokenAsync(Dictionary<string, string> form, CancellationToken ct)
        {
            using var resp = await Http.PostAsync(Base + "/authentication/v2/token", new FormUrlEncodedContent(form), ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"Token {(int)resp.StatusCode}: {body}");

            var json = JsonNode.Parse(body);
            _accessToken = (string)json["access_token"];
            _expiresUtc = DateTime.UtcNow.AddSeconds(((int?)json["expires_in"] ?? 3600) - 60);
            var refresh = (string)json["refresh_token"];
            if (refresh != null) SaveRefreshToken(refresh);
        }

        // O refresh token fica criptografado com DPAPI (só o usuário do Windows consegue ler).
        static void SaveRefreshToken(string token)
        {
            Directory.CreateDirectory(Settings.UserDir);
            File.WriteAllBytes(TokenFile, ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser));
        }

        static string LoadRefreshToken()
        {
            try
            {
                return File.Exists(TokenFile)
                    ? Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(TokenFile), null, DataProtectionScope.CurrentUser))
                    : null;
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------ Data Management

        async Task<JsonNode> GetJsonAsync(string url, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            using var resp = await Http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"GET {url} -> {(int)resp.StatusCode}: {body}");
            return JsonNode.Parse(body);
        }

        /// <summary>Lista os arquivos da pasta configurada (com a versão mais recente de cada um).</summary>
        public async Task<List<RemoteFile>> ListFolderAsync(CancellationToken ct)
        {
            await EnsureLoginAsync(ct);
            var result = new List<RemoteFile>();
            var url = $"{Base}/data/v1/projects/{Uri.EscapeDataString(_cfg.DmProjectId)}/folders/{Uri.EscapeDataString(_cfg.FolderUrn)}/contents?filter[type]=items";

            while (url != null)
            {
                var page = await GetJsonAsync(url, ct);
                var versions = (page["included"] as JsonArray ?? new JsonArray())
                    .Where(v => (string)v?["type"] == "versions")
                    .ToDictionary(v => (string)v["id"], v => v);

                foreach (var item in page["data"] as JsonArray ?? new JsonArray())
                {
                    var tipId = (string)item?["relationships"]?["tip"]?["data"]?["id"];
                    if (tipId == null) continue;
                    if (!versions.TryGetValue(tipId, out var ver))
                        ver = (await GetJsonAsync($"{Base}/data/v1/projects/{Uri.EscapeDataString(_cfg.DmProjectId)}/items/{Uri.EscapeDataString((string)item["id"])}/tip", ct))["data"];

                    result.Add(new RemoteFile
                    {
                        Name = (string)item["attributes"]?["displayName"],
                        ItemId = (string)item["id"],
                        VersionId = (string)ver["id"],
                        VersionNumber = (int?)ver["attributes"]?["versionNumber"] ?? 0,
                        LastModified = (string)ver["attributes"]?["lastModifiedTime"],
                        LastModifiedBy = (string)ver["attributes"]?["lastModifiedUserName"],
                        StorageUrn = (string)ver["relationships"]?["storage"]?["data"]?["id"]
                    });
                }
                url = (string)page["links"]?["next"]?["href"];
            }
            return result;
        }

        /// <summary>Baixa o conteúdo de uma versão (via URL assinada do OSS).</summary>
        public async Task<byte[]> DownloadAsync(RemoteFile f, CancellationToken ct)
        {
            await EnsureLoginAsync(ct);
            if (string.IsNullOrEmpty(f.StorageUrn)) throw new InvalidOperationException(f.Name + ": versão sem storage.");

            // urn:adsk.objects:os.object:wip.dm.prod/abc-123.json
            var path = f.StorageUrn.Substring(f.StorageUrn.LastIndexOf(':') + 1);
            var slash = path.IndexOf('/');
            var bucket = path.Substring(0, slash);
            var key = path.Substring(slash + 1);

            var signed = await GetJsonAsync($"{Base}/oss/v2/buckets/{Uri.EscapeDataString(bucket)}/objects/{Uri.EscapeDataString(key)}/signeds3download", ct);
            var s3 = (string)signed["url"] ?? throw new InvalidOperationException(f.Name + ": URL de download não retornada.");
            return await Http.GetByteArrayAsync(s3, ct);
        }

        static string Base64Url(byte[] b) =>
            Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
