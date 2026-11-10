using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using Autodesk.Revit.DB;
using MetroSP.Ui;
using Grid = System.Windows.Controls.Grid;
using Control = System.Windows.Controls.Control;
using Binding = System.Windows.Data.Binding;
using Visibility = System.Windows.Visibility;

namespace MetroSP.Classificacao
{
    /// <summary>
    /// Verifica se as classificações do padrão (uma ou mais por elemento, nos parâmetros da Autodesk ou personalizados)
    /// conferem com as planilhas: formato, código existente, descrição igual à da lista e categoria prevista.
    /// Cada linha tem um botão com a ação para a situação; as propostas seguras podem ser aplicadas de uma vez.
    /// Modal: as transações rodam no contexto do comando.
    /// </summary>
    public class VerificarClassificacaoWindow : Window
    {
        private readonly Autodesk.Revit.UI.UIDocument _uidoc;
        private readonly Document _doc;
        private readonly ClassSettings _config;
        private readonly ICollection<ElementId> _selecao;
        private readonly ComboBox _padraoCombo;
        private readonly TextBlock _padraoInfo;
        private PadraoClassificacao _padrao;
        private bool _trocandoPadrao;
        private Button _escolherPlanilha;
        private readonly ComboBox _escopo;
        private readonly SeletorCategorias _categorias = new SeletorCategorias { Margin = new Thickness(0, 0, 12, 0) };
        private readonly Button _reanalisar;
        private List<Element> _base = new List<Element>();
        private bool _ocultarOk;
        private string _situacao; // etiqueta clicada (filtra a tabela)
        private readonly CheckBox _incluirInseguras;
        private readonly WrapPanel _resumo;
        private readonly DockPanel _areaTabela;
        private readonly TextBlock _rodapeInfo;
        private readonly Button _aplicar;
        private readonly Button _aplicarSelecionadas;
        private readonly Button _exportar;
        private readonly Button _selecionar;
        private List<LinhaVerificacao> _linhas;
        private FilterableGrid _tabela;

