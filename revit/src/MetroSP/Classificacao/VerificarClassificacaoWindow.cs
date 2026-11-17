using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using Autodesk.Revit.DB;
using MetroSP.Ui;
using Grid = System.Windows.Controls.Grid;
using Control = System.Windows.Controls.Control;
using Binding = System.Windows.Data.Binding;
using Visibility = System.Windows.Visibility;
using Color = System.Windows.Media.Color;

namespace MetroSP.Classificacao
{
    /// <summary>
    /// Linhas da verificação com o mesmo valor escrito e a mesma situação: uma linha da tabela,
    /// em vez de uma por elemento (milhares de elementos viram poucas linhas).
    /// </summary>
    public class GrupoVerificacao
    {
        public List<LinhaVerificacao> Linhas { get; } = new List<LinhaVerificacao>();
        public LinhaVerificacao Primeira => Linhas[0];
        public SistemaClassificacao SistemaRef => Primeira.SistemaRef;
        public string Situacao => Primeira.Situacao;
        public string Preenchido => Primeira.Preenchido;
        public string NaLista => Primeira.NaLista;
        public string Proposta => Primeira.Proposta;
        public string Bloqueio => Primeira.Bloqueio;
        public string Acao => Primeira.Acao;
        public string Parametro => Primeira.Parametro;
        public ClassItem Item => Primeira.Item;
        public int Elementos => Linhas.Count;
        public string Onde
        {
            get
            {
                var w = Linhas.Select(l => l.Onde).Where(o => !string.IsNullOrEmpty(o)).Distinct().ToList();
                return w.Count == 1 ? w[0] : string.Join(" e ", w.Select(o => o.StartsWith("instância") ? "instância" : o).Distinct());
            }
        }
        public bool Problema => Situacao != ClassificationCore.OK;
        public IEnumerable<(ElementId Dono, string Parametro, string Valor)> Gravacoes => Linhas.SelectMany(l => l.Gravacoes);
    }

    /// <summary>
    /// Verifica a escrita das classificações do perfil (uma ou mais por elemento) contra as planilhas:
    /// formato, código existente e título igual ao da lista. Não confere categoria.
    /// Uma aba por classificação, uma linha por valor escrito, e à direita os elementos da linha escolhida.
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
        private readonly Button _escolherPlanilha;
        private readonly ComboBox _escopo;
        private readonly Button _reanalisar;
        private readonly StackPanel _abas;
        private SistemaClassificacao _sisAtual;
        private string _situacao; // etiqueta clicada (filtra a tabela)
        private bool _ocultarOk = true;
        private readonly WrapPanel _resumo;
        private readonly DockPanel _areaTabela;
        private readonly StackPanel _detalhe;
        private readonly TextBlock _rodapeInfo;
        private readonly Button _aplicar;
        private readonly Button _aplicarSelecionadas;
        private readonly Button _exportar;
        private readonly Button _selecionar;
        private List<LinhaVerificacao> _linhas;
        private List<GrupoVerificacao> _grupos = new List<GrupoVerificacao>();
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

            // Perfil, onde e Verificar numa linha só.
            var barra = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            barra.Children.Add(Theme.Rotulo("Perfil"));
            _padraoCombo = new ComboBox
            {
                MinWidth = 220, Margin = new Thickness(0, 0, 12, 0), IsEditable = true, IsTextSearchEnabled = true,
                ToolTip = "Perfil de classificação: as classificações a conferir, com a planilha e o parâmetro de cada uma. Digite para procurar.",
            };
            TextSearch.SetTextPath(_padraoCombo, nameof(PadraoClassificacao.Nome));
            _padraoCombo.SelectionChanged += (s, e) => { if (!_trocandoPadrao && _padraoCombo.SelectedItem is PadraoClassificacao p) UsarPadrao(p); };
            barra.Children.Add(_padraoCombo);
            barra.Children.Add(Theme.Rotulo("Onde"));
            _escopo = new ComboBox { Padding = new Thickness(4, 3, 4, 3), MinWidth = 170, Margin = new Thickness(0, 0, 12, 0), ToolTip = "Só elementos que vão para o IFC." };
            if (_selecao != null) _escopo.Items.Add($"Seleção atual ({_selecao.Count} elementos)");
            _escopo.Items.Add("Vista atual");
            _escopo.Items.Add("Todo o modelo");
            _escopo.SelectedIndex = 0;
            barra.Children.Add(_escopo);
            var verificar = new Button { Content = "Verificar", Style = Theme.PrimaryButton, Margin = new Thickness(0, 0, 8, 0) };
            verificar.Click += (s, e) => Verificar();
            barra.Children.Add(verificar);
            var editar = new Button
            {
                Content = "Classificação...",
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "Perfis de classificação: planilha, colunas (código, título, descrição), parâmetro e mais de uma classificação. Também em Coordenação › Padrões.",
            };
            editar.Click += (s, e) => EditarPadroes();
            barra.Children.Add(editar);
            _escolherPlanilha = new Button
            {
                Content = "Escolher planilha...",
                Style = Theme.PrimaryButton,
                Margin = new Thickness(0, 0, 8, 0),
                Visibility = Visibility.Collapsed,
                ToolTip = "Abre a planilha (.xlsx ou .csv) com a lista. As colunas são sugeridas pelos títulos; dá para mudar em \"Classificação...\".",
            };
            _escolherPlanilha.Click += (s, e) => EscolherPlanilha();
            barra.Children.Add(_escolherPlanilha);
            _padraoInfo = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            barra.Children.Add(_padraoInfo);

