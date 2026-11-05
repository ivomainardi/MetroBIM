using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using MetroSP.Planilha;

namespace MetroSP.Confronto
{
    // ======================================================================================================
    // Confrontar com planilha: parte comum ao Revit e ao Civil 3D (este arquivo é igual nos dois repositórios).
    // O que muda entre os programas fica na IFonteConfronto: como achar os objetos, ler e gravar os campos.
    // ======================================================================================================

    /// <summary>Um objeto do modelo (elemento do Revit, objeto do Civil 3D).</summary>
    public class ObjetoConfronto
    {
        public object Ref { get; set; }
        public string Id { get; set; }
        public string Nome { get; set; }
        public string Categoria { get; set; }
    }

    /// <summary>Um dado que pode ser conferido: parâmetro, propriedade de um property set, nome, layer...</summary>
    public class CampoConfronto
    {
        /// <summary>Identificador estável, gravado no padrão.</summary>
        public string Chave { get; set; }
        public string Nome { get; set; }
        /// <summary>"instância", "tipo", nome do property set...</summary>
        public string Grupo { get; set; }
        public string Detalhe { get; set; }
        public bool Editavel { get; set; }
        /// <summary>Texto livre (não número, medida, Sim/Não ou referência): entra no Corrigir escrita.</summary>
        public bool Texto { get; set; }
        public string Rotulo => string.IsNullOrEmpty(Grupo) ? Nome : $"{Nome}  ·  {Grupo}";
        public override string ToString() => Rotulo;
    }

    /// <summary>Valor de um campo num objeto.</summary>
    public class ValorConfronto
    {
        public bool Existe { get; set; }
        public string Texto { get; set; } = "";
        public bool Editavel { get; set; }
        /// <summary>Quando o valor mora em outro objeto (tipo do Revit, por exemplo): chave do dono e o nome dele.</summary>
        public string Dono { get; set; }
        public string DonoRotulo { get; set; }
        public string Motivo { get; set; }
    }

    /// <summary>Valor a gravar, já convertido pela fonte.</summary>
    public class Gravacao
    {
        public ObjetoConfronto Objeto { get; set; }
        public CampoConfronto Campo { get; set; }
        public object Valor { get; set; }
        public string Texto { get; set; }
        /// <summary>A fonte marca true no que conseguiu gravar.</summary>
        public bool Ok { get; set; }
    }

    /// <summary>Acesso ao modelo, um por programa.</summary>
    public interface IFonteConfronto
    {
        /// <summary>"Revit" ou "Civil3D": separa as pastas dos padrões salvos.</summary>
        string Programa { get; }
        string NomeDocumento { get; }
        /// <summary>Palavra usada na interface: "elemento" ou "objeto".</summary>
        string Termo { get; }
        /// <summary>Barra de escopo (seleção, vista, modelo, categorias); pode ser null.</summary>
        FrameworkElement PainelEscopo { get; }
        event Action EscopoMudou;
        /// <summary>Objetos do escopo que vão para o IFC.</summary>
        IList<ObjetoConfronto> Objetos();
        /// <summary>Campos disponíveis nesses objetos.</summary>
        IList<CampoConfronto> Campos();
        ValorConfronto Ler(ObjetoConfronto o, CampoConfronto c);
        /// <summary>Igualdade que depende do tipo do dado (números, medidas com unidade, Sim/Não).</summary>
        bool Iguais(ObjetoConfronto o, CampoConfronto c, string modelo, string planilha);
        /// <summary>Converte o texto da planilha para gravar; false com o motivo quando não dá.</summary>
        bool Preparar(ObjetoConfronto o, CampoConfronto c, string planilha, out object valor, out string motivo);
        /// <summary>Grava tudo numa operação só (desfazível), marcando Ok em cada gravação. Devolve gravados e falhas.</summary>
        (int Ok, int Falhas) Gravar(IList<Gravacao> gravacoes);
        void Selecionar(IEnumerable<ObjetoConfronto> objetos, bool mostrar);
    }

    // ------------------------------------------------------------------ padrão salvo

