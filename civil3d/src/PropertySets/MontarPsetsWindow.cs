using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Autodesk.AutoCAD.DatabaseServices;
using MetroSP.Ui;
using Microsoft.Win32;
using AcDocument = Autodesk.AutoCAD.ApplicationServices.Document;
using Grid = System.Windows.Controls.Grid;

namespace MetroSP.PropertySets
{
    /// <summary>
    /// Monta os property sets do Civil 3D (Style Manager › Property Set Definitions): lista de montagem com nome,
    /// propriedades, tipos e a quais objetos se aplica; importa/exporta planilha; cria/completa no desenho; leva ao
    /// mapeamento do exportador IFC; e anexa aos objetos que vão para o IFC.
    /// </summary>
    public class MontarPsetsWindow : Window
    {
        private readonly AcDocument _doc;
        private readonly Database _db;
        private readonly List<ObjectId> _selecao;

        // Montar
        private readonly ObservableCollection<PsetMontagem> _psets = new ObservableCollection<PsetMontagem>();
        private readonly FilterableGrid _tabelaPsets;
        private readonly StackPanel _detalhe;
        private readonly TextBox _nome, _descricao;
        private readonly RadioButton _todos, _alguns;
        private readonly ListBox _classes;
        private readonly CheckBox _levarIfc;
        private readonly ComboBox _entidadeIfc;
        private readonly DataGrid _props;
        private readonly TextBlock _resumo;
        private PsetMontagem _atual;
        private bool _preenchendo;

        // Anexar
        private readonly WrapPanel _escolhaPsets;
        private readonly ComboBox _escopo;
        private readonly DockPanel _areaObjetos;
        private readonly TextBlock _infoAnexar;
        private List<ObjetoAnexar> _objetos;
        private FilterableGrid _tabelaObjetos;
        private readonly HashSet<string> _escolhidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly TabControl _abas;
        private readonly Button _criar, _anexar;

