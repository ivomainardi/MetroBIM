using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.DB;
using MetroSP.Planilha;

namespace MetroSP.Classificacao
{
    /// <summary>Como a classificação fica gravada no elemento.</summary>
    public enum FormatoClassificacao
    {
        /// <summary>Um parâmetro "Código:Descrição" (ClassificationCode do exportador IFC da Autodesk).</summary>
        CodigoDescricao,
        /// <summary>Código e descrição em parâmetros separados (Classification Manager, Código de montagem, OmniClass).</summary>
        Separados,
    }

    /// <summary>Uma classificação do padrão: onde fica no modelo e de qual planilha vem a lista.</summary>
    public class SistemaClassificacao
    {
        public string Nome { get; set; } = "Metrô SP";
        public string Edicao { get; set; }
        public FormatoClassificacao Formato { get; set; } = FormatoClassificacao.CodigoDescricao;
        public string ParametroCodigo { get; set; } = "ClassificationCode";
        /// <summary>Só no formato Separados; vazio = só o código é conferido.</summary>
        public string ParametroDescricao { get; set; }
        /// <summary>Gravar "[Nome]Código:Descrição" (o exportador IFC usa o nome como IfcClassification).</summary>
        public bool GravarNome { get; set; }
        public MapaColunas Planilha { get; set; } = new MapaColunas();

        [JsonIgnore] public ClassificationList Lista { get; set; }
        [JsonIgnore] public string Erro { get; set; }

        /// <summary>Texto curto para tabelas: "Uniformat (Assembly Code)".</summary>
        [JsonIgnore]
        public string Rotulo => $"{Nome} ({ParametroCodigo})";

        /// <summary>Parâmetros gravados por esta classificação.</summary>
        [JsonIgnore]
        public string Parametros => Formato == FormatoClassificacao.Separados && !string.IsNullOrWhiteSpace(ParametroDescricao)
            ? ParametroCodigo + " + " + ParametroDescricao : ParametroCodigo;

        public SistemaClassificacao Clonar()
        {
            var c = (SistemaClassificacao)MemberwiseClone();
            c.Planilha = Planilha?.Clonar() ?? new MapaColunas();
            c.Lista = null;
            c.Erro = null;
            return c;
        }

        public static readonly CampoPlanilha CampoCodigo = new CampoPlanilha("codigo", "Código", true,
            "Coluna com o código do item (ex.: EST.02.01).", "código", "codigo", "cód", "cod", "code", "identification", "number", "número", "numero", "item");
        public static readonly CampoPlanilha CampoDescricao = new CampoPlanilha("descricao", "Descrição", false,
            "Coluna com a descrição oficial. Sem ela, só o código é conferido.", "descrição", "descricao", "description", "nome", "name", "título", "titulo", "title");
        public static readonly CampoPlanilha CampoPai = new CampoPlanilha("pai", "Código pai", false,
            "Opcional. Sem ela, a hierarquia sai do próprio código (EST.02.01 fica dentro de EST.02).", "código pai", "codigo pai", "pai", "parent", "referencedsource");
        public static readonly CampoPlanilha CampoCategorias = new CampoPlanilha("categorias", "Categorias", false,
            "Opcional. Categorias do Revit em que o item vale, separadas por ; (vazio = todas).", "categorias", "categoria", "categorias revit", "categories", "category");

        /// <summary>Só na leitura automática (Conferir Entrega): edição da lista.</summary>
        public static readonly CampoPlanilha CampoEdicao = new CampoPlanilha("edicao", "Edição", false, null, "edição", "edicao", "edition", "versão", "versao");

        public static List<CampoPlanilha> Campos => new List<CampoPlanilha> { CampoCodigo, CampoDescricao, CampoPai, CampoCategorias };

