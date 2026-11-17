using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MetroSP.Comum
{
    /// <summary>
    /// Aviso de versão nova, igual nos três plugins (Revit, Civil 3D e Navisworks). Este arquivo fica em comum/ e entra
    /// no projeto de cada plugin como link, sem interface: cada plugin mostra o aviso do seu jeito.
    ///
    /// Como funciona:
    /// - A versão instalada é a do instalador único (MetroSP-Setup-x.y.z.exe), lida do registro do Windows onde o
    ///   Inno Setup guarda a desinstalação. Sem instalador (DLL copiada à mão, compilação de teste), não avisa nada.
    /// - A última versão vem do endereço github.com/.../releases/latest, que redireciona para a tag da última release.
    ///   Não usa a API do GitHub, que limita 60 consultas por hora para todo o Metrô (mesmo IP na rede).
    /// - As novidades vêm do NOTAS-DA-VERSAO.md daquela tag (raw.githubusercontent.com).
    /// - Consulta no máximo uma vez por dia por usuário (vale para todos os plugins) e fica quieto sem internet,
    ///   com proxy que bloqueia ou com qualquer erro.
    /// </summary>
    public static class VersaoNova
    {
        public const string Repositorio = "ivomainardi/MetroBIM";

        /// <summary>AppId do instalador único (instalador/MetroSP.iss), com o sufixo que o Inno Setup acrescenta.</summary>
        private const string ChaveDesinstalar = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{8F3A2C61-4B7E-4D29-9E15-6A0C3B7D5E42}_is1";

        /// <summary>Onde o aviso guarda a data da última consulta e a versão que o usuário pediu para não avisar.</summary>
        private const string ChaveAviso = @"Software\MetroSP\AvisoVersao";

        public static string PaginaReleases => $"https://github.com/{Repositorio}/releases/latest";

        public class Novidade
        {
            public string Instalada;
            public string Nova;
            /// <summary>Página da release no GitHub.</summary>
            public string Pagina;
            /// <summary>Link direto do instalador (o navegador baixa o .exe).</summary>
            public string Instalador;
            /// <summary>Notas da versão nova em texto simples (sem a marcação do Markdown); vazio se não deu para ler.</summary>
            public string Notas;
        }

        /// <summary>Versão do instalador único neste computador, ou null se o MetroSP não foi instalado por ele.</summary>
        public static string Instalada()
        {
            foreach (var raiz in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                try
                {
                    using (var k = raiz.OpenSubKey(ChaveDesinstalar))
                    {
                        if (k?.GetValue("DisplayVersion") is string v && Ler(v) != null) return v.Trim();
                    }
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// Procura uma versão mais nova que a instalada. Sem <paramref name="manual"/> (ao abrir o software), respeita a
        /// consulta diária e a versão que o usuário pediu para não avisar. Devolve null quando não há o que avisar.
        /// Com <paramref name="manual"/>, erros de rede sobem como exceção, para a janela dizer o que houve.
        /// </summary>
        public static async Task<Novidade> ProcurarAsync(bool manual = false)
        {
            try
            {
                string instalada = Instalada();
                if (instalada == null && !manual) return null;
                if (!manual)
                {
                    if (LerAviso("UltimaConsulta") == DateTime.Today.ToString("yyyy-MM-dd")) return null;
                    GravarAviso("UltimaConsulta", DateTime.Today.ToString("yyyy-MM-dd"));
                }

                using (var http = CriarHttp())
                {
                    string nova = await UltimaVersaoAsync(http).ConfigureAwait(false);
                    if (nova == null) throw new InvalidOperationException("O GitHub não informou a última versão.");
                    if (!MaisNova(nova, instalada)) return manual ? new Novidade { Instalada = instalada, Nova = nova } : null;
                    if (!manual && LerAviso("Ignorada") == nova) return null;

                    string notas = "";
                    try
                    {
                        string md = await http.GetStringAsync($"https://raw.githubusercontent.com/{Repositorio}/v{nova}/NOTAS-DA-VERSAO.md").ConfigureAwait(false);
                        notas = NotasDaVersao(md, nova);
                    }
                    catch { /* sem as notas, o aviso sai mesmo assim */ }

                    return new Novidade
                    {
                        Instalada = instalada,
                        Nova = nova,
                        Pagina = $"https://github.com/{Repositorio}/releases/tag/v{nova}",
                        Instalador = $"https://github.com/{Repositorio}/releases/download/v{nova}/MetroSP-Setup-{nova}.exe",
                        Notas = notas,
                    };
                }
            }
            catch
            {
                if (manual) throw;
                return null;
            }
        }

        /// <summary>Há versão nova de verdade (a resposta do manual também volta quando já está atualizado).</summary>
        public static bool TemNova(Novidade n) => n != null && MaisNova(n.Nova, n.Instalada);

        /// <summary>"Não avisar desta versão": o aviso volta só quando sair uma versão mais nova que esta.</summary>
        public static void Ignorar(string versao) => GravarAviso("Ignorada", versao);

        public static void Abrir(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        // ---------- partes testáveis (sem rede e sem registro) ----------

        /// <summary>"v1.0.10" ou "1.0.10" vira Version(1,0,10); qualquer outra coisa vira null.</summary>
        public static Version Ler(string texto)
        {
            var m = Regex.Match(texto ?? "", @"^\s*v?(\d+)\.(\d+)\.(\d+)\s*$");
            return m.Success ? new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)) : null;
        }

        /// <summary>A versão <paramref name="nova"/> é maior que a <paramref name="instalada"/> (sem instalada, qualquer uma é nova).</summary>
        public static bool MaisNova(string nova, string instalada)
        {
            var n = Ler(nova);
            if (n == null) return false;
            var i = Ler(instalada);
            return i == null || n > i;
        }

        /// <summary>Tira a versão do endereço para onde o /releases/latest redireciona (…/releases/tag/v1.0.7).</summary>
        public static string VersaoDoEndereco(string endereco)
        {
            var m = Regex.Match(endereco ?? "", @"/releases/tag/v?(\d+\.\d+\.\d+)/?$");
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>
        /// A seção "## x.y.z" do NOTAS-DA-VERSAO.md em texto simples: sem o título, sem **, ` e links do Markdown,
        /// com os itens começando por "•". Corta em <paramref name="limite"/> caracteres.
        /// </summary>
        public static string NotasDaVersao(string markdown, string versao, int limite = 4000)
        {
            if (string.IsNullOrEmpty(markdown)) return "";
            markdown = markdown.Replace("\r\n", "\n");
            var m = Regex.Match(markdown, @"(?ms)^## " + Regex.Escape(versao) + @"\b[^\n]*\n(.*?)(?=^## |\z)");
            if (!m.Success) return "";
            string t = m.Groups[1].Value;
            t = Regex.Replace(t, @"\[([^\]]+)\]\([^)]*\)", "$1");   // [texto](link) -> texto
            t = t.Replace("**", "").Replace("`", "");
            t = Regex.Replace(t, @"(?m)^(\s*)- ", "$1• ");
            t = Regex.Replace(t, @"\n{3,}", "\n\n").Trim();
            if (t.Length > limite) t = t.Substring(0, limite).TrimEnd() + " …";
            return t;
        }

        // ---------- rede e registro ----------

        private static HttpClient CriarHttp()
        {
#if NETFRAMEWORK
            // Revit 2022-2024 e Navisworks (.NET Framework 4.8): garante o TLS 1.2 que o GitHub exige.
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
#endif
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,   // o que interessa é o endereço do redirecionamento
                UseProxy = true,
                DefaultProxyCredentials = CredentialCache.DefaultCredentials,   // proxy da rede do Metrô, com o login do Windows
            };
            var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MetroSP-plugin");
            return http;
        }

        private static async Task<string> UltimaVersaoAsync(HttpClient http)
        {
            using (var r = await http.GetAsync(PaginaReleases, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                var destino = r.Headers.Location;
                if (destino == null) return null;
                return VersaoDoEndereco(destino.IsAbsoluteUri ? destino.AbsolutePath : destino.OriginalString);
            }
        }

        private static string LerAviso(string nome)
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(ChaveAviso)) return k?.GetValue(nome) as string; }
            catch { return null; }
        }

        private static void GravarAviso(string nome, string valor)
        {
            try { using (var k = Registry.CurrentUser.CreateSubKey(ChaveAviso)) k?.SetValue(nome, valor ?? ""); }
            catch { }
        }
    }
}
