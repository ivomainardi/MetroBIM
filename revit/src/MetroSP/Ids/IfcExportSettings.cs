using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MetroSP.Ids
{
    /// <summary>Lembra as escolhas da exportação IFC (em %AppData%\MetroSP\ifc_export.ini).</summary>
    public class IfcExportSettings
    {
        private static readonly string FilePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MetroSP", "ifc_export.ini");

        /// <summary>Última classe instalada escolhida (null = arquivos indicados à mão).</summary>
        public string Classe { get; set; }
        /// <summary>Último setup .json escolhido.</summary>
        public string Setup { get; set; }
        /// <summary>Último .txt de property sets escolhido.</summary>
        public string PropertySets { get; set; }
        /// <summary>Último .txt de mapeamento de parâmetros escolhido (opcional).</summary>
        public string Mapeamento { get; set; }
        public string PastaSaida { get; set; }
        /// <summary>Sempre exportar os eixos (Grids), mesmo exportando só o visível numa vista 3D.</summary>
        public bool SempreEixos { get; set; } = true;
        /// <summary>Elementos: "Modelo inteiro" ou o nome da vista 3D (vale se a vista existir no modelo).</summary>
        public string Vista { get; set; }
        /// <summary>Exportar com o EPSG em branco.</summary>
        public bool EpsgEmBranco { get; set; } = true;
        /// <summary>Nome do último .ifc, sem extensão; "{modelo}" quando era o nome do modelo.</summary>
        public string NomeArquivo { get; set; }

        public static IfcExportSettings Load()
        {
            var s = new IfcExportSettings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                Dictionary<string, string> d = File.ReadAllLines(FilePath)
                    .Select(l => l.Split(new[] { '=' }, 2))
                    .Where(p => p.Length == 2)
                    .GroupBy(p => p[0])
                    .ToDictionary(g => g.Key, g => g.Last()[1]);
                d.TryGetValue("classe", out string classe); s.Classe = string.IsNullOrEmpty(classe) ? null : classe;
                d.TryGetValue("setup", out string setup); s.Setup = setup;
                d.TryGetValue("psets", out string psets); s.PropertySets = psets;
                d.TryGetValue("mapeamento", out string mapeamento); s.Mapeamento = mapeamento;
                d.TryGetValue("saida", out string saida); s.PastaSaida = saida;
                if (d.TryGetValue("eixos", out string eixos)) s.SempreEixos = eixos != "0";
                if (d.TryGetValue("vista", out string vista) && vista.Length > 0) s.Vista = vista;
                if (d.TryGetValue("epsg", out string epsg)) s.EpsgEmBranco = epsg != "0";
                if (d.TryGetValue("nome", out string nome) && nome.Length > 0) s.NomeArquivo = nome;
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
                    "classe=" + Classe,
                    "setup=" + Setup,
                    "psets=" + PropertySets,
                    "mapeamento=" + Mapeamento,
                    "saida=" + PastaSaida,
                    "eixos=" + (SempreEixos ? "1" : "0"),
                    "vista=" + Vista,
                    "epsg=" + (EpsgEmBranco ? "1" : "0"),
                    "nome=" + NomeArquivo,
                });
            }
            catch { /* não é crítico */ }
        }
    }
}
