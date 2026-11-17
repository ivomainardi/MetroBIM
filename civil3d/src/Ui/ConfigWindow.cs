using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using MetroSP.Config;
using MetroSP.Ifc;

namespace MetroSP.Ui
{
    /// <summary>
    /// Janela "Configurar IFC": edita o pacote de configuração ativo (arquivos do IFC Extension da Autodesk
    /// e opções MetroSP), importa/exporta JSON e baixa do Forma.
    /// </summary>
    public class ConfigWindow : Window
    {
        readonly string _dwgDir;
        readonly TabControl _tabs = new TabControl();
        readonly TextBlock _status = Theme.Detalhe();

        // estado editável
        JsonObject _mapping;
        readonly ObservableCollection<MappingRow> _mappingRows = new ObservableCollection<MappingRow>();
        ComboBox _mappingSection;
        string _currentSection;

        readonly ObservableCollection<PropertyRow> _propertyRows = new ObservableCollection<PropertyRow>();
        string _csvHeader = string.Join(",", PropertyRow.Columns);
        char _csvSep = ',';

        JsonNode _autodeskConfig;
        TextBox _templatesText;
        MetroSpIfcConfig _metro;
        readonly ObservableCollection<FacilityInfo> _facilities = new ObservableCollection<FacilityInfo>();
        DadosProjetoPainel _dadosPainel;
        readonly bool _somenteProjeto;

        public static readonly string[] KnownSections = { "MapCADLayerName", "MapBlockName", "MapBaselineName" };

        /// <summary>Classes IFC do esquema escolhido no IfcInfraConfiguration.json (Export.IfcVersion).</summary>
        string IfcVersion => (string)(_autodeskConfig?["Export"]?["IfcVersion"]) ?? "IFC4X3_ADD2";
        string[] IfcClasses => IfcCatalog.For(IfcVersion);

        /// <param name="somenteProjeto">Só a aba Projeto (botão Projeto IFC): IfcProject, IfcSite, instalações e MetroSP_Projeto.</param>
        public ConfigWindow(string dwgDir, bool somenteProjeto = false)
        {
            _dwgDir = dwgDir;
            _somenteProjeto = somenteProjeto;
            Title = somenteProjeto ? "MetroSP - Projeto IFC" : "MetroSP - Configuração IFC";
            Width = 900;
            Height = 640;
            MinWidth = 760;
            MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var saveClose = Btn("Salvar e fechar", (s, e) => SaveAll(close: true));
            saveClose.Style = Theme.PrimaryButton;
            saveClose.IsDefault = true;
            var body = new DockPanel();
            if (somenteProjeto) Width = 980;
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(toolbar, Dock.Top);
            if (somenteProjeto) toolbar.Visibility = Visibility.Collapsed;
            toolbar.Children.Add(Tool("Adicionar padrões…", "Abre a janela Padrões, onde se trazem os arquivos de configuração IFC (e os outros padrões): arraste ou escolha os arquivos.", (s, e) => AbrirPadroes()));
            toolbar.Children.Add(Tool("Exportar…", "Grava todos os arquivos do pacote numa pasta.", (s, e) => ExportPackage()));
            if (Settings.Load().UsesForma)
                toolbar.Children.Add(Tool("Baixar do Forma", "Troca o pacote pela versão mais recente do Forma.", async (s, e) => await DownloadFromForma()));
            toolbar.Children.Add(Tool("Restaurar padrão", "Volta ao padrão MetroSP.", (s, e) => RestoreDefaults()));
            body.Children.Add(toolbar);
            body.Children.Add(_tabs);

            Theme.Apply(this, somenteProjeto ? "Projeto IFC" : "Configuração IFC",
                somenteProjeto ? "IfcProject, IfcSite, instalações (IfcRailway, IfcBuilding) e Pset MetroSP_Projeto, aplicados na exportação"
                    : Settings.Load().UsesForma ? "Pacote sincronizado com o Forma" : "Pacote local",
                body, Theme.Rodape(_status,
                    Btn("Salvar", (s, e) => SaveAll(close: false)),
                    saveClose,
                    Btn("Fechar", (s, e) => Close())));

            // Seleção das tabelas em laranja, como no MetroSP do Revit (vale para todas as abas).
            Resources[SystemColors.HighlightBrushKey] = Theme.Solid(Theme.Laranja);
            Resources[SystemColors.HighlightTextBrushKey] = Theme.Solid(Theme.Preto);
            Resources[SystemColors.InactiveSelectionHighlightBrushKey] = Theme.Solid(Theme.LaranjaClaro);
            Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = Theme.Solid(Theme.Preto);

            ConfigPackage.EnsureActive();
            LoadAll();
            BuildTabs();
        }

        // ================================================================== carregar / salvar

        void LoadAll()
        {
            _mapping = ConfigPackage.LoadJson(ConfigKind.ObjectMapping) as JsonObject ?? new JsonObject();
            _autodeskConfig = ConfigPackage.LoadJson(ConfigKind.AutodeskConfig);
            _metro = ConfigPackage.LoadMetroSp();

            _facilities.Clear();
            foreach (var f in _metro.Estrutura.AllFacilities()) _facilities.Add(f);

            LoadCsv();
        }

        void SaveAll(bool close)
        {
            SincronizarProjeto();
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(_metro.Estrutura.Projeto?.Nome)) missing.Add("Nome do projeto");
            if (string.IsNullOrWhiteSpace(_metro.Estrutura.Terreno?.Nome)) missing.Add("Nome do terreno");
            if (missing.Count > 0)
            {
                _tabs.SelectedIndex = 0;
                Theme.MudarResumo(_status, missing.Select(m => m + " é obrigatório").ToList(), "");
                return;
            }

            CommitMappingSection();
            var invalidas = _somenteProjeto ? new List<string>() : InvalidClasses();
            if (invalidas.Count > 0)
            {
                Theme.MudarResumo(_status, invalidas.Take(8).Select(m => "Classe fora do " + IfcCatalog.Label(IfcVersion) + " em " + m)
                    .Concat(invalidas.Count > 8 ? new[] { $"e mais {invalidas.Count - 8}" } : new string[0]).ToList(), "");
                return;
            }

            try
            {
                ConfigPackage.SaveJson(ConfigKind.ObjectMapping, _mapping);
                if (_autodeskConfig != null) ConfigPackage.SaveJson(ConfigKind.AutodeskConfig, _autodeskConfig);
                if (_templatesText != null && !string.IsNullOrWhiteSpace(_templatesText.Text))
                    ConfigPackage.Write(ConfigKind.PropertyTemplates, Encoding.UTF8.GetBytes(_templatesText.Text));
                if (_propertyRows.Count > 0 || File.Exists(ConfigPackage.PathOf(ConfigKind.PropertyMappingCsv)))
                    SaveCsv();

                _metro.Estrutura.Instalacoes = _facilities.Where(f => !string.IsNullOrWhiteSpace(f.Nome)).ToList();
                _metro.Estrutura.Edificacoes = null;
                ConfigPackage.SaveMetroSp(_metro);

                Status("Configuração salva em " + ConfigPackage.ActiveDir);
                if (close) Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "MetroSP", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        void ReloadEverything(string message)
        {
            LoadAll();
            var selected = _tabs.SelectedIndex;
            _tabs.Items.Clear();
            BuildTabs();
            _tabs.SelectedIndex = Math.Max(0, selected);
            Status(message);
        }

        void BuildTabs()
        {
            _tabs.Items.Add(new TabItem { Header = "Projeto", Content = SpatialTab() });
            if (_somenteProjeto) return;
            _tabs.Items.Add(new TabItem { Header = "Classes IFC", Content = MappingTab() });
            _tabs.Items.Add(new TabItem { Header = "Propriedades", Content = PropertiesTab() });
            _tabs.Items.Add(new TabItem { Header = "Alinhamento", Content = AlignmentTab() });
            _tabs.Items.Add(new TabItem { Header = "Avançado", Content = AdvancedTab() });
        }

        // ================================================================== aba Pacote

        UIElement PackageFiles()
        {
            var list = new ListView { MaxHeight = 150 };
            var grid = new GridView();
            grid.Columns.Add(new GridViewColumn { Header = "Conteúdo", Width = 230, DisplayMemberBinding = new Binding("Tipo") });
            grid.Columns.Add(new GridViewColumn { Header = "Arquivo", Width = 300, DisplayMemberBinding = new Binding("Arquivo") });
            grid.Columns.Add(new GridViewColumn { Header = "Alterado em", Width = 130, DisplayMemberBinding = new Binding("Data") });
            list.View = grid;
            list.ItemsSource = ConfigPackage.List()
                .Select(f => new { Tipo = KindLabel(f.Kind), Arquivo = Path.GetFileName(f.Path), Data = f.Modified.ToString("dd/MM/yyyy HH:mm") })
                .ToList();
            return list;
        }

        static string KindLabel(ConfigKind k)
        {
            switch (k)
            {
                case ConfigKind.AutodeskConfig: return "Opções da exportação";
                case ConfigKind.ObjectMapping: return "Classes IFC";
                case ConfigKind.PropertyTemplates: return "Property sets";
                case ConfigKind.PropertyMappingCsv: return "Propriedades";
                case ConfigKind.MetroSP: return "Projeto e alinhamento";
                default: return k.ToString();
            }
        }

        /// <summary>Trazer arquivos é pela janela Padrões (um lugar só para todos os padrões); depois relê o pacote.</summary>
        void AbrirPadroes()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            new Padroes.GerenciarPadroesWindow(Padroes.PadroesCommands.Tipos(doc), "Civil 3D") { Owner = this }.ShowDialog();
            ReloadEverything("Pacote relido.");
        }

