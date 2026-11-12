using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MetroSP.Escrita
{
    // ======================================================================================================
    // Corrigir escrita: parte comum ao Revit e ao Civil 3D (este arquivo é igual nos dois repositórios).
    // Só regras de texto, sem nada do Revit, do AutoCAD ou da interface: dá para testar fora dos programas.
    // ======================================================================================================

    /// <summary>Uma regra de correção, que pode ser ligada ou desligada na janela.</summary>
    public class RegraEscrita
    {
        public string Id { get; set; }
        public string Nome { get; set; }
        public string Dica { get; set; }
        /// <summary>false = regra desligada até a pessoa marcar.</summary>
        public bool PadraoLigada { get; set; } = true;
        internal Func<string, CorretorEscrita, string> Aplicar { get; set; }
    }

    /// <summary>Resultado de um valor: a sugestão e o que foi achado.</summary>
    public class ResultadoEscrita
    {
        public string Original { get; set; }
        public string Sugestao { get; set; }
        public List<RegraEscrita> Regras { get; } = new List<RegraEscrita>();
        /// <summary>Problema que a regra acha mas não sabe corrigir (letra perdida: "Esta??o", "�").</summary>
        public string Revisar { get; set; }
        public bool TemProblema => Sugestao != Original || Revisar != null;
        public string Problemas => string.Join(" · ", Regras.Select(r => r.Nome).Concat(Revisar != null ? new[] { Revisar } : new string[0]));
    }

    /// <summary>Padrão salvo: regras desligadas, termos que não mudam, correções próprias e o que foi ignorado.</summary>
    public class PadraoEscrita
    {
        public string Nome { get; set; } = "";
        /// <summary>Ids das regras desligadas (regras novas entram ligadas).</summary>
        public List<string> RegrasDesligadas { get; set; } = new List<string>();
        /// <summary>Siglas, códigos e nomes que ficam como estão (ex.: PTL, Pr_20_30, MetroSP).</summary>
        public List<string> Termos { get; set; } = new List<string>();
        /// <summary>Correções próprias, uma por linha: "errado = certo" (ex.: "plataf = plataforma").</summary>
        public List<string> Correcoes { get; set; } = new List<string>();
        /// <summary>Valores marcados como "Ignorar": objeto|campo|valor.</summary>
        public List<string> Ignorados { get; set; } = new List<string>();

        public PadraoEscrita Copia() => JsonSerializer.Deserialize<PadraoEscrita>(JsonSerializer.Serialize(this));

        public static PadraoEscrita Inicial() => new PadraoEscrita
        {
            Nome = "Metrô",
            Termos = new List<string> { "BIM", "IFC", "MetroSP", "PTL" },
        };
    }

    /// <summary>Padrões em %AppData%\MetroSP\Escrita\*.json, os mesmos para o Revit e o Civil 3D.</summary>
    public static class PadroesEscrita
    {
        static readonly JsonSerializerOptions Opcoes = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public static string Pasta =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MetroSP", "Escrita");

        public static string NomeArquivo(string nome)
        {
            var sb = new StringBuilder();
            foreach (char c in nome ?? "") sb.Append(Path.GetInvalidFileNameChars().Contains(c) ? '_' : c);
            string r = sb.ToString().Trim();
            return r.Length == 0 ? "padrao" : r;
        }

        public static string Caminho(string nome) => Path.Combine(Pasta, NomeArquivo(nome) + ".json");

        /// <summary>Lista os padrões; na primeira vez cria o padrão "Metrô".</summary>
        public static List<PadraoEscrita> Listar()
        {
            var r = new List<PadraoEscrita>();
            try
            {
                if (!Directory.Exists(Pasta) || Directory.GetFiles(Pasta, "*.json").Length == 0) Salvar(PadraoEscrita.Inicial());
                foreach (string f in Directory.GetFiles(Pasta, "*.json").OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
                {
                    try
                    {
                        PadraoEscrita p = Ler(f);
                        if (string.IsNullOrWhiteSpace(p.Nome)) p.Nome = Path.GetFileNameWithoutExtension(f);
                        r.Add(p);
                    }
                    catch { /* arquivo estragado fica de fora */ }
                }
            }
            catch { /* sem acesso à pasta: segue com o padrão inicial em memória */ }
            if (r.Count == 0) r.Add(PadraoEscrita.Inicial());
            return r;
        }

        public static PadraoEscrita Ler(string arquivo)
        {
            PadraoEscrita p = JsonSerializer.Deserialize<PadraoEscrita>(File.ReadAllText(arquivo), Opcoes) ?? new PadraoEscrita();
            p.RegrasDesligadas = p.RegrasDesligadas ?? new List<string>();
            p.Termos = p.Termos ?? new List<string>();
            p.Correcoes = p.Correcoes ?? new List<string>();
            p.Ignorados = p.Ignorados ?? new List<string>();
            return p;
        }

        public static void Salvar(PadraoEscrita p)
        {
            Directory.CreateDirectory(Pasta);
            Gravar(Caminho(p.Nome), p);
        }

        public static void Gravar(string arquivo, PadraoEscrita p) => File.WriteAllText(arquivo, JsonSerializer.Serialize(p, Opcoes), new UTF8Encoding(false));

        public static void Excluir(string nome)
        {
            string f = Caminho(nome);
            if (File.Exists(f)) File.Delete(f);
        }
    }

    /// <summary>
    /// Aplica as regras ligadas de um padrão a um texto e devolve a sugestão. Os termos do padrão ficam protegidos
    /// de todas as regras do meio do texto (só os espaços nas pontas e repetidos são acertados em volta deles).
    /// </summary>
    public class CorretorEscrita
    {
        // ------------------------------------------------------------------ regras

        public const string EspacosPontas = "espacos-pontas", EspacosRepetidos = "espacos-repetidos", EspacosEspeciais = "espacos-especiais",
            Quebras = "quebras", Pontuacao = "pontuacao", Unidades = "unidades", Acentos = "acentos", Correcoes = "correcoes",
            Codificacao = "codificacao", Caracteres = "caracteres";

        /// <summary>Todas as regras, na ordem em que são aplicadas.</summary>
        public static readonly IReadOnlyList<RegraEscrita> Regras = new List<RegraEscrita>
        {
            new RegraEscrita
            {
                Id = Codificacao, Nome = "Texto embaralhado",
                Dica = "Acentos que viraram símbolos ao passar por outro programa: \"EstaÃ§Ã£o\" → \"Estação\". " +
                       "Também junta acentos gravados separados da letra (vindos de PDF ou Mac), que parecem iguais mas não são.",
                Aplicar = (s, c) => c.Codificar(s),
            },
            new RegraEscrita
            {
                Id = Caracteres, Nome = "Caracteres estranhos",
                Dica = "Tira caracteres de controle e troca aspas e apóstrofos curvos (“ ” ‘ ’ ´) pelos retos. " +
                       "Letras perdidas (\"Esta??o\", \"�\") aparecem para corrigir à mão na coluna Sugestão.",
                Aplicar = (s, c) => c.Estranhos(s),
            },
            new RegraEscrita
            {
                Id = Quebras, Nome = "Quebras de linha",
                Dica = "Troca quebras de linha e tabulações por um espaço: no IFC o valor fica numa linha só.",
                Aplicar = (s, c) => ReQuebras.Replace(s, " "),
            },
            new RegraEscrita
            {
                Id = EspacosEspeciais, Nome = "Espaços especiais",
                Dica = "Espaço não separável (vem do Word e do Excel) vira espaço comum; espaços invisíveis e hífens " +
                       "ocultos são tirados. Na tela parecem normais, mas atrapalham filtros e a comparação de valores.",
                Aplicar = (s, c) => ReInvisiveis.Replace(ReEspacosEspeciais.Replace(s, " "), ""),
            },
            new RegraEscrita
            {
                Id = Correcoes, Nome = "Correções do padrão",
                Dica = "As correções que você cadastrou no padrão (\"errado = certo\"), palavra inteira, mantendo maiúsculas.",
                Aplicar = (s, c) => c.TrocarPalavras(s, c._correcoes),
            },
            new RegraEscrita
            {
                Id = Acentos, Nome = "Acentuação",
                Dica = "Palavras sem acento ou com acento a mais: \"estacao\" → \"estação\", \"nivel\" → \"nível\", " +
                       "\"eletrica\" → \"elétrica\", \"idéia\" → \"ideia\", \"freqüência\" → \"frequência\". " +
                       "Usa uma lista de palavras de projeto e as terminações certas (-ção, -ões, -ência, -ável, -ário, -ógico...). " +
                       "Siglas e códigos que não devem mudar vão nos termos do padrão.",
                Aplicar = (s, c) => c.Acentuar(s),
            },
            new RegraEscrita
            {
                Id = Pontuacao, Nome = "Espaços na pontuação",
                Dica = "Tira o espaço antes de vírgula, ponto, ponto e vírgula, dois-pontos, ! ? e ) e depois de (. " +
                       "Põe o espaço que falta depois de vírgula e ponto e vírgula entre palavras: \"aço ,concreto\" → \"aço, concreto\".",
                Aplicar = (s, c) => Pontuar(s),
            },
            new RegraEscrita
            {
                Id = Unidades, Nome = "Número e unidade",
                Dica = "Espaço entre o número e a unidade, como pede o Sistema Internacional: \"10m\" → \"10 m\", \"25MPa\" → \"25 MPa\". " +
                       "Unidades: mm, cm, m, km, m², m³, kg, kgf, tf, kN, kPa, MPa, GPa, kV, kVA, kW, kWh, Hz, rpm, °C. " +
                       "Ordinais ficam juntos: \"1 º\" → \"1º\".",
                Aplicar = (s, c) => ReOrdinal.Replace(ReUnidade.Replace(s, "$1 $2"), "$1$2"),
            },
            new RegraEscrita
            {
                Id = EspacosRepetidos, Nome = "Espaços repetidos",
                Dica = "Dois ou mais espaços seguidos no meio do texto viram um só: \"Concreto  armado\" → \"Concreto armado\".",
                Aplicar = (s, c) => ReRepetidos.Replace(s, " "),
            },
            new RegraEscrita
            {
                Id = EspacosPontas, Nome = "Espaços nas pontas",
                Dica = "Espaços no começo ou no fim do valor: \" Concreto \" → \"Concreto\".",
                Aplicar = (s, c) => s.Trim(' '),
            },
        };

        public static RegraEscrita Regra(string id) => Regras.FirstOrDefault(r => r.Id == id);

        // ------------------------------------------------------------------ expressões

        static readonly Regex ReQuebras = new Regex(@"[ ]*(\r\n|[\r\n\t\v\f])[ ]*", RegexOptions.Compiled);
        static readonly Regex ReEspacosEspeciais = new Regex(@"[   -   　]", RegexOptions.Compiled);
        static readonly Regex ReInvisiveis = new Regex(@"[­᠎​-‍⁠﻿]", RegexOptions.Compiled);
        static readonly Regex ReControle = new Regex(@"[\u0000-\u0008\u000E-\u001F\u007F-\u009F]", RegexOptions.Compiled);
        static readonly Regex ReRepetidos = new Regex(@"(?<=\S) {2,}(?=\S)", RegexOptions.Compiled);
        static readonly Regex RePerdido = new Regex(@"�|\p{L}\?+\p{L}", RegexOptions.Compiled);
        static readonly Regex RePalavra = new Regex(@"(?<![\p{L}\p{M}\p{N}_])[\p{L}\p{M}]+(?![\p{L}\p{M}\p{N}_])", RegexOptions.Compiled);

        static readonly Regex ReAntesPontuacao = new Regex(@" +(?=[,;:!?)\]}])", RegexOptions.Compiled);
        static readonly Regex ReAntesPonto = new Regex(@"(?<=\S) +(?=\.(\s|$))", RegexOptions.Compiled);
        static readonly Regex ReDepoisAbre = new Regex(@"(?<=[(\[{]) +", RegexOptions.Compiled);
        static readonly Regex RePalavraAntes = new Regex(@"[\p{L}\p{N}_]*$", RegexOptions.Compiled);
        static readonly Regex RePalavraDepois = new Regex(@"^[\p{L}\p{N}_]*", RegexOptions.Compiled);
        static readonly Regex ReFaltaEspaco = new Regex(@"(?<=\p{Ll})[,;](?=\p{L})|(?<=\p{L})[,;](?=\p{Ll})", RegexOptions.Compiled);

        static readonly Regex ReUnidade = new Regex(
            @"(?<![\p{L}\p{N}_.,/\-])(\d+(?:[.,]\d+)?)(mm|cm|km|m²|m³|m2|m3|m|kgf|kg|tf|kN|kPa|MPa|GPa|kVA|kV|kWh|kW|Hz|rpm|°C|ºC)(?![\p{L}\p{N}_²³])",
            RegexOptions.Compiled);
        static readonly Regex ReOrdinal = new Regex(@"(\d) +([ºª])(?![\p{L}\p{N}])", RegexOptions.Compiled);

        /// <summary>Caminho de arquivo, endereço da internet ou e-mail: só os espaços e caracteres invisíveis são acertados.</summary>
        static readonly Regex ReCaminho = new Regex(@"^\s*[A-Za-z]:[\\/]|\\\\|://|^\s*www\.|[\w.\-]+@[\w\-]+\.\w", RegexOptions.Compiled);

        static readonly HashSet<string> SoNasPontas = new HashSet<string> { Codificacao, Caracteres, Quebras, EspacosEspeciais, EspacosRepetidos, EspacosPontas };

        // ------------------------------------------------------------------ instância

        private readonly HashSet<string> _ligadas;
        private readonly List<string> _termos;
        private readonly HashSet<string> _termosPalavra;
        internal readonly Dictionary<string, string> _correcoes;

        public CorretorEscrita(PadraoEscrita padrao)
        {
            padrao = padrao ?? new PadraoEscrita();
            _ligadas = new HashSet<string>(Regras.Where(r => !padrao.RegrasDesligadas.Contains(r.Id)).Select(r => r.Id));
            _termos = padrao.Termos.Select(t => (t ?? "").Trim()).Where(t => t.Length > 0)
                .Distinct(StringComparer.CurrentCultureIgnoreCase).OrderByDescending(t => t.Length).ToList();
            _termosPalavra = new HashSet<string>(_termos, StringComparer.CurrentCultureIgnoreCase);
            _correcoes = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (string linha in padrao.Correcoes)
            {
                int i = (linha ?? "").IndexOf('=');
                if (i <= 0) continue;
                string errado = linha.Substring(0, i).Trim(), certo = linha.Substring(i + 1).Trim();
                if (errado.Length > 0 && certo.Length > 0 && errado != certo) _correcoes[errado] = certo;
            }
        }

        public bool Ligada(string id) => _ligadas.Contains(id);

        /// <summary>true se o texto tem letra perdida ("Esta??o", "�"), que só a pessoa sabe corrigir.</summary>
        public static bool TemLetraPerdida(string s) => !string.IsNullOrEmpty(s) && RePerdido.IsMatch(s);

        /// <summary>Palavras do texto original que a correção mudou (para virar termo do padrão).</summary>
        public static List<string> PalavrasMudadas(string antes, string depois)
        {
            var novas = new HashSet<string>(RePalavra.Matches(depois ?? "").Cast<Match>().Select(m => m.Value));
            return RePalavra.Matches(antes ?? "").Cast<Match>().Select(m => m.Value).Where(p => !novas.Contains(p)).Distinct().ToList();
        }

        public ResultadoEscrita Analisar(string texto)
        {
            texto = texto ?? "";
            var r = new ResultadoEscrita { Original = texto };
            bool caminho = ReCaminho.IsMatch(texto);

            // Termos protegidos: viram marcadores (área de uso privado do Unicode) e voltam no fim.
            var guardados = new List<string>();
            string s = Proteger(texto, guardados);

            foreach (RegraEscrita regra in Regras)
            {
                if (!_ligadas.Contains(regra.Id) || (caminho && !SoNasPontas.Contains(regra.Id))) continue;
                string antes = s;
                s = regra.Aplicar(s, this);
                if (s != antes) r.Regras.Add(regra);
            }
            r.Sugestao = Restaurar(s, guardados);
            if (_ligadas.Contains(Caracteres) && RePerdido.IsMatch(r.Sugestao)) r.Revisar = "Letra perdida (corrigir à mão)";
            return r;
        }

        private string Proteger(string s, List<string> guardados)
        {
            if (_termos.Count == 0 || s.Length == 0) return s;
            foreach (string t in _termos)
            {
                int i = 0;
                while ((i = s.IndexOf(t, i, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    // Só como palavra inteira (ou trecho inteiro quando o termo tem pontuação).
                    bool inicioOk = i == 0 || !char.IsLetterOrDigit(s[i - 1]);
                    bool fimOk = i + t.Length >= s.Length || !char.IsLetterOrDigit(s[i + t.Length]);
                    if (!inicioOk || !fimOk) { i += t.Length; continue; }
                    string marca = ((char)(0xE000 + guardados.Count)).ToString();
                    guardados.Add(s.Substring(i, t.Length));
                    s = s.Substring(0, i) + marca + s.Substring(i + t.Length);
                    i += 1;
                    if (guardados.Count >= 6000) return s;
                }
            }
            return s;
        }

        private static string Restaurar(string s, List<string> guardados)
        {
            if (guardados.Count == 0) return s;
            var sb = new StringBuilder(s.Length + 16);
            foreach (char c in s)
            {
                int i = c - 0xE000;
                sb.Append(i >= 0 && i < guardados.Count ? guardados[i] : c.ToString());
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ espaços e pontuação

        private static string Pontuar(string s)
        {
            s = ReAntesPontuacao.Replace(s, "");
            s = ReAntesPonto.Replace(s, "");
            s = ReDepoisAbre.Replace(s, "");
            // Listas de códigos ("IfcWall,IfcSlab") ficam juntas: só põe o espaço entre palavras comuns.
            s = ReFaltaEspaco.Replace(s, m =>
            {
                string antes = RePalavraAntes.Match(s.Substring(0, m.Index)).Value, depois = RePalavraDepois.Match(s.Substring(m.Index + 1)).Value;
                return CaixaDe(antes) == Caixas.Mista || CaixaDe(depois) == Caixas.Mista ? m.Value : m.Value + " ";
            });
            return s;
        }

        // ------------------------------------------------------------------ caracteres

        private string Estranhos(string s)
        {
            s = ReControle.Replace(s, "");
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '“': case '”': case '„': case '‟': case '«': case '»': sb.Append('"'); break;
                    case '‘': case '’': case '‚': case '‛': sb.Append('\''); break;
                    // Acento agudo solto usado como apóstrofo: "d´água".
                    case '´': case '`':
                        bool entreLetras = i > 0 && i + 1 < s.Length && char.IsLetter(s[i - 1]) && char.IsLetter(s[i + 1]);
                        sb.Append(entreLetras ? "'" : c.ToString());
                        break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>Desfaz o texto UTF-8 lido como Windows-1252 ("Ã§" → "ç") e junta acentos separados (NFC).</summary>
        private string Codificar(string s)
        {
            if (s.Length == 0) return s;
            string r = ReTrechoCp1252.Replace(s, m =>
            {
                string t = m.Value;
                if (t.IndexOf('Ã') < 0 && t.IndexOf('Â') < 0 && t.IndexOf("â€", StringComparison.Ordinal) < 0) return t;
                var bytes = new byte[t.Length];
                for (int i = 0; i < t.Length; i++)
                {
                    char c = t[i];
                    if (c <= 0xFF) bytes[i] = (byte)c;
                    else if (Cp1252.TryGetValue(c, out byte b)) bytes[i] = b;
                    else return t;
                }
                try { return Utf8Estrito.GetString(bytes); }
                catch (DecoderFallbackException) { return t; }
            });
            try { if (!r.IsNormalized(NormalizationForm.FormC)) r = r.Normalize(NormalizationForm.FormC); }
            catch (ArgumentException) { /* texto com caractere inválido: fica como está */ }
            return r;
        }

        static readonly UTF8Encoding Utf8Estrito = new UTF8Encoding(false, true);

        static readonly Dictionary<char, byte> Cp1252 = new Dictionary<char, byte>
        {
            ['€'] = 0x80, ['‚'] = 0x82, ['ƒ'] = 0x83, ['„'] = 0x84, ['…'] = 0x85, ['†'] = 0x86,
            ['‡'] = 0x87, ['ˆ'] = 0x88, ['‰'] = 0x89, ['Š'] = 0x8A, ['‹'] = 0x8B, ['Œ'] = 0x8C,
            ['Ž'] = 0x8E, ['‘'] = 0x91, ['’'] = 0x92, ['“'] = 0x93, ['”'] = 0x94, ['•'] = 0x95,
            ['–'] = 0x96, ['—'] = 0x97, ['˜'] = 0x98, ['™'] = 0x99, ['š'] = 0x9A, ['›'] = 0x9B,
            ['œ'] = 0x9C, ['ž'] = 0x9E, ['Ÿ'] = 0x9F,
        };

        static readonly Regex ReTrechoCp1252 = new Regex(
            "[\u0080-ÿ€‚ƒ„…†‡ˆ‰Š‹ŒŽ‘’“”•–—˜™š›œžŸ]{2,}",
            RegexOptions.Compiled);

        // ------------------------------------------------------------------ palavras

        /// <summary>Troca palavras inteiras pelo dicionário, mantendo maiúsculas ("ESTACAO" → "ESTAÇÃO", "Nivel" → "Nível").</summary>
        private string TrocarPalavras(string s, Dictionary<string, string> dic)
        {
            if (dic.Count == 0) return s;
            // Correções com mais de uma palavra ("pre moldado = pré-moldado") vão primeiro, como trecho.
            foreach (var kv in dic.Where(k => k.Key.Any(ch => !char.IsLetter(ch))))
                s = Regex.Replace(s, @"(?<![\p{L}\p{N}])" + Regex.Escape(kv.Key) + @"(?![\p{L}\p{N}])", m => Caixa(m.Value, kv.Value), RegexOptions.IgnoreCase);
            return RePalavra.Replace(s, m =>
                !_termosPalavra.Contains(m.Value) && dic.TryGetValue(m.Value, out string certo) ? Caixa(m.Value, certo) : m.Value);
        }

        /// <summary>Palavras claramente inglesas: o valor está em inglês ("Section Area") e a acentuação não mexe nele.</summary>
        static readonly HashSet<string> Ingles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "of", "and", "with", "for", "from", "section", "gross", "net", "projected", "width", "height", "length", "depth",
            "type", "name", "level", "surface", "outer", "inner", "top", "bottom", "left", "right", "side", "is", "not", "does", "apply",
        };

        private string Acentuar(string s)
        {
            if (RePalavra.Matches(s).Cast<Match>().Any(m => Ingles.Contains(m.Value))) return s;
            return RePalavra.Replace(s, m =>
            {
                string p = m.Value;
                if (p.Length < 2 || _termosPalavra.Contains(p)) return p;
                Caixas caixa = CaixaDe(p);
                if (caixa == Caixas.Mista) return p; // camelCase, códigos: não mexe
                string baixa = p.ToLower(Pt);
                string certo = Acentuacao.Corrigir(baixa);
                return certo == null || certo == baixa ? p : Aplicar(caixa, certo);
            });
        }

        static readonly CultureInfo Pt = Cultura();

        private static CultureInfo Cultura()
        {
            try { return CultureInfo.GetCultureInfo("pt-BR"); } catch { return CultureInfo.InvariantCulture; }
        }

        private enum Caixas { Baixa, Alta, Titulo, Mista }

        private static Caixas CaixaDe(string p)
        {
            bool algumaAlta = false, algumaBaixa = false;
            for (int i = 0; i < p.Length; i++)
            {
                if (char.IsUpper(p[i])) { algumaAlta = true; if (i > 0 && algumaBaixa) return Caixas.Mista; }
                else if (char.IsLower(p[i])) algumaBaixa = true;
            }
            if (!algumaAlta) return Caixas.Baixa;
            if (!algumaBaixa) return p.Length == 1 ? Caixas.Titulo : Caixas.Alta;
            return char.IsUpper(p[0]) && p.Skip(1).All(ch => !char.IsUpper(ch)) ? Caixas.Titulo : Caixas.Mista;
        }

        private static string Aplicar(Caixas caixa, string certo)
        {
            switch (caixa)
            {
                case Caixas.Alta: return certo.ToUpper(Pt);
                case Caixas.Titulo: return certo.Length == 0 ? certo : char.ToUpper(certo[0], Pt) + certo.Substring(1);
                default: return certo;
            }
        }

        /// <summary>Usa a caixa do original na correção (para correções do padrão).</summary>
        private static string Caixa(string original, string certo)
        {
            // Correção escrita com maiúsculas ("metrosp = MetroSP") vale como está.
            if (certo.Any(char.IsUpper)) return certo;
            Caixas c = CaixaDe(original);
            return c == Caixas.Mista || c == Caixas.Baixa ? certo : Aplicar(c, certo);
        }
    }

    /// <summary>
    /// Acentuação do português para palavras de projeto: uma lista de palavras certas (a forma sem acento aponta para
    /// a certa), acentos que o Acordo Ortográfico tirou e terminações que sempre levam acento.
    /// </summary>
    public static class Acentuacao
    {
        /// <summary>Palavras certas. Ficam de fora as que também existem sem acento (esta/está, e/é, calculo/cálculo...).</summary>
        const string Palavras =
            "água águas área áreas nível níveis número números código códigos índice índices série séries espécie espécies " +
            "elétrico elétrica elétricos elétricas eletrônico eletrônica eletrônicos eletrônicas hidráulico hidráulica hidráulicos hidráulicas " +
            "mecânico mecânica mecânicos mecânicas metálico metálica metálicos metálicas cerâmico cerâmica cerâmicos cerâmicas " +
            "térmico térmica térmicos térmicas acústico acústica acústicos acústicas plástico plástica plásticos plásticas " +
            "sísmico sísmica geotécnico geotécnica técnico técnica técnicos técnicas topográfico topográfica geométrico geométrica " +
            "automático automática automáticos automáticas pneumático pneumática estático estática dinâmico dinâmica " +
            "túnel túneis poço poços pátio pátios térreo térrea saída saídas vestiário vestiários depósito depósitos " +
            "escritório escritórios laboratório laboratórios auditório refeitório reservatório reservatórios lavatório lavatórios " +
            "mictório mictórios sanitário sanitários sanitária sanitárias aço aços alumínio mármore polímero polímeros " +
            "vão vãos portão portões corrimão corrimãos botão botões chão câmara câmaras cômodo cômodos módulo módulos " +
            "célula células válvula válvulas fórmula fórmulas máquina máquinas veículo veículos catenária catenárias pantógrafo " +
            "tráfego trânsito ônibus diâmetro diâmetros perímetro perímetros parâmetro parâmetros quilômetro quilômetros " +
            "milímetro milímetros centímetro centímetros cúbico cúbica cúbicos líquido líquida líquidos sólido sólida sólidos " +
            "médio média médios médias máximo máxima máximos máximas mínimo mínima mínimos mínimas útil úteis único única " +
            "próximo próxima próximos próprio própria próprios próprias provisório provisória obrigatório obrigatória " +
            "não são já até após também além através três você vocês família famílias rodapé rodapés calçada calçadas " +
            "praça praças incêndio incêndios lâmpada lâmpadas luminária luminárias contêiner contêineres lâmina lâminas " +
            "tátil táteis ângulo ângulos triângulo triângulos superfície superfícies período períodos móvel móveis imóvel imóveis " +
            "automóvel automóveis fácil difícil rápido rápida lógico lógica gráfico gráficos histórico histórica genérico genérica " +
            "típico típica memória memórias sumário óleo óleos sílica estrutura ferroviário ferroviária metroviário metroviária " +
            "rodoviário rodoviária usuário usuários horário horários diário diária cenário cenários galpão galpões pilão " +
            "específico específica específicos específicas elétron hidrômetro manômetro termômetro cronômetro ampère " +
            "trilho dormência fôrma fôrmas pré concessionária concessionárias bilhética baía baías rampa patamar patamares " +
            "espaçador espaçadores espaçamento revestimento tubulação tubulações conexão conexões região regiões pressão " +
            "inspeção ventilação exaustão drenagem fundação contenção escavação vedação proteção sinalização iluminação " +
            "alimentação distribuição subestação estação estações plataforma acessível acessíveis sequência frequência " +
            "emergência referência resistência potência distância tolerância";

        /// <summary>Acentos a mais ou que caíram com o Acordo Ortográfico: errado → certo.</summary>
        static readonly Dictionary<string, string> Errados = new Dictionary<string, string>
        {
            ["ítem"] = "item", ["ítens"] = "itens", ["hífens"] = "hifens", ["idéia"] = "ideia", ["idéias"] = "ideias",
            ["assembléia"] = "assembleia", ["platéia"] = "plateia", ["européia"] = "europeia", ["estréia"] = "estreia",
            ["heróico"] = "heroico", ["jóia"] = "joia", ["bóia"] = "boia", ["bóias"] = "boias", ["apóio"] = "apoio",
            ["vôo"] = "voo", ["vôos"] = "voos", ["enjôo"] = "enjoo", ["lêem"] = "leem", ["vêem"] = "veem", ["crêem"] = "creem",
            ["dêem"] = "deem", ["pára"] = "para", ["pêlo"] = "pelo", ["pólo"] = "polo", ["côr"] = "cor", ["rúbrica"] = "rubrica",
            ["gratuíto"] = "gratuito", ["fortuíto"] = "fortuito", ["ruím"] = "ruim", ["tôrre"] = "torre",
        };

        /// <summary>Terminações (sem acento → com acento). As mais longas primeiro.</summary>
        static readonly (string Sem, string Com, int Minimo)[] Terminacoes =
        {
            ("coes", "ções", 5), ("cao", "ção", 4), ("soes", "sões", 5), ("sao", "são", 5), ("oes", "ões", 5), ("ao", "ão", 4),
            ("encias", "ências", 7), ("encia", "ência", 6), ("ancias", "âncias", 7), ("ancia", "ância", 6),
            ("aveis", "áveis", 6), ("avel", "ável", 6), ("iveis", "íveis", 6), ("ivel", "ível", 5), ("oveis", "óveis", 6), ("ovel", "óvel", 5),
            ("arios", "ários", 6), ("ario", "ário", 5), ("orios", "órios", 6), ("orio", "ório", 5),
            ("ogico", "ógico", 6), ("ogica", "ógica", 6), ("ogicos", "ógicos", 7), ("ogicas", "ógicas", 7),
            ("etrico", "étrico", 7), ("etrica", "étrica", 7), ("etricos", "étricos", 8), ("etricas", "étricas", 8),
            ("aulico", "áulico", 7), ("aulica", "áulica", 7), ("aulicos", "áulicos", 8), ("aulicas", "áulicas", 8),
            ("anico", "ânico", 6), ("anica", "ânica", 6), ("anicos", "ânicos", 7), ("anicas", "ânicas", 7),
            ("onico", "ônico", 6), ("onica", "ônica", 6), ("onicos", "ônicos", 7), ("onicas", "ônicas", 7),
            ("afico", "áfico", 6), ("afica", "áfica", 6), ("aficos", "áficos", 7), ("aficas", "áficas", 7),
            ("amico", "âmico", 6), ("amica", "âmica", 6), ("amicos", "âmicos", 7), ("amicas", "âmicas", 7),
            ("alico", "álico", 6), ("alica", "álica", 6), ("alicos", "álicos", 7), ("alicas", "álicas", 7),
            ("ermico", "érmico", 7), ("ermica", "érmica", 7), ("ustico", "ústico", 7), ("ustica", "ústica", 7),
            ("astico", "ástico", 7), ("astica", "ástica", 7), ("asticos", "ásticos", 8), ("asticas", "ásticas", 8),
            ("matico", "mático", 7), ("matica", "mática", 7), ("maticos", "máticos", 8), ("maticas", "máticas", 8),
            ("omico", "ômico", 6), ("omica", "ômica", 6), ("orico", "órico", 6), ("orica", "órica", 6),
            ("enico", "ênico", 6), ("enica", "ênica", 6), ("ismico", "ísmico", 7), ("ismica", "ísmica", 7),
            ("itico", "ítico", 7), ("itica", "ítica", 7),
        };

        /// <summary>Palavras que parecem portuguesas sem acento mas não são erro (inglês, nomes, verbos).</summary>
        static readonly HashSet<string> NaoMexer = new HashSet<string>
        {
            "does", "goes", "shoes", "toes", "heroes", "echoes", "tomatoes", "potatoes", "tao", "cacao", "ciao", "callao", "bilbao",
            "travel", "gravel", "ravel", "unravel", "navel", "swivel", "drivel", "snivel", "civel",
            "scenario", "scenarios", "mario", "ontario", "rosario", "dario", "aurelio", "critica", "critico", "pratica", "pratico",
            "fabrica", "fabrico", "publica", "publico", "calculo", "analise", "especifico", "multiplico", "duplico", "replica", "replico",
            "musica", "estatica", "matematico", "magico", "etico", "domestico", "explico", "aplico", "implica", "complica", "indica",
            "comunica", "verifica", "classifica", "identifica", "modifica", "especifica", "qualifica", "quantifica", "certifica",
            "unifica", "simplifica", "intensifica", "edifica", "retifica", "ratifica", "justifica", "dignifica",
        };

        static readonly char[] Tonicos = "áéíóúâêô".ToCharArray();

        static readonly Dictionary<string, string> Mapa = Montar();

        static Dictionary<string, string> Montar()
        {
            var r = new Dictionary<string, string>();
            foreach (string w in Palavras.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string sem = SemAcento(w);
                if (sem != w && !r.ContainsKey(sem)) r[sem] = w;
            }
            foreach (var kv in Errados) if (kv.Key != kv.Value) r[kv.Key] = kv.Value;
            return r;
        }

        public static string SemAcento(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s.Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        /// <summary>Forma certa da palavra (minúscula) ou null se não sabe. Só mexe em palavras só de letras.</summary>
        public static string Corrigir(string p)
        {
            if (string.IsNullOrEmpty(p)) return null;
            // Trema caiu: freqüência, cinqüenta, agüentar.
            if (p.Contains("gü") || p.Contains("qü")) p = p.Replace("gü", "gu").Replace("qü", "qu");
            if (Mapa.TryGetValue(p, out string certo)) return certo;
            if (NaoMexer.Contains(p) || p.Length < 4) return p;
            // Uma palavra só tem um acento tônico: "órgao" → "órgão" vale, "hidráulico" não ganha outro.
            bool temTonico = p.IndexOfAny(Tonicos) >= 0;
            foreach (var t in Terminacoes)
            {
                if (p.Length < t.Minimo || !p.EndsWith(t.Sem, StringComparison.Ordinal)) continue;
                // "-ao/-oes" logo depois de vogal é outra coisa.
                string radical = p.Substring(0, p.Length - t.Sem.Length);
                if (!SemAcento(radical).Any(ch => "aeiouy".IndexOf(ch) >= 0)) return p;
                if ((t.Sem == "ao" || t.Sem == "oes") && "aeiou".IndexOf(radical[radical.Length - 1]) >= 0) return p;
                if (temTonico && t.Com.IndexOfAny(Tonicos) >= 0) return p;
                return radical + t.Com;
            }
            return p;
        }
    }
}
