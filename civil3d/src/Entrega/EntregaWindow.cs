using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;
using MetroSP.Alinhamento;
using MetroSP.Config;
using MetroSP.PropertySets;
using MetroSP.Ui;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDocument = Autodesk.AutoCAD.ApplicationServices.Document;

namespace MetroSP.Entrega
{
    /// <summary>
    /// Janela única da entrega (mesmo visual do plugin do Revit): as etapas em cima, numa linha fina com setinhas, e embaixo
    /// as pendências da etapa aberta (à esquerda, com os botões que resolvem) e a ferramenta da etapa embutida (à direita).
    /// Abre modal, no contexto do comando, para que as ferramentas embutidas possam gravar no desenho.
    /// </summary>
    public class EntregaWindow : Window
    {
        static readonly Color Vermelho = Color.FromRgb(0xB2, 0x22, 0x22);
        const int Ultima = ResultadoEntrega.UltimaEtapa;

        readonly AcDocument _doc;
        ResultadoEntrega _res;
        int _passo;
        int _abaDados;               // etapa 4: 0 = Corrigir escrita, 1 = Confrontar planilha
        string _comandoDepois;       // comando que roda quando a janela fecha (exportar, sincronizar...)
        readonly List<ObjectId> _selecionar = new List<ObjectId>();

        readonly Grid _etapas;
        readonly TextBlock _tituloPendencias;
        readonly StackPanel _pendencias;
        readonly StackPanel _extraEsquerda;
        readonly Border _ferramenta;
        readonly StackPanel _abas;
        readonly TextBlock _rodapeTexto;
        readonly Button _anterior;
        readonly Button _proximo;
        readonly TextBlock _escopo;

        Window _janelaFerramenta;
        Action _salvarPainel;        // etapas com painel próprio (1, 6): o que o "Salvar e conferir" grava
        bool _trocando;

        /// <summary>Confere o desenho ativo e abre a janela. Chamar dentro de um comando.</summary>
        public static void Abrir(AcDocument doc, int passo = 0)
        {
            var janela = new EntregaWindow(doc);
            if (!janela.Conferir(primeira: passo == 0)) return;
            if (passo >= 1 && passo <= Ultima) { janela._passo = passo; janela.Mostrar(); }
            AcApp.ShowModalWindow(AcApp.MainWindow.Handle, janela, false);
            if (janela._selecionar.Count > 0)
            {
                try { doc.Editor.SetImpliedSelection(janela._selecionar.Where(id => id.IsValid && !id.IsErased).ToArray()); } catch { }
            }
            if (!string.IsNullOrEmpty(janela._comandoDepois))
                doc.SendStringToExecute("_" + janela._comandoDepois + " ", true, false, true);
        }

