using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Autodesk.Revit.UI;
using MetroSP.Commands;
using MetroSP.Conferencia;
using MetroSP.Ui;
using DB = Autodesk.Revit.DB;

namespace MetroSP.Painel
{
    /// <summary>
    /// Janela única da entrega: as etapas em cima (linha com setinhas) e, embaixo, as pendências da etapa escolhida
    /// (à esquerda, com os botões que resolvem) e a ferramenta da etapa embutida (à direita). Abre modal, no contexto
    /// da API, para que as ferramentas embutidas possam gravar no modelo.
    /// </summary>
    public class EntregaWindow : Window
    {
        private const int PassoLocal = 6;
        private static readonly Color Vermelho = Color.FromRgb(0xB2, 0x22, 0x22);

        private readonly UIApplication _app;
        private readonly DB.Document _doc;
        private PacoteMetro _pacote;
        private ResultadoConferencia _res;
        private (Sinal Sinal, string Texto) _local;
        private int _passo;

        private readonly Grid _etapas;
        private readonly TextBlock _tituloPendencias;
        private readonly StackPanel _pendencias;
        private readonly Border _ferramenta;
        private readonly TextBlock _rodapeTexto;
        private readonly Button _anterior;
        private readonly Button _perfil;
        private readonly Button _proximo;
        private readonly TextBlock _escopo;

        private Window _janelaFerramenta;
        private bool _trocando;

        /// <summary>Confere o modelo ativo e abre a janela. Chamar no contexto da API (comando ou ExternalEvent).</summary>
        public static Result Abrir(UIApplication app)
        {
            DB.Document doc = app.ActiveUIDocument?.Document;
            if (doc == null || doc.IsFamilyDocument)
            {
                TaskDialog.Show("MetroSP", "Abra um projeto (.rvt) para preparar a entrega.");
                return Result.Cancelled;
            }
            var janela = new EntregaWindow(app, doc);
            if (!janela.Conferir(primeira: true)) return Result.Cancelled;
            new WindowInteropHelper(janela).Owner = app.MainWindowHandle;
            janela.ShowDialog();
            Painel.PainelMetroSP.Instancia?.ModeloAtivo(doc);
            return Result.Succeeded;
        }

        private EntregaWindow(UIApplication app, DB.Document doc)
        {
            _app = app;
            _doc = doc;
            Title = "MetroSP · Entrega · " + doc.Title;
            Width = 1240;
            Height = 780;
            MinWidth = 900;
            MinHeight = 560;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brushes.White;
            Theme.Estilos(this);

            // ---- faixa preta
            var marca = new TextBlock { Foreground = Brushes.White, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            marca.Inlines.Add(new System.Windows.Documents.Run("MetroSP") { Foreground = Theme.Solid(Theme.Laranja), FontWeight = FontWeights.SemiBold });
            marca.Inlines.Add(new System.Windows.Documents.Run(" · Entrega · " + doc.Title));
            _escopo = new TextBlock { Foreground = Theme.Solid(Color.FromRgb(0xBB, 0xBB, 0xBB)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            var trocar = BotaoEscuro("Padrões...", "Escolher os padrões do Metrô (arquivo .metrosp ou os arquivos um a um).");
            trocar.Click += (s, e) => EscolherPacote(true);
            var conferir = BotaoEscuro("⟳ Conferir", "Confere o modelo de novo e atualiza as etapas.");
            conferir.Click += (s, e) => { if (FecharFerramenta()) Conferir(); };
            _perfil = BotaoEscuro("", "Perfil do arquivo de coordenação em uso neste modelo. Clique para trocar (fica gravado no modelo).");
            _perfil.Click += (s, e) => Coordenacao.MenuPerfis.Abrir(_perfil, _doc, CoordenacaoMudou);
            var direita = new StackPanel { Orientation = Orientation.Horizontal };
            direita.Children.Add(_perfil);
            direita.Children.Add(_escopo);
            direita.Children.Add(trocar);
            direita.Children.Add(conferir);
            var faixa = new DockPanel { Background = Theme.Solid(Theme.Preto) };
            DockPanel.SetDock(direita, Dock.Right);
            faixa.Children.Add(direita);
            faixa.Children.Add(new Border { Padding = new Thickness(14, 8, 14, 8), Child = marca });

            // ---- etapas
            _etapas = new Grid { Margin = new Thickness(28, 18, 28, 16) };
            var etapasBorda = new Border { BorderBrush = Theme.Solid(Color.FromRgb(0xEE, 0xEE, 0xEE)), BorderThickness = new Thickness(0, 0, 0, 1), Child = _etapas };

            // ---- pendências (esquerda)
            _tituloPendencias = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) };
            _pendencias = new StackPanel();
            var esquerdaConteudo = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };
            esquerdaConteudo.Children.Add(_tituloPendencias);
            esquerdaConteudo.Children.Add(_pendencias);
            var esquerda = new Border
            {
                Width = 360,
                Background = Theme.Solid(Color.FromRgb(0xFC, 0xFC, 0xFC)),
                BorderBrush = Theme.Solid(Color.FromRgb(0xEE, 0xEE, 0xEE)),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = esquerdaConteudo },
            };

