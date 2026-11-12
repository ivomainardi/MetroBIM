using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MetroSP
{
    /// <summary>
    /// Configuração do plugin (MetroSP.settings.json).
    /// Procurada primeiro em %APPDATA%\MetroSP\ (configuração do usuário)
    /// e depois na pasta Contents do bundle (padrão da empresa).
    /// </summary>
    public class Settings
    {
        /// <summary>Client ID do app APS (tipo "Desktop, Mobile, Single-Page App", com PKCE).</summary>
        public string ClientId { get; set; } = "";

        /// <summary>Callback registrado no app APS. Precisa ser idêntico ao cadastrado.</summary>
        public string CallbackUrl { get; set; } = "http://localhost:8765/metrosp/callback";

        /// <summary>ID do projeto no Forma/Docs. Aceita o GUID puro ou já com prefixo "b.".</summary>
        public string ProjectId { get; set; } = "";

        /// <summary>URN da pasta com os JSON (ex.: urn:adsk.wipprod:fs.folder:co.XXXX). Vem da URL da pasta no navegador.</summary>
        public string FolderUrn { get; set; } = "";

        /// <summary>Arquivos a baixar. Vazio = todos os .json e .csv da pasta.</summary>
        public List<string> Files { get; set; } = new List<string>
        {
            "IfcInfraConfiguration.json",
            "IfcInfraExportMapping.json",
            "IFCInfraExportPropertyMapping.json",
            "IFCInfraExportPropertyMapping.csv",
            "MetroSP.ifc.json"
        };

        /// <summary>
        /// Onde gravar os arquivos:
        ///  "PorDesenho" = [pasta do DWG]\[nome do DWG]-IfcInfraConfiguration.json (mais específico, recomendado);
        ///  "PastaDesenho" = [pasta do DWG]\IfcInfraConfiguration.json;
        ///  "ProgramData" = C:\ProgramData\Autodesk\Civil3D\ (vale para todos os desenhos da máquina).
        /// </summary>
        public string Destino { get; set; } = "PorDesenho";

        /// <summary>Comando do IFC Extension chamado depois da sincronização.</summary>
        public string ExportCommand { get; set; } = "IFCINFRAEXPORT";
        public string ExportSelectedCommand { get; set; } = "IFCINFRAEXPORTSELECTED";

        /// <summary>Caminho oficial: atualizações na conta Autodesk, onde fica a IFC 4.3 Extension.</summary>
        public string ExtensaoAutodeskUrl { get; set; } = "https://manage.autodesk.com/products/updates";

        /// <summary>Caminho GitHub: versões publicadas do plugin MetroSP.</summary>
        public string VersoesMetroSpUrl { get; set; } = "https://github.com/ivomainardi/MetroSP_Civil3D/releases";

        /// <summary>
        /// De onde vem o pacote de configuração IFC antes de exportar:
        ///  "Automatico" = Forma se ClientId/ProjectId/FolderUrn estiverem preenchidos, senão o pacote local;
        ///  "Forma" = sempre baixa do Forma; "Local" = usa só o pacote local (editado na interface).
        /// </summary>
        public string FonteConfiguracao { get; set; } = "Automatico";

        [JsonIgnore]
        public bool UsesForma =>
            string.Equals(FonteConfiguracao, "Forma", StringComparison.OrdinalIgnoreCase) ||
            (string.Equals(FonteConfiguracao, "Automatico", StringComparison.OrdinalIgnoreCase) &&
             !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ProjectId) && !string.IsNullOrWhiteSpace(FolderUrn));

        [JsonIgnore] public string LoadedFrom { get; private set; }

        static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true
        };

        public const string FileName = "MetroSP.settings.json";

        public static string UserDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MetroSP");

        /// <summary>Pasta Contents do bundle (a DLL fica em Contents\R25 ou Contents\R26).</summary>
        public static string BundleDir
        {
            get
            {
                var dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (File.Exists(Path.Combine(dllDir, FileName))) return dllDir;
                return Directory.GetParent(dllDir)?.FullName ?? dllDir;
            }
        }

        public static string UserFile => Path.Combine(UserDir, FileName);

        public static Settings Load()
        {
            foreach (var path in new[] { UserFile, Path.Combine(BundleDir, FileName) })
            {
                if (!File.Exists(path)) continue;
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), JsonOpts) ?? new Settings();
                s.LoadedFrom = path;
                return s;
            }
            return new Settings();
        }

        /// <summary>Garante que exista uma cópia editável em %APPDATA%\MetroSP e devolve o caminho.</summary>
        public static string EnsureUserFile()
        {
            if (File.Exists(UserFile)) return UserFile;
            Directory.CreateDirectory(UserDir);
            var bundled = Path.Combine(BundleDir, FileName);
            if (File.Exists(bundled)) File.Copy(bundled, UserFile);
            else File.WriteAllText(UserFile, JsonSerializer.Serialize(new Settings(), JsonOpts));
            return UserFile;
        }

        public string DmProjectId =>
            ProjectId.StartsWith("b.", StringComparison.OrdinalIgnoreCase) ? ProjectId : "b." + ProjectId;

        public void Validate()
        {
            var faltando = new List<string>();
            if (string.IsNullOrWhiteSpace(ClientId)) faltando.Add("ClientId");
            if (string.IsNullOrWhiteSpace(ProjectId)) faltando.Add("ProjectId");
            if (string.IsNullOrWhiteSpace(FolderUrn)) faltando.Add("FolderUrn");
            if (faltando.Count > 0)
                throw new InvalidOperationException(
                    "Preencha em " + (LoadedFrom ?? UserFile) + ": " + string.Join(", ", faltando) +
                    ". Use o botão \"Configurações\" na aba MetroSP.");
        }
    }
}