        EntregaWindow(AcDocument doc)
        {
            _doc = doc;
            string nome = System.IO.Path.GetFileNameWithoutExtension(doc.Name);
            Title = "MetroSP · Entrega · " + nome;
            Width = 1240;
            Height = 780;
            MinWidth = 900;
            MinHeight = 560;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Theme.Estilos(this);
            Resources[SystemColors.HighlightBrushKey] = Theme.Solid(Theme.Laranja);
            Resources[SystemColors.HighlightTextBrushKey] = Theme.Solid(Theme.Preto);
            Resources[SystemColors.InactiveSelectionHighlightBrushKey] = Theme.Solid(Theme.LaranjaClaro);
            Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = Theme.Solid(Theme.Preto);

            // ---- faixa preta
            var marca = new TextBlock { Foreground = Brushes.White, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            marca.Inlines.Add(new System.Windows.Documents.Run("MetroSP") { Foreground = Theme.Solid(Theme.Laranja), FontWeight = FontWeights.SemiBold });
            marca.Inlines.Add(new System.Windows.Documents.Run(" · Entrega · " + nome));
            _escopo = new TextBlock { Foreground = Theme.Solid(Color.FromRgb(0xBB, 0xBB, 0xBB)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            var conferir = BotaoEscuro("⟳ Conferir", "Confere o desenho de novo e atualiza as etapas.");
            conferir.Click += (s, e) => { if (FecharFerramenta()) Conferir(); };
            var direita = new StackPanel { Orientation = Orientation.Horizontal };
            direita.Children.Add(_escopo);
            direita.Children.Add(conferir);
            var faixa = new DockPanel { Background = Theme.Solid(Theme.Preto) };
            DockPanel.SetDock(direita, Dock.Right);
            faixa.Children.Add(direita);
            faixa.Children.Add(new Border { Padding = new Thickness(14, 8, 14, 8), Child = marca });

            // ---- etapas
            _etapas = new Grid { Margin = new Thickness(24, 18, 24, 16) };
            var etapasBorda = new Border { BorderBrush = Theme.Solid(Color.FromRgb(0xEE, 0xEE, 0xEE)), BorderThickness = new Thickness(0, 0, 0, 1), Child = _etapas };

            // ---- pendências (esquerda)
            _tituloPendencias = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap };
            _pendencias = new StackPanel();
            _extraEsquerda = new StackPanel();
            var esquerdaConteudo = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };
            esquerdaConteudo.Children.Add(_tituloPendencias);
            esquerdaConteudo.Children.Add(_pendencias);
            esquerdaConteudo.Children.Add(_extraEsquerda);
            var esquerda = new Border
            {
                Width = 360,
                Background = Theme.Solid(Color.FromRgb(0xFC, 0xFC, 0xFC)),
                BorderBrush = Theme.Solid(Color.FromRgb(0xEE, 0xEE, 0xEE)),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = esquerdaConteudo },
            };

            // ---- ferramenta (direita), com abas em pílula quando a etapa tem mais de uma
            _abas = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 10, 14, 0) };
            _ferramenta = new Border { Padding = new Thickness(14, 10, 14, 6) };
            var direitaCorpo = new DockPanel();
            DockPanel.SetDock(_abas, Dock.Top);
            direitaCorpo.Children.Add(_abas);
            direitaCorpo.Children.Add(_ferramenta);