            // ---- ferramenta (direita)
            _ferramenta = new Border { Padding = new Thickness(14, 10, 14, 6) };

            // ---- rodapé
            _anterior = Pilula("‹ Passo anterior");
            _anterior.Click += (s, e) => IrPara(_passo - 1);
            _proximo = Pilula("Próximo passo ›");
            _proximo.Click += (s, e) => IrPara(_passo + 1);
            var salvar = Pilula("Salvar e conferir", primario: true);
            salvar.ToolTip = "Grava o que foi alterado na ferramenta desta etapa e confere o modelo de novo.";
            salvar.Margin = new Thickness(0, 0, 8, 0);
            salvar.Click += (s, e) => SalvarEConferir();
            _rodapeTexto = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(12, 0, 12, 0) };
            var direitaRodape = new StackPanel { Orientation = Orientation.Horizontal };
            direitaRodape.Children.Add(salvar);
            direitaRodape.Children.Add(_proximo);
            // Arquivo de coordenação: levar tudo como está para outra pessoa, ou usar o que alguém mandou.
            var exportarAlteracoes = Pilula("⇪ Exportar alterações...");
            exportarAlteracoes.ToolTip = "Grava tudo como está agora (setup e fase, categorias IFC, classes por família, property sets, mapeamento " +
                                         "e Projeto e Local) num arquivo .coord-metrosp, com um ou mais perfis, para outra pessoa abrir e usar.";
            exportarAlteracoes.Margin = new Thickness(8, 0, 0, 0);
            exportarAlteracoes.Click += (s, e) => { if (FecharFerramenta() && Coordenacao.ExportarAlteracoesWindow.Abrir(this, _doc)) CoordenacaoMudou(); };
            var abrirAlteracoes = Pilula("Abrir alterações...");
            abrirAlteracoes.ToolTip = "Abre o arquivo .coord-metrosp que o coordenador BIM enviou e escolhe o perfil: o Exportar IFC passa a usar tudo o que veio nele.";
            abrirAlteracoes.Margin = new Thickness(8, 0, 0, 0);
            abrirAlteracoes.Click += (s, e) => { if (FecharFerramenta() && Coordenacao.AbrirAlteracoesWindow.Abrir(this, _doc)) CoordenacaoMudou(); };
            var esquerdaRodape = new StackPanel { Orientation = Orientation.Horizontal };
            esquerdaRodape.Children.Add(_anterior);
            esquerdaRodape.Children.Add(exportarAlteracoes);
            esquerdaRodape.Children.Add(abrirAlteracoes);
            var rodape = new DockPanel { Margin = new Thickness(16, 10, 16, 10) };
            DockPanel.SetDock(esquerdaRodape, Dock.Left);
            DockPanel.SetDock(direitaRodape, Dock.Right);
            rodape.Children.Add(esquerdaRodape);
            rodape.Children.Add(direitaRodape);
            rodape.Children.Add(_rodapeTexto);
            var rodapeBorda = new Border
            {
                Background = Theme.Solid(Theme.CinzaFundo),
                BorderBrush = Theme.Solid(Theme.CinzaClaro),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = rodape,
            };

            var corpo = new DockPanel();
            DockPanel.SetDock(esquerda, Dock.Left);
            corpo.Children.Add(esquerda);
            corpo.Children.Add(_ferramenta);