        void ExportPackage()
        {
            SaveAll(close: false);
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Pasta para exportar o pacote de configuração" };
            if (dlg.ShowDialog(this) != true) return;
            ConfigPackage.ExportTo(dlg.FolderName);
            Status("Pacote exportado para " + dlg.FolderName);
        }

        /// <summary>Importa os arquivos gerados por IFCInfraSaveConfig / IFCInfraSaveMappingConfig.</summary>
        void ImportAutodeskSamples()
        {
            var dirs = new[] { _dwgDir, Commands.ProgramDataDir }.Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d));
            var found = new List<string>();
            foreach (var kind in new[] { ConfigKind.AutodeskConfig, ConfigKind.ObjectMapping })
            {
                var name = ConfigPackage.FileNames[kind];
                var hit = dirs.Select(d => Path.Combine(d, name)).FirstOrDefault(File.Exists);
                if (hit != null) found.Add(hit);
            }
            if (found.Count == 0)
            {
                Warn("Não achei IfcInfraConfiguration.json nem IfcInfraExportMapping.json na pasta do desenho nem em " +
                     Commands.ProgramDataDir + ".\n\nRode IFCInfraSaveConfig e IFCInfraSaveMappingConfig no Civil 3D " +
                     "(eles gravam os modelos da Autodesk na pasta do desenho) e clique de novo.");
                return;
            }
            if (MessageBox.Show(this, "Substituir no pacote ativo:\n\n" + string.Join("\n", found), "MetroSP",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            try { ReloadEverything("Importado: " + string.Join("; ", ConfigPackage.Import(found))); }
            catch (Exception ex) { Warn(ex.Message); }
        }

        async Task DownloadFromForma()
        {
            var cfg = Settings.Load();
            try { cfg.Validate(); }
            catch (Exception ex) { Warn(ex.Message); return; }

            Status("Baixando do Forma (o navegador pode abrir para login)...");
            IsEnabled = false;
            var log = new List<string>();
            try
            {
                var count = await Task.Run(async () =>
                {
                    var client = new FormaClient(cfg, m => { lock (log) log.Add(m); });
                    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                    var remote = await client.ListFolderAsync(cts.Token);
                    int n = 0;
                    foreach (var f in remote)
                    {
                        var kind = ConfigPackage.FileNames.FirstOrDefault(kv => string.Equals(kv.Value, f.Name, StringComparison.OrdinalIgnoreCase)).Key;
                        if (kind == ConfigKind.Unknown) continue;
                        ConfigPackage.Write(kind, await client.DownloadAsync(f, cts.Token));
                        lock (log) log.Add($"{f.Name} v{f.VersionNumber}");
                        n++;
                    }
                    return n;
                });
                ReloadEverything($"{count} arquivo(s) baixados do Forma: " + string.Join("; ", log));
            }
            catch (Exception ex) { Warn("Falha ao baixar do Forma: " + ex.Message); Status(""); }
            finally { IsEnabled = true; }
        }

        void RestoreDefaults()
        {
            if (MessageBox.Show(this, "Trocar o pacote ativo pelo padrão MetroSP? Os arquivos atuais serão apagados " +
                                      "(exporte o pacote antes, se quiser guardar).", "MetroSP",
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            ConfigPackage.RestoreDefaults();
            ReloadEverything("Padrão MetroSP restaurado.");
        }

        // ================================================================== aba Mapeamento

        static readonly Dictionary<string, string> SectionLabels = new Dictionary<string, string>
        {
            ["MapCADLayerName"] = "Por layer",
            ["MapBlockName"] = "Por bloco",
            ["MapBaselineName"] = "Por baseline (corredor)"
        };

        UIElement MappingTab()
        {
            var panel = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };

            var top = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(top, Dock.Top);
            _mappingSection = new ComboBox { Width = 190, Margin = new Thickness(0, 0, 16, 0) };
            foreach (var key in KnownSections.Union(_mapping.Where(kv => kv.Value is JsonArray).Select(kv => kv.Key)))
                _mappingSection.Items.Add(new ComboBoxItem { Content = SectionLabels.TryGetValue(key, out var l) ? l : key, Tag = key, ToolTip = key });
            _mappingSection.SelectionChanged += (s, e) => SwitchSection((_mappingSection.SelectedItem as ComboBoxItem)?.Tag as string);
            top.Children.Add(_mappingSection);

            var classes = ClassPicker(IfcClasses);
            classes.ToolTip = $"Todas as classes do {IfcCatalog.Label(IfcVersion)}, o esquema escolhido na configuração da Autodesk. " +
                              "Digite parte do nome (ex.: TRACKSLAB) para filtrar; só vale uma classe da lista.";
            top.Children.Add(classes);

            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = true,
                ItemsSource = _mappingRows,
                HeadersVisibility = DataGridHeadersVisibility.Column
            };
            _gradeRegras = grid;
            grid.Columns.Add(new DataGridTextColumn { Header = "Nome", Binding = new Binding("Name"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            MappingRow.Catalog = IfcClasses;
            var invalid = new Style(typeof(TextBlock));
            var trig = new DataTrigger { Binding = new Binding(nameof(MappingRow.ClasseInvalida)), Value = true };
            trig.Setters.Add(new Setter(TextBlock.ForegroundProperty, Brushes.Firebrick));
            trig.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
            trig.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, "Classe fora do esquema; escolha uma da lista e clique Aplicar (ou Corrigir classes)."));
            invalid.Triggers.Add(trig);
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Classe IFC", Binding = new Binding("IfcExportAs"), IsReadOnly = true, ElementStyle = invalid,
                Width = new DataGridLength(2, DataGridLengthUnitType.Star)
            });
            grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Exportar", Binding = new Binding("Export") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 62 });
            var semUso = new Style(typeof(TextBlock));
            semUso.Setters.Add(new Setter(TextBlock.ToolTipProperty, new Binding(nameof(MappingRow.NoDesenhoDica))));
            var trigUso = new DataTrigger { Binding = new Binding(nameof(MappingRow.UsadaNoDesenho)), Value = false };
            trigUso.Setters.Add(new Setter(TextBlock.ForegroundProperty, Theme.Solid(Theme.Cinza)));
            semUso.Triggers.Add(trigUso);
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "No desenho", Binding = new Binding(nameof(MappingRow.NoDesenho)) { Mode = BindingMode.OneWay }, IsReadOnly = true,
                ElementStyle = semUso, Width = 92
            });
            grid.ToolTip = "Padrão carregado. Nome aceita expressão regular (ex.: ^U-3D-). Vale a primeira regra que casar, de cima para baixo.";
            grid.CellEditEnding += (s, e) => Dispatcher.BeginInvoke(new Action(Recalcular), System.Windows.Threading.DispatcherPriority.Background);
            grid.CurrentCellChanged += (s, e) => Recalcular();
            _mappingRows.CollectionChanged += (s, e) => Recalcular();

            var apply = Btn("Aplicar", (s, e) =>
            {
                var escolhida = classes.SelectedItem as string;
                if (escolhida == null) { Status("Escolha uma classe da lista."); return; }
                var linhas = grid.SelectedItems.OfType<MappingRow>().ToList();
                if (linhas.Count == 0) { Status("Selecione as regras que vão receber a classe."); return; }
                foreach (var r in linhas) r.IfcExportAs = escolhida == SemClasse ? "" : escolhida;
                grid.Items.Refresh();
                Recalcular();
            });
            apply.Margin = new Thickness(6, 0, 16, 0);
            apply.ToolTip = "Põe a classe escolhida nas regras selecionadas.";
            top.Children.Add(apply);
            top.Children.Add(SymbolButton("↑", "Subir (a regra de cima vale primeiro)", (s, e) => { Move(grid, -1); Recalcular(); }));
            top.Children.Add(SymbolButton("↓", "Descer", (s, e) => { Move(grid, +1); Recalcular(); }));
            top.Children.Add(SymbolButton("+", "Nova regra", (s, e) => AddRow(grid, _mappingRows)));
            top.Children.Add(SymbolButton("✕", "Excluir as regras selecionadas (ou tecla Delete)", (s, e) => RemoveSelected(grid, _mappingRows, "regra")));
            _corrigir = Tool("Corrigir classes", "", (s, e) => CorrigirClasses());
            _corrigir.VerticalAlignment = VerticalAlignment.Center;
            top.Children.Add(_corrigir);
            panel.Children.Add(top);

            // ---- lado a lado: padrão (regras) | desenho (layers, blocos, baselines)
            var lados = new Grid();
            lados.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 260 });
            lados.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            lados.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 260 });

            var esquerda = new DockPanel();
            var tituloPadrao = Theme.Secao("No padrão");
            tituloPadrao.Margin = new Thickness(0, 0, 0, 4);
            tituloPadrao.ToolTip = "Regras do padrão de Classes IFC carregado (IfcInfraExportMapping.json).";
            DockPanel.SetDock(tituloPadrao, Dock.Top);
            esquerda.Children.Add(tituloPadrao);
            esquerda.Children.Add(grid);
            lados.Children.Add(esquerda);

            var divisor = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent, ToolTip = "Arraste para mudar a largura" };
            Grid.SetColumn(divisor, 1);
            lados.Children.Add(divisor);

            var direita = DesenhoPainel(grid, classes);
            Grid.SetColumn(direita, 2);
            lados.Children.Add(direita);

            panel.Children.Add(lados);

            _currentSection = null;
            _mappingSection.SelectedIndex = 0;
            return panel;
        }

        // ---- o que existe no desenho

        DataGrid _gradeRegras;
        Button _corrigir;
        Dictionary<string, List<ItemDesenho>> _desenho;
        readonly ObservableCollection<ItemDesenho> _itensDesenho = new ObservableCollection<ItemDesenho>();
        FilterableGrid _gradeDesenho;
        TextBlock _tituloDesenho, _resumoDesenho;
        CheckBox _soUsados, _soSemRegra;
        bool _recalculando;

        UIElement DesenhoPainel(DataGrid regras, ComboBox classes)
        {
            var p = new DockPanel();
            var cab = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            DockPanel.SetDock(cab, Dock.Top);
            var reler = Theme.Link("Reler");
            reler.ToolTip = "Lê de novo layers, blocos e corredores do desenho.";
            reler.Click += (s, e) => { _desenho = null; MostrarDesenho(); };
            DockPanel.SetDock(reler, Dock.Right);
            cab.Children.Add(reler);
            _tituloDesenho = Theme.Secao("No desenho");
            _tituloDesenho.ToolTip = "O que existe neste DWG e a regra do padrão que cada nome pega. Conta só objetos do espaço do modelo que não são anotação.";
            cab.Children.Add(_tituloDesenho);
            p.Children.Add(cab);

            var filtros = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            DockPanel.SetDock(filtros, Dock.Top);
            _soUsados = new CheckBox { Content = "Só usados no modelo", IsChecked = true, Margin = new Thickness(0, 0, 14, 0), ToolTip = "Esconde os nomes sem nenhum objeto no espaço do modelo." };
            _soSemRegra = new CheckBox { Content = "Só sem regra", Margin = new Thickness(0, 0, 14, 0), ToolTip = "Mostra só o que nenhuma regra do padrão pega (sai com a classe que a Autodesk escolher)." };
            _soUsados.Click += (s, e) => _gradeDesenho.Refresh();
            _soSemRegra.Click += (s, e) => _gradeDesenho.Refresh();
            filtros.Children.Add(_soUsados);
            filtros.Children.Add(_soSemRegra);
            var criar = new Button { Content = "Criar regra", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 0, 0),
                ToolTip = "Cria no topo do padrão uma regra para cada nome selecionado, com a classe escolhida na lista de cima." };
            criar.Click += (s, e) => CriarRegras(classes);
            filtros.Children.Add(criar);
            p.Children.Add(filtros);

            _resumoDesenho = Theme.Detalhe();
            _resumoDesenho.Margin = new Thickness(0, 4, 0, 0);
            DockPanel.SetDock(_resumoDesenho, Dock.Bottom);
            p.Children.Add(_resumoDesenho);

            _gradeDesenho = new FilterableGrid(_itensDesenho, typeof(ItemDesenho));
            _gradeDesenho.AddColumn("Nome", nameof(ItemDesenho.Nome), 150, fill: true);
            var obj = _gradeDesenho.AddColumn("Objetos", nameof(ItemDesenho.Objetos), 70);
            obj.ElementStyle = new Style(typeof(TextBlock), obj.ElementStyle) { Setters = { new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right) } };
            _gradeDesenho.AddColumn("Vai como", nameof(ItemDesenho.VaiComo), 170);
            _gradeDesenho.AddColumn("Regra", nameof(ItemDesenho.Regra), 130);
            _gradeDesenho.AddColumn("Tipos", nameof(ItemDesenho.Tipos), 160);
            _gradeDesenho.AddColumn("Situação", nameof(ItemDesenho.Situacao), 90);
            _gradeDesenho.AddColumn("Também casa", nameof(ItemDesenho.Outras), 130);
            _gradeDesenho.ExtraFilter = o => o is ItemDesenho i && (_soUsados.IsChecked != true || i.Objetos > 0) && (_soSemRegra.IsChecked != true || i.SemRegra);
            _gradeDesenho.Filtered += AtualizarResumo;

            var linha = new Style(typeof(DataGridRow), _gradeDesenho.Grid.RowStyle);
            var sem = new DataTrigger { Binding = new Binding(nameof(ItemDesenho.SemRegra)), Value = true };
            sem.Setters.Add(new Setter(Control.BackgroundProperty, Theme.LinhaProblema));
            sem.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, "Nenhuma regra do padrão pega este nome: sai com a classe que a Autodesk escolher."));
            linha.Triggers.Add(sem);
            var fora = new DataTrigger { Binding = new Binding(nameof(ItemDesenho.NaoExporta)), Value = true };
            fora.Setters.Add(new Setter(Control.ForegroundProperty, Theme.Solid(Theme.Cinza)));
            linha.Triggers.Add(fora);
            _gradeDesenho.Grid.RowStyle = linha;

            // Clicar numa regra marca no desenho os nomes que ela pega; clicar num nome marca a regra que vale.
            regras.SelectionChanged += (s, e) =>
            {
                if (_recalculando || !(regras.SelectedItem is MappingRow r) || !regras.IsKeyboardFocusWithin) return;
                var pegos = _gradeDesenho.VisibleItems<ItemDesenho>().Where(i => i.RegraObj == r).ToList();
                _gradeDesenho.Grid.SelectedItems.Clear();
                foreach (var i in pegos) _gradeDesenho.Grid.SelectedItems.Add(i);
                if (pegos.Count > 0) _gradeDesenho.Grid.ScrollIntoView(pegos[0]);
            };
            _gradeDesenho.Grid.SelectionChanged += (s, e) =>
            {
                if (_recalculando || !_gradeDesenho.Grid.IsKeyboardFocusWithin) return;
                if (_gradeDesenho.Grid.SelectedItem is ItemDesenho i && i.RegraObj is MappingRow r)
                {
                    regras.SelectedItem = r;
                    regras.ScrollIntoView(r);
                }
            };
            p.Children.Add(_gradeDesenho);
            return p;
        }

        /// <summary>Troca a lista da direita para a seção escolhida (lê o desenho na primeira vez).</summary>
        void MostrarDesenho()
        {
            if (_gradeDesenho == null) return;
            if (_desenho == null)
            {
                try { _desenho = ConteudoDesenho.Ler(); }
                catch (Exception ex) { _desenho = new Dictionary<string, List<ItemDesenho>>(); Status("Não consegui ler o desenho: " + ex.Message); }
            }
            _itensDesenho.Clear();
            var palavra = ConteudoDesenho.Palavra(_currentSection);
            _tituloDesenho.Text = ConteudoDesenho.Le(_currentSection) ? "No desenho (" + palavra.Varios + ")" : "No desenho";
            if (_currentSection != null && _desenho.TryGetValue(_currentSection, out var itens))
                foreach (var i in itens) _itensDesenho.Add(i);
            Recalcular();
        }

        /// <summary>
        /// Para cada nome do desenho, a regra que vale (a primeira de cima para baixo que casa) e, para cada regra,
        /// quantos nomes e objetos ela pega. Também liga o "Corrigir classes" quando há classe com correção óbvia.
        /// </summary>
        void Recalcular()
        {
            if (_gradeDesenho == null || _recalculando) return;
            _recalculando = true;
            try
            {
                var regras = _mappingRows.Where(r => !string.IsNullOrWhiteSpace(r.Name)).ToList();
                var pega = regras.ToDictionary(r => r, r => (Nomes: 0, Objetos: 0, Cobertos: 0));
                foreach (var i in _itensDesenho)
                {
                    var casam = regras.Where(r => Casa(r.Name, i.Nome)).ToList();
                    var vale = casam.FirstOrDefault();
                    string outras = string.Join("; ", casam.Skip(1).Select(r => r.Name));
                    if (vale == null)
                        i.Definir(null, "", "Sem regra (classe da Autodesk)", true, false, "");
                    else
                        i.Definir(vale, vale.Name, !vale.Export ? "Não exporta"
                            : string.IsNullOrWhiteSpace(vale.IfcExportAs) ? "(classe da Autodesk)" : vale.IfcExportAs, false, !vale.Export, outras);
                    if (vale != null)
                    {
                        var c = pega[vale];
                        pega[vale] = (c.Nomes + 1, c.Objetos + i.Objetos, c.Cobertos);
                    }
                    foreach (var r in casam.Skip(1)) { var c = pega[r]; pega[r] = (c.Nomes, c.Objetos, c.Cobertos + 1); }
                }
                bool le = ConteudoDesenho.Le(_currentSection);
                var palavra = ConteudoDesenho.Palavra(_currentSection);
                foreach (var r in _mappingRows)
                {
                    if (!le || !pega.TryGetValue(r, out var c)) { r.DefinirUso("", "", true); continue; }
                    if (c.Nomes > 0)
                        r.DefinirUso($"{c.Nomes} · {c.Objetos} obj", $"Pega {c.Nomes} {(c.Nomes == 1 ? palavra.Um : palavra.Varios)} do desenho, com {c.Objetos} objeto(s)." +
                            (c.Cobertos > 0 ? $" Casa também com {c.Cobertos} que ficam com uma regra de cima." : ""), true);
                    else if (c.Cobertos > 0)
                        r.DefinirUso("coberta", $"Casa com {c.Cobertos} {(c.Cobertos == 1 ? palavra.Um : palavra.Varios)}, mas uma regra de cima vale primeiro.", false);
                    else
                        r.DefinirUso("—", "Nenhum " + palavra.Um + " do desenho tem este nome.", false);
                }

                CommitMappingSection();   // só passa a tela para o JSON (não grava)
                int corrigiveis = Corrigiveis().Count;
                _corrigir.Visibility = corrigiveis > 0 ? Visibility.Visible : Visibility.Collapsed;
                _corrigir.Content = corrigiveis == 1 ? "Corrigir 1 classe" : $"Corrigir {corrigiveis} classes";
                _corrigir.ToolTip = "Troca as classes fora do esquema pela correção óbvia: " +
                                    string.Join("; ", Corrigiveis().Take(4).Select(x => $"{x.De} → {x.Para}")) + ".";
            }
            finally { _recalculando = false; }
            _gradeDesenho.Refresh();
        }

        void AtualizarResumo()
        {
            if (_resumoDesenho == null) return;
            if (!ConteudoDesenho.Le(_currentSection))
            {
                _resumoDesenho.Text = "O plugin lê do desenho só layers, blocos e baselines de corredor.";
                return;
            }
            var palavra = ConteudoDesenho.Palavra(_currentSection);
            var usados = _itensDesenho.Where(i => i.Objetos > 0).ToList();
            int sem = usados.Count(i => i.SemRegra);
            int naoExp = usados.Count(i => i.NaoExporta);
            _resumoDesenho.Text = $"{usados.Count} {palavra.Varios} com objetos · {sem} sem regra · {naoExp} não exportados";
        }

        static readonly Dictionary<string, System.Text.RegularExpressions.Regex> _regex = new Dictionary<string, System.Text.RegularExpressions.Regex>();

        /// <summary>Nome igual (sem diferenciar maiúsculas) ou expressão regular que casa, como no FiltroIfc.</summary>
        static bool Casa(string regra, string nome)
        {
            if (string.IsNullOrWhiteSpace(regra) || nome == null) return false;
            if (regra.Trim().Equals(nome, StringComparison.OrdinalIgnoreCase)) return true;
            if (!_regex.TryGetValue(regra, out var re))
            {
                try { re = new System.Text.RegularExpressions.Regex(regra.Trim(), System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant); }
                catch { re = null; }
                _regex[regra] = re;
            }
            return re != null && re.IsMatch(nome);
        }

        /// <summary>Cria regras com o nome exato dos itens selecionados no desenho, no topo (para valerem primeiro).</summary>
        void CriarRegras(ComboBox classes)
        {
            var itens = _gradeDesenho.Grid.SelectedItems.OfType<ItemDesenho>().ToList();
            if (itens.Count == 0) { Status("Selecione no desenho os nomes que vão ganhar regra."); return; }
            var escolhida = classes.SelectedItem as string;
            string classe = escolhida == null || escolhida == SemClasse ? "" : escolhida;
            _gradeRegras.CommitEdit(DataGridEditingUnit.Row, true);
            var novas = new List<MappingRow>();
            int pos = 0;
            foreach (var i in itens)
            {
                var r = new MappingRow { Name = "^" + System.Text.RegularExpressions.Regex.Escape(i.Nome) + "$", IfcExportAs = classe, Export = true };
                _mappingRows.Insert(pos++, r);
                novas.Add(r);
            }
            _gradeRegras.SelectedItems.Clear();
            foreach (var r in novas) _gradeRegras.SelectedItems.Add(r);
            _gradeRegras.ScrollIntoView(novas[0]);
            Status(novas.Count == 1 ? "1 regra criada no topo." : $"{novas.Count} regras criadas no topo." +
                   (classe.Length == 0 ? " Escolha a classe e clique Aplicar." : ""));
            Recalcular();
        }

        /// <summary>Classes fora do esquema com correção óbvia, em todas as seções.</summary>
        List<(JsonObject Regra, string De, string Para)> Corrigiveis()
        {
            var r = new List<(JsonObject, string, string)>();
            foreach (var kv in _mapping)
                if (kv.Value is JsonArray arr)
                    foreach (var o in arr.OfType<JsonObject>())
                    {
                        var c = (string)o["IfcExportAs"];
                        if (IfcCatalog.IsValid(c, IfcClasses)) continue;
                        var s = IfcCatalog.Sugestao(c, IfcClasses);
                        if (s != null) r.Add((o, c, s));
                    }
            return r;
        }

        void CorrigirClasses()
        {
            CommitMappingSection();
            var lista = Corrigiveis();
            foreach (var x in lista) x.Regra["IfcExportAs"] = x.Para;
            var secao = _currentSection;
            _currentSection = null;
            SwitchSection(secao);
            Status(lista.Count == 1 ? $"Corrigida: {lista[0].De} → {lista[0].Para}. Clique Salvar." : $"{lista.Count} classes corrigidas. Clique Salvar.");
        }

        const string SemClasse = "(vazio: classe da Autodesk)";

        /// <summary>
        /// Lista com todas as classes do esquema. Digitar só filtra (parte do nome, sem diferenciar maiúsculas);
        /// o valor escolhido é sempre um item da lista.
        /// </summary>
        static ComboBox ClassPicker(string[] catalog)
        {
            var items = new[] { SemClasse }.Concat(catalog).ToList();
            var view = new ListCollectionView(items);
            var box = new ComboBox
            {
                Width = 280, IsEditable = true, IsTextSearchEnabled = false, StaysOpenOnEdit = true,
                ItemsSource = view, MaxDropDownHeight = 360
            };
            string ultima = null;
            box.Loaded += (s, e) =>
            {
                if (!(box.Template.FindName("PART_EditableTextBox", box) is TextBox tb)) return;
                tb.TextChanged += (s2, e2) =>
                {
                    if (box.SelectedItem is string sel && sel == tb.Text) return;
                    var f = tb.Text.Trim();
                    view.Filter = f.Length == 0 ? null : (Predicate<object>)(o => ((string)o).IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!box.IsDropDownOpen && box.IsKeyboardFocusWithin) box.IsDropDownOpen = true;
                    tb.SelectionStart = tb.Text.Length;
                };
            };
            box.SelectionChanged += (s, e) => { if (box.SelectedItem is string sel) ultima = sel; };
            box.LostKeyboardFocus += (s, e) =>
            {
                if (box.IsKeyboardFocusWithin) return;
                var exata = items.FirstOrDefault(i => i.Equals(box.Text?.Trim(), StringComparison.OrdinalIgnoreCase));
                view.Filter = null;
                if (exata != null) box.SelectedItem = exata;
                else { box.SelectedItem = ultima; box.Text = ultima ?? ""; }
            };
            return box;
        }

        /// <summary>Regras com classe fora do esquema, em todas as seções do mapeamento.</summary>
        /// <summary>Abas da janela (a Entrega passo a passo abre direto em Classes IFC).</summary>
        internal TabControl Abas => _tabs;

        List<string> InvalidClasses() => ClassesInvalidas(_mapping, IfcVersion);

        /// <summary>Linhas do mapeamento com classe fora do esquema IFC escolhido (usado também na conferência da entrega).</summary>
        internal static List<string> ClassesInvalidas(JsonObject mapping, string ifcVersion)
        {
            var list = IfcCatalog.For(ifcVersion);
            var bad = new List<string>();
            foreach (var kv in mapping)
                if (kv.Value is JsonArray arr)
                    foreach (var o in arr.OfType<JsonObject>())
                    {
                        var c = (string)o["IfcExportAs"];
                        if (!IfcCatalog.IsValid(c, list))
                            bad.Add($"{(SectionLabels.TryGetValue(kv.Key, out var l) ? l : kv.Key)}: {(string)o["Name"]} → \"{c}\"");
                    }
            return bad;
        }

        void SwitchSection(string section)
        {
            if (string.IsNullOrWhiteSpace(section) || section == _currentSection) return;
            CommitMappingSection();
            _currentSection = section.Trim();
            _mappingRows.Clear();
            if (_mapping[_currentSection] is JsonArray arr)
                foreach (var item in arr.OfType<JsonObject>()) _mappingRows.Add(MappingRow.From(item));
            MostrarDesenho();
        }

        void CommitMappingSection()
        {
            if (_currentSection == null) return;
            var arr = new JsonArray();
            foreach (var r in _mappingRows.Where(r => !string.IsNullOrWhiteSpace(r.Name))) arr.Add(r.ToJson());
            if (arr.Count == 0 && !_mapping.ContainsKey(_currentSection)) return;
            _mapping[_currentSection] = arr;
        }

        static void Move(DataGrid grid, int delta)
        {
            if (!(grid.ItemsSource is ObservableCollection<MappingRow> rows) || !(grid.SelectedItem is MappingRow r)) return;
            var i = rows.IndexOf(r);
            var j = i + delta;
            if (i < 0 || j < 0 || j >= rows.Count) return;
            rows.Move(i, j);
            grid.SelectedItem = r;
        }

        // ================================================================== aba Property sets

        UIElement PropertiesTab()
        {
            var tabs = new TabControl { Margin = new Thickness(0, 8, 0, 0) };

            var grid = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = true, ItemsSource = _propertyRows, Margin = new Thickness(0, 8, 0, 0) };
            grid.Columns.Add(new DataGridTextColumn { Header = "Property set", Binding = new Binding("PropertySet"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Propriedade", Binding = new Binding("Property"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Ativa", Binding = new Binding("Active"), Width = 50 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Origem", Binding = new Binding("Source"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Grupo", Binding = new Binding("Group"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Nome no Civil 3D", Binding = new Binding("CivilName"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            grid.ToolTip = "Origem: Properties, PartData, General ou PropertySet.";
            tabs.Items.Add(new TabItem { Header = "De onde vem cada uma", Content = WithTools(grid, _propertyRows, "propriedade") });

            var tpl = File.Exists(ConfigPackage.PathOf(ConfigKind.PropertyTemplates))
                ? File.ReadAllText(ConfigPackage.PathOf(ConfigKind.PropertyTemplates))
                : "";
            _templatesText = new TextBox
            {
                Text = tpl,
                AcceptsReturn = true,
                AcceptsTab = true,
                FontFamily = new FontFamily("Consolas"),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 8, 0, 0),
                ToolTip = "IFCInfraExportPropertyMapping.json. O JSON é validado ao salvar."
            };
            tabs.Items.Add(new TabItem { Header = "Property sets (JSON)", Content = _templatesText });
            return tabs;
        }

        void LoadCsv()
        {
            _propertyRows.Clear();
            var path = ConfigPackage.PathOf(ConfigKind.PropertyMappingCsv);
            if (!File.Exists(path)) return;
            var lines = File.ReadAllLines(path).Where(l => l.Trim().Length > 0).ToList();
            if (lines.Count == 0) return;
            _csvSep = lines[0].Count(c => c == ';') > lines[0].Count(c => c == ',') ? ';' : ',';
            _csvHeader = lines[0];
            foreach (var l in lines.Skip(1)) _propertyRows.Add(PropertyRow.FromCells(Csv.Split(l, _csvSep)));
        }

        void SaveCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine(_csvHeader);
            foreach (var r in _propertyRows.Where(r => !string.IsNullOrWhiteSpace(r.PropertySet) || !string.IsNullOrWhiteSpace(r.Property)))
                sb.AppendLine(string.Join(_csvSep.ToString(), r.ToCells().Select(c => Csv.Quote(c, _csvSep))));
            ConfigPackage.Write(ConfigKind.PropertyMappingCsv, new UTF8Encoding(true).GetBytes(sb.ToString()));
        }

        // ================================================================== aba Projeto, terreno e edificações

        UIElement SpatialTab()
        {
            var e = _metro.Estrutura;
            e.Projeto ??= new ProjectInfo();
            e.Terreno ??= new SiteInfo();

            // Lat/long, elevação, matrícula e coordenadas: só do Civil 3D, dentro da coluna IfcSite.
            var extra = Form(
                ("Latitude", Text(e.Terreno, nameof(SiteInfo.Latitude), "Graus decimais; sul é negativo. Vazio: calculada pelo PTL.")),
                ("Longitude", Text(e.Terreno, nameof(SiteInfo.Longitude), "Graus decimais; oeste é negativo. Vazio: calculada pelo PTL.")),
                ("Elevação (m)", Text(e.Terreno, nameof(SiteInfo.Elevacao))),
                ("Matrícula", Text(e.Terreno, nameof(SiteInfo.Matricula), "Matrícula do terreno (LandTitleNumber).")),
                ("Coordenadas", Ptl(e)));
            extra.Margin = new Thickness(0, 6, 0, 0);

            // Instalações no lugar do IfcBuilding único: a linha (IfcRailway) e cada estação (IfcBuilding).
            var inst = new StackPanel();
            var bgridHead = new DockPanel();
            inst.Children.Add(bgridHead);
            var bgrid = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = true, ItemsSource = _facilities, MinHeight = 110,
                ToolTip = "Dentro do IfcSite. A linha como IfcRailway; cada estação como IfcBuilding. Se a Autodesk já gravou uma com o mesmo nome, ela recebe o nome longo, a descrição e o endereço." };
            bgrid.Columns.Add(new DataGridComboBoxColumn { Header = "Tipo", ItemsSource = FacilityInfo.Types, SelectedItemBinding = new Binding("Tipo"), Width = 110 });
            bgrid.Columns.Add(new DataGridTextColumn { Header = "Nome", Binding = new Binding("Nome"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            bgrid.Columns.Add(new DataGridTextColumn { Header = "Nome longo", Binding = new Binding("NomeLongo"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            bgrid.Columns.Add(new DataGridTextColumn { Header = "Descrição", Binding = new Binding("Descricao"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            bgrid.Columns.Add(new DataGridTextColumn { Header = "Endereço", Binding = new Binding("Endereco"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            bgrid.Columns.Add(new DataGridTextColumn { Header = "Elevação (m)", Binding = new Binding("ElevacaoReferencia") { TargetNullValue = "" }, Width = 90 });
            inst.Children.Add(bgrid);
            SectionWithTools(bgridHead, "Instalações · IfcRailway, IfcBuilding", bgrid, _facilities, "instalação");

            var dados = new DadosProjetoIfc
            {
                Projeto = new DadosEntidadeIfc { Nome = e.Projeto.Nome, NomeLongo = e.Projeto.NomeLongo, Descricao = e.Projeto.Descricao, Fase = e.Projeto.Fase },
                Terreno = new DadosEntidadeIfc { Nome = e.Terreno.Nome, NomeLongo = e.Terreno.NomeLongo, Descricao = e.Terreno.Descricao, Endereco = e.Terreno.Endereco },
                Propriedades = new Dictionary<string, string>(_metro.PropriedadesProjeto ?? new Dictionary<string, string>()),
            };
            var dicas = new Dictionary<string, string>
            {
                ["Projeto.Nome"] = "Obrigatório. Vazio nos demais campos mantém o que a Autodesk gravou.",
                ["Terreno.Nome"] = "Obrigatório.",
            };
            _dadosPainel = new DadosProjetoPainel(dados, dicas, extra, inst)
            {
                EdificioParaPadrao = () =>
                {
                    var b = _facilities.FirstOrDefault(f => f.Tipo == "IfcBuilding" && !string.IsNullOrWhiteSpace(f.Nome));
                    return b == null ? null : new DadosEntidadeIfc { Nome = b.Nome, NomeLongo = b.NomeLongo, Descricao = b.Descricao, Endereco = b.Endereco };
                },
            };
            _dadosPainel.PadraoUsado += UsarEdificioDoPadrao;
            return new ScrollViewer { Content = _dadosPainel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 6, 0) };
        }

        /// <summary>Leva os campos do painel (IfcProject, IfcSite, MetroSP_Projeto) para a configuração.</summary>
        void SincronizarProjeto()
        {
            if (_dadosPainel == null) return;
            var d = _dadosPainel.Ler();
            var e = _metro.Estrutura;
            e.Projeto ??= new ProjectInfo();
            e.Terreno ??= new SiteInfo();
            static string N(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
            e.Projeto.Nome = N(d.Projeto.Nome); e.Projeto.NomeLongo = N(d.Projeto.NomeLongo);
            e.Projeto.Descricao = N(d.Projeto.Descricao); e.Projeto.Fase = N(d.Projeto.Fase);
            e.Terreno.Nome = N(d.Terreno.Nome); e.Terreno.NomeLongo = N(d.Terreno.NomeLongo);
            e.Terreno.Descricao = N(d.Terreno.Descricao); e.Terreno.Endereco = N(d.Terreno.Endereco);
            _metro.PropriedadesProjeto = d.Propriedades;
        }

        /// <summary>O edifício do padrão (salvo no Revit, por exemplo) entra como IfcBuilding, ou completa o de mesmo nome.</summary>
        void UsarEdificioDoPadrao(DadosProjetoIfc padrao)
        {
            var ed = padrao?.Edificio;
            if (ed == null || string.IsNullOrWhiteSpace(ed.Nome)) return;
            var f = _facilities.FirstOrDefault(x => string.Equals((x.Nome ?? "").Trim(), ed.Nome.Trim(), StringComparison.OrdinalIgnoreCase));
            if (f == null)
            {
                _facilities.Add(new FacilityInfo { Tipo = "IfcBuilding", Nome = ed.Nome.Trim(), NomeLongo = ed.NomeLongo, Descricao = ed.Descricao, Endereco = ed.Endereco });
                return;
            }
            if (!string.IsNullOrWhiteSpace(ed.NomeLongo)) f.NomeLongo = ed.NomeLongo;
            if (!string.IsNullOrWhiteSpace(ed.Descricao)) f.Descricao = ed.Descricao;
            if (!string.IsNullOrWhiteSpace(ed.Endereco)) f.Endereco = ed.Endereco;
            var i = _facilities.IndexOf(f);
            _facilities[i] = f;   // atualiza a linha na tabela
        }

        // ================================================================== aba Alinhamento

        UIElement AlignmentTab()
        {
            var a = _metro.Alinhamento;
            var stack = new StackPanel { MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };
            stack.Children.Add(Section("No IFC exportado"));
            var apply = new CheckBox
            {
                Content = "Alinhamento com as cotas do perfil",
                Margin = new Thickness(0, 2, 0, 8),
                ToolTip = "Eixo 3D, IfcAlignmentVertical e, se a Autodesk não gravar o alinhamento, o alinhamento completo."
            };
            apply.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(nameof(AlignmentOptions.AplicarNoIfc)) { Source = a });
            stack.Children.Add(apply);
            stack.Children.Add(Form(("Passo (m)", Text(a, nameof(AlignmentOptions.Passo), "Distância entre os pontos do eixo 3D."))));

            stack.Children.Add(Section("Perfil preferido"));
            var prefs = new TextBox
            {
                Text = string.Join(Environment.NewLine, a.PerfisPreferidos ?? new List<string>()),
                AcceptsReturn = true,
                Height = 110,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                ToolTip = "Um nome por linha, em ordem (aceita expressão regular). Se nenhum casar, o Civil 3D pergunta."
            };
            prefs.TextChanged += (s, e) => a.PerfisPreferidos = prefs.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToList();
            stack.Children.Add(prefs);
            return stack;
        }

        // ================================================================== aba Configuração Autodesk

        UIElement AdvancedTab()
        {
            var panel = new DockPanel();
            var head = new StackPanel();
            DockPanel.SetDock(head, Dock.Top);
            var filesHead = new DockPanel();
            var open = Theme.Link("Abrir pasta");
            open.Click += (s, e) => Process.Start(new ProcessStartInfo("explorer.exe", "\"" + ConfigPackage.ActiveDir + "\"") { UseShellExecute = true });
            DockPanel.SetDock(open, Dock.Right);
            filesHead.Children.Add(open);
            filesHead.Children.Add(Section("Arquivos do pacote"));
            head.Children.Add(filesHead);
            head.Children.Add(PackageFiles());

            var cfgHead = new DockPanel();
            var samples = Theme.Link("Importar da Autodesk");
            samples.ToolTip = "Usa os modelos gravados por IFCInfraSaveConfig e IFCInfraSaveMappingConfig.";
            samples.Click += (s, e) => ImportAutodeskSamples();
            DockPanel.SetDock(samples, Dock.Right);
            cfgHead.Children.Add(samples);
            cfgHead.Children.Add(Section("Opções da exportação · IfcInfraConfiguration.json"));
            head.Children.Add(cfgHead);
            panel.Children.Add(head);

            if (_autodeskConfig == null)
            {
                panel.Children.Add(Theme.Detalhe("Ainda não há este arquivo no pacote. Rode IFCInfraSaveConfig no Civil 3D e use Importar da Autodesk."));
                return panel;
            }
            var tree = new TreeView();
            foreach (var item in JsonTree.Build(_autodeskConfig)) tree.Items.Add(item);
            panel.Children.Add(tree);
            return panel;
        }

        // ================================================================== helpers de UI

        static Button Btn(string text, RoutedEventHandler click)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 4) };
            b.Click += click;
            return b;
        }

        static TextBlock Label(string text) => new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };

        /// <summary>Ação do pacote no topo da janela, como link.</summary>
        static Button Tool(string text, string tip, RoutedEventHandler click)
        {
            var b = Theme.Link(text);
            b.ToolTip = tip;
            b.Margin = new Thickness(16, 0, 0, 0);
            b.Click += click;
            return b;
        }

        /// <summary>Botão quadrado só com símbolo (↑ ↓ ✕), com a ação na dica.</summary>
        static Button SymbolButton(string symbol, string tip, RoutedEventHandler click)
        {
            var b = new Button { Content = symbol, ToolTip = tip, MinWidth = 32, Width = 32, Padding = new Thickness(0, 3, 0, 3), Margin = new Thickness(0, 0, 4, 0) };
            b.Click += click;
            return b;
        }

        /// <summary>Título de seção com + (incluir) e ✕ (excluir as linhas selecionadas) à direita.</summary>
        static void SectionWithTools<T>(DockPanel head, string title, DataGrid grid, ObservableCollection<T> rows, string what) where T : new()
        {
            var tools = Tools(grid, rows, what);
            tools.Margin = new Thickness(0, 10, 0, 4);
            DockPanel.SetDock(tools, Dock.Right);
            head.Children.Add(tools);
            head.Children.Add(Section(title));
        }

        /// <summary>Tabela com a barra + / ✕ em cima.</summary>
        static UIElement WithTools<T>(DataGrid grid, ObservableCollection<T> rows, string what) where T : new()
        {
            var panel = new DockPanel();
            var tools = Tools(grid, rows, what);
            tools.HorizontalAlignment = HorizontalAlignment.Right;
            tools.Margin = new Thickness(0, 8, 0, 0);
            DockPanel.SetDock(tools, Dock.Top);
            panel.Children.Add(tools);
            grid.Margin = new Thickness(0, 4, 0, 0);
            panel.Children.Add(grid);
            return panel;
        }

        static StackPanel Tools<T>(DataGrid grid, ObservableCollection<T> rows, string what) where T : new()
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            p.Children.Add(SymbolButton("+", "Incluir " + what, (s, e) => AddRow(grid, rows)));
            var del = SymbolButton("✕", "Excluir as linhas selecionadas (ou tecla Delete)", (s, e) => RemoveSelected(grid, rows, what));
            del.Margin = new Thickness(0);
            p.Children.Add(del);
            return p;
        }

        /// <summary>Linha nova depois da selecionada (ou no fim), já selecionada e em edição.</summary>
        static void AddRow<T>(DataGrid grid, ObservableCollection<T> rows) where T : new()
        {
            grid.CommitEdit(DataGridEditingUnit.Row, true);
            var item = new T();
            var i = grid.SelectedItem is T sel ? rows.IndexOf(sel) + 1 : rows.Count;
            rows.Insert(Math.Max(0, Math.Min(i, rows.Count)), item);
            grid.SelectedItem = item;
            grid.ScrollIntoView(item);
            grid.CurrentCell = new DataGridCellInfo(item, grid.Columns.FirstOrDefault(c => !c.IsReadOnly) ?? grid.Columns[0]);
            grid.Focus();
            grid.BeginEdit();
        }

        /// <summary>Exclui as linhas selecionadas; com mais de uma, pergunta antes.</summary>
        static void RemoveSelected<T>(DataGrid grid, ObservableCollection<T> rows, string what)
        {
            grid.CancelEdit(DataGridEditingUnit.Row);
            var sel = grid.SelectedItems.OfType<T>().ToList();
            if (sel.Count == 0) return;
            if (sel.Count > 1 && MessageBox.Show(Window.GetWindow(grid), $"Excluir {sel.Count} linhas?", "MetroSP",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            var first = rows.IndexOf(sel[0]);
            foreach (var r in sel) rows.Remove(r);
            if (rows.Count > 0) grid.SelectedItem = rows[Math.Max(0, Math.Min(first, rows.Count - 1))];
        }

        static TextBlock Section(string text) { var t = Theme.Secao(text); t.Margin = new Thickness(0, 10, 0, 4); return t; }

        static TextBox Text(object source, string property, string tip = null)
        {
            var tb = new TextBox { MinWidth = 180, ToolTip = tip ?? "Vazio mantém o que a Autodesk gravou." };
            var binding = new Binding(property)
            {
                Source = source,
                TargetNullValue = "",
                UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
                ConverterCulture = CultureInfo.InvariantCulture
            };
            // Número aceita vírgula ou ponto: "-23,63" com a cultura invariante virava -2363 (vírgula de milhar).
            var type = source?.GetType().GetProperty(property)?.PropertyType;
            if (type == typeof(double?) || type == typeof(double)) binding.Converter = new NumeroConverter();
            tb.SetBinding(TextBox.TextProperty, binding);
            return tb;
        }

        /// <summary>PTL do Metrô (padrão, igual ao Revit) ou um EPSG digitado.</summary>
        static UIElement Ptl(SpatialConfig e)
        {
            var panel = new StackPanel();
            var check = new CheckBox
            {
                Content = "PTL do Metrô, sem EPSG",
                ToolTip = "Ligado (padrão): o IFC sai nas coordenadas do desenho (PTL do Metrô), sem EPSG, IfcProjectedCRS e " +
                          "IfcMapConversion, como no plugin do Revit. Assim o modelo encontra as estruturas do Revit.\n" +
                          "Desligado: vale o EPSG ao lado no IfcMapConversion.",
                Margin = new Thickness(0, 3, 0, 3)
            };
            check.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(SpatialConfig.CoordenadasPtl)) { Source = e });
            var epsg = Text(e, nameof(SpatialConfig.SistemaCoordenadas), "Só com o PTL desligado: EPSG do IfcMapConversion, ex.: EPSG:31983");
            epsg.SetBinding(UIElement.IsEnabledProperty, new Binding(nameof(SpatialConfig.CoordenadasPtl)) { Source = e, Converter = new InverterConverter() });
            panel.Children.Add(check);
            panel.Children.Add(epsg);
            return panel;
        }

        /// <summary>Campo obrigatório: atualiza a cada tecla e fica com a borda laranja enquanto vazio.</summary>
        static TextBox Required(object source, string property)
        {
            var tb = Text(source, property, "Obrigatório.");
            BindingOperations.ClearBinding(tb, TextBox.TextProperty);
            tb.SetBinding(TextBox.TextProperty, new Binding(property) { Source = source, TargetNullValue = "", UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            void Mark() => tb.BorderBrush = string.IsNullOrWhiteSpace(tb.Text) ? Theme.Solid(Theme.Laranja) : Theme.Solid(Color.FromRgb(0xBB, 0xBB, 0xBB));
            tb.TextChanged += (s, e) => Mark();
            tb.Loaded += (s, e) => Mark();
            return tb;
        }

        static Grid Form(params (string Label, UIElement Field)[] rows)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < rows.Length; i++)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var text = rows[i].Label;
                var l = Label(text.EndsWith(" *") ? text.Substring(0, text.Length - 2) : text);
                if (text.EndsWith(" *"))
                    l.Inlines.Add(new System.Windows.Documents.Run(" *") { Foreground = Theme.Solid(Theme.Laranja), FontWeight = FontWeights.SemiBold });
                l.Margin = new Thickness(0, 2, 8, 2);
                Grid.SetRow(l, i);
                g.Children.Add(l);
                var f = rows[i].Field;
                if (f is FrameworkElement fe) fe.Margin = new Thickness(0, 2, 0, 2);
                Grid.SetRow(f, i);
                Grid.SetColumn(f, 1);
                g.Children.Add(f);
            }
            return g;
        }

        void Status(string text) => _status.Text = text;
        void Warn(string text) => MessageBox.Show(this, text, "MetroSP", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ====================================================================== linhas das tabelas

    public class MappingRow : System.ComponentModel.INotifyPropertyChanged
    {
        string _noDesenho = "", _noDesenhoDica = "";
        bool _usada = true;
        /// <summary>Quantos nomes e objetos do desenho esta regra pega (ex.: "3 · 120 obj").</summary>
        public string NoDesenho => _noDesenho;
        public string NoDesenhoDica => _noDesenhoDica;
        public bool UsadaNoDesenho => _usada;

        public void DefinirUso(string texto, string dica, bool usada)
        {
            if (_noDesenho == texto && _noDesenhoDica == dica && _usada == usada) return;
            _noDesenho = texto; _noDesenhoDica = dica; _usada = usada;
            foreach (var p in new[] { nameof(NoDesenho), nameof(NoDesenhoDica), nameof(UsadaNoDesenho) })
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(p));
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        public string Name { get; set; }
        public string IfcExportAs { get; set; }
        public bool Export { get; set; } = true;
        JsonObject _extra;   // outras chaves do item, preservadas

        /// <summary>Classes do esquema em uso, para marcar as que estão fora dele.</summary>
        public static string[] Catalog;
        public bool ClasseInvalida => Catalog != null && !IfcCatalog.IsValid(IfcExportAs, Catalog);

        public static MappingRow From(JsonObject o)
        {
            var r = new MappingRow
            {
                Name = (string)o["Name"],
                IfcExportAs = (string)o["IfcExportAs"],
                Export = (bool?)o["Export"] ?? true,
                _extra = new JsonObject()
            };
            foreach (var kv in o.Where(kv => kv.Key != "Name" && kv.Key != "IfcExportAs" && kv.Key != "Export"))
                r._extra[kv.Key] = kv.Value?.DeepClone();
            return r;
        }

        public JsonObject ToJson()
        {
            var o = new JsonObject { ["Name"] = Name, ["IfcExportAs"] = IfcExportAs ?? "", ["Export"] = Export };
            if (_extra != null) foreach (var kv in _extra) o[kv.Key] = kv.Value?.DeepClone();
            return o;
        }
    }

    public class PropertyRow
    {
        public static readonly string[] Columns = { "IFCPropertySet Name", "IFCProperty Name", "Active", "Source", "Group", "Civil 3D Name" };

        public string PropertySet { get; set; }
        public string Property { get; set; }
        public bool Active { get; set; } = true;
        public string Source { get; set; }
        public string Group { get; set; }
        public string CivilName { get; set; }
        List<string> _extra = new List<string>();

        public static PropertyRow FromCells(List<string> c)
        {
            string At(int i) => i < c.Count ? c[i] : "";
            return new PropertyRow
            {
                PropertySet = At(0),
                Property = At(1),
                Active = !string.Equals(At(2).Trim(), "false", StringComparison.OrdinalIgnoreCase),
                Source = At(3),
                Group = At(4),
                CivilName = At(5),
                _extra = c.Skip(6).ToList()
            };
        }

        public IEnumerable<string> ToCells() =>
            new[] { PropertySet, Property, Active ? "True" : "False", Source, Group, CivilName }.Concat(_extra);
    }

    public class KeyValueRow
    {
        public string Chave { get; set; }
        public string Valor { get; set; }
    }

    static class Csv
    {
        public static List<string> Split(string line, char sep)
        {
            var cells = new List<string>();
            var sb = new StringBuilder();
            bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (q)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else if (c == '"') q = false;
                    else sb.Append(c);
                }
                else if (c == '"') q = true;
                else if (c == sep) { cells.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            cells.Add(sb.ToString());
            return cells;
        }

        public static string Quote(string s, char sep)
        {
            s ??= "";
            return s.IndexOfAny(new[] { sep, '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }
    }

    /// <summary>Árvore editável de um JSON qualquer: booleanos viram caixas, números e textos viram campos.</summary>
    static class JsonTree
    {
        public static IEnumerable<TreeViewItem> Build(JsonNode node)
        {
            if (node is JsonObject o)
                foreach (var kv in o.ToList()) yield return Item(kv.Key, kv.Value, v => o[kv.Key] = v);
            else if (node is JsonArray a)
                for (int i = 0; i < a.Count; i++)
                {
                    var idx = i;
                    yield return Item("[" + i + "]", a[i], v => a[idx] = v);
                }
        }

        static TreeViewItem Item(string key, JsonNode value, Action<JsonNode> set)
        {
            var item = new TreeViewItem { IsExpanded = true };
            if (value is JsonObject || value is JsonArray)
            {
                item.Header = key;
                foreach (var child in Build(value)) item.Items.Add(child);
                return item;
            }

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = key, MinWidth = 300, VerticalAlignment = VerticalAlignment.Center });

            var kind = value?.GetValueKind();
            if (kind == System.Text.Json.JsonValueKind.True || kind == System.Text.Json.JsonValueKind.False)
            {
                var cb = new CheckBox { IsChecked = value.GetValue<bool>(), VerticalAlignment = VerticalAlignment.Center };
                cb.Click += (s, e) => set(JsonValue.Create(cb.IsChecked == true));
                row.Children.Add(cb);
            }
            else if (kind == System.Text.Json.JsonValueKind.Number)
            {
                var tb = new TextBox { Text = value.ToJsonString(), MinWidth = 120 };
                tb.LostFocus += (s, e) =>
                {
                    if (double.TryParse(tb.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                        set(d == Math.Floor(d) && Math.Abs(d) < 1e15 ? JsonValue.Create((long)d) : JsonValue.Create(d));
                    else tb.Text = value.ToJsonString();
                };
                row.Children.Add(tb);
            }
            else
            {
                var tb = new TextBox { Text = kind == System.Text.Json.JsonValueKind.String ? value.GetValue<string>() : "", MinWidth = 320 };
                tb.LostFocus += (s, e) => set(JsonValue.Create(tb.Text));
                row.Children.Add(tb);
            }
            item.Header = row;
            return item;
        }
    }

    /// <summary>Número com vírgula ou ponto decimal; vazio = sem valor; texto inválido fica marcado no campo.</summary>
    class NumeroConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is double d ? d.ToString("0.#########", CultureInfo.InvariantCulture) : "";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = (value as string ?? "").Trim();
            if (s.Length == 0) return null;
            if (s.Count(c => c == ',') + s.Count(c => c == '.') > 1) return DependencyProperty.UnsetValue;
            return double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
                ? n : DependencyProperty.UnsetValue;
        }
    }

    class InverterConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => !(value is bool b && b);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => !(value is bool b && b);
    }
}
