using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MetroSP.Ui;

namespace MetroSP.Parametros
{
    /// <summary>
    /// Parâmetros compartilhados do arquivo em duas abas:
    /// "Parâmetros do arquivo" (todos, com o .txt de origem achado pelo GUID nas referências) e
    /// "Comparar com .txt" (painel de arquivos de referência e compatibilidade com o modelo).
    /// Filtro por coluna, ordenação por várias colunas, renomear e exportar .txt/.csv.
    /// </summary>
    public class ListaCompartilhadosWindow : Window
    {
        /// <summary>Renomeia no modelo: (parâmetro, nome sugerido, .txt sugerido, atualizar .txt?) → true se renomeou.</summary>
        private readonly Func<SharedParameterInfo, string, string, bool?, bool> _renomear;
        private readonly Func<List<SharedParameterInfo>> _recarregar;
        /// <summary>Grava o .txt pela API do Revit; retorna os itens que falharam.</summary>
        private readonly Func<List<SharedParameterInfo>, string, List<string>> _exportarTxt;
        private readonly string _documento;
        private readonly string _arquivoRevit;
        private readonly bool _ehFamilia;
        private readonly ReferenceLibrary _refs;
        private List<SharedParameterInfo> _itens;
        private SharedParamTxt _txtSelecionado;
        private bool _umaLinha = true;
        /// <summary>true = só parâmetros de projeto, como o diálogo Parâmetros de projeto do Revit.</summary>
        private bool _soProjeto;
        private readonly RadioButton _escopoProjeto;
        private readonly RadioButton _escopoTodos;

        private readonly TabControl _abas;
        private readonly TabItem _abaArquivo;
        private readonly TabItem _abaComparar;
        private readonly DockPanel _dockArquivo;
        private readonly DockPanel _dockComparar;
        private readonly WrapPanel _resumoArquivo;
        private readonly WrapPanel _resumoComparar;
        private readonly StackPanel _cards;
        private readonly CheckBox _mostrarSoNoTxt;
        private readonly Button _renomearParaTxt;
        private readonly Button _atualizarTxt;
        private readonly Button _definirBase;
        private readonly Button _remover;
        private readonly TextBlock _rodapeInfo;
        private FilterableGrid _gridArquivo;
        private FilterableGrid _gridComparar;
        private UIElement _vazioComparar;

        public ListaCompartilhadosWindow(List<SharedParameterInfo> itens, string documento, bool ehFamilia, string arquivoRevit,
            Func<SharedParameterInfo, string, string, bool?, bool> renomear,
            Func<List<SharedParameterInfo>> recarregar,
            Func<List<SharedParameterInfo>, string, List<string>> exportarTxt)
        {
            _itens = itens;
            _documento = documento;
            _ehFamilia = ehFamilia;
            _arquivoRevit = arquivoRevit;
            _renomear = renomear;
            _recarregar = recarregar;
            _exportarTxt = exportarTxt;
            _refs = ReferenceLibrary.Abrir();
            _soProjeto = !ehFamilia;

            Title = $"MetroSP - Parâmetros compartilhados: {documento}";
            Width = 1300;
            Height = 700;
            MinWidth = 800;
            MinHeight = 380;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // Aba 1: parâmetros do arquivo.
            _dockArquivo = new DockPanel();
            _resumoArquivo = new WrapPanel();
            _dockArquivo.Children.Add(BarraSuperior(_resumoArquivo,
                "Shift+clique no cabeçalho ordena por várias colunas · duplo clique renomeia"));

            // Aba 2: comparar com .txt.
            _dockComparar = new DockPanel();
            _resumoComparar = new WrapPanel();
            _mostrarSoNoTxt = new CheckBox
            {
                Content = "Mostrar \"só no .txt\"",
                ToolTip = "Inclui os parâmetros do .txt que não estão no modelo.",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 16, 0),
            };
            _mostrarSoNoTxt.Click += (s, e) => _gridComparar?.Refresh();
            _dockComparar.Children.Add(BarraSuperior(_resumoComparar, null, _mostrarSoNoTxt));

            _renomearParaTxt = new Button { Content = "Renomear no modelo para o nome do .txt", IsEnabled = false };
            _renomearParaTxt.Click += (s, e) => RenomearParaNomeDoTxt();
            _atualizarTxt = new Button { Content = "Atualizar o .txt com o nome do modelo", IsEnabled = false };
            _atualizarTxt.Click += (s, e) => AtualizarTxtComNomeDoModelo();
            var acoes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            acoes.Children.Add(_renomearParaTxt);
            _atualizarTxt.Margin = new Thickness(8, 0, 0, 0);
            acoes.Children.Add(_atualizarTxt);
            DockPanel.SetDock(acoes, Dock.Top);
            _dockComparar.Children.Add(acoes);

