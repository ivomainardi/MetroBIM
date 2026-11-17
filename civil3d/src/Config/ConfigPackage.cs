using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MetroSP.Ifc;

namespace MetroSP.Config
{
    public enum ConfigKind
    {
        Unknown,
        AutodeskConfig,        // IfcInfraConfiguration.json
        ObjectMapping,         // IfcInfraExportMapping.json
        PropertyTemplates,     // IFCInfraExportPropertyMapping.json
        PropertyMappingCsv,    // IFCInfraExportPropertyMapping.csv
        MetroSP                // MetroSP.ifc.json
    }

    /// <summary>Opções MetroSP do pós-processamento (arquivo MetroSP.ifc.json).</summary>
    public class MetroSpIfcConfig
    {
        public SpatialConfig Estrutura { get; set; } = new SpatialConfig();
        public AlignmentOptions Alinhamento { get; set; } = new AlignmentOptions();

        /// <summary>Gravadas como Pset "MetroSP_Projeto" no IfcProject (linha, lote, contrato...).</summary>
        public Dictionary<string, string> PropriedadesProjeto { get; set; } = new Dictionary<string, string>();
    }

    public class AlignmentOptions
    {
        /// <summary>Depois do IFCINFRAEXPORT, troca o eixo dos IfcAlignment pelo eixo 3D com as cotas do perfil.</summary>
        public bool AplicarNoIfc { get; set; } = true;
        public double Passo { get; set; } = 1.0;

        /// <summary>Expressões regulares, em ordem de preferência, para escolher o perfil quando há mais de um.</summary>
        public List<string> PerfisPreferidos { get; set; } = new List<string>();
    }

    /// <summary>
    /// Perfis de exportação IFC (%APPDATA%\MetroSP\Perfis\<nome>). O MetroSP vem pré-carregado (pasta Padrao do bundle);
    /// cada modelo escolhe o seu, que pode ser trocado por arquivos do Forma ou importados e é copiado para a pasta do
    /// modelo antes de cada exportação, no formato que o IFC Extension lê.
    /// </summary>
    public static class ConfigPackage
    {
        public static readonly Dictionary<ConfigKind, string> FileNames = new Dictionary<ConfigKind, string>
        {
            [ConfigKind.AutodeskConfig] = "IfcInfraConfiguration.json",
            [ConfigKind.ObjectMapping] = "IfcInfraExportMapping.json",
            [ConfigKind.PropertyTemplates] = "IFCInfraExportPropertyMapping.json",
            [ConfigKind.PropertyMappingCsv] = "IFCInfraExportPropertyMapping.csv",
            [ConfigKind.MetroSP] = "MetroSP.ifc.json"
        };

        public static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        static readonly JsonDocumentOptions DocOpts = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        // ------------------------------------------------------------------ perfis de exportação
        // Cada perfil é uma pasta em %AppData%\MetroSP\Perfis\<nome> com os arquivos do exportador e o MetroSP.ifc.json.
        // Os pré-configurados vêm no bundle (MetroSP = pasta Padrao; outros em Perfis\<nome>) e são copiados na primeira vez.
        // O modelo guarda o perfil escolhido numa propriedade do DWG (Arquivo › Propriedades do desenho › Personalizado).

        public const string PerfilMetroSP = "MetroSP";
        public const string PropriedadePerfil = "MetroSP.Perfil";
        public static string PerfisDir => Path.Combine(Settings.UserDir, "Perfis");
        static string LegacyDir => Path.Combine(Settings.UserDir, "Config");
        static string BundlePerfisDir => Path.Combine(Settings.BundleDir, "Perfis");
        public static string DefaultDir => Path.Combine(Settings.BundleDir, "Padrao");

        /// <summary>Pasta do perfil do modelo ativo (ou do MetroSP, se o modelo não escolheu ou o perfil não existe aqui).</summary>
        public static string ActiveDir { get { Preparar(); return DirDe(PerfilAtivo); } }
        public static string DirDe(string perfil) => Path.Combine(PerfisDir, perfil);
        public static string PathOf(ConfigKind kind) => Path.Combine(ActiveDir, FileNames[kind]);

