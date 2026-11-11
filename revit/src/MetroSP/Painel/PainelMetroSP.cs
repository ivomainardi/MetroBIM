using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Autodesk.Revit.UI;
using MetroSP.Commands;
using MetroSP.Conferencia;
using MetroSP.Ui;
using DB = Autodesk.Revit.DB;

namespace MetroSP.Painel
{
    /// <summary>
    /// Painel MetroSP: fica encaixado ao lado do modelo (DockablePane) com os passos da entrega e o semáforo de cada um.
    /// Usa a mesma conferência do Conferir Entrega. Abrir um passo mostra as pendências; dali dá para selecionar e pintar
    /// os elementos e abrir a ferramenta que corrige. Tudo o que mexe no Revit passa por um ExternalEvent, porque o
    /// painel não é modal.
    /// </summary>
    public class PainelMetroSP : Page, IDockablePaneProvider
    {
        public static readonly DockablePaneId Id = new DockablePaneId(new Guid("6E0B7C2A-4F1D-4B8E-9C35-2D7A1F0E8B41"));
        public const string Titulo = "MetroSP · Entrega";

        /// <summary>O painel registrado no Revit (um por sessão).</summary>
        internal static PainelMetroSP Instancia { get; private set; }

        private readonly FilaRevit _fila = new FilaRevit();
        private readonly ExternalEvent _evento;

        private readonly TextBlock _nomePacote;
        private readonly Border _barraProgresso;
        private readonly TextBlock _textoProgresso;
        private readonly Border _aviso;
        private readonly TextBlock _textoAviso;
        private readonly StackPanel _passos;
        private readonly CheckBox _soVista;
        private readonly Button _pintar;

        private DB.Document _doc;
        private PacoteMetro _pacote;
        private ResultadoConferencia _res;
        /// <summary>Situação do passo Projeto e Local, lida no contexto da API junto com a conferência.</summary>
        private (Sinal Sinal, string Texto) _local = (Sinal.Cinza, TextoLocal);
        private const string TextoLocal = "IfcProject, IfcSite, IfcBuilding e a localização pelo Project Base Point.";
        private readonly HashSet<int> _abertos = new HashSet<int>();

        /// <summary>Criar no OnStartup (contexto da API, para o ExternalEvent).</summary>
        public PainelMetroSP()
        {
            Instancia = this;
            _evento = ExternalEvent.Create(_fila);
            Theme.Estilos(this);
            Background = Brushes.White;

            // ---- topo: padrões em uso
            _nomePacote = new TextBlock { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Text = "—" };
            var trocar = Theme.Link("trocar");
            trocar.ToolTip = "Escolher os padrões do Metrô (arquivo .metrosp do coordenador BIM, ou os arquivos um a um).";
            trocar.Click += (s, e) => Revit(app => EscolherPacote(app, true));
            var topo = new DockPanel { Margin = new Thickness(12, 10, 12, 8) };
            DockPanel.SetDock(trocar, Dock.Right);
            topo.Children.Add(trocar);
            var textoTopo = new StackPanel();
            textoTopo.Children.Add(Theme.Secao("PADRÕES"));
            textoTopo.Children.Add(_nomePacote);
            topo.Children.Add(textoTopo);
            var faixa = new Border { BorderBrush = Theme.Solid(Theme.Laranja), BorderThickness = new Thickness(0, 0, 0, 3), Child = topo };

            // ---- progresso
            _barraProgresso = new Border { Background = Theme.Solid(Theme.Laranja), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            var trilho = new Border { Background = Theme.Solid(Theme.CinzaClaro), Height = 6, Child = _barraProgresso, VerticalAlignment = VerticalAlignment.Center };
            _textoProgresso = Theme.Detalhe("");
            _textoProgresso.Margin = new Thickness(8, 0, 0, 0);
            _textoProgresso.TextWrapping = TextWrapping.NoWrap;
            var progresso = new DockPanel { Margin = new Thickness(12, 8, 12, 6) };
            DockPanel.SetDock(_textoProgresso, Dock.Right);
            progresso.Children.Add(_textoProgresso);
            progresso.Children.Add(trilho);
            trilho.SizeChanged += (s, e) => AtualizarBarra();

            // ---- aviso (modelo trocado, sem conferência)
            _textoAviso = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Theme.Solid(Theme.LaranjaTexto) };
            _aviso = new Border
            {
                Background = Theme.Solid(Theme.LaranjaSelecao),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(12, 0, 12, 6),
                Child = _textoAviso,
            };

            // ---- passos
            _passos = new StackPanel();
            var rolagem = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _passos };

