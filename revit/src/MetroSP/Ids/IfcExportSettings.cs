using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MetroSP.Ids
{
    /// <summary>De onde vêm os arquivos de configuração da exportação IFC.</summary>
    public enum OrigemConfiguracao
    {
        /// <summary>Pasta no computador ou na rede.</summary>
        Local,
        /// <summary>Pasta do Autodesk Forma (Autodesk Docs) sincronizada pelo Desktop Connector.</summary>
        Forma,
    }

    /// <summary>Lembra as escolhas da exportação IFC (em %AppData%\MetroSP\ifc_export.ini).</summary>
    public class IfcExportSettings
    {
        private static readonly string FilePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MetroSP", "ifc_export.ini");

        public OrigemConfiguracao Origem { get; set; } = OrigemConfiguracao.Local;
        /// <summary>Pasta de configurações quando a origem é o computador.</summary>
        public string PastaLocal { get; set; }
        /// <summary>Pasta do projeto no Desktop Connector (ACCDocs\Hub\Projeto).</summary>
        public string ProjetoForma { get; set; }
        /// <summary>Pasta padrão das configurações dentro de cada projeto do Forma.</summary>
        public string PastaPadraoForma { get; set; } = FormaPaths.PastaPadraoInicial;
        /// <summary>Último setup .json usado.</summary>
        public string Setup { get; set; }
        /// <summary>Último .txt de property sets escolhido à mão (vazio = o do setup).</summary>
        public string PropertySets { get; set; }
        public string PastaSaida { get; set; }

        /// <summary>Pasta onde procurar setups e .txt, conforme a origem.</summary>
        public string PastaConfiguracoes =>
            Origem == OrigemConfiguracao.Local ? PastaLocal
            : string.IsNullOrEmpty(ProjetoForma) ? null
            : Path.Combine(ProjetoForma, PastaPadraoForma ?? string.Empty);

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
                if (d.TryGetValue("origem", out string origem) && origem == "forma") s.Origem = OrigemConfiguracao.Forma;
                d.TryGetValue("pasta_local", out string local); s.PastaLocal = local;
                d.TryGetValue("projeto_forma", out string projeto); s.ProjetoForma = projeto;
                if (d.TryGetValue("pasta_padrao_forma", out string padrao) && !string.IsNullOrWhiteSpace(padrao)) s.PastaPadraoForma = padrao;
                d.TryGetValue("setup", out string setup); s.Setup = setup;
                d.TryGetValue("psets", out string psets); s.PropertySets = psets;
                d.TryGetValue("saida", out string saida); s.PastaSaida = saida;
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
                    "origem=" + (Origem == OrigemConfiguracao.Forma ? "forma" : "local"),
                    "pasta_local=" + PastaLocal,
                    "projeto_forma=" + ProjetoForma,
                    "pasta_padrao_forma=" + PastaPadraoForma,
                    "setup=" + Setup,
                    "psets=" + PropertySets,
                    "saida=" + PastaSaida,
                });
            }
            catch { /* não é crítico */ }
        }
    }
}