        /// <summary>Perfil que vale para o modelo ativo.</summary>
        public static string PerfilAtivo
        {
            get
            {
                string n = PerfilDoModelo();
                return n != null && Existe(n) ? n : PerfilMetroSP;
            }
        }

        public static bool Existe(string perfil) => !string.IsNullOrWhiteSpace(perfil) && Directory.Exists(DirDe(perfil));

        /// <summary>Pasta do bundle de onde o perfil veio (null se foi criado pelo usuário).</summary>
        public static string OrigemPreConfigurada(string perfil)
        {
            if (string.Equals(perfil, PerfilMetroSP, StringComparison.OrdinalIgnoreCase)) return DefaultDir;
            string d = Path.Combine(BundlePerfisDir, perfil ?? "");
            return Directory.Exists(d) ? d : null;
        }

        public static bool PreConfigurado(string perfil) => OrigemPreConfigurada(perfil) != null;

        /// <summary>Perfis deste computador, MetroSP primeiro.</summary>
        public static List<string> Perfis()
        {
            Preparar();
            return Directory.EnumerateDirectories(PerfisDir).Select(Path.GetFileName)
                .OrderBy(n => string.Equals(n, PerfilMetroSP, StringComparison.OrdinalIgnoreCase) ? 0 : PreConfigurado(n) ? 1 : 2)
                .ThenBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>Cria a pasta de perfis: o MetroSP (do Config antigo, se houver) e os pré-configurados que faltam.</summary>
        static bool _preparado;
        static void Preparar()
        {
            if (_preparado && Directory.Exists(DirDe(PerfilMetroSP))) return;
            _preparado = true;
            Directory.CreateDirectory(PerfisDir);
            string metro = DirDe(PerfilMetroSP);
            if (!Directory.Exists(metro))
            {
                bool antigo = Directory.Exists(LegacyDir) && Directory.EnumerateFiles(LegacyDir).Any();
                Copiar(antigo ? LegacyDir : DefaultDir, metro);
            }
            if (Directory.Exists(BundlePerfisDir))
                foreach (string d in Directory.EnumerateDirectories(BundlePerfisDir))
                {
                    string destino = DirDe(Path.GetFileName(d));
                    if (!Directory.Exists(destino)) Copiar(d, destino);
                }
        }

        static void Copiar(string de, string para)
        {
            Directory.CreateDirectory(para);
            if (!Directory.Exists(de)) return;
            foreach (var f in Directory.EnumerateFiles(de))
                File.Copy(f, Path.Combine(para, Path.GetFileName(f)), overwrite: true);
        }

        /// <summary>Nome aceitável para pasta; devolve o motivo se não for.</summary>
        public static string NomeInvalido(string nome)
        {
            if (string.IsNullOrWhiteSpace(nome)) return "Digite um nome.";
            if (nome.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || nome.Trim() != nome || nome.EndsWith("."))
                return "O nome não pode ter \\ / : * ? \" < > | nem começar ou terminar com espaço.";
            if (Existe(nome)) return "Já existe um perfil com esse nome.";
            return null;
        }

        public static void Duplicar(string origem, string novo)
        {
            string erro = NomeInvalido(novo);
            if (erro != null) throw new InvalidOperationException(erro);
            Copiar(DirDe(origem), DirDe(novo));
        }

        public static void Renomear(string velho, string novo)
        {
            if (PreConfigurado(velho)) throw new InvalidOperationException("Perfil pré-configurado não muda de nome. Duplique e renomeie a cópia.");
            string erro = string.Equals(velho, novo, StringComparison.OrdinalIgnoreCase) ? null : NomeInvalido(novo);
            if (erro != null) throw new InvalidOperationException(erro);
            string tmp = DirDe(velho) + ".renomeando";
            Directory.Move(DirDe(velho), tmp);
            Directory.Move(tmp, DirDe(novo));
        }

        public static void Excluir(string perfil)
        {
            if (PreConfigurado(perfil)) throw new InvalidOperationException("Perfil pré-configurado não pode ser excluído.");
            Directory.Delete(DirDe(perfil), recursive: true);
        }

        /// <summary>Perfil gravado no modelo (null se não há).</summary>
        public static string PerfilDoModelo(Autodesk.AutoCAD.ApplicationServices.Document doc = null)
        {
            try
            {
                doc = doc ?? Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return null;
                var e = doc.Database.SummaryInfo.CustomProperties;
                while (e.MoveNext())
                    if (string.Equals(e.Key as string, PropriedadePerfil, StringComparison.OrdinalIgnoreCase))
                        return string.IsNullOrWhiteSpace(e.Value as string) ? null : ((string)e.Value).Trim();
            }
            catch { }
            return null;
        }

        /// <summary>Grava no modelo o perfil que vale para ele (fica salvo com o DWG).</summary>
        public static void UsarNoModelo(Autodesk.AutoCAD.ApplicationServices.Document doc, string perfil)
        {
            using (doc.LockDocument())
            {
                var db = doc.Database;
                var b = new Autodesk.AutoCAD.DatabaseServices.DatabaseSummaryInfoBuilder(db.SummaryInfo);
                b.CustomPropertyTable[PropriedadePerfil] = perfil ?? "";
                db.SummaryInfo = b.ToDatabaseSummaryInfo();
            }
        }

        /// <summary>Garante que o perfil ativo tem arquivos.</summary>
        public static void EnsureActive()
        {
            Preparar();
            if (Directory.Exists(ActiveDir) && Directory.EnumerateFiles(ActiveDir).Any()) return;
            RestoreDefaults();
        }

        /// <summary>Volta o perfil ativo ao pré-configurado de mesmo nome (ou ao MetroSP, se foi criado pelo usuário).</summary>
        public static void RestoreDefaults()
        {
            string dir = ActiveDir;
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.EnumerateFiles(dir)) File.Delete(f);
            Copiar(OrigemPreConfigurada(PerfilAtivo) ?? DefaultDir, dir);
        }