        public VerificarClassificacaoWindow(Autodesk.Revit.UI.UIDocument uidoc, ClassSettings config)
        {
            _uidoc = uidoc;
            _doc = uidoc.Document;
            _config = config;
            ICollection<ElementId> sel = uidoc.Selection.GetElementIds();
            _selecao = sel.Count > 0 ? sel : null;

            Title = "MetroSP - Verificar classificação";
            Width = 1200;
            Height = 740;
            MinWidth = 820;
            MinHeight = 480;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var linhaPadrao = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var rotPadrao = Theme.Rotulo("Padrão");
            DockPanel.SetDock(rotPadrao, Dock.Left);
            linhaPadrao.Children.Add(rotPadrao);
            _padraoCombo = new ComboBox { MinWidth = 220, Margin = new Thickness(0, 0, 8, 0), ToolTip = "Conjunto de classificações a conferir, com a planilha de cada uma." };
            _padraoCombo.SelectionChanged += (s, e) => { if (!_trocandoPadrao) UsarPadrao(_padraoCombo.SelectedItem as PadraoClassificacao); };
            DockPanel.SetDock(_padraoCombo, Dock.Left);
            linhaPadrao.Children.Add(_padraoCombo);
            var editar = new Button { Content = "Editar padrões...", ToolTip = "Criar ou mudar padrões: classificações (modelos da Autodesk ou personalizadas), parâmetros e as colunas da planilha." };
            editar.Click += (s, e) => EditarPadroes();
            DockPanel.SetDock(editar, Dock.Left);
            linhaPadrao.Children.Add(editar);
            _escolherPlanilha = new Button
            {
                Content = "Escolher planilha...",
                Style = Theme.PrimaryButton,
                Margin = new Thickness(8, 0, 0, 0),
                Visibility = System.Windows.Visibility.Collapsed,
                ToolTip = "Abre a planilha (.xlsx ou .csv) com a lista de classificação. As colunas são sugeridas pelos títulos; dá para mudar em \"Editar padrões...\".",
            };
            _escolherPlanilha.Click += (s, e) => EscolherPlanilha();
            DockPanel.SetDock(_escolherPlanilha, Dock.Left);
            linhaPadrao.Children.Add(_escolherPlanilha);
            _padraoInfo = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            linhaPadrao.Children.Add(_padraoInfo);

            _escopo = new ComboBox { Padding = new Thickness(4, 3, 4, 3), MinWidth = 220, Margin = new Thickness(0, 0, 12, 0) };
            if (_selecao != null) _escopo.Items.Add($"Seleção atual ({_selecao.Count} elementos)");
            _escopo.Items.Add("Vista atual");
            _escopo.Items.Add("Todo o modelo");
            _escopo.SelectedIndex = 0;
            var verificar = new Button { Content = "Verificar", Style = Theme.PrimaryButton };
            verificar.Click += (s, e) => Verificar();
            var barra = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            barra.Children.Add(Theme.Rotulo("Escopo"));
            barra.Children.Add(_escopo);
            barra.Children.Add(_categorias);
            barra.Children.Add(verificar);
            // A Consistência saiu da Ribbon (0.3.3): abre daqui, com a mesma planilha.
            var consistencia = new Button
            {
                Content = "Códigos × descrições...",
                Margin = new Thickness(8, 0, 0, 0),
                ToolTip = "Encontra códigos usados com descrições diferentes no modelo e unifica pela descrição escolhida " +
                          "(antes era o botão Consistência da Ribbon).",
            };
            consistencia.Click += (s, e) => AbrirConsistencia();
            barra.Children.Add(consistencia);
            _escopo.SelectionChanged += (s, e) => CarregarEscopo();
            _categorias.Mudou += () => { if (_linhas != null) Verificar(); };

            _resumo = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            _incluirInseguras = new CheckBox
            {
                Content = "Incluir propostas que trocam o código (código parecido, texto fora do formato)",
                ToolTip = "Sem esta opção, só são aplicadas as correções de escrita: mesmo código, descrição como na lista.",
                Margin = new Thickness(0, 0, 0, 8),
            };
            _incluirInseguras.Click += (s, e) => AtualizarRodape();
            _areaTabela = new DockPanel();
            _areaTabela.Children.Add(new TextBlock
            {
                Text = "Escolha o padrão e clique em \"Verificar\". Cada classificação é lida na instância ou, se não houver, no tipo.",
                Foreground = Theme.Solid(Theme.Cinza),
            });

            var corpo = new DockPanel();
            foreach (UIElement el in new UIElement[] { linhaPadrao, barra, _resumo, _incluirInseguras })
            {
                DockPanel.SetDock(el, Dock.Top);
                corpo.Children.Add(el);
            }
            corpo.Children.Add(_areaTabela);

            _rodapeInfo = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            _exportar = new Button { Content = "Exportar (.csv)", IsEnabled = false };
            _selecionar = new Button { Content = "Selecionar no modelo", IsEnabled = false, ToolTip = "Seleciona no Revit os elementos visíveis na tabela." };
            _aplicar = new Button { Content = "Aplicar propostas", Style = Theme.PrimaryButton, IsEnabled = false };
            _reanalisar = new Button { Content = "Reanalisar", IsEnabled = false, ToolTip = "Lê o modelo de novo, mantendo escopo, categorias e filtros da tabela." };
            _reanalisar.Click += (s, e) => Verificar();
            var fechar = new Button { Content = "Fechar", IsCancel = true };
            _exportar.Click += (s, e) => Exportar();
            _selecionar.Click += (s, e) => Selecionar();
            _aplicar.Click += (s, e) => Aplicar(false);
            _aplicarSelecionadas = new Button
            {
                Content = "Aplicar selecionadas",
                IsEnabled = false,
                ToolTip = "Aplica só as propostas das linhas selecionadas na tabela (Ctrl/Shift+clique para escolher várias). Inclui as que trocam o código.",
            };
            _aplicarSelecionadas.Click += (s, e) => Aplicar(true);
            fechar.Click += (s, e) => Close();
            var rodape = new DockPanel();
            StackPanel botoes = Theme.Buttons(_reanalisar, _exportar, _selecionar, _aplicarSelecionadas, _aplicar, fechar);
            DockPanel.SetDock(botoes, Dock.Right);
            rodape.Children.Add(botoes);
            rodape.Children.Add(_rodapeInfo);

            Theme.Apply(this, "Verificar classificação", _doc.Title + " · confere as classificações com as planilhas do padrão", corpo, rodape);
            CarregarEscopo();
            CarregarPadroes(config.Padrao);
        }

        /// <summary>Abre a conferência código × descrição (modal, no mesmo contexto da API desta janela).</summary>
        private void AbrirConsistencia()
        {
            var janela = new ConsistenciaClassificacaoWindow(_uidoc, _config) { Owner = this };
            janela.ShowDialog();
        }

        // ------------------------------------------------------------------ padrões

