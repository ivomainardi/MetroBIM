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
            toolbar.Children.Add(Tool("Classes IFC…", "Em que classe IFC cada layer, bloco e baseline sai, no IFC4 e no IFC 4.3.", (s, e) =>
            {
                new ClassesIfcWindow { Owner = this }.ShowDialog();
                _mapping = ConfigPackage.LoadJson(ConfigKind.ObjectMapping) as JsonObject ?? new JsonObject();
            }));
            toolbar.Children.Add(Tool("Adicionar perfis…", "Abre a janela Perfis, onde se trazem os arquivos de configuração IFC (e os outros perfis): arraste ou escolha os arquivos.", (s, e) => AbrirPadroes()));
            toolbar.Children.Add(Tool("Exportar…", "Grava todos os arquivos do perfil de exportação numa pasta.", (s, e) => ExportPackage()));
            if (Settings.Load().UsesForma)
                toolbar.Children.Add(Tool("Baixar do Forma", "Troca o perfil de exportação pela versão mais recente do Forma.", async (s, e) => await DownloadFromForma()));
            toolbar.Children.Add(Tool("Restaurar perfil", "Volta o perfil a ficar como veio no plugin (o MetroSP, se foi criado aqui).", (s, e) => RestoreDefaults()));
            body.Children.Add(toolbar);
            body.Children.Add(_tabs);

            Theme.Apply(this, somenteProjeto ? "Projeto IFC" : "Configuração IFC",
                somenteProjeto ? "IfcProject, IfcSite, instalações (IfcRailway, IfcBuilding) e Pset MetroSP_Projeto, aplicados na exportação"
                    : "Perfil de exportação " + ConfigPackage.PerfilAtivo + (Settings.Load().UsesForma ? " (sincronizado com o Forma)" : ""),
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

            try
            {
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
                case ConfigKind.ObjectMapping: return "Classes IFC (IFC 4.3)";
                case ConfigKind.ObjectMappingIfc4: return "Classes IFC (IFC4)";
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
            ReloadEverything("Perfil de exportação relido.");
        }

        void ExportPackage()
        {
            SaveAll(close: false);
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Pasta para exportar o perfil de exportação" };
            if (dlg.ShowDialog(this) != true) return;
            ConfigPackage.ExportTo(dlg.FolderName);
            Status("Perfil de exportação gravado em " + dlg.FolderName);
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
                Warn("Não achei IfcInfraConfiguration.json nem IfcInfraExportMapping.json na pasta do modelo nem em " +
                     Commands.ProgramDataDir + ".\n\nRode IFCInfraSaveConfig e IFCInfraSaveMappingConfig no Civil 3D " +
                     "(eles gravam os modelos da Autodesk na pasta do modelo) e clique de novo.");
                return;
            }
            if (MessageBox.Show(this, "Substituir no perfil de exportação ativo:\n\n" + string.Join("\n", found), "MetroSP",
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
            string perfil = ConfigPackage.PerfilAtivo;
            string base_ = ConfigPackage.PreConfigurado(perfil) ? "como veio no plugin" : "igual ao perfil MetroSP";
            if (MessageBox.Show(this, $"Voltar o perfil {perfil} a ficar {base_}? Os arquivos atuais dele serão apagados " +
                                      "(exporte o perfil antes, se quiser guardar).", "MetroSP",
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            ConfigPackage.RestoreDefaults();
            ReloadEverything("Perfil " + perfil + " restaurado.");
        }

        // ================================================================== aba Mapeamento

        static readonly Dictionary<string, string> SectionLabels = new Dictionary<string, string>
        {
            ["MapCADLayerName"] = "Por layer",
            ["MapBlockName"] = "Por bloco",
            ["MapBaselineName"] = "Por baseline (corredor)"
        };

        // Lista única: cada layer (bloco, baseline) do modelo é uma linha, com a regra do padrão que vale para ele;
        // as regras que não pegam nada no modelo vêm no fim, como "só no padrão". As regras continuam em _mappingRows,
        // na ordem do padrão (vale a primeira que casar, de cima para baixo).

        readonly ObservableCollection<LinhaClasse> _linhas = new ObservableCollection<LinhaClasse>();
        FilterableGrid _lista;
        Dictionary<string, List<ItemDesenho>> _modelo;
        TextBlock _resumoLista;
        CheckBox _soUsados;
        Button _corrigir;
        ComboBox _classes;

        UIElement MappingTab()
        {
            var panel = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };

            var top = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(top, Dock.Top);
            _mappingSection = new ComboBox { Width = 150, Margin = new Thickness(0, 0, 8, 4) };
            Theme.Digitavel(_mappingSection);
            foreach (var key in KnownSections.Union(_mapping.Where(kv => kv.Value is JsonArray).Select(kv => kv.Key)))
                _mappingSection.Items.Add(new ComboBoxItem { Content = SectionLabels.TryGetValue(key, out var l) ? l : key, Tag = key, ToolTip = key });
            _mappingSection.SelectionChanged += (s, e) => SwitchSection((_mappingSection.SelectedItem as ComboBoxItem)?.Tag as string);
            top.Children.Add(_mappingSection);

            _classes = ClassPicker(IfcClasses);
            _classes.Width = 240;
            _classes.Margin = new Thickness(0, 0, 0, 4);
            _classes.ToolTip = $"Todas as classes do {IfcCatalog.Label(IfcVersion)}, o esquema escolhido na configuração da Autodesk. " +
                               "Digite parte do nome (ex.: TRACKSLAB) para filtrar; só vale uma classe da lista.";
            top.Children.Add(_classes);

            var apply = Btn("Aplicar", (s, e) => AplicarClasse());
            apply.Margin = new Thickness(6, 0, 12, 4);
            apply.ToolTip = "Põe a classe nas linhas selecionadas. Se a linha segue uma regra geral (ex.: ^G-3D-.*), muda a regra " +
                            "e todos os layers dela. Layer sem regra ganha uma regra só para ele.";
            top.Children.Add(apply);
            top.Children.Add(SymbolButton("↑", "Sobe a regra da linha selecionada (a de cima vale primeiro)", (s, e) => MoverRegra(-1)));
            top.Children.Add(SymbolButton("↓", "Desce a regra da linha selecionada", (s, e) => MoverRegra(+1)));
            top.Children.Add(SymbolButton("+", "Nova regra (nome ou expressão regular, ex.: ^U-3D-)", (s, e) => NovaRegra()));
            top.Children.Add(SymbolButton("✕", "Exclui do perfil a regra das linhas selecionadas", (s, e) => ExcluirRegras()));
            _soUsados = new CheckBox
            {
                Content = "Só usados no modelo", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 4),
                ToolTip = "Esconde layers sem objeto no espaço do modelo."
            };
            _soUsados.Click += (s, e) => _lista.Refresh();
            top.Children.Add(_soUsados);
            _corrigir = Tool("Corrigir classes", "", (s, e) => CorrigirClasses());
            _corrigir.VerticalAlignment = VerticalAlignment.Center;
            _corrigir.Margin = new Thickness(12, 0, 0, 4);
            top.Children.Add(_corrigir);
            panel.Children.Add(top);

            _resumoLista = Theme.Detalhe();
            _resumoLista.Margin = new Thickness(0, 4, 0, 0);
            DockPanel.SetDock(_resumoLista, Dock.Bottom);
            panel.Children.Add(_resumoLista);

            MappingRow.Catalog = IfcClasses;
            _lista = new FilterableGrid(_linhas, typeof(LinhaClasse));
            _lista.AddColumn("Nome", nameof(LinhaClasse.Nome), 170, fill: true);
            _lista.AddColumn("Onde", nameof(LinhaClasse.Onde), 110);
            var obj = _lista.AddColumn("Objetos", nameof(LinhaClasse.Objetos), 66);
            obj.SortMemberPath = nameof(LinhaClasse.ObjetosNum);
            obj.ElementStyle = new Style(typeof(TextBlock), obj.ElementStyle) { Setters = { new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right) } };
            var classe = _lista.AddColumn("Classe IFC", nameof(LinhaClasse.Classe), 200);
            var invalida = new Style(typeof(TextBlock), classe.ElementStyle);
            var trig = new DataTrigger { Binding = new Binding(nameof(LinhaClasse.ClasseInvalida)), Value = true };
            trig.Setters.Add(new Setter(TextBlock.ForegroundProperty, Brushes.Firebrick));
            trig.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
            trig.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, "Classe fora do esquema: escolha uma da lista e clique Aplicar (ou Corrigir classes)."));
            invalida.Triggers.Add(trig);
            classe.ElementStyle = invalida;

            var marca = new FrameworkElementFactory(typeof(CheckBox));
            marca.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(LinhaClasse.Exportar)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            marca.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            marca.SetValue(FrameworkElement.ToolTipProperty, "Desmarcado: não vai para o IFC. Muda a regra (todos os layers dela).");
            _lista.Grid.Columns.Insert(4, new DataGridTemplateColumn { Header = "Exportar", CellTemplate = new DataTemplate { VisualTree = marca }, Width = 62 });
            _lista.AddColumn("Regra que vale", nameof(LinhaClasse.Regra), 150);
            var ordem = _lista.AddColumn("Ordem", nameof(LinhaClasse.Ordem), 56);
            ordem.ElementStyle = new Style(typeof(TextBlock), ordem.ElementStyle) { Setters = { new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right) } };
            _lista.ExtraFilter = o => o is LinhaClasse l && (_soUsados.IsChecked != true || l.Item == null || l.Item.Objetos > 0);
            _lista.Filtered += AtualizarResumo;
            _lista.Grid.MouseDoubleClick += (s, e) => EditarRegra();
            _lista.Grid.ToolTip = "Clique duas vezes para editar a regra da linha. Vale a primeira regra que casar, de cima para baixo (coluna Ordem).";

            var linha = new Style(typeof(DataGridRow), _lista.Grid.RowStyle);
            var sem = new DataTrigger { Binding = new Binding(nameof(LinhaClasse.SemRegra)), Value = true };
            sem.Setters.Add(new Setter(Control.BackgroundProperty, Theme.LinhaProblema));
            sem.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, "Nenhuma regra do perfil pega este nome: sai com a classe que a Autodesk escolher."));
            linha.Triggers.Add(sem);
            var fora = new DataTrigger { Binding = new Binding(nameof(LinhaClasse.Exportar)), Value = false };
            fora.Setters.Add(new Setter(Control.ForegroundProperty, Theme.Solid(Theme.Cinza)));
            linha.Triggers.Add(fora);
            _lista.Grid.RowStyle = linha;
            panel.Children.Add(_lista);

            _currentSection = null;
            _mappingSection.SelectedIndex = 0;
            return panel;
        }

        /// <summary>Lê o modelo (na primeira vez) e monta a lista da seção atual.</summary>
        void Recalcular()
        {
            if (_lista == null || _currentSection == null) return;
            if (_modelo == null)
            {
                try { _modelo = ConteudoDesenho.Ler(); }
                catch (Exception ex) { _modelo = new Dictionary<string, List<ItemDesenho>>(); Status("Não consegui ler o modelo: " + ex.Message); }
            }
            var selecionadas = new HashSet<string>(_lista.Grid.SelectedItems.OfType<LinhaClasse>().Select(l => l.Chave));
            var regras = _mappingRows.Where(r => !string.IsNullOrWhiteSpace(r.Name)).ToList();
            bool le = ConteudoDesenho.Le(_currentSection);
            var usadas = new HashSet<MappingRow>();
            var novas = new List<LinhaClasse>();
            if (le && _modelo.TryGetValue(_currentSection, out var itens))
                foreach (var i in itens)
                {
                    var vale = regras.FirstOrDefault(r => Casa(r.Name, i.Nome));
                    if (vale != null) usadas.Add(vale);
                    novas.Add(new LinhaClasse(this, i, vale, vale == null ? 0 : regras.IndexOf(vale) + 1));
                }
            foreach (var r in regras.Where(r => !usadas.Contains(r)))
                novas.Add(new LinhaClasse(this, null, r, regras.IndexOf(r) + 1) { SoPadrao = le });

            _linhas.Clear();
            foreach (var l in novas) _linhas.Add(l);
            _lista.Refresh();
            foreach (var l in _lista.VisibleItems<LinhaClasse>().Where(l => selecionadas.Contains(l.Chave)))
                _lista.Grid.SelectedItems.Add(l);

            CommitMappingSection();   // só passa a tela para o JSON (não grava), para conferir as classes
            var corrigiveis = Corrigiveis();
            _corrigir.Visibility = corrigiveis.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _corrigir.Content = corrigiveis.Count == 1 ? "Corrigir 1 classe" : $"Corrigir {corrigiveis.Count} classes";
            _corrigir.ToolTip = "Troca as classes fora do esquema pela correção óbvia: " +
                                string.Join("; ", corrigiveis.Take(4).Select(x => $"{x.De} → {x.Para}")) + ".";
        }

        void AtualizarResumo()
        {
            if (_resumoLista == null) return;
            var palavra = ConteudoDesenho.Palavra(_currentSection);
            int regras = _mappingRows.Count(r => !string.IsNullOrWhiteSpace(r.Name));
            if (!ConteudoDesenho.Le(_currentSection))
            {
                _resumoLista.Text = $"{regras} regra(s). O plugin lê do modelo só layers, blocos e baselines de corredor.";
                return;
            }
            var usados = _linhas.Where(l => l.Item != null && l.Item.Objetos > 0).ToList();
            int sem = usados.Count(l => l.SemRegra);
            int soPadrao = _linhas.Count(l => l.SoPadrao);
            _resumoLista.Text = $"{usados.Count} {palavra.Varios} com objetos no modelo · {sem} sem regra · {soPadrao} regra(s) só no perfil";
        }

        /// <summary>Linhas selecionadas, com aviso se não houver.</summary>
        List<LinhaClasse> Selecionadas(string oQue)
        {
            var l = _lista.Grid.SelectedItems.OfType<LinhaClasse>().ToList();
            if (l.Count == 0) Status("Selecione as linhas " + oQue + ".");
            return l;
        }

        /// <summary>Regra só para este nome, no topo (vale primeiro).</summary>
        MappingRow RegraPropria(ItemDesenho i, string classe, bool exportar)
        {
            var r = new MappingRow { Name = "^" + System.Text.RegularExpressions.Regex.Escape(i.Nome) + "$", IfcExportAs = classe, Export = exportar };
            _mappingRows.Insert(0, r);
            return r;
        }

        void AplicarClasse()
        {
            var escolhida = _classes.SelectedItem as string;
            if (escolhida == null) { Status("Escolha uma classe da lista."); return; }
            string classe = escolhida == SemClasse ? "" : escolhida;
            var linhas = Selecionadas("que vão receber a classe");
            if (linhas.Count == 0) return;
            var mudadas = new HashSet<MappingRow>();
            int novas = 0;
            foreach (var l in linhas)
            {
                if (l.RegraObj != null) { l.RegraObj.IfcExportAs = classe; mudadas.Add(l.RegraObj); }
                else { RegraPropria(l.Item, classe, true); novas++; }
            }
            int afetados = _linhas.Count(l => l.Item != null && l.RegraObj != null && mudadas.Contains(l.RegraObj));
            Recalcular();
            var partes = new List<string>();
            if (mudadas.Count > 0) partes.Add($"{mudadas.Count} regra(s) mudada(s), {afetados} {ConteudoDesenho.Palavra(_currentSection).Varios} com a nova classe");
            if (novas > 0) partes.Add($"{novas} regra(s) nova(s) no topo");
            Status(string.Join(" · ", partes) + ". Clique Salvar.");
        }

        /// <summary>Marcar/desmarcar Exportar numa linha (chamado pela LinhaClasse).</summary>
        internal void MudarExportar(LinhaClasse l, bool valor)
        {
            if (l.RegraObj != null) l.RegraObj.Export = valor;
            else RegraPropria(l.Item, "", valor);
            Dispatcher.BeginInvoke(new Action(Recalcular), System.Windows.Threading.DispatcherPriority.Background);
        }

        void MoverRegra(int delta)
        {
            if (!(_lista.Grid.SelectedItem is LinhaClasse l) || l.RegraObj == null) { Status("Selecione uma linha com regra."); return; }
            int i = _mappingRows.IndexOf(l.RegraObj), j = i + delta;
            if (i < 0 || j < 0 || j >= _mappingRows.Count) return;
            _mappingRows.Move(i, j);
            Recalcular();
        }

        void NovaRegra()
        {
            string nome = PedirTexto("Nova regra", "Nome do layer ou expressão regular (ex.: ^U-3D-DORMENTE.*):", "");
            if (string.IsNullOrWhiteSpace(nome)) return;
            int pos = _lista.Grid.SelectedItem is LinhaClasse l && l.RegraObj != null ? _mappingRows.IndexOf(l.RegraObj) : 0;
            var classe = _classes.SelectedItem as string;
            _mappingRows.Insert(Math.Max(0, pos), new MappingRow { Name = nome.Trim(), IfcExportAs = classe == null || classe == SemClasse ? "" : classe, Export = true });
            Recalcular();
            Status("Regra criada. Clique Salvar.");
        }

        void EditarRegra()
        {
            if (!(_lista.Grid.SelectedItem is LinhaClasse l)) return;
            if (l.RegraObj == null)
            {
                string novo = PedirTexto("Nova regra", "Regra para " + l.Nome + ":", "^" + System.Text.RegularExpressions.Regex.Escape(l.Nome) + "$");
                if (string.IsNullOrWhiteSpace(novo)) return;
                var r = RegraPropria(l.Item, "", true);
                r.Name = novo.Trim();
            }
            else
            {
                string novo = PedirTexto("Editar regra", "Nome do layer ou expressão regular:", l.RegraObj.Name);
                if (string.IsNullOrWhiteSpace(novo)) return;
                l.RegraObj.Name = novo.Trim();
            }
            Recalcular();
        }

        void ExcluirRegras()
        {
            var regras = Selecionadas("cuja regra sai do perfil").Select(l => l.RegraObj).Where(r => r != null).Distinct().ToList();
            if (regras.Count == 0) return;
            int afetados = _linhas.Count(l => l.Item != null && regras.Contains(l.RegraObj));
            if (MessageBox.Show(this, $"Excluir {regras.Count} regra(s) do perfil?" + (afetados > 0 ? $" {afetados} {ConteudoDesenho.Palavra(_currentSection).Varios} do modelo deixam de segui-la(s)." : ""),
                    "MetroSP", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            foreach (var r in regras) _mappingRows.Remove(r);
            Recalcular();
        }

        /// <summary>Caixinha para digitar um texto (nome de regra).</summary>
        string PedirTexto(string titulo, string pergunta, string inicial) => Theme.PedirTexto(this, titulo, pergunta, inicial);

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
            Recalcular();
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
            // Cada coluna de texto é uma lista em que dá para digitar: o que existe no modelo (property sets e propriedades
            // do Civil 3D) mais o que já está no mapeamento.
            grid.Columns.Add(ColunaLista("Property set", nameof(PropertyRow.PropertySet), 2, r =>
                PsetsDoModelo().Select(x => x.Nome).Concat(_propertyRows.Select(x => x.PropertySet))));
            grid.Columns.Add(ColunaLista("Propriedade", nameof(PropertyRow.Property), 2, r =>
                PropsDoModelo(r.PropertySet).Concat(_propertyRows.Where(x => Igual(x.PropertySet, r.PropertySet)).Select(x => x.Property))));
            grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Ativa", Binding = new Binding("Active"), Width = 50 });
            grid.Columns.Add(ColunaLista("Origem", nameof(PropertyRow.Source), 1, r =>
                new[] { "PropertySet", "Properties", "General", "PartData", "DrawingProperties" }.Concat(_propertyRows.Select(x => x.Source))));
            grid.Columns.Add(ColunaLista("Grupo", nameof(PropertyRow.Group), 1, r =>
                (Igual(r.Source, "PropertySet") ? PsetsDoModelo().Select(x => x.Nome) : Enumerable.Empty<string>())
                    .Concat(_propertyRows.Where(x => Igual(x.Source, r.Source)).Select(x => x.Group))));
            grid.Columns.Add(ColunaLista("Nome no Civil 3D", nameof(PropertyRow.CivilName), 2, r =>
                (Igual(r.Source, "PropertySet") ? PropsDoModelo(r.Group) : Enumerable.Empty<string>())
                    .Concat(_propertyRows.Where(x => Igual(x.Source, r.Source) && Igual(x.Group, r.Group)).Select(x => x.CivilName))));
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

        static bool Igual(string a, string b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        List<PropertySets.PsetMontagem> _psetsModelo;

        /// <summary>Property sets definidos no modelo (lidos uma vez por janela).</summary>
        List<PropertySets.PsetMontagem> PsetsDoModelo()
        {
            if (_psetsModelo != null) return _psetsModelo;
            try
            {
                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                using (doc.LockDocument()) _psetsModelo = PropertySets.PsetsCore.LerDesenho(doc.Database);
            }
            catch { _psetsModelo = new List<PropertySets.PsetMontagem>(); }
            return _psetsModelo;
        }

        IEnumerable<string> PropsDoModelo(string pset) =>
            PsetsDoModelo().Where(x => Igual(x.Nome, pset)).SelectMany(x => x.Propriedades.Select(pr => pr.Nome));

        /// <summary>
        /// Coluna de texto que, ao editar, vira lista suspensa em que dá para digitar (regra de todas as telas).
        /// As opções são montadas na hora, a partir da linha (ex.: as propriedades do property set escolhido).
        /// </summary>
        static DataGridTemplateColumn ColunaLista(string titulo, string prop, double peso, Func<PropertyRow, IEnumerable<string>> opcoes)
        {
            var ver = new FrameworkElementFactory(typeof(TextBlock));
            ver.SetBinding(TextBlock.TextProperty, new Binding(prop));
            ver.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            ver.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 0, 2, 0));

            var editar = new FrameworkElementFactory(typeof(ComboBox));
            editar.SetValue(ComboBox.IsEditableProperty, true);
            editar.SetValue(ComboBox.IsTextSearchEnabledProperty, true);
            editar.SetValue(ComboBox.StaysOpenOnEditProperty, true);
            editar.SetValue(ComboBox.MaxDropDownHeightProperty, 320.0);
            editar.SetBinding(ComboBox.TextProperty, new Binding(prop) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus });
            editar.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler((s, e) =>
            {
                var c = (ComboBox)s;
                if (!(c.DataContext is PropertyRow r)) return;
                var texto = c.Text;
                c.ItemsSource = opcoes(r).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();
                c.Text = texto;
                c.Focus();
            }));
            return new DataGridTemplateColumn
            {
                Header = titulo, SortMemberPath = prop, Width = new DataGridLength(peso, DataGridLengthUnitType.Star),
                CellTemplate = new DataTemplate { VisualTree = ver },
                CellEditingTemplate = new DataTemplate { VisualTree = editar },
            };
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
                ("Latitude", Text(e.Terreno, nameof(SiteInfo.Latitude), "Graus decimais; sul é negativo. Vazio: a da origem do PTL (150.000; 250.000), onde fica o IfcSite.")),
                ("Longitude", Text(e.Terreno, nameof(SiteInfo.Longitude), "Graus decimais; oeste é negativo. Vazio: a da origem do PTL (150.000; 250.000), onde fica o IfcSite.")),
                ("Elevação (m)", Text(e.Terreno, nameof(SiteInfo.Elevacao))),
                ("Matrícula", Text(e.Terreno, nameof(SiteInfo.Matricula), "Matrícula do terreno (LandTitleNumber).")),
                ("Coordenadas", Ptl(e)));
            extra.Margin = new Thickness(0, 6, 0, 0);

            // Instalações em árvore: a principal (a que o exportador grava, com os objetos) e as de referência (vazias).
            var inst = ArvoreInstalacoes();

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
                    var b = _facilities.FirstOrDefault(f => f.Principal && !string.IsNullOrWhiteSpace(f.Nome))
                            ?? _facilities.FirstOrDefault(f => f.Tipo == "IfcBuilding" && !string.IsNullOrWhiteSpace(f.Nome));
                    return b == null ? null : new DadosEntidadeIfc { Nome = b.Nome, NomeLongo = b.NomeLongo, Descricao = b.Descricao, Endereco = b.Endereco };
                },
            };
            _dadosPainel.PadraoUsado += UsarEdificioDoPadrao;
            return new ScrollViewer { Content = _dadosPainel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 6, 0) };
        }

        /// <summary>
        /// Estrutura no IFC: IfcProject › IfcSite › instalação principal (destacada: é onde o exportador põe todos os objetos
        /// do modelo; aqui se escolhe o tipo, o nome e o nome longo) e as instalações de referência (vazias, ex.: a estação vizinha).
        /// </summary>
        UIElement ArvoreInstalacoes()
        {
            var raiz = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            raiz.Children.Add(Theme.Secao("Estrutura no IFC · instalações dentro do IfcSite"));
            if (!_facilities.Any(f => f.Principal))
            {
                // Antes, a primeira da lista com o mesmo nome da que a Autodesk grava era a principal; sem marca, nenhuma é.
                _facilities.Insert(0, new FacilityInfo { Principal = true, Tipo = FacilityInfo.DefaultType });
            }
            int objetos = -1;
            try
            {
                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
                {
                    objetos = FiltroIfc.Objetos(doc.Database, tr).Count;
                    tr.Commit();
                }
            }
            catch { }

            var linhas = new StackPanel();
            raiz.Children.Add(linhas);
            void Montar()
            {
                linhas.Children.Clear();
                linhas.Children.Add(LinhaArvore("", "IfcProject", Theme.Detalhe("nome e nome longo no alto, em IfcProject"), null, false));
                linhas.Children.Add(LinhaArvore(" └", "IfcSite", Theme.Detalhe("na origem do PTL (150.000; 250.000), com a latitude e a longitude exatas"), null, false));
                FacilityInfo pr = _facilities.First(f => f.Principal);
                linhas.Children.Add(LinhaArvore("    └", null, CamposInstalacao(pr, true),
                    new TextBlock
                    {
                        Text = (objetos >= 0 ? $"{objetos:N0} objetos do modelo" : "os objetos do modelo") + "\ninstalação principal: é onde o exportador põe tudo",
                        Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center,
                    }, true));
                foreach (FacilityInfo f in _facilities.Where(f => !f.Principal).ToList())
                {
                    var tirar = Theme.Link("tirar");
                    tirar.Click += (s, e) => { _facilities.Remove(f); Montar(); };
                    var lado = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    lado.Children.Add(new TextBlock { Text = "vazia, só referência · ", Foreground = Theme.Solid(Theme.Cinza) });
                    lado.Children.Add(tirar);
                    linhas.Children.Add(LinhaArvore("    └", null, CamposInstalacao(f, false), lado, false));
                }
                var mais = new Button { Content = "+ instalação de referência", Padding = new Thickness(10, 1, 10, 1), Margin = new Thickness(0, 4, 8, 0),
                    ToolTip = "IfcBuilding, IfcBridge, IfcRoad ou IfcRailway sem objetos (ex.: a estação vizinha). Vai dentro do IfcSite." };
                mais.Click += (s, e) => { _facilities.Add(new FacilityInfo { Tipo = "IfcBuilding" }); Montar(); };
                var linhaMais = new StackPanel { Orientation = Orientation.Horizontal };
                linhaMais.Children.Add(mais);
                linhas.Children.Add(LinhaArvore("    └", null, linhaMais, null, false));
            }
            Montar();
            _facilities.CollectionChanged += (s, e) => Dispatcher.BeginInvoke(new Action(Montar));
            return raiz;
        }

        static Grid LinhaArvore(string ramo, string entidade, UIElement campos, UIElement lado, bool destaque)
        {
            var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            if (destaque) g.Background = Theme.Solid(Theme.LaranjaSelecao);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var r = new TextBlock { Text = ramo, FontFamily = new FontFamily("Consolas"), Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(r);
            int col = 1;
            if (entidade != null)
            {
                var t = new TextBlock { Text = entidade, FontWeight = FontWeights.SemiBold, Width = 130, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4) };
                Grid.SetColumn(t, 1);
                g.Children.Add(t);
                col = 2;
            }
            Grid.SetColumn((FrameworkElement)campos, col);
            if (entidade == null) Grid.SetColumnSpan((FrameworkElement)campos, 2);
            g.Children.Add(campos);
            if (lado != null)
            {
                ((FrameworkElement)lado).Margin = new Thickness(12, 2, 6, 2);
                Grid.SetColumn((FrameworkElement)lado, 3);
                g.Children.Add(lado);
            }
            return g;
        }

        static UIElement CamposInstalacao(FacilityInfo f, bool principal)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            var tipo = Theme.Digitavel(new ComboBox { ItemsSource = FacilityInfo.Types, Width = 130, Margin = new Thickness(0, 0, 8, 0), FontWeight = FontWeights.SemiBold,
                ToolTip = principal ? "Tipo da instalação principal no IFC 4.3 (no IFC4 só existe IfcBuilding)." : "Tipo da instalação de referência." });
            tipo.SelectedItem = FacilityInfo.Types.FirstOrDefault(t => string.Equals(t, f.Tipo, StringComparison.OrdinalIgnoreCase)) ?? FacilityInfo.DefaultType;
            tipo.SelectionChanged += (s, e) => { if (tipo.SelectedItem is string t) f.Tipo = t; };
            p.Children.Add(tipo);
            TextBox Caixa(string valor, double w, string dica, Action<string> gravar)
            {
                var t = new TextBox { Text = valor ?? "", Width = w, Margin = new Thickness(0, 0, 8, 0), ToolTip = dica, VerticalContentAlignment = VerticalAlignment.Center };
                t.TextChanged += (s, e) => gravar(t.Text);
                p.Children.Add(t);
                return t;
            }
            Caixa(f.Nome, 200, principal ? "Nome da instalação principal (ex.: Linha 15). Vazio: fica o nome que a Autodesk dá." : "Nome (ex.: Estação Vila Prudente).", v => f.Nome = v);
            Caixa(f.NomeLongo, 240, "Nome longo.", v => f.NomeLongo = v);
            Caixa(f.Endereco, 200, "Endereço (só IfcBuilding).", v => f.Endereco = v);
            return p;
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
            var f = _facilities.FirstOrDefault(x => string.Equals((x.Nome ?? "").Trim(), ed.Nome.Trim(), StringComparison.OrdinalIgnoreCase))
                    ?? _facilities.FirstOrDefault(x => x.Principal && string.IsNullOrWhiteSpace(x.Nome));
            if (f != null && string.IsNullOrWhiteSpace(f.Nome)) f.Nome = ed.Nome.Trim();
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
            filesHead.Children.Add(Section("Arquivos do perfil"));
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
                panel.Children.Add(Theme.Detalhe("Ainda não há este arquivo no perfil. Rode IFCInfraSaveConfig no Civil 3D e use Importar da Autodesk."));
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
                ToolTip = "Ligado (padrão): o IFC sai nas coordenadas do modelo (PTL do Metrô), sem EPSG, IfcProjectedCRS e " +
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

    public class MappingRow
    {

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

    /// <summary>Linha da lista única de Classes IFC: um nome do modelo com a regra que vale, ou uma regra só do padrão.</summary>
    public class LinhaClasse
    {
        readonly ConfigWindow _janela;
        public ItemDesenho Item { get; }
        public MappingRow RegraObj { get; }
        public bool SoPadrao { get; set; }

        public LinhaClasse(ConfigWindow janela, ItemDesenho item, MappingRow regra, int ordem)
        {
            _janela = janela; Item = item; RegraObj = regra; Ordem = ordem > 0 ? ordem.ToString() : "";
        }

        public string Chave => Item != null ? "m:" + Item.Nome : "r:" + RegraObj?.Name;
        public string Nome => Item?.Nome ?? RegraObj?.Name;
        public string Onde => Item == null ? (SoPadrao ? "só no perfil" : "no perfil") : RegraObj == null ? "só no modelo" : "perfil e modelo";
        public string Objetos => Item == null ? "—" : Item.Objetos.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"));
        public int ObjetosNum => Item?.Objetos ?? -1;
        public string Classe => RegraObj == null ? "(classe da Autodesk)" : string.IsNullOrWhiteSpace(RegraObj.IfcExportAs) ? "(classe da Autodesk)" : RegraObj.IfcExportAs;
        public bool ClasseInvalida => RegraObj != null && RegraObj.ClasseInvalida;
        public string Regra => RegraObj == null ? "sem regra" : RegraObj.Name;
        public string Ordem { get; }
        public bool SemRegra => Item != null && RegraObj == null;
        public bool Exportar
        {
            get => RegraObj?.Export ?? true;
            set { if (value != Exportar) _janela.MudarExportar(this, value); }
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