        /// <summary>Lê a planilha da classificação (as planilhas já lidas ficam no cache, por arquivo).</summary>
        public void Carregar(Dictionary<string, Tabela> cache = null)
        {
            Lista = null;
            Erro = null;
            try
            {
                if (string.IsNullOrEmpty(Planilha?.Arquivo)) { Erro = "sem planilha"; return; }
                if (!File.Exists(Planilha.Arquivo)) { Erro = "planilha não encontrada: " + Planilha.Arquivo; return; }
                Tabela t = null;
                if (cache == null || !cache.TryGetValue(Planilha.Arquivo, out t))
                {
                    t = LeitorPlanilha.Ler(Planilha.Arquivo);
                    if (cache != null) cache[Planilha.Arquivo] = t;
                }
                if (Planilha.Coluna(CampoCodigo.Chave) == null) { Erro = "falta indicar a coluna do código"; return; }
                Lista = ClassificationList.De(t, Planilha, Nome);
                if (Lista.Itens.Count == 0) Erro = "nenhum código na planilha";
            }
            catch (Exception ex) { Erro = ex.Message; }
        }
    }

    /// <summary>Modelos de classificação da Autodesk, para preencher um sistema com um clique.</summary>
    public class ModeloAutodesk
    {
        public string Rotulo { get; set; }
        public string Dica { get; set; }
        public string Nome { get; set; }
        public FormatoClassificacao Formato { get; set; }
        public string Codigo { get; set; }
        public string Descricao { get; set; }
        public override string ToString() => Rotulo;

        public static List<ModeloAutodesk> Todos()
        {
            // Pelo nome: alguns BuiltInParameter mudam entre versões da API (2025 x 2026).
            string L(string nomes, string padrao)
            {
                foreach (string nome in nomes.Split('|'))
                {
                    try { if (Enum.TryParse(nome, out BuiltInParameter p)) return LabelUtils.GetLabelFor(p); }
                    catch { }
                }
                return padrao;
            }
            var m = new List<ModeloAutodesk>();
            for (int i = 1; i <= 4; i++)
            {
                string p = i == 1 ? "ClassificationCode" : $"ClassificationCode({i})";
                m.Add(new ModeloAutodesk
                {
                    Rotulo = "Exportador IFC · " + p,
                    Dica = $"Parâmetro {p} com \"Código:Descrição\" (ou \"[Nome]Código:Descrição\"), lido pelo exportador IFC da Autodesk " +
                           "como IfcClassificationReference. Use (2), (3)... para mais de uma classificação no mesmo elemento.",
                    Nome = i == 1 ? "Metrô SP" : "Classificação " + i,
                    Formato = FormatoClassificacao.CodigoDescricao,
                    Codigo = p,
                });
            }
            foreach (var (nome, tabela) in new[] { ("Uniformat", "Uniformat"), ("OmniClass 21", "OmniClass.21"), ("OmniClass 23", "OmniClass.23"), ("Uniclass Pr", "Uniclass.Pr"), ("Uniclass Ss", "Uniclass.Ss") })
                m.Add(new ModeloAutodesk
                {
                    Rotulo = "Classification Manager · " + nome,
                    Dica = $"Parâmetros do Classification Manager da Autodesk: Classification.{tabela}.Number e .Description.",
                    Nome = nome,
                    Formato = FormatoClassificacao.Separados,
                    Codigo = $"Classification.{tabela}.Number",
                    Descricao = $"Classification.{tabela}.Description",
                });
            m.Add(new ModeloAutodesk
            {
                Rotulo = "Revit · Código de montagem (Uniformat)",
                Dica = "Parâmetro de tipo nativo do Revit. A descrição é preenchida pelo próprio Revit (arquivo Uniformat), por isso só é conferida.",
                Nome = "Uniformat",
                Formato = FormatoClassificacao.Separados,
                Codigo = L("UNIFORMAT_CODE|ASSEMBLY_CODE", "Assembly Code"),
                Descricao = L("UNIFORMAT_DESCRIPTION|ASSEMBLY_DESCRIPTION", "Assembly Description"),
            });
            m.Add(new ModeloAutodesk
            {
                Rotulo = "Revit · OmniClass",
                Dica = "Parâmetro de tipo nativo do Revit. O título é preenchido pelo próprio Revit, por isso só é conferido.",
                Nome = "OmniClass",
                Formato = FormatoClassificacao.Separados,
                Codigo = L("OMNICLASS_CODE", "OmniClass Number"),
                Descricao = L("OMNICLASS_DESCRIPTION", "OmniClass Title"),
            });
            return m;
        }
    }

    /// <summary>
    /// Padrão de verificação: uma ou mais classificações, cada uma com sua planilha e colunas.
    /// Fica em %AppData%\MetroSP\Classificacao\[nome].json; Exportar/Importar leva para outra máquina.
    /// </summary>
    public class PadraoClassificacao
    {
        public string Nome { get; set; } = "Padrão Metrô";
        public List<SistemaClassificacao> Sistemas { get; set; } = new List<SistemaClassificacao>();

