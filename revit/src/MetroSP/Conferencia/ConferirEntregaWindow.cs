using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MetroSP.Commands;
using MetroSP.Ids;
using MetroSP.Ui;
using WColor = System.Windows.Media.Color;
using WGrid = System.Windows.Controls.Grid;

namespace MetroSP.Conferencia
{
    /// <summary>
    /// Janela não modal "Conferir Entrega Metrô": cartões das etapas com semáforo à esquerda e, à direita, o detalhe
    /// da etapa (matriz categoria × campo na etapa 4). Tudo que usa a API (conferir, selecionar, pintar, abrir as outras
    /// ferramentas, exportar) passa por um ExternalEvent, porque a janela fica aberta enquanto se trabalha no Revit.
    /// </summary>
    public class ConferirEntregaWindow : Window
    {
        // Cores do semáforo (fundo, texto e bolinha).
        private static readonly Brush VerdeFundo = Theme.Solid(WColor.FromRgb(0xDF, 0xF2, 0xE4)), VerdeTexto = Theme.Solid(WColor.FromRgb(0x1E, 0x6B, 0x35)), VerdePonto = Theme.Solid(WColor.FromRgb(0x2E, 0x9E, 0x4F));
        private static readonly Brush AmareloFundo = Theme.Solid(WColor.FromRgb(0xFD, 0xF1, 0xC7)), AmareloTexto = Theme.Solid(WColor.FromRgb(0x7A, 0x5C, 0x00)), AmareloPonto = Theme.Solid(WColor.FromRgb(0xE8, 0xB4, 0x00));
        private static readonly Brush VermelhoFundo = Theme.Solid(WColor.FromRgb(0xF9, 0xDC, 0xDB)), VermelhoTexto = Theme.Solid(WColor.FromRgb(0x9B, 0x25, 0x22)), VermelhoPonto = Theme.Solid(WColor.FromRgb(0xD6, 0x45, 0x41));
        private static readonly Brush CinzaFundo = Theme.Solid(WColor.FromRgb(0xEE, 0xEE, 0xEE)), CinzaTexto = Theme.Solid(WColor.FromRgb(0x88, 0x88, 0x88)), CinzaPonto = Theme.Solid(WColor.FromRgb(0xBB, 0xBB, 0xBB));

        private static Brush Fundo(Sinal s) => s == Sinal.Verde ? VerdeFundo : s == Sinal.Amarelo ? AmareloFundo : s == Sinal.Vermelho ? VermelhoFundo : CinzaFundo;
        private static Brush Texto(Sinal s) => s == Sinal.Verde ? VerdeTexto : s == Sinal.Amarelo ? AmareloTexto : s == Sinal.Vermelho ? VermelhoTexto : CinzaTexto;
        private static Brush Ponto(Sinal s) => s == Sinal.Verde ? VerdePonto : s == Sinal.Amarelo ? AmareloPonto : s == Sinal.Vermelho ? VermelhoPonto : CinzaPonto;

        private readonly Document _doc;
        private PacoteMetro _pacote;
        private ResultadoConferencia _res;
        private int _etapaAtual = 4;
        private CelulaMatriz _celula;
        private bool _soPendencias = true;

        private readonly AcoesRevit _acoes = new AcoesRevit();
        private readonly ExternalEvent _evento;

        private readonly TextBlock _pacoteNome, _pacoteInfo, _resumoTitulo, _resumoTexto, _resumoPct;
        private readonly ProgressBar _resumoBarra;
        private readonly StackPanel _cartoes;
        private readonly ContentControl _detalhe;
        private readonly System.Windows.Controls.ComboBox _escopo;
        private readonly Button _exportarIfc;

        /// <summary>Linha agrupada do detalhe de uma célula: tipo + problema.</summary>
        public class LinhaDetalhe
        {
            public string Problema { get; set; }
            public string Tipo { get; set; }
            public int Elementos { get; set; }
            public string Valor { get; set; }
            public List<ElementId> Ids { get; set; }
        }

        public ConferirEntregaWindow(Document doc, PacoteMetro pacote, ResultadoConferencia resultado)
        {
            _doc = doc;
            _pacote = pacote;
            _evento = ExternalEvent.Create(_acoes);

            Title = "MetroSP - Conferir Entrega Metrô";
            Width = 1240;
            Height = 760;
            MinWidth = 900;
            MinHeight = 520;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var corpo = new DockPanel();

            // ---------- barra do pacote
            var barra = new Border
            {
                Background = Theme.Solid(Theme.CinzaFundo),
                BorderBrush = Theme.Solid(Theme.CinzaClaro),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 0, 10),
            };
            var barraDock = new DockPanel();
            var botoesBarra = new StackPanel { Orientation = Orientation.Horizontal };
            var trocar = new Button { Content = "Padrões...", ToolTip = "Abrir ou editar os padrões do Metrô (IDS, setup, psets, planilha, MetroShared)." };
            _escopo = new System.Windows.Controls.ComboBox { Width = 150, Margin = new Thickness(8, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center };
            _escopo.Items.Add("Modelo inteiro");
            _escopo.Items.Add("Só a vista ativa");
            _escopo.SelectedIndex = _pacote.SoVistaAtiva ? 1 : 0;
            _escopo.ToolTip = "O que entra na conferência. Use a vista ativa quando o setup exporta só o visível numa vista 3D.";
            var conferir = new Button { Content = "Conferir de novo", Style = Theme.PrimaryButton, Margin = new Thickness(8, 0, 0, 0) };
            botoesBarra.Children.Add(trocar);
            botoesBarra.Children.Add(_escopo);
            botoesBarra.Children.Add(conferir);
            DockPanel.SetDock(botoesBarra, Dock.Right);
            barraDock.Children.Add(botoesBarra);
            var textosPacote = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _pacoteNome = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 13 };
            _pacoteInfo = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
            textosPacote.Children.Add(_pacoteNome);
            textosPacote.Children.Add(_pacoteInfo);
            barraDock.Children.Add(textosPacote);
            barra.Child = barraDock;
            DockPanel.SetDock(barra, Dock.Top);
            corpo.Children.Add(barra);