            // ---- rodapé
            _anterior = Pilula("‹ Passo anterior");
            _anterior.Click += (s, e) => IrPara(_passo - 1);
            _proximo = Pilula("Próximo passo ›");
            _proximo.Click += (s, e) => IrPara(_passo + 1);
            var salvar = Pilula("Salvar e conferir", primario: true);
            salvar.ToolTip = "Grava o que foi alterado na ferramenta desta etapa e confere o desenho de novo.";
            salvar.Margin = new Thickness(0, 0, 8, 0);
            salvar.Click += (s, e) => SalvarEConferir();
            _rodapeTexto = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(12, 0, 12, 0) };
            var direitaRodape = new StackPanel { Orientation = Orientation.Horizontal };
            direitaRodape.Children.Add(salvar);
            direitaRodape.Children.Add(_proximo);
            var rodape = new DockPanel { Margin = new Thickness(16, 10, 16, 10) };
            DockPanel.SetDock(_anterior, Dock.Left);
            DockPanel.SetDock(direitaRodape, Dock.Right);
            rodape.Children.Add(_anterior);
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
            corpo.Children.Add(direitaCorpo);

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

        // ================================================================== conferir

        bool Conferir(bool primeira = false)
        {
            try
            {
                Cursor = Cursors.Wait;
                _res = ConferenciaEntrega.Executar(_doc);
            }
            catch (Exception ex)
            {
                Log.Erro("conferência da entrega", ex);
                MessageBox.Show("Não foi possível conferir o desenho:\n" + ex.Message, "MetroSP");
                return false;
            }
            finally { Cursor = null; }
            if (primeira || _passo == 0)
            {
                // Começa pela primeira etapa com pendência (ou pela última, se está tudo certo).
                _passo = _res.Etapas.FirstOrDefault(e => e.Numero < Ultima && e.Abertas.Count > 0)?.Numero ?? Ultima;
            }
            Mostrar();
            return true;
        }

        void SalvarEConferir()
        {
            if (_salvarPainel != null)
            {
                try { _salvarPainel(); }
                catch (Exception ex) { MessageBox.Show(this, "Não foi possível salvar:\n" + ex.Message, "MetroSP"); return; }
                Conferir();
                return;
            }
            Window w = _janelaFerramenta;
            Button padrao = w == null ? null : Achar<Button>(_ferramenta, b => b.IsDefault && b.IsEnabled);
            if (padrao != null && new ButtonAutomationPeer(padrao).GetPattern(PatternInterface.Invoke) is IInvokeProvider clique)
            {
                // O botão principal da ferramenta grava (e às vezes fecha); ao fechar, a etapa é conferida de novo.
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

        void IrPara(int passo)
        {
            if (passo < 1 || passo > Ultima || passo == _passo) return;
            if (!FecharFerramenta()) return;
            _passo = passo;
            Mostrar();
        }

        void Mostrar()
        {
            _escopo.Text = $"conferido às {_res.Quando:HH:mm}";
            MostrarEtapas();
            MostrarPendencias();
            if (IsLoaded) MostrarFerramenta();   // antes de abrir, espera o Loaded (a ferramenta precisa desta janela como dona)
            _anterior.Visibility = _passo > 1 ? Visibility.Visible : Visibility.Hidden;
            _proximo.Visibility = _passo < Ultima ? Visibility.Visible : Visibility.Hidden;
            _rodapeTexto.Text = "Ao salvar, a etapa é conferida de novo e fica verde se estiver tudo certo.";
        }

        void MostrarEtapas()
        {
            _etapas.Children.Clear();
            _etapas.ColumnDefinitions.Clear();
            foreach (EtapaEntrega e in _res.Etapas)
            {
                if (e.Numero > 1)
                {
                    _etapas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 18 });
                    var seta = Seta();
                    Grid.SetColumn(seta, _etapas.ColumnDefinitions.Count - 1);
                    _etapas.Children.Add(seta);
                }
                _etapas.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                // A última etapa não fica verde sozinha: ela é a exportação.
                Sinal sinal = e.Numero == Ultima && e.Abertas.Count == 0 ? Sinal.Cinza : e.Sinal;
                FrameworkElement item = Etapa(e.Numero, e.Nome, sinal, e.Abertas.Count, e.Dica + (string.IsNullOrEmpty(e.Resumo) ? "" : "\n" + e.Resumo));
                Grid.SetColumn(item, _etapas.ColumnDefinitions.Count - 1);
                _etapas.Children.Add(item);
            }
        }

        static FrameworkElement Seta()
        {
            var g = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0), Height = 10 };
            g.Children.Add(new Rectangle { Height = 1, Fill = Theme.Solid(Color.FromRgb(0xCC, 0xCC, 0xCC)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            g.Children.Add(new Polygon
            {
                Points = new PointCollection { new Point(0, 0), new Point(7, 5), new Point(0, 10) },
                Fill = Theme.Solid(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                HorizontalAlignment = HorizontalAlignment.Right,
            });
            return g;
        }

        FrameworkElement Etapa(int numero, string nome, Sinal sinal, int qtd, string dica)
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
                Margin = new Thickness(7, 0, 0, 0),
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
                    Margin = new Thickness(5, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            linha.MouseLeftButtonUp += (s, e) => IrPara(numero);
            return linha;
        }

        void MostrarPendencias()
        {
            _pendencias.Children.Clear();
            _extraEsquerda.Children.Clear();
            EtapaEntrega etapa = _res.Etapa(_passo);
            if (etapa == null) return;
            List<Pendencia> abertas = etapa.Abertas;
            _tituloPendencias.Text = abertas.Count == 0 ? $"Passo {_passo}: tudo certo" : $"O que falta no passo {_passo}";
            if (abertas.Count == 0)
                _pendencias.Children.Add(Cartao(Sinal.Verde, etapa.Nome, etapa.Resumo ?? "Nenhuma pendência.", new List<(string, string, Action)>()));
            const int Maximo = 40;
            foreach (Pendencia p in abertas.Take(Maximo))
            {
                var acoes = Acoes(p).ToList();
                if (p.Objetos.Count > 0)
                    acoes.Add(($"Selecionar ({p.Objetos.Count})", "Seleciona esses objetos no desenho (aparecem selecionados ao fechar esta janela).", () => Selecionar(p.Objetos)));
                _pendencias.Children.Add(Cartao(p.Sinal, p.Titulo, p.Texto, acoes));
            }
            if (abertas.Count > Maximo)
                _pendencias.Children.Add(Theme.Detalhe($"e mais {abertas.Count - Maximo}."));
            if (_passo == Ultima) _extraEsquerda.Children.Add(PainelExportar());
        }

        FrameworkElement Cartao(Sinal sinal, string titulo, string texto, List<(string Texto, string Dica, Action Acao)> acoes)
        {
            Color cor = sinal == Sinal.Vermelho ? Vermelho : sinal == Sinal.Verde ? Theme.Verde : Theme.Laranja;
            var conteudo = new StackPanel();
            conteudo.Children.Add(new TextBlock { Text = titulo ?? "", FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            if (!string.IsNullOrEmpty(texto))
                conteudo.Children.Add(new TextBlock { Text = texto, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
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

        /// <summary>Botões que resolvem a pendência daqui mesmo.</summary>
        IEnumerable<(string Texto, string Dica, Action Acao)> Acoes(Pendencia p)
        {
            switch (p.Acao)
            {
                case Acao.Comando:
                    yield return (p.Valor ?? "Abrir", "Fecha esta janela e abre " + (p.Valor ?? p.Alvo) + ".", () => FecharE(p.Alvo));
                    break;
                case Acao.Exportador:
                    yield return ("Exportador...", "Versão da IFC 4.3 Extension e onde baixar.", () =>
                    {
                        var cfg = Settings.Load();
                        ExtensionWindow.Show(cfg, cfg.ExportCommand, blocking: false);
                        if (FecharFerramenta()) Conferir();
                    });
                    break;
                case Acao.SalvarDesenho:
                    yield return ("Salvar o desenho", "Fecha esta janela e salva o DWG.", () => FecharE("QSAVE"));
                    break;
                case Acao.CriarPset:
                    yield return ("Criar no desenho", $"Cria o property set {p.Alvo} como está no mapeamento IFC. Pode ser ajustado depois em Property sets.", () =>
                    {
                        if (!FecharFerramenta()) return;
                        Executar(() => PsetsCore.CriarNoDesenho(_doc.Database, new[] { (PsetMontagem)p.Dados }));
                    });
                    break;
                case Acao.AnexarPset:
                    yield return ("Anexar", $"Anexa {p.Alvo} aos {p.Objetos.Count} objeto(s), com o valor padrão. Desfaz com Ctrl+Z depois de fechar.", () =>
                    {
                        if (!FecharFerramenta()) return;
                        var objetos = (List<ObjetoAnexar>)p.Dados;
                        foreach (var o in objetos) o.Faltam = p.Alvo;
                        Executar(() => PsetsCore.Anexar(_doc.Database, objetos));
                    });
                    break;
                case Acao.UsarPerfil:
                    yield return ($"Usar {p.Valor}", "Guarda este perfil para o alinhamento neste desenho; a exportação não pergunta mais.", () =>
                    {
                        if (!FecharFerramenta()) return;
                        AlignmentCommands.GuardarPerfis(_doc, new[] { (p.Alvo, p.Valor) });
                        Conferir();
                    });
                    break;
                case Acao.AbrirEscrita:
                    if (_abaDados != 0)
                        yield return ("Corrigir escrita", "Abre o Corrigir escrita ao lado.", () => { if (FecharFerramenta()) { _abaDados = 0; Mostrar(); } });
                    break;
                case Acao.PreencherProjeto:
                    break; // o Projeto e Local já está aberto ao lado
            }
        }

        void Executar(Func<string> gravar)
        {
            string msg;
            try { msg = gravar(); }
            catch (Exception ex) { msg = "Não foi possível gravar:\n" + ex.Message; }
            Conferir();
            if (!string.IsNullOrEmpty(msg)) MessageBox.Show(this, msg, "MetroSP");
        }

        void FecharE(string comando)
        {
            if (!FecharFerramenta()) return;
            _comandoDepois = comando;
            Close();
        }

        void Selecionar(List<ObjectId> ids)
        {
            _selecionar.Clear();
            _selecionar.AddRange(ids);
            try { _doc.Editor.SetImpliedSelection(ids.Where(id => id.IsValid && !id.IsErased).ToArray()); } catch { }
            _rodapeTexto.Text = $"{ids.Count} objeto(s) ficam selecionados ao fechar esta janela.";
        }

        // ================================================================== ferramenta embutida

        Window CriarFerramenta()
        {
            switch (_passo)
            {
                case 2:
                {
                    var w = new MontarPsetsWindow(_doc, null);
                    // Faltando anexar: abre direto na aba Anexar.
                    if (_res.Etapa(2).Abertas.Any(p => p.Acao == Acao.AnexarPset)) w.Abas.SelectedIndex = 1;
                    return w;
                }
                case 3:
                {
                    var w = new ConfigWindow(Commands.DrawingFolder(_doc));
                    w.Abas.SelectedIndex = 1;   // Classes IFC
                    return w;
                }
                case 4:
                    return _abaDados == 0
                        ? new Escrita.CorrigirEscritaWindow(new Confronto.FonteCivil3D(_doc, null))
                        : (Window)new Confronto.ConfrontarWindow(new Confronto.FonteCivil3D(_doc, null));
                case 5:
                    return new Classificacao.VerificarClassificacaoWindow(_doc, null);
                case 7:
                    return new ConfigWindow(Commands.DrawingFolder(_doc), somenteProjeto: true);
                default:
                    return null;
            }
        }

        void MostrarAbas()
        {
            _abas.Children.Clear();
            _abas.Visibility = _passo == 4 ? Visibility.Visible : Visibility.Collapsed;
            if (_passo != 4) return;
            string[] nomes = { "Corrigir escrita", "Confrontar planilha" };
            string[] dicas =
            {
                "Espaços, acentos e caracteres estranhos em property sets, nome e descrição.",
                "Compara property sets, nome, descrição e layer com uma planilha externa.",
            };
            for (int i = 0; i < nomes.Length; i++)
            {
                int n = i;
                var b = new Button { Content = nomes[i], ToolTip = dicas[i], Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 4, 0) };
                bool ativa = n == _abaDados;
                var borda = new FrameworkElementFactory(typeof(Border));
                borda.SetValue(Border.CornerRadiusProperty, new CornerRadius(16));
                borda.SetValue(Border.PaddingProperty, new Thickness(14, 6, 14, 6));
                borda.SetValue(Border.BackgroundProperty, ativa ? Theme.Solid(Theme.Preto) : Theme.Solid(Theme.CinzaFundo));
                borda.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
                b.Template = new ControlTemplate(typeof(Button)) { VisualTree = borda };
                b.Foreground = ativa ? Brushes.White : Theme.Solid(Color.FromRgb(0x55, 0x55, 0x55));
                if (ativa) b.FontWeight = FontWeights.SemiBold;
                b.Click += (s, e) =>
                {
                    if (n == _abaDados || !FecharFerramenta()) return;
                    _abaDados = n;
                    MostrarAbas();
                    MostrarPendencias();
                    MostrarFerramenta();
                };
                _abas.Children.Add(b);
            }
        }

        void MostrarFerramenta()
        {
            if (_janelaFerramenta != null) return;
            MostrarAbas();
            _salvarPainel = null;
            _ferramenta.Child = null;
            if (_passo == 1) { _ferramenta.Child = PainelExportador(); return; }
            if (_passo == 6) { _ferramenta.Child = PainelAlinhamentos(); return; }

            Window w;
            try { w = CriarFerramenta(); }
            catch (Exception ex)
            {
                Log.Erro("ferramenta da etapa " + _passo, ex);
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
            EsconderCabecalho(conteudo);
            var hospede = new Border { Child = conteudo };
            hospede.Resources.MergedDictionaries.Add(w.Resources);
            System.Windows.Documents.TextElement.SetFontFamily(hospede, w.FontFamily);
            System.Windows.Documents.TextElement.SetFontSize(hospede, w.FontSize);
            _ferramenta.Child = hospede;
            _janelaFerramenta = w;
            AcompanharFerramenta();
            w.Closed += (s, e) =>
            {
                if (_janelaFerramenta != w) return;
                _janelaFerramenta = null;
                hospede.Child = null;
                _ferramenta.Child = null;
                // Fechou por dentro (Salvar e fechar, Fechar ou Esc): confere de novo e reabre a ferramenta.
                if (!_trocando) Dispatcher.BeginInvoke(new Action(() => Conferir()));
            };
        }

        /// <summary>O cabeçalho preto da ferramenta repete o que a faixa desta janela já diz.</summary>
        static void EsconderCabecalho(UIElement conteudo)
        {
            if (!(conteudo is Panel p)) return;
            foreach (UIElement filho in p.Children)
                if (filho is FrameworkElement fe && Equals(fe.Tag, Theme.TagCabecalho)) fe.Visibility = Visibility.Collapsed;
        }

        void AcompanharFerramenta()
        {
            if (_janelaFerramenta == null) return;
            _janelaFerramenta.Left = Left;
            _janelaFerramenta.Top = Top;
            _janelaFerramenta.Width = Math.Max(ActualWidth, 400);
            _janelaFerramenta.Height = Math.Max(ActualHeight, 300);
        }

        /// <summary>Fecha a ferramenta embutida. Falso se ela não fechou (a pessoa cancelou).</summary>
        bool FecharFerramenta()
        {
            Window w = _janelaFerramenta;
            if (w == null) return true;
            _trocando = true;
            try { w.Close(); }
            finally { _trocando = false; }
            return _janelaFerramenta == null;
        }

        // ================================================================== painéis próprios

        UIElement PainelExportador()
        {
            var cfg = Settings.Load();
            var p = new StackPanel { Margin = new Thickness(4) };
            p.Children.Add(Titulo("Exportador da Autodesk"));
            var instaladas = AutodeskIfcExtension.FindInstalled();
            bool ok = instaladas.Count > 0 || AutodeskIfcExtension.CommandDefined(cfg.ExportCommand);
            p.Children.Add(Theme.Selo(ok ? "IFC 4.3 Extension instalada" : "IFC 4.3 Extension não instalada", ok ? Theme.Estado.Ok : Theme.Estado.Aviso));
            foreach (var i in instaladas)
                p.Children.Add(Theme.Detalhe(i.Name + (i.Version != null ? " · versão " + i.Version : "")));

            p.Children.Add(Titulo("Configuração IFC", 18));
            p.Children.Add(Theme.Detalhe(cfg.UsesForma ? "Pacote sincronizado com o Autodesk Forma" : "Pacote local (padrão MetroSP ou importado)"));
            var grade = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            int linha = 0;
            foreach (var (kind, caminho, data) in ConfigPackage.List())
            {
                grade.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var n = new TextBlock { Text = System.IO.Path.GetFileName(caminho), ToolTip = caminho, Margin = new Thickness(0, 2, 12, 2), TextTrimming = TextTrimming.CharacterEllipsis };
                var d = new TextBlock { Text = data.ToString("dd/MM/yyyy HH:mm"), Foreground = Theme.Solid(Theme.Cinza), Margin = new Thickness(0, 2, 0, 2) };
                Grid.SetRow(n, linha); Grid.SetRow(d, linha); Grid.SetColumn(d, 1);
                grade.Children.Add(n); grade.Children.Add(d);
                linha++;
            }
            p.Children.Add(grade);

            var botoes = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
            var exp = Pilula("Exportador...", primario: !ok);
            exp.ToolTip = "Versão instalada, atualizações (Autodesk e GitHub) e a pasta dos arquivos do desenho.";
            exp.Click += (s, e) => { ExtensionWindow.Show(cfg, cfg.ExportCommand, blocking: false); MostrarFerramenta(); };
            var config = Pilula("Configurar IFC...");
            config.ToolTip = "Classes, propriedades, alinhamento e opções avançadas do pacote.";
            config.Click += (s, e) =>
            {
                var w = new ConfigWindow(Commands.DrawingFolder(_doc)) { Owner = this };
                w.ShowDialog();
                Conferir();
            };
            var forma = Pilula("Forma...");
            forma.ToolTip = "Entrar, sair, sincronizar e conta do Autodesk Forma.";
            forma.Click += (s, e) =>
            {
                string cmd = FormaWindow.Escolher(this);
                if (cmd != null) FecharE(cmd);
            };
            foreach (var b in new[] { exp, config, forma }) { b.Margin = new Thickness(0, 0, 6, 4); botoes.Children.Add(b); }
            p.Children.Add(botoes);
            return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = p };
        }

        /// <summary>Linha da tabela de perfis (etapa 6).</summary>
        public class LinhaPerfil
        {
            public string Handle { get; set; }
            public string Alinhamento { get; set; }
            public List<string> Perfis { get; set; }
            public string Escolhido { get; set; }
            public string Estacas { get; set; }
            public string Situacao { get; set; }
            public bool Pendente { get; set; }
            public bool PodeEscolher => Perfis != null && Perfis.Count > 1;
            public string Original { get; set; }
        }

        UIElement PainelAlinhamentos()
        {
            var linhas = new ObservableCollection<LinhaPerfil>(_res.Perfis.Select(s => new LinhaPerfil
            {
                Handle = s.Handle,
                Alinhamento = s.Alinhamento,
                Perfis = s.Perfis,
                Escolhido = s.Escolhido,
                Original = s.Origem == "Sugerido" ? null : s.Escolhido,
                Estacas = s.Estacas,
                Pendente = s.Pendente,
                Situacao = s.Origem switch
                {
                    "Único" => "Um perfil",
                    "Guardado" => "Escolhido para este desenho",
                    "Preferido" => "Pela lista de preferidos",
                    "Sugerido" => $"{s.Perfis.Count} perfis: escolha",
                    _ => "Sem perfil: vai só a planta",
                },
            }));
            var p = new DockPanel();
            var topo = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var soPendentes = new CheckBox { Content = $"Só os pendentes ({linhas.Count(l => l.Pendente)})", IsChecked = linhas.Any(l => l.Pendente), VerticalAlignment = VerticalAlignment.Center };
            var opcoes = Pilula("Opções do alinhamento...");
            opcoes.ToolTip = "Eixo 3D no IFC, passo das estacas e a lista de perfis preferidos (vale para todos os desenhos).";
            opcoes.Click += (s, e) =>
            {
                var w = new ConfigWindow(Commands.DrawingFolder(_doc)) { Owner = this };
                w.Abas.SelectedIndex = 3;   // Alinhamento
                w.ShowDialog();
                Conferir();
            };
            DockPanel.SetDock(opcoes, Dock.Right);
            topo.Children.Add(opcoes);
            topo.Children.Add(soPendentes);
            DockPanel.SetDock(topo, Dock.Top);
            p.Children.Add(topo);

            if (linhas.Count == 0)
            {
                p.Children.Add(Theme.Detalhe("Este desenho não tem alinhamento: o IFC vai com projeto, terreno, instalações e os objetos do desenho."));
                return p;
            }

            var grade = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                SelectionMode = DataGridSelectionMode.Single,
            };
            grade.Columns.Add(new DataGridTextColumn { Header = "Alinhamento", Binding = new Binding(nameof(LinhaPerfil.Alinhamento)), IsReadOnly = true, Width = new DataGridLength(1.3, DataGridLengthUnitType.Star) });
            var combo = new FrameworkElementFactory(typeof(ComboBox));
            combo.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(LinhaPerfil.Perfis)));
            combo.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, new Binding(nameof(LinhaPerfil.Escolhido)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            combo.SetBinding(UIElement.IsEnabledProperty, new Binding(nameof(LinhaPerfil.PodeEscolher)));
            grade.Columns.Add(new DataGridTemplateColumn
            {
                Header = "Perfil usado no IFC",
                CellTemplate = new DataTemplate { VisualTree = combo },
                Width = new DataGridLength(1.6, DataGridLengthUnitType.Star),
            });
            grade.Columns.Add(new DataGridTextColumn { Header = "Estacas", Binding = new Binding(nameof(LinhaPerfil.Estacas)), IsReadOnly = true, Width = 150 });
            var estilo = new Style(typeof(TextBlock));
            var pend = new DataTrigger { Binding = new Binding(nameof(LinhaPerfil.Pendente)), Value = true };
            pend.Setters.Add(new Setter(TextBlock.ForegroundProperty, Theme.Solid(Theme.LaranjaTexto)));
            pend.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
            estilo.Triggers.Add(pend);
            grade.Columns.Add(new DataGridTextColumn { Header = "Situação", Binding = new Binding(nameof(LinhaPerfil.Situacao)), IsReadOnly = true, ElementStyle = estilo, Width = new DataGridLength(1.2, DataGridLengthUnitType.Star) });

            var vista = CollectionViewSource.GetDefaultView(linhas);
            void Filtrar() => vista.Filter = soPendentes.IsChecked == true ? (o => ((LinhaPerfil)o).Pendente) : (Predicate<object>)null;
            soPendentes.Checked += (s, e) => Filtrar();
            soPendentes.Unchecked += (s, e) => Filtrar();
            Filtrar();
            grade.ItemsSource = vista;
            p.Children.Add(grade);

            // Salvar e conferir: guarda o perfil de cada alinhamento com mais de um (a exportação não pergunta mais).
            _salvarPainel = () =>
            {
                var escolhas = linhas.Where(l => l.PodeEscolher && !string.IsNullOrEmpty(l.Escolhido) && l.Escolhido != l.Original)
                    .Select(l => (l.Handle, l.Escolhido)).ToList();
                if (escolhas.Count > 0) AlignmentCommands.GuardarPerfis(_doc, escolhas);
            };
            return p;
        }

        UIElement PainelExportar()
        {
            var p = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            p.Children.Add(new TextBlock { Text = "Exportar IFC", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            string texto = _res.Vermelhas > 0
                ? $"Ainda há {_res.Vermelhas} etapa(s) com pendência. Dá para exportar, mas o IFC sai incompleto."
                : _res.Amarelas > 0 ? "Pronto para exportar, com avisos nas etapas em laranja." : "Tudo certo para exportar.";
            p.Children.Add(new TextBlock { Text = texto, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            var botoes = new WrapPanel();
            var tudo = Pilula("Exportar IFC", primario: _res.Etapa(Ultima).Abertas.All(x => x.Sinal != Sinal.Vermelho));
            tudo.ToolTip = "Fecha esta janela e exporta o desenho todo (IFC 4.3).";
            tudo.Click += (s, e) => FecharE("MSP_IFCEXPORT");
            var sel = Pilula("Só a seleção");
            sel.ToolTip = "Fecha esta janela e exporta só os objetos selecionados.";
            sel.Click += (s, e) => FecharE("MSP_IFCEXPORTSEL");
            var ali = Pilula("Só os alinhamentos");
            ali.ToolTip = "Fecha esta janela e grava um IFC separado só com os alinhamentos 3D.";
            ali.Click += (s, e) => FecharE("MSP_IFCALINHAMENTO");
            foreach (var b in new[] { tudo, sel, ali }) { b.Margin = new Thickness(0, 0, 6, 4); botoes.Children.Add(b); }
            p.Children.Add(botoes);
            return p;
        }

        // ================================================================== visual

        static TextBlock Titulo(string texto, double acima = 0) =>
            new TextBlock { Text = texto, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, acima, 0, 6) };

        static T Achar<T>(DependencyObject raiz, Func<T, bool> condicao) where T : DependencyObject
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

        static Button BotaoEscuro(string texto, string dica)
        {
            var b = new Button { Content = texto, ToolTip = dica, Foreground = Brushes.White, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            var borda = new FrameworkElementFactory(typeof(Border));
            borda.SetValue(Border.PaddingProperty, new Thickness(6, 4, 6, 4));
            borda.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            borda.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
            b.Template = new ControlTemplate(typeof(Button)) { VisualTree = borda };
            return b;
        }

        /// <summary>Botão arredondado (pílula); o primário é laranja.</summary>
        internal static Button Pilula(string texto, bool primario = false)
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