        [JsonIgnore] public string Caminho { get; set; }
        public override string ToString() => Nome;

        public static readonly string Pasta =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MetroSP", "Classificacao");

        private static readonly JsonSerializerOptions Opcoes = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public PadraoClassificacao Clonar() => new PadraoClassificacao { Nome = Nome, Caminho = Caminho, Sistemas = Sistemas.Select(s => s.Clonar()).ToList() };

        public static PadraoClassificacao Ler(string caminho)
        {
            string json = File.ReadAllText(caminho, Encoding.UTF8);
            var p = JsonSerializer.Deserialize<PadraoClassificacao>(json, Opcoes) ?? throw new InvalidDataException("Arquivo de padrão vazio.");
            p.Caminho = caminho;
            p.Sistemas ??= new List<SistemaClassificacao>();
            foreach (SistemaClassificacao s in p.Sistemas) s.Planilha ??= new MapaColunas();
            if (string.IsNullOrWhiteSpace(p.Nome)) p.Nome = Path.GetFileNameWithoutExtension(caminho);
            return p;
        }

        /// <summary>Padrões salvos neste computador, por nome. Arquivos com erro ficam de fora.</summary>
        public static List<PadraoClassificacao> Listar()
        {
            var lista = new List<PadraoClassificacao>();
            try
            {
                if (!Directory.Exists(Pasta)) return lista;
                foreach (string f in Directory.GetFiles(Pasta, "*.json"))
                {
                    try { lista.Add(Ler(f)); } catch { /* arquivo corrompido: ignora */ }
                }
            }
            catch { }
            return lista.OrderBy(p => p.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>Grava em %AppData% (renomear o padrão troca o arquivo).</summary>
        public void Salvar()
        {
            Directory.CreateDirectory(Pasta);
            string destino = Path.Combine(Pasta, NomeArquivo(Nome) + ".json");
            Exportar(destino);
            if (!string.IsNullOrEmpty(Caminho) && !string.Equals(Caminho, destino, StringComparison.OrdinalIgnoreCase) &&
                Path.GetDirectoryName(Caminho).TrimEnd('\\', '/').Equals(Pasta.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(Caminho); } catch { }
            }
            Caminho = destino;
        }

        public void Exportar(string destino) =>
            File.WriteAllText(destino, JsonSerializer.Serialize(this, Opcoes), new UTF8Encoding(false));

        public void Excluir()
        {
            if (!string.IsNullOrEmpty(Caminho) && File.Exists(Caminho)) File.Delete(Caminho);
        }

        public static bool Existe(string nome) => File.Exists(Path.Combine(Pasta, NomeArquivo(nome) + ".json"));

        private static string NomeArquivo(string nome)
        {
            string n = string.Concat((nome ?? "padrao").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
            return n.Length == 0 ? "padrao" : n;
        }

        /// <summary>Primeiro uso: monta um padrão com a planilha e o parâmetro que a classificação já usava.</summary>
        public static PadraoClassificacao DoLegado(ClassSettings config)
        {
            var s = new SistemaClassificacao
            {
                Nome = "Metrô SP",
                ParametroCodigo = string.IsNullOrWhiteSpace(config.Parametro) ? "ClassificationCode" : config.Parametro,
                Planilha = new MapaColunas { Arquivo = config.Planilha, LinhaCabecalho = 0 },
            };
            if (!string.IsNullOrEmpty(config.Planilha) && File.Exists(config.Planilha))
            {
                try
                {
                    Tabela t = LeitorPlanilha.Ler(config.Planilha);
                    s.Planilha.Aba = t.NomesAbas[0];
                    s.Planilha.LinhaCabecalho = MapaColunas.AcharCabecalho(t, s.Planilha.Aba, SistemaClassificacao.Campos);
                    s.Planilha.Sugerir(t, SistemaClassificacao.Campos);
                }
                catch { }
            }
            if (s.Planilha.LinhaCabecalho <= 0) s.Planilha.LinhaCabecalho = 1;
            return new PadraoClassificacao { Nome = "Padrão Metrô", Sistemas = { s } };
        }
    }
}