        private void CarregarPadroes(string escolher)
        {
            List<PadraoClassificacao> padroes = PadraoClassificacao.Listar();
            if (padroes.Count == 0)
            {
                // Primeiro uso: a planilha e o parâmetro que a classificação já usava viram o primeiro padrão.
                PadraoClassificacao p = PadraoClassificacao.DoLegado(_config);
                try { p.Salvar(); } catch { }
                padroes.Add(p);
            }
            _trocandoPadrao = true;
            _padraoCombo.ItemsSource = padroes;
            _padraoCombo.SelectedItem = padroes.FirstOrDefault(p => p.Nome == escolher) ?? padroes[0];
            _trocandoPadrao = false;
            UsarPadrao(_padraoCombo.SelectedItem as PadraoClassificacao);
        }

        private void UsarPadrao(PadraoClassificacao p)
        {
            _padrao = p;
            if (p == null) { _padraoInfo.Text = ""; return; }
            _config.Padrao = p.Nome;
            _config.Salvar();
            var cache = new Dictionary<string, Planilha.Tabela>(StringComparer.OrdinalIgnoreCase);
            Cursor = Cursors.Wait;
            try { foreach (SistemaClassificacao s in p.Sistemas) s.Carregar(cache); }
            finally { Cursor = null; }
            _padraoInfo.Text = string.Join("  ·  ", p.Sistemas.Select(s => s.Lista != null && s.Erro == null
                ? $"{s.Nome}: {s.Lista.Itens.Count} itens em {s.Parametros}"
                : $"{s.Nome}: {s.Erro}"));
            _padraoInfo.ToolTip = string.Join("\n", p.Sistemas.Select(s =>
                $"{s.Nome}  ·  {s.Parametros}  ·  {s.Planilha?.NomeArquivo}" + (string.IsNullOrEmpty(s.Planilha?.Aba) ? "" : $" › {s.Planilha.Aba}") +
                (s.Lista?.Avisos.Count > 0 ? "\n   " + string.Join("\n   ", s.Lista.Avisos.Take(10)) : "")));
            _padraoInfo.Foreground = Theme.Solid(p.Sistemas.Any(s => s.Lista == null || s.Erro != null) ? Theme.LaranjaTexto : Theme.Cinza);
            _escolherPlanilha.Visibility = p.Sistemas.Any(s => s.Lista == null) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            if (_linhas != null) Verificar();
        }

        /// <summary>Escolhe a planilha da primeira classificação sem lista, sugere as colunas e salva o padrão.</summary>
        private void EscolherPlanilha()
        {
            SistemaClassificacao s = _padrao?.Sistemas.FirstOrDefault(x => x.Lista == null);
            if (s == null) return;
            var d = new Microsoft.Win32.OpenFileDialog { Title = $"Planilha da classificação {s.Nome}", Filter = Planilha.LeitorPlanilha.Filtro };
            if (d.ShowDialog(this) != true) return;
            try
            {
                Planilha.Tabela t = Planilha.LeitorPlanilha.Ler(d.FileName);
                var campos = SistemaClassificacao.Campos;
                string aba = t.NomesAbas.FirstOrDefault(a => t.Linhas(a).Count > 1) ?? t.NomesAbas[0];
                var mapa = new Planilha.MapaColunas { Arquivo = d.FileName, Aba = aba, LinhaCabecalho = Planilha.MapaColunas.AcharCabecalho(t, aba, campos) };
                mapa.Sugerir(t, campos);
                s.Planilha = mapa;
                _padrao.Salvar();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível ler a planilha:\n" + ex.Message, Title);
                return;
            }
            UsarPadrao(_padrao);
            if (s.Lista == null)
            {
                MessageBox.Show(this, $"Não achei sozinho as colunas da planilha ({s.Erro}). Indique em qual coluna está cada dado.", Title);
                EditarPadroes();
            }
        }

        private void EditarPadroes()
        {
            var w = new PadroesClassificacaoWindow(_doc, _padrao?.Nome) { Owner = this };
            w.ShowDialog();
            CarregarPadroes(w.Escolhido ?? _padrao?.Nome);
        }

        /// <summary>Lê os elementos do escopo e atualiza a lista de categorias.</summary>
        private void CarregarEscopo()
        {
            string esc = _escopo.SelectedItem as string ?? "";
            _base = esc.StartsWith("Seleção") ? ClassificationCore.Elementos(_doc, _selecao, null)
                : esc == "Vista atual" ? ClassificationCore.Elementos(_doc, null, _doc.ActiveView.Id)
                : ClassificationCore.Elementos(_doc, null, null);
            _categorias.Definir(_base.GroupBy(e => e.Category.Name).ToDictionary(g => g.Key, g => g.Count()));
        }

