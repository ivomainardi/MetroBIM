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
    /// Pacote de configuração IFC ativo do usuário (%APPDATA%\MetroSP\Config). Vem pré-carregado com o
    /// padrão MetroSP (pasta Padrao do bundle), pode ser trocado por arquivos do Forma ou importados,
    /// e é copiado para a pasta do desenho antes de cada exportação, no formato que o IFC Extension lê.
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

        public static string ActiveDir => Path.Combine(Settings.UserDir, "Config");
        public static string DefaultDir => Path.Combine(Settings.BundleDir, "Padrao");
        public static string PathOf(ConfigKind kind) => Path.Combine(ActiveDir, FileNames[kind]);

        /// <summary>Cria o pacote ativo a partir do padrão MetroSP na primeira vez.</summary>
        public static void EnsureActive()
        {
            if (Directory.Exists(ActiveDir) && Directory.EnumerateFiles(ActiveDir).Any()) return;
            RestoreDefaults();
        }

        public static void RestoreDefaults()
        {
            Directory.CreateDirectory(ActiveDir);
            foreach (var f in Directory.EnumerateFiles(ActiveDir)) File.Delete(f);
            if (!Directory.Exists(DefaultDir)) return;
            foreach (var f in Directory.EnumerateFiles(DefaultDir))
                File.Copy(f, Path.Combine(ActiveDir, Path.GetFileName(f)), overwrite: true);
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