            // ---------- coluna dos cartões
            var esquerda = new DockPanel { Width = 300, Margin = new Thickness(0, 0, 12, 0) };
            var resumo = new Border
            {
                BorderBrush = Theme.Solid(Theme.CinzaClaro), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 8), Background = Brushes.White,
            };
            var resumoPainel = new StackPanel();
            var linhaPct = new DockPanel();
            _resumoPct = new TextBlock { FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(_resumoPct, Dock.Left);
            linhaPct.Children.Add(_resumoPct);
            var resumoTextos = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _resumoTitulo = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 13, TextWrapping = TextWrapping.Wrap };
            _resumoTexto = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), FontSize = 11, TextWrapping = TextWrapping.Wrap };
            resumoTextos.Children.Add(_resumoTitulo);
            resumoTextos.Children.Add(_resumoTexto);
            linhaPct.Children.Add(resumoTextos);
            resumoPainel.Children.Add(linhaPct);
            _resumoBarra = new ProgressBar { Height = 6, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 8, 0, 0), Foreground = Theme.Solid(Theme.Laranja), Background = Theme.Solid(Theme.CinzaClaro), BorderThickness = new Thickness(0) };
            resumoPainel.Children.Add(_resumoBarra);
            resumo.Child = resumoPainel;
            DockPanel.SetDock(resumo, Dock.Top);
            esquerda.Children.Add(resumo);
            _cartoes = new StackPanel();
            esquerda.Children.Add(new ScrollViewer { Content = _cartoes, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            DockPanel.SetDock(esquerda, Dock.Left);
            corpo.Children.Add(esquerda);

            // ---------- detalhe
            _detalhe = new ContentControl();
            corpo.Children.Add(_detalhe);

            // ---------- rodapé
            var csv = new Button { Content = "Exportar pendências (.csv)..." };
            var limpar = new Button { Content = "Limpar pintura", ToolTip = "Devolve a aparência original dos elementos pintados pela conferência." };
            var fechar = new Button { Content = "Fechar" };
            _exportarIfc = new Button { Content = "Exportar IFC...", Style = Theme.PrimaryButton, ToolTip = "Abre o Exportar IFC com o setup e o .txt dos padrões." };
            var rodape = new DockPanel();
            StackPanel direita = Theme.Buttons(fechar, _exportarIfc);
            DockPanel.SetDock(direita, Dock.Right);
            rodape.Children.Add(direita);
            var esquerdaRodape = new StackPanel { Orientation = Orientation.Horizontal };
            esquerdaRodape.Children.Add(csv);
            limpar.Margin = new Thickness(8, 0, 0, 0);
            esquerdaRodape.Children.Add(limpar);
            esquerdaRodape.Children.Add(new TextBlock
            {
                Text = "Amarelo não impede a entrega; vermelho é pendência.",
                Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0),
            });
            rodape.Children.Add(esquerdaRodape);

            trocar.Click += (s, e) => EditarPacote();
            conferir.Click += (s, e) => Conferir();
            _escopo.SelectionChanged += (s, e) =>
            {
                _pacote.SoVistaAtiva = _escopo.SelectedIndex == 1;
                _pacote.SalvarComoAtual();
                Conferir();
            };
            csv.Click += (s, e) => ExportarCsv();
            limpar.Click += (s, e) => Revit(app =>
            {
                int n = PinturaVista.Limpar(app.ActiveUIDocument.Document);
                if (n == 0) TaskDialog.Show("MetroSP", "Não há nada pintado pela conferência neste modelo.");
            });
            fechar.Click += (s, e) => Close();
            _exportarIfc.Click += (s, e) => ExportarIfc();
            Closed += (s, e) => _evento.Dispose();

            Theme.Apply(this, "Conferir Entrega Metrô", doc.Title, corpo, rodape);
            Mostrar(resultado);
        }

        // ================================================================== estado

        /// <summary>Mostra um resultado novo, mantendo a etapa escolhida.</summary>
        public void Mostrar(ResultadoConferencia res)
        {
            _res = res;
            _celula = null;
            _pacoteNome.Text = "Padrões: " + _pacote.NomeExibido;
            _pacoteInfo.Text = $"{res.Documento} · {res.TotalElementos} elementos que vão para o IFC ({res.Escopo}) · conferido às {res.Quando:HH:mm}";
            _pacoteInfo.ToolTip = _pacote.Arquivo;

            _resumoPct.Text = $"{Math.Floor(res.Progresso * 100):0}%";
            _resumoBarra.Value = res.Progresso;
            if (res.Vermelhas == 0 && res.Amarelas == 0)
            {
                _resumoTitulo.Text = "Pronto para exportar";
                _resumoTexto.Text = "Tudo o que o Metrô pede foi encontrado.";
            }
            else if (res.Vermelhas == 0)
            {
                _resumoTitulo.Text = "Pronto para exportar";
                _resumoTexto.Text = $"{res.Amarelas} etapa(s) com avisos que valem uma olhada.";
            }
            else
            {
                _resumoTitulo.Text = res.Progresso >= 0.9 ? "Quase pronto" : "Há pendências";
                _resumoTexto.Text = $"{res.Vermelhas} etapa(s) com pendência" + (res.Amarelas > 0 ? $" e {res.Amarelas} com aviso." : ".");
            }

            // Abre na primeira etapa vermelha, se a atual estiver ok.
            EtapaConferencia atual = res.Etapas.FirstOrDefault(e => e.Numero == _etapaAtual);
            if (atual == null || atual.Sinal == Sinal.Verde || atual.Sinal == Sinal.Cinza)
                _etapaAtual = (res.Etapas.FirstOrDefault(e => e.Sinal == Sinal.Vermelho) ?? res.Etapas.FirstOrDefault(e => e.Sinal == Sinal.Amarelo) ?? res.Etapas[0]).Numero;

            MontarCartoes();
            MontarDetalhe();
        }

        private void MontarCartoes()
        {
            _cartoes.Children.Clear();
            foreach (EtapaConferencia e in _res.Etapas) _cartoes.Children.Add(Cartao(e));

            // Etapa final: exportar.
            bool pronto = _res.ProntoParaExportar;
            var exportar = Cartao(new EtapaConferencia
            {
                Numero = 6, Titulo = "Exportar IFC",
                Resumo = pronto ? "Pode exportar com o setup e o .txt dos padrões." : "Exporta mesmo com pendências, mas avisa antes.",
                Sinal = pronto ? Sinal.Verde : Sinal.Cinza,
            }, clicavel: false);
            exportar.Cursor = Cursors.Hand;
            exportar.MouseLeftButtonUp += (s, e) => ExportarIfc();
            _cartoes.Children.Add(exportar);
        }

        private Border Cartao(EtapaConferencia etapa, bool clicavel = true)
        {
            bool sel = clicavel && etapa.Numero == _etapaAtual;
            var b = new Border
            {
                BorderBrush = Theme.Solid(sel ? Theme.Laranja : Theme.CinzaClaro),
                BorderThickness = sel ? new Thickness(3, 1, 1, 1) : new Thickness(1),
                Background = sel ? Theme.Solid(Theme.LaranjaSelecao) : Brushes.White,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = clicavel ? Cursors.Hand : null,
            };
            var dock = new DockPanel();
            var ponto = new System.Windows.Shapes.Ellipse { Width = 13, Height = 13, Fill = Ponto(etapa.Sinal), Margin = new Thickness(0, 2, 10, 0), VerticalAlignment = VerticalAlignment.Top };
            DockPanel.SetDock(ponto, Dock.Left);
            dock.Children.Add(ponto);
            var contagem = new TextBlock
            {
                Text = etapa.Sinal == Sinal.Cinza ? "—" : etapa.Sinal == Sinal.Verde ? "OK" : etapa.Contagem.ToString("N0"),
                FontWeight = FontWeights.Bold, Foreground = etapa.Sinal == Sinal.Cinza ? CinzaTexto : Ponto(etapa.Sinal),
                Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top,
            };
            if (etapa.Sinal == Sinal.Amarelo) contagem.Foreground = AmareloTexto;
            DockPanel.SetDock(contagem, Dock.Right);
            dock.Children.Add(contagem);
            var textos = new StackPanel();
            textos.Children.Add(new TextBlock { Text = $"{etapa.Numero}. {etapa.Titulo}", FontWeight = FontWeights.SemiBold, FontSize = 13 });
            textos.Children.Add(new TextBlock { Text = etapa.Resumo, Foreground = Theme.Solid(Theme.Cinza), FontSize = 11, TextWrapping = TextWrapping.Wrap });
            dock.Children.Add(textos);
            b.Child = dock;
            if (clicavel)
            {
                b.MouseLeftButtonUp += (s, e) =>
                {
                    _etapaAtual = etapa.Numero;
                    _celula = null;
                    MontarCartoes();
                    MontarDetalhe();
                };
            }
            return b;
        }

        // ================================================================== detalhe da etapa

        private void MontarDetalhe()
        {
            EtapaConferencia etapa = _res.Etapas.First(e => e.Numero == _etapaAtual);
            var dock = new DockPanel();

            var cabecalho = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            cabecalho.Children.Add(new TextBlock { Text = $"{etapa.Numero}. {etapa.Titulo}", FontSize = 16, FontWeight = FontWeights.SemiBold });
            cabecalho.Children.Add(new TextBlock { Text = etapa.Descricao, Foreground = Theme.Solid(Theme.Cinza), TextWrapping = TextWrapping.Wrap });
            DockPanel.SetDock(cabecalho, Dock.Top);
            dock.Children.Add(cabecalho);

            if (etapa.Numero == 4 && _res.Matriz.Count > 0)
            {
                var abas = new TabControl();
                abas.Items.Add(new TabItem { Header = "Matriz", Content = MontarMatriz() });
                abas.Items.Add(new TabItem { Header = $"Lista ({etapa.Pendencias.Count})", Content = MontarLista(etapa) });
                dock.Children.Add(abas);
            }
            else
            {
                dock.Children.Add(MontarLista(etapa));
            }
            _detalhe.Content = dock;
        }

        /// <summary>Tabela das pendências da etapa, com selecionar, pintar e a ferramenta que corrige.</summary>
        private UIElement MontarLista(EtapaConferencia etapa)
        {
            var dock = new DockPanel();
            List<Pendencia> itens = etapa.Pendencias.OrderBy(p => Ordem(p.Sinal)).ToList();

            var tabela = new FilterableGrid(itens, typeof(Pendencia));
            tabela.AddColumn("Situação", nameof(Pendencia.Situacao), 80);
            if (etapa.Numero != 1) tabela.AddColumn("Categoria", nameof(Pendencia.Categoria), 130);
            tabela.AddColumn(etapa.Numero == 1 ? "Arquivo" : etapa.Numero == 5 ? "Situação da classificação" : etapa.Numero == 3 ? "Classe IFC" : "Campo / parâmetro",
                nameof(Pendencia.Assunto), 170, wrap: true);
            tabela.AddColumn("O que foi encontrado", nameof(Pendencia.Problema), 280, wrap: true);
            if (etapa.Numero != 1) tabela.AddColumn("Elem.", nameof(Pendencia.QtdElementos), 55);
            tabela.AddColumn("O que fazer", nameof(Pendencia.OQueFazer), 240, wrap: true, fill: true);
            DataGrid grid = tabela.Grid;
            Theme.StyleSelection(grid);
            grid.LoadingRow += (s, e) =>
            {
                var p = (Pendencia)e.Row.Item;
                e.Row.Background = p.Sinal == Sinal.Verde ? Brushes.White : Fundo(p.Sinal);
            };

            List<ElementId> Escolhidos() =>
                (grid.SelectedItems.Count > 0 ? grid.SelectedItems.Cast<Pendencia>() : tabela.VisibleItems<Pendencia>())
                .SelectMany(p => p.Elementos).Distinct().ToList();

            var botoes = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            if (etapa.Numero != 1)
            {
                botoes.Children.Add(BotaoSelecionar(Escolhidos));
                botoes.Children.Add(BotaoPintar(Escolhidos));
            }
            foreach (Correcao c in itens.Select(p => p.Correcao).Append(etapa.Correcao).Where(c => c != Correcao.Nenhuma).Distinct())
            {
                Button b = BotaoCorrecao(c, () => grid.SelectedItems.Count > 0 ? Escolhidos() : null);
                if (b != null) botoes.Children.Add(b);
            }
            if (etapa.Numero == 2 && _res.Ids != null && !itens.Any(p => p.Correcao == Correcao.DetalhesIds))
                botoes.Children.Add(BotaoCorrecao(Correcao.DetalhesIds, () => null));
            grid.MouseDoubleClick += (s, e) =>
            {
                if (grid.SelectedItem is Pendencia p && p.Elementos.Count > 0) Selecionar(p.Elementos);
            };

            DockPanel.SetDock(botoes, Dock.Bottom);
            dock.Children.Add(botoes);
            if (itens.Count == 0)
            {
                dock.Children.Add(new TextBlock { Text = "Nada a corrigir nesta etapa.", Foreground = VerdeTexto, FontSize = 13, Margin = new Thickness(0, 10, 0, 0) });
                return dock;
            }
            dock.Children.Add(tabela);
            return dock;
        }

        private static int Ordem(Sinal s) => s == Sinal.Vermelho ? 0 : s == Sinal.Amarelo ? 1 : s == Sinal.Cinza ? 2 : 3;

        /// <summary>Matriz categoria × campo, com o detalhe da célula escolhida embaixo.</summary>
        private UIElement MontarMatriz()
        {
            var dock = new DockPanel();

            var opcoes = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var so = new CheckBox { Content = "Só linhas e colunas com pendência", IsChecked = _soPendencias, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(so, Dock.Left);
            opcoes.Children.Add(so);
            opcoes.Children.Add(new TextBlock
            {
                Text = "Clique numa célula para ver os tipos e elementos. % = elementos com o campo ok.",
                Foreground = Theme.Solid(Theme.Cinza), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            });
            DockPanel.SetDock(opcoes, Dock.Top);
            dock.Children.Add(opcoes);

            var detalhe = new ContentControl { Height = 230, Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(detalhe, Dock.Bottom);
            dock.Children.Add(detalhe);

            var rolagem = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            dock.Children.Add(new Border { BorderBrush = Theme.Solid(Theme.CinzaClaro), BorderThickness = new Thickness(1), Child = rolagem });

            Border selecionada = null;
            void Montar()
            {
                List<string> campos = _res.Campos.Where(c => !_soPendencias || _res.Categorias.Any(k => Celula(k.Categoria, c)?.TemPendencia == true)).ToList();
                var categorias = _res.Categorias.Where(k => !_soPendencias || _res.Campos.Any(c => Celula(k.Categoria, c)?.TemPendencia == true)).ToList();

                var g = new WGrid { Background = Brushes.White };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
                foreach (string _ in campos) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                foreach (var _ in categorias) g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });

                void Pos(UIElement el, int lin, int col) { WGrid.SetRow(el, lin); WGrid.SetColumn(el, col); g.Children.Add(el); }

                Pos(Cabecalho("Categoria (classe IFC)", false), 0, 0);
                Pos(Cabecalho("Elem.", false), 0, 1);
                for (int j = 0; j < campos.Count; j++)
                {
                    string campo = campos[j];
                    int corte = campo.IndexOf('.');
                    var h = Cabecalho(corte > 0 ? campo.Substring(corte + 1) : campo, true);
                    h.ToolTip = campo;
                    Pos(h, 0, j + 2);
                }

                for (int i = 0; i < categorias.Count; i++)
                {
                    var k = categorias[i];
                    var nome = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0), TextTrimming = TextTrimming.CharacterEllipsis };
                    nome.Inlines.Add(new System.Windows.Documents.Run(k.Categoria) { FontWeight = FontWeights.SemiBold });
                    nome.Inlines.Add(new System.Windows.Documents.Run($"  {k.Classe}") { Foreground = Theme.Solid(Theme.Cinza) });
                    nome.ToolTip = $"{k.Categoria} ({k.Classe})";
                    Pos(new Border { BorderBrush = Theme.Solid(Theme.CinzaClaro), BorderThickness = new Thickness(0, 0, 0, 1), Child = nome }, i + 1, 0);
                    Pos(new Border
                    {
                        BorderBrush = Theme.Solid(Theme.CinzaClaro), BorderThickness = new Thickness(0, 0, 0, 1),
                        Child = new TextBlock { Text = k.Elementos.ToString("N0"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) },
                    }, i + 1, 1);

                    for (int j = 0; j < campos.Count; j++)
                    {
                        CelulaMatriz c = Celula(k.Categoria, campos[j]);
                        var cel = new Border
                        {
                            Margin = new Thickness(3, 3, 3, 3),
                            CornerRadius = new CornerRadius(3),
                            Background = c == null ? Brushes.Transparent : Fundo(c.Sinal),
                            BorderBrush = Brushes.Transparent,
                            BorderThickness = new Thickness(2),
                            Child = new TextBlock
                            {
                                Text = c == null ? "" : c.Texto,
                                FontWeight = FontWeights.SemiBold, FontSize = 11,
                                Foreground = c == null ? Brushes.Transparent : Texto(c.Sinal),
                                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                            },
                        };
                        if (c != null)
                        {
                            cel.ToolTip = DicaCelula(c);
                            cel.Cursor = Cursors.Hand;
                            if (c == _celula) { cel.BorderBrush = Theme.Solid(Theme.Preto); selecionada = cel; }
                            cel.MouseLeftButtonUp += (s, e) =>
                            {
                                if (selecionada != null) selecionada.BorderBrush = Brushes.Transparent;
                                selecionada = cel;
                                cel.BorderBrush = Theme.Solid(Theme.Preto);
                                _celula = c;
                                detalhe.Content = DetalheCelula(c);
                            };
                        }
                        Pos(cel, i + 1, j + 2);
                    }
                }

                if (categorias.Count == 0)
                {
                    g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var ok = new TextBlock { Text = "Todos os campos exigidos estão preenchidos.", Foreground = VerdeTexto, Margin = new Thickness(8), FontSize = 13 };
                    WGrid.SetRow(ok, 1);
                    WGrid.SetColumnSpan(ok, 2);
                    g.Children.Add(ok);
                }
                rolagem.Content = g;
            }

            so.Click += (s, e) => { _soPendencias = so.IsChecked == true; Montar(); };
            Montar();

            // Sem célula escolhida: a pior célula já aparece aberta.
            if (_celula == null)
                _celula = _res.Matriz.Values.Where(c => c.TemPendencia && !c.SemMapeamento).OrderBy(c => c.Percentual).FirstOrDefault();
            detalhe.Content = _celula != null ? DetalheCelula(_celula)
                : new TextBlock { Text = "Clique numa célula para ver os elementos.", Foreground = Theme.Solid(Theme.Cinza), Margin = new Thickness(4) };
            if (_celula != null) Montar(); // destaca a célula aberta
            return dock;
        }

        private CelulaMatriz Celula(string categoria, string campo) =>
            _res.Matriz.TryGetValue((categoria, campo), out CelulaMatriz c) ? c : null;

        private static string DicaCelula(CelulaMatriz c)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{c.Categoria} · {c.Campo}");
            if (c.SemMapeamento) { sb.Append("Nenhuma linha do .txt de property sets gera este campo."); return sb.ToString(); }
            sb.AppendLine($"Parâmetro: {c.Parametro} ({(c.NoTipo ? "tipo" : "instância")})");
            if (c.NaoConferido != null) { sb.Append(c.NaoConferido); return sb.ToString(); }
            sb.Append($"{c.Ok} de {c.Total} elementos ok");
            if (c.ValorExigido != null) sb.Append($"\nValores aceitos: {c.ValorExigido}");
            return sb.ToString();
        }

        private UIElement DetalheCelula(CelulaMatriz c)
        {
            var borda = new Border { BorderBrush = Theme.Solid(Theme.CinzaClaro), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) };
            var dock = new DockPanel();

            var topo = new DockPanel { Background = Fundo(c.Sinal == Sinal.Verde ? Sinal.Verde : c.Sinal), LastChildFill = true };
            var titulo = new TextBlock { Margin = new Thickness(8, 6, 8, 6), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            titulo.Inlines.Add(new System.Windows.Documents.Run($"{c.Categoria} · {c.Campo}") { FontWeight = FontWeights.SemiBold });
            string desc = c.SemMapeamento ? "  sem mapeamento no .txt de property sets"
                : c.NaoConferido != null ? "  " + c.NaoConferido
                : $"  {c.Ok} de {c.Total} ok · parâmetro \"{c.Parametro}\" de {(c.NoTipo ? "tipo" : "instância")}" + (c.ValorExigido != null ? $" · aceitos: {c.ValorExigido}" : "");
            titulo.Inlines.Add(new System.Windows.Documents.Run(desc) { Foreground = Texto(c.Sinal) });
            DockPanel.SetDock(topo, Dock.Top);

            var linhas = c.Pendentes
                .GroupBy(p => (p.Problema, p.Tipo))
                .Select(g => new LinhaDetalhe
                {
                    Problema = g.Key.Problema, Tipo = g.Key.Tipo, Elementos = g.Count(),
                    Valor = string.Join(", ", g.Select(p => p.Valor).Where(v => v != null).Distinct().Take(3)),
                    Ids = g.Select(p => p.Id).ToList(),
                })
                .OrderBy(l => l.Problema == ConferenciaEntrega.ProblemaSemParametro ? 0 : l.Problema == ConferenciaEntrega.ProblemaVazio ? 1 : 2)
                .ThenByDescending(l => l.Elementos)
                .ToList();

            var grid = new DataGrid
            {
                ItemsSource = linhas, AutoGenerateColumns = false, IsReadOnly = true, HeadersVisibility = DataGridHeadersVisibility.Column,
                CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Extended, BorderThickness = new Thickness(0),
            };
            grid.Columns.Add(new DataGridTextColumn { Header = "Problema", Binding = new System.Windows.Data.Binding(nameof(LinhaDetalhe.Problema)), Width = 110 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Família: tipo", Binding = new System.Windows.Data.Binding(nameof(LinhaDetalhe.Tipo)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Elementos", Binding = new System.Windows.Data.Binding(nameof(LinhaDetalhe.Elementos)), Width = 75 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Valor encontrado", Binding = new System.Windows.Data.Binding(nameof(LinhaDetalhe.Valor)), Width = 170 });
            Theme.StyleSelection(grid);
            grid.MouseDoubleClick += (s, e) => { if (grid.SelectedItem is LinhaDetalhe l) Selecionar(l.Ids); };

            List<ElementId> Escolhidos() =>
                (grid.SelectedItems.Count > 0 ? grid.SelectedItems.Cast<LinhaDetalhe>() : linhas).SelectMany(l => l.Ids).Distinct().ToList();

            var botoes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            if (linhas.Count > 0)
            {
                botoes.Children.Add(BotaoSelecionar(Escolhidos));
                botoes.Children.Add(BotaoPintar(Escolhidos));
            }
            if (c.Pendentes.Any(p => p.Problema == ConferenciaEntrega.ProblemaSemParametro))
                botoes.Children.Add(BotaoCorrecao(Correcao.ParametrosDeProjeto, () => null));
            if (c.SemMapeamento && _res.Ids != null)
                botoes.Children.Add(BotaoCorrecao(Correcao.DetalhesIds, () => null));
            DockPanel.SetDock(botoes, Dock.Right);
            topo.Children.Add(botoes);
            topo.Children.Add(titulo);
            dock.Children.Add(topo);

            if (linhas.Count > 0) dock.Children.Add(grid);
            else dock.Children.Add(new TextBlock
            {
                Text = c.SemMapeamento ? "Inclua a propriedade no .txt de property sets, com o parâmetro do Revit de onde ela sai."
                     : c.NaoConferido != null ? c.NaoConferido : "Todos os elementos têm o campo ok.",
                Margin = new Thickness(8), Foreground = Theme.Solid(Theme.Cinza), TextWrapping = TextWrapping.Wrap,
            });
            borda.Child = dock;
            return borda;
        }

        private static Border Cabecalho(string texto, bool vertical)
        {
            var t = new TextBlock { Text = texto, FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 4, 6, 4), VerticalAlignment = VerticalAlignment.Bottom };
            if (vertical)
            {
                t.LayoutTransform = new RotateTransform(-90);
                t.HorizontalAlignment = HorizontalAlignment.Center;
                t.MaxWidth = 190;
                t.TextTrimming = TextTrimming.CharacterEllipsis;
            }
            return new Border
            {
                Background = Theme.Solid(Theme.CinzaFundo),
                BorderBrush = Theme.Solid(Theme.Preto),
                BorderThickness = new Thickness(0, 0, 0, 2),
                Child = t,
            };
        }

        // ================================================================== botões

        private Button BotaoSelecionar(Func<List<ElementId>> ids)
        {
            var b = new Button { Content = "Selecionar no Revit", Margin = new Thickness(0, 0, 6, 0), ToolTip = "Seleciona os elementos (as linhas escolhidas ou, sem escolha, todas) e mostra no modelo." };
            b.Click += (s, e) => Selecionar(ids());
            return b;
        }

        private Button BotaoPintar(Func<List<ElementId>> ids)
        {
            var b = new Button { Content = "Pintar na vista", Margin = new Thickness(0, 0, 6, 0), ToolTip = "Pinta de vermelho, na vista ativa, os elementos com pendência. \"Limpar pintura\" desfaz." };
            b.Click += (s, e) =>
            {
                List<ElementId> lista = ids();
                if (lista.Count == 0) return;
                Revit(app =>
                {
                    int n = PinturaVista.Pintar(app.ActiveUIDocument, lista, PinturaVista.Vermelho);
                    if (n < 0) TaskDialog.Show("MetroSP", "A vista ativa não aceita sobrepor gráficos. Abra uma vista 3D ou de planta e tente de novo.");
                });
            };
            return b;
        }

        private Button BotaoCorrecao(Correcao c, Func<List<ElementId>> selecionarAntes)
        {
            string texto, dica;
            switch (c)
            {
                case Correcao.EditarPacote: texto = "Editar os padrões..."; dica = "Escolher os arquivos do Metrô."; break;
                case Correcao.ParametrosDeProjeto: texto = "Parâmetros de projeto..."; dica = "Abre a janela Parâmetros de projeto do Revit para vincular o parâmetro às categorias. Depois clique em Conferir de novo."; break;
                case Correcao.ListarCompartilhados: texto = "Listar Compartilhados..."; dica = "Abre a lista de parâmetros compartilhados com a coluna Padrão (MetroShared)."; break;
                case Correcao.AplicarClassificacao: texto = "Aplicar Classificação..."; dica = "Seleciona os elementos e abre o Aplicar Classificação com a planilha dos padrões."; break;
                case Correcao.VerificarClassificacao: texto = "Verificar Classificação..."; dica = "Abre a Verificar Classificação com a planilha dos padrões: ela propõe as correções."; break;
                case Correcao.DetalhesIds: texto = "Detalhes dos padrões (IDS)..."; dica = "Abre o resultado completo do Verificar Padrões Metrô."; break;
                default: return null;
            }
            var b = new Button { Content = texto, ToolTip = dica, Margin = new Thickness(0, 0, 6, 0) };
            b.Click += (s, e) => Corrigir(c, selecionarAntes?.Invoke());
            return b;
        }

        // ================================================================== ações

        /// <summary>Roda uma ação no contexto da API, conferindo se o modelo ativo é o da conferência.</summary>
        private void Revit(Action<UIApplication> acao)
        {
            _acoes.Enfileirar(app =>
            {
                Document ativo = app.ActiveUIDocument?.Document;
                if (!_doc.IsValidObject)
                {
                    TaskDialog.Show("MetroSP", "O modelo desta conferência foi fechado.");
                    return;
                }
                if (ativo == null || !ativo.Equals(_doc))
                {
                    TaskDialog.Show("MetroSP", $"Esta conferência é do modelo \"{_doc.Title}\". Ative a janela dele no Revit e tente de novo.");
                    return;
                }
                acao(app);
            });
            _evento.Raise();
        }

        private void Selecionar(List<ElementId> ids)
        {
            if (ids == null || ids.Count == 0) return;
            Revit(app =>
            {
                UIDocument uidoc = app.ActiveUIDocument;
                List<ElementId> validos = ids.Where(id => _doc.GetElement(id) != null).ToList();
                uidoc.Selection.SetElementIds(validos);
                try { uidoc.ShowElements(validos); }
                catch { /* nenhum visível nas vistas abertas: fica só a seleção */ }
            });
        }

        public void Conferir()
        {
            Revit(app => Mostrar(Executar(app, _doc, _pacote)));
        }

        /// <summary>Roda a conferência com a janela de progresso. Chamar no contexto da API.</summary>
        public static ResultadoConferencia Executar(UIApplication app, Document doc, PacoteMetro pacote)
        {
            ElementId vista = VistaDoEscopo(app, pacote, out string aviso);
            ResultadoConferencia r;
            using (Progresso.Abrir("Conferir Entrega Metrô"))
                r = new ConferenciaEntrega(doc, pacote, vista).Executar();
            if (aviso != null) r.Escopo = aviso;
            return r;
        }

        /// <summary>Vista ativa, se o escopo pede e ela mostra elementos de modelo (3D, planta ou corte); senão null.</summary>
        private static ElementId VistaDoEscopo(UIApplication app, PacoteMetro pacote, out string aviso)
        {
            aviso = null;
            if (!pacote.SoVistaAtiva) return null;
            View v = app.ActiveUIDocument?.ActiveView;
            if (v is View3D || v is ViewPlan || v is ViewSection) return v.Id;
            aviso = "modelo inteiro: a vista ativa não é 3D, planta ou corte";
            return null;
        }

        private void EditarPacote()
        {
            var janela = new PacoteWindow(_pacote, _pacote.Arquivo != null ? Path.GetDirectoryName(_pacote.Arquivo) : null) { Owner = this };
            if (janela.ShowDialog() != true) return;
            _pacote = janela.Pacote;
            _pacote.SalvarComoAtual();
            _pacote.AplicarNasOutrasFerramentas();
            Conferir();
        }

        private void Corrigir(Correcao c, List<ElementId> selecionarAntes)
        {
            if (c == Correcao.EditarPacote) { EditarPacote(); return; }
            _pacote.AplicarNasOutrasFerramentas();
            Revit(app =>
            {
                switch (c)
                {
                    case Correcao.ParametrosDeProjeto:
                        app.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.ProjectParameters));
                        break;
                    case Correcao.ListarCompartilhados:
                        ListarCompartilhadosCommand.Abrir(app);
                        Mostrar(Executar(app, _doc, _pacote));
                        break;
                    case Correcao.AplicarClassificacao:
                        if (selecionarAntes != null && selecionarAntes.Count > 0)
                            app.ActiveUIDocument.Selection.SetElementIds(selecionarAntes.Where(id => _doc.GetElement(id) != null).ToList());
                        AplicarClassificacaoCommand.Abrir(app);
                        break;
                    case Correcao.VerificarClassificacao:
                        VerificarClassificacaoCommand.Abrir(app);
                        Mostrar(Executar(app, _doc, _pacote));
                        break;
                    case Correcao.DetalhesIds:
                        (IdsDocument ids, List<CheckResult> resultados) d;
                        ElementId vista = VistaDoEscopo(app, _pacote, out _);
                        using (Progresso.Abrir("Verificar Padrões Metrô"))
                            d = ConferenciaEntrega.DetalhesIds(_doc, _pacote, vista);
                        if (d.ids == null) { TaskDialog.Show("MetroSP", "Os padrões não têm IDS."); break; }
                        var janela = new ResultadosIdsWindow(d.ids, d.resultados);
                        new WindowInteropHelper(janela).Owner = app.MainWindowHandle;
                        janela.Show();
                        break;
                }
            });
        }

        private void ExportarIfc()
        {
            if (_res != null && !_res.ProntoParaExportar)
            {
                string etapas = string.Join(", ", _res.Etapas.Where(e => e.Sinal == Sinal.Vermelho).Select(e => e.Titulo));
                if (MessageBox.Show(this, $"Ainda há pendências em: {etapas}.\n\nExportar o IFC mesmo assim?", "MetroSP",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                    return;
            }
            _pacote.AplicarNasOutrasFerramentas();
            Revit(app => ExportarIfcCommand.Exportar(app));
        }

        private void ExportarCsv()
        {
            if (_res == null) return;
            var d = new Microsoft.Win32.SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = $"conferencia_metro_{Limpar(_res.Documento)}.csv" };
            if (d.ShowDialog(this) != true) return;

            var sb = new StringBuilder();
            sb.AppendLine($"\"Conferir Entrega Metrô\";\"{_res.Documento}\";\"{_pacote.NomeExibido}\";\"{_res.Quando:dd/MM/yyyy HH:mm}\";\"{_res.Escopo}\"");
            sb.AppendLine("Etapa;Situação;Categoria;Assunto;O que foi encontrado;Elementos;O que fazer;IDs");
            foreach (EtapaConferencia e in _res.Etapas)
            {
                foreach (Pendencia p in e.Pendencias.OrderBy(p => Ordem(p.Sinal)))
                {
                    sb.AppendLine(string.Join(";", new[]
                    {
                        $"{e.Numero}. {e.Titulo}", p.Situacao, p.Categoria, p.Assunto, p.Problema, p.QtdElementos.ToString(), p.OQueFazer,
                        string.Join(",", p.Elementos.Select(id => id.Value.ToString())),
                    }.Select(Csv)));
                }
            }
            try { File.WriteAllText(d.FileName, sb.ToString(), new UTF8Encoding(true)); }
            catch (Exception ex) { MessageBox.Show(this, "Não deu para salvar:\n" + ex.Message, "MetroSP"); }
        }

        private static string Csv(string v) => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";

        private static string Limpar(string nome)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) nome = nome.Replace(c, '-');
            return nome;
        }

        /// <summary>Fila de ações executadas pelo Revit quando o ExternalEvent dispara.</summary>
        private class AcoesRevit : IExternalEventHandler
        {
            private readonly Queue<Action<UIApplication>> _fila = new Queue<Action<UIApplication>>();

            public void Enfileirar(Action<UIApplication> a) { lock (_fila) _fila.Enqueue(a); }

            public void Execute(UIApplication app)
            {
                while (true)
                {
                    Action<UIApplication> a;
                    lock (_fila)
                    {
                        if (_fila.Count == 0) return;
                        a = _fila.Dequeue();
                    }
                    try { a(app); }
                    catch (Exception ex) { TaskDialog.Show("MetroSP", "Erro:\n" + ex.Message); }
                }
            }

            public string GetName() => "MetroSP: conferir entrega";
        }
    }
}