        public static IEnumerable<(ConfigKind Kind, string Path, DateTime Modified)> List()
        {
            EnsureActive();
            foreach (var kv in FileNames)
            {
                var p = Path.Combine(ActiveDir, kv.Value);
                if (File.Exists(p)) yield return (kv.Key, p, File.GetLastWriteTime(p));
            }
        }

        /// <summary>Reconhece o tipo pelo nome e, se preciso, pelo conteúdo.</summary>
        public static ConfigKind Detect(string path)
        {
            var name = System.IO.Path.GetFileName(path);
            foreach (var kv in FileNames)
                if (name.EndsWith(kv.Value, StringComparison.OrdinalIgnoreCase)) return kv.Key;
            if (name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) return ConfigKind.PropertyMappingCsv;
            if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return ConfigKind.Unknown;

            JsonNode node;
            try { node = JsonNode.Parse(File.ReadAllText(path), null, DocOpts); }
            catch { return ConfigKind.Unknown; }

            if (node is JsonArray) return ConfigKind.PropertyTemplates;
            if (node is JsonObject o)
            {
                if (o.ContainsKey("Estrutura") || o.ContainsKey("Alinhamento") || o.ContainsKey("PropriedadesProjeto")) return ConfigKind.MetroSP;
                if (o.Any(kv => kv.Key.StartsWith("Map", StringComparison.Ordinal))) return ConfigKind.ObjectMapping;
                return ConfigKind.AutodeskConfig;
            }
            return ConfigKind.Unknown;
        }

