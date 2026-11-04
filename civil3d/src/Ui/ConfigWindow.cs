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
        readonly ObservableCollection<BuildingInfo> _buildings = new ObservableCollection<BuildingInfo>();
        readonly ObservableCollection<KeyValueRow> _projectProps = new ObservableCollection<KeyValueRow>();

        public static readonly string[] KnownSections = { "MapCADLayerName", "MapBlockName", "MapBaselineName" };

        public static readonly string[] CommonIfcClasses =
        {
            "IfcAnnotation", "IfcBuiltElement", "IfcBuiltElement.USERDEFINED", "IfcBuildingElementProxy",
            "IfcCourse.BALLASTBED", "IfcCourse.PAVEMENT", "IfcEarthworksFill.EMBANKMENT", "IfcEarthworksFill.SUBGRADE",
            "IfcElementAssembly.TRACKPANEL", "IfcElementAssembly.TURNOUTPANEL", "IfcGeographicElement.TERRAIN",
            "IfcRail.RAIL", "IfcRail.CHECKRAIL", "IfcRailway", "IfcRailwayPart.TRACK", "IfcRailwayPart.PLAINTRACK",
            "IfcRailwayPart.TURNOUTTRACK", "IfcRailwayPart.SUBSTRUCTURE", "IfcRoad", "IfcRoadPart", "IfcSlab", "IfcWall",
            "IfcColumn", "IfcBeam", "IfcTrackElement.SLEEPER", "IfcTrackElement.VEHICLESTOP", "IfcTrackElement.FROG"
        };

        public ConfigWindow(string dwgDir)
        {
            _dwgDir = dwgDir;
            Title = "MetroSP - Configuração IFC";
            Width = 980;
            Height = 680;
            MinWidth = 760;
            MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var saveClose = Btn("Salvar e fechar", (s, e) => SaveAll(close: true));
            saveClose.Style = Theme.PrimaryButton;
            saveClose.IsDefault = true;
            Theme.Apply(this, "Configuração IFC", "Mapeamento de classes, property sets, projeto, terreno, edificações e alinhamento",
                _tabs, Theme.Rodape(_status,
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

            _buildings.Clear();
            foreach (var b in _metro.Estrutura.Edificacoes) _buildings.Add(b);
            _projectProps.Clear();
            foreach (var kv in _metro.PropriedadesProjeto) _projectProps.Add(new KeyValueRow { Chave = kv.Key, Valor = kv.Value });

            LoadCsv();
        }

        void SaveAll(bool close)
        {
            try
            {
                CommitMappingSection();
                ConfigPackage.SaveJson(ConfigKind.ObjectMapping, _mapping);
                if (_autodeskConfig != null) ConfigPackage.SaveJson(ConfigKind.AutodeskConfig, _autodeskConfig);
                if (_templatesText != null && !string.IsNullOrWhiteSpace(_templatesText.Text))
                    ConfigPackage.Write(ConfigKind.PropertyTemplates, Encoding.UTF8.GetBytes(_templatesText.Text));
                if (_propertyRows.Count > 0 || File.Exists(ConfigPackage.PathOf(ConfigKind.PropertyMappingCsv)))
                    SaveCsv();

                _metro.Estrutura.Edificacoes = _buildings.Where(b => !string.IsNullOrWhiteSpace(b.Nome)).ToList();
                _metro.PropriedadesProjeto = _projectProps.Where(r => !string.IsNullOrWhiteSpace(r.Chave))
                    .GroupBy(r => r.Chave.Trim()).ToDictionary(g => g.Key, g => g.Last().Valor ?? "");
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
            _tabs.Items.Add(new TabItem { Header = "Pacote", Content = PackageTab() });
            _tabs.Items.Add(new TabItem { Header = "Mapeamento de classes", Content = MappingTab() });
            _tabs.Items.Add(new TabItem { Header = "Property sets", Content = PropertiesTab() });
            _tabs.Items.Add(new TabItem { Header = "Projeto, terreno e edificações", Content = SpatialTab() });
            _tabs.Items.Add(new TabItem { Header = "Alinhamento", Content = AlignmentTab() });
            _tabs.Items.Add(new TabItem { Header = "Configuração Autodesk", Content = AutodeskTab() });
        }

        // ================================================================== aba Pacote

        UIElement PackageTab()
        {
            var panel = new DockPanel { Margin = new Thickness(10) };
            var cfg = Settings.Load();
            var info = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
                Text = "Pacote ativo: " + ConfigPackage.ActiveDir + "\n" +
                       "Fonte antes de exportar: " + (cfg.UsesForma ? "Autodesk Forma (baixa a versão mais recente a cada exportação)" : "este pacote local") + "\n" +
                       "Os arquivos da Autodesk são copiados para a pasta do desenho no formato que o IFC Extension lê; " +
                       "MetroSP.ifc.json é usado no pós-processamento (alinhamento 3D, projeto, terreno, edificações)."
            };
            DockPanel.SetDock(info, Dock.Top);
            panel.Children.Add(info);

            var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(actions, Dock.Bottom);
            actions.Children.Add(Btn("Importar JSON/CSV...", (s, e) => ImportFiles()));
            actions.Children.Add(Btn("Exportar pacote...", (s, e) => ExportPackage()));
            actions.Children.Add(Btn("Importar modelos da Autodesk", (s, e) => ImportAutodeskSamples()));
            actions.Children.Add(Btn("Baixar do Forma", async (s, e) => await DownloadFromForma()));
            actions.Children.Add(Btn("Restaurar padrão MetroSP", (s, e) => RestoreDefaults()));
            actions.Children.Add(Btn("Abrir pasta", (s, e) => Process.Start(new ProcessStartInfo("explorer.exe", "\"" + ConfigPackage.ActiveDir + "\"") { UseShellExecute = true })));
            panel.Children.Add(actions);

            var list = new ListView();
            var grid = new GridView();
            grid.Columns.Add(new GridViewColumn { Header = "Tipo", Width = 230, DisplayMemberBinding = new Binding("Tipo") });
            grid.Columns.Add(new GridViewColumn { Header = "Arquivo", Width = 300, DisplayMemberBinding = new Binding("Arquivo") });
            grid.Columns.Add(new GridViewColumn { Header = "Alterado em", Width = 160, DisplayMemberBinding = new Binding("Data") });
            list.View = grid;
            list.ItemsSource = ConfigPackage.List()
                .Select(f => new { Tipo = KindLabel(f.Kind), Arquivo = Path.GetFileName(f.Path), Data = f.Modified.ToString("dd/MM/yyyy HH:mm") })
                .ToList();
            panel.Children.Add(list);
            return panel;
        }

        static string KindLabel(ConfigKind k)
        {
            switch (k)
            {
                case ConfigKind.AutodeskConfig: return "Configuração da exportação (Autodesk)";
                case ConfigKind.ObjectMapping: return "Mapeamento de classes IFC";
                case ConfigKind.PropertyTemplates: return "Templates de property sets";
                case ConfigKind.PropertyMappingCsv: return "Mapeamento de propriedades";
                case ConfigKind.MetroSP: return "Opções MetroSP";
                default: return k.ToString();
            }
        }

        void ImportFiles()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Importar configurações IFC",
                Filter = "Configuração IFC (*.json;*.csv)|*.json;*.csv",
                Multiselect = true
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                var done = ConfigPackage.Import(dlg.FileNames);
                ReloadEverything("Importado: " + string.Join("; ", done));
            }
            catch (Exception ex) { Warn(ex.Message); }
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

        UIElement MappingTab()
        {
            var panel = new DockPanel { Margin = new Thickness(10) };

            var top = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(top, Dock.Top);
            top.Children.Add(Label("Critério:"));
            _mappingSection = new ComboBox { Width = 220, IsEditable = true, Margin = new Thickness(4, 0, 12, 0) };
            foreach (var s in KnownSections.Union(_mapping.Where(kv => kv.Value is JsonArray).Select(kv => kv.Key))) _mappingSection.Items.Add(s);
            _mappingSection.SelectionChanged += (s, e) => SwitchSection(_mappingSection.SelectedItem as string);
            _mappingSection.LostFocus += (s, e) => SwitchSection(_mappingSection.Text);
            top.Children.Add(_mappingSection);

            var classes = new ComboBox { Width = 260, IsEditable = true, Margin = new Thickness(4, 0, 4, 0), ItemsSource = CommonIfcClasses };
            top.Children.Add(Label("Classe IFC:"));
            top.Children.Add(classes);

            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = true,
                ItemsSource = _mappingRows,
                HeadersVisibility = DataGridHeadersVisibility.Column
            };
            grid.Columns.Add(new DataGridTextColumn { Header = "Nome (expressão regular)", Binding = new Binding("Name"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Exportar como (Classe.TIPO)", Binding = new Binding("IfcExportAs"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Exportar", Binding = new Binding("Export"), Width = 70 });

            top.Children.Add(Btn("Aplicar à linha", (s, e) =>
            {
                if (grid.SelectedItem is MappingRow r) { r.IfcExportAs = classes.Text; grid.Items.Refresh(); }
            }));
            top.Children.Add(Btn("Subir", (s, e) => Move(grid, -1)));
            top.Children.Add(Btn("Descer", (s, e) => Move(grid, +1)));
            top.Children.Add(Btn("Remover", (s, e) => { if (grid.SelectedItem is MappingRow r) _mappingRows.Remove(r); }));
            panel.Children.Add(top);

            var note = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = Theme.Solid(Theme.Cinza),
                Margin = new Thickness(0, 6, 0, 0),
                Text = "A Autodesk avalia as regras de baixo para cima e usa a primeira que casar. Exportar desmarcado tira o objeto do IFC. " +
                       "Propriedade estendida \"IFC::IfcExportAs\" no objeto tem prioridade sobre estas regras."
            };
            DockPanel.SetDock(note, Dock.Bottom);
            panel.Children.Add(note);
            panel.Children.Add(grid);

            _currentSection = null;
            _mappingSection.SelectedIndex = 0;
            return panel;
        }

        void SwitchSection(string section)
        {
            if (string.IsNullOrWhiteSpace(section) || section == _currentSection) return;
            CommitMappingSection();
            _currentSection = section.Trim();
            _mappingRows.Clear();
            if (_mapping[_currentSection] is JsonArray arr)
                foreach (var item in arr.OfType<JsonObject>()) _mappingRows.Add(MappingRow.From(item));
            if (!_mappingSection.Items.Contains(_currentSection)) _mappingSection.Items.Add(_currentSection);
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
            var tabs = new TabControl { Margin = new Thickness(10) };

            var panel = new DockPanel { Margin = new Thickness(6) };
            var note = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = Theme.Solid(Theme.Cinza),
                Margin = new Thickness(0, 0, 0, 6),
                Text = "Mapeamento de propriedades (IFCInfraExportPropertyMapping.csv): qual propriedade do Civil 3D vai para qual " +
                       "property set e propriedade IFC. Source: Properties, PartData, General ou PropertySet. " +
                       "Para gerar a lista completa da Autodesk, use IFC Infrastructure > Save Property Templates e importe na aba Pacote."
            };
            DockPanel.SetDock(note, Dock.Top);
            panel.Children.Add(note);
            var grid = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = true, ItemsSource = _propertyRows };
            grid.Columns.Add(new DataGridTextColumn { Header = "Property set IFC", Binding = new Binding("PropertySet"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Propriedade IFC", Binding = new Binding("Property"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Ativa", Binding = new Binding("Active"), Width = 50 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Source", Binding = new Binding("Source"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Group", Binding = new Binding("Group"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Nome no Civil 3D", Binding = new Binding("CivilName"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            panel.Children.Add(grid);
            tabs.Items.Add(new TabItem { Header = "Mapeamento (CSV)", Content = panel });

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
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            var tplPanel = new DockPanel { Margin = new Thickness(6) };
            var tplNote = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = Theme.Solid(Theme.Cinza),
                Margin = new Thickness(0, 0, 0, 6),
                Text = "Templates dos property sets (IFCInfraExportPropertyMapping.json): grupos, propriedades e tipos IFC. " +
                       "Editor de texto com validação de JSON ao salvar."
            };
            DockPanel.SetDock(tplNote, Dock.Top);
            tplPanel.Children.Add(tplNote);
            tplPanel.Children.Add(_templatesText);
            tabs.Items.Add(new TabItem { Header = "Templates (JSON)", Content = tplPanel });
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

            var stack = new StackPanel { Margin = new Thickness(10) };
            stack.Children.Add(Note("Campos vazios mantêm o que a Autodesk gravou. Aplicado ao IFC depois da exportação."));

            stack.Children.Add(Section("IfcProject"));
            stack.Children.Add(Form(
                ("Nome", Text(e.Projeto, nameof(ProjectInfo.Nome))),
                ("Nome longo", Text(e.Projeto, nameof(ProjectInfo.NomeLongo))),
                ("Descrição", Text(e.Projeto, nameof(ProjectInfo.Descricao))),
                ("Fase", Text(e.Projeto, nameof(ProjectInfo.Fase)))));

            stack.Children.Add(Section("IfcSite"));
            stack.Children.Add(Form(
                ("Nome", Text(e.Terreno, nameof(SiteInfo.Nome))),
                ("Nome longo", Text(e.Terreno, nameof(SiteInfo.NomeLongo))),
                ("Descrição", Text(e.Terreno, nameof(SiteInfo.Descricao))),
                ("Latitude (graus, sul negativo)", Text(e.Terreno, nameof(SiteInfo.Latitude))),
                ("Longitude (graus, oeste negativo)", Text(e.Terreno, nameof(SiteInfo.Longitude))),
                ("Elevação de referência (m)", Text(e.Terreno, nameof(SiteInfo.Elevacao))),
                ("Matrícula (LandTitleNumber)", Text(e.Terreno, nameof(SiteInfo.Matricula))),
                ("Sistema de coordenadas (IfcMapConversion)", Text(e, nameof(SpatialConfig.SistemaCoordenadas)))));

            stack.Children.Add(Section("IfcBuilding (estações, edificações) - agregados ao IfcSite"));
            var bgrid = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = true, ItemsSource = _buildings, MinHeight = 110, Margin = new Thickness(0, 0, 0, 6) };
            bgrid.Columns.Add(new DataGridTextColumn { Header = "Nome", Binding = new Binding("Nome"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            bgrid.Columns.Add(new DataGridTextColumn { Header = "Nome longo", Binding = new Binding("NomeLongo"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            bgrid.Columns.Add(new DataGridTextColumn { Header = "Descrição", Binding = new Binding("Descricao"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            bgrid.Columns.Add(new DataGridTextColumn { Header = "Elevação ref. (m)", Binding = new Binding("ElevacaoReferencia") { TargetNullValue = "" }, Width = 120 });
            stack.Children.Add(bgrid);

            stack.Children.Add(Section("Pset MetroSP_Projeto (no IfcProject)"));
            var pgrid = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = true, ItemsSource = _projectProps, MinHeight = 110 };
            pgrid.Columns.Add(new DataGridTextColumn { Header = "Propriedade", Binding = new Binding("Chave"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            pgrid.Columns.Add(new DataGridTextColumn { Header = "Valor", Binding = new Binding("Valor"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            stack.Children.Add(pgrid);

            return new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        // ================================================================== aba Alinhamento

        UIElement AlignmentTab()
        {
            var a = _metro.Alinhamento;
            var stack = new StackPanel { Margin = new Thickness(10) };
            stack.Children.Add(Note("O Civil 3D grava o alinhamento plano na cota 0. Com a opção abaixo, depois do IFCINFRAEXPORT o MetroSP " +
                                    "troca o eixo de cada IfcAlignment por um eixo 3D com as cotas do perfil e acrescenta o IfcAlignmentVertical " +
                                    "se ele não existir, no mesmo arquivo."));
            var apply = new CheckBox { Content = "Aplicar alinhamento 3D no IFC exportado", Margin = new Thickness(0, 6, 0, 6) };
            apply.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(nameof(AlignmentOptions.AplicarNoIfc)) { Source = a });
            stack.Children.Add(apply);
            stack.Children.Add(Form(("Passo dos pontos do eixo 3D (m)", Text(a, nameof(AlignmentOptions.Passo)))));

            stack.Children.Add(Section("Perfis preferidos (um por linha, expressão regular, em ordem)"));
            var prefs = new TextBox
            {
                Text = string.Join(Environment.NewLine, a.PerfisPreferidos ?? new List<string>()),
                AcceptsReturn = true,
                Height = 110,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            prefs.TextChanged += (s, e) => a.PerfisPreferidos = prefs.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToList();
            stack.Children.Add(prefs);
            stack.Children.Add(Note("Quando o alinhamento tem mais de um perfil de projeto, usa o primeiro que casar com esta lista; se nenhum casar, pergunta na linha de comando."));
            return stack;
        }

        // ================================================================== aba Configuração Autodesk

        UIElement AutodeskTab()
        {
            var panel = new DockPanel { Margin = new Thickness(10) };
            var note = Note("IfcInfraConfiguration.json do IFC Extension: esquema IFC, cabeçalho, objetos, quantidades, alinhamentos e demais opções. " +
                            "As chaves seguem o arquivo da Autodesk; o editor mostra o que estiver no arquivo.");
            DockPanel.SetDock(note, Dock.Top);
            panel.Children.Add(note);

            if (_autodeskConfig == null)
            {
                var empty = new StackPanel();
                empty.Children.Add(Note("O pacote ainda não tem IfcInfraConfiguration.json. Rode IFCInfraSaveConfig no Civil 3D para a Autodesk gerar o modelo " +
                                        "da versão instalada e depois use \"Importar modelos da Autodesk\" na aba Pacote."));
                empty.Children.Add(Btn("Importar modelos da Autodesk", (s, e) => ImportAutodeskSamples()));
                panel.Children.Add(empty);
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

        static TextBlock Note(string text) => new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Theme.Solid(Theme.Cinza), Margin = new Thickness(0, 0, 0, 6) };

        static TextBlock Section(string text) { var t = Theme.Secao(text); t.Margin = new Thickness(0, 10, 0, 4); return t; }

        static TextBox Text(object source, string property)
        {
            var tb = new TextBox { MinWidth = 320 };
            tb.SetBinding(TextBox.TextProperty, new Binding(property)
            {
                Source = source,
                TargetNullValue = "",
                UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
                ConverterCulture = CultureInfo.InvariantCulture
            });
            return tb;
        }

        static Grid Form(params (string Label, UIElement Field)[] rows)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < rows.Length; i++)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var l = Label(rows[i].Label);
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

    public class MappingRow
    {
        public string Name { get; set; }
        public string IfcExportAs { get; set; }
        public bool Export { get; set; } = true;
        JsonObject _extra;   // outras chaves do item, preservadas

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
}
