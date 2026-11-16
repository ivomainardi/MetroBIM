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

        /// <summary>Padrão escolhido para cada modelo (pelo título do modelo).</summary>
        private readonly Dictionary<string, string> _padroes = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
        /// <summary>Marca de "nenhum padrão" escolhido para o modelo.</summary>
        public const string SemPadrao = "-";
        private const string PrefixoPadrao = "padrao:";
        private readonly HashSet<string> _padroesMudados = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

        /// <summary>Padrão escolhido para o modelo (null = nenhum escolhido; <see cref="SemPadrao"/> = escolheu nenhum).</summary>
        public string PadraoDo(string modelo) =>
            !string.IsNullOrWhiteSpace(modelo) && _padroes.TryGetValue(modelo.Trim(), out string p) ? p : null;

        public void GuardarPadrao(string modelo, string padrao)
        {
            if (string.IsNullOrWhiteSpace(modelo) || modelo.Contains("=")) return;
            if (string.IsNullOrWhiteSpace(padrao)) _padroes.Remove(modelo.Trim());
            else _padroes[modelo.Trim()] = padrao.Trim();
            _padroesMudados.Add(modelo.Trim());
        }

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
                foreach (KeyValuePair<string, string> kv in d)
                    if (kv.Key.StartsWith(PrefixoPadrao, StringComparison.Ordinal) && kv.Value.Length > 0)
                        s._padroes[kv.Key.Substring(PrefixoPadrao.Length)] = kv.Value;
            }
            catch { /* configuração corrompida: usa padrão */ }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                // Os padrões por modelo que outra janela gravou depois desta abrir continuam (só os mudados aqui são trocados).
                var padroes = new Dictionary<string, string>(Load()._padroes, StringComparer.CurrentCultureIgnoreCase);
                foreach (string m in _padroesMudados)
                {
                    if (_padroes.TryGetValue(m, out string p)) padroes[m] = p;
                    else padroes.Remove(m);
                }
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
                }.Concat(padroes.Select(kv => PrefixoPadrao + kv.Key + "=" + kv.Value)));
            }
            catch { /* não é crítico */ }
        }
    }
}