    /// <summary>Campo do modelo a conferir; a coluna fica no mapa da planilha, pela chave do campo.</summary>
    public class ComparacaoPadrao
    {
        public string Campo { get; set; }
        public string CampoNome { get; set; }
    }

    /// <summary>Padrão salvo: planilha, coluna da chave, pares campo ↔ coluna e as opções.</summary>
    public class PadraoConfronto
    {
        public string Nome { get; set; } = "";
        /// <summary>Planilha, aba, linha dos títulos e coluna de cada campo (a chave usa <see cref="ChaveColuna"/>).</summary>
        public MapaColunas Mapa { get; set; } = new MapaColunas();
        public string CampoChave { get; set; }
        public string CampoChaveNome { get; set; }
        public List<ComparacaoPadrao> Comparacoes { get; set; } = new List<ComparacaoPadrao>();
        public bool IgnorarMaiusculas { get; set; } = true;
        public bool VaziaNaoConfere { get; set; } = true;
        public bool MostrarForaDaPlanilha { get; set; }
        /// <summary>Diferenças marcadas como "Ignorar": chave|campo|valor da planilha.</summary>
        public List<string> Ignorados { get; set; } = new List<string>();

        /// <summary>Chave da coluna que identifica a linha, no mapa de colunas.</summary>
        public const string ChaveColuna = "@chave";

        public PadraoConfronto Copia() => JsonSerializer.Deserialize<PadraoConfronto>(JsonSerializer.Serialize(this));
    }

    /// <summary>Padrões em %AppData%\MetroSP\Confrontar\{programa}\*.json.</summary>
    public static class Padroes
    {
        static readonly JsonSerializerOptions Opcoes = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public static string Pasta(string programa) =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MetroSP", "Confrontar", programa);

        public static List<PadraoConfronto> Listar(string programa)
        {
            var r = new List<PadraoConfronto>();
            string dir = Pasta(programa);
            if (!Directory.Exists(dir)) return r;
            foreach (string f in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    PadraoConfronto p = Ler(f);
                    if (string.IsNullOrWhiteSpace(p.Nome)) p.Nome = Path.GetFileNameWithoutExtension(f);
                    r.Add(p);
                }
                catch { /* arquivo estragado: fica de fora */ }
            }
            return r.OrderBy(p => p.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public static PadraoConfronto Ler(string arquivo) =>
            JsonSerializer.Deserialize<PadraoConfronto>(File.ReadAllText(arquivo, Encoding.UTF8), Opcoes) ?? new PadraoConfronto();

        public static void Gravar(string arquivo, PadraoConfronto p)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(arquivo));
            File.WriteAllText(arquivo, JsonSerializer.Serialize(p, Opcoes), new UTF8Encoding(false));
        }

        public static string Caminho(string programa, string nome) => Path.Combine(Pasta(programa), NomeArquivo(nome) + ".json");

        public static void Salvar(string programa, PadraoConfronto p) => Gravar(Caminho(programa, p.Nome), p);

        public static void Excluir(string programa, string nome)
        {
            string f = Caminho(programa, nome);
            if (File.Exists(f)) File.Delete(f);
        }

