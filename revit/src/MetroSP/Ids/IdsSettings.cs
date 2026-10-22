using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MetroSP.Ids
{
    /// <summary>Lembra os últimos arquivos usados (em %AppData%\MetroSP\ids.ini).</summary>
    public class IdsSettings
    {
        private static readonly string FilePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MetroSP", "ids.ini");

        public string IdsFile { get; set; }
        public string PropertySetsFile { get; set; }
        public string CategoryMappingFile { get; set; }
        public bool CheckValues { get; set; } = true;

        public static IdsSettings Load()
        {
            var s = new IdsSettings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                Dictionary<string, string> d = File.ReadAllLines(FilePath)
                    .Select(l => l.Split(new[] { '=' }, 2))
                    .Where(p => p.Length == 2)
                    .ToDictionary(p => p[0], p => p[1]);
                d.TryGetValue("ids", out string ids); s.IdsFile = ids;
                d.TryGetValue("psets", out string psets); s.PropertySetsFile = psets;
                d.TryGetValue("categorias", out string cat); s.CategoryMappingFile = cat;
                if (d.TryGetValue("valores", out string v)) s.CheckValues = v != "0";
            }
            catch { /* configuração corrompida: usa padrão */ }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, new[]
                {
                    "ids=" + IdsFile,
                    "psets=" + PropertySetsFile,
                    "categorias=" + CategoryMappingFile,
                    "valores=" + (CheckValues ? "1" : "0"),
                });
            }
            catch { /* não é crítico */ }
        }
    }
}
