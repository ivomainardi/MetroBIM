using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MetroSP.Coordenacao
{
    /// <summary>
    /// Arquivo de coordenação (.coord-metrosp): tudo o que o coordenador BIM ajustou para a exportação IFC, num
    /// arquivo só, em JSON legível. Leva um ou mais perfis (cada um com o setup do exportador, a fase, as tabelas de
    /// categorias, as classes por família, os property sets e o mapeamento) e os dados comuns (Projeto e Local).
    /// Quem recebe abre o arquivo: ele fica guardado no modelo e o Exportar IFC passa a usar o perfil escolhido.
    /// Não depende da API do Revit (o Civil 3D pode ler o mesmo envelope, com "programa": "Civil3D").
    /// </summary>
    public class ArquivoCoordenacao
    {
        public const string Extensao = ".coord-metrosp";
        public const string FormatoAtual = "MetroSP.Coordenacao/1";
        public const string Filtro = "Arquivo de coordenação MetroSP (*.coord-metrosp)|*.coord-metrosp|Todos os arquivos (*.*)|*.*";

        public string Formato { get; set; } = FormatoAtual;
        public string Programa { get; set; } = "Revit";
        public int Revisao { get; set; } = 1;
        public string Autor { get; set; }
        /// <summary>Data e hora da gravação (ISO 8601).</summary>
        public string Data { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Nota { get; set; }
        /// <summary>Modelo de onde o arquivo saiu (só para mostrar).</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string ModeloOrigem { get; set; }
        /// <summary>Perfil que vale quando quem recebe não escolhe outro.</summary>
        public string PerfilPadrao { get; set; }
        public List<PerfilCoordenacao> Perfis { get; set; } = new List<PerfilCoordenacao>();
        public DadosComuns Comum { get; set; } = new DadosComuns();

        public static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>O perfil pelo nome; sem nome ou sem perfil com esse nome, o padrão e depois o primeiro.</summary>
        public PerfilCoordenacao Perfil(string nome)
        {
            PerfilCoordenacao Por(string n) => string.IsNullOrWhiteSpace(n) ? null :
                Perfis.FirstOrDefault(p => string.Equals(p.Nome?.Trim(), n.Trim(), StringComparison.CurrentCultureIgnoreCase));
            return Por(nome) ?? Por(PerfilPadrao) ?? Perfis.FirstOrDefault();
        }

        public int Alteracoes => Perfis.Sum(p => p.Alteracoes) + Comum.Alteracoes;

        public string ParaJson() => JsonSerializer.Serialize(this, Json);

        public static ArquivoCoordenacao DeJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("O arquivo de coordenação está vazio.");
            ArquivoCoordenacao a;
            try
            {
                // O campo "formato" tem que estar no arquivo (o valor padrão da classe não conta).
                using (JsonDocument d = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }))
                    if (d.RootElement.ValueKind != JsonValueKind.Object ||
                        !d.RootElement.EnumerateObject().Any(x => string.Equals(x.Name, "formato", StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidDataException("O arquivo não é um arquivo de coordenação MetroSP.");
                a = JsonSerializer.Deserialize<ArquivoCoordenacao>(json, Json);
            }
            catch (JsonException ex) { throw new InvalidDataException("O arquivo não é um arquivo de coordenação MetroSP válido (" + ex.Message + ")."); }
            if (a == null || a.Formato == null || !a.Formato.StartsWith("MetroSP.Coordenacao/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("O arquivo não é um arquivo de coordenação MetroSP.");
            if (!int.TryParse(a.Formato.Substring("MetroSP.Coordenacao/".Length), out int v) || v > 1)
                throw new InvalidDataException($"O arquivo de coordenação é de uma versão mais nova do MetroSP ({a.Formato}). Atualize o plugin.");
            a.Perfis = (a.Perfis ?? new List<PerfilCoordenacao>()).Where(p => p != null).ToList();
            foreach (PerfilCoordenacao p in a.Perfis) p.Normalizar();
            a.Comum = a.Comum ?? new DadosComuns();
            if (a.Perfis.Count == 0) throw new InvalidDataException("O arquivo de coordenação não tem nenhum perfil.");
            return a;
        }

        public static ArquivoCoordenacao Ler(string arquivo) => DeJson(File.ReadAllText(arquivo, Encoding.UTF8));

        public void Gravar(string arquivo)
        {
            string pasta = Path.GetDirectoryName(arquivo);
            if (!string.IsNullOrEmpty(pasta)) Directory.CreateDirectory(pasta);
            File.WriteAllText(arquivo, ParaJson(), new UTF8Encoding(false));
        }

        /// <summary>Impressão digital curta do conteúdo (para saber se mudou).</summary>
        public static string Assinatura(string texto)
        {
            using (SHA256 h = SHA256.Create())
            {
                byte[] b = h.ComputeHash(Encoding.UTF8.GetBytes(texto ?? ""));
                return string.Concat(b.Take(8).Select(x => x.ToString("x2")));
            }
        }

        /// <summary>Nome de arquivo sugerido: "&lt;classe&gt; rev3.coord-metrosp".</summary>
        public string NomeSugerido()
        {
            string baseNome = Perfil(null)?.Classe ?? "Coordenacao";
            foreach (char c in Path.GetInvalidFileNameChars()) baseNome = baseNome.Replace(c, '_');
            return $"{baseNome} rev{Revisao}{Extensao}";
        }

        /// <summary>Arquivos de coordenação numa pasta (sem subpastas), do mais novo para o mais antigo.</summary>
        public static List<string> Procurar(string pasta)
        {
            try
            {
                if (string.IsNullOrEmpty(pasta) || !Directory.Exists(pasta)) return new List<string>();
                return Directory.EnumerateFiles(pasta, "*" + Extensao).OrderByDescending(File.GetLastWriteTimeUtc).ToList();
            }
            catch { return new List<string>(); }
        }
    }

    /// <summary>Um jeito de exportar (ex.: "Entrega Metrô · IFC 4.3", "Compatibilização · IFC 4").</summary>
    public class PerfilCoordenacao
    {
        public string Nome { get; set; }
        /// <summary>Classe MetroSP (nome do setup e dos .txt, ex.: "B3").</summary>
        public string Classe { get; set; }
        /// <summary>Setup do exportador IFC do Revit (o .json inteiro), sem o ActivePhaseId (que é número interno do modelo).</summary>
        public JsonObject Exportador { get; set; }
        /// <summary>Fase exportada, pelo nome (null = padrão do exportador, a última fase).</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Fase { get; set; }
        /// <summary>Tabelas de categorias por esquema ("IFC4", "IFC4x3"). Esquema sem tabela: vale a do computador de quem exporta.</summary>
        public Dictionary<string, List<LinhaCategoria>> Categorias { get; set; } = new Dictionary<string, List<LinhaCategoria>>();
        /// <summary>Classe IFC, tipo predefinido e "Exportar para IFC" por família e tipo (vale em qualquer modelo).</summary>
        public List<AjusteFamilia> Familias { get; set; } = new List<AjusteFamilia>();
        /// <summary>Os mesmos ajustes por elemento (UniqueId): só valem no modelo de onde saíram.</summary>
        public List<AjusteElemento> Elementos { get; set; } = new List<AjusteElemento>();
        /// <summary>Conteúdo do .txt de property sets do usuário.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string PropertySets { get; set; }
        /// <summary>Conteúdo do .txt de mapeamento de parâmetros.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Mapeamento { get; set; }
        /// <summary>As outras escolhas da janela Exportar IFC (elementos, nome do arquivo, EPSG, eixos).</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public OpcoesExportacao Exportacao { get; set; }

        /// <summary>Esquema do setup (IFCVersion), para mostrar.</summary>
        [JsonIgnore] public string Esquema
        {
            get
            {
                try { return Exportador?["IFCVersion"]?.ToString(); }
                catch { return null; }
            }
        }

        [JsonIgnore] public int Alteracoes =>
            (Exportador != null ? 1 : 0) + (Fase != null ? 1 : 0) + Categorias.Values.Sum(l => l.Count > 0 ? 1 : 0) +
            Familias.Count + Elementos.Count + (PropertySets != null ? 1 : 0) + (Mapeamento != null ? 1 : 0) +
            (Exportacao?.Alteracoes ?? 0);

        internal void Normalizar()
        {
            Categorias = (Categorias ?? new Dictionary<string, List<LinhaCategoria>>())
                .Where(k => k.Value != null && !string.IsNullOrWhiteSpace(k.Key))
                .ToDictionary(k => k.Key, k => k.Value.Where(l => l != null && !string.IsNullOrWhiteSpace(l.Categoria)).ToList());
            Familias = (Familias ?? new List<AjusteFamilia>()).Where(f => f != null && !string.IsNullOrWhiteSpace(f.Familia)).ToList();
            Elementos = (Elementos ?? new List<AjusteElemento>()).Where(e => e != null && !string.IsNullOrWhiteSpace(e.UniqueId)).ToList();
        }

        public PerfilCoordenacao Copia() => new PerfilCoordenacao
        {
            Nome = Nome, Classe = Classe, Fase = Fase, PropertySets = PropertySets, Mapeamento = Mapeamento,
            Exportacao = Exportacao?.Copia(),
            Exportador = Exportador == null ? null : (JsonObject)JsonNode.Parse(Exportador.ToJsonString()),
            Categorias = Categorias.ToDictionary(k => k.Key, k => k.Value.Select(l => (LinhaCategoria)l.MemberwiseCopy()).ToList()),
            Familias = Familias.Select(f => (AjusteFamilia)f.MemberwiseCopy()).ToList(),
            Elementos = Elementos.Select(e => (AjusteElemento)e.MemberwiseCopy()).ToList(),
        };

        public override string ToString() => Nome;
    }

    /// <summary>Escolhas da janela Exportar IFC que não estão no setup. Campo null = vale a escolha de quem exporta.</summary>
    public class OpcoesExportacao
    {
        public const string ModeloInteiro = "Modelo inteiro";
        public const string NomeDoModelo = "{modelo}";

        /// <summary>"Modelo inteiro" ou o nome da vista 3D cujo visível é exportado.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Elementos { get; set; }
        /// <summary>Nome do .ifc, sem a extensão; "{modelo}" é trocado pelo nome do modelo.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string NomeArquivo { get; set; }
        /// <summary>IFC sem EPSG, IfcProjectedCRS e IfcMapConversion.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? EpsgEmBranco { get; set; }
        /// <summary>Sempre exportar os eixos (Grids).</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? SempreEixos { get; set; }

        [JsonIgnore] public int Alteracoes => (Elementos != null ? 1 : 0) + (NomeArquivo != null ? 1 : 0) + (EpsgEmBranco != null ? 1 : 0) + (SempreEixos != null ? 1 : 0);
        [JsonIgnore] public bool SoVista => Elementos != null && !string.Equals(Elementos.Trim(), ModeloInteiro, StringComparison.CurrentCultureIgnoreCase);

        /// <summary>Nome do .ifc para o modelo (null = não define).</summary>
        public string NomePara(string modelo) =>
            string.IsNullOrWhiteSpace(NomeArquivo) ? null : NomeArquivo.Replace(NomeDoModelo, modelo ?? "").Trim();

        public OpcoesExportacao Copia() => (OpcoesExportacao)MemberwiseClone();
    }

    public class LinhaCategoria
    {
        public string Categoria { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Subcategoria { get; set; }
        public string Classe { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Tipo { get; set; }
        public bool Exportar { get; set; } = true;
        internal object MemberwiseCopy() => MemberwiseClone();
    }

    /// <summary>Ajuste da classe IFC. Campo null = não mexe.</summary>
    public abstract class AjusteIfc
    {
        /// <summary>"Exportar para IFC como" (IfcExportAs).</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Classe { get; set; }
        /// <summary>Tipo predefinido (IfcExportType).</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string TipoPredefinido { get; set; }
        /// <summary>"Exportar para IFC": "Sim", "Não" ou "Padrão" (pelo tipo/categoria).</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Exportar { get; set; }
        internal object MemberwiseCopy() => MemberwiseClone();

        /// <summary>Valor do parâmetro "Exportar para IFC" do Revit: 0 = padrão, 1 = sim, 2 = não; null = não mexe.</summary>
        [JsonIgnore] public int? ExportarValor
        {
            get
            {
                string e = (Exportar ?? "").Trim().ToLowerInvariant();
                if (e == "sim" || e == "yes" || e == "1") return 1;
                if (e == "não" || e == "nao" || e == "no" || e == "2") return 2;
                if (e == "padrão" || e == "padrao" || e == "0" || e == "pelo tipo") return 0;
                return null;
            }
        }

        public static string ExportarTexto(int valor) => valor == 1 ? "Sim" : valor == 2 ? "Não" : "Padrão";
    }

    public class AjusteFamilia : AjusteIfc
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Categoria { get; set; }
        public string Familia { get; set; }
        public string Tipo { get; set; }
        [JsonIgnore] public string Chave => (Familia ?? "").Trim() + " : " + (Tipo ?? "").Trim();
    }

    public class AjusteElemento : AjusteIfc
    {
        public string UniqueId { get; set; }
        /// <summary>Só para mostrar (categoria, família e tipo, id).</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Descricao { get; set; }
    }

    public class DadosComuns
    {
        /// <summary>IfcProject, IfcSite, IfcBuilding e Pset MetroSP_Projeto (mesmo formato dos dados salvos em Projeto e Local).</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public JsonObject DadosProjeto { get; set; }
        /// <summary>Conteúdo do Nomenclatura.txt (regras "padrão do nome do modelo → classe").</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Nomenclatura { get; set; }
        [JsonIgnore] public int Alteracoes => (DadosProjeto != null ? 1 : 0) + (Nomenclatura != null ? 1 : 0);
    }
}