            _abas = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            _resumo = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            _areaTabela = new DockPanel();
            _areaTabela.Children.Add(new TextBlock
            {
                Text = "Escolha o perfil e clique em \"Verificar\".",
                ToolTip = "Cada classificação é lida na instância ou, se não houver, no tipo. Só os elementos que vão para o IFC.",
                Foreground = Theme.Solid(Theme.Cinza),
            });
            _detalhe = new StackPanel();
            var rolagemDetalhe = new ScrollViewer
            {
                Content = _detalhe,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(12, 0, 0, 0),
            };

            var principal = new Grid();
            principal.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 400 });
            principal.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            principal.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300), MinWidth = 200 });
            principal.Children.Add(_areaTabela);
            var divisor = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Stretch, Background = System.Windows.Media.Brushes.Transparent };
            Grid.SetColumn(divisor, 1);
            principal.Children.Add(divisor);
            Grid.SetColumn(rolagemDetalhe, 2);
            principal.Children.Add(rolagemDetalhe);

            var corpo = new DockPanel();
            foreach (UIElement el in new UIElement[] { barra, _abas, _resumo })
            {
                DockPanel.SetDock(el, Dock.Top);
                corpo.Children.Add(el);
            }
            corpo.Children.Add(principal);

            _rodapeInfo = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            _exportar = new Button { Content = "Exportar (.csv)", IsEnabled = false, ToolTip = "Grava um .csv com um elemento por linha (das linhas visíveis)." };
            _selecionar = new Button { Content = "Selecionar no modelo", IsEnabled = false, ToolTip = "Seleciona no Revit os elementos das linhas escolhidas (ou de todas as visíveis)." };
            _aplicar = new Button { Content = "Corrigir escrita", Style = Theme.PrimaryButton, IsEnabled = false, ToolTip = "Corrige de uma vez só a escrita (mesmo código, texto como na lista) das linhas visíveis. Nada troca de código." };
            _reanalisar = new Button { Content = "Reanalisar", IsEnabled = false, ToolTip = "Lê o modelo de novo, mantendo o que está escolhido." };
            _reanalisar.Click += (s, e) => Verificar();
            var fechar = new Button { Content = "Fechar", IsCancel = true };
            _exportar.Click += (s, e) => Exportar();
            _selecionar.Click += (s, e) => Selecionar();
            _aplicar.Click += (s, e) => Aplicar(false);
            _aplicarSelecionadas = new Button
            {
                Content = "Aplicar selecionadas",
                IsEnabled = false,
                ToolTip = "Grava as propostas das linhas escolhidas na tabela (Ctrl/Shift+clique para várias), inclusive as que trocam o código.",
            };
            _aplicarSelecionadas.Click += (s, e) => Aplicar(true);
            fechar.Click += (s, e) => Close();
            var rodape = new DockPanel();
            StackPanel botoes = Theme.Buttons(_reanalisar, _exportar, _selecionar, _aplicarSelecionadas, _aplicar, fechar);
            DockPanel.SetDock(botoes, Dock.Right);
            rodape.Children.Add(botoes);
            rodape.Children.Add(_rodapeInfo);

            Theme.Apply(this, "Verificar classificação", _doc.Title + " · confere a escrita com a planilha · só elementos que vão para o IFC", corpo, rodape);
            CarregarPadroes(config.Padrao);
        }

        // ------------------------------------------------------------------ perfis

        private void CarregarPadroes(string escolher)
        {
            List<PadraoClassificacao> padroes = PadraoClassificacao.Listar();
            if (padroes.Count == 0)
            {
                // Primeiro uso: a planilha e o parâmetro que a classificação já usava viram o primeiro perfil.
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
                ? $"{s.Nome}: {s.Lista.Itens.Count} itens"
                : $"{s.Nome}: {s.Erro}"));
            _padraoInfo.ToolTip = string.Join("\n", p.Sistemas.Select(s =>
                $"{s.Nome}  ·  {s.Parametros}  ·  {s.Planilha?.NomeArquivo}" + (string.IsNullOrEmpty(s.Planilha?.Aba) ? "" : $" › {s.Planilha.Aba}") +
                (s.Lista?.Avisos.Count > 0 ? "\n   " + string.Join("\n   ", s.Lista.Avisos.Take(10)) : "")));
            _padraoInfo.Foreground = Theme.Solid(p.Sistemas.Any(s => s.Lista == null || s.Erro != null) ? Theme.LaranjaTexto : Theme.Cinza);
            _escolherPlanilha.Visibility = p.Sistemas.Any(s => s.Lista == null) ? Visibility.Visible : Visibility.Collapsed;
            if (_linhas != null) Verificar();
        }

        /// <summary>Escolhe a planilha da primeira classificação sem lista, sugere as colunas e salva o perfil.</summary>
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

        private List<Element> NoEscopo()
        {
            string esc = _escopo.SelectedItem as string ?? "";
            return esc.StartsWith("Seleção") ? ClassificationCore.Elementos(_doc, _selecao, null)
                : esc == "Vista atual" ? ClassificationCore.Elementos(_doc, null, _doc.ActiveView.Id)
                : ClassificationCore.Elementos(_doc, null, null);
        }

        private void Verificar()
        {
            var sistemas = _padrao?.Sistemas.Where(x => x.Lista != null && x.Lista.Itens.Count > 0).ToList() ?? new List<SistemaClassificacao>();
            if (sistemas.Count == 0)
            {
                MessageBox.Show(this, "Nenhuma classificação do perfil tem a planilha lida. Clique em \"Classificação...\" e escolha a planilha e as colunas.", Title);
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
            if (_sisAtual == null || !sistemas.Contains(_sisAtual)) _sisAtual = sistemas[0];
            MostrarAbas(sistemas);
            Mostrar();
        }

        // ------------------------------------------------------------------ abas, etiquetas e tabela

        /// <summary>Uma aba por classificação do perfil (escondida quando há uma só).</summary>
        private void MostrarAbas(List<SistemaClassificacao> sistemas)
        {
            _abas.Children.Clear();
            _abas.Visibility = sistemas.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            foreach (SistemaClassificacao s in sistemas)
            {
                int problemas = _linhas.Count(l => l.SistemaRef == s && l.Situacao != ClassificationCore.OK);
                bool atual = s == _sisAtual;
                var aba = new Border
                {
                    BorderBrush = Theme.Solid(atual ? Theme.Laranja : Theme.CinzaClaro),
                    BorderThickness = new Thickness(0, atual ? 3 : 1, 0, 0),
                    Background = Theme.Solid(atual ? System.Windows.Media.Colors.White : Theme.CinzaFundo),
                    Padding = new Thickness(12, 5, 12, 5),
                    Margin = new Thickness(0, 0, 2, 0),
                    Cursor = Cursors.Hand,
                    ToolTip = $"{s.Parametros} · {problemas} elementos com algo a corrigir",
                    Child = new TextBlock { Text = $"{s.Nome} ({s.ParametroCodigo})", FontWeight = atual ? FontWeights.SemiBold : FontWeights.Normal },
                };
                SistemaClassificacao sis = s;
                aba.MouseLeftButtonUp += (o, e) =>
                {
                    if (_sisAtual == sis) return;
                    _sisAtual = sis;
                    _situacao = null;
                    MostrarAbas(sistemas);
                    Mostrar();
                };
                _abas.Children.Add(aba);
            }
        }

        private static readonly (string Situacao, string Rotulo)[] Ordem =
        {
            (ClassificationCore.OK, "certos"),
            (ClassificationCore.DescricaoDiferente, "escrita diferente"),
            (ClassificationCore.ForaDaLista, "fora da lista"),
            (ClassificationCore.FormatoInvalido, "formato inválido"),
            (ClassificationCore.SemClassificacao, "sem classificação"),
            (ClassificationCore.SemParametro, "sem o parâmetro"),
        };

        private List<LinhaVerificacao> LinhasDaAba() => _linhas?.Where(l => l.SistemaRef == _sisAtual).ToList() ?? new List<LinhaVerificacao>();

        /// <summary>Agrupa as linhas por valor escrito, situação e proposta (e bloqueio, que muda o que dá para fazer).</summary>
        private static List<GrupoVerificacao> Agrupar(IEnumerable<LinhaVerificacao> linhas)
        {
            var ordem = Ordem.Select(o => o.Situacao).ToList();
            return linhas
                .GroupBy(l => (l.Situacao, l.Preenchido ?? "", l.ValorProposto ?? "", l.Proposta ?? "", l.Bloqueio ?? "", l.FamiliaFormula?.Value ?? 0))
                .Select(g => { var gr = new GrupoVerificacao(); gr.Linhas.AddRange(g); return gr; })
                .OrderBy(g => ordem.IndexOf(g.Situacao) < 0 ? 99 : ordem.IndexOf(g.Situacao) == 0 ? 98 : ordem.IndexOf(g.Situacao))
                .ThenByDescending(g => g.Elementos)
                .ToList();
        }

        private void Mostrar()
        {
            List<LinhaVerificacao> daAba = LinhasDaAba();
            if (_situacao != null && !daAba.Any(l => l.Situacao == _situacao)) _situacao = null;
            _grupos = Agrupar(daAba);
            MostrarEtiquetas(daAba);

            var estado = _tabela?.Estado();
            _areaTabela.Children.Clear();
            _tabela = new FilterableGrid(_grupos, typeof(GrupoVerificacao));
            _tabela.AddColumn("Situação", nameof(GrupoVerificacao.Situacao), 150);
            _tabela.AddColumn("Está escrito", nameof(GrupoVerificacao.Preenchido), 240);
            _tabela.AddColumn("Na lista", nameof(GrupoVerificacao.NaLista), 240);
            _tabela.AddColumn("O que muda", nameof(GrupoVerificacao.Proposta), 170, fill: true);
            _tabela.AddColumn("Elem.", nameof(GrupoVerificacao.Elementos), 60);
            _tabela.AddColumn("Onde", nameof(GrupoVerificacao.Onde), 80);
            _tabela.Grid.Columns.Add(ColunaAcao());
            _tabela.Grid.LoadingRow += (s, e) =>
            {
                var g = (GrupoVerificacao)e.Row.Item;
                e.Row.Background = g.Situacao == ClassificationCore.OK ? Theme.LinhaNormal
                    : g.Situacao == ClassificationCore.SemClassificacao || g.Situacao == ClassificationCore.SemParametro ? Theme.LinhaAviso
                    : Theme.LinhaProblema;
            };
            _tabela.Filtered += AtualizarRodape;
            _tabela.Grid.SelectionChanged += (s, e) => { AtualizarSelecionadas(); MostrarDetalhe(); };
            Theme.StyleSelection(_tabela.Grid);
            var opcoes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            var ocultar = new CheckBox { Content = "Ocultar os certos", VerticalAlignment = VerticalAlignment.Center, IsChecked = _ocultarOk, Margin = new Thickness(0, 0, 12, 0) };
            ocultar.Click += (s, e) => { _ocultarOk = ocultar.IsChecked == true; AplicarFiltroExtra(); };
            opcoes.Children.Add(ocultar);
            opcoes.Children.Add(_tabela.CreateSingleLineToggle());
            AplicarFiltroExtra();
            _tabela.Restaurar(estado);
            DockPanel.SetDock(opcoes, Dock.Top);
            _areaTabela.Children.Add(opcoes);
            _areaTabela.Children.Add(_tabela);

            _exportar.IsEnabled = _selecionar.IsEnabled = _reanalisar.IsEnabled = true;
            AtualizarRodape();
            MostrarDetalhe();
        }

        /// <summary>Contadores por situação (elementos); clicar num filtra a tabela (clicar de novo mostra tudo).</summary>
        private void MostrarEtiquetas(List<LinhaVerificacao> daAba)
        {
            _resumo.Children.Clear();
            foreach (var (s, rotulo) in Ordem)
            {
                int n = daAba.Count(l => l.Situacao == s);
                if (n == 0 && s != ClassificationCore.OK) continue;
                bool problema = s == ClassificationCore.DescricaoDiferente || s == ClassificationCore.ForaDaLista || s == ClassificationCore.FormatoInvalido;
                Border chip = Theme.Chip($"{n:N0} {rotulo}", destaque: problema, escuro: _situacao == s);
                chip.Cursor = Cursors.Hand;
                chip.ToolTip = _situacao == s ? "Clique para mostrar todas" : $"{s}: clique para ver só estes";
                string sit = s;
                chip.MouseLeftButtonUp += (o, e) =>
                {
                    _situacao = _situacao == sit ? null : sit;
                    if (_situacao == ClassificationCore.OK) _ocultarOk = false;
                    Mostrar();
                };
                _resumo.Children.Add(chip);
            }

            foreach (var g in daAba.Where(l => l.Situacao == ClassificationCore.SemParametro).GroupBy(l => l.Parametro))
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

            foreach (var porParametro in daAba.Where(l => l.FamiliaFormula != null).GroupBy(l => l.Parametro))
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

        private void AplicarFiltroExtra()
        {
            if (_tabela == null) return;
            string sit = _situacao;
            bool ocultar = _ocultarOk && sit != ClassificationCore.OK;
            _tabela.ExtraFilter = sit == null && !ocultar ? (Func<object, bool>)null
                : o => (sit == null || ((GrupoVerificacao)o).Situacao == sit) && (!ocultar || ((GrupoVerificacao)o).Situacao != ClassificationCore.OK);
            _tabela.Refresh();
        }

        // ------------------------------------------------------------------ detalhe da linha escolhida

        private List<GrupoVerificacao> Escolhidos() => _tabela?.Grid.SelectedItems.OfType<GrupoVerificacao>().ToList() ?? new List<GrupoVerificacao>();

        private void MostrarDetalhe()
        {
            _detalhe.Children.Clear();
            List<GrupoVerificacao> sel = Escolhidos();
            if (sel.Count == 0)
            {
                _detalhe.Children.Add(Theme.Secao("Detalhe"));
                _detalhe.Children.Add(Theme.Detalhe("Escolha uma linha para ver os elementos e o que muda."));
                return;
            }
            var linhas = sel.SelectMany(g => g.Linhas).ToList();
            _detalhe.Children.Add(Theme.Secao($"{linhas.Count:N0} {(linhas.Count == 1 ? "elemento" : "elementos")}"));

            if (sel.Count == 1)
            {
                GrupoVerificacao g = sel[0];
                if (!string.IsNullOrEmpty(g.Preenchido) && !string.IsNullOrEmpty(g.Primeira.ValorProposto) && g.Primeira.ValorProposto != g.Preenchido)
                {
                    _detalhe.Children.Add(Theme.Label("Está escrito"));
                    _detalhe.Children.Add(Diferenca(g.Preenchido, g.Primeira.ValorProposto, true));
                    _detalhe.Children.Add(Theme.Label("Vai ficar"));
                    _detalhe.Children.Add(Diferenca(g.Primeira.ValorProposto, g.Preenchido, false));
                }
                else if (!string.IsNullOrEmpty(g.Preenchido))
                {
                    _detalhe.Children.Add(Theme.Label("Está escrito"));
                    _detalhe.Children.Add(Diferenca(g.Preenchido, g.Preenchido, true));
                }
                if (!string.IsNullOrEmpty(g.Proposta)) _detalhe.Children.Add(Theme.Detalhe(g.Proposta));
                if (!string.IsNullOrEmpty(g.Bloqueio))
                    _detalhe.Children.Add(new TextBlock { Text = "Não dá para gravar: " + g.Bloqueio, TextWrapping = TextWrapping.Wrap, Foreground = Theme.Solid(Color.FromRgb(0xB0, 0x20, 0x20)), Margin = new Thickness(0, 6, 0, 0) });
            }

            _detalhe.Children.Add(Theme.Label("Categorias"));
            _detalhe.Children.Add(Theme.Detalhe(string.Join(" · ", linhas.GroupBy(l => l.Categoria).OrderByDescending(x => x.Count()).Select(x => $"{x.Key} {x.Count()}"))));
            _detalhe.Children.Add(Theme.Label("Tipos"));
            var tipos = linhas.GroupBy(l => l.Elemento).OrderByDescending(x => x.Count()).ToList();
            var t = new StringBuilder();
            foreach (var x in tipos.Take(10)) t.AppendLine($"{x.Count(),5}  {x.Key}");
            if (tipos.Count > 10) t.AppendLine($"      e mais {tipos.Count - 10} tipos ({tipos.Skip(10).Sum(x => x.Count())} elementos)");
            _detalhe.Children.Add(new TextBlock { Text = t.ToString().TrimEnd(), TextWrapping = TextWrapping.Wrap, FontSize = 11 });

            var acoes = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            var selecionar = new Button { Content = "Selecionar no modelo", Margin = new Thickness(0, 0, 6, 4), ToolTip = "Seleciona estes elementos no Revit." };
            selecionar.Click += (s, e) => Selecionar();
            acoes.Children.Add(selecionar);
            if (sel.Count == 1 && sel[0].Acao != null)
            {
                GrupoVerificacao g = sel[0];
                var acao = new Button { Content = g.Acao, Margin = new Thickness(0, 0, 6, 4), ToolTip = g.Proposta };
                acao.Click += (s, e) => ExecutarAcao(g);
                acoes.Children.Add(acao);
            }
            _detalhe.Children.Add(acoes);
        }

        /// <summary>Texto com o que difere do outro marcado: em vermelho (no modelo) ou verde (na lista).</summary>
        private static TextBlock Diferenca(string texto, string outro, bool noModelo)
        {
            var tb = new TextBlock
            {
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                TextWrapping = TextWrapping.Wrap,
                Background = Theme.Solid(Theme.CinzaFundo),
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(0, 0, 0, 4),
            };
            texto ??= "";
            outro ??= "";
            int ini = 0;
            while (ini < texto.Length && ini < outro.Length && texto[ini] == outro[ini]) ini++;
            int fim = 0;
            while (fim < texto.Length - ini && fim < outro.Length - ini && texto[texto.Length - 1 - fim] == outro[outro.Length - 1 - fim]) fim++;
            string meio = texto.Substring(ini, texto.Length - ini - fim);
            tb.Inlines.Add(new Run(texto.Substring(0, ini)));
            if (meio.Length > 0)
                tb.Inlines.Add(new Run(meio.Replace(" ", "·"))
                {
                    Background = Theme.Solid(noModelo ? Color.FromRgb(0xFF, 0xD8, 0xD8) : Theme.VerdeClaro),
                    Foreground = Theme.Solid(noModelo ? Color.FromRgb(0xA0, 0x00, 0x00) : Theme.Verde),
                    FontWeight = FontWeights.SemiBold,
                });
            else if (texto != outro)
                tb.Inlines.Add(new Run("‸") { Foreground = Theme.Solid(noModelo ? Color.FromRgb(0xA0, 0x00, 0x00) : Theme.Verde), FontWeight = FontWeights.Bold });
            tb.Inlines.Add(new Run(texto.Substring(texto.Length - fim)));
            return tb;
        }

        // ------------------------------------------------------------------ fórmulas e parâmetros

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
                "\n\nAgora é só corrigir.", Title);
            Verificar();
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

        // ------------------------------------------------------------------ gravar

        /// <summary>Linhas (elementos) com proposta a gravar, uma por dono e parâmetro.</summary>
        /// <param name="soSelecionadas">true = só as linhas escolhidas na tabela (aí entram também as que trocam o código).</param>
        private List<LinhaVerificacao> Propostas(out int conflitos, bool soSelecionadas = false)
        {
            conflitos = 0;
            if (_tabela == null) return new List<LinhaVerificacao>();
            IEnumerable<GrupoVerificacao> origem = soSelecionadas ? Escolhidos() : _tabela.VisibleItems<GrupoVerificacao>();
            var aceitas = origem.SelectMany(g => g.Linhas).Where(l => l.Gravacoes.Count > 0 && (l.PropostaSegura || soSelecionadas)).ToList();
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
                if (l.Gravacoes.All(g => !vistos.Add((g.Dono, g.Parametro)))) continue;
                resultado.Add(l);
            }
            return resultado;
        }

        private void AtualizarRodape()
        {
            if (_tabela == null) return;
            var visiveis = _tabela.VisibleItems<GrupoVerificacao>().ToList();
            int elems = visiveis.Sum(g => g.Elementos);
            var p = Propostas(out int conflitos);
            _rodapeInfo.Text = $"{visiveis.Count} linhas ({elems:N0} elementos)" + (visiveis.Count < _grupos.Count ? $" de {_grupos.Count} (filtro ativo)" : "") +
                               (conflitos > 0 ? $" · {conflitos} tipos com propostas diferentes ficam de fora" : "");
            _aplicar.IsEnabled = p.Count > 0;
            _aplicar.Content = p.Count == 0 ? "Nada a corrigir" : $"Corrigir escrita ({p.Count:N0})";
            AtualizarSelecionadas();
        }

        private void AtualizarSelecionadas()
        {
            int n = _tabela == null ? 0 : Propostas(out _, true).Count;
            _aplicarSelecionadas.IsEnabled = n > 0;
            _aplicarSelecionadas.Content = n > 0 ? $"Aplicar selecionadas ({n:N0})" : "Aplicar selecionadas";
        }

        private void Aplicar(bool soSelecionadas)
        {
            var p = Propostas(out _, soSelecionadas);
            if (p.Count == 0) return;
            int tipos = p.Count(l => l.Onde == "tipo");
            string msg = $"Gravar {p.Count} correções em {string.Join(", ", p.SelectMany(l => l.Gravacoes).Select(g => g.Parametro).Distinct())}?" +
                         (tipos > 0 ? $"\n\n{tipos} delas são em tipos e valem para todas as instâncias desses tipos." : "") +
                         "\n\nExemplos:\n" + string.Join("\n", p.GroupBy(l => (l.Preenchido, l.ValorProposto)).Take(5).Select(g => $"• {g.Key.Preenchido}  →  {g.Key.ValorProposto}")) +
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
            botao.SetBinding(ContentControl.ContentProperty, new Binding(nameof(GrupoVerificacao.Acao)));
            botao.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(GrupoVerificacao.Proposta)));
            botao.SetValue(Control.PaddingProperty, new Thickness(6, 0, 6, 0));
            botao.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 1, 2, 1));
            botao.SetValue(Control.FontSizeProperty, 11.0);
            var estilo = new Style(typeof(Button), TryFindResource(typeof(Button)) as Style);
            var semAcao = new DataTrigger { Binding = new Binding(nameof(GrupoVerificacao.Acao)), Value = null };
            semAcao.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
            estilo.Triggers.Add(semAcao);
            botao.SetValue(FrameworkElement.StyleProperty, estilo);
            botao.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((s, e) =>
            {
                if ((s as FrameworkElement)?.DataContext is GrupoVerificacao g) ExecutarAcao(g);
            }));
            return new DataGridTemplateColumn
            {
                Header = "Ação",
                Width = 112,
                CellTemplate = new DataTemplate { VisualTree = botao },
            };
        }

        /// <summary>Linhas escolhidas junto com a clicada que têm a mesma ação (a ação vale para todas).</summary>
        private List<GrupoVerificacao> Junto(GrupoVerificacao g)
        {
            var sel = Escolhidos();
            return sel.Contains(g) ? sel.Where(x => x.Acao == g.Acao).ToList() : new List<GrupoVerificacao> { g };
        }

        private void ExecutarAcao(GrupoVerificacao g)
        {
            try
            {
                List<GrupoVerificacao> grupos = Junto(g);
                List<LinhaVerificacao> linhas = grupos.SelectMany(x => x.Linhas).ToList();
                switch (g.Acao)
                {
                    case "Corrigir...":
                    case "Escolher...":
                        // Abre a lista com a proposta marcada (se houver): aceitar ou escolher outro item, e sobrescrever.
                        Escolher(g, linhas, grupos.Select(x => x.Item).Distinct().Count() == 1 ? g.Item ?? ItemProposto(g) : null);
                        break;
                    case "Criar parâmetro":
                        CriarParametro(g.Parametro, linhas.Select(x => x.Categoria).Distinct().ToList());
                        break;
                    case "Remover fórmula":
                        RemoverFormulas(new List<ElementId> { g.Primeira.FamiliaFormula }, new List<string> { g.Bloqueio }, g.Parametro);
                        break;
                    default: Mostrar(g); break;
                }
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Title); }
        }

        /// <summary>Item da lista que a proposta grava (código parecido, texto reconhecido).</summary>
        private static ClassItem ItemProposto(GrupoVerificacao g)
        {
            ValorClassificacao v = ValorClassificacao.Ler(g.Primeira.ValorProposto);
            string cod = v.Valido ? v.Codigo : g.Primeira.ValorProposto?.Split('·')[0].Trim();
            return cod != null && g.SistemaRef.Lista.PorCodigo.TryGetValue(cod, out ClassItem it) ? it : null;
        }

        private void Escolher(GrupoVerificacao g, List<LinhaVerificacao> linhas, ClassItem sugerido)
        {
            SistemaClassificacao s = g.SistemaRef;
            var w = new EscolherItemWindow(s.Lista, s.Nome, null, $"{linhas.Count} {(linhas.Count == 1 ? "elemento" : "elementos")}",
                linhas.Select(x => x.Preenchido).Distinct().Count() == 1 ? g.Preenchido : null, sugerido) { Owner = this };
            if (w.ShowDialog() != true || w.Escolhido == null) return;
            ClassItem item = w.Escolhido;
            var gravar = new List<(ElementId, string, string)>();
            foreach (LinhaVerificacao x in linhas.Where(x => x.DonoId != null))
            {
                Element e = _doc.GetElement(x.ElementoId);
                if (s.Formato == FormatoClassificacao.CodigoDescricao)
                    gravar.Add((x.DonoId, s.ParametroCodigo, ValorClassificacao.Escrever(item.Codigo, item.Descricao, s.GravarNome ? s.Nome : null)));
                else
                {
                    gravar.Add((x.DonoId, s.ParametroCodigo, item.Codigo));
                    if (!string.IsNullOrWhiteSpace(s.ParametroDescricao) && item.Descricao.Length > 0) Tambem(e, s.ParametroDescricao, item.Descricao);
                }
                if (!string.IsNullOrWhiteSpace(s.ParametroDescricaoLonga) && !string.IsNullOrEmpty(item.DescricaoLonga)) Tambem(e, s.ParametroDescricaoLonga, item.DescricaoLonga);
            }

            void Tambem(Element e, string parametro, string valor)
            {
                AlvoClassificacao ad = e == null ? null : ClassificationCore.Alvo(_doc, e, parametro);
                if (ad?.Parametro != null && !ad.Parametro.IsReadOnly) gravar.Add((ad.Dono.Id, parametro, valor));
            }

            int tipos = linhas.Count(x => x.Onde == "tipo");
            if (tipos > 0 && MessageBox.Show(this, $"{s.ParametroCodigo} está no tipo em {tipos} {(tipos == 1 ? "elemento" : "elementos")}: " +
                    $"{item.Codigo} vai valer para todas as instâncias desses tipos. Continuar?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            MostrarResultado(ClassificationCore.Gravar(_doc, gravar, "Classificação " + item.Codigo, Confirmar), "gravações");
            Verificar();
        }

        /// <summary>Seleciona os elementos no Revit e explica por que não dá para gravar.</summary>
        private void Mostrar(GrupoVerificacao g)
        {
            var ids = g.Linhas.Select(l => l.ElementoId).ToList();
            _uidoc.Selection.SetElementIds(ids);
            if (ids.Count == 1) { try { _uidoc.ShowElements(ids[0]); } catch { } }
            string texto = !string.IsNullOrEmpty(g.Bloqueio) ? "Não dá para gravar: " + g.Bloqueio : g.Proposta;
            _rodapeInfo.Text = $"{ids.Count} elementos selecionados no Revit." + (string.IsNullOrEmpty(texto) ? "" : " " + texto);
        }

        private void Selecionar()
        {
            if (_tabela == null) return;
            var origem = Escolhidos();
            if (origem.Count == 0) origem = _tabela.VisibleItems<GrupoVerificacao>().ToList();
            var ids = origem.SelectMany(g => g.Linhas).Select(l => l.ElementoId).Distinct().ToList();
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
            sb.AppendLine("Situação;Classificação;Parâmetro;Elemento;Id;Categoria;Onde;Está escrito;Na lista;O que muda;Bloqueio");
            foreach (LinhaVerificacao l in _tabela.VisibleItems<GrupoVerificacao>().SelectMany(g => g.Linhas))
                sb.AppendLine(string.Join(";", new[] { l.Situacao, l.Sistema, l.Parametro, l.Elemento, l.Id.ToString(), l.Categoria, l.Onde, l.Preenchido, l.NaLista, l.Proposta, l.Bloqueio }
                    .Select(v => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"")));
            try { File.WriteAllText(dialogo.FileName, sb.ToString(), new UTF8Encoding(true)); }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível gravar o arquivo:\n" + ex.Message, Title); }
        }
    }
}