        public MontarPsetsWindow(AcDocument doc, List<ObjectId> selecao)
        {
            _doc = doc;
            _db = doc.Database;
            _selecao = selecao != null && selecao.Count > 0 ? selecao : null;

            Title = "MetroSP - Property sets";
            Width = 1180;
            Height = 760;
            MinWidth = 860;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // ============================================================ aba Montar
            var barra = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            Button Acao(string texto, string dica, Action a)
            {
                var b = new Button { Content = texto, ToolTip = dica, Margin = new Thickness(0, 0, 6, 4) };
                b.Click += (s, e) => a();
                barra.Children.Add(b);
                return b;
            }
            Acao("+ Property set", "Acrescenta um property set vazio à lista.", NovoPset);
            Acao("Tirar da lista", "Tira o property set da lista. Não apaga do desenho (para isso, use o Style Manager).", TirarPset);
            barra.Children.Add(new Border { Width = 12 });
            Acao("Importar planilha...", "Cria a estrutura a partir de uma planilha (uma linha por propriedade). Você indica a coluna de cada informação:\n" +
                "Property set, Propriedade, Tipo, Valor padrão, Aplica-se a, Descrição e IFC. Os valores dos objetos não são preenchidos.\n" +
                "Também lê o IFCInfraExportPropertyMapping.csv da Autodesk (linhas com Source = PropertySet).", Importar);
            Acao("Exportar planilha...", "Salva a lista em .csv para editar no Excel, levar a outro desenho ou mandar para a equipe.", Exportar);
            Acao("Do mapeamento IFC", "Traz os property sets que o mapeamento do exportador IFC (Configurar IFC) espera encontrar no desenho.", DoMapeamento);
            var modelos = new Button { Content = "Modelo pronto ▾", ToolTip = "Property sets prontos para começar.", Margin = new Thickness(0, 0, 6, 4) };
            var menu = new ContextMenu();
            foreach (var (rotulo, dica, criar) in PsetsCore.Modelos())
            {
                var item = new MenuItem { Header = rotulo, ToolTip = dica };
                item.Click += (s, e) => Acrescentar(new[] { criar() }, rotulo);
                menu.Items.Add(item);
            }
            modelos.Click += (s, e) => { menu.PlacementTarget = modelos; menu.IsOpen = true; };
            barra.Children.Add(modelos);
            Acao("Reler o desenho", "Descarta o que não foi criado e lê de novo os property sets do desenho.", () => Carregar(true));

            _tabelaPsets = new FilterableGrid(_psets, typeof(PsetMontagem));
            _tabelaPsets.AddColumn("Property set", nameof(PsetMontagem.Nome), 200, fill: true);
            _tabelaPsets.AddColumn("Situação", nameof(PsetMontagem.Situacao), 170);
            _tabelaPsets.AddColumn("Props.", nameof(PsetMontagem.NumeroPropriedades), 60);
            _tabelaPsets.AddColumn("Aplica-se a", nameof(PsetMontagem.AplicaA), 150);
            _tabelaPsets.Grid.SelectionMode = DataGridSelectionMode.Single;
            _tabelaPsets.Grid.SelectionChanged += (s, e) => Mostrar(_tabelaPsets.Grid.SelectedItem as PsetMontagem);
            _tabelaPsets.Grid.LoadingRow += (s, e) =>
            {
                var p = (PsetMontagem)e.Row.Item;
                e.Row.Background = p.Situacao == "No desenho" ? Theme.LinhaNormal : Theme.LinhaProblema;
            };
            Theme.StyleSelection(_tabelaPsets.Grid);

            // Detalhe
            _detalhe = new StackPanel();
            _nome = new TextBox { ToolTip = "Nome do property set no desenho (e do Pset no IFC)." };
            _nome.TextChanged += (s, e) => { if (Editando()) { _atual.Nome = _nome.Text.Trim(); Alterou(); } };
            _descricao = new TextBox { ToolTip = "Aparece no Style Manager." };
            _descricao.TextChanged += (s, e) => { if (Editando()) { _atual.Descricao = _descricao.Text; Alterou(); } };
            _detalhe.Children.Add(DuasColunas(Campo("Nome", _nome), Campo("Descrição", _descricao)));

            _todos = new RadioButton { Content = "Todos os objetos", GroupName = "aplica", Margin = new Thickness(0, 0, 16, 0) };
            _alguns = new RadioButton { Content = "Só estes tipos", GroupName = "aplica", ToolTip = "O property set só pode ser anexado aos tipos marcados." };
            _todos.Checked += (s, e) => { if (Editando()) { _atual.TodosObjetos = true; _classes.IsEnabled = false; Alterou(); } };
            _alguns.Checked += (s, e) => { if (Editando()) { _atual.TodosObjetos = false; _classes.IsEnabled = true; Alterou(); } };
            var linhaAplica = new StackPanel { Orientation = Orientation.Horizontal };
            linhaAplica.Children.Add(_todos);
            linhaAplica.Children.Add(_alguns);
            _detalhe.Children.Add(Campo("Aplica-se a", linhaAplica));
            _classes = new ListBox { Height = 104, Margin = new Thickness(0, 6, 0, 0) };
            ScrollViewer.SetHorizontalScrollBarVisibility(_classes, ScrollBarVisibility.Disabled);
            _classes.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(WrapPanel)));
            _detalhe.Children.Add(_classes);