            // ---- rodapé
            var conferir = new Button { Content = "Conferir", Style = Theme.PrimaryButton, MinWidth = 0, ToolTip = "Confere o modelo ativo com os padrões do Metrô e atualiza os passos." };
            conferir.Click += (s, e) => Revit(Conferir, conferirDoc: false);
            _pintar = new Button { Content = "Pintar pendências", MinWidth = 0, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Pinta de vermelho, na vista ativa, os elementos com pendência nos passos abertos (ou em todos, se nenhum estiver aberto)." };
            _pintar.Click += (s, e) => PintarOuLimpar();
            _soVista = new CheckBox { Content = "Só a vista ativa", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 6), ToolTip = "Confere só os elementos da vista ativa (3D, planta ou corte), em vez do modelo inteiro." };
            var completa = Theme.Link("Conferência completa");
            completa.ToolTip = "Abre a janela do Conferir Entrega, com a matriz categoria × campo, a exportação das pendências e as correções de classe IFC.";
            completa.Margin = new Thickness(10, 0, 0, 0);
            completa.Click += (s, e) => Revit(app => ConferirEntregaCommand.Abrir(app), conferirDoc: false);
            var botoes = new StackPanel { Orientation = Orientation.Horizontal };
            botoes.Children.Add(conferir);
            botoes.Children.Add(_pintar);
            botoes.Children.Add(completa);
            var rodapeConteudo = new StackPanel();
            rodapeConteudo.Children.Add(_soVista);
            rodapeConteudo.Children.Add(botoes);
            var rodape = new Border
            {
                Background = Theme.Solid(Theme.CinzaFundo),
                BorderBrush = Theme.Solid(Theme.CinzaClaro),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12, 8, 12, 10),
                Child = rodapeConteudo,
            };

            var raiz = new DockPanel();
            DockPanel.SetDock(faixa, Dock.Top);
            DockPanel.SetDock(progresso, Dock.Top);
            DockPanel.SetDock(_aviso, Dock.Top);
            DockPanel.SetDock(rodape, Dock.Bottom);
            raiz.Children.Add(faixa);
            raiz.Children.Add(progresso);
            raiz.Children.Add(_aviso);
            raiz.Children.Add(rodape);
            raiz.Children.Add(rolagem);
            Content = raiz;

