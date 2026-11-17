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
        /// <summary>Contagem por categoria pedida pelo clique no botão Categorias (não se perde se outro pedido vier antes).</summary>
        public bool ContarPendente { get; set; }
        public AplicarClassificacaoWindow Janela { get; set; }

        public void Execute(UIApplication app)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null || Janela == null) return;
            try
            {
                if (ContarPendente)
                {
                    ContarPendente = false;
                    Janela.Status("Contando os elementos por categoria...");
                    Dictionary<string, int> contagem = ContarCategorias(uidoc);
                    Janela.Status($"{contagem.Values.Sum()} elementos em {contagem.Count} categorias ({(Janela.SelecaoNaVista ? "vista atual" : "todo o modelo")}).");
                    Janela.DefinirCategorias(contagem, abrir: true);
                    if (Pedido == Acao.Categorias) return;
                }
                if (Pedido == Acao.Aplicar) Aplicar(uidoc);
                else if (Pedido == Acao.Selecionar) SelecionarPorCategoria(uidoc);
                else if (Pedido == Acao.Categorias) return;
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
            string parametro = Janela.Parametro;
            return ClassificationCore.Elementos(doc, uidoc.Selection.GetElementIds(), null)
                .Select(e => (e, parametro == null ? new AlvoClassificacao { Elemento = e } : ClassificationCore.Alvo(doc, e, parametro)))
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
                .Where(e => !Janela.SoSemClassificacao || Janela.Parametro == null || string.IsNullOrWhiteSpace(ClassificationCore.Alvo(doc, e, Janela.Parametro).Valor))
                .Select(e => e.Id).ToList();
            uidoc.Selection.SetElementIds(ids);
            Janela.Status($"{ids.Count} elementos selecionados no Revit.");
        }

        private void Vincular(UIDocument uidoc)
        {
            Document doc = uidoc.Document;
            if (Janela.Parametro == null) return;
            var cats = Selecionados(uidoc).Where(x => x.Alvo.Parametro == null).Select(x => x.E.Category.Name).Distinct();
            string msg = ParametroClassificacao.Vincular(doc, Janela.Parametro, ParametroClassificacao.Categorias(doc, cats));
            if (msg != null) TaskDialog.Show("MetroSP", msg);
        }

        public ResumoSelecao Resumir(UIDocument uidoc)
        {
            var sel = Selecionados(uidoc);
            ClassItem item = Janela.Item;
            SistemaClassificacao sis = Janela.Sistema;
            string novo = item == null || sis == null ? null : AplicarClassificacaoWindow.ValorCodigo(sis, item);
            var r = new ResumoSelecao
            {
                Total = sel.Count,
                Categorias = sel.Select(x => x.E.Category.Name).Distinct().OrderBy(c => c).ToList(),
            };

            string Situacao((Element E, AlvoClassificacao Alvo) x)
            {
                if (x.Alvo.Parametro == null) return ClassificationCore.SemParametro;
                if (x.Alvo.Parametro.IsReadOnly) return "Somente leitura";
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

        /// <summary>Grava, em cada elemento selecionado, o item escolhido de cada classificação do perfil.</summary>
        private void Aplicar(UIDocument uidoc)
        {
            Document doc = uidoc.Document;
            var escolhas = Janela.Escolhas();
            if (escolhas.Count == 0) return;
            List<Element> elems = ClassificationCore.Elementos(doc, uidoc.Selection.GetElementIds(), null);
            var gravar = new List<(ElementId Dono, string Parametro, string Valor)>();
            foreach (Element e in elems)
                foreach (var (sis, item) in escolhas)
                {
                    void Gravar(string parametro, string valor)
                    {
                        if (string.IsNullOrWhiteSpace(parametro) || valor == null) return;
                        AlvoClassificacao a = ClassificationCore.Alvo(doc, e, parametro.Trim());
                        if (a.Parametro == null || a.Parametro.IsReadOnly || a.Valor == valor) return;
                        gravar.Add((a.Dono.Id, parametro.Trim(), valor));
                    }
                    Gravar(sis.ParametroCodigo, AplicarClassificacaoWindow.ValorCodigo(sis, item));
                    if (sis.Formato == FormatoClassificacao.Separados && item.Descricao.Length > 0) Gravar(sis.ParametroDescricao, item.Descricao);
                    if (!string.IsNullOrEmpty(item.DescricaoLonga)) Gravar(sis.ParametroDescricaoLonga, item.DescricaoLonga);
                }
            if (gravar.Count == 0) { Janela.Status("Nada a gravar: os elementos já estão com esses valores (ou sem o parâmetro)."); return; }
            string nomes = string.Join(", ", escolhas.Select(x => x.Item.Codigo));
            ResultadoGravacao r = ClassificationCore.Gravar(doc, gravar, "Aplicar classificação " + nomes, Perguntar);
            Janela.Status($"{nomes} aplicado: {r.Ok} {(r.Ok == 1 ? "gravação" : "gravações")}" +
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
    /// Janela não modal: o projetista escolhe o perfil de classificação e, em cada aba (uma por classificação),
    /// o item na árvore da planilha; Aplicar grava todos nos elementos selecionados no Revit.
    /// A seleção pode mudar com a janela aberta.
    /// </summary>
    public class AplicarClassificacaoWindow : Window
    {
        private readonly ClassSettings _config;
        private readonly Document _doc;
        private readonly ExternalEvent _evento;
        private readonly AplicarHandler _handler;
        private readonly ComboBox _perfil;
        private readonly TextBlock _perfilInfo;
        private readonly StackPanel _abas;
        private readonly TextBox _busca;
        private readonly ComboBox _escopoSelecao;
        private readonly CheckBox _soSem;
        private PadraoClassificacao _padrao;
        private bool _trocandoPerfil;
        /// <summary>Item escolhido em cada classificação do perfil.</summary>
        private readonly Dictionary<SistemaClassificacao, ClassItem> _escolhidos = new Dictionary<SistemaClassificacao, ClassItem>();

        public SeletorCategorias Categorias { get; } = new SeletorCategorias { Margin = new Thickness(0, 0, 8, 0) };
        public bool SelecaoNaVista => _escopoSelecao.SelectedIndex == 0;
        public bool SoSemClassificacao => _soSem.IsChecked == true;
        private readonly TreeView _arvore;
        private readonly StackPanel _detalhe;
        private readonly StackPanel _resumo;
        private readonly TextBlock _status;
        private readonly Button _aplicar;
        private ClassificationList _lista;
        /// <summary>Código + título normalizados de cada item (para a busca não recalcular a cada tecla).</summary>
        private Dictionary<ClassItem, string> _textoBusca = new Dictionary<ClassItem, string>();
        /// <summary>A busca espera uma pausa na digitação antes de remontar a árvore.</summary>
        private readonly System.Windows.Threading.DispatcherTimer _esperaBusca =
            new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        /// <summary>true enquanto a árvore é remontada: as trocas de seleção nesse meio não contam.</summary>
        private bool _montandoArvore;

        /// <summary>Classificação da aba aberta.</summary>
        public SistemaClassificacao Sistema { get; private set; }
        /// <summary>Item escolhido na aba aberta.</summary>
        public ClassItem Item { get; private set; }
        /// <summary>Parâmetro do código da aba aberta (o resumo da seleção é sobre ele).</summary>
        public string Parametro => string.IsNullOrWhiteSpace(Sistema?.ParametroCodigo) ? null : Sistema.ParametroCodigo.Trim();

        /// <summary>O que vai no parâmetro do código: "Código:Título", "[Nome]Código:Título" ou só o código.</summary>
        public static string ValorCodigo(SistemaClassificacao s, ClassItem item) =>
            s.Formato == FormatoClassificacao.Separados ? item.Codigo : ValorClassificacao.Escrever(item.Codigo, item.Descricao, s.GravarNome ? s.Nome : null);

        /// <summary>Classificações com item escolhido, na ordem do perfil.</summary>
        public List<(SistemaClassificacao Sis, ClassItem Item)> Escolhas() =>
            _padrao == null ? new List<(SistemaClassificacao, ClassItem)>()
                : _padrao.Sistemas.Where(s => _escolhidos.TryGetValue(s, out ClassItem i) && i != null && !string.IsNullOrWhiteSpace(s.ParametroCodigo))
                    .Select(s => (s, _escolhidos[s])).ToList();

        public AplicarClassificacaoWindow(ClassSettings config, UIDocument uidoc)
        {
            _config = config;
            _doc = uidoc.Document;
            _handler = new AplicarHandler { Janela = this };
            _evento = ExternalEvent.Create(_handler);

            Title = "MetroSP - Aplicar classificação";
            Width = 1100;
            Height = 700;
            MinWidth = 760;
            MinHeight = 420;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // Perfil e classificações (abas).
            var barra = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            barra.Children.Add(Theme.Rotulo("Perfil"));
            _perfil = new ComboBox
            {
                MinWidth = 220, Margin = new Thickness(0, 0, 8, 0), IsEditable = true, IsTextSearchEnabled = true,
                ToolTip = "Perfil de classificação: as classificações, a planilha e o parâmetro de cada uma. Digite para procurar.",
            };
            TextSearch.SetTextPath(_perfil, nameof(PadraoClassificacao.Nome));
            _perfil.SelectionChanged += (s, e) => { if (!_trocandoPerfil && _perfil.SelectedItem is PadraoClassificacao p) UsarPerfil(p); };
            barra.Children.Add(_perfil);
            var editar = new Button { Content = "Classificação...", Margin = new Thickness(0, 0, 8, 0), ToolTip = "Perfis de classificação: planilha, colunas, parâmetro e mais de uma classificação." };
            editar.Click += (s, e) => EditarPerfis(uidoc.Document);
            barra.Children.Add(editar);
            _perfilInfo = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            barra.Children.Add(_perfilInfo);
            _abas = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

            // Esquerda: busca e árvore.
            _busca = new TextBox { ToolTip = "Procure por código ou título (sem diferenciar acentos)." };
            _esperaBusca.Tick += (s, e) => { _esperaBusca.Stop(); MontarArvore(); };
            _busca.TextChanged += (s, e) => { _esperaBusca.Stop(); _esperaBusca.Start(); };
            _arvore = new TreeView { BorderBrush = Theme.Solid(Theme.CinzaClaro), Margin = new Thickness(0, 6, 0, 0) };
            // Planilhas grandes: só os itens visíveis na tela são desenhados.
            VirtualizingPanel.SetIsVirtualizing(_arvore, true);
            VirtualizingPanel.SetVirtualizationMode(_arvore, VirtualizationMode.Standard);
            _arvore.SelectedItemChanged += (s, e) =>
            {
                if (_montandoArvore) return; // a árvore está sendo remontada (ex.: busca): o item é conferido no fim
                var novo = (_arvore.SelectedItem as TreeViewItem)?.Tag as ClassItem;
                if (novo == Item && novo != null) return; // mesmo item (remontagem da árvore): não refaz o resumo
                Escolher(novo);
            };
            var esquerda = new DockPanel();
            var procurar = new TextBlock { Text = "Procurar", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
            foreach (UIElement el in new UIElement[] { procurar, _busca })
            {
                DockPanel.SetDock(el, Dock.Top);
                esquerda.Children.Add(el);
            }
            esquerda.Children.Add(_arvore);

            // Direita: o que vai gravar e resumo da seleção.
            _detalhe = new StackPanel();
            _resumo = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            // Selecionar no Revit por categoria (sem fechar a janela).
            _escopoSelecao = new ComboBox { Padding = new Thickness(4, 3, 4, 3), Margin = new Thickness(0, 0, 8, 0) };
            _escopoSelecao.Items.Add("Vista atual");
            _escopoSelecao.Items.Add("Todo o modelo");
            _escopoSelecao.SelectedIndex = 0;
            // Trocar o escopo não varre o modelo: a contagem é refeita quando a lista de categorias for aberta.
            _escopoSelecao.SelectionChanged += (s, e) => Categorias.Invalidar();
            Categorias.ToolTip = "Escolha as categorias a selecionar. Os elementos são contados ao abrir a lista.";
            Categorias.PedirContagem = () => { _handler.ContarPendente = true; Pedir(AplicarHandler.Acao.Categorias); };
            _soSem = new CheckBox { Content = "Só os sem classificação", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), ToolTip = "Sem nada no parâmetro da aba aberta." };
            var selecionar = new Button { Content = "Selecionar", Padding = new Thickness(10, 2, 10, 2) };
            selecionar.Click += (s, e) => Pedir(AplicarHandler.Acao.Selecionar);
            var linhaSel = new WrapPanel();
            foreach (FrameworkElement el in new FrameworkElement[] { _escopoSelecao, Categorias, _soSem, selecionar })
            {
                el.Margin = new Thickness(0, 2, 8, 2);
                linhaSel.Children.Add(el);
            }
            var caixaSel = new StackPanel();
            caixaSel.Children.Add(new TextBlock { Text = "Selecionar no Revit", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
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
            DockPanel.SetDock(barra, Dock.Top);
            DockPanel.SetDock(_abas, Dock.Top);
            tudo.Children.Add(barra);
            tudo.Children.Add(_abas);
            tudo.Children.Add(corpo);

            _status = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Text = "A janela fica aberta: selecione elementos no Revit e aplique." };
            _aplicar = new Button { Content = "Aplicar", Style = Theme.PrimaryButton, IsEnabled = false, ToolTip = "Grava o item escolhido de cada classificação nos elementos selecionados (Ctrl+Z desfaz)." };
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

            Theme.Apply(this, "Aplicar classificação", "escolha um item em cada classificação e aplique na seleção · só elementos que vão para o IFC", tudo, rodape);
            Closed += (s, e) => _evento.Dispose();

            // Abrir rápido: nada de varrer o modelo antes de mostrar a janela. As planilhas do perfil são lidas depois
            // que a janela aparece (fora da tela, e só uma vez enquanto o arquivo não mudar) e as categorias são contadas
            // só quando o projetista abre a lista de categorias.
            MostrarDetalhe();
            ContentRendered += PrimeiroDesenho;
        }

        private void PrimeiroDesenho(object sender, EventArgs e)
        {
            ContentRendered -= PrimeiroDesenho;
            CarregarPerfis(_config.Padrao);
        }

        // ------------------------------------------------------------------ perfis e abas

        private void CarregarPerfis(string escolher)
        {
            List<PadraoClassificacao> perfis = PadraoClassificacao.Listar();
            if (perfis.Count == 0)
            {
                // Primeiro uso: a planilha e o parâmetro que a classificação já usava viram o primeiro perfil.
                PadraoClassificacao p = PadraoClassificacao.DoLegado(_config);
                try { p.Salvar(); } catch { }
                perfis.Add(p);
            }
            _trocandoPerfil = true;
            _perfil.ItemsSource = perfis;
            _perfil.SelectedItem = perfis.FirstOrDefault(p => p.Nome == escolher) ?? perfis[0];
            _trocandoPerfil = false;
            UsarPerfil(_perfil.SelectedItem as PadraoClassificacao);
        }

        /// <summary>Lê as planilhas do perfil fora da tela; a árvore aparece quando a leitura termina.</summary>
        private async void UsarPerfil(PadraoClassificacao p)
        {
            _padrao = p;
            _escolhidos.Clear();
            Sistema = null;
            Item = null;
            _lista = null;
            _arvore.Items.Clear();
            MostrarAbas();
            MostrarDetalhe();
            if (p == null) return;
            _config.Padrao = p.Nome;
            try { _config.Salvar(); } catch { }
            _perfilInfo.Text = "lendo as planilhas...";
            try
            {
                await System.Threading.Tasks.Task.Run(() =>
                {
                    var cache = new Dictionary<string, Planilha.Tabela>(StringComparer.OrdinalIgnoreCase);
                    foreach (SistemaClassificacao s in p.Sistemas) s.Carregar(cache);
                });
            }
            catch (Exception ex) { _perfilInfo.Text = "erro ao ler: " + ex.Message; return; }
            if (_padrao != p) return; // trocou de perfil enquanto lia
            _perfilInfo.Text = string.Join("  ·  ", p.Sistemas.Select(s => s.Lista != null && s.Erro == null ? $"{s.Nome}: {s.Lista.Itens.Count} itens" : $"{s.Nome}: {s.Erro}"));
            _perfilInfo.ToolTip = string.Join("\n", p.Sistemas.Select(s => $"{s.Nome}  ·  {s.Parametros}  ·  {s.Planilha?.NomeArquivo}"));
            _perfilInfo.Foreground = Theme.Solid(p.Sistemas.Any(s => s.Lista == null || s.Erro != null) ? Theme.LaranjaTexto : Theme.Cinza);
            AbrirAba(p.Sistemas.FirstOrDefault(s => s.Lista != null) ?? p.Sistemas.FirstOrDefault());
        }

        private void EditarPerfis(Document doc)
        {
            var w = new PadroesClassificacaoWindow(doc, _padrao?.Nome) { Owner = this };
            w.ShowDialog();
            CarregarPerfis(w.Escolhido ?? _padrao?.Nome);
        }

        private void AbrirAba(SistemaClassificacao s)
        {
            Sistema = s;
            _lista = s?.Lista;
            _textoBusca = new Dictionary<ClassItem, string>();
            Item = s != null && _escolhidos.TryGetValue(s, out ClassItem i) ? i : null;
            MostrarAbas();
            MontarArvore();
            MostrarDetalhe();
            PedirResumo();
        }

        /// <summary>Uma aba por classificação; ✓ quando já tem item escolhido.</summary>
        private void MostrarAbas()
        {
            _abas.Children.Clear();
            if (_padrao == null) return;
            foreach (SistemaClassificacao s in _padrao.Sistemas)
            {
                bool atual = s == Sistema;
                bool escolhido = _escolhidos.TryGetValue(s, out ClassItem it) && it != null;
                var aba = new Border
                {
                    BorderBrush = Theme.Solid(atual ? Theme.Laranja : Theme.CinzaClaro),
                    BorderThickness = new Thickness(0, atual ? 3 : 1, 0, 0),
                    Background = atual ? Brushes.White : Theme.Solid(Theme.CinzaFundo),
                    Padding = new Thickness(12, 5, 12, 5),
                    Margin = new Thickness(0, 0, 2, 0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = s.Lista == null ? $"{s.Nome}: {s.Erro ?? "lendo..."}" : $"{s.Parametros}" + (escolhido ? $"\nEscolhido: {it.Codigo} {it.Descricao}" : ""),
                    Child = new TextBlock
                    {
                        Text = (escolhido ? "✓ " : "") + s.Nome,
                        FontWeight = atual ? FontWeights.SemiBold : FontWeights.Normal,
                        Opacity = s.Lista == null ? 0.5 : 1,
                    },
                };
                SistemaClassificacao sis = s;
                aba.MouseLeftButtonUp += (o, e) => { if (Sistema != sis) AbrirAba(sis); };
                _abas.Children.Add(aba);
            }
        }

        private void Escolher(ClassItem item)
        {
            Item = item;
            if (Sistema != null)
            {
                if (item == null) _escolhidos.Remove(Sistema);
                else _escolhidos[Sistema] = item;
            }
            MostrarAbas();
            MostrarDetalhe();
            PedirResumo();
        }

        private void Pedir(AplicarHandler.Acao acao)
        {
            _handler.Pedido = acao;
            _evento.Raise();
        }

        public void DefinirCategorias(Dictionary<string, int> contagem, bool abrir = false) => Categorias.Definir(contagem, abrir);

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
                var fonte = new FontFamily("Consolas");
                Brush cinza = Theme.Solid(Theme.Cinza);

                string Texto(ClassItem i)
                {
                    if (!_textoBusca.TryGetValue(i, out string t)) _textoBusca[i] = t = ClassificationCore.Normalizar(i.Codigo + " " + i.Descricao);
                    return t;
                }

                bool Combina(ClassItem i) => !filtrando || Texto(i).Contains(busca);

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
                    if (anterior != null && i.Codigo == anterior.Codigo)
                    {
                        n.IsSelected = true;
                        reselecionado = i;
                    }
                    if (filhos.Any(ContemSelecionado)) n.IsExpanded = true; // abre o caminho até o item escolhido
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
                // A busca pode esconder o item escolhido: ele continua escolhido nesta classificação.
                if (reselecionado != null) Item = reselecionado;
            }
        }

        private static bool ContemSelecionado(TreeViewItem n) =>
            n.IsSelected || n.Items.OfType<TreeViewItem>().Any(ContemSelecionado);

        // ------------------------------------------------------------------ painel direito

        private void MostrarDetalhe()
        {
            _detalhe.Children.Clear();
            _detalhe.Children.Add(new TextBlock { Text = "Vai gravar", FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(0, 0, 0, 6) });
            if (_padrao == null || Sistema == null)
            {
                _detalhe.Children.Add(new TextBlock { Text = "Escolha o perfil acima.", Foreground = Theme.Solid(Theme.Cinza) });
                _aplicar.IsEnabled = false;
                return;
            }
            if (_lista == null)
            {
                _detalhe.Children.Add(new TextBlock { Text = $"{Sistema.Nome}: {Sistema.Erro ?? "lendo a planilha..."}. Ajuste em \"Classificação...\".", Foreground = Theme.Solid(Theme.Cinza), TextWrapping = TextWrapping.Wrap });
            }

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            void Linha(string a, string b, string c, bool cinza = false, bool cabecalho = false)
            {
                int r = g.RowDefinitions.Count;
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                string[] t = { a, b, c };
                for (int k = 0; k < 3; k++)
                {
                    var tb = new TextBlock
                    {
                        Text = t[k],
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 2, 12, 2),
                        FontWeight = cabecalho ? FontWeights.SemiBold : FontWeights.Normal,
                        Foreground = cinza ? Theme.Solid(Theme.Cinza) : Brushes.Black,
                        FontFamily = k == 2 && !cabecalho ? new FontFamily("Consolas") : null,
                    };
                    if (tb.FontFamily == null) tb.ClearValue(TextBlock.FontFamilyProperty);
                    Grid.SetRow(tb, r);
                    Grid.SetColumn(tb, k);
                    g.Children.Add(tb);
                }
            }
            Linha("Classificação", "Parâmetro", "Valor", cabecalho: true);
            foreach (SistemaClassificacao s in _padrao.Sistemas)
            {
                if (!_escolhidos.TryGetValue(s, out ClassItem it) || it == null) { Linha(s.Nome, s.ParametroCodigo, "nada escolhido", cinza: true); continue; }
                Linha(s.Nome, s.ParametroCodigo, ValorCodigo(s, it));
                if (s.Formato == FormatoClassificacao.Separados && !string.IsNullOrWhiteSpace(s.ParametroDescricao) && it.Descricao.Length > 0)
                    Linha(s.Nome, s.ParametroDescricao, it.Descricao);
                if (!string.IsNullOrWhiteSpace(s.ParametroDescricaoLonga) && !string.IsNullOrEmpty(it.DescricaoLonga))
                    Linha(s.Nome, s.ParametroDescricaoLonga, it.DescricaoLonga);
            }
            _detalhe.Children.Add(g);

            if (Item != null)
            {
                if (Item.Pai != null)
                    _detalhe.Children.Add(new TextBlock { Text = Item.Caminho, Foreground = Theme.Solid(Theme.Cinza), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), ToolTip = "Caminho na classificação" });
                if (Item.Filhos.Count > 0)
                    _detalhe.Children.Add(new TextBlock
                    {
                        Text = "Este item tem subitens; normalmente se aplica o nível mais detalhado.",
                        Foreground = Theme.Solid(Color.FromRgb(0x8A, 0x45, 0x00)),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 6, 0, 0),
                    });
                var limpar = Theme.Link($"Tirar {Sistema.Nome} desta aplicação");
                limpar.Margin = new Thickness(0, 6, 0, 0);
                limpar.HorizontalAlignment = HorizontalAlignment.Left;
                limpar.Click += (s, e) => { _arvore.Items.OfType<TreeViewItem>().ToList().ForEach(Desmarcar); Escolher(null); };
                _detalhe.Children.Add(limpar);
            }
        }

        private static void Desmarcar(TreeViewItem n)
        {
            n.IsSelected = false;
            foreach (TreeViewItem f in n.Items.OfType<TreeViewItem>()) Desmarcar(f);
        }

        /// <summary>Atualiza o resumo da seleção (chamado no contexto da API). A situação é a da aba aberta.</summary>
        public void MostrarResumo(ResumoSelecao r)
        {
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
                _resumo.Children.Add(new TextBlock { Text = $"Como está hoje em {Parametro}", Foreground = Theme.Solid(Theme.Cinza), Margin = new Thickness(0, 0, 0, 4) });
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
                g.Columns.Add(new DataGridTextColumn { Header = "Valor atual", Binding = new System.Windows.Data.Binding("Atual"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
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
            if (r.SemParametro.Count > 0 && Parametro != null)
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
            int n = Escolhas().Count;
            _aplicar.IsEnabled = n > 0 && r.Total > 0;
            _aplicar.Content = n == 0 || r.Total == 0 ? "Aplicar" : n == 1 ? $"Aplicar em {r.Total} elementos" : $"Aplicar {n} classificações em {r.Total} elementos";
        }
    }
}