            _levarIfc = new CheckBox
            {
                Content = "Levar para o IFC",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
                ToolTip = "Ao criar, acrescenta o Pset ao mapeamento do exportador IFC (Configurar IFC › Property sets), com a origem PropertySet.",
            };
            _levarIfc.Click += (s, e) => { if (Editando()) { _atual.LevarIfc = _levarIfc.IsChecked == true; _entidadeIfc.IsEnabled = _atual.LevarIfc; Alterou(); } };
            _entidadeIfc = new ComboBox { IsEditable = true, MinWidth = 160, ToolTip = "Entidade IFC a que o Pset se aplica (ApplicableEntities)." };
            foreach (string s in new[] { "IfcElement", "IfcProduct", "IfcAlignment", "IfcCourse", "IfcEarthworksElement", "IfcPipeSegment", "IfcSpatialElement" })
                _entidadeIfc.Items.Add(s);
            _entidadeIfc.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((s, e) =>
            {
                if (Editando()) { _atual.EntidadeIfc = _entidadeIfc.Text.Trim(); Alterou(); }
            }));
            var linhaIfc = new StackPanel { Orientation = Orientation.Horizontal };
            linhaIfc.Children.Add(_levarIfc);
            linhaIfc.Children.Add(_entidadeIfc);
            _detalhe.Children.Add(Campo("IFC", linhaIfc));

            var tituloProps = Theme.Label("Propriedades");
            tituloProps.ToolTip = "Clique na célula para editar. Propriedades que já estão no desenho não mudam de tipo (os valores se perderiam).";
            _detalhe.Children.Add(tituloProps);

            _props = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserSortColumns = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                SelectionMode = DataGridSelectionMode.Extended,
            };
            _props.Columns.Add(new DataGridTextColumn { Header = "Propriedade", Binding = new Binding(nameof(PropriedadeMontagem.Nome)) { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }, Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            _props.Columns.Add(new DataGridComboBoxColumn
            {
                Header = "Tipo",
                ItemsSource = PsetsCore.NomesTipos,
                SelectedItemBinding = new Binding(nameof(PropriedadeMontagem.TipoTexto)),
                Width = 90,
            });
            _props.Columns.Add(new DataGridTextColumn { Header = "Valor padrão", Binding = new Binding(nameof(PropriedadeMontagem.Padrao)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            _props.Columns.Add(new DataGridTextColumn { Header = "Situação", Binding = new Binding(nameof(PropriedadeMontagem.Situacao)), IsReadOnly = true, Width = new DataGridLength(1.3, DataGridLengthUnitType.Star) });
            _props.CellEditEnding += (s, e) => Dispatcher.BeginInvoke(new Action(Alterou), System.Windows.Threading.DispatcherPriority.Background);
            _props.BeginningEdit += (s, e) =>
            {
                if (e.Row.Item is PropriedadeMontagem p && p.Automatica) e.Cancel = true;
            };
            Theme.StyleSelection(_props);

            var botoesProps = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            var maisProp = new Button { Content = "+ Propriedade" };
            maisProp.Click += (s, e) => NovaPropriedade();
            var menosProp = new Button { Content = "Tirar", Margin = new Thickness(6, 0, 0, 0), ToolTip = "Tira as propriedades selecionadas da lista. Não apaga do desenho." };
            menosProp.Click += (s, e) => TirarPropriedades();
            botoesProps.Children.Add(maisProp);
            botoesProps.Children.Add(menosProp);

            var direita = new DockPanel { Margin = new Thickness(14, 0, 0, 0) };
            DockPanel.SetDock(_detalhe, Dock.Top);
            direita.Children.Add(_detalhe);
            DockPanel.SetDock(botoesProps, Dock.Bottom);
            direita.Children.Add(botoesProps);
            direita.Children.Add(_props);

            var miolo = new Grid();
            miolo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });
            miolo.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            miolo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star), MinWidth = 380 });
            Grid.SetColumn(_tabelaPsets, 0);
            miolo.Children.Add(_tabelaPsets);
            var divisor = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch, Background = Theme.Solid(Theme.CinzaClaro), ShowsPreview = false };
            Grid.SetColumn(divisor, 1);
            miolo.Children.Add(divisor);
            Grid.SetColumn(direita, 2);
            miolo.Children.Add(direita);

            var montar = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            DockPanel.SetDock(barra, Dock.Top);
            montar.Children.Add(barra);
            montar.Children.Add(miolo);

            // ============================================================ aba Anexar
            var anexar = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var linhaEscopo = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            linhaEscopo.Children.Add(Theme.Rotulo("Objetos"));
            _escopo = new ComboBox { MinWidth = 220, Margin = new Thickness(0, 0, 12, 0), ToolTip = "Só objetos que vão para o IFC (layers ligados, fora das anotações e do que a exportação exclui)." };
            if (_selecao != null) _escopo.Items.Add($"Seleção atual ({_selecao.Count} objetos)");
            _escopo.Items.Add("Todo o desenho");
            _escopo.SelectedIndex = 0;
            _escopo.SelectionChanged += (s, e) => { if (IsLoaded) LerObjetos(); };
            linhaEscopo.Children.Add(_escopo);
            var reler = new Button { Content = "Ler objetos", ToolTip = "Lê de novo os objetos e os property sets anexados." };
            reler.Click += (s, e) => LerObjetos();
            linhaEscopo.Children.Add(reler);
            DockPanel.SetDock(linhaEscopo, Dock.Top);
            anexar.Children.Add(linhaEscopo);

            var rotEscolha = Theme.Label("Property sets a anexar");
            rotEscolha.ToolTip = "Só os que já estão no desenho. Cada um vai só para os tipos de objeto a que se aplica.";
            DockPanel.SetDock(rotEscolha, Dock.Top);
            anexar.Children.Add(rotEscolha);
            _escolhaPsets = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(_escolhaPsets, Dock.Top);
            anexar.Children.Add(_escolhaPsets);
            _areaObjetos = new DockPanel();
            anexar.Children.Add(_areaObjetos);

            _abas = new TabControl();
            _abas.Items.Add(new TabItem { Header = "Montar", Content = montar, ToolTip = "Nome, propriedades e tipos de objeto de cada property set." });
            _abas.Items.Add(new TabItem { Header = "Anexar aos objetos", Content = anexar, ToolTip = "Anexa os property sets aos objetos que vão para o IFC." });
            _abas.SelectionChanged += (s, e) => { if (e.Source == _abas) TrocouAba(); };

            // ============================================================ rodapé
            _resumo = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            _infoAnexar = _resumo;
            _criar = new Button { Content = "Criar no desenho", Style = Theme.PrimaryButton, ToolTip = "Cria os property sets novos e completa os que mudaram. Nada é apagado do desenho." };
            _criar.Click += (s, e) => Criar();
            _anexar = new Button { Content = "Anexar", Style = Theme.PrimaryButton, Visibility = System.Windows.Visibility.Collapsed, ToolTip = "Anexa o que falta aos objetos visíveis na tabela (use os filtros das colunas para escolher)." };
            _anexar.Click += (s, e) => Anexar();
            var fechar = new Button { Content = "Fechar", IsCancel = true };
            fechar.Click += (s, e) => Close();
            var rodape = Theme.Rodape(_resumo, _criar, _anexar, fechar);

            Theme.Apply(this, "Property sets", Path.GetFileName(doc.Name) + " · monta os property sets do Civil 3D e anexa aos objetos", _abas, rodape);
            Carregar(false);
        }

        // ------------------------------------------------------------------ layout

        static UIElement Campo(string rotulo, UIElement conteudo)
        {
            var p = new StackPanel();
            p.Children.Add(Theme.Label(rotulo));
            p.Children.Add(conteudo);
            return p;
        }

        static UIElement DuasColunas(UIElement a, UIElement b)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            Grid.SetColumn(a, 0);
            Grid.SetColumn(b, 2);
            g.Children.Add(a);
            g.Children.Add(b);
            return g;
        }

        bool Editando() => !_preenchendo && _atual != null;

        // ------------------------------------------------------------------ montar

        private void Carregar(bool perguntar)
        {
            if (perguntar && _psets.Any(p => p.Situacao != "No desenho") &&
                MessageBox.Show(this, "O que ainda não foi criado no desenho sai da lista. Continuar?", Title, MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;
            try
            {
                _psets.Clear();
                foreach (PsetMontagem p in PsetsCore.LerDesenho(_db)) _psets.Add(p);
                Atualizar();
                if (_psets.Count > 0) _tabelaPsets.Grid.SelectedIndex = 0; else Mostrar(null);
            }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível ler os property sets do desenho:\n" + ex.Message, Title); }
        }

        /// <summary>Compara com o desenho e atualiza tabela e rodapé.</summary>
        private void Atualizar()
        {
            try { PsetsCore.Comparar(_db, _psets); } catch { }
            _tabelaPsets.Refresh();
            _tabelaPsets.Grid.Items.Refresh();
            _props.Items.Refresh();
            int novos = _psets.Count(p => p.Situacao == "Novo");
            int muda = _psets.Count(p => p.Situacao.StartsWith("Muda"));
            int ifc = _psets.Count(p => p.LevarIfc);
            _criar.IsEnabled = novos + muda > 0 || ifc > 0;
            if (_abas == null || _abas.SelectedIndex == 0)
            {
                _resumo.Text = novos + muda == 0
                    ? $"{_psets.Count} property set(s) · tudo igual ao desenho" + (ifc > 0 ? $" · {ifc} para levar ao IFC" : "")
                    : $"{_psets.Count} property set(s) · {novos} novo(s) · {muda} com mudança" + (ifc > 0 ? $" · {ifc} para levar ao IFC" : "");
                _resumo.Foreground = Theme.Solid(novos + muda == 0 ? Theme.Verde : Theme.LaranjaTexto);
            }
        }

        private void Alterou()
        {
            var sel = _atual;
            Atualizar();
            if (sel != null && _tabelaPsets.Grid.SelectedItem != sel) _tabelaPsets.Grid.SelectedItem = sel;
        }

        private void Mostrar(PsetMontagem p)
        {
            _atual = p;
            _preenchendo = true;
            try
            {
                _detalhe.IsEnabled = p != null;
                _props.IsEnabled = p != null;
                _nome.Text = p?.Nome ?? "";
                _descricao.Text = p?.Descricao ?? "";
                _todos.IsChecked = p == null || p.TodosObjetos;
                _alguns.IsChecked = p != null && !p.TodosObjetos;
                _levarIfc.IsChecked = p?.LevarIfc == true;
                _entidadeIfc.Text = p?.EntidadeIfc ?? "IfcElement";
                _entidadeIfc.IsEnabled = p?.LevarIfc == true;
                PreencherClasses(p);
                _props.ItemsSource = p?.Propriedades;
            }
            finally { _preenchendo = false; }
        }

        private List<string> _classesDesenho;

        private void PreencherClasses(PsetMontagem p)
        {
            if (_classesDesenho == null)
            {
                try { _classesDesenho = PsetsCore.Objetos(_db, null).Select(o => o.Classe).Distinct().ToList(); }
                catch { _classesDesenho = new List<string>(); }
            }
            _classes.Items.Clear();
            _classes.IsEnabled = p != null && !p.TodosObjetos;
            var marcadas = new HashSet<string>(p?.Classes ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var opcoes = PsetsCore.OpcoesClasses(_classesDesenho.Concat(marcadas));
            foreach (var (rx, nome) in opcoes)
            {
                var c = new CheckBox { Content = nome, Tag = rx, IsChecked = marcadas.Contains(rx), Width = 170, Margin = new Thickness(0, 2, 0, 2), ToolTip = rx };
                c.Click += (s, e) =>
                {
                    if (!Editando()) return;
                    if (c.IsChecked == true) { if (!_atual.Classes.Contains(rx, StringComparer.OrdinalIgnoreCase)) _atual.Classes.Add(rx); }
                    else _atual.Classes.RemoveAll(x => string.Equals(x, rx, StringComparison.OrdinalIgnoreCase));
                    Alterou();
                };
                _classes.Items.Add(c);
            }
        }

        private void NovoPset()
        {
            string nome = "Novo property set";
            for (int i = 2; _psets.Any(p => string.Equals(p.Nome, nome, StringComparison.OrdinalIgnoreCase)); i++) nome = "Novo property set " + i;
            var p = new PsetMontagem { Nome = nome, Propriedades = { new PropriedadeMontagem { Nome = "Propriedade" } } };
            _psets.Add(p);
            Atualizar();
            _tabelaPsets.Grid.SelectedItem = p;
            _nome.Focus();
            _nome.SelectAll();
        }

        private void TirarPset()
        {
            if (_atual == null) return;
            _psets.Remove(_atual);
            Atualizar();
            if (_psets.Count > 0) _tabelaPsets.Grid.SelectedIndex = 0; else Mostrar(null);
        }

        private void NovaPropriedade()
        {
            if (_atual == null) return;
            var pr = new PropriedadeMontagem { Nome = "Propriedade " + (_atual.Propriedades.Count + 1) };
            _atual.Propriedades.Add(pr);
            Alterou();
            _props.SelectedItem = pr;
            _props.ScrollIntoView(pr);
            _props.CurrentCell = new DataGridCellInfo(pr, _props.Columns[0]);
            _props.BeginEdit();
        }

        private void TirarPropriedades()
        {
            if (_atual == null) return;
            foreach (PropriedadeMontagem pr in _props.SelectedItems.Cast<object>().OfType<PropriedadeMontagem>().ToList()) _atual.Propriedades.Remove(pr);
            Alterou();
        }

        /// <summary>Junta property sets à lista: os de mesmo nome ganham as propriedades que faltam.</summary>
        private void Acrescentar(IEnumerable<PsetMontagem> novos, string origem)
        {
            int ps = 0, props = 0;
            PsetMontagem ultimo = null;
            foreach (PsetMontagem n in novos)
            {
                PsetMontagem e = _psets.FirstOrDefault(x => string.Equals(x.Nome, n.Nome, StringComparison.OrdinalIgnoreCase));
                if (e == null) { _psets.Add(n); ps++; props += n.Propriedades.Count; ultimo = n; continue; }
                foreach (PropriedadeMontagem pr in n.Propriedades)
                    if (!e.Propriedades.Any(x => string.Equals(x.Nome, pr.Nome, StringComparison.OrdinalIgnoreCase))) { e.Propriedades.Add(pr); props++; }
                if (!n.TodosObjetos && !e.TodosObjetos)
                    foreach (string c in n.Classes) if (!e.Classes.Contains(c, StringComparer.OrdinalIgnoreCase)) e.Classes.Add(c);
                if (n.LevarIfc) { e.LevarIfc = true; e.EntidadeIfc = n.EntidadeIfc; }
                if (string.IsNullOrEmpty(e.Descricao)) e.Descricao = n.Descricao;
                ultimo = e;
            }
            Atualizar();
            if (ultimo != null) _tabelaPsets.Grid.SelectedItem = ultimo;
            _resumo.Text = $"{origem}: {ps} property set(s) e {props} propriedade(s) entraram na lista. Clique em \"Criar no desenho\".";
            _resumo.Foreground = Theme.Solid(Theme.LaranjaTexto);
        }

        private void Importar()
        {
            var dlg = new OpenFileDialog { Title = "Importar property sets", Filter = Planilha.LeitorPlanilha.Filtro };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                // O mapeamento da Autodesk tem formato fixo; as outras planilhas passam pela escolha das colunas.
                List<PsetMontagem> lidos = PsetsCore.SeMapeamentoAutodesk(dlg.FileName);
                if (lidos == null)
                {
                    var w = new ImportarPsetsWindow(dlg.FileName) { Owner = this };
                    if (w.ShowDialog() != true) return;
                    lidos = w.Resultado;
                }
                if (lidos.Count == 0) { MessageBox.Show(this, "Nenhum property set encontrado na planilha.", Title); return; }
                Acrescentar(lidos, Path.GetFileName(dlg.FileName));
            }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível ler a planilha:\n" + ex.Message, Title); }
        }

        private void Exportar()
        {
            if (_psets.Count == 0) return;
            var dlg = new SaveFileDialog { Title = "Exportar property sets", Filter = "Planilha CSV (*.csv)|*.csv", FileName = Path.GetFileNameWithoutExtension(_doc.Name) + " - property sets.csv" };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                PsetsCore.ExportarPlanilha(dlg.FileName, _tabelaPsets.VisibleItems<PsetMontagem>());
                _resumo.Text = "Planilha salva: " + dlg.FileName;
                _resumo.Foreground = Theme.Solid(Theme.Verde);
            }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível salvar:\n" + ex.Message, Title); }
        }

        private void DoMapeamento()
        {
            try
            {
                List<PsetMontagem> lidos = PsetsCore.DoMapeamentoIfc();
                if (lidos.Count == 0)
                {
                    MessageBox.Show(this, "O mapeamento IFC ativo não tem propriedades vindas de property sets (Source = PropertySet).\nVeja em Configurar IFC › Property sets.", Title);
                    return;
                }
                Acrescentar(lidos, "Mapeamento IFC");
            }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível ler o mapeamento IFC:\n" + ex.Message, Title); }
        }

        private void Criar()
        {
            _props.CommitEdit(DataGridEditingUnit.Row, true);
            var nomes = _psets.Select(p => (p.Nome ?? "").Trim()).ToList();
            if (nomes.Any(n => n.Length == 0)) { MessageBox.Show(this, "Há property set sem nome.", Title); return; }
            var dup = nomes.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (dup != null) { MessageBox.Show(this, $"O nome \"{dup.Key}\" aparece mais de uma vez.", Title); return; }
            var semProps = _psets.Where(p => p.Propriedades.Count == 0).Select(p => p.Nome).ToList();
            var dupProp = _psets.SelectMany(p => p.Propriedades.GroupBy(x => (x.Nome ?? "").Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => p.Nome + " › " + g.Key)).FirstOrDefault();
            if (dupProp != null) { MessageBox.Show(this, $"Propriedade repetida: {dupProp}.", Title); return; }

            var alvo = _psets.Where(p => p.Situacao != "No desenho").ToList();
            Cursor = Cursors.Wait;
            string msg;
            try
            {
                using (_doc.LockDocument()) msg = alvo.Count > 0 ? PsetsCore.CriarNoDesenho(_db, alvo) : "Os property sets já estavam iguais no desenho.";
                string ifc = PsetsCore.LevarParaIfc(_psets);
                if (ifc != null) msg += "\n\n" + ifc;
                if (semProps.Count > 0) msg += "\n\nSem propriedades: " + string.Join(", ", semProps) + ".";
            }
            catch (Exception ex) { msg = "Não foi possível criar:\n" + ex.Message; }
            finally { Cursor = null; }
            var sel = _atual;
            Atualizar();
            if (sel != null) _tabelaPsets.Grid.SelectedItem = sel;
            MessageBox.Show(this, msg, Title);
        }

        // ------------------------------------------------------------------ anexar

        private void TrocouAba()
        {
            bool anexar = _abas.SelectedIndex == 1;
            _criar.Visibility = anexar ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
            _anexar.Visibility = anexar ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            if (!anexar) { Atualizar(); return; }
            PreencherEscolha();
            if (_objetos == null) LerObjetos(); else AtualizarFaltas();
        }

        private void PreencherEscolha()
        {
            _escolhaPsets.Children.Clear();
            var noDesenho = PsetsCore.LerDesenho(_db);
            if (noDesenho.Count == 0)
            {
                _escolhaPsets.Children.Add(new TextBlock { Text = "Nenhum property set no desenho. Monte e crie na aba Montar.", Foreground = Theme.Solid(Theme.Cinza) });
                return;
            }
            _escolhidos.IntersectWith(noDesenho.Select(p => p.Nome));
            foreach (PsetMontagem p in noDesenho)
            {
                var c = new CheckBox
                {
                    Content = p.Nome,
                    IsChecked = _escolhidos.Contains(p.Nome),
                    Margin = new Thickness(0, 2, 16, 2),
                    ToolTip = $"{p.Propriedades.Count} propriedade(s) · aplica-se a: {p.AplicaA}",
                };
                c.Click += (s, e) =>
                {
                    if (c.IsChecked == true) _escolhidos.Add(p.Nome); else _escolhidos.Remove(p.Nome);
                    AtualizarFaltas();
                };
                _escolhaPsets.Children.Add(c);
            }
        }

        private void LerObjetos()
        {
            Cursor = Cursors.Wait;
            try
            {
                bool selecao = (_escopo.SelectedItem as string ?? "").StartsWith("Seleção");
                _objetos = PsetsCore.Objetos(_db, selecao ? _selecao : null);
            }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível ler os objetos:\n" + ex.Message, Title); _objetos = new List<ObjetoAnexar>(); }
            finally { Cursor = null; }

            var estado = _tabelaObjetos?.Estado();
            _tabelaObjetos = new FilterableGrid(_objetos, typeof(ObjetoAnexar));
            _tabelaObjetos.AddColumn("Tipo", nameof(ObjetoAnexar.Tipo), 150);
            _tabelaObjetos.AddColumn("Nome", nameof(ObjetoAnexar.Nome), 200);
            _tabelaObjetos.AddColumn("Layer", nameof(ObjetoAnexar.Layer), 180);
            _tabelaObjetos.AddColumn("Já anexados", nameof(ObjetoAnexar.Anexados), 220, fill: true);
            _tabelaObjetos.AddColumn("Falta anexar", nameof(ObjetoAnexar.Faltam), 220);
            _tabelaObjetos.Grid.LoadingRow += (s, e) =>
                e.Row.Background = string.IsNullOrEmpty(((ObjetoAnexar)e.Row.Item).Faltam) ? Theme.LinhaNormal : Theme.LinhaProblema;
            _tabelaObjetos.Filtered += AtualizarInfoAnexar;
            Theme.StyleSelection(_tabelaObjetos.Grid);
            if (estado != null) _tabelaObjetos.Restaurar(estado);
            _areaObjetos.Children.Clear();
            _areaObjetos.Children.Add(_tabelaObjetos);
            AtualizarFaltas();
        }

        private void AtualizarFaltas()
        {
            if (_objetos == null) return;
            try { PsetsCore.CalcularFaltas(_db, _objetos, _escolhidos); } catch { }
            _tabelaObjetos?.Refresh();
            _tabelaObjetos?.Grid.Items.Refresh();
            AtualizarInfoAnexar();
        }

        private void AtualizarInfoAnexar()
        {
            if (_abas.SelectedIndex != 1 || _tabelaObjetos == null) return;
            var vis = _tabelaObjetos.VisibleItems<ObjetoAnexar>().ToList();
            int faltam = vis.Count(o => !string.IsNullOrEmpty(o.Faltam));
            _anexar.IsEnabled = faltam > 0;
            _anexar.Content = faltam > 0 ? $"Anexar em {faltam}" : "Anexar";
            _infoAnexar.Text = _escolhidos.Count == 0
                ? $"{_objetos.Count} objeto(s) que vão para o IFC · marque os property sets a anexar"
                : $"{vis.Count} objeto(s) na tabela · {faltam} sem algum dos property sets marcados";
            _infoAnexar.Foreground = Theme.Solid(faltam > 0 || _escolhidos.Count == 0 ? Theme.LaranjaTexto : Theme.Verde);
        }

        private void Anexar()
        {
            var alvo = _tabelaObjetos.VisibleItems<ObjetoAnexar>().Where(o => !string.IsNullOrEmpty(o.Faltam)).ToList();
            if (alvo.Count == 0) return;
            Cursor = Cursors.Wait;
            string msg;
            try { using (_doc.LockDocument()) msg = PsetsCore.Anexar(_db, alvo); }
            catch (Exception ex) { msg = "Não foi possível anexar:\n" + ex.Message; }
            finally { Cursor = null; }
            AtualizarFaltas();
            MessageBox.Show(this, msg + "\n\nOs valores ficam na paleta Propriedades › Extended Data.", Title);
        }
    }
}