            try
            {
                _pacote = PacoteMetro.Abrir();
                _nomePacote.Text = _pacote.NomeExibido;
                _soVista.IsChecked = _pacote.SoVistaAtiva;
            }
            catch { /* sem padrões ainda: escolhe ao conferir */ }
            Avisar("Clique em Conferir para ver o que falta para a entrega deste modelo.");
            MostrarPassos();
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = this;
            data.InitialState = new DockablePaneState { DockPosition = DockPosition.Right };
        }

        // ================================================================== chamado pelo Revit

        /// <summary>Mostra o painel e confere o modelo ativo. Chamar no contexto da API (comando).</summary>
        public static Result MostrarEConferir(UIApplication app)
        {
            DB.Document doc = app.ActiveUIDocument?.Document;
            if (doc == null || doc.IsFamilyDocument)
            {
                TaskDialog.Show("MetroSP", "Abra um projeto (.rvt) para conferir a entrega.");
                return Result.Cancelled;
            }
            try { app.GetDockablePane(Id)?.Show(); }
            catch (Exception ex)
            {
                TaskDialog.Show("MetroSP", "Não foi possível abrir o Painel MetroSP:\n" + ex.Message);
                return Result.Failed;
            }
            Instancia?.Conferir(app);
            return Result.Succeeded;
        }

        /// <summary>Outro modelo ficou ativo: o resultado mostrado deixa de valer para ele.</summary>
        internal void ModeloAtivo(DB.Document doc)
        {
            if (_doc == null || doc == null) return;
            if (!_doc.IsValidObject)
            {
                _doc = null;
                _res = null;
                Avisar("O modelo conferido foi fechado. Clique em Conferir para ver o modelo ativo.");
                MostrarPassos();
                return;
            }
            if (doc.IsFamilyDocument) return;
            Avisar(doc.Equals(_doc) ? null : $"Este resultado é do modelo \"{_doc.Title}\". Clique em Conferir para ver \"{doc.Title}\".");
        }

        // ================================================================== conferir

        private void Conferir(UIApplication app)
        {
            DB.Document doc = app.ActiveUIDocument?.Document;
            if (doc == null || doc.IsFamilyDocument)
            {
                TaskDialog.Show("MetroSP", "Abra um projeto (.rvt) para conferir a entrega.");
                return;
            }
            if (!EscolherPacote(app, false)) return;
            _pacote.SoVistaAtiva = _soVista.IsChecked == true;
            _pacote.SalvarComoAtual();
            _pacote.AplicarNasOutrasFerramentas();

            ResultadoConferencia r;
            try { r = ConferirEntregaWindow.Executar(app, doc, _pacote); }
            catch (Exception ex)
            {
                TaskDialog.Show("MetroSP", "Não foi possível conferir:\n" + ex.Message);
                return;
            }
            _doc = doc;
            _res = r;
            _local = SituacaoLocal(doc);
            Avisar(null);
            MostrarPassos();
        }

        /// <summary>Garante um pacote com arquivos; <paramref name="sempre"/> abre a escolha mesmo se já houver.</summary>
        private bool EscolherPacote(UIApplication app, bool sempre)
        {
            if (_pacote == null) _pacote = PacoteMetro.Abrir();
            bool vazio = !PacoteMetro.Existe(_pacote.PropertySets) && !PacoteMetro.Existe(_pacote.Ids);
            if (sempre || vazio)
            {
                var escolha = new PacoteWindow(_pacote, _pacote.Arquivo != null ? System.IO.Path.GetDirectoryName(_pacote.Arquivo) : null);
                new WindowInteropHelper(escolha).Owner = app.MainWindowHandle;
                if (escolha.ShowDialog() != true) return !vazio;
                _pacote = escolha.Pacote;
                _pacote.SalvarComoAtual();
                _pacote.AplicarNasOutrasFerramentas();
                if (sempre && _doc != null && _doc.IsValidObject) Conferir(app);
            }
            _nomePacote.Text = _pacote.NomeExibido;
            return true;
        }

        // ================================================================== mostrar

        private void Avisar(string texto)
        {
            _textoAviso.Text = texto ?? "";
            _aviso.Visibility = string.IsNullOrEmpty(texto) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void AtualizarBarra()
        {
            if (!(_barraProgresso.Parent is Border trilho)) return;
            double p = _res?.Progresso ?? 0;
            _barraProgresso.Width = Math.Max(0, trilho.ActualWidth * Math.Min(1, p));
        }

        private void MostrarPassos()
        {
            _passos.Children.Clear();
            if (_res == null)
            {
                _textoProgresso.Text = "";
                AtualizarBarra();
                _pintar.Content = TemPintura ? "Limpar pintura" : "Pintar pendências";
                return;
            }

            int prontos = _res.Etapas.Count(e => e.Sinal == Sinal.Verde || e.Sinal == Sinal.Cinza);
            _textoProgresso.Text = $"{prontos} de {_res.Etapas.Count} passos ok · {_res.Escopo}";
            _textoProgresso.ToolTip = $"{_res.TotalElementos} elementos conferidos, às {_res.Quando:HH:mm}. {Math.Round(_res.Progresso * 100)}% das verificações por elemento passaram.";
            AtualizarBarra();

            foreach (EtapaConferencia etapa in _res.Etapas) _passos.Children.Add(Passo(etapa));

            // Passos que não são conferências: abrir a ferramenta.
            // Ao fechar a janela do Projeto e Local, relê só este passo (sem reconferir o modelo todo).
            _passos.Children.Add(PassoAcao(_res.Etapas.Count + 1, "Projeto e Local", _local.Texto, "Abrir",
                app =>
                {
                    LocalizacaoIfcCommand.Abrir(app);
                    if (_doc != null && _doc.IsValidObject) { _local = SituacaoLocal(_doc); MostrarPassos(); }
                }, _local.Sinal));
            Sinal sinalExportar = _res.ProntoParaExportar ? (_res.Amarelas > 0 ? Sinal.Amarelo : Sinal.Verde) : Sinal.Cinza;
            string textoExportar = _res.ProntoParaExportar
                ? (_res.Amarelas > 0 ? "Pronto, com avisos." : "Tudo certo para exportar.")
                : $"Ainda há {_res.Vermelhas} passo(s) com pendência. Dá para exportar, mas o IFC sai incompleto.";
            _passos.Children.Add(PassoAcao(_res.Etapas.Count + 2, "Exportar IFC", textoExportar, "Exportar",
                app => { ExportarIfcCommand.Exportar(app); }, sinalExportar, primario: _res.ProntoParaExportar));

            _pintar.Content = TemPintura ? "Limpar pintura" : "Pintar pendências";
        }

        /// <summary>
        /// Verde quando o Location do Revit está no Project Base Point e o IfcProject, o IfcSite e o IfcBuilding têm nome.
        /// Chamar no contexto da API.
        /// </summary>
        private static (Sinal, string) SituacaoLocal(DB.Document doc)
        {
            Ids.LocalizacaoIfc l;
            try { l = Ids.LocalizacaoIfc.Calcular(doc); }
            catch (Exception ex) { return (Sinal.Amarelo, "Não foi possível ler a localização: " + ex.Message); }
            if (l.Erro != null) return (Sinal.Vermelho, "Não dá para localizar: " + l.Erro);
            var faltam = new List<string>();
            if (string.IsNullOrWhiteSpace(l.DadosAtuais.Projeto.Nome)) faltam.Add("nome do IfcProject");
            if (string.IsNullOrWhiteSpace(l.DadosAtuais.Terreno.Nome)) faltam.Add("nome do IfcSite");
            if (string.IsNullOrWhiteSpace(l.DadosAtuais.Edificio.Nome)) faltam.Add("nome do IfcBuilding");
            if (!l.LocationCorreto)
                return (Sinal.Amarelo, $"O Location do Revit está a {l.Diferenca:0.##} m do Project Base Point. Abra e confirme." +
                                       (faltam.Count > 0 ? " Falta também: " + string.Join(", ", faltam) + "." : ""));
            if (faltam.Count > 0) return (Sinal.Amarelo, "Location certo. Falta: " + string.Join(", ", faltam) + ".");
            return (Sinal.Verde, "Location no Project Base Point; IfcProject, IfcSite e IfcBuilding preenchidos.");
        }

        private bool TemPintura => _doc != null && _doc.IsValidObject && PinturaVista.TemPintura(_doc);

        private static Color CorDo(Sinal s) =>
            s == Sinal.Verde ? Theme.Verde
            : s == Sinal.Amarelo ? Color.FromRgb(0xC2, 0x74, 0x00)
            : s == Sinal.Vermelho ? Color.FromRgb(0xC0, 0x39, 0x2B)
            : Theme.Cinza;

        private static FrameworkElement Bolinha(int numero, Sinal s)
        {
            string texto = s == Sinal.Verde ? "✓" : s == Sinal.Amarelo ? "!" : numero.ToString();
            var g = new Grid { Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 10, 0) };
            g.Children.Add(new Ellipse { Fill = Theme.Solid(CorDo(s)) });
            g.Children.Add(new TextBlock
            {
                Text = texto,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
            return g;
        }

        private static List<DB.ElementId> ElementosDe(IEnumerable<Pendencia> pendencias) =>
            pendencias.Where(p => p.Sinal == Sinal.Vermelho || p.Sinal == Sinal.Amarelo)
                .SelectMany(p => p.Elementos).Distinct().ToList();

        private UIElement Passo(EtapaConferencia etapa)
        {
            bool aberto = _abertos.Contains(etapa.Numero);
            List<Pendencia> pendencias = etapa.Pendencias.Where(p => p.Sinal == Sinal.Vermelho || p.Sinal == Sinal.Amarelo)
                .OrderBy(p => p.Sinal == Sinal.Vermelho ? 0 : 1).ThenByDescending(p => p.QtdElementos).ToList();
            List<DB.ElementId> elementos = ElementosDe(pendencias);

            // Cabeçalho do passo (clicar abre e fecha).
            var cabecalho = new DockPanel { Margin = new Thickness(12, 8, 12, 8), Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
            var seta = new TextBlock { Text = pendencias.Count == 0 ? "" : aberto ? "▾" : "▸", Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Width = 10 };
            DockPanel.SetDock(seta, Dock.Right);
            cabecalho.Children.Add(seta);
            if (elementos.Count > 0)
            {
                var selecionar = new Button
                {
                    Content = $"Selecionar ({elementos.Count})",
                    Style = etapa.Sinal == Sinal.Vermelho ? Theme.PrimaryButton : null,
                    MinWidth = 0,
                    Padding = new Thickness(8, 2, 8, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "Seleciona no Revit todos os elementos com pendência neste passo.",
                };
                selecionar.Click += (s, e) => { e.Handled = true; Selecionar(elementos); };
                DockPanel.SetDock(selecionar, Dock.Right);
                cabecalho.Children.Add(selecionar);
            }
            Bolinha(etapa.Numero, etapa.Sinal).With(b => { DockPanel.SetDock(b, Dock.Left); cabecalho.Children.Add(b); });
            var textos = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textos.Children.Add(new TextBlock { Text = etapa.Titulo, FontWeight = FontWeights.SemiBold, ToolTip = etapa.Descricao });
            textos.Children.Add(new TextBlock
            {
                Text = etapa.Resumo ?? "",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Theme.Solid(etapa.Sinal == Sinal.Verde ? Color.FromRgb(0x55, 0x55, 0x55) : CorDo(etapa.Sinal)),
            });
            cabecalho.Children.Add(textos);

            var bloco = new StackPanel();
            bloco.Children.Add(cabecalho);
            if (pendencias.Count > 0)
            {
                cabecalho.MouseLeftButtonUp += (s, e) =>
                {
                    if (!_abertos.Remove(etapa.Numero)) _abertos.Add(etapa.Numero);
                    MostrarPassos();
                };
                if (aberto) bloco.Children.Add(Detalhes(etapa, pendencias));
            }

            return new Border
            {
                BorderBrush = Theme.Solid(Theme.CinzaClaro),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Background = aberto ? Theme.Solid(Color.FromRgb(0xFF, 0xFB, 0xF4)) : Brushes.White,
                Child = bloco,
            };
        }

        private UIElement Detalhes(EtapaConferencia etapa, List<Pendencia> pendencias)
        {
            const int Maximo = 40;
            var painel = new StackPanel { Margin = new Thickness(44, 0, 12, 10) };
            foreach (Pendencia p in pendencias.Take(Maximo))
            {
                var linha = new DockPanel { Margin = new Thickness(0, 0, 0, 1), Background = Brushes.Transparent };
                if (p.QtdElementos > 0)
                {
                    var sel = Theme.Link("selecionar");
                    sel.Margin = new Thickness(8, 0, 0, 0);
                    sel.Foreground = Theme.Solid(Color.FromRgb(0xC2, 0x74, 0x00));
                    sel.Click += (s, e) => Selecionar(p.Elementos);
                    DockPanel.SetDock(sel, Dock.Right);
                    linha.Children.Add(sel);
                    var qtd = new TextBlock { Text = p.QtdElementos.ToString(), FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                    DockPanel.SetDock(qtd, Dock.Right);
                    linha.Children.Add(qtd);
                }
                var ponto = new Ellipse { Width = 7, Height = 7, Fill = Theme.Solid(CorDo(p.Sinal)), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(ponto, Dock.Left);
                linha.Children.Add(ponto);
                string onde = string.IsNullOrEmpty(p.Categoria) ? p.Assunto : string.IsNullOrEmpty(p.Assunto) ? p.Categoria : p.Categoria + " · " + p.Assunto;
                linha.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrEmpty(p.Problema) ? onde : $"{onde}: {p.Problema}",
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = string.IsNullOrEmpty(p.OQueFazer) ? null : p.OQueFazer,
                });
                painel.Children.Add(linha);
            }
            if (pendencias.Count > Maximo)
                painel.Children.Add(Theme.Detalhe($"e mais {pendencias.Count - Maximo}. Veja todas na Conferência completa."));

            // Ações do passo.
            var acoes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            string ferramenta = NomeFerramenta(etapa.Correcao);
            if (ferramenta != null)
            {
                var abrir = new Button { Content = ferramenta, Style = Theme.PrimaryButton, MinWidth = 0, Padding = new Thickness(10, 3, 10, 3) };
                abrir.Click += (s, e) => AbrirFerramenta(etapa.Correcao, ElementosDe(pendencias));
                acoes.Children.Add(abrir);
            }
            var pintar = Theme.Link("Pintar na vista");
            pintar.Margin = new Thickness(ferramenta != null ? 12 : 0, 0, 0, 0);
            pintar.Click += (s, e) => Pintar(ElementosDe(pendencias));
            acoes.Children.Add(pintar);
            painel.Children.Add(acoes);
            return painel;
        }

        private UIElement PassoAcao(int numero, string titulo, string texto, string botao, Action<UIApplication> acao, Sinal sinal, bool primario = false)
        {
            var linha = new DockPanel { Margin = new Thickness(12, 8, 12, 8) };
            var b = new Button { Content = botao, MinWidth = 0, Padding = new Thickness(10, 2, 10, 2), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) };
            if (primario) b.Style = Theme.PrimaryButton;
            b.Click += (s, e) => Revit(app => { acao(app); });
            DockPanel.SetDock(b, Dock.Right);
            linha.Children.Add(b);
            FrameworkElement bola = Bolinha(numero, sinal);
            DockPanel.SetDock(bola, Dock.Left);
            linha.Children.Add(bola);
            var textos = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textos.Children.Add(new TextBlock { Text = titulo, FontWeight = FontWeights.SemiBold });
            textos.Children.Add(new TextBlock { Text = texto, FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Theme.Solid(Color.FromRgb(0x55, 0x55, 0x55)) });
            linha.Children.Add(textos);
            return new Border { BorderBrush = Theme.Solid(Theme.CinzaClaro), BorderThickness = new Thickness(0, 1, 0, 0), Child = linha };
        }

        // ================================================================== ações no Revit

        /// <summary>
        /// Roda uma ação no contexto da API. Com <paramref name="conferirDoc"/>, só se o modelo ativo for o conferido
        /// (as listas guardam elementos desse modelo).
        /// </summary>
        private void Revit(Action<UIApplication> acao, bool conferirDoc = true)
        {
            _fila.Enfileirar(app =>
            {
                if (conferirDoc && _doc != null)
                {
                    DB.Document ativo = app.ActiveUIDocument?.Document;
                    if (!_doc.IsValidObject)
                    {
                        TaskDialog.Show("MetroSP", "O modelo conferido foi fechado. Clique em Conferir.");
                        return;
                    }
                    if (ativo == null || !ativo.Equals(_doc))
                    {
                        TaskDialog.Show("MetroSP", $"Este resultado é do modelo \"{_doc.Title}\". Ative a janela dele ou clique em Conferir.");
                        return;
                    }
                }
                acao(app);
            });
            _evento.Raise();
        }

        private void Selecionar(List<DB.ElementId> ids)
        {
            if (ids == null || ids.Count == 0) return;
            Revit(app =>
            {
                UIDocument uidoc = app.ActiveUIDocument;
                List<DB.ElementId> validos = ids.Where(id => _doc.GetElement(id) != null).ToList();
                uidoc.Selection.SetElementIds(validos);
                try { uidoc.ShowElements(validos); }
                catch { /* nenhum visível nas vistas abertas: fica só a seleção */ }
            });
        }

        private void Pintar(List<DB.ElementId> ids)
        {
            if (ids == null || ids.Count == 0) return;
            Revit(app =>
            {
                List<DB.ElementId> validos = ids.Where(id => _doc.GetElement(id) != null).ToList();
                int n = PinturaVista.Pintar(app.ActiveUIDocument, validos, PinturaVista.Vermelho);
                if (n < 0) TaskDialog.Show("MetroSP", "A vista ativa não aceita pintar elementos. Abra uma vista 3D, planta ou corte.");
                _pintar.Content = TemPintura ? "Limpar pintura" : "Pintar pendências";
            });
        }

        private void PintarOuLimpar()
        {
            if (_doc == null || _res == null) return;
            if (TemPintura)
            {
                Revit(app =>
                {
                    PinturaVista.Limpar(_doc);
                    _pintar.Content = "Pintar pendências";
                });
                return;
            }
            // Passos abertos; se nenhum, todos.
            IEnumerable<EtapaConferencia> etapas = _res.Etapas.Where(e => _abertos.Contains(e.Numero));
            if (!etapas.Any()) etapas = _res.Etapas;
            Pintar(ElementosDe(etapas.SelectMany(e => e.Pendencias)));
        }

        private static string NomeFerramenta(Correcao c)
        {
            switch (c)
            {
                case Correcao.AplicarClassificacao: return "Aplicar classificação";
                case Correcao.VerificarClassificacao: return "Verificar classificação";
                case Correcao.ListarCompartilhados: return "Abrir Parâmetros";
                case Correcao.ParametrosDeProjeto: return "Abrir Parâmetros";
                case Correcao.CategoriasIfc: return "Categorias IFC";
                case Correcao.EditarPacote: return "Escolher padrões";
                case Correcao.EditarEmMassa: return "Editar em massa";
                default: return null;
            }
        }

        private void AbrirFerramenta(Correcao c, List<DB.ElementId> pendentes)
        {
            if (c == Correcao.EditarPacote)
            {
                Revit(app => EscolherPacote(app, true), conferirDoc: false);
                return;
            }
            Revit(app =>
            {
                UIDocument uidoc = app.ActiveUIDocument;
                void SelecionarPendentes()
                {
                    List<DB.ElementId> validos = pendentes.Where(id => _doc.GetElement(id) != null).ToList();
                    if (validos.Count > 0) uidoc.Selection.SetElementIds(validos);
                }
                switch (c)
                {
                    case Correcao.AplicarClassificacao:
                        SelecionarPendentes();
                        AplicarClassificacaoCommand.Abrir(app); // janela aberta enquanto se seleciona: conferir de novo depois
                        return;
                    case Correcao.VerificarClassificacao:
                        VerificarClassificacaoCommand.Abrir(app);
                        break;
                    case Correcao.ListarCompartilhados:
                        ListarCompartilhadosCommand.Abrir(app);
                        break;
                    case Correcao.ParametrosDeProjeto:
                        ListarCompartilhadosCommand.Abrir(app, ListarCompartilhadosCommand.Modelo);
                        break;
                    case Correcao.CategoriasIfc:
                    {
                        var janela = new Ids.CategoriasIfcWindow(_doc);
                        new WindowInteropHelper(janela).Owner = app.MainWindowHandle;
                        janela.ShowDialog();
                        break;
                    }
                    case Correcao.EditarEmMassa:
                    {
                        SelecionarPendentes();
                        var janela = new Dados.EdicaoMassaWindow(uidoc);
                        new WindowInteropHelper(janela).Owner = app.MainWindowHandle;
                        janela.ShowDialog();
                        break;
                    }
                    default:
                        return;
                }
                // Ferramentas modais: ao fechar, confere de novo.
                Conferir(app);
            });
        }

        /// <summary>Fila de ações executadas pelo Revit quando o ExternalEvent dispara.</summary>
        private class FilaRevit : IExternalEventHandler
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

            public string GetName() => "MetroSP: Painel MetroSP";
        }
    }

    internal static class ExtensoesPainel
    {
        /// <summary>Executa uma ação sobre o objeto e devolve ele (para montar a interface em sequência).</summary>
        public static T With<T>(this T obj, Action<T> acao)
        {
            acao(obj);
            return obj;
        }
    }
}