        private List<Element> NoEscopo()
        {
            CarregarEscopo(); // relê o modelo (reanalisar pega o que mudou)
            return _base.Where(e => _categorias.Aceita(e.Category.Name)).ToList();
        }

        private void Verificar()
        {
            var sistemas = _padrao?.Sistemas.Where(x => x.Lista != null && x.Lista.Itens.Count > 0).ToList() ?? new List<SistemaClassificacao>();
            if (sistemas.Count == 0)
            {
                MessageBox.Show(this, "Nenhuma classificação do padrão tem a planilha lida. Clique em \"Editar padrões...\" e escolha a planilha e as colunas.", Title);
                return;
            }
            Cursor = Cursors.Wait;
            try
            {
                using (Ui.Progresso.Abrir("Verificar classificação"))
                {
                    List<Element> elementos = NoEscopo();
                    var linhas = new List<LinhaVerificacao>();
                    foreach (SistemaClassificacao sis in sistemas) linhas.AddRange(ClassificationCore.Verificar(_doc, elementos, sis));
                    _linhas = linhas;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível verificar:\n" + ex.Message, Title);
                return;
            }
            finally { Cursor = null; }
            Mostrar();
        }

        private static readonly string[] Ordem =
        {
            ClassificationCore.OK, ClassificationCore.DescricaoDiferente, ClassificationCore.ForaDaLista, ClassificationCore.FormatoInvalido,
            ClassificationCore.CategoriaNaoPrevista, ClassificationCore.SemClassificacao, ClassificationCore.SemParametro,
        };

        private void Mostrar()
        {
            if (_situacao != null && !_linhas.Any(l => l.Situacao == _situacao)) _situacao = null;
            MostrarEtiquetas();

            var estado = _tabela?.Estado();
            _areaTabela.Children.Clear();
            _tabela = new FilterableGrid(_linhas, typeof(LinhaVerificacao));
            _tabela.AddColumn("Situação", nameof(LinhaVerificacao.Situacao), 170);
            if (_linhas.Select(l => l.SistemaRef).Distinct().Count() > 1)
                _tabela.AddColumn("Classificação", nameof(LinhaVerificacao.Sistema), 120);
            _tabela.AddColumn("Elemento", nameof(LinhaVerificacao.Elemento), 220);
            _tabela.AddColumn("Id", nameof(LinhaVerificacao.Id), 75);
            _tabela.AddColumn("Categoria", nameof(LinhaVerificacao.Categoria), 115);
            _tabela.AddColumn("Onde", nameof(LinhaVerificacao.Onde), 70);
            _tabela.AddColumn("Preenchido", nameof(LinhaVerificacao.Preenchido), 200);
            _tabela.AddColumn("Na lista", nameof(LinhaVerificacao.NaLista), 200);
            _tabela.AddColumn("Proposta", nameof(LinhaVerificacao.Proposta), 160, fill: true);
            if (_linhas.Any(l => !string.IsNullOrEmpty(l.Bloqueio)))
                _tabela.AddColumn("Bloqueio para gravar", nameof(LinhaVerificacao.Bloqueio), 220);
            _tabela.Grid.Columns.Insert(1, ColunaAcao());
            _tabela.Grid.LoadingRow += (s, e) =>
            {
                var l = (LinhaVerificacao)e.Row.Item;
                e.Row.Background = l.Situacao == ClassificationCore.OK ? Theme.LinhaNormal
                    : l.Situacao == ClassificationCore.SemClassificacao || l.Situacao == ClassificationCore.SemParametro ? Theme.LinhaAviso
                    : Theme.LinhaProblema;
            };
            _tabela.Filtered += AtualizarRodape;
            _tabela.Grid.SelectionChanged += (s, e) => AtualizarSelecionadas();
            Theme.StyleSelection(_tabela.Grid);
            var opcoes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            opcoes.Children.Add(_tabela.CreateSingleLineToggle());
            var soProblemas = new CheckBox { Content = "Ocultar OK", VerticalAlignment = VerticalAlignment.Center, IsChecked = _ocultarOk };
            void FiltroOk()
            {
                _ocultarOk = soProblemas.IsChecked == true;
                AplicarFiltroExtra();
            }
            soProblemas.Click += (s, e) => FiltroOk();
            FiltroOk();
            _tabela.Restaurar(estado);
            opcoes.Children.Add(soProblemas);
            DockPanel.SetDock(opcoes, Dock.Top);
            _areaTabela.Children.Add(opcoes);
            _areaTabela.Children.Add(_tabela);

            _exportar.IsEnabled = _selecionar.IsEnabled = _reanalisar.IsEnabled = true;
            AtualizarRodape();
        }

        /// <summary>Etiquetas de resumo; clicar numa filtra a tabela por aquela situação (clicar de novo mostra tudo).</summary>
        private void MostrarEtiquetas()
        {
            _resumo.Children.Clear();
            foreach (string s in Ordem)
            {
                int n = _linhas.Count(l => l.Situacao == s);
                if (n == 0 && s != ClassificationCore.OK) continue;
                bool problema = s == ClassificationCore.DescricaoDiferente || s == ClassificationCore.ForaDaLista || s == ClassificationCore.FormatoInvalido;
                Border chip = Theme.Chip($"{n} {s.ToLowerInvariant()}", destaque: problema, escuro: s == ClassificationCore.ForaDaLista);
                chip.Cursor = Cursors.Hand;
                chip.ToolTip = _situacao == s ? "Clique para mostrar todas as situações" : "Clique para ver só estes na tabela (depois, \"Selecionar no modelo\" seleciona no Revit)";
                if (_situacao == s)
                {
                    chip.BorderBrush = Theme.Solid(Theme.Laranja);
                    chip.BorderThickness = new Thickness(2);
                }
                else if (_situacao != null) chip.Opacity = 0.55;
                string sit = s;
                chip.MouseLeftButtonUp += (o, e) =>
                {
                    _situacao = _situacao == sit ? null : sit;
                    MostrarEtiquetas();
                    AplicarFiltroExtra();
                };
                _resumo.Children.Add(chip);
            }

            foreach (var g in _linhas.Where(l => l.Situacao == ClassificationCore.SemParametro).GroupBy(l => l.Parametro))
            {
                var cats = g.Select(l => l.Categoria).Distinct().ToList();
                var criar = new Button
                {
                    Content = $"Criar {g.Key} em {cats.Count} {(cats.Count == 1 ? "categoria" : "categorias")}",
                    ToolTip = "Sem o parâmetro: " + string.Join(", ", cats),
                    Padding = new Thickness(8, 1, 8, 1),
                    Margin = new Thickness(4, 0, 0, 0),
                };
                string param = g.Key;
                criar.Click += (o, e) => CriarParametro(param, cats);
                _resumo.Children.Add(criar);
            }

            foreach (var porParametro in _linhas.Where(l => l.FamiliaFormula != null).GroupBy(l => l.Parametro))
            {
                var comFormula = porParametro.GroupBy(l => l.FamiliaFormula).ToList();
                var remover = new Button
                {
                    Content = $"Remover fórmula de {porParametro.Key} em {comFormula.Count} {(comFormula.Count == 1 ? "família" : "famílias")}",
                    ToolTip = string.Join("\n", comFormula.Select(g => g.First().Bloqueio)),
                    Padding = new Thickness(8, 1, 8, 1),
                    Margin = new Thickness(4, 0, 0, 0),
                };
                string param = porParametro.Key;
                remover.Click += (o, e) => RemoverFormulas(comFormula.Select(g => g.Key).ToList(), comFormula.Select(g => g.First().Bloqueio).ToList(), param);
                _resumo.Children.Add(remover);
            }
        }

        private void RemoverFormulas(List<ElementId> familias, List<string> descricoes, string parametro)
        {
            string msg = $"Remover a fórmula de {parametro} em {familias.Count} {(familias.Count == 1 ? "família" : "famílias")} e recarregá-las no projeto?\n\n" +
                         string.Join("\n", descricoes.Take(8).Select(d => "• " + d.Substring(ClassificationCore.PrefixoFormula.Length))) +
                         (descricoes.Count > 8 ? $"\n• e mais {descricoes.Count - 8}" : "") +
                         $"\n\n{parametro} continua o mesmo parâmetro (mesmo GUID) e fica com o último valor calculado, agora editável. " +
                         "Os arquivos .rfa da biblioteca não são alterados. Cada recarga pode ser desfeita com Ctrl+Z.";
            if (MessageBox.Show(this, msg, Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            Cursor = Cursors.Wait;
            List<string> ok, falhas;
            try
            {
                using (Ui.Progresso.Abrir("Remover fórmula da família", podeCancelar: true))
                    (ok, falhas) = FormulaFamilia.Remover(_doc, familias.Select(id => _doc.GetElement(id)).OfType<Family>(), parametro);
            }
            finally { Cursor = null; }
            MessageBox.Show(this, $"Fórmula removida em {ok.Count} {(ok.Count == 1 ? "família" : "famílias")}." +
                (falhas.Count > 0 ? "\n\nNão foi possível em:\n" + string.Join("\n", falhas.Select(f => "• " + f)) : "") +
                "\n\nAgora é só aplicar as propostas.", Title);
            Verificar();
        }

        private void AplicarFiltroExtra()
        {
            if (_tabela == null) return;
            string sit = _situacao;
            bool ocultar = _ocultarOk;
            _tabela.ExtraFilter = sit == null && !ocultar ? (Func<object, bool>)null
                : o => (sit == null || ((LinhaVerificacao)o).Situacao == sit) && (!ocultar || ((LinhaVerificacao)o).Situacao != ClassificationCore.OK);
            _tabela.Refresh();
        }

        private void CriarParametro(string parametro, List<string> categorias)
        {
            try
            {
                string msg = ParametroClassificacao.Vincular(_doc, parametro, ParametroClassificacao.Categorias(_doc, categorias));
                if (msg == null) return;
                MessageBox.Show(this, msg, Title);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível criar o parâmetro:\n" + ex.Message, Title);
                return;
            }
            Verificar();
        }

        /// <summary>Propostas das linhas visíveis que serão gravadas, uma por dono (instância ou tipo).</summary>
        /// <param name="soSelecionadas">true = só as linhas selecionadas na tabela (aí entram também as que trocam o código).</param>
        private List<LinhaVerificacao> Propostas(out int conflitos, bool soSelecionadas = false)
        {
            conflitos = 0;
            if (_tabela == null) return new List<LinhaVerificacao>();
            bool todas = soSelecionadas || _incluirInseguras.IsChecked == true;
            IEnumerable<LinhaVerificacao> origem = soSelecionadas
                ? _tabela.Grid.SelectedItems.OfType<LinhaVerificacao>()
                : _tabela.VisibleItems<LinhaVerificacao>();
            var aceitas = origem.Where(l => l.Gravacoes.Count > 0 && (l.PropostaSegura || todas)).ToList();
            // Um tipo compartilhado por elementos com propostas diferentes fica de fora.
            var conflitantes = aceitas.SelectMany(l => l.Gravacoes)
                .GroupBy(g => (g.Dono, g.Parametro))
                .Where(g => g.Select(x => x.Valor).Distinct().Count() > 1)
                .Select(g => g.Key).ToHashSet();
            conflitos = conflitantes.Count;
            var vistos = new HashSet<(ElementId, string)>();
            var resultado = new List<LinhaVerificacao>();
            foreach (LinhaVerificacao l in aceitas)
            {
                if (l.Gravacoes.Any(g => conflitantes.Contains((g.Dono, g.Parametro)))) continue;
                // Uma linha por dono e parâmetro (instâncias do mesmo tipo gravam uma vez só).
                if (l.Gravacoes.All(g => !vistos.Add((g.Dono, g.Parametro)))) continue;
                resultado.Add(l);
            }
            return resultado;
        }

        private void AtualizarRodape()
        {
            if (_tabela == null) return;
            int v = _tabela.VisibleItems<LinhaVerificacao>().Count();
            var p = Propostas(out int conflitos);
            _rodapeInfo.Text = (v == _linhas.Count ? $"{v} elementos" : $"{v} de {_linhas.Count} visíveis (filtro ativo)") +
                               (conflitos > 0 ? $" · {conflitos} tipos com propostas diferentes ficam de fora" : "");
            _aplicar.IsEnabled = p.Count > 0;
            _aplicar.Content = p.Count == 0 ? "Nada a corrigir" : $"Aplicar todas ({p.Count})";
            AtualizarSelecionadas();
        }

        private void AtualizarSelecionadas()
        {
            int n = _tabela == null ? 0 : Propostas(out _, true).Count;
            _aplicarSelecionadas.IsEnabled = n > 0;
            _aplicarSelecionadas.Content = n > 0 ? $"Aplicar selecionadas ({n})" : "Aplicar selecionadas";
        }

        private void Aplicar(bool soSelecionadas)
        {
            var p = Propostas(out _, soSelecionadas);
            if (p.Count == 0) return;
            int tipos = p.Count(l => l.Onde == "tipo");
            string msg = $"Gravar {p.Count} correções em {string.Join(", ", p.SelectMany(l => l.Gravacoes).Select(g => g.Parametro).Distinct())}?" +
                         (tipos > 0 ? $"\n\n{tipos} delas são em tipos e valem para todas as instâncias desses tipos." : "") +
                         "\n\nExemplos:\n" + string.Join("\n", p.Take(5).Select(l => $"• {l.Preenchido}  →  {l.ValorProposto}")) +
                         "\n\nÉ uma única operação: pode ser desfeita com Ctrl+Z.";
            if (MessageBox.Show(this, msg, Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                ResultadoGravacao r = ClassificationCore.Gravar(_doc, p.SelectMany(l => l.Gravacoes), "Corrigir classificação", Confirmar);
                MostrarResultado(r, "correções");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível gravar. Nada foi alterado.\n\n" + ex.Message, Title);
                return;
            }
            Verificar();
        }

        private bool Confirmar(string pergunta) =>
            MessageBox.Show(this, pergunta, Title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

        /// <summary>Mostra o resultado e oferece selecionar no Revit o que não foi gravado.</summary>
        private void MostrarResultado(ResultadoGravacao r, string oQue)
        {
            if (r.Falhas.Count == 0) { MessageBox.Show(this, r.Resumo(oQue), Title); return; }
            if (MessageBox.Show(this, r.Resumo(oQue) + "\n\nSelecionar no Revit os que não foram gravados?", Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                _uidoc.Selection.SetElementIds(r.Falhas.Select(f => f.Id).ToList());
        }

        // ------------------------------------------------------------------ ação por linha

        private DataGridTemplateColumn ColunaAcao()
        {
            var botao = new FrameworkElementFactory(typeof(Button));
            botao.SetBinding(ContentControl.ContentProperty, new Binding(nameof(LinhaVerificacao.Acao)));
            botao.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(LinhaVerificacao.Proposta)));
            botao.SetValue(Control.PaddingProperty, new Thickness(6, 0, 6, 0));
            botao.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 1, 2, 1));
            botao.SetValue(Control.FontSizeProperty, 11.0);
            var estilo = new Style(typeof(Button), TryFindResource(typeof(Button)) as Style);
            var semAcao = new DataTrigger { Binding = new Binding(nameof(LinhaVerificacao.Acao)), Value = null };
            semAcao.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
            estilo.Triggers.Add(semAcao);
            botao.SetValue(FrameworkElement.StyleProperty, estilo);
            botao.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((s, e) =>
            {
                if ((s as FrameworkElement)?.DataContext is LinhaVerificacao l) ExecutarAcao(l);
            }));
            return new DataGridTemplateColumn
            {
                Header = "Ação",
                Width = 112,
                CellTemplate = new DataTemplate { VisualTree = botao },
            };
        }

        /// <summary>Linhas da mesma classificação selecionadas junto com a clicada (a ação vale para todas).</summary>
        private List<LinhaVerificacao> Junto(LinhaVerificacao l)
        {
            var sel = _tabela.Grid.SelectedItems.OfType<LinhaVerificacao>().ToList();
            return sel.Contains(l) ? sel.Where(x => x.SistemaRef == l.SistemaRef && x.Acao == l.Acao).ToList() : new List<LinhaVerificacao> { l };
        }

        private void ExecutarAcao(LinhaVerificacao l)
        {
            try
            {
                switch (l.Acao)
                {
                    case "Corrigir": Corrigir(Junto(l)); break;
                    case "Escolher...": Escolher(l, Junto(l)); break;
                    case "Criar parâmetro":
                        CriarParametro(l.Parametro, Junto(l).Select(x => x.Categoria).Distinct().ToList());
                        break;
                    case "Remover fórmula":
                        RemoverFormulas(new List<ElementId> { l.FamiliaFormula }, new List<string> { l.Bloqueio }, l.Parametro);
                        break;
                    default: Mostrar(l); break;
                }
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Title); }
        }

        private void Corrigir(List<LinhaVerificacao> linhas)
        {
            var gravar = linhas.SelectMany(x => x.Gravacoes).ToList();
            if (gravar.Count == 0) return;
            if (gravar.GroupBy(g => (g.Dono, g.Parametro)).Any(g => g.Select(x => x.Valor).Distinct().Count() > 1))
            {
                MessageBox.Show(this, "Há linhas do mesmo tipo com correções diferentes. Corrija uma de cada vez ou use \"Escolher...\".", Title);
                return;
            }
            if (linhas.Count > 1 &&
                MessageBox.Show(this, $"Corrigir as {linhas.Count} linhas selecionadas?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            MostrarResultado(ClassificationCore.Gravar(_doc, gravar, "Corrigir classificação", Confirmar), "correções");
            Verificar();
        }

        private void Escolher(LinhaVerificacao l, List<LinhaVerificacao> linhas)
        {
            SistemaClassificacao s = l.SistemaRef;
            var w = new EscolherItemWindow(s.Lista, s.Nome, l.Categoria, linhas.Count > 1 ? $"{linhas.Count} elementos" : l.Elemento, linhas.Count > 1 ? null : l.Preenchido) { Owner = this };
            if (w.ShowDialog() != true || w.Escolhido == null) return;
            ClassItem item = w.Escolhido;
            var gravar = new List<(ElementId, string, string)>();
            foreach (LinhaVerificacao x in linhas.Where(x => x.DonoId != null))
            {
                if (s.Formato == FormatoClassificacao.CodigoDescricao)
                {
                    gravar.Add((x.DonoId, s.ParametroCodigo, ValorClassificacao.Escrever(item.Codigo, item.Descricao, s.GravarNome ? s.Nome : null)));
                    continue;
                }
                gravar.Add((x.DonoId, s.ParametroCodigo, item.Codigo));
                if (!string.IsNullOrWhiteSpace(s.ParametroDescricao) && item.Descricao.Length > 0)
                {
                    Element e = _doc.GetElement(x.ElementoId);
                    AlvoClassificacao ad = e == null ? null : ClassificationCore.Alvo(_doc, e, s.ParametroDescricao);
                    if (ad?.Parametro != null && !ad.Parametro.IsReadOnly) gravar.Add((ad.Dono.Id, s.ParametroDescricao, item.Descricao));
                }
            }
            int tipos = linhas.Count(x => x.Onde == "tipo");
            if (tipos > 0 && MessageBox.Show(this, $"{s.ParametroCodigo} está no tipo em {tipos} {(tipos == 1 ? "elemento" : "elementos")}: " +
                    $"{item.Codigo} vai valer para todas as instâncias desses tipos. Continuar?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            MostrarResultado(ClassificationCore.Gravar(_doc, gravar, "Aplicar classificação " + item.Codigo, Confirmar), "gravações");
            Verificar();
        }

        /// <summary>Seleciona o elemento no Revit e explica por que não dá para gravar.</summary>
        private void Mostrar(LinhaVerificacao l)
        {
            _uidoc.Selection.SetElementIds(new List<ElementId> { l.ElementoId });
            try { _uidoc.ShowElements(l.ElementoId); } catch { }
            string texto = !string.IsNullOrEmpty(l.Bloqueio) ? "Não dá para gravar: " + l.Bloqueio : l.Proposta;
            _rodapeInfo.Text = $"{l.Elemento} (Id {l.Id}) selecionado no Revit." + (string.IsNullOrEmpty(texto) ? "" : " " + texto);
        }

        private void Selecionar()
        {
            if (_tabela == null) return;
            var ids = _tabela.VisibleItems<LinhaVerificacao>().Select(l => l.ElementoId).ToList();
            _uidoc.Selection.SetElementIds(ids);
            _rodapeInfo.Text = $"{ids.Count} elementos selecionados no Revit.";
        }

        private void Exportar()
        {
            if (_tabela == null) return;
            var dialogo = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                FileName = $"verificacao_classificacao_{_doc.Title}.csv".Replace(" ", "_"),
            };
            if (dialogo.ShowDialog(this) != true) return;
            var sb = new StringBuilder();
            sb.AppendLine("Situação;Classificação;Parâmetro;Elemento;Id;Categoria;Onde;Preenchido;Na lista;Proposta;Bloqueio");
            foreach (LinhaVerificacao l in _tabela.VisibleItems<LinhaVerificacao>())
                sb.AppendLine(string.Join(";", new[] { l.Situacao, l.Sistema, l.Parametro, l.Elemento, l.Id.ToString(), l.Categoria, l.Onde, l.Preenchido, l.NaLista, l.Proposta, l.Bloqueio }
                    .Select(v => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"")));
            try { File.WriteAllText(dialogo.FileName, sb.ToString(), new UTF8Encoding(true)); }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível gravar o arquivo:\n" + ex.Message, Title); }
        }
    }
}
