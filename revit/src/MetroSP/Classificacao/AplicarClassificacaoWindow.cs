using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MetroSP.Ui;
using Grid = System.Windows.Controls.Grid;
using TextBox = System.Windows.Controls.TextBox;
using Color = System.Windows.Media.Color;
using ComboBox = System.Windows.Controls.ComboBox;

namespace MetroSP.Classificacao
{
    /// <summary>Resumo da seleção atual para o item escolhido (calculado no contexto da API).</summary>
    public class ResumoSelecao
    {
        public int Total { get; set; }
        public List<string> Categorias { get; set; } = new List<string>();
        public List<(string Situacao, int Elementos, string Atual)> Linhas { get; set; } = new List<(string, int, string)>();
        public int Gravaveis { get; set; }
        public int NoTipo { get; set; }
        public int EmGrupo { get; set; }
        public List<string> SemParametro { get; set; } = new List<string>();
    }

    /// <summary>
    /// Ações que precisam da API do Revit, disparadas pela janela não modal:
    /// recalcular o resumo da seleção e aplicar a classificação.
    /// </summary>
    public class AplicarHandler : IExternalEventHandler
    {
        public enum Acao { Resumo, Aplicar, Selecionar, Categorias, Vincular }
        public Acao Pedido { get; set; }
        public AplicarClassificacaoWindow Janela { get; set; }

