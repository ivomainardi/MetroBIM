using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace MetroSP.Ids
{
    /// <summary>
    /// Os arquivos de cada classe IFC (setup .json, property sets .txt e mapeamento .txt) nas três origens:
    /// os do usuário (%AppData%\MetroSP\IFC), os baixados do Forma online e os instalados. Gravar, trocar de
    /// classe ou apagar mexe só nos do usuário, e a versão anterior fica guardada em IFC\Anteriores.
    /// </summary>
    public static class ArquivosIfc
    {
        public enum Tipo { Setup, PropertySets, Mapeamento }

        public const string Seu = "seu", DoForma = "Forma online", Instalado = "instalado";

        public class Arquivo
        {
            public string Classe { get; set; }
            public Tipo Tipo { get; set; }
            /// <summary>O que vale para a exportação (o primeiro que existe na ordem seu › Forma › instalado).</summary>
            public string Caminho { get; set; }
            public string Origem { get; set; }
            /// <summary>Há um do Forma ou instalado por baixo da cópia do usuário.</summary>
            public bool TemPadrao { get; set; }
            public bool DoUsuario => Origem == Seu;
            public string Nome => Path.GetFileName(Caminho);
            public override string ToString() => Classe;
        }

        public static string Sub(Tipo t) =>
            t == Tipo.Setup ? FormaPaths.SubSetups : t == Tipo.PropertySets ? FormaPaths.SubPropertySets : FormaPaths.SubMapeamentos;

        public static string Extensao(Tipo t) => t == Tipo.Setup ? ".json" : ".txt";

        public static string Descricao(Tipo t) =>
            t == Tipo.Setup ? "Setup" : t == Tipo.PropertySets ? "Property sets" : "Mapeamento";

        /// <summary>Onde fica a cópia do usuário (a que "Salvar" grava).</summary>
        public static string Destino(Tipo t, string classe) =>
            Path.Combine(ClassesIfc.PastaUsuario, Sub(t), classe + Extensao(t));

        public static string PastaAnteriores => Path.Combine(ClassesIfc.PastaUsuario, "Anteriores");

        /// <summary>Arquivos de um tipo, um por classe, com o que vale e de onde vem.</summary>
        public static List<Arquivo> Listar(Tipo t)
        {
            var nomes = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (string raiz in ClassesIfc.RaizesEmUso)
            {
                try
                {
                    string pasta = Path.Combine(raiz, Sub(t));
                    if (!Directory.Exists(pasta)) continue;
                    foreach (string f in Directory.EnumerateFiles(pasta, "*" + Extensao(t))) nomes.Add(Path.GetFileNameWithoutExtension(f));
                }
                catch { /* pasta inacessível */ }
            }
            return nomes.Select(n => Obter(t, n)).Where(a => a != null).ToList();
        }

        public static Arquivo Obter(Tipo t, string classe)
        {
            List<string> existentes = ClassesIfc.RaizesEmUso.Select(r => Path.Combine(r, Sub(t), classe + Extensao(t))).Where(File.Exists).ToList();
            if (existentes.Count == 0) return null;
            string origem = Origem(existentes[0]);
            return new Arquivo
            {
                Classe = classe,
                Tipo = t,
                Caminho = existentes[0],
                Origem = origem,
                TemPadrao = origem == Seu ? existentes.Count > 1 : false,
            };
        }

        public static string Origem(string caminho)
        {
            if (caminho == null) return null;
            if (caminho.StartsWith(ClassesIfc.PastaUsuario, StringComparison.OrdinalIgnoreCase)) return Seu;
            if (caminho.StartsWith(FormaOnline.PastaCache, StringComparison.OrdinalIgnoreCase)) return DoForma;
            return Instalado;
        }

        /// <summary>Tipo pelo conteúdo: .json com IFCVersion é setup; .txt com "PropertySet:" é property set; outro .txt é mapeamento.</summary>
        public static Tipo? Detectar(string arquivo, out string motivo)
        {
            motivo = null;
            string ext = Path.GetExtension(arquivo).ToLowerInvariant();
            try
            {
                if (ext == ".json")
                {
                    using (JsonDocument j = JsonDocument.Parse(File.ReadAllText(arquivo), OpcoesLeitura))
                    {
                        if (j.RootElement.ValueKind == JsonValueKind.Object && j.RootElement.EnumerateObject().Any(p => p.Name.Equals("IFCVersion", StringComparison.OrdinalIgnoreCase)))
                            return Tipo.Setup;
                    }
                    motivo = "não é um setup salvo pelo exportador IFC (sem IFCVersion)";
                    return null;
                }
                if (ext == ".txt")
                {
                    List<string> linhas = File.ReadLines(arquivo).Take(4000).ToList();
                    if (linhas.Any(l => l.TrimStart().StartsWith("PropertySet:", StringComparison.OrdinalIgnoreCase))) return Tipo.PropertySets;
                    if (linhas.Any(l => !l.TrimStart().StartsWith("#") && l.Split('\t').Count(c => c.Trim().Length > 0) >= 2)) return Tipo.Mapeamento;
                    motivo = "o .txt não tem property sets nem linhas de mapeamento separadas por tab";
                    return null;
                }
            }
            catch (Exception ex)
            {
                motivo = "não foi possível ler: " + ex.Message;
                return null;
            }
            motivo = "só .json e .txt";
            return null;
        }

        /// <summary>Classe pelo nome do arquivo: o nome inteiro de uma classe existente, um trecho que seja classe, ou o próprio nome.</summary>
        public static string ClasseSugerida(string arquivo, IEnumerable<string> classes)
        {
            string nome = Path.GetFileNameWithoutExtension(arquivo).Trim();
            List<string> lista = classes.ToList();
            string igual = lista.FirstOrDefault(c => string.Equals(c, nome, StringComparison.CurrentCultureIgnoreCase));
            if (igual != null) return igual;
            foreach (string trecho in nome.Split(new[] { '.', '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string c = lista.FirstOrDefault(x => string.Equals(x, trecho, StringComparison.CurrentCultureIgnoreCase));
                if (c != null) return c;
            }
            return nome;
        }

        /// <summary>Nome de classe aceito como nome de arquivo.</summary>
        public static bool NomeValido(string classe) =>
            !string.IsNullOrWhiteSpace(classe) && classe.Trim().IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

        /// <summary>Copia o arquivo como a cópia do usuário da classe. Retorna onde ficou a versão anterior (ou null).</summary>
        public static string Gravar(string origem, Tipo t, string classe)
        {
            string destino = Destino(t, classe.Trim());
            if (string.Equals(Path.GetFullPath(origem), Path.GetFullPath(destino), StringComparison.OrdinalIgnoreCase)) return null;
            string anterior = GuardarAnterior(destino);
            Directory.CreateDirectory(Path.GetDirectoryName(destino));
            File.Copy(origem, destino, true);
            return anterior;
        }

        /// <summary>Grava o texto como a cópia do usuário (guardando a anterior).</summary>
        public static string GravarTexto(Tipo t, string classe, string texto, bool bom)
        {
            string destino = Destino(t, classe);
            string anterior = GuardarAnterior(destino);
            Directory.CreateDirectory(Path.GetDirectoryName(destino));
            File.WriteAllText(destino, texto, new UTF8Encoding(bom));
            return anterior;
        }

        /// <summary>Apaga a cópia do usuário (a classe volta ao Forma ou ao instalado, se houver). A cópia fica em Anteriores.</summary>
        public static string ApagarDoUsuario(Tipo t, string classe)
        {
            string destino = Destino(t, classe);
            if (!File.Exists(destino)) return null;
            string anterior = GuardarAnterior(destino);
            File.Delete(destino);
            return anterior;
        }

        /// <summary>Troca a classe da cópia do usuário (renomeia o arquivo).</summary>
        public static void MudarClasse(Tipo t, string classe, string nova)
        {
            string de = Destino(t, classe), para = Destino(t, nova.Trim());
            if (string.Equals(de, para, StringComparison.OrdinalIgnoreCase)) return;
            GuardarAnterior(para);
            if (File.Exists(para)) File.Delete(para);
            File.Move(de, para);
        }

        internal static string GuardarAnterior(string arquivo)
        {
            if (!File.Exists(arquivo)) return null;
            string pasta = Path.Combine(PastaAnteriores, DateTime.Now.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture),
                Path.GetFileName(Path.GetDirectoryName(arquivo)));
            Directory.CreateDirectory(pasta);
            string copia = Path.Combine(pasta, Path.GetFileName(arquivo));
            File.Copy(arquivo, copia, true);
            return copia;
        }

        internal static readonly JsonDocumentOptions OpcoesLeitura =
            new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
    }

    /// <summary>
    /// As opções principais de um setup .json, para ver e trocar sem abrir o exportador do Revit. Só as chaves
    /// mostradas mudam; o resto do arquivo é regravado como estava.
    /// </summary>
    public class OpcoesSetup
    {
        private readonly JsonObject _json;

        public string Arquivo { get; }

        private OpcoesSetup(string arquivo, JsonObject json)
        {
            Arquivo = arquivo;
            _json = json;
        }

        public static OpcoesSetup Ler(string arquivo)
        {
            JsonNode n = JsonNode.Parse(File.ReadAllText(arquivo), documentOptions: ArquivosIfc.OpcoesLeitura);
            if (!(n is JsonObject o) || !Tem(o, "IFCVersion"))
                throw new InvalidDataException("O arquivo não é um setup salvo pelo exportador IFC (sem IFCVersion).");
            return new OpcoesSetup(arquivo, o);
        }

        /// <summary>Versões que a exportação MetroSP oferece, na ordem da lista.</summary>
        public static readonly (string Nome, string Texto)[] Versoes =
        {
            ("IFC4RV", "IFC4 Reference View"),
            ("IFC4DTV", "IFC4 Design Transfer View"),
            ("IFC4", "IFC4 Coordination View 2.0"),
            ("IFC4x3", "IFC 4.3"),
            ("IFC2x3CV2", "IFC 2x3 Coordination View 2.0"),
        };

        /// <summary>Nome da versão (IFC4RV...), lido como número ou como texto.</summary>
        public string Versao
        {
            get
            {
                JsonNode v = Valor("IFCVersion");
                if (v is JsonValue jv)
                {
                    if (jv.TryGetValue(out int n)) return Enum.GetName(typeof(IFCVersion), n) ?? n.ToString(CultureInfo.InvariantCulture);
                    if (jv.TryGetValue(out string s)) return s;
                }
                return null;
            }
            set
            {
                if (!Enum.TryParse(value, true, out IFCVersion e)) return;
                JsonNode atual = Valor("IFCVersion");
                bool numero = atual is JsonValue jv && jv.TryGetValue(out int _);
                Mudar("IFCVersion", numero ? JsonValue.Create((int)e) : JsonValue.Create(e.ToString()));
            }
        }

        public static bool VersaoExiste(string nome) => Enum.TryParse(nome, true, out IFCVersion _) && Enum.IsDefined(typeof(IFCVersion), Enum.Parse(typeof(IFCVersion), nome, true));

#if REVIT_ANTES_2023
        /// <summary>Revit 2022: o exportador vai só até o IFC4, então o IFC 2x3 também é aceito.</summary>
        public const bool Aceita2x3 = true;
        public const string VersoesAceitas = "IFC4 ou IFC 2x3";
#else
        public const bool Aceita2x3 = false;
        public const string VersoesAceitas = "IFC4 ou IFC 4.3";
#endif

        /// <summary>IFC4 (CV 2.0, RV, DTV) ou IFC 4.3. O IFC 2x3 só no Revit 2022 (COBie fica de fora).</summary>
        public static bool VersaoAceita(string versao) =>
            !string.IsNullOrEmpty(versao) &&
            (versao.StartsWith("IFC4", StringComparison.OrdinalIgnoreCase) ||
             (Aceita2x3 && versao.StartsWith("IFC2x3", StringComparison.OrdinalIgnoreCase)));

        public bool EhVersaoAceita => VersaoAceita(Versao);

        /// <summary>0 = nenhum, 1 = 1º nível, 2 = 2º nível.</summary>
        public int SpaceBoundaries { get => Inteiro("SpaceBoundaries"); set => Mudar("SpaceBoundaries", JsonValue.Create(value)); }
        public bool DividirPorNivel { get => Bool("SplitWallsAndColumns"); set => MudarBool("SplitWallsAndColumns", value); }
        public bool SoVisiveis { get => Bool("VisibleElementsOfCurrentView"); set => MudarBool("VisibleElementsOfCurrentView", value); }
        public bool Quantidades { get => Bool("ExportBaseQuantities"); set => MudarBool("ExportBaseQuantities", value); }
        public bool GravaGuid { get => Bool("StoreIFCGUID"); set => MudarBool("StoreIFCGUID", value); }
        public bool PsetsComuns { get => Bool("ExportIFCCommonPropertySets"); set => MudarBool("ExportIFCCommonPropertySets", value); }
        public bool PsetsDoRevit { get => Bool("ExportInternalRevitPropertySets"); set => MudarBool("ExportInternalRevitPropertySets", value); }
        public bool PsetsDeMaterial { get => Bool("ExportMaterialPsets"); set => MudarBool("ExportMaterialPsets", value); }

        /// <summary>Id da fase (-1 = fase padrão da vista/modelo).</summary>
        public long Fase
        {
            get
            {
                JsonNode v = Valor("ActivePhaseId");
                if (v is JsonObject o)   // ElementId gravado como objeto ({"IntegerValue": n} no Revit 2022)
                    v = o[Chave(o, "Value") ?? Chave(o, "IntegerValue") ?? "Value"];
                return v is JsonValue jv && jv.TryGetValue(out long n) ? n : -1;
            }
            set
            {
                // Mantém o formato do exportador que salvou o setup (objeto ou número)
                if (Valor("ActivePhaseId") is JsonObject o)
                {
                    var novo = (JsonObject)o.DeepClone();
                    foreach (string k in new[] { "Value", "IntegerValue" })
                    {
                        string real = Chave(novo, k);
                        if (real != null) novo[real] = value;
                    }
                    Mudar("ActivePhaseId", novo);
                }
                else Mudar("ActivePhaseId", JsonValue.Create(value));
            }
        }

        public string ArquivoPsets => Texto("ExportUserDefinedPsetsFileName");
        public string ArquivoMapeamento => Texto("ExportUserDefinedParameterMappingFileName");

        public bool Alterado { get; private set; }

        public void Salvar(string destino)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destino));
            File.WriteAllText(destino, _json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            Alterado = false;
        }

        public string Texto(string chave) => Valor(chave) is JsonValue v && v.TryGetValue(out string s) ? s : null;

        private bool Bool(string chave) => Valor(chave) is JsonValue v && v.TryGetValue(out bool b) && b;

        private int Inteiro(string chave) => Valor(chave) is JsonValue v && v.TryGetValue(out int n) ? n : 0;

        private JsonNode Valor(string chave)
        {
            string real = Chave(_json, chave);
            return real == null ? null : _json[real];
        }

        private void MudarBool(string chave, bool valor) => Mudar(chave, JsonValue.Create(valor));

        private void Mudar(string chave, JsonNode novo)
        {
            string real = Chave(_json, chave) ?? chave;
            if (JsonNode.DeepEquals(_json[real], novo)) return;
            _json[real] = novo;
            Alterado = true;
        }

        private static bool Tem(JsonObject o, string chave) => Chave(o, chave) != null;

        private static string Chave(JsonObject o, string chave) =>
            o.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, chave, StringComparison.OrdinalIgnoreCase));
    }
}