        /// <summary>Importa arquivos para o pacote ativo (com o nome padrão de cada tipo). Devolve o que entrou.</summary>
        public static List<string> Import(IEnumerable<string> paths)
        {
            EnsureActive();
            var done = new List<string>();
            foreach (var p in paths)
            {
                var kind = Detect(p);
                if (kind == ConfigKind.Unknown)
                    throw new InvalidOperationException(System.IO.Path.GetFileName(p) + ": tipo de configuração não reconhecido.");
                if (kind != ConfigKind.PropertyMappingCsv) ValidateJson(System.IO.Path.GetFileName(p), File.ReadAllBytes(p));
                var target = PathOf(kind);
                Backup(target);
                File.Copy(p, target, overwrite: true);
                done.Add($"{System.IO.Path.GetFileName(p)} → {FileNames[kind]}");
            }
            return done;
        }

        public static void Write(ConfigKind kind, byte[] data)
        {
            EnsureActive();
            if (kind != ConfigKind.PropertyMappingCsv) ValidateJson(FileNames[kind], data);
            var target = PathOf(kind);
            Backup(target);
            File.WriteAllBytes(target, data);
        }

        public static void ExportTo(string dir)
        {
            Directory.CreateDirectory(dir);
            foreach (var f in List()) File.Copy(f.Path, System.IO.Path.Combine(dir, System.IO.Path.GetFileName(f.Path)), overwrite: true);
        }

        public static MetroSpIfcConfig LoadMetroSp()
        {
            EnsureActive();
            var p = PathOf(ConfigKind.MetroSP);
            return File.Exists(p)
                ? JsonSerializer.Deserialize<MetroSpIfcConfig>(File.ReadAllText(p), JsonOpts) ?? new MetroSpIfcConfig()
                : new MetroSpIfcConfig();
        }

        public static void SaveMetroSp(MetroSpIfcConfig cfg) =>
            Write(ConfigKind.MetroSP, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cfg, JsonOpts)));

        public static JsonNode LoadJson(ConfigKind kind)
        {
            var p = PathOf(kind);
            return File.Exists(p) ? JsonNode.Parse(File.ReadAllText(p), null, DocOpts) : null;
        }

        public static void SaveJson(ConfigKind kind, JsonNode node) =>
            Write(kind, Encoding.UTF8.GetBytes(node.ToJsonString(JsonOpts)));

        public static void ValidateJson(string name, byte[] data)
        {
            try { using var _ = JsonDocument.Parse(data, DocOpts); }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"{name} não é um JSON válido (linha {ex.LineNumber + 1}): {ex.Message}");
            }
        }

        /// <summary>
        /// Copia os arquivos da Autodesk do pacote ativo para onde o IFC Extension procura.
        /// "PorDesenho" usa o prefixo [desenho]- nos dois arquivos que aceitam prefixo.
        /// </summary>
        public static List<string> ApplyToDrawing(string dwgDir, string dwgName, string destino, string programDataDir)
        {
            var written = new List<string>();
            foreach (var f in List().Where(f => f.Kind != ConfigKind.MetroSP))
            {
                var name = System.IO.Path.GetFileName(f.Path);
                string target;
                switch (destino)
                {
                    case "ProgramData": target = System.IO.Path.Combine(programDataDir, name); break;
                    case "PastaDesenho": target = System.IO.Path.Combine(dwgDir, name); break;
                    default:
                        var prefix = f.Kind == ConfigKind.AutodeskConfig || f.Kind == ConfigKind.ObjectMapping;
                        target = System.IO.Path.Combine(dwgDir, prefix ? dwgName + "-" + name : name);
                        break;
                }
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
                Backup(target, onlyIfDifferentFrom: f.Path);
                File.Copy(f.Path, target, overwrite: true);
                written.Add(target);
            }
            return written;
        }

        static void Backup(string target, string onlyIfDifferentFrom = null)
        {
            if (!File.Exists(target)) return;
            if (onlyIfDifferentFrom != null && File.ReadAllBytes(target).SequenceEqual(File.ReadAllBytes(onlyIfDifferentFrom))) return;
            File.Copy(target, target + ".bak", overwrite: true);
        }
    }
}