        public void Execute(UIApplication app)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null || Janela == null) return;
            try
            {
                if (Pedido == Acao.Aplicar) Aplicar(uidoc);
                else if (Pedido == Acao.Selecionar) SelecionarPorCategoria(uidoc);
                else if (Pedido == Acao.Categorias) { Janela.DefinirCategorias(ContarCategorias(uidoc)); return; }
                else if (Pedido == Acao.Vincular) Vincular(uidoc);
                Janela.MostrarResumo(Resumir(uidoc));
            }
            catch (Exception ex)
            {
                TaskDialog.Show("MetroSP", "Erro na classificação:\n" + ex.Message);
            }
        }

        public string GetName() => "MetroSP: Aplicar classificação";

        private List<(Element E, AlvoClassificacao Alvo)> Selecionados(UIDocument uidoc)
        {
            Document doc = uidoc.Document;
            return ClassificationCore.Elementos(doc, uidoc.Selection.GetElementIds(), null)
                .Select(e => (e, ClassificationCore.Alvo(doc, e, Janela.Parametro)))
                .ToList();
        }

        private List<Element> NoEscopoDeSelecao(UIDocument uidoc)
        {
            Document doc = uidoc.Document;
            return ClassificationCore.Elementos(doc, null, Janela.SelecaoNaVista ? doc.ActiveView.Id : null);
        }

        public Dictionary<string, int> ContarCategorias(UIDocument uidoc) =>
            NoEscopoDeSelecao(uidoc).GroupBy(e => e.Category.Name).ToDictionary(g => g.Key, g => g.Count());

        /// <summary>Seleciona no Revit os elementos das categorias escolhidas (opcionalmente só os sem classificação).</summary>
        private void SelecionarPorCategoria(UIDocument uidoc)
        {
            Document doc = uidoc.Document;
            var ids = NoEscopoDeSelecao(uidoc)
                .Where(e => Janela.Categorias.Aceita(e.Category.Name))
                .Where(e => !Janela.SoSemClassificacao || string.IsNullOrWhiteSpace(ClassificationCore.Alvo(doc, e, Janela.Parametro).Valor))
                .Select(e => e.Id).ToList();
            uidoc.Selection.SetElementIds(ids);
            Janela.Status($"{ids.Count} elementos selecionados no Revit.");
        }

        private void Vincular(UIDocument uidoc)
        {
            Document doc = uidoc.Document;
            var cats = Selecionados(uidoc).Where(x => x.Alvo.Parametro == null).Select(x => x.E.Category.Name).Distinct();
            string msg = ParametroClassificacao.Vincular(doc, Janela.Parametro, ParametroClassificacao.Categorias(doc, cats));
            if (msg != null) TaskDialog.Show("MetroSP", msg);
        }

        public ResumoSelecao Resumir(UIDocument uidoc)
        {
            var sel = Selecionados(uidoc);
            ClassItem item = Janela.Item;
            string novo = item == null ? null : ValorClassificacao.Escrever(item.Codigo, item.Descricao);
            var r = new ResumoSelecao
            {
                Total = sel.Count,
                Categorias = sel.Select(x => x.E.Category.Name).Distinct().OrderBy(c => c).ToList(),
            };

            string Situacao((Element E, AlvoClassificacao Alvo) x)
            {
                if (x.Alvo.Parametro == null) return ClassificationCore.SemParametro;
                if (x.Alvo.Parametro.IsReadOnly) return "Somente leitura";
                if (item != null && !item.ValeParaCategoria(x.E.Category.Name) && !Janela.ForcarCategoria) return ClassificationCore.CategoriaNaoPrevista;
                if (string.IsNullOrWhiteSpace(x.Alvo.Valor)) return ClassificationCore.SemClassificacao;
                return x.Alvo.Valor == novo ? "Já igual" : "Será substituída";
            }

            // A situação de cada elemento é calculada uma vez só (antes era recalculada em cada contagem).
            var comSituacao = sel.Select(x => (x.E, x.Alvo, Sit: Situacao(x))).ToList();
            foreach (var g in comSituacao.GroupBy(x => x.Sit))
            {
                string atual = g.Key == "Será substituída" || g.Key == "Já igual"
                    ? string.Join(" | ", g.Select(x => x.Alvo.Valor).Distinct().Take(3))
                    : "";
                r.Linhas.Add((g.Key, g.Count(), atual));
            }
            var gravaveis = comSituacao.Where(x => x.Sit == ClassificationCore.SemClassificacao || x.Sit == "Será substituída").ToList();
            r.Gravaveis = gravaveis.Count;
            r.NoTipo = gravaveis.Where(x => x.Alvo.NoTipo).Select(x => x.Alvo.Dono.Id).Distinct().Count();
            r.SemParametro = sel.Where(x => x.Alvo.Parametro == null).Select(x => x.E.Category.Name).Distinct().OrderBy(c => c).ToList();
            r.EmGrupo = gravaveis.Count(x => !x.Alvo.NoTipo && x.E.GroupId != ElementId.InvalidElementId);
            return r;
        }

        private void Aplicar(UIDocument uidoc)
        {
            ClassItem item = Janela.Item;
            if (item == null) return;
            string novo = ValorClassificacao.Escrever(item.Codigo, item.Descricao);
            var alvos = Selecionados(uidoc)
                .Where(x => x.Alvo.Parametro != null && !x.Alvo.Parametro.IsReadOnly && x.Alvo.Valor != novo &&
                            (Janela.ForcarCategoria || item.ValeParaCategoria(x.E.Category.Name)))
                .Select(x => (x.Alvo.Dono.Id, novo))
                .ToList();
            if (alvos.Count == 0) return;
            ResultadoGravacao r = ClassificationCore.Gravar(uidoc.Document, Janela.Parametro, alvos, "Aplicar classificação " + item.Codigo, Perguntar);
            Janela.Status($"{item.Codigo} aplicado em {r.Ok} {(r.Ok == 1 ? "elemento/tipo" : "elementos/tipos")}" +
                          (r.Falhas.Count > 0 ? $"; {r.Falhas.Count} não gravados" : "") + ". Ctrl+Z desfaz.");
            if (r.Falhas.Count > 0 || r.GrupoLiberado || r.Avisos.Count > 0)
            {
                var td = new TaskDialog("MetroSP") { MainInstruction = "Aplicar classificação", MainContent = r.Resumo() };
                if (r.Falhas.Count > 0)
                {
                    td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Selecionar os que não foram gravados");
                    td.CommonButtons = TaskDialogCommonButtons.Close;
                }
                if (td.Show() == TaskDialogResult.CommandLink1)
                    uidoc.Selection.SetElementIds(r.Falhas.Select(f => f.Id).ToList());
            }
        }

        private static bool Perguntar(string pergunta)
        {
            var td = new TaskDialog("MetroSP")
            {
                MainInstruction = "Elementos em grupos",
                MainContent = pergunta,
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.Yes,
            };
            return td.Show() == TaskDialogResult.Yes;
        }
    }

    /// <summary>
    /// Janela não modal: o projetista escolhe o item na árvore da planilha e aplica nos elementos
    /// selecionados no Revit. A seleção pode mudar com a janela aberta.
    /// </summary>
    public class AplicarClassificacaoWindow : Window
    {
        private readonly ClassSettings _config;
        private readonly ExternalEvent _evento;
        private readonly AplicarHandler _handler;
        private readonly PlanilhaSeletor _planilha;
        private readonly TextBox _busca;
        private readonly CheckBox _soCategorias;
        private readonly CheckBox _forcar;
        private readonly ComboBox _escopoSelecao;
        private readonly CheckBox _soSem;

        public SeletorCategorias Categorias { get; } = new SeletorCategorias { Margin = new Thickness(0, 0, 8, 0) };
        public bool SelecaoNaVista => _escopoSelecao.SelectedIndex == 0;
        public bool SoSemClassificacao => _soSem.IsChecked == true;
        private readonly TreeView _arvore;
        private readonly StackPanel _detalhe;
        private readonly StackPanel _resumo;
        private readonly TextBlock _status;
        private readonly Button _aplicar;
        private ClassificationList _lista;
        /// <summary>Código + descrição normalizados de cada item (para a busca não recalcular a cada tecla).</summary>
        private Dictionary<ClassItem, string> _textoBusca = new Dictionary<ClassItem, string>();
        /// <summary>A busca espera uma pausa na digitação antes de remontar a árvore.</summary>
        private readonly System.Windows.Threading.DispatcherTimer _esperaBusca =
            new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        /// <summary>true enquanto a árvore é remontada: as trocas de seleção nesse meio não contam.</summary>
        private bool _montandoArvore;
        private List<string> _categoriasSelecao = new List<string>();

        public ClassItem Item { get; private set; }
        public string Parametro => _config.Parametro;
        public bool ForcarCategoria => _forcar.IsChecked == true;

        public AplicarClassificacaoWindow(ClassSettings config, UIDocument uidoc)
        {
            _config = config;
            _handler = new AplicarHandler { Janela = this };
            _evento = ExternalEvent.Create(_handler);

            Title = "MetroSP - Aplicar classificação";
            Width = 1100;
            Height = 700;
            MinWidth = 760;
            MinHeight = 420;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _planilha = new PlanilhaSeletor(config);
            _planilha.Carregada += l => { _lista = l; _textoBusca = new Dictionary<ClassItem, string>(); MontarArvore(); };

            // Esquerda: busca e árvore.
            _busca = new TextBox { ToolTip = "Procure por código ou descrição (sem diferenciar acentos)." };
            _esperaBusca.Tick += (s, e) => { _esperaBusca.Stop(); MontarArvore(); };
            _busca.TextChanged += (s, e) => { _esperaBusca.Stop(); _esperaBusca.Start(); };
            _soCategorias = new CheckBox { Content = "Mostrar só o que vale para as categorias selecionadas", IsChecked = true, Margin = new Thickness(0, 6, 0, 6) };
            _soCategorias.Click += (s, e) => MontarArvore();
            _arvore = new TreeView { BorderBrush = Theme.Solid(Theme.CinzaClaro) };
            _arvore.SelectedItemChanged += (s, e) =>
            {
                if (_montandoArvore) return; // a árvore está sendo remontada (ex.: busca): o item é conferido no fim
                var novo = (_arvore.SelectedItem as TreeViewItem)?.Tag as ClassItem;
                if (novo == Item && novo != null) return; // mesmo item (remontagem da árvore): não refaz o resumo
                Item = novo;
                MostrarDetalhe();
                PedirResumo();
            };
            var esquerda = new DockPanel();
            var procurar = new TextBlock { Text = "Procurar", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
            foreach (UIElement el in new UIElement[] { procurar, _busca, _soCategorias })
            {
                DockPanel.SetDock(el, Dock.Top);
                esquerda.Children.Add(el);
            }
            esquerda.Children.Add(_arvore);

            // Direita: item escolhido e resumo da seleção.
            _detalhe = new StackPanel();
            _resumo = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            _forcar = new CheckBox { Content = "Aplicar também em categorias não previstas para o item", Margin = new Thickness(0, 10, 0, 0) };
            _forcar.Click += (s, e) => PedirResumo();
            // Selecionar no Revit por categoria (sem fechar a janela).
            _escopoSelecao = new ComboBox { Padding = new Thickness(4, 3, 4, 3), Margin = new Thickness(0, 0, 8, 0) };
            _escopoSelecao.Items.Add("Vista atual");
            _escopoSelecao.Items.Add("Todo o modelo");
            _escopoSelecao.SelectedIndex = 0;
            _escopoSelecao.SelectionChanged += (s, e) => Pedir(AplicarHandler.Acao.Categorias);
            _soSem = new CheckBox { Content = "Só os sem classificação", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var selecionar = new Button { Content = "Selecionar", Padding = new Thickness(10, 2, 10, 2) };
            selecionar.Click += (s, e) => Pedir(AplicarHandler.Acao.Selecionar);
            var linhaSel = new WrapPanel();
            foreach (FrameworkElement el in new FrameworkElement[] { _escopoSelecao, Categorias, _soSem, selecionar })
            {
                el.Margin = new Thickness(0, 2, 8, 2);
                linhaSel.Children.Add(el);
            }
            var caixaSel = new StackPanel();
            caixaSel.Children.Add(new TextBlock { Text = "Selecionar no Revit por categoria", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            caixaSel.Children.Add(linhaSel);
            var blocoSel = new Border
            {
                Background = Theme.Solid(Theme.CinzaFundo),
                BorderBrush = Theme.Solid(Theme.CinzaClaro),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 0, 12),
                Child = caixaSel,
            };

            var direita = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
            direita.Children.Add(blocoSel);
            direita.Children.Add(_detalhe);
            direita.Children.Add(_resumo);
            direita.Children.Add(_forcar);

            var corpo = new Grid();
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });
            corpo.Children.Add(esquerda);
            var divisor = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent };
            Grid.SetColumn(divisor, 1);
            corpo.Children.Add(divisor);
            var rolagem = new ScrollViewer { Content = direita, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetColumn(rolagem, 2);
            corpo.Children.Add(rolagem);

            var tudo = new DockPanel();
            DockPanel.SetDock(_planilha, Dock.Top);
            tudo.Children.Add(_planilha);
            tudo.Children.Add(corpo);

            _status = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Text = "A janela fica aberta: selecione elementos no Revit e aplique." };
            _aplicar = new Button { Content = "Aplicar", Style = Theme.PrimaryButton, IsEnabled = false };
            _aplicar.Click += (s, e) => { _handler.Pedido = AplicarHandler.Acao.Aplicar; _evento.Raise(); };
            var atualizar = new Button { Content = "Atualizar seleção" };
            atualizar.Click += (s, e) => PedirResumo();
            var fechar = new Button { Content = "Fechar", IsCancel = true };
            fechar.Click += (s, e) => Close();
            var rodape = new DockPanel();
            StackPanel botoes = Theme.Buttons(atualizar, _aplicar, fechar);
            DockPanel.SetDock(botoes, Dock.Right);
            rodape.Children.Add(botoes);
            rodape.Children.Add(_status);

            Theme.Apply(this, "Aplicar classificação", $"grava em {config.Parametro} no formato Código:Descrição", tudo, rodape);
            Closed += (s, e) => _evento.Dispose();

            _planilha.Carregar();
            MostrarDetalhe();
            DefinirCategorias(_handler.ContarCategorias(uidoc));
        }

        private void Pedir(AplicarHandler.Acao acao)
        {
            _handler.Pedido = acao;
            _evento.Raise();
        }

        public void DefinirCategorias(Dictionary<string, int> contagem) => Categorias.Definir(contagem);

        /// <summary>Chamado pelo comando quando a seleção do Revit muda (contexto da API).</summary>
        public void SelecaoMudou(UIApplication app)
        {
            UIDocument uidoc = app?.ActiveUIDocument;
            if (!IsLoaded || uidoc == null || uidoc.Document.IsFamilyDocument) return;
            try { MostrarResumo(_handler.Resumir(uidoc)); }
            catch { /* seleção em transição (troca de vista ou documento) */ }
        }

        private void PedirResumo()
        {
            _handler.Pedido = AplicarHandler.Acao.Resumo;
            _evento.Raise();
        }

        public void Status(string texto) => _status.Text = texto;

        // ------------------------------------------------------------------ árvore

        private void MontarArvore()
        {
            _esperaBusca.Stop();
            ClassItem anterior = Item;
            ClassItem reselecionado = null;
            _montandoArvore = true;
            try
            {
                _arvore.Items.Clear();
                if (_lista == null) return;
                string busca = ClassificationCore.Normalizar(_busca.Text);
                bool filtrando = busca.Length > 0;
                bool porCategoria = _soCategorias.IsChecked == true && _categoriasSelecao.Count > 0;
                var fonte = new FontFamily("Consolas");
                Brush cinza = Theme.Solid(Theme.Cinza);

                string Texto(ClassItem i)
                {
                    if (!_textoBusca.TryGetValue(i, out string t)) _textoBusca[i] = t = ClassificationCore.Normalizar(i.Codigo + " " + i.Descricao);
                    return t;
                }

                bool Combina(ClassItem i) =>
                    (!filtrando || Texto(i).Contains(busca)) && (!porCategoria || _categoriasSelecao.Any(i.ValeParaCategoria));

                TreeViewItem No(ClassItem i)
                {
                    var filhos = i.Filhos.Select(No).Where(n => n != null).ToList();
                    bool combina = Combina(i);
                    if (!combina && filhos.Count == 0) return null;
                    var header = new StackPanel { Orientation = Orientation.Horizontal };
                    header.Children.Add(new TextBlock { Text = i.Codigo, FontFamily = fonte, Foreground = cinza, Margin = new Thickness(0, 0, 8, 0) });
                    header.Children.Add(new TextBlock { Text = i.Descricao, Opacity = combina ? 1 : 0.55 });
                    var n = new TreeViewItem { Header = header, Tag = i, IsExpanded = filtrando || i.Pai == null };
                    foreach (TreeViewItem f in filhos) n.Items.Add(f);
                    if (anterior != null && i.Codigo == anterior.Codigo) { n.IsSelected = true; reselecionado = i; }
                    return n;
                }

                foreach (ClassItem r in _lista.Raizes)
                {
                    TreeViewItem n = No(r);
                    if (n != null) _arvore.Items.Add(n);
                }
            }
            finally
            {
                _montandoArvore = false;
                // Só pede um novo resumo ao Revit se o item escolhido mudou (antes, cada tecla na busca refazia o resumo).
                Item = reselecionado;
                if (Item != anterior) { MostrarDetalhe(); PedirResumo(); }
            }
        }

        // ------------------------------------------------------------------ painel direito

        private void MostrarDetalhe()
        {
            _detalhe.Children.Clear();
            _detalhe.Children.Add(new TextBlock { Text = "Item escolhido", FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(0, 0, 0, 6) });
            if (Item == null)
            {
                _detalhe.Children.Add(new TextBlock { Text = _lista == null ? "Escolha a planilha acima." : "Escolha um item na árvore.", Foreground = Theme.Solid(Theme.Cinza) });
                _aplicar.IsEnabled = false;
                return;
            }
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            void Linha(string k, string v, bool forte = false)
            {
                int r = g.RowDefinitions.Count;
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var a = new TextBlock { Text = k, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 8, 2) };
                var b = new TextBlock { Text = v, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2), FontWeight = forte ? FontWeights.SemiBold : FontWeights.Normal };
                Grid.SetRow(a, r); Grid.SetRow(b, r); Grid.SetColumn(b, 1);
                g.Children.Add(a); g.Children.Add(b);
            }
            Linha("Código", Item.Codigo);
            Linha("Descrição", Item.Descricao);
            if (Item.Pai != null) Linha("Caminho", Item.Caminho);
            Linha("Categorias", Item.Categorias.Count == 0 ? "todas" : string.Join(", ", Item.Categorias));
            Linha("Vai gravar", $"{Parametro} = {ValorClassificacao.Escrever(Item.Codigo, Item.Descricao)}", true);
            _detalhe.Children.Add(g);
            if (Item.Filhos.Count > 0)
                _detalhe.Children.Add(new TextBlock
                {
                    Text = "Este item tem subitens; normalmente se aplica o nível mais detalhado.",
                    Foreground = Theme.Solid(Color.FromRgb(0x8A, 0x45, 0x00)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 6, 0, 0),
                });
        }

        /// <summary>Atualiza o resumo da seleção (chamado no contexto da API).</summary>
        public void MostrarResumo(ResumoSelecao r)
        {
            bool mudouCategorias = !r.Categorias.SequenceEqual(_categoriasSelecao);
            _categoriasSelecao = r.Categorias;
            if (mudouCategorias && _soCategorias.IsChecked == true) MontarArvore();

            _resumo.Children.Clear();
            _resumo.Children.Add(new TextBlock
            {
                Text = r.Total == 0 ? "Nenhum elemento selecionado no Revit." : $"Seleção no Revit: {r.Total} elementos ({string.Join(", ", r.Categorias)})",
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6),
            });
            if (r.Total > 0 && Item != null)
            {
                var g = new DataGrid
                {
                    AutoGenerateColumns = false,
                    IsReadOnly = true,
                    HeadersVisibility = DataGridHeadersVisibility.Column,
                    CanUserAddRows = false,
                    ItemsSource = r.Linhas.Select(l => new { l.Situacao, l.Elementos, l.Atual }).ToList(),
                };
                g.Columns.Add(new DataGridTextColumn { Header = "Situação", Binding = new System.Windows.Data.Binding("Situacao"), Width = new DataGridLength(150) });
                g.Columns.Add(new DataGridTextColumn { Header = "Elementos", Binding = new System.Windows.Data.Binding("Elementos"), Width = new DataGridLength(80) });
                g.Columns.Add(new DataGridTextColumn { Header = "Classificação atual", Binding = new System.Windows.Data.Binding("Atual"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                _resumo.Children.Add(g);
                if (r.NoTipo > 0)
                    _resumo.Children.Add(new TextBlock
                    {
                        Text = $"{Parametro} é de tipo em parte dos elementos: {r.NoTipo} tipos serão alterados (vale para todas as instâncias deles).",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = Theme.Solid(Color.FromRgb(0x8A, 0x45, 0x00)),
                        Margin = new Thickness(0, 6, 0, 0),
                    });
            }
            if (r.Total > 0 && Item != null && r.EmGrupo > 0)
                _resumo.Children.Add(new TextBlock
                {
                    Text = $"{r.EmGrupo} elementos estão dentro de grupos. Se {Parametro} estiver alinhado por tipo de grupo, ao aplicar o MetroSP pergunta se pode liberá-lo para variar por instância de grupo (os grupos não são desfeitos).",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Theme.Solid(Color.FromRgb(0x8A, 0x45, 0x00)),
                    Margin = new Thickness(0, 6, 0, 0),
                });
            if (r.SemParametro.Count > 0)
            {
                var criar = new Button
                {
                    Content = $"Criar {Parametro} em {string.Join(", ", r.SemParametro)}",
                    ToolTip = "Vincula o parâmetro às categorias selecionadas que não o têm (usa o GUID da base Metrô / .txt de referência).",
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 8, 0, 0),
                    Padding = new Thickness(10, 2, 10, 2),
                };
                criar.Click += (s, e) => Pedir(AplicarHandler.Acao.Vincular);
                _resumo.Children.Add(criar);
            }
            _aplicar.IsEnabled = Item != null && r.Gravaveis > 0;
            _aplicar.Content = r.Gravaveis > 0 ? $"Aplicar em {r.Gravaveis} elementos" : "Aplicar";
        }
    }
}