            var raiz = new DockPanel();
            DockPanel.SetDock(faixa, Dock.Top);
            DockPanel.SetDock(etapasBorda, Dock.Top);
            DockPanel.SetDock(rodapeBorda, Dock.Bottom);
            raiz.Children.Add(faixa);
            raiz.Children.Add(etapasBorda);
            raiz.Children.Add(rodapeBorda);
            raiz.Children.Add(corpo);
            Content = raiz;

            // A janela da ferramenta embutida fica escondida atrás desta: os diálogos dela abrem sobre esta janela.
            LocationChanged += (s, e) => AcompanharFerramenta();
            SizeChanged += (s, e) => AcompanharFerramenta();
            Closing += (s, e) => { if (!FecharFerramenta()) e.Cancel = true; };
            Loaded += (s, e) => { if (_res != null) MostrarFerramenta(); };
        }

        // ================================================================== arquivo de coordenação

        /// <summary>Mostra o perfil em uso na faixa preta (só com arquivo de coordenação de mais de um perfil, ou para dizer a revisão).</summary>
        private void MostrarPerfil()
        {
            Coordenacao.CoordenacaoRevit.Ativa c = Coordenacao.CoordenacaoRevit.De(_doc);
            _perfil.Visibility = c == null ? Visibility.Collapsed : Visibility.Visible;
            if (c == null) return;
            _perfil.Content = $"Coordenação rev. {c.Arquivo.Revisao} · {c.Perfil.Nome}" + (c.Arquivo.Perfis.Count > 1 ? " ▾" : "");
            _perfil.IsHitTestVisible = c.Arquivo.Perfis.Count > 1;
        }

        /// <summary>A coordenação mudou (abriu, exportou ou trocou o perfil): os arquivos da classe mudam, então confere de novo.</summary>
        private void CoordenacaoMudou()
        {
            MostrarPerfil();
            Conferir();
        }

        // ================================================================== conferir

        private bool Conferir(bool primeira = false)
        {
            if (!EscolherPacote(false)) return false;
            _pacote.SalvarComoAtual();
            _pacote.AplicarNasOutrasFerramentas();
            ResultadoConferencia r;
            try
            {
                Cursor = Cursors.Wait;
                r = ConferirEntregaWindow.Executar(_app, _doc, _pacote);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("MetroSP", "Não foi possível conferir:\n" + ex.Message);
                return false;
            }
            finally { Cursor = null; }
            _res = r;
            _local = AcoesPendencia.SituacaoLocal(_doc);
            MostrarPerfil();
            if (primeira || _passo == 0)
            {
                // Começa pela primeira etapa com pendência (ou pela última, se está tudo certo).
                EtapaConferencia aberta = _res.Etapas.FirstOrDefault(e => Pendentes(e).Count > 0);
                _passo = aberta?.Numero ?? PassoLocal;
            }
            Mostrar();
            return true;
        }

        private bool EscolherPacote(bool sempre)
        {
            if (_pacote == null) _pacote = PacoteMetro.Abrir();
            bool vazio = !PacoteMetro.Existe(_pacote.PropertySets) && !PacoteMetro.Existe(_pacote.Ids);
            if (sempre || vazio)
            {
                var escolha = new PacoteWindow(_pacote, _pacote.Arquivo != null ? System.IO.Path.GetDirectoryName(_pacote.Arquivo) : null);
                if (IsLoaded) escolha.Owner = this;
                else new WindowInteropHelper(escolha).Owner = _app.MainWindowHandle;
                if (escolha.ShowDialog() != true) return !vazio;
                _pacote = escolha.Pacote;
                _pacote.SalvarComoAtual();
                _pacote.AplicarNasOutrasFerramentas();
                if (sempre && _res != null && FecharFerramenta()) Conferir();
            }
            return true;
        }

        private void SalvarEConferir()
        {
            Window w = _janelaFerramenta;
            Button padrao = w == null ? null : Achar<Button>(_ferramenta, b => b.IsDefault && b.IsEnabled);
            if (padrao != null && new ButtonAutomationPeer(padrao).GetPattern(PatternInterface.Invoke) is IInvokeProvider clique)
            {
                // O botão Salvar da ferramenta grava e fecha; ao fechar, a etapa é conferida de novo.
                clique.Invoke();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_janelaFerramenta == w && FecharFerramenta()) Conferir();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                return;
            }
            if (FecharFerramenta()) Conferir();
        }

        // ================================================================== mostrar

        private static List<Pendencia> Pendentes(EtapaConferencia e) =>
            e.Pendencias.Where(p => p.Sinal == Sinal.Vermelho || p.Sinal == Sinal.Amarelo)
                .OrderBy(p => p.Sinal == Sinal.Vermelho ? 0 : 1).ThenByDescending(p => p.QtdElementos).ToList();

        private int UltimoPasso => PassoLocal;

        private void IrPara(int passo)
        {
            if (passo < 1 || passo > UltimoPasso || passo == _passo) return;
            if (!FecharFerramenta()) return;
            _passo = passo;
            Mostrar();
        }

        private void Mostrar()
        {
            _escopo.Text = $"{_res.Escopo} · conferido às {_res.Quando:HH:mm}";
            MostrarEtapas();
            MostrarPendencias();
            if (IsLoaded) MostrarFerramenta();   // antes de abrir, espera o Loaded (a ferramenta precisa desta janela como dona)
            _anterior.Visibility = _passo > 1 ? Visibility.Visible : Visibility.Hidden;
            _proximo.Visibility = _passo < UltimoPasso ? Visibility.Visible : Visibility.Hidden;
            _rodapeTexto.Text = "Ao salvar, a etapa é conferida de novo e fica verde se estiver tudo certo.";
        }

        private static string NomeCurto(int numero, string titulo)
        {
            switch (numero)
            {
                case 1: return "Arquivos";
                case 2: return "Parâmetros";
                case 3: return "Classe IFC";
                case 4: return "Dados";
                case 5: return "Classificação";
                default: return titulo;
            }
        }

        private void MostrarEtapas()
        {
            _etapas.Children.Clear();
            _etapas.ColumnDefinitions.Clear();
            var itens = _res.Etapas.Select(e => (e.Numero, Nome: NomeCurto(e.Numero, e.Titulo), e.Sinal, Qtd: Pendentes(e).Count, Dica: e.Titulo + ": " + (e.Resumo ?? ""))).ToList();
            itens.Add((PassoLocal, "Local e exportar", _local.Sinal == Sinal.Verde ? Sinal.Verde : Sinal.Cinza, 0, "Projeto e local, e Exportar IFC: " + _local.Texto));
            for (int i = 0; i < itens.Count; i++)
            {
                if (i > 0)
                {
                    _etapas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 24 });
                    var seta = Seta();
                    Grid.SetColumn(seta, _etapas.ColumnDefinitions.Count - 1);
                    _etapas.Children.Add(seta);
                }
                _etapas.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                FrameworkElement item = Etapa(itens[i].Numero, itens[i].Nome, itens[i].Sinal, itens[i].Qtd, itens[i].Dica);
                Grid.SetColumn(item, _etapas.ColumnDefinitions.Count - 1);
                _etapas.Children.Add(item);
            }
        }

        private static FrameworkElement Seta()
        {
            var g = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0), Height = 10 };
            g.Children.Add(new Rectangle { Height = 1, Fill = Theme.Solid(Color.FromRgb(0xCC, 0xCC, 0xCC)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            g.Children.Add(new Polygon
            {
                Points = new PointCollection { new Point(0, 0), new Point(7, 5), new Point(0, 10) },
                Fill = Theme.Solid(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                HorizontalAlignment = HorizontalAlignment.Right,
            });
            return g;
        }

        private FrameworkElement Etapa(int numero, string nome, Sinal sinal, int qtd, string dica)
        {
            bool atual = numero == _passo;
            bool ok = sinal == Sinal.Verde && qtd == 0;
            Color borda = ok ? Theme.Verde : atual || qtd > 0 ? Theme.Laranja : Color.FromRgb(0xCC, 0xCC, 0xCC);
            Brush fundo = atual ? Theme.Solid(Theme.Laranja) : ok ? Theme.Solid(Theme.Verde) : Brushes.White;
            Brush letra = atual ? Theme.Solid(Theme.Preto) : ok ? Brushes.White : Theme.Solid(qtd > 0 ? Theme.LaranjaTexto : Theme.Cinza);
            var bola = new Grid { Width = 26, Height = 26, VerticalAlignment = VerticalAlignment.Center };
            bola.Children.Add(new Ellipse { Fill = fundo, Stroke = Theme.Solid(borda), StrokeThickness = 2 });
            bola.Children.Add(new TextBlock
            {
                Text = ok && !atual ? "✓" : numero.ToString(),
                Foreground = letra,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var rotulo = new TextBlock
            {
                Text = nome,
                FontSize = 13,
                FontWeight = atual ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = Theme.Solid(ok || qtd > 0 || atual ? Theme.Preto : Theme.Cinza),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var sublinhado = new Border
            {
                Child = rotulo,
                BorderBrush = Theme.Solid(Theme.Laranja),
                BorderThickness = new Thickness(0, 0, 0, atual ? 2 : 0),
                Padding = new Thickness(0, 0, 0, atual ? 1 : 3),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var linha = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = dica };
            linha.Children.Add(bola);
            linha.Children.Add(sublinhado);
            if (qtd > 0)
                linha.Children.Add(new TextBlock
                {
                    Text = qtd.ToString(),
                    FontSize = 11,
                    Foreground = Theme.Solid(sinal == Sinal.Vermelho ? Vermelho : Theme.LaranjaTexto),
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            linha.MouseLeftButtonUp += (s, e) => IrPara(numero);
            return linha;
        }

        private void MostrarPendencias()
        {
            _pendencias.Children.Clear();
            if (_passo == PassoLocal)
            {
                _tituloPendencias.Text = "Projeto, local e exportação";
                _pendencias.Children.Add(Cartao(_local.Sinal == Sinal.Verde ? Sinal.Verde : Sinal.Amarelo, "Projeto e local", _local.Texto, null, new List<(string, string, Action)>()));
                Sinal exp = _res.ProntoParaExportar ? (_res.Amarelas > 0 ? Sinal.Amarelo : Sinal.Verde) : Sinal.Vermelho;
                string texto = _res.ProntoParaExportar
                    ? (_res.Amarelas > 0 ? "Pronto para exportar, com avisos nas etapas em laranja." : "Tudo certo para exportar.")
                    : $"Ainda há {_res.Vermelhas} etapa(s) com pendência. Dá para exportar, mas o IFC sai incompleto.";
                _pendencias.Children.Add(Cartao(exp, "Exportar IFC", texto, null, new List<(string, string, Action)>()));
                return;
            }
            EtapaConferencia etapa = _res.Etapas.FirstOrDefault(e => e.Numero == _passo);
            if (etapa == null) return;
            List<Pendencia> pendencias = Pendentes(etapa);
            _tituloPendencias.Text = pendencias.Count == 0 ? $"Passo {_passo}: tudo certo" : $"O que falta no passo {_passo}";
            if (pendencias.Count == 0)
            {
                _pendencias.Children.Add(Cartao(Sinal.Verde, etapa.Titulo, etapa.Resumo ?? "Nenhuma pendência.", null, new List<(string, string, Action)>()));
                return;
            }
            const int Maximo = 40;
            foreach (Pendencia p in pendencias.Take(Maximo))
            {
                string onde = string.IsNullOrEmpty(p.Categoria) ? p.Assunto : string.IsNullOrEmpty(p.Assunto) ? p.Categoria : p.Assunto + " · " + p.Categoria;
                if (!string.IsNullOrEmpty(p.Campo)) onde = p.Campo + (string.IsNullOrEmpty(p.Origem) ? "" : $" ← {p.Origem}") + (string.IsNullOrEmpty(p.Categoria) ? "" : " · " + p.Categoria);
                var acoes = Decisoes(etapa, p).ToList();
                if (p.QtdElementos > 0) acoes.Add(($"Selecionar ({p.QtdElementos})", "Seleciona esses elementos no Revit (aparecem selecionados ao fechar esta janela).", () => Selecionar(p.Elementos)));
                _pendencias.Children.Add(Cartao(p.Sinal, onde, p.Problema, p.OQueFazer, acoes));
            }
            if (pendencias.Count > Maximo)
                _pendencias.Children.Add(Theme.Detalhe($"e mais {pendencias.Count - Maximo}. Veja todas na Conferência completa."));
        }

        private FrameworkElement Cartao(Sinal sinal, string titulo, string texto, string fazer, List<(string Texto, string Dica, Action Acao)> acoes)
        {
            Color cor = sinal == Sinal.Vermelho ? Vermelho : sinal == Sinal.Verde ? Theme.Verde : Theme.Laranja;
            var conteudo = new StackPanel();
            conteudo.Children.Add(new TextBlock { Text = titulo ?? "", FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            if (!string.IsNullOrEmpty(texto))
                conteudo.Children.Add(new TextBlock { Text = texto, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            if (!string.IsNullOrEmpty(fazer))
                conteudo.Children.Add(new TextBlock { Text = fazer, TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Theme.Solid(Color.FromRgb(0x66, 0x66, 0x66)), Margin = new Thickness(0, 3, 0, 0) });
            if (acoes.Count > 0)
            {
                var botoes = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                bool primeiro = true;
                foreach ((string t, string d, Action a) in acoes)
                {
                    var b = Pilula(t, primario: primeiro && !t.StartsWith("Selecionar", StringComparison.Ordinal));
                    b.ToolTip = d;
                    b.Margin = new Thickness(0, 0, 6, 4);
                    b.Click += (s, e) => a();
                    botoes.Children.Add(b);
                    primeiro = false;
                }
                conteudo.Children.Add(botoes);
            }
            return new Border
            {
                Background = Brushes.White,
                BorderBrush = Theme.Solid(Color.FromRgb(0xEE, 0xEE, 0xEE)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 0, 10),
                Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Opacity = 0.08 },
                Child = new Border
                {
                    BorderBrush = Theme.Solid(cor),
                    BorderThickness = new Thickness(5, 0, 0, 0),
                    CornerRadius = new CornerRadius(6, 0, 0, 6),
                    Padding = new Thickness(10, 8, 10, 6),
                    Child = conteudo,
                },
            };
        }

        /// <summary>Botões que resolvem a pendência daqui mesmo (os mesmos do Painel MetroSP).</summary>
        private IEnumerable<(string Texto, string Dica, Action Acao)> Decisoes(EtapaConferencia etapa, Pendencia p)
        {
            if (p.Correcao == Correcao.ParametrosDeProjeto && !string.IsNullOrEmpty(p.Categoria) && !string.IsNullOrEmpty(p.Assunto))
                yield return ("Vincular à categoria", $"Acrescenta {p.Categoria} nas categorias do parâmetro de projeto \"{p.Assunto}\" e confere de novo. Ctrl+Z desfaz.",
                    () => { if (FecharFerramenta() && AcoesPendencia.VincularCategoria(_doc, p.Assunto, p.Categoria)) Conferir(); });
            if (p.Correcao == Correcao.EditarPropertySets)
                yield return ("Ajustar o .txt", "Abre o .txt de property sets dos padrões para trocar o tipo ou o parâmetro; ao salvar, confere de novo.",
                    () => { if (FecharFerramenta() && AcoesPendencia.EditarPropertySets(_doc, _pacote, this, IntPtr.Zero)) Conferir(); });
            if (etapa.Numero == 3 && p.Elementos.Count > 0)
                yield return ("Escolher classe...", "Escolhe a classe IFC desses elementos e grava no \"Exportar para IFC como\" dos tipos.",
                    () => { if (FecharFerramenta()) AcoesPendencia.EscolherClasse(_doc, _res, p, this, IntPtr.Zero, () => Conferir()); });
            if (p.Correcao == Correcao.EditarEmMassa && p.Elementos.Count > 0)
                yield return ("Preencher...", $"Seleciona os {p.Elementos.Count} elemento(s) e abre o Editar em massa à direita.",
                    () => { if (FecharFerramenta()) { Selecionar(p.Elementos); MostrarFerramenta(); } });
        }

        private void Selecionar(List<DB.ElementId> ids)
        {
            List<DB.ElementId> validos = ids.Where(id => _doc.GetElement(id) != null).ToList();
            if (validos.Count == 0) return;
            try { _app.ActiveUIDocument.Selection.SetElementIds(validos); }
            catch (Exception ex) { TaskDialog.Show("MetroSP", "Não foi possível selecionar:\n" + ex.Message); }
        }

        // ================================================================== ferramenta embutida

        private Window CriarFerramenta()
        {
            UIDocument uidoc = _app.ActiveUIDocument;
            switch (_passo)
            {
                case 2:
                {
                    var w = new Dados.GerenciarParametrosWindow(uidoc);
                    w.Abas.SelectedItem = w.AbaModelo;
                    return w;
                }
                case 3:
                    return new Ids.CategoriasIfcWindow(_doc);
                case 4:
                {
                    // O Editar em massa trabalha na seleção: começa pelos elementos com dado faltando, se nada estiver selecionado.
                    if (uidoc.Selection.GetElementIds().Count == 0)
                    {
                        EtapaConferencia etapa = _res.Etapas.FirstOrDefault(e => e.Numero == 4);
                        if (etapa != null) Selecionar(Pendentes(etapa).SelectMany(p => p.Elementos).Distinct().ToList());
                    }
                    return new Dados.EdicaoMassaWindow(uidoc);
                }
                case 5:
                    return new Classificacao.VerificarClassificacaoWindow(uidoc, Classificacao.ClassSettings.Abrir());
                default:
                    return null;
            }
        }

        private void MostrarFerramenta()
        {
            if (_janelaFerramenta != null) return;
            _ferramenta.Child = null;
            if (_passo == 1) { _ferramenta.Child = PainelArquivos(); return; }
            if (_passo == PassoLocal) { _ferramenta.Child = PainelLocal(); return; }

            Window w;
            try { w = CriarFerramenta(); }
            catch (Exception ex)
            {
                _ferramenta.Child = Theme.Detalhe("Não foi possível abrir a ferramenta desta etapa: " + ex.Message);
                return;
            }
            if (w == null) return;

            // A janela da ferramenta existe (escondida, atrás desta) e o conteúdo dela vem para cá.
            w.ShowInTaskbar = false;
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Owner = this;
            new WindowInteropHelper(w).EnsureHandle();
            if (!(w.Content is UIElement conteudo)) return;
            w.Content = null;
            var hospede = new Border { Child = conteudo };
            hospede.Resources.MergedDictionaries.Add(w.Resources);
            if (w.FontFamily != null) TextElementFonte(hospede, w);
            _ferramenta.Child = hospede;
            _janelaFerramenta = w;
            AcompanharFerramenta();
            w.Closed += (s, e) =>
            {
                if (_janelaFerramenta != w) return;
                _janelaFerramenta = null;
                hospede.Child = null;
                _ferramenta.Child = null;
                // Fechou por dentro (Salvar, Fechar ou Esc): confere de novo e reabre a ferramenta.
                if (!_trocando) Dispatcher.BeginInvoke(new Action(() => Conferir()));
            };
        }

        private static void TextElementFonte(Border hospede, Window w)
        {
            System.Windows.Documents.TextElement.SetFontFamily(hospede, w.FontFamily);
            System.Windows.Documents.TextElement.SetFontSize(hospede, w.FontSize);
        }

        private void AcompanharFerramenta()
        {
            if (_janelaFerramenta == null) return;
            _janelaFerramenta.Left = Left;
            _janelaFerramenta.Top = Top;
            _janelaFerramenta.Width = Math.Max(ActualWidth, 400);
            _janelaFerramenta.Height = Math.Max(ActualHeight, 300);
        }

        /// <summary>Fecha a ferramenta embutida (ela pergunta se quer salvar). Falso se a pessoa cancelou.</summary>
        private bool FecharFerramenta()
        {
            Window w = _janelaFerramenta;
            if (w == null) return true;
            _trocando = true;
            try { w.Close(); }
            finally { _trocando = false; }
            return _janelaFerramenta == null;
        }

        private UIElement PainelArquivos()
        {
            var p = new StackPanel { Margin = new Thickness(4) };
            p.Children.Add(new TextBlock { Text = "Padrões do Metrô em uso", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            p.Children.Add(new TextBlock { Text = _pacote.NomeExibido, FontSize = 13, Margin = new Thickness(0, 0, 0, 10) });
            void Arquivo(string rotulo, string caminho)
            {
                bool existe = PacoteMetro.Existe(caminho);
                var l = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var r = new TextBlock { Text = rotulo, Width = 190, Foreground = Theme.Solid(Theme.Cinza) };
                DockPanel.SetDock(r, Dock.Left);
                l.Children.Add(r);
                l.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrEmpty(caminho) ? "não escolhido" : System.IO.Path.GetFileName(caminho) + (existe ? "" : " (não encontrado)"),
                    ToolTip = caminho,
                    Foreground = Theme.Solid(existe ? Theme.Preto : Vermelho),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
                p.Children.Add(l);
            }
            Arquivo("Property sets (.txt)", _pacote.PropertySets);
            Arquivo("Requisitos (.ids)", _pacote.Ids);
            Arquivo("Setup de exportação (.json)", _pacote.Setup);
            Arquivo("Classes IFC", _pacote.ClassesIfc);
            Arquivo("Planilha de classificação", _pacote.Planilha);
            Arquivo("MetroShared (.txt)", _pacote.MetroShared);
            var b = Pilula("Escolher padrões...", primario: true);
            b.HorizontalAlignment = HorizontalAlignment.Left;
            b.Margin = new Thickness(0, 14, 0, 0);
            b.Click += (s, e) => EscolherPacote(true);
            p.Children.Add(b);
            return p;
        }

        private UIElement PainelLocal()
        {
            var p = new StackPanel { Margin = new Thickness(4) };
            p.Children.Add(new TextBlock { Text = "Projeto e local", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            p.Children.Add(new TextBlock { Text = _local.Texto, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            var local = Pilula("Preencher projeto e local...", primario: _local.Sinal != Sinal.Verde);
            local.HorizontalAlignment = HorizontalAlignment.Left;
            local.Click += (s, e) =>
            {
                LocalizacaoIfcCommand.Abrir(_app);
                _local = AcoesPendencia.SituacaoLocal(_doc);
                Mostrar();
            };
            p.Children.Add(local);
            p.Children.Add(new TextBlock { Text = "Exportar IFC", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 22, 0, 4) });
            p.Children.Add(new TextBlock
            {
                Text = _res.ProntoParaExportar ? "Exporta com o setup (.json) e os property sets (.txt) dos padrões." : "Ainda há etapas com pendência: o IFC sai incompleto.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
            });
            var exportar = Pilula("Exportar IFC...", primario: _local.Sinal == Sinal.Verde);
            exportar.HorizontalAlignment = HorizontalAlignment.Left;
            exportar.Click += (s, e) => ExportarIfcCommand.Exportar(_app);
            p.Children.Add(exportar);
            return p;
        }

        // ================================================================== visual

        private static T Achar<T>(DependencyObject raiz, Func<T, bool> condicao) where T : DependencyObject
        {
            if (raiz == null) return null;
            if (raiz is T t && condicao(t)) return t;
            foreach (object filho in LogicalTreeHelper.GetChildren(raiz))
                if (filho is DependencyObject d)
                {
                    T r = Achar(d, condicao);
                    if (r != null) return r;
                }
            return null;
        }

        private static Button BotaoEscuro(string texto, string dica)
        {
            var b = new Button { Content = texto, ToolTip = dica, Foreground = Brushes.White, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            var borda = new FrameworkElementFactory(typeof(Border));
            borda.SetValue(Border.PaddingProperty, new Thickness(6, 4, 6, 4));
            borda.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            borda.AppendChild(cp);
            b.Template = new ControlTemplate(typeof(Button)) { VisualTree = borda };
            return b;
        }

        /// <summary>Botão arredondado (pílula); o primário é laranja.</summary>
        private static Button Pilula(string texto, bool primario = false)
        {
            var b = new Button { Content = texto, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            var borda = new FrameworkElementFactory(typeof(Border), "borda");
            borda.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
            borda.SetValue(Border.PaddingProperty, new Thickness(14, 4, 14, 5));
            borda.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            borda.SetValue(Border.BackgroundProperty, primario ? Theme.Solid(Theme.Laranja) : Brushes.White);
            borda.SetValue(Border.BorderBrushProperty, primario ? Theme.Solid(Theme.Laranja) : Theme.Solid(Color.FromRgb(0xBB, 0xBB, 0xBB)));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            borda.AppendChild(cp);
            var t = new ControlTemplate(typeof(Button)) { VisualTree = borda };
            var sobre = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            sobre.Setters.Add(new Setter(Border.BackgroundProperty, primario ? Theme.Solid(Color.FromRgb(0xFF, 0xAD, 0x33)) : Theme.Solid(Theme.LaranjaSelecao), "borda"));
            t.Triggers.Add(sobre);
            var desligado = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            desligado.Setters.Add(new Setter(UIElement.OpacityProperty, 0.5));
            t.Triggers.Add(desligado);
            b.Template = t;
            if (primario) b.FontWeight = FontWeights.SemiBold;
            b.Foreground = Theme.Solid(Theme.Preto);
            return b;
        }
    }
}
