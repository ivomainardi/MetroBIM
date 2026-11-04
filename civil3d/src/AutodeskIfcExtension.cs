using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace MetroSP
{
    /// <summary>
    /// Localiza a "IFC 4.3 Extension for Civil 3D" da Autodesk, que fornece IFCINFRAEXPORT.
    /// Ela não vem com o Civil 3D: é baixada na conta Autodesk e instalada como bundle em ApplicationPlugins.
    /// </summary>
    public static class AutodeskIfcExtension
    {
        public const string DownloadsUrl = "https://manage.autodesk.com/products/updates";

        public class Info
        {
            public string Name;
            public string Version;
            public string Folder;
        }

        /// <summary>O comando está registrado agora (carregado ou com carga sob demanda).</summary>
        public static bool CommandDefined(string command)
        {
            try { return Autodesk.AutoCAD.Internal.Utils.IsCommandDefined(command); }
            catch { return false; }
        }

        /// <summary>Bundles instalados cujo PackageContents fala de IFC e Civil/Infra (o nome do bundle muda por versão).</summary>
        public static List<Info> FindInstalled()
        {
            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            }.Select(r => Path.Combine(r, "Autodesk", "ApplicationPlugins")).Where(Directory.Exists);

            var found = new List<Info>();
            foreach (var bundle in roots.SelectMany(Directory.EnumerateDirectories))
            {
                var pc = Path.Combine(bundle, "PackageContents.xml");
                if (!File.Exists(pc)) continue;
                string text;
                try { text = File.ReadAllText(pc); }
                catch { continue; }
                var looksIfc = text.IndexOf("IFCINFRAEXPORT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               (text.IndexOf("IFC", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                (text.IndexOf("Civil", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Infra", StringComparison.OrdinalIgnoreCase) >= 0));
                if (!looksIfc || bundle.EndsWith("MetroSP.bundle", StringComparison.OrdinalIgnoreCase)) continue;

                var info = new Info { Folder = bundle, Name = Path.GetFileNameWithoutExtension(bundle) };
                try
                {
                    var root = XDocument.Parse(text).Root;
                    info.Name = (string)root?.Attribute("Name") ?? info.Name;
                    info.Version = (string)root?.Attribute("AppVersion");
                }
                catch { }
                found.Add(info);
            }
            return found;
        }

        /// <summary>Resumo de uma linha para mensagens.</summary>
        public static string Describe(string command)
        {
            var installed = FindInstalled();
            if (installed.Count > 0)
                return string.Join("; ", installed.Select(i => i.Name + (i.Version != null ? " " + i.Version : "")));
            return CommandDefined(command) ? "carregada" : "não encontrada";
        }
    }
}
