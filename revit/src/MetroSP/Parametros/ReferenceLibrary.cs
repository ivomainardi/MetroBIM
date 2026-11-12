using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MetroSP.Parametros
{
    /// <summary>Uma linha PARAM de um .txt de parâmetros compartilhados.</summary>
    public class SharedParamDef
    {
        public Guid Guid { get; set; }
        public string Nome { get; set; }
        public string TipoDeDado { get; set; }
        public string Grupo { get; set; }
        public string Descricao { get; set; }
        public bool Visivel { get; set; } = true;
        public SharedParamTxt Arquivo { get; set; }
    }

    /// <summary>
    /// Arquivo .txt de parâmetros compartilhados lido diretamente (sem a API do Revit),
    /// então não precisa estar configurado no Revit.
    /// </summary>
    public class SharedParamTxt
    {
        public string Caminho { get; set; }
        public string Nome => Path.GetFileName(Caminho);
        /// <summary>Item da lista de referências de onde o arquivo veio (o próprio arquivo ou a pasta).</summary>
        public string Entrada { get; set; }
        public bool EhBase { get; set; }
        public bool EhRevitAtual { get; set; }
        public string Erro { get; set; }
        public List<SharedParamDef> Definicoes { get; } = new List<SharedParamDef>();
        public Dictionary<Guid, SharedParamDef> PorGuid { get; } = new Dictionary<Guid, SharedParamDef>();
        public Dictionary<string, SharedParamDef> PorNome { get; } = new Dictionary<string, SharedParamDef>(StringComparer.CurrentCultureIgnoreCase);

        public static SharedParamTxt Ler(string caminho)
        {
            var txt = new SharedParamTxt { Caminho = caminho };
            try
            {
                var grupos = new Dictionary<string, string>();
                // Colunas padrão; o cabeçalho *PARAM do arquivo, se existir, prevalece.
                var col = new Dictionary<string, int>
                {
                    ["GUID"] = 1, ["NAME"] = 2, ["DATATYPE"] = 3, ["GROUP"] = 5, ["VISIBLE"] = 6, ["DESCRIPTION"] = 7,
                };

                foreach (string linha in LerLinhas(caminho))
                {
                    string[] c = linha.Split('\t');
                    if (c[0] == "*PARAM")
                    {
                        for (int i = 1; i < c.Length; i++) col[c[i].Trim().ToUpperInvariant()] = i;
                    }
                    else if (c[0] == "GROUP" && c.Length >= 3)
                    {
                        grupos[c[1]] = c[2];
                    }
                    else if (c[0] == "PARAM" && c.Length >= 4 && Guid.TryParse(Col(c, col, "GUID"), out Guid g))
                    {
                        string grupoId = Col(c, col, "GROUP");
                        var d = new SharedParamDef
                        {
                            Guid = g,
                            Nome = Col(c, col, "NAME"),
                            TipoDeDado = Col(c, col, "DATATYPE"),
                            Grupo = grupoId != null && grupos.TryGetValue(grupoId, out string gn) ? gn : grupoId,
                            Descricao = Col(c, col, "DESCRIPTION"),
                            Visivel = Col(c, col, "VISIBLE") != "0",
                            Arquivo = txt,
                        };
                        txt.Definicoes.Add(d);
                        txt.PorGuid[g] = d;
                        if (!string.IsNullOrEmpty(d.Nome)) txt.PorNome[d.Nome] = d;
                    }
                }
            }
            catch (Exception ex)
            {
                txt.Erro = ex.Message;
            }
            return txt;
        }

        private static string Col(string[] c, Dictionary<string, int> col, string nome) =>
            col.TryGetValue(nome, out int i) && i < c.Length ? c[i] : null;

        /// <summary>Lê respeitando BOM (o Revit costuma gravar UTF-16); sem BOM tenta UTF-8 e cai para Latin-1.</summary>
        private static IEnumerable<string> LerLinhas(string caminho)
        {
            byte[] bytes = File.ReadAllBytes(caminho);
            string texto;
            using (var leitor = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true))
            {
                try { texto = leitor.ReadToEnd(); }
                catch (DecoderFallbackException) { texto = Encoding.Latin1.GetString(bytes); }
            }
            return texto.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        }
    }

    /// <summary>
    /// Lista de arquivos e pastas de referência de parâmetros compartilhados, com a "base Metrô".
    /// Salva por usuário em %AppData%\MetroSP\referencias.ini.
    /// </summary>
    public class ReferenceLibrary
    {
        private static readonly string ArquivoConfig =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MetroSP", "referencias.ini");

        /// <summary>Arquivos .txt e pastas, na ordem em que foram adicionados.</summary>
        public List<string> Entradas { get; } = new List<string>();
        public string Base { get; set; }

        /// <summary>Arquivos lidos (base primeiro). Preenchido por Carregar.</summary>
        public List<SharedParamTxt> Arquivos { get; private set; } = new List<SharedParamTxt>();

        public static ReferenceLibrary Abrir()
        {
            var lib = new ReferenceLibrary();
            try
            {
                if (File.Exists(ArquivoConfig))
                {
                    foreach (string linha in File.ReadAllLines(ArquivoConfig, Encoding.UTF8))
                    {
                        string[] p = linha.Split(new[] { '=' }, 2);
                        if (p.Length != 2 || p[1].Length == 0) continue;
                        if (p[0] == "base") lib.Base = p[1];
                        else if (p[0] == "ref" && !lib.Entradas.Contains(p[1], StringComparer.OrdinalIgnoreCase)) lib.Entradas.Add(p[1]);
                    }
                }
            }
            catch { /* configuração corrompida: começa vazia */ }
            return lib;
        }

        public void Salvar()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ArquivoConfig));
                var linhas = new List<string> { "base=" + Base };
                linhas.AddRange(Entradas.Select(e => "ref=" + e));
                File.WriteAllLines(ArquivoConfig, linhas, Encoding.UTF8);
            }
            catch { /* não é crítico */ }
        }

        public void Adicionar(string caminho)
        {
            if (!Entradas.Contains(caminho, StringComparer.OrdinalIgnoreCase)) Entradas.Add(caminho);
            if (string.IsNullOrEmpty(Base) && File.Exists(caminho)) Base = caminho;
        }

        /// <summary>Remove a entrada (arquivo ou pasta) de onde veio o arquivo.</summary>
        public void Remover(SharedParamTxt txt)
        {
            Entradas.RemoveAll(e => string.Equals(e, txt.Entrada, StringComparison.OrdinalIgnoreCase));
            if (string.Equals(Base, txt.Caminho, StringComparison.OrdinalIgnoreCase)) Base = null;
        }

        /// <summary>
        /// Lê todos os arquivos: as pastas viram os .txt que contêm. Sem nenhuma referência,
        /// usa o arquivo configurado no Revit, marcado como "Revit atual".
        /// </summary>
        public void Carregar(string arquivoRevit)
        {
            var caminhos = new List<(string Caminho, string Entrada)>();
            void Incluir(string c, string entrada)
            {
                if (!caminhos.Any(x => string.Equals(x.Caminho, c, StringComparison.OrdinalIgnoreCase))) caminhos.Add((c, entrada));
            }

            if (!string.IsNullOrEmpty(Base) && File.Exists(Base)) Incluir(Base, Entradas.FirstOrDefault(e => Contem(e, Base)) ?? Base);
            foreach (string e in Entradas)
            {
                if (Directory.Exists(e))
                {
                    IEnumerable<string> txts;
                    try { txts = Directory.EnumerateFiles(e, "*.txt", SearchOption.AllDirectories).OrderBy(f => f).ToList(); }
                    catch { txts = Enumerable.Empty<string>(); }
                    foreach (string f in txts) Incluir(f, e);
                }
                else if (File.Exists(e)) Incluir(e, e);
            }

            var lidos = caminhos.Select(c =>
            {
                SharedParamTxt t = SharedParamTxt.Ler(c.Caminho);
                t.Entrada = c.Entrada;
                t.EhBase = string.Equals(c.Caminho, Base, StringComparison.OrdinalIgnoreCase);
                return t;
            })
            // Numa pasta podem existir .txt que não são de parâmetros compartilhados.
            .Where(t => t.Definicoes.Count > 0 || !Directory.Exists(t.Entrada))
            .ToList();

            if (lidos.Count == 0 && !string.IsNullOrEmpty(arquivoRevit) && File.Exists(arquivoRevit))
            {
                SharedParamTxt t = SharedParamTxt.Ler(arquivoRevit);
                t.Entrada = arquivoRevit;
                t.EhRevitAtual = true;
                lidos.Add(t);
            }
            Arquivos = lidos;
        }

        private static bool Contem(string entrada, string arquivo) =>
            string.Equals(entrada, arquivo, StringComparison.OrdinalIgnoreCase) ||
            (Directory.Exists(entrada) && arquivo.StartsWith(entrada.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

        /// <summary>Todas as definições com o GUID, na ordem dos arquivos (base primeiro).</summary>
        public List<SharedParamDef> Buscar(Guid guid) =>
            Arquivos.Select(a => a.PorGuid.TryGetValue(guid, out SharedParamDef d) ? d : null).Where(d => d != null).ToList();
    }
}
