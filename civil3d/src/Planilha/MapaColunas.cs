using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MetroSP.Planilha
{
    /// <summary>Um dado que a ferramenta procura na planilha (ex.: Código, Descrição, ou um parâmetro escolhido pelo usuário).</summary>
    public class CampoPlanilha
    {
        public string Chave { get; set; }
        public string Rotulo { get; set; }
        public bool Obrigatorio { get; set; }
        /// <summary>Texto da dica (tooltip) do campo.</summary>
        public string Dica { get; set; }
        /// <summary>Títulos de coluna que indicam este campo (ex.: "código", "code"); o rótulo também conta.</summary>
        public string[] Sinonimos { get; set; } = Array.Empty<string>();

        public CampoPlanilha() { }

        public CampoPlanilha(string chave, string rotulo, bool obrigatorio = false, string dica = null, params string[] sinonimos)
        {
            Chave = chave;
            Rotulo = rotulo;
            Obrigatorio = obrigatorio;
            Dica = dica;
            Sinonimos = sinonimos ?? Array.Empty<string>();
        }
    }

    /// <summary>Uma linha de dados da planilha, com os valores já pela chave de cada campo.</summary>
    public class LinhaPlanilha
    {
        /// <summary>Número da linha no Excel (1 = primeira).</summary>
        public int Numero { get; set; }
        public Dictionary<string, string> Valores { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string this[string chave] => chave != null && Valores.TryGetValue(chave, out string v) ? v : null;
    }

    /// <summary>
    /// Onde estão os dados na planilha: arquivo, aba, linha do título e a coluna (letra) de cada campo.
    /// Salvo em JSON junto dos padrões de cada ferramenta. As chaves são livres (campos fixos ou escolhidos pelo usuário).
    /// </summary>
    public class MapaColunas
    {
        public string Arquivo { get; set; }
        /// <summary>Nome da aba; vazio = primeira.</summary>
        public string Aba { get; set; }
        /// <summary>Linha dos títulos das colunas (1 = primeira); os dados começam na linha seguinte.</summary>
        public int LinhaCabecalho { get; set; } = 1;
        /// <summary>Chave do campo → letra da coluna ("A", "B", ..., "AA").</summary>
        public Dictionary<string, string> Colunas { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public MapaColunas Clonar() => new MapaColunas
        {
            Arquivo = Arquivo,
            Aba = Aba,
            LinhaCabecalho = LinhaCabecalho,
            Colunas = new Dictionary<string, string>(Colunas ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase),
        };

        /// <summary>Letra da coluna do campo, ou null.</summary>
        public string Coluna(string chave)
        {
            if (Colunas == null || chave == null) return null;
            foreach (var kv in Colunas)
                if (string.Equals(kv.Key, chave, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(kv.Value)) return kv.Value.Trim().ToUpperInvariant();
            return null;
        }

        public void DefinirColuna(string chave, string letra)
        {
            Colunas ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string k in Colunas.Keys.Where(k => string.Equals(k, chave, StringComparison.OrdinalIgnoreCase)).ToList()) Colunas.Remove(k);
            if (!string.IsNullOrWhiteSpace(letra)) Colunas[chave] = letra.Trim().ToUpperInvariant();
        }

        [JsonIgnore]
        public string NomeArquivo => string.IsNullOrEmpty(Arquivo) ? "" : System.IO.Path.GetFileName(Arquivo);

        // ------------------------------------------------------------------ colunas

        /// <summary>0 → "A", 27 → "AB".</summary>
        public static string Letra(int indice)
        {
            string s = "";
            for (int n = indice + 1; n > 0; n = (n - 1) / 26) s = (char)('A' + (n - 1) % 26) + s;
            return s;
        }

        /// <summary>"A" → 0, "AB" → 27; inválido → -1.</summary>
        public static int Indice(string letra)
        {
            if (string.IsNullOrWhiteSpace(letra)) return -1;
            int n = 0;
            foreach (char ch in letra.Trim().ToUpperInvariant())
            {
                if (ch < 'A' || ch > 'Z') return -1;
                n = n * 26 + (ch - 'A' + 1);
            }
            return n - 1;
        }

        /// <summary>Títulos das colunas na linha do cabeçalho (índice = coluna).</summary>
        public List<string> Cabecalho(Tabela t)
        {
            List<List<string>> linhas = t.Linhas(Aba);
            int h = LinhaCabecalho - 1;
            return h >= 0 && h < linhas.Count ? linhas[h].Select(c => c ?? "").ToList() : new List<string>();
        }

        /// <summary>Número de colunas usadas (maior linha entre o cabeçalho e as 50 seguintes).</summary>
        public int NumeroColunas(Tabela t)
        {
            List<List<string>> linhas = t.Linhas(Aba);
            int h = Math.Max(0, LinhaCabecalho - 1);
            return linhas.Skip(h).Take(51).Select(l => l.Count).DefaultIfEmpty(0).Max();
        }

        // ------------------------------------------------------------------ sugestão

        /// <summary>Minúsculas, sem acentos e sem pontuação.</summary>
        public static string Normalizar(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            foreach (char c in s.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
            }
            return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        }

        private static int Pontos(string titulo, CampoPlanilha campo)
        {
            string t = Normalizar(titulo);
            if (t.Length == 0) return 0;
            var nomes = new[] { campo.Rotulo, campo.Chave }.Concat(campo.Sinonimos ?? Array.Empty<string>()).Select(Normalizar).Where(n => n.Length > 0).ToList();
            if (nomes.Contains(t)) return 3;
            if (nomes.Any(n => t.StartsWith(n + " ") || t.EndsWith(" " + n))) return 1;
            return 0;
        }

        /// <summary>Linha (1-based) entre as 20 primeiras com mais títulos que batem com os campos; 1 se nenhuma.</summary>
        public static int AcharCabecalho(Tabela t, string aba, IEnumerable<CampoPlanilha> campos)
        {
            var lista = campos.ToList();
            List<List<string>> linhas = t.Linhas(aba);
            int melhor = 0, pontos = 0;
            for (int i = 0; i < Math.Min(20, linhas.Count); i++)
            {
                int p = linhas[i].Sum(c => lista.Select(f => Pontos(c, f)).DefaultIfEmpty(0).Max());
                if (p > pontos) { pontos = p; melhor = i; }
            }
            if (pontos == 0)
                melhor = Math.Max(0, linhas.FindIndex(l => l.Count(c => !string.IsNullOrWhiteSpace(c)) >= 2));
            return melhor + 1;
        }

        /// <summary>Preenche as colunas dos campos ainda sem coluna, pelos títulos (exato primeiro, depois parecido).</summary>
        public void Sugerir(Tabela t, IEnumerable<CampoPlanilha> campos)
        {
            List<string> cab = Cabecalho(t);
            var usadas = new HashSet<int>(Colunas?.Values.Select(Indice) ?? Enumerable.Empty<int>());
            var pendentes = campos.Where(c => Coluna(c.Chave) == null).ToList();
            foreach (int nivel in new[] { 3, 1 })
                foreach (CampoPlanilha c in pendentes.Where(c => Coluna(c.Chave) == null))
                {
                    int i = -1;
                    for (int j = 0; j < cab.Count; j++)
                        if (!usadas.Contains(j) && Pontos(cab[j], c) >= nivel) { i = j; break; }
                    if (i < 0) continue;
                    DefinirColuna(c.Chave, Letra(i));
                    usadas.Add(i);
                }
        }

        /// <summary>Campos obrigatórios sem coluna.</summary>
        public List<CampoPlanilha> Faltando(IEnumerable<CampoPlanilha> campos) =>
            campos.Where(c => c.Obrigatorio && Coluna(c.Chave) == null).ToList();

        // ------------------------------------------------------------------ dados

        /// <summary>Linhas de dados (depois do cabeçalho), com os valores das colunas mapeadas; linhas vazias ficam de fora.</summary>
        public List<LinhaPlanilha> Linhas(Tabela t)
        {
            var resultado = new List<LinhaPlanilha>();
            var mapa = (Colunas ?? new Dictionary<string, string>()).Select(kv => (Chave: kv.Key, Col: Indice(kv.Value))).Where(x => x.Col >= 0).ToList();
            List<List<string>> linhas = t.Linhas(Aba);
            for (int i = Math.Max(0, LinhaCabecalho); i < linhas.Count; i++)
            {
                var l = new LinhaPlanilha { Numero = i + 1 };
                foreach (var (chave, col) in mapa)
                {
                    string v = col < linhas[i].Count ? linhas[i][col]?.Trim() : null;
                    l.Valores[chave] = string.IsNullOrEmpty(v) ? null : v;
                }
                if (l.Valores.Values.Any(v => v != null)) resultado.Add(l);
            }
            return resultado;
        }
    }
}