            _cards = new StackPanel();
            _definirBase = new Button { Content = "Definir como base", IsEnabled = false };
            _remover = new Button { Content = "Remover", IsEnabled = false };
            var adicionarTxt = new Button { Content = "+ Adicionar .txt" };
            var adicionarPasta = new Button { Content = "+ Adicionar pasta" };
            adicionarTxt.Click += (s, e) => AdicionarTxt();
            adicionarPasta.Click += (s, e) => AdicionarPasta();
            _definirBase.Click += (s, e) => DefinirBase();
            _remover.Click += (s, e) => RemoverReferencia();

            var comparar = new Grid();
            comparar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300), MinWidth = 220 });
            comparar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            comparar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            UIElement lateral = PainelLateral(adicionarTxt, adicionarPasta);
            comparar.Children.Add(lateral);
            var divisor = new GridSplitter { Width = 8, HorizontalAlignment = HorizontalAlignment.Stretch, Background = System.Windows.Media.Brushes.Transparent };
            Grid.SetColumn(divisor, 1);
            comparar.Children.Add(divisor);
            Grid.SetColumn(_dockComparar, 2);
            comparar.Children.Add(_dockComparar);

            _abaArquivo = new TabItem { Header = "Parâmetros do arquivo", Content = _dockArquivo };
            _abaComparar = new TabItem { Header = "Comparar com .txt", Content = comparar };
            _abas = new TabControl();
            _abas.Items.Add(_abaArquivo);
            _abas.Items.Add(_abaComparar);
            _abas.SelectionChanged += (s, e) => { if (e.Source == _abas) AtualizarRodape(); };

            // Rodapé.
            var copiar = new Button { Content = "Copiar GUID" };
            var exportar = new Button { Content = "Exportar ▾" };
            var renomearBtn = new Button { Content = "Renomear...", Style = Theme.PrimaryButton };
            var fechar = new Button { Content = "Fechar", IsCancel = true };
            copiar.Click += (s, e) => CopiarGuid();
            exportar.Click += (s, e) => AbrirMenuExportar(exportar);
            renomearBtn.Click += (s, e) => RenomearSelecionado();
            fechar.Click += (s, e) => Close();

            _rodapeInfo = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center };
            var rodape = new DockPanel();
            StackPanel botoes = Theme.Buttons(copiar, exportar, renomearBtn, fechar);
            DockPanel.SetDock(botoes, Dock.Right);
            rodape.Children.Add(botoes);
            rodape.Children.Add(_rodapeInfo);

            // Escopo, acima das abas: vale para as duas.
            _escopoProjeto = new RadioButton
            {
                GroupName = "escopo",
                IsChecked = true,
                Margin = new Thickness(0, 0, 18, 0),
                ToolTip = "Os mesmos itens do diálogo Gerenciar › Parâmetros de projeto do Revit, inclusive os não compartilhados.",
            };
            _escopoTodos = new RadioButton
            {
                GroupName = "escopo",
                ToolTip = "Inclui os parâmetros compartilhados que existem no arquivo só porque famílias ou etiquetas carregadas os usam.\n" +
                          "Eles não aparecem no diálogo Parâmetros de projeto.",
            };
            _escopoProjeto.Checked += (s, e) => MudarEscopo(true);
            _escopoTodos.Checked += (s, e) => MudarEscopo(false);
            var escopo = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            escopo.Children.Add(new TextBlock { Text = "Mostrar:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
            escopo.Children.Add(_escopoProjeto);
            escopo.Children.Add(_escopoTodos);
            var corpo = new DockPanel();
            if (!ehFamilia)
            {
                DockPanel.SetDock(escopo, Dock.Top);
                corpo.Children.Add(escopo);
            }
            corpo.Children.Add(_abas);

            Theme.Apply(this, "Parâmetros compartilhados", documento, corpo, rodape);
            CarregarReferencias();
        }

        // ------------------------------------------------------------------ montagem

        private DockPanel BarraSuperior(WrapPanel resumo, string dica, params UIElement[] extras)
        {
            var topo = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            if (dica != null)
            {
                var t = new TextBlock { Text = dica, Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(t, Dock.Right);
                topo.Children.Add(t);
            }
            var umaLinha = new CheckBox
            {
                Content = "Uma linha por item",
                ToolTip = "Textos longos terminam em \"...\"; passe o mouse para ver o conteúdo completo.",
                IsChecked = _umaLinha,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 16, 0),
            };
            umaLinha.Click += (s, e) =>
            {
                _umaLinha = umaLinha.IsChecked == true;
                if (_gridArquivo != null) _gridArquivo.SingleLine = _umaLinha;
                if (_gridComparar != null) _gridComparar.SingleLine = _umaLinha;
            };
            DockPanel.SetDock(umaLinha, Dock.Right);
            topo.Children.Add(umaLinha);
            foreach (UIElement x in extras)
            {
                DockPanel.SetDock(x, Dock.Right);
                topo.Children.Add(x);
            }
            topo.Children.Add(resumo);
            DockPanel.SetDock(topo, Dock.Top);
            return topo;
        }

        private UIElement PainelLateral(Button adicionarTxt, Button adicionarPasta)
        {
            var painel = new DockPanel();
            var titulo = new TextBlock { Text = "Arquivos de referência", FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(0, 0, 0, 2) };
            DockPanel.SetDock(titulo, Dock.Top);
            painel.Children.Add(titulo);
            var sub = new TextBlock
            {
                Text = "Não precisam estar carregados no Revit. Clique num arquivo para comparar.",
                Foreground = Theme.Solid(Theme.Cinza),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
            };
            DockPanel.SetDock(sub, Dock.Top);
            painel.Children.Add(sub);

            var grade = new UniformGrid2x2(adicionarTxt, adicionarPasta, _definirBase, _remover);
            DockPanel.SetDock(grade, Dock.Bottom);
            painel.Children.Add(grade);

            painel.Children.Add(new ScrollViewer
            {
                Content = _cards,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 8),
            });
            return new Border
            {
                Background = Theme.Solid(Theme.CinzaFundo),
                BorderBrush = Theme.Solid(Theme.CinzaClaro),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10),
                Child = painel,
            };
        }

        /// <summary>Quatro botões em duas linhas, mesma largura.</summary>
        private class UniformGrid2x2 : System.Windows.Controls.Primitives.UniformGrid
        {
            public UniformGrid2x2(params Button[] botoes)
            {
                Columns = 2;
                foreach (Button b in botoes)
                {
                    b.Margin = new Thickness(2);
                    b.MinWidth = 0;
                    Children.Add(b);
                }
            }
        }

        // ------------------------------------------------------------------ dados

        /// <summary>Itens do escopo escolhido (parâmetros de projeto, ou todos).</summary>
        private List<SharedParameterInfo> NoEscopo => _itens.Where(i => !_soProjeto || i.Vinculado).ToList();

        /// <summary>Só os compartilhados do escopo: os únicos que podem estar num .txt.</summary>
        private List<SharedParameterInfo> CompartilhadosNoEscopo => NoEscopo.Where(i => i.Compartilhado).ToList();

        private void MudarEscopo(bool soProjeto)
        {
            if (_gridArquivo == null || _soProjeto == soProjeto) return;
            _soProjeto = soProjeto;
            MontarAbaArquivo();
            MontarCards();
            MontarComparacao();
        }

        private void AtualizarRotulosEscopo()
        {
            if (_escopoProjeto == null) return;
            int projeto = _itens.Count(i => i.Vinculado);
            _escopoProjeto.Content = $"Parâmetros de projeto ({projeto}), como no Revit";
            _escopoTodos.Content = $"Todos os compartilhados do arquivo ({_itens.Count(i => i.Compartilhado)}), inclusive os de famílias";
        }

        /// <summary>Lê de novo os arquivos de referência e recalcula as duas abas.</summary>
        private void CarregarReferencias()
        {
            Cursor = Cursors.Wait;
            try
            {
                _refs.Carregar(_arquivoRevit);
                SharedParameterCollector.Enriquecer(_itens, _refs);
                if (_txtSelecionado != null)
                    _txtSelecionado = _refs.Arquivos.FirstOrDefault(a => string.Equals(a.Caminho, _txtSelecionado.Caminho, StringComparison.OrdinalIgnoreCase));
                if (_txtSelecionado == null) _txtSelecionado = _refs.Arquivos.FirstOrDefault();
                MontarAbaArquivo();
                MontarCards();
                MontarComparacao();
            }
            finally { Cursor = null; }
        }

        /// <summary>Lê de novo os parâmetros do modelo (depois de renomear).</summary>
        private void RecarregarModelo()
        {
            _itens = _recarregar();
            CarregarReferencias();
        }

        private void MontarAbaArquivo()
        {
            if (_gridArquivo != null) _dockArquivo.Children.Remove(_gridArquivo);

            var g = new FilterableGrid(_itens, typeof(SharedParameterInfo));
            g.AddColumn("Nome", nameof(SharedParameterInfo.Nome), 200);
            g.AddColumn("GUID", nameof(SharedParameterInfo.GuidTexto), 255);
            g.AddColumn("Tipo", nameof(SharedParameterInfo.TipoDeDado), 95);
            if (!_ehFamilia) g.AddColumn("Onde está", nameof(SharedParameterInfo.Onde), 165);
            g.AddColumn("Inst./Tipo", nameof(SharedParameterInfo.InstanciaOuTipo), 75);
            g.AddColumn("Categorias", nameof(SharedParameterInfo.Categorias), 170, wrap: true);
            if (_ehFamilia) g.AddColumn("Fórmula", nameof(SharedParameterInfo.Formula), 90);
            g.AddColumn("Arquivo de origem", nameof(SharedParameterInfo.Origem), 170, wrap: true);
            g.AddColumn("Grupo no .txt", nameof(SharedParameterInfo.GrupoNoArquivo), 110);
            g.AddColumn("Situação no .txt", nameof(SharedParameterInfo.Situacao), 160, fill: true);
            g.SingleLine = _umaLinha;
            g.ExtraFilter = o => !_soProjeto || ((SharedParameterInfo)o).Vinculado;
            g.Refresh();
            g.Grid.LoadingRow += (s, e) =>
            {
                var i = (SharedParameterInfo)e.Row.Item;
                e.Row.Background = i.Gravidade == 2 ? Theme.LinhaProblema : i.Gravidade == 1 ? Theme.LinhaAviso : Theme.LinhaNormal;
            };
            g.Grid.MouseDoubleClick += (s, e) => RenomearSelecionado();
            g.Filtered += AtualizarRodape;
            Theme.StyleSelection(g.Grid);
            _gridArquivo = g;
            _dockArquivo.Children.Add(g);

            AtualizarRotulosEscopo();
            List<SharedParameterInfo> escopo = NoEscopo;
            _resumoArquivo.Children.Clear();
            _resumoArquivo.Children.Add(Theme.Chip($"{escopo.Count} parâmetros"));
            if (!_ehFamilia)
            {
                int naoCompartilhados = escopo.Count(i => !i.Compartilhado);
                _resumoArquivo.Children.Add(Theme.Chip($"{escopo.Count(i => i.Compartilhado)} compartilhados"));
                if (naoCompartilhados > 0) _resumoArquivo.Children.Add(Theme.Chip($"{naoCompartilhados} não compartilhados"));
                if (!_soProjeto) _resumoArquivo.Children.Add(Theme.Chip($"{escopo.Count(i => !i.Vinculado)} só em famílias / tags"));
            }
            if (_refs.Arquivos.Count == 0)
            {
                _resumoArquivo.Children.Add(Theme.Chip("sem arquivos de referência: adicione em \"Comparar com .txt\"", destaque: true));
            }
            else
            {
                int semOrigem = escopo.Count(i => i.Gravidade == 1);
                int incompativeis = escopo.Count(i => i.Gravidade == 2);
                _resumoArquivo.Children.Add(Theme.Chip($"{semOrigem} sem origem", destaque: semOrigem > 0));
                _resumoArquivo.Children.Add(Theme.Chip($"{incompativeis} incompatíveis", escuro: incompativeis > 0));
            }
            AtualizarRodape();
        }

        private void MontarCards()
        {
            _cards.Children.Clear();
            foreach (SharedParamTxt txt in _refs.Arquivos)
                _cards.Children.Add(Card(txt));
            if (_refs.Arquivos.Count == 0)
            {
                _cards.Children.Add(new TextBlock
                {
                    Text = "Nenhum arquivo ainda. Adicione o .txt base do Metrô ou uma pasta com vários .txt.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Theme.Solid(Theme.Cinza),
                    Margin = new Thickness(0, 4, 0, 0),
                });
            }
            _definirBase.IsEnabled = _txtSelecionado != null && !_txtSelecionado.EhBase;
            _remover.IsEnabled = _txtSelecionado != null && !_txtSelecionado.EhRevitAtual;
        }

        private Border Card(SharedParamTxt txt)
        {
            bool selecionado = txt == _txtSelecionado;
            List<SharedParameterInfo> modelo = CompartilhadosNoEscopo;
            int doModelo = modelo.Count(i => txt.PorGuid.ContainsKey(i.Guid));

            var conteudo = new StackPanel();
            var linha1 = new DockPanel();
            if (txt.EhBase || txt.EhRevitAtual)
            {
                Border tag = Theme.Chip(txt.EhBase ? "BASE METRÔ" : "REVIT ATUAL", destaque: txt.EhBase);
                tag.Margin = new Thickness(6, 0, 0, 0);
                DockPanel.SetDock(tag, Dock.Right);
                linha1.Children.Add(tag);
            }
            linha1.Children.Add(new TextBlock { Text = txt.Nome, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            conteudo.Children.Add(linha1);

            string origem = string.Equals(txt.Entrada, txt.Caminho, StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(txt.Caminho)
                : "pasta: " + txt.Entrada;
            conteudo.Children.Add(new TextBlock
            {
                Text = origem,
                ToolTip = txt.Caminho,
                FontSize = 10.5,
                Foreground = Theme.Solid(Theme.Cinza),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 1, 0, 4),
            });
            conteudo.Children.Add(new TextBlock
            {
                Text = txt.Erro != null
                    ? "Erro ao ler: " + txt.Erro
                    : $"{txt.Definicoes.Count} parâmetros · {doModelo} de {modelo.Count} do modelo",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
            });

            // Barra: quanto do modelo está neste arquivo.
            var barra = new Grid { Height = 4, Margin = new Thickness(0, 5, 0, 0), Background = Theme.Solid(Theme.CinzaClaro) };
            barra.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(doModelo, 0.0001), GridUnitType.Star) });
            barra.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(modelo.Count - doModelo, 0.0001), GridUnitType.Star) });
            barra.Children.Add(new Border { Background = Theme.Solid(Theme.Laranja) });
            conteudo.Children.Add(barra);

            var card = new Border
            {
                Background = selecionado ? Theme.Solid(Theme.LaranjaSelecao) : System.Windows.Media.Brushes.White,
                BorderBrush = Theme.Solid(selecionado ? Theme.Laranja : Theme.CinzaClaro),
                BorderThickness = new Thickness(selecionado ? 2 : 1),
                Padding = new Thickness(selecionado ? 8 : 9),
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = Cursors.Hand,
                Child = conteudo,
            };
            card.MouseLeftButtonUp += (s, e) =>
            {
                _txtSelecionado = txt;
                MontarCards();
                MontarComparacao();
            };
            return card;
        }

        private void MontarComparacao()
        {
            if (_gridComparar != null) _dockComparar.Children.Remove(_gridComparar);
            if (_vazioComparar != null) _dockComparar.Children.Remove(_vazioComparar);
            _gridComparar = null;
            _vazioComparar = null;
            _resumoComparar.Children.Clear();
            _renomearParaTxt.IsEnabled = _atualizarTxt.IsEnabled = false;

            if (_txtSelecionado == null)
            {
                _abaComparar.Header = "Comparar com .txt";
                _vazioComparar = new TextBlock
                {
                    Text = "Adicione um arquivo .txt ou uma pasta de referência no painel ao lado.",
                    Foreground = Theme.Solid(Theme.Cinza),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                _dockComparar.Children.Add(_vazioComparar);
                AtualizarRodape();
                return;
            }

            List<ComparacaoItem> linhas = Comparar();
            var g = new FilterableGrid(linhas, typeof(ComparacaoItem));
            g.AddColumn("Situação", nameof(ComparacaoItem.Situacao), 165);
            g.AddColumn("Nome no modelo", nameof(ComparacaoItem.NomeModelo), 190);
            g.AddColumn("Nome no .txt", nameof(ComparacaoItem.NomeTxt), 190);
            g.AddColumn("Tipo modelo", nameof(ComparacaoItem.TipoModelo), 95);
            g.AddColumn("Tipo .txt", nameof(ComparacaoItem.TipoNoTxt), 95);
            g.AddColumn("Grupo no .txt", nameof(ComparacaoItem.GrupoNoTxt), 110);
            g.AddColumn("GUID", nameof(ComparacaoItem.Guid), 255, fill: true);
            g.SingleLine = _umaLinha;
            g.ExtraFilter = o => _mostrarSoNoTxt.IsChecked == true || !((ComparacaoItem)o).SoNoTxt;
            g.Refresh();
            g.Grid.LoadingRow += (s, e) =>
            {
                var c = (ComparacaoItem)e.Row.Item;
                e.Row.Background = c.Compativel ? Theme.LinhaNormal
                    : c.Situacao == Comparacao.SoNoModelo || c.SoNoTxt ? Theme.LinhaAviso
                    : Theme.LinhaProblema;
            };
            g.Grid.SelectionChanged += (s, e) =>
            {
                var c = g.Grid.SelectedItem as ComparacaoItem;
                bool nomeDiferente = c != null && c.Situacao == Comparacao.NomeDiferente;
                _renomearParaTxt.IsEnabled = nomeDiferente;
                _atualizarTxt.IsEnabled = nomeDiferente;
            };
            g.Filtered += AtualizarRodape;
            Theme.StyleSelection(g.Grid);
            _gridComparar = g;
            _dockComparar.Children.Add(g);

            int nome = linhas.Count(c => c.Situacao == Comparacao.NomeDiferente);
            int tipo = linhas.Count(c => c.Situacao == Comparacao.TipoDiferente);
            int emUso = linhas.Count(c => c.Situacao == Comparacao.NomeEmUso);
            int soModelo = linhas.Count(c => c.Situacao == Comparacao.SoNoModelo);
            int soTxt = linhas.Count(c => c.SoNoTxt);
            _resumoComparar.Children.Add(Theme.Chip($"{linhas.Count(c => c.Compativel)} compatíveis"));
            _resumoComparar.Children.Add(Theme.Chip($"{nome} nome diferente", escuro: nome > 0));
            _resumoComparar.Children.Add(Theme.Chip($"{tipo} tipo diferente", escuro: tipo > 0));
            _resumoComparar.Children.Add(Theme.Chip($"{emUso} nome em uso", escuro: emUso > 0));
            _resumoComparar.Children.Add(Theme.Chip($"{soModelo} só no modelo", destaque: soModelo > 0));
            _resumoComparar.Children.Add(Theme.Chip($"{soTxt} só no .txt"));

            int incompativeis = nome + tipo + emUso;
            _abaComparar.Header = incompativeis > 0 ? $"Comparar com .txt ({incompativeis})" : "Comparar com .txt";
            AtualizarRodape();
        }

        private List<ComparacaoItem> Comparar() =>
            Comparacao.Comparar(CompartilhadosNoEscopo, _txtSelecionado, _itens.Where(i => i.Compartilhado).Select(i => i.Guid));

        private bool NaAbaComparar => _abas.SelectedItem == _abaComparar;

        private void AtualizarRodape()
        {
            if (_rodapeInfo == null) return;
            if (NaAbaComparar)
            {
                if (_gridComparar == null) { _rodapeInfo.Text = ""; return; }
                int v = _gridComparar.VisibleItems<ComparacaoItem>().Count();
                _rodapeInfo.Text = $"{v} itens visíveis · comparando com {_txtSelecionado?.Nome}";
            }
            else if (_gridArquivo != null)
            {
                int total = NoEscopo.Count;
                int v = _gridArquivo.VisibleItems<SharedParameterInfo>().Count();
                _rodapeInfo.Text = v == total ? $"{total} itens" : $"{v} de {total} visíveis (filtro ativo)";
            }
        }

        // ------------------------------------------------------------------ ações

        private void RenomearSelecionado()
        {
            SharedParameterInfo item = NaAbaComparar
                ? (_gridComparar?.Grid.SelectedItem as ComparacaoItem)?.Modelo
                : _gridArquivo?.Grid.SelectedItem as SharedParameterInfo;
            if (item == null) return;
            if (!item.Compartilhado)
            {
                MessageBox.Show(this, $"\"{item.Nome}\" é um parâmetro de projeto não compartilhado: o Revit permite renomeá-lo direto em " +
                                      "Gerenciar › Parâmetros de projeto.", Title);
                return;
            }
            if (_renomear(item, null, item.Origens.FirstOrDefault()?.Arquivo.Caminho, null)) RecarregarModelo();
        }

        private void RenomearParaNomeDoTxt()
        {
            if (!(_gridComparar?.Grid.SelectedItem is ComparacaoItem c) || c.Modelo == null || c.Txt == null) return;
            if (_renomear(c.Modelo, c.NomeTxt, c.Txt.Arquivo.Caminho, false)) RecarregarModelo();
        }

        private void AtualizarTxtComNomeDoModelo()
        {
            if (!(_gridComparar?.Grid.SelectedItem is ComparacaoItem c) || c.Modelo == null || c.Txt == null) return;
            string caminho = c.Txt.Arquivo.Caminho;
            if (SharedParameterFile.NameTakenByOtherGuid(caminho, c.Guid, c.NomeModelo))
            {
                MessageBox.Show(this, $"O arquivo {c.Txt.Arquivo.Nome} já tem outro parâmetro (GUID diferente) chamado \"{c.NomeModelo}\".", Title);
                return;
            }
            if (MessageBox.Show(this,
                    $"Alterar no arquivo {c.Txt.Arquivo.Nome}:\n\n\"{c.NomeTxt}\" → \"{c.NomeModelo}\"\n\nO GUID é mantido e o arquivo atual fica salvo como .bak.",
                    Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                if (!SharedParameterFile.Rename(caminho, c.Guid, c.NomeModelo))
                    MessageBox.Show(this, "O GUID não foi encontrado no arquivo; nada foi alterado.", Title);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível gravar o arquivo:\n" + ex.Message, Title);
            }
            CarregarReferencias();
        }

        private void CopiarGuid()
        {
            IEnumerable<Guid> guids = NaAbaComparar
                ? _gridComparar?.Grid.SelectedItems.Cast<ComparacaoItem>().Select(c => c.Guid)
                : _gridArquivo?.Grid.SelectedItems.Cast<SharedParameterInfo>().Where(i => i.Compartilhado).Select(i => i.Guid);
            List<string> lista = guids?.Select(g => g.ToString()).ToList();
            if (lista != null && lista.Count > 0) Clipboard.SetText(string.Join(Environment.NewLine, lista));
        }

        private void AdicionarTxt()
        {
            var dialogo = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Arquivos de parâmetros compartilhados de referência",
                Filter = "Parâmetros compartilhados (*.txt)|*.txt|Todos os arquivos (*.*)|*.*",
                Multiselect = true,
            };
            if (dialogo.ShowDialog(this) != true) return;
            foreach (string f in dialogo.FileNames) _refs.Adicionar(f);
            _refs.Salvar();
            _txtSelecionado = new SharedParamTxt { Caminho = dialogo.FileNames[0] };
            CarregarReferencias();
        }

        private void AdicionarPasta()
        {
            var dialogo = new Microsoft.Win32.OpenFolderDialog { Title = "Pasta com arquivos .txt de parâmetros compartilhados" };
            if (dialogo.ShowDialog(this) != true) return;
            _refs.Adicionar(dialogo.FolderName);
            _refs.Salvar();
            CarregarReferencias();
        }

        private void DefinirBase()
        {
            if (_txtSelecionado == null) return;
            if (_txtSelecionado.EhRevitAtual) _refs.Adicionar(_txtSelecionado.Caminho);
            _refs.Base = _txtSelecionado.Caminho;
            _refs.Salvar();
            CarregarReferencias();
        }

        private void RemoverReferencia()
        {
            SharedParamTxt t = _txtSelecionado;
            if (t == null || t.EhRevitAtual) return;
            bool daPasta = !string.Equals(t.Entrada, t.Caminho, StringComparison.OrdinalIgnoreCase);
            string pergunta = daPasta
                ? $"Este arquivo vem da pasta:\n{t.Entrada}\n\nRemover a pasta inteira das referências? (os arquivos não são apagados)"
                : $"Remover {t.Nome} das referências? (o arquivo não é apagado)";
            if (MessageBox.Show(this, pergunta, Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _refs.Remover(t);
            _refs.Salvar();
            _txtSelecionado = null;
            CarregarReferencias();
        }

        // ------------------------------------------------------------------ exportar

        private enum Escopo { Visiveis, Selecionados, Todos }

        private void AbrirMenuExportar(Button origem)
        {
            var menu = new ContextMenu { PlacementTarget = origem, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
            bool comparar = NaAbaComparar;

            var txt = new MenuItem
            {
                Header = "Parâmetros compartilhados (.txt) para usar no Revit",
                IsEnabled = !comparar,
                ToolTip = comparar ? "Disponível na aba \"Parâmetros do arquivo\"." : "Mesmo GUID, nome, tipo e visibilidade.",
            };
            var csv = new MenuItem { Header = "Planilha (.csv)" };
            foreach (Escopo e in new[] { Escopo.Visiveis, Escopo.Selecionados, Escopo.Todos })
            {
                string rotulo = e == Escopo.Visiveis ? "Visíveis (com os filtros atuais)" : e == Escopo.Selecionados ? "Selecionados" : "Todos";
                var mt = new MenuItem { Header = rotulo };
                mt.Click += (s, a) => ExportarTxt(e);
                txt.Items.Add(mt);
                var mc = new MenuItem { Header = rotulo };
                mc.Click += (s, a) => ExportarCsv(e);
                csv.Items.Add(mc);
            }
            menu.Items.Add(txt);
            menu.Items.Add(csv);
            menu.IsOpen = true;
        }

        private static List<T> Itens<T>(FilterableGrid g, IEnumerable<T> todos, Escopo e) =>
            e == Escopo.Visiveis ? g.VisibleItems<T>().ToList()
            : e == Escopo.Selecionados ? g.Grid.SelectedItems.Cast<T>().ToList()
            : todos.ToList();

        private void ExportarTxt(Escopo e)
        {
            List<SharedParameterInfo> lista = Itens(_gridArquivo, NoEscopo, e).Where(i => i.Compartilhado).ToList();
            if (lista.Count == 0) { MessageBox.Show(this, "Nenhum parâmetro para exportar.", Title); return; }

            var dialogo = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Parâmetros compartilhados (*.txt)|*.txt",
                FileName = $"parametros_{Path.GetFileNameWithoutExtension(_documento)}.txt",
            };
            if (dialogo.ShowDialog(this) != true) return;

            // Nunca sobrescrever um arquivo de referência ou o configurado no Revit.
            var protegidos = _refs.Arquivos.Select(a => a.Caminho).Append(_arquivoRevit).Where(p => !string.IsNullOrEmpty(p));
            if (protegidos.Any(p => string.Equals(Path.GetFullPath(p), Path.GetFullPath(dialogo.FileName), StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, "Escolha outro nome: este arquivo é uma referência ou o arquivo configurado no Revit.", Title);
                return;
            }

            try
            {
                List<string> falhas = _exportarTxt(lista, dialogo.FileName);
                int ok = lista.Count - falhas.Count;
                MessageBox.Show(this,
                    $"{ok} parâmetros exportados para:\n{dialogo.FileName}" +
                    (falhas.Count > 0 ? "\n\nNão exportados:\n• " + string.Join("\n• ", falhas.Take(15)) + (falhas.Count > 15 ? "\n..." : "") : ""),
                    Title);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível exportar:\n" + ex.Message, Title);
            }
        }

        private void ExportarCsv(Escopo e)
        {
            var dialogo = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                FileName = NaAbaComparar
                    ? $"comparacao_{Path.GetFileNameWithoutExtension(_txtSelecionado?.Nome)}.csv"
                    : $"parametros_compartilhados_{Path.GetFileNameWithoutExtension(_documento)}.csv",
            };

            List<string[]> linhas;
            string cabecalho;
            if (NaAbaComparar)
            {
                if (_gridComparar == null) return;
                cabecalho = "Situação;Nome no modelo;Nome no .txt;Tipo modelo;Tipo .txt;Grupo no .txt;GUID;Arquivo";
                linhas = Itens(_gridComparar, Comparar(), e)
                    .Select(c => new[] { c.Situacao, c.NomeModelo, c.NomeTxt, c.TipoModelo, c.TipoNoTxt, c.GrupoNoTxt, c.Guid.ToString(), _txtSelecionado.Caminho })
                    .ToList();
            }
            else
            {
                cabecalho = "Nome;GUID;Tipo;Grupo;Onde está;Instância/Tipo;Categorias;Visível;Fórmula;Arquivo de origem;Grupo no .txt;Situação no .txt";
                linhas = Itens(_gridArquivo, NoEscopo, e)
                    .Select(i => new[] { i.Nome, i.GuidTexto, i.TipoDeDado, i.Grupo, i.Onde, i.InstanciaOuTipo, i.Categorias, i.Visivel, i.Formula, i.Origem, i.GrupoNoArquivo, i.Situacao })
                    .ToList();
            }
            if (linhas.Count == 0) { MessageBox.Show(this, "Nenhum item para exportar.", Title); return; }
            if (dialogo.ShowDialog(this) != true) return;

            var sb = new StringBuilder();
            sb.AppendLine(cabecalho);
            foreach (string[] l in linhas)
                sb.AppendLine(string.Join(";", l.Select(v => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"")));
            try { File.WriteAllText(dialogo.FileName, sb.ToString(), new UTF8Encoding(true)); }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível gravar o arquivo:\n" + ex.Message, Title); }
        }
    }
}