        public static string NomeArquivo(string s)
        {
            string n = string.Concat((s ?? "").Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)).Trim();
            return n.Length == 0 ? "padrao" : n;
        }
    }

    // ------------------------------------------------------------------ resultado

    /// <summary>Uma diferença (ou um aviso) encontrada no confronto.</summary>
    public class LinhaConfronto
    {
        public const string Diferente = "Diferente";
        public const string VazioModelo = "Vazio no modelo";
        public const string VazioPlanilha = "Vazio na planilha";
        public const string ConflitoTipo = "Conflito no tipo";
        public const string SemCampo = "Sem o campo";
        public const string NaoEstaNoModelo = "Não está no modelo";
        public const string ForaDaPlanilha = "Fora da planilha";
        public const string Ignorado = "Ignorado";
        public const string Aplicado = "Aplicado";

        public string Situacao { get; set; }
        public string Elemento { get; set; }
        public string Id { get; set; }
        public string Categoria { get; set; }
        public string Chave { get; set; }
        public string Campo { get; set; }
        public string NoModelo { get; set; }
        public string NaPlanilha { get; set; }
        public string OQueFazer { get; set; }
        public int Linha { get; set; }

        internal List<ObjetoConfronto> Objetos { get; } = new List<ObjetoConfronto>();
        internal CampoConfronto CampoRef { get; set; }
        internal object Valor { get; set; }
        internal bool PodeAplicar { get; set; }
        internal string ChaveIgnorar { get; set; }
        internal string Grupo { get; set; }
        /// <summary>Situação antes de ignorar ou aplicar, para poder voltar.</summary>
        internal (string Situacao, string OQueFazer, bool PodeAplicar) Original { get; set; }

        public bool EhDiferenca => Situacao == Diferente || Situacao == VazioModelo || Situacao == VazioPlanilha;
    }

    public class ResultadoConfronto
    {
        public List<LinhaConfronto> Linhas { get; } = new List<LinhaConfronto>();
        public List<string> Avisos { get; } = new List<string>();
        public int Conferidos { get; set; }
        public int Iguais { get; set; }
        public int LinhasPlanilha { get; set; }
        public int ObjetosAchados { get; set; }
    }

    // ------------------------------------------------------------------ comparação

    public static class Confrontador
    {
        public static string Normalizar(string s, bool semMaiusculas)
        {
            s = string.Join(" ", (s ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
            return semMaiusculas ? s.ToLower(CultureInfo.CurrentCulture) : s;
        }

        static readonly System.Text.RegularExpressions.Regex NumeroSimples =
            new System.Text.RegularExpressions.Regex(@"^-?(0|[1-9][0-9]*)([.,][0-9]+)?$");

        /// <summary>
        /// Número simples ("12", "2,5", "2.5", "-0,75"). Códigos com zero à esquerda ("001") ou com mais de um
        /// separador ("01.02.03", "1.234,5") continuam texto, para não virar outro número.
        /// </summary>
        public static bool Numero(string s, out double d)
        {
            d = 0;
            s = (s ?? "").Trim();
            if (!NumeroSimples.IsMatch(s)) return false;
            return double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out d);
        }

        /// <summary>Chave para casar a linha da planilha com o objeto: sem espaços extras e, se for número, no formato invariante.</summary>
        public static string ChaveTexto(string s, bool semMaiusculas)
        {
            string n = Normalizar(s, semMaiusculas);
            return Numero(n, out double d) ? d.ToString("R", CultureInfo.InvariantCulture) : n;
        }

        static bool TextoIgual(string a, string b, bool semMaiusculas)
        {
            if (Normalizar(a, semMaiusculas) == Normalizar(b, semMaiusculas)) return true;
            return Numero(a, out double x) && Numero(b, out double y) && Math.Abs(x - y) <= 1e-9 * Math.Max(1, Math.Abs(x));
        }

        public static CampoConfronto Achar(IList<CampoConfronto> campos, string chave, string nome) =>
            campos.FirstOrDefault(c => c.Chave == chave)
            ?? campos.FirstOrDefault(c => !string.IsNullOrEmpty(nome) && c.Rotulo == nome)
            ?? campos.FirstOrDefault(c => !string.IsNullOrEmpty(nome) && string.Equals(c.Nome, nome, StringComparison.CurrentCultureIgnoreCase));

        public static string ChaveIgnorar(string chave, CampoConfronto c, string planilha) => chave + "|" + c?.Chave + "|" + (planilha ?? "").Trim();

        public static ResultadoConfronto Analisar(IFonteConfronto fonte, PadraoConfronto p, IList<LinhaPlanilha> planilha, Action<string> etapa = null)
        {
            var r = new ResultadoConfronto();
            bool sm = p.IgnorarMaiusculas;
            etapa?.Invoke("Lendo os campos do modelo");
            IList<ObjetoConfronto> objetos = fonte.Objetos();
            IList<CampoConfronto> campos = fonte.Campos();
            CampoConfronto campoChave = Achar(campos, p.CampoChave, p.CampoChaveNome);
            if (campoChave == null) { r.Avisos.Add("escolha o campo do modelo que identifica cada linha"); return r; }
            if (p.Mapa?.Coluna(PadraoConfronto.ChaveColuna) == null) { r.Avisos.Add("escolha a coluna da chave"); return r; }

            var pares = new List<(CampoConfronto Campo, string Coluna)>();
            foreach (ComparacaoPadrao cp in p.Comparacoes)
            {
                if (p.Mapa.Coluna(cp.Campo) == null) continue;
                CampoConfronto c = Achar(campos, cp.Campo, cp.CampoNome);
                if (c == null) { r.Avisos.Add($"\"{cp.CampoNome}\" não existe nestes {fonte.Termo}s"); continue; }
                pares.Add((c, cp.Campo));
            }

            // Índice do modelo: valor da chave → objetos.
            etapa?.Invoke("Montando o índice do modelo");
            var indice = new Dictionary<string, List<ObjetoConfronto>>();
            int semChave = 0;
            foreach (ObjetoConfronto o in objetos)
            {
                ValorConfronto v = fonte.Ler(o, campoChave);
                string k = v.Existe ? ChaveTexto(v.Texto, sm) : "";
                if (k.Length == 0) { semChave++; continue; }
                if (!indice.TryGetValue(k, out var l)) indice[k] = l = new List<ObjetoConfronto>();
                l.Add(o);
            }
            if (semChave > 0) r.Avisos.Add($"{semChave} {fonte.Termo}s sem valor em \"{campoChave.Nome}\"");

            // Linhas da planilha (depois do cabeçalho).
            etapa?.Invoke("Comparando");
            var chavesPlanilha = new HashSet<string>();
            int repetidas = 0;
            var porDono = new Dictionary<string, LinhaConfronto>();
            var pedidos = new Dictionary<string, HashSet<string>>();
            var ignorados = new HashSet<string>(p.Ignorados ?? new List<string>());
            foreach (LinhaPlanilha linha in planilha)
            {
                string bruto = (linha[PadraoConfronto.ChaveColuna] ?? "").Trim();
                if (bruto.Length == 0) continue;
                r.LinhasPlanilha++;
                string k = ChaveTexto(bruto, sm);
                if (!chavesPlanilha.Add(k)) { repetidas++; continue; }

                if (!indice.TryGetValue(k, out List<ObjetoConfronto> achados))
                {
                    r.Linhas.Add(new LinhaConfronto
                    {
                        Situacao = LinhaConfronto.NaoEstaNoModelo, Chave = bruto, Linha = linha.Numero, Campo = campoChave.Nome, NaPlanilha = bruto,
                        OQueFazer = $"Conferir a chave na planilha ou o {fonte.Termo} no modelo",
                    });
                    continue;
                }
                r.ObjetosAchados += achados.Count;
                foreach (ObjetoConfronto o in achados)
                {
                    foreach (var (c, col) in pares)
                    {
                        string cel = linha[col] ?? "";
                        if (cel.Trim().Length == 0 && p.VaziaNaoConfere) continue;
                        ValorConfronto v = fonte.Ler(o, c);
                        r.Conferidos++;
                        // Valor que mora no tipo: guarda o que cada linha pede, para achar pedidos diferentes para o mesmo tipo.
                        string g = string.IsNullOrEmpty(v.Dono) ? null : v.Dono + "|" + c.Chave;
                        if (g != null)
                        {
                            if (!pedidos.TryGetValue(g, out var ped)) pedidos[g] = ped = new HashSet<string>();
                            ped.Add(Normalizar(cel, sm));
                        }
                        if (v.Existe && (TextoIgual(v.Texto, cel, sm) || fonte.Iguais(o, c, v.Texto, cel))) { r.Iguais++; continue; }

                        var d = new LinhaConfronto
                        {
                            Elemento = o.Nome, Id = o.Id, Categoria = o.Categoria, Chave = bruto, Campo = c.Rotulo,
                            NoModelo = v.Existe ? v.Texto : "", NaPlanilha = cel.Trim(), Linha = linha.Numero, CampoRef = c,
                            ChaveIgnorar = ChaveIgnorar(k, c, cel),
                        };
                        d.Objetos.Add(o);
                        if (!v.Existe)
                        {
                            d.Situacao = LinhaConfronto.SemCampo;
                            d.OQueFazer = v.Motivo ?? $"Acrescentar \"{c.Nome}\" a este {fonte.Termo}";
                        }
                        else
                        {
                            d.Situacao = v.Texto.Trim().Length == 0 ? LinhaConfronto.VazioModelo
                                : cel.Trim().Length == 0 ? LinhaConfronto.VazioPlanilha : LinhaConfronto.Diferente;
                            if (!v.Editavel) d.OQueFazer = "Corrigir à mão: " + (v.Motivo ?? "somente leitura");
                            else if (!fonte.Preparar(o, c, cel, out object valor, out string motivo)) d.OQueFazer = "Corrigir à mão: " + motivo;
                            else
                            {
                                d.Valor = valor;
                                d.PodeAplicar = true;
                                d.OQueFazer = d.Situacao == LinhaConfronto.VazioPlanilha ? "Limpar o modelo, ou completar a planilha" : "Aplicar a planilha, ou ignorar se o modelo está certo";
                            }
                        }
                        d.Original = (d.Situacao, d.OQueFazer, d.PodeAplicar);
                        if (ignorados.Contains(d.ChaveIgnorar)) { d.Situacao = LinhaConfronto.Ignorado; d.OQueFazer = "Ignorado neste padrão"; d.PodeAplicar = false; }

                        // Uma linha por tipo.
                        if (g != null)
                        {
                            d.Grupo = g;
                            if (porDono.TryGetValue(g, out LinhaConfronto ja))
                            {
                                if (!ja.Objetos.Contains(o)) ja.Objetos.Add(o);
                                ja.Elemento = $"{v.DonoRotulo} ({ja.Objetos.Count} {fonte.Termo}s)";
                                continue;
                            }
                            porDono[g] = d;
                            d.Elemento = v.DonoRotulo;
                        }
                        r.Linhas.Add(d);
                    }
                }
            }
            foreach (LinhaConfronto l in r.Linhas.Where(x => x.Grupo != null && pedidos[x.Grupo].Count > 1))
            {
                l.Situacao = LinhaConfronto.ConflitoTipo;
                l.PodeAplicar = false;
                l.OQueFazer = "Linhas do mesmo tipo pedem valores diferentes: corrigir a planilha";
                l.Original = (l.Situacao, l.OQueFazer, false);
            }
            if (repetidas > 0) r.Avisos.Add($"{repetidas} linhas com chave repetida na planilha (vale a primeira)");

            if (p.MostrarForaDaPlanilha)
                foreach (var kv in indice.Where(kv => !chavesPlanilha.Contains(kv.Key)))
                    foreach (ObjetoConfronto o in kv.Value)
                    {
                        var d = new LinhaConfronto
                        {
                            Situacao = LinhaConfronto.ForaDaPlanilha, Elemento = o.Nome, Id = o.Id, Categoria = o.Categoria,
                            Chave = fonte.Ler(o, campoChave).Texto, Campo = campoChave.Nome,
                            OQueFazer = "Incluir na planilha, ou conferir a chave no modelo",
                        };
                        d.NoModelo = d.Chave;
                        d.Objetos.Add(o);
                        r.Linhas.Add(d);
                    }
            return r;
        }

        /// <summary>Relatório .csv (abre no Excel em português: separador ";" e UTF-8 com BOM).</summary>
        public static void Relatorio(string arquivo, IEnumerable<LinhaConfronto> linhas, string termo)
        {
            string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(";", new[] { "Situação", termo, "Id", "Categoria", "Chave", "Campo", "No modelo", "Na planilha", "Linha da planilha", "O que fazer" }.Select(Q)));
            foreach (LinhaConfronto l in linhas)
                sb.AppendLine(string.Join(";", new[] { l.Situacao, l.Elemento, l.Id, l.Categoria, l.Chave, l.Campo, l.NoModelo, l.NaPlanilha,
                    l.Linha > 0 ? l.Linha.ToString(CultureInfo.InvariantCulture) : "", l.OQueFazer }.Select(Q)));
            File.WriteAllText(arquivo, sb.ToString(), new UTF8Encoding(true));
        }
    }
}
