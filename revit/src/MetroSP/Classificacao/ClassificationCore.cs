using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;

namespace MetroSP.Classificacao
{
    /// <summary>Planilha e parâmetro usados, lembrados por usuário em %AppData%\MetroSP\classificacao.ini.</summary>
    public class ClassSettings
    {
        private static readonly string Caminho =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MetroSP", "classificacao.ini");

        public string Planilha { get; set; }
        public string Parametro { get; set; } = "ClassificationCode";

        public static ClassSettings Abrir()
        {
            var s = new ClassSettings();
            try
            {
                if (!File.Exists(Caminho)) return s;
                foreach (string l in File.ReadAllLines(Caminho, Encoding.UTF8))
                {
                    string[] p = l.Split(new[] { '=' }, 2);
                    if (p.Length != 2 || p[1].Length == 0) continue;
                    if (p[0] == "planilha") s.Planilha = p[1];
                    else if (p[0] == "parametro") s.Parametro = p[1];
                }
            }
            catch { /* configuração corrompida: padrão */ }
            return s;
        }

        public void Salvar()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Caminho));
                File.WriteAllLines(Caminho, new[] { "planilha=" + Planilha, "parametro=" + Parametro }, Encoding.UTF8);
            }
            catch { /* não é crítico */ }
        }
    }

    /// <summary>
    /// Valor do ClassificationCode no formato do exportador IFC do Revit:
    /// "Código:Descrição", opcionalmente com o nome da classificação: "[Nome]Código:Descrição".
    /// </summary>
    public class ValorClassificacao
    {
        public string Sistema { get; set; }
        public string Codigo { get; set; }
        public string Descricao { get; set; }
        public bool Valido { get; set; }

        private static readonly Regex Formato = new Regex(@"^\s*(?:\[(?<s>[^\]]*)\])?\s*(?<c>[^:]+?)\s*:\s*(?<d>.*?)\s*$");

        public static ValorClassificacao Ler(string texto)
        {
            var v = new ValorClassificacao();
            if (string.IsNullOrWhiteSpace(texto)) return v;
            Match m = Formato.Match(texto);
            if (m.Success && m.Groups["d"].Value.Length > 0)
            {
                v.Sistema = m.Groups["s"].Success ? m.Groups["s"].Value : null;
                v.Codigo = m.Groups["c"].Value;
                v.Descricao = m.Groups["d"].Value;
                v.Valido = true;
            }
            return v;
        }

        public static string Escrever(string codigo, string descricao) => $"{codigo}:{descricao}";
    }

    /// <summary>Onde o parâmetro está num elemento (na instância ou no tipo) e o valor atual.</summary>
    public class AlvoClassificacao
    {
        public Element Elemento { get; set; }
        public Element Dono { get; set; }
        public Parameter Parametro { get; set; }
        public bool NoTipo => Dono != null && Dono.Id != Elemento.Id;
        public string Valor => Parametro?.AsString() ?? "";
    }

    public class LinhaVerificacao
    {
        public string Situacao { get; set; }
        public string Elemento { get; set; }
        public long Id { get; set; }
        public string Categoria { get; set; }
        public string Preenchido { get; set; }
        public string NaLista { get; set; }
        public string Proposta { get; set; }
        public string Onde { get; set; }
        /// <summary>Por que não dá para gravar (vazio = editável).</summary>
        public string Bloqueio { get; set; }
        /// <summary>Família cuja fórmula trava o parâmetro (null = não é fórmula).</summary>
        public ElementId FamiliaFormula { get; set; }

        public ElementId ElementoId { get; set; }
        public ElementId DonoId { get; set; }
        /// <summary>Valor a gravar se a proposta for aceita (null = sem correção automática).</summary>
        public string ValorProposto { get; set; }
        /// <summary>true quando a proposta é segura (mesma classificação, só a escrita muda).</summary>
        public bool PropostaSegura { get; set; }
    }

    /// <summary>Um código usado no modelo e as descrições encontradas para ele.</summary>
    public class GrupoCodigo
    {
        public string Codigo { get; set; }
        public ClassItem NaLista { get; set; }
        public List<(string Descricao, List<ElementId> Donos, List<ElementId> Elementos)> Descricoes { get; } = new List<(string, List<ElementId>, List<ElementId>)>();
        public string Escolhida { get; set; }
    }

    public static class ClassificationCore
    {
        public const string OK = "OK";
        public const string SemClassificacao = "Sem classificação";
        public const string FormatoInvalido = "Formato inválido";
        public const string ForaDaLista = "Código fora da lista";
        public const string DescricaoDiferente = "Descrição diferente da lista";
        public const string CategoriaNaoPrevista = "Categoria não prevista";
        public const string SemParametro = "Sem o parâmetro";
        public const string PrefixoFormula = "Fórmula na família ";

        // ------------------------------------------------------------------ elementos

        /// <param name="soIfc">Só os elementos que vão para o IFC (regra de todas as ferramentas MetroSP).</param>
        public static List<Element> Elementos(Document doc, ICollection<ElementId> selecao, ElementId vista, bool soIfc = true)
        {
            IEnumerable<Element> elems = selecao != null
                ? selecao.Select(doc.GetElement).Where(e => e != null && !(e is ElementType))
                : (vista != null ? new FilteredElementCollector(doc, vista) : new FilteredElementCollector(doc)).WhereElementIsNotElementType();
            elems = elems.Where(e => e.Category != null && e.Category.CategoryType == CategoryType.Model && !(e is View) &&
                                     !(e is RevitLinkInstance) && !(e is ImportInstance)); // vínculos: classificar no arquivo de origem
            if (soIfc)
            {
                ElementId fase = Ids.FiltroIfc.Fase(doc, -1, vista);
                elems = elems.Where(e => Ids.FiltroIfc.VaiParaIfc(doc, e, fase));
            }
            return elems.ToList();
        }

        /// <summary>
        /// Parâmetro de texto com esse nome. Se houver mais de um com o mesmo nome (ex.: compartilhado e de projeto),
        /// prefere o editável e, entre eles, o compartilhado.
        /// </summary>
        public static Parameter Param(Element e, string nome) =>
            e.GetParameters(nome).Where(p => p.StorageType == StorageType.String)
             .OrderBy(p => p.IsReadOnly).ThenBy(p => p.IsShared ? 0 : 1).FirstOrDefault();

        /// <summary>Por que o parâmetro não pode ser gravado neste elemento (melhor explicação encontrada).</summary>
        public static string MotivoSomenteLeitura(Document doc, Element e, Parameter p, string nome)
        {
            if (!(e is ElementType) && e.GroupId != ElementId.InvalidElementId)
                return "Em grupo, com o parâmetro alinhado por tipo de grupo";
            try
            {
                if (p.GetAssociatedGlobalParameter() != ElementId.InvalidElementId)
                    return "Associado a um parâmetro global (mude o valor do global ou desassocie)";
            }
            catch { }
            if (e.DesignOption != null && e.DesignOption.Id != DesignOption.GetActiveDesignOptionId(doc) && !e.DesignOption.IsPrimary)
                return $"Em opção de projeto não ativa ({e.DesignOption.Name})";
            string chave = TabelaDeChave(doc, e, nome);
            if (chave != null) return chave;
            Family familia = FormulaFamilia.Familia(doc, e);
            string formula = FormulaFamilia.Formula(doc, familia, nome);
            if (formula != null) return $"{PrefixoFormula}\"{familia.Name}\": {formula}";
            if (!p.UserModifiable)
                return "Parâmetro compartilhado criado como não editável (USERMODIFIABLE=0 no .txt)";
            if (e.GetParameters(nome).Count > 1)
                return $"Há mais de um parâmetro \"{nome}\" no elemento e nenhum é editável";
            return "Somente leitura (controlado pelo Revit)";
        }

        private static readonly Dictionary<(long Cat, string Nome), string> _chaves = new Dictionary<(long, string), string>();

        /// <summary>Tabela de chave da categoria que controla o parâmetro, se houver.</summary>
        private static string TabelaDeChave(Document doc, Element e, string nome)
        {
            if (e.Category == null) return null;
            var k = (e.Category.Id.Value, nome);
            if (_chaves.TryGetValue(k, out string m)) return m;
            m = null;
            foreach (ViewSchedule v in new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>())
            {
                try
                {
                    ScheduleDefinition d = v.Definition;
                    if (!d.IsKeySchedule || d.CategoryId != e.Category.Id) continue;
                    bool tem = d.GetFieldOrder().Select(id => d.GetField(id)).Any(f => f.GetName() == nome);
                    if (tem) { m = $"Controlado pela tabela de chave \"{v.Name}\" (mude na tabela ou deixe a chave vazia)"; break; }
                }
                catch { }
            }
            _chaves[k] = m;
            return m;
        }

        /// <summary>O parâmetro na instância; se não houver, no tipo.</summary>
        public static AlvoClassificacao Alvo(Document doc, Element e, string parametro)
        {
            Parameter p = Param(e, parametro);
            if (p != null) return new AlvoClassificacao { Elemento = e, Dono = e, Parametro = p };
            Element tipo = doc.GetElement(e.GetTypeId());
            Parameter pt = tipo == null ? null : Param(tipo, parametro);
            if (pt != null && pt.StorageType == StorageType.String) return new AlvoClassificacao { Elemento = e, Dono = tipo, Parametro = pt };
            return new AlvoClassificacao { Elemento = e };
        }

        public static string NomeElemento(Document doc, Element e)
        {
            string tipo = (doc.GetElement(e.GetTypeId()) as ElementType)?.Name;
            string familia = (e as FamilyInstance)?.Symbol?.FamilyName;
            string nome = familia != null ? $"{familia}: {tipo}" : tipo ?? e.Name;
            return string.IsNullOrEmpty(nome) ? e.Name : nome;
        }

        // ------------------------------------------------------------------ verificar

        public static List<LinhaVerificacao> Verificar(Document doc, IEnumerable<Element> elementos, ClassificationList lista, string parametro)
        {
            var linhas = new List<LinhaVerificacao>();
            _chaves.Clear();
            FormulaFamilia.LimparCache();
            List<Element> todos = elementos.ToList();
            Ui.Progresso.Atual?.Etapa($"Analisando {todos.Count} elementos", todos.Count);
            foreach (Element e in todos)
            {
                Ui.Progresso.Atual?.Avancar(e.Category?.Name);
                AlvoClassificacao a = Alvo(doc, e, parametro);
                var l = new LinhaVerificacao
                {
                    Elemento = NomeElemento(doc, e),
                    Id = e.Id.Value,
                    ElementoId = e.Id,
                    Categoria = e.Category?.Name,
                    DonoId = a.Dono?.Id,
                    Onde = a.Parametro == null ? "" : a.NoTipo ? "tipo" : e.GroupId != ElementId.InvalidElementId ? "instância (em grupo)" : "instância",
                };
                linhas.Add(l);
                if (a.Parametro != null && a.Parametro.IsReadOnly)
                {
                    l.Bloqueio = MotivoSomenteLeitura(doc, a.Dono, a.Parametro, parametro);
                    if (l.Bloqueio.StartsWith(PrefixoFormula)) l.FamiliaFormula = FormulaFamilia.Familia(doc, a.Dono)?.Id;
                }
                if (a.Parametro == null) { l.Situacao = SemParametro; l.Proposta = $"Vincule o parâmetro {parametro} à categoria"; continue; }

                string valor = a.Valor;
                l.Preenchido = valor;
                if (string.IsNullOrWhiteSpace(valor)) { l.Situacao = SemClassificacao; l.Proposta = "Aplicar classificação"; continue; }

                ValorClassificacao v = ValorClassificacao.Ler(valor);
                ClassItem item = null;
                if (!v.Valido)
                {
                    // Tenta reconhecer o código no começo do texto (ex.: "EST 02 03 01 - Laje").
                    item = ProcurarPorTexto(lista, valor);
                    l.Situacao = FormatoInvalido;
                    l.Proposta = "Formato esperado Código:Descrição";
                    if (item != null)
                    {
                        l.NaLista = ValorClassificacao.Escrever(item.Codigo, item.Descricao);
                        l.ValorProposto = l.NaLista;
                        l.Proposta = $"Reescrever como {l.NaLista}";
                    }
                    continue;
                }

                if (!lista.PorCodigo.TryGetValue(v.Codigo, out item))
                {
                    l.Situacao = ForaDaLista;
                    ClassItem parecido = Parecido(lista, v.Codigo, v.Descricao);
                    if (parecido != null)
                    {
                        l.NaLista = ValorClassificacao.Escrever(parecido.Codigo, parecido.Descricao);
                        l.ValorProposto = l.NaLista;
                        l.Proposta = $"Código parecido: {parecido.Codigo}";
                    }
                    else l.Proposta = "Escolha na lista (Aplicar classificação)";
                    continue;
                }

                l.NaLista = ValorClassificacao.Escrever(item.Codigo, item.Descricao);
                if (!item.ValeParaCategoria(e.Category?.Name))
                {
                    l.Situacao = CategoriaNaoPrevista;
                    l.Proposta = "Item vale para " + string.Join(", ", item.Categorias);
                    continue;
                }
                if (v.Descricao != item.Descricao || v.Codigo != item.Codigo || v.Sistema != null)
                {
                    l.Situacao = DescricaoDiferente;
                    l.Proposta = "Usar a escrita da lista" + Diferenca(v.Descricao, item.Descricao);
                    l.ValorProposto = l.NaLista;
                    l.PropostaSegura = true;
                    if (v.Sistema != null && v.Descricao == item.Descricao && v.Codigo == item.Codigo)
                    {
                        // [Nome] no começo é aceito pelo IFC: não é erro.
                        l.Situacao = OK;
                        l.Proposta = null;
                        l.ValorProposto = null;
                    }
                    continue;
                }
                l.Situacao = OK;
            }
            return linhas;
        }

        /// <summary>Explica a diferença de escrita (maiúsculas, espaços, acentos ou texto diferente).</summary>
        private static string Diferenca(string atual, string lista)
        {
            if (atual == null || lista == null) return "";
            if (string.Equals(atual, lista, StringComparison.CurrentCultureIgnoreCase)) return " (maiúsculas/minúsculas)";
            if (Normalizar(atual) == Normalizar(lista))
                return Regex.IsMatch(atual, @"\s{2,}") || atual != atual.Trim() ? " (espaços)" : " (acentos ou pontuação)";
            return "";
        }

        /// <summary>Minúsculas, sem acentos, sem pontuação e com espaços simples.</summary>
        public static string Normalizar(string s)
        {
            if (s == null) return "";
            string d = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char c in d)
            {
                UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(c);
                if (cat == UnicodeCategory.NonSpacingMark) continue;
                sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
            }
            return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        }

        private static string SoAlfanumerico(string s) => new string((s ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

        private static ClassItem ProcurarPorTexto(ClassificationList lista, string texto)
        {
            string t = SoAlfanumerico(texto);
            return lista.Itens.Where(i => t.StartsWith(SoAlfanumerico(i.Codigo)))
                .OrderByDescending(i => SoAlfanumerico(i.Codigo).Length).FirstOrDefault();
        }

        /// <summary>Código da lista mais parecido: mesmo código sem separadores, ou poucas letras de diferença com descrição parecida.</summary>
        private static ClassItem Parecido(ClassificationList lista, string codigo, string descricao)
        {
            string c = SoAlfanumerico(codigo);
            ClassItem igual = lista.Itens.FirstOrDefault(i => SoAlfanumerico(i.Codigo) == c);
            if (igual != null) return igual;
            string d = Normalizar(descricao);
            var candidatos = lista.Itens
                .Select(i => (Item: i, Dist: Distancia(c, SoAlfanumerico(i.Codigo)), Desc: Normalizar(i.Descricao)))
                .Where(x => x.Dist <= 2)
                .OrderBy(x => x.Dist)
                .ThenByDescending(x => PalavrasEmComum(d, x.Desc))
                .ToList();
            return candidatos.FirstOrDefault().Item;
        }

        private static int PalavrasEmComum(string a, string b) =>
            a.Split(' ').Intersect(b.Split(' ')).Count(w => w.Length > 2);

        private static int Distancia(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length];
        }

        // ------------------------------------------------------------------ consistência

        /// <summary>Agrupa por código os valores usados no modelo; mostra só códigos com mais de uma descrição.</summary>
        public static List<GrupoCodigo> Consistencia(Document doc, IEnumerable<Element> elementos, ClassificationList lista, string parametro,
            out List<(string Descricao, List<string> Codigos)> mesmaDescricao)
        {
            var porCodigo = new Dictionary<string, Dictionary<string, (HashSet<ElementId> Donos, List<ElementId> Elems)>>(StringComparer.OrdinalIgnoreCase);
            foreach (Element e in elementos)
            {
                AlvoClassificacao a = Alvo(doc, e, parametro);
                if (a.Parametro == null) continue;
                ValorClassificacao v = ValorClassificacao.Ler(a.Valor);
                if (!v.Valido) continue;
                if (!porCodigo.TryGetValue(v.Codigo, out var descs)) porCodigo[v.Codigo] = descs = new Dictionary<string, (HashSet<ElementId>, List<ElementId>)>();
                if (!descs.TryGetValue(v.Descricao, out var x)) descs[v.Descricao] = x = (new HashSet<ElementId>(), new List<ElementId>());
                x.Donos.Add(a.Dono.Id);
                x.Elems.Add(e.Id);
            }

            var grupos = new List<GrupoCodigo>();
            foreach (var kv in porCodigo.Where(k => k.Value.Count > 1).OrderBy(k => k.Key))
            {
                var g = new GrupoCodigo { Codigo = kv.Key };
                g.NaLista = lista != null && lista.PorCodigo.TryGetValue(kv.Key, out ClassItem it) ? it : null;
                foreach (var d in kv.Value.OrderByDescending(d => d.Value.Elems.Count))
                    g.Descricoes.Add((d.Key, d.Value.Donos.ToList(), d.Value.Elems));
                g.Escolhida = g.NaLista != null ? g.NaLista.Descricao : g.Descricoes[0].Descricao;
                grupos.Add(g);
            }

            // Mesma descrição (normalizada) usada com códigos diferentes.
            mesmaDescricao = porCodigo
                .SelectMany(k => k.Value.Keys.Select(d => (Codigo: k.Key, Desc: d)))
                .GroupBy(x => Normalizar(x.Desc))
                .Where(g => g.Select(x => x.Codigo).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                .Select(g => (g.First().Desc, g.Select(x => x.Codigo).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c).ToList()))
                .ToList();
            return grupos;
        }

        // ------------------------------------------------------------------ gravar

        /// <summary>
        /// Grava valores (um por dono: instância ou tipo) numa única transação, tratando os bloqueios:
        /// elementos em grupo (oferece ligar "variar por instância de grupo" no parâmetro),
        /// worksharing (pega emprestado o que estiver livre) e explica o motivo de cada falha.
        /// </summary>
        /// <param name="confirmar">Pergunta ao usuário (true = sim). Usada antes de mudar a definição do parâmetro.</param>
        public static ResultadoGravacao Gravar(Document doc, string parametro, IEnumerable<(ElementId Dono, string Valor)> valores,
            string nomeTransacao, Func<string, bool> confirmar)
        {
            var r = new ResultadoGravacao();
            var pendentes = new List<(Element Dono, Parameter P, string Valor)>();
            foreach (var (donoId, valor) in valores.GroupBy(v => v.Dono).Select(g => g.First()))
            {
                Element dono = doc.GetElement(donoId);
                Parameter p = dono == null ? null : Param(dono, parametro);
                if (dono == null) r.Falhou(donoId, "Elemento não encontrado (apagado?)");
                else if (p == null) r.Falhou(donoId, $"Sem o parâmetro {parametro}");
                else pendentes.Add((dono, p, valor)); // somente leitura é tratado depois dos grupos (membros de grupo aparecem como somente leitura)
            }

            // Grupos: parâmetro de instância só pode ser alterado nos membros se puder variar por instância de grupo.
            var emGrupo = pendentes.Where(x => !(x.Dono is ElementType) && x.Dono.GroupId != ElementId.InvalidElementId).ToList();
            InternalDefinition def = emGrupo.Select(x => x.P.Definition).OfType<InternalDefinition>().FirstOrDefault();
            if (emGrupo.Count > 0 && def != null && !def.VariesAcrossGroups)
            {
                bool liberar = confirmar(
                    $"{emGrupo.Count} elementos estão dentro de grupos, e {parametro} está com os valores alinhados por tipo de grupo, " +
                    "por isso o Revit não deixa gravar neles.\n\n" +
                    $"Ligar \"Os valores podem variar por instância de grupo\" em {parametro}?\n\n" +
                    "Os grupos continuam como estão; só esse parâmetro passa a poder ser diferente em cada instância de grupo. " +
                    "Pode ser desfeito (Ctrl+Z ou em Parâmetros de projeto).");
                if (liberar)
                {
                    try
                    {
                        using (var t = new Transaction(doc, "MetroSP: " + parametro + " varia por instância de grupo"))
                        {
                            t.Start();
                            def.SetAllowVaryBetweenGroups(doc, true);
                            t.Commit();
                        }
                        r.GrupoLiberado = true;
                    }
                    catch (Exception ex)
                    {
                        r.Avisos.Add("Não foi possível liberar o parâmetro nos grupos: " + ex.Message);
                    }
                }
                if (!r.GrupoLiberado)
                {
                    foreach (var x in emGrupo) r.Falhou(x.Dono.Id, "Em grupo (valores alinhados por tipo de grupo)");
                    pendentes = pendentes.Except(emGrupo).ToList();
                }
            }

            // Daqui em diante não há mais perguntas: mostra o andamento quando a gravação é grande.
            using Ui.Progresso progresso = Ui.Progresso.Atual == null && pendentes.Count >= 200 ? Ui.Progresso.Abrir(nomeTransacao) : null;

            // Worksharing: pega emprestado; o que estiver com outra pessoa fica de fora, com o nome do dono.
            if (doc.IsWorkshared && pendentes.Count > 0)
            {
                Ui.Progresso.Atual?.Etapa($"Pegando {pendentes.Count} elementos emprestados (worksharing)");
                var ids = pendentes.Select(x => x.Dono.Id).ToList();
                ICollection<ElementId> obtidos;
                try { obtidos = WorksharingUtils.CheckoutElements(doc, ids); }
                catch (Exception ex)
                {
                    obtidos = new HashSet<ElementId>();
                    r.Avisos.Add("Não foi possível pegar elementos emprestados: " + ex.Message);
                }
                var livres = new List<(Element, Parameter, string)>();
                foreach (var x in pendentes)
                {
                    if (obtidos.Contains(x.Dono.Id)) { livres.Add(x); continue; }
                    string dono = null;
                    try { dono = WorksharingUtils.GetWorksharingTooltipInfo(doc, x.Dono.Id).Owner; } catch { }
                    ModelUpdatesStatus st = ModelUpdatesStatus.CurrentWithCentral;
                    try { st = WorksharingUtils.GetModelUpdatesStatus(doc, x.Dono.Id); } catch { }
                    r.Falhou(x.Dono.Id,
                        st == ModelUpdatesStatus.UpdatedInCentral ? "Desatualizado em relação ao central: sincronize e tente de novo"
                        : !string.IsNullOrEmpty(dono) ? $"Em uso por {dono} (worksharing)"
                        : "Não foi possível pegar emprestado (worksharing)");
                }
                r.Emprestados = obtidos.Count;
                pendentes = livres;
            }

            // Somente leitura que sobrou: explica o motivo (o parâmetro é lido de novo, pois liberar os grupos muda isso).
            var editaveis = new List<(Element Dono, Parameter P, string Valor)>();
            _chaves.Clear();
            Ui.Progresso.Atual?.Etapa("Conferindo o que pode ser gravado");
            foreach (var (dono, _, valor) in pendentes)
            {
                Parameter p = Param(dono, parametro);
                if (p == null) r.Falhou(dono.Id, $"Sem o parâmetro {parametro}");
                else if (p.IsReadOnly) r.Falhou(dono.Id, MotivoSomenteLeitura(doc, dono, p, parametro));
                else editaveis.Add((dono, p, valor));
            }
            pendentes = editaveis;

            if (pendentes.Count == 0) return r;
            Ui.Progresso.Atual?.Etapa($"Gravando {parametro} em {pendentes.Count} elementos", pendentes.Count);
            using (var t = new Transaction(doc, "MetroSP: " + nomeTransacao))
            {
                t.Start();
                foreach (var (dono, p, valor) in pendentes)
                {
                    Ui.Progresso.Atual?.Avancar(dono.Name);
                    string erro = null;
                    try { if (!p.Set(valor)) erro = "O Revit recusou o valor"; }
                    catch (Exception ex) { erro = dono.GroupId != ElementId.InvalidElementId ? "Em grupo: " + ex.Message : ex.Message; }
                    if (erro == null) r.Ok++; else r.Falhou(dono.Id, erro);
                }
                t.Commit();
            }
            return r;
        }
    }

    /// <summary>O que foi gravado e, para o que não foi, o motivo.</summary>
    public class ResultadoGravacao
    {
        public int Ok { get; set; }
        public int Emprestados { get; set; }
        public bool GrupoLiberado { get; set; }
        public List<(ElementId Id, string Motivo)> Falhas { get; } = new List<(ElementId, string)>();
        public List<string> Avisos { get; } = new List<string>();

        public void Falhou(ElementId id, string motivo) => Falhas.Add((id, motivo));

        /// <summary>Texto para mostrar ao usuário: gravados e falhas agrupadas por motivo.</summary>
        public string Resumo(string oQue = "gravações")
        {
            var sb = new StringBuilder();
            sb.Append($"{Ok} {oQue} feitas.");
            if (GrupoLiberado) sb.Append("\nO parâmetro agora pode variar por instância de grupo.");
            if (Emprestados > 0) sb.Append($"\n{Emprestados} elementos foram pegos emprestados (sincronize para devolvê-los).");
            if (Falhas.Count > 0)
            {
                sb.Append($"\n\n{Falhas.Count} não puderam ser gravados:");
                foreach (var g in Falhas.GroupBy(f => f.Motivo).OrderByDescending(g => g.Count()))
                    sb.Append($"\n•  {g.Count()}: {g.Key}");
            }
            foreach (string a in Avisos) sb.Append("\n\n" + a);
            return sb.ToString();
        }
    }
}
