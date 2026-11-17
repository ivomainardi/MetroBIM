using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using MetroSP.Config;
using MetroSP.Ui;

namespace MetroSP.PropertySets
{
    /// <summary>
    /// Vista "Saída no IFC" do passo 3: cada linha é uma propriedade que sai no IFC (Pset e nome de saída, tipo, ativa)
    /// e "Vem de" diz de qual propriedade do modelo o valor é lido. É o mapeamento de propriedades do exportador da
    /// Autodesk no perfil de exportação: IFCInfraExportPropertyMapping.csv (origem) e .json (modelo do Pset e tipos).
    /// </summary>
    public class SaidaIfcPainel : DockPanel
    {
        public const string CabecalhoPadrao = "IfcPropertySet Name,IfcProperty Name,Active,Source,Group,Civil 3D Name";

        /// <summary>Psets que já vêm no mapeamento da Autodesk (começam fechados).</summary>
        static readonly HashSet<string> PsetsAutodesk = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Summary", "Custom_Properties", "Properties", "General", "Geometry", "Misc", "Miscellaneous", "3D Visualization" };

        public static readonly string[] TiposIfc =
            { "IfcLabel", "IfcText", "IfcIdentifier", "IfcReal", "IfcInteger", "IfcBoolean", "IfcLogical", "IfcLengthMeasure",
              "IfcAreaMeasure", "IfcVolumeMeasure", "IfcPositiveLengthMeasure", "IfcPlaneAngleMeasure", "IfcMassMeasure", "IfcDate", "IfcDateTime" };

        public static readonly string[] Entidades = { "IfcElement", "IfcObject", "IfcProduct", "IfcProject", "IfcSite", "IfcAlignment", "IfcSpatialElement" };

        readonly Window _dono;
        readonly Func<IEnumerable<PsetMontagem>> _psetsModelo;
        readonly Func<string, int> _anexados;
        readonly Func<string, string, int> _vazios;
        readonly Func<IEnumerable<string>> _propsDoModelo;

        readonly ObservableCollection<LinhaSaida> _linhas = new ObservableCollection<LinhaSaida>();
        readonly List<GrupoSaida> _grupos = new List<GrupoSaida>();
        readonly HashSet<string> _psetsOriginais = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly FilterableGrid _lista;
        readonly CheckBox _soSemOrigem, _soVazios;
        readonly TextBlock _resumo = Theme.Detalhe();
        readonly Button _salvar;
        string _cabecalho = CabecalhoPadrao;
        char _sep = ',';
        bool _alterado;

        /// <summary>Depois de salvar (para a vista No modelo ler o perfil de novo).</summary>
        public event Action Salvo;

        public bool Alterado => _alterado;

        public SaidaIfcPainel(Window dono, Func<IEnumerable<PsetMontagem>> psetsModelo, Func<string, int> anexados,
            Func<string, string, int> vazios, Func<IEnumerable<string>> propsDoModelo)
        {
            _dono = dono;
            _psetsModelo = psetsModelo;
            _anexados = anexados;
            _vazios = vazios;
            _propsDoModelo = propsDoModelo;

            // ---- barra
            var barra = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(barra, Dock.Top);
            Button Botao(string texto, string dica, Action a, bool primario = false)
            {
                var b = new Button { Content = texto, ToolTip = dica, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 4), VerticalAlignment = VerticalAlignment.Center };
                if (primario) b.Style = Theme.PrimaryButton;
                b.Click += (s, e) => a();
                barra.Children.Add(b);
                return b;
            }
            Botao("+ Pset no IFC", "Cria um Pset de saída no IFC (o nome é o que aparece no IFC).", NovoPset);
            Botao("+ propriedade", "Acrescenta uma propriedade de saída ao Pset da linha escolhida.", () => NovaPropriedade(Selecionada()?.Grupo));
            Botao("Mapear do modelo…", "Escolha propriedades do modelo: cada uma vira uma propriedade de saída num Pset (com o mesmo nome ou outro).", MapearDoModelo);
            var mais = new Button { Content = "Mais ▾", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 14, 4), VerticalAlignment = VerticalAlignment.Center };
            mais.Click += (s, e) => MenuMais(mais);
            barra.Children.Add(mais);
            _soSemOrigem = new CheckBox { Content = "Só sem origem", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 4),
                ToolTip = "Só as saídas cujo \"Vem de\" está vazio ou aponta para algo que não existe no modelo." };
            _soVazios = new CheckBox { Content = "Só com vazios", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 4),
                ToolTip = "Só as saídas lidas de um property set que tem objetos com o valor vazio." };
            _soSemOrigem.Click += (s, e) => _lista.Refresh();
            _soVazios.Click += (s, e) => _lista.Refresh();
            barra.Children.Add(_soSemOrigem);
            barra.Children.Add(_soVazios);
            _salvar = Botao("Salvar no perfil", "Grava o mapeamento no perfil de exportação (IFCInfraExportPropertyMapping .csv e .json).", () => Salvar(), primario: true);
            _salvar.IsEnabled = false;

            // ---- lista
            _lista = new FilterableGrid(_linhas, typeof(LinhaSaida));
            _lista.Grid.IsReadOnly = false;
            Theme.StyleSelection(_lista.Grid);
            VirtualizingPanel.SetIsVirtualizingWhenGrouping(_lista.Grid, true);
            _lista.AddColumn("Propriedade no IFC", nameof(LinhaSaida.Propriedade), 220);
            ColunaLista("Tipo no IFC", nameof(LinhaSaida.Tipo), 130, false, l => TiposIfc);
            ColunaLista("Vem de (no modelo)", nameof(LinhaSaida.VemDe), 320, true, l => Origens());
            var ativa = _lista.AddColumn("Ativa", nameof(LinhaSaida.Ativa), 50);
            int iAtiva = _lista.Grid.Columns.IndexOf(ativa);
            _lista.Grid.Columns.Remove(ativa);
            _lista.Grid.Columns.Insert(iAtiva, new DataGridCheckBoxColumn
            {
                Header = ativa.Header, Width = 50, SortMemberPath = nameof(LinhaSaida.Ativa),
                Binding = new Binding(nameof(LinhaSaida.Ativa)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            });
            foreach (var (titulo, prop, ordem) in new[] { ("Preenchidos", nameof(LinhaSaida.PreenchidosTexto), nameof(LinhaSaida.Preenchidos)), ("Vazios", nameof(LinhaSaida.VaziosTexto), nameof(LinhaSaida.Vazios)) })
            {
                var c = _lista.AddColumn(titulo, prop, 80);
                c.IsReadOnly = true;
                c.SortMemberPath = ordem;
                c.ElementStyle = new Style(typeof(TextBlock), c.ElementStyle) { Setters = { new Setter(TextBlock.TextAlignmentProperty, System.Windows.TextAlignment.Right) } };
            }
            _lista.Grid.CellEditEnding += (s, e) =>
            {
                Marcar();
                Dispatcher.BeginInvoke(new Action(() => { foreach (var l in _linhas) l.Recalcular(this); Resumo(); }), System.Windows.Threading.DispatcherPriority.Background);
            };
            _lista.ExtraFilter = o => o is LinhaSaida l && Passa(l);
            _lista.Filtered += Resumo;
            _lista.Grid.ToolTip = "Clique na célula para editar. \"Vem de\" é uma lista em que dá para digitar: Origem › grupo › nome.";

            var estilo = new Style(typeof(DataGridRow), _lista.Grid.RowStyle);
            var pend = new DataTrigger { Binding = new Binding(nameof(LinhaSaida.Pendente)), Value = true };
            pend.Setters.Add(new Setter(Control.BackgroundProperty, Theme.LinhaProblema));
            estilo.Triggers.Add(pend);
            var inativa = new DataTrigger { Binding = new Binding(nameof(LinhaSaida.Ativa)), Value = false };
            inativa.Setters.Add(new Setter(Control.ForegroundProperty, Theme.Solid(Theme.Cinza)));
            estilo.Triggers.Add(inativa);
            estilo.Setters.Add(new Setter(ToolTipProperty, new Binding(nameof(LinhaSaida.Dica))));
            _lista.Grid.RowStyle = estilo;
            _lista.Grid.GroupStyle.Add(EstiloGrupo());
            if (_lista.Grid.ItemsSource is ICollectionView v) v.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LinhaSaida.Grupo)));

            _resumo.TextWrapping = TextWrapping.NoWrap;
            _resumo.TextTrimming = TextTrimming.CharacterEllipsis;
            _resumo.Margin = new Thickness(0, 4, 0, 0);
            DockPanel.SetDock(_resumo, Dock.Bottom);

            Children.Add(barra);
            Children.Add(_resumo);
            Children.Add(_lista);
        }

        // ================================================================== colunas e grupo

        void ColunaLista(string titulo, string prop, double largura, bool preencher, Func<LinhaSaida, IEnumerable<string>> opcoes)
        {
            DataGridTextColumn texto = _lista.AddColumn(titulo, prop, largura);
            int i = _lista.Grid.Columns.IndexOf(texto);
            _lista.Grid.Columns.Remove(texto);

            var ver = new FrameworkElementFactory(typeof(TextBlock));
            ver.SetBinding(TextBlock.TextProperty, new Binding(prop));
            ver.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            ver.SetValue(MarginProperty, new Thickness(2, 0, 2, 0));
            if (preencher)
            {
                var est = new Style(typeof(TextBlock));
                var sem = new DataTrigger { Binding = new Binding(nameof(LinhaSaida.SemOrigem)), Value = true };
                sem.Setters.Add(new Setter(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.Firebrick));
                est.Triggers.Add(sem);
                ver.SetValue(StyleProperty, est);
                ver.SetBinding(TextBlock.TextProperty, new Binding(nameof(LinhaSaida.VemDeTexto)));
            }

            var editar = new FrameworkElementFactory(typeof(ComboBox));
            editar.SetValue(ComboBox.IsEditableProperty, true);
            editar.SetValue(ComboBox.IsTextSearchEnabledProperty, true);
            editar.SetValue(ComboBox.StaysOpenOnEditProperty, true);
            editar.SetValue(ComboBox.MaxDropDownHeightProperty, 380.0);
            editar.SetBinding(ComboBox.TextProperty, new Binding(prop) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus });
            editar.AddHandler(LoadedEvent, new RoutedEventHandler((s, e) =>
            {
                var c = (ComboBox)s;
                if (!(c.DataContext is LinhaSaida l)) return;
                string atual = c.Text;
                c.ItemsSource = opcoes(l).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                c.Text = atual;
                c.Focus();
                c.IsDropDownOpen = preencher;
            }));
            _lista.Grid.Columns.Insert(i, new DataGridTemplateColumn
            {
                Header = texto.Header, SortMemberPath = prop, Width = preencher ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(largura),
                MinWidth = 100,
                CellTemplate = new DataTemplate { VisualTree = ver },
                CellEditingTemplate = new DataTemplate { VisualTree = editar },
            });
        }

        GroupStyle EstiloGrupo()
        {
            var cab = new FrameworkElementFactory(typeof(StackPanel));
            cab.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            cab.SetValue(MarginProperty, new Thickness(0, 3, 0, 2));
            cab.SetBinding(DataContextProperty, new Binding("Name"));

            var nome = new FrameworkElementFactory(typeof(TextBlock));
            nome.SetBinding(TextBlock.TextProperty, new Binding(nameof(GrupoSaida.Nome)));
            nome.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            nome.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            nome.SetValue(MarginProperty, new Thickness(0, 0, 10, 0));
            nome.SetValue(ToolTipProperty, "Clique duas vezes para renomear o Pset de saída.");
            nome.AddHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler((s, e) =>
            {
                if (e.ClickCount == 2 && ((FrameworkElement)s).DataContext is GrupoSaida g) { Renomear(g); e.Handled = true; }
            }));
            cab.AppendChild(nome);

            var info = new FrameworkElementFactory(typeof(TextBlock));
            info.SetBinding(TextBlock.TextProperty, new Binding(nameof(GrupoSaida.Info)));
            info.SetValue(TextBlock.ForegroundProperty, Theme.Solid(Theme.Cinza));
            info.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            info.SetValue(MarginProperty, new Thickness(0, 0, 12, 0));
            cab.AppendChild(info);

            FrameworkElementFactory Link(string texto, string dica, Action<GrupoSaida> acao)
            {
                var b = new FrameworkElementFactory(typeof(Button));
                b.SetValue(ContentControl.ContentProperty, texto);
                b.SetValue(ToolTipProperty, dica);
                b.SetValue(Control.PaddingProperty, new Thickness(8, 0, 8, 1));
                b.SetValue(MarginProperty, new Thickness(0, 0, 6, 0));
                b.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
                b.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((s, e) =>
                {
                    if (((FrameworkElement)s).DataContext is GrupoSaida g) acao(g);
                    e.Handled = true;
                }));
                return b;
            }
            var aplica = Link("", "A que entidade IFC o Pset se aplica (ApplicableEntities).", Entidade);
            aplica.SetBinding(ContentControl.ContentProperty, new Binding(nameof(GrupoSaida.EntidadeTexto)));
            cab.AppendChild(aplica);
            cab.AppendChild(Link("+ propriedade", "Acrescenta uma propriedade de saída a este Pset.", NovaPropriedade));

            var exp = new FrameworkElementFactory(typeof(Expander));
            exp.SetBinding(Expander.IsExpandedProperty, new Binding("Name." + nameof(GrupoSaida.Aberto)) { Mode = BindingMode.TwoWay });
            exp.SetValue(Control.BackgroundProperty, Theme.Solid(Theme.CinzaClaro));
            exp.SetValue(Expander.HeaderTemplateProperty, new DataTemplate { VisualTree = cab });
            exp.SetBinding(HeaderedContentControl.HeaderProperty, new Binding());
            exp.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
            var cont = new Style(typeof(GroupItem));
            cont.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(GroupItem)) { VisualTree = exp }));
            return new GroupStyle { ContainerStyle = cont };
        }

        // ================================================================== origem ("Vem de")

        static readonly (string Source, string Rotulo)[] Rotulos =
        {
            ("PropertySet", "Property set"),
            ("General", "Propriedade do objeto"),
            ("DrawingProperties", "Dados do modelo"),
            ("PartData", "Dados da peça"),
            ("UserDefined", "Propriedade definida pelo usuário"),
        };

        public static string RotuloDe(string source) =>
            Rotulos.FirstOrDefault(r => Igual(r.Source, source)).Rotulo ?? source ?? "";

        public static string SourceDe(string rotulo) =>
            Rotulos.FirstOrDefault(r => Igual(r.Rotulo, rotulo) || Igual(r.Source, rotulo)).Source ?? (rotulo ?? "").Trim();

        public static string Formatar(string source, string grupo, string nome)
        {
            if (string.IsNullOrWhiteSpace(source) && string.IsNullOrWhiteSpace(nome)) return "";
            return RotuloDe(source) + " › " + (grupo ?? "").Trim() + " › " + (nome ?? "").Trim();
        }

        /// <summary>Lê "Origem › grupo › nome" (também aceita "&gt;" ou só dois pedaços).</summary>
        public static (string Source, string Grupo, string Nome) Ler(string texto)
        {
            var p = (texto ?? "").Replace(">", "›").Split('›').Select(x => x.Trim()).ToList();
            if (p.Count == 0 || p.All(x => x.Length == 0)) return ("", "", "");
            if (p.Count >= 3) return (SourceDe(p[0]), p[1], string.Join(" › ", p.Skip(2)));
            if (p.Count == 2)
            {
                string s = SourceDe(p[0]);
                string g = Igual(s, "General") ? "Properties" : Igual(s, "DrawingProperties") ? "Custom_Properties" : p[1];
                return (s, g, p[1]);
            }
            return ("", "", p[0]);
        }

        /// <summary>Opções do "Vem de": property sets do modelo, o que já está no mapeamento e as propriedades do modelo.</summary>
        IEnumerable<string> Origens()
        {
            var r = new List<string>();
            foreach (PsetMontagem p in _psetsModelo().Where(p => p.NoDesenho).OrderBy(p => p.Nome, StringComparer.CurrentCultureIgnoreCase))
                foreach (PropriedadeMontagem pr in p.Propriedades) r.Add(Formatar("PropertySet", p.Nome, pr.Nome));
            foreach (string c in new[] { "Name", "Description", "Layer", "Style", "Handle" }) r.Add(Formatar("General", "Properties", c));
            foreach (string c in new[] { "Title", "Subject", "Author", "Keywords", "Comments" }) r.Add(Formatar("DrawingProperties", "Summary", c));
            foreach (string c in _propsDoModelo() ?? Enumerable.Empty<string>()) r.Add(Formatar("DrawingProperties", "Custom_Properties", c));
            r.AddRange(_linhas.Select(l => l.VemDe).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase));
            return r;
        }

        internal bool ExisteNoModelo(string pset, string prop) =>
            _psetsModelo().Any(p => p.NoDesenho && Igual(p.Nome, pset) && p.Propriedades.Any(x => Igual(x.Nome, prop)));

        internal int Anexados(string pset) => _anexados(pset);
        internal int Vazios(string pset, string prop) => _vazios(pset, prop);

        // ================================================================== carregar e salvar

        public void Carregar()
        {
            _linhas.Clear();
            _grupos.Clear();
            _psetsOriginais.Clear();
            _cabecalho = CabecalhoPadrao;
            _sep = ',';
            var tipos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var entidades = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (JsonNode tpl in Templates(ConfigPackage.LoadJson(ConfigKind.PropertyTemplates)) ?? new JsonArray())
                {
                    string pn = (string)tpl?["Name"];
                    if (string.IsNullOrWhiteSpace(pn)) continue;
                    entidades[pn] = (string)tpl["ApplicableEntities"];
                    if (tpl["PropertyTemplates"] is JsonArray props)
                        foreach (JsonNode pt in props) tipos[pn + "\u0001" + (string)pt?["Name"]] = (string)pt?["PrimaryMeasureType"];
                }
            }
            catch { }

            try
            {
                string csv = ConfigPackage.PathOf(ConfigKind.PropertyMappingCsv);
                if (File.Exists(csv))
                {
                    var linhas = File.ReadAllLines(csv).Where(l => l.Trim().Length > 0).ToList();
                    if (linhas.Count > 0)
                    {
                        _sep = linhas[0].Count(c => c == ';') > linhas[0].Count(c => c == ',') ? ';' : ',';
                        _cabecalho = linhas[0];
                        foreach (string l in linhas.Skip(1))
                        {
                            List<string> c = Csv.Split(l, _sep);
                            string At(int i) => i < c.Count ? c[i] : "";
                            string pset = At(0).Trim();
                            if (pset.Length == 0) continue;
                            GrupoSaida g = Grupo(pset, entidades.TryGetValue(pset, out var en) ? en : null);
                            _psetsOriginais.Add(pset);
                            tipos.TryGetValue(pset + "\u0001" + At(1).Trim(), out string tipo);
                            _linhas.Add(new LinhaSaida(g)
                            {
                                Propriedade = At(1).Trim(),
                                Ativa = !Igual(At(2), "false"),
                                Source = At(3).Trim(),
                                GrupoOrigem = At(4).Trim(),
                                NomeOrigem = At(5).Trim(),
                                Tipo = string.IsNullOrWhiteSpace(tipo) ? "IfcLabel" : tipo,
                                Extra = c.Skip(6).ToList(),
                            });
                        }
                    }
                }
            }
            catch (Exception ex) { MessageBox.Show(_dono, "Não foi possível ler o mapeamento de propriedades do perfil:\n" + ex.Message, _dono.Title); }
            foreach (LinhaSaida l in _linhas) l.Recalcular(this);
            Ordenar();
            _alterado = false;
            _salvar.IsEnabled = false;
            Resumo();
        }

        GrupoSaida Grupo(string nome, string entidade = null)
        {
            GrupoSaida g = _grupos.FirstOrDefault(x => Igual(x.Nome, nome));
            if (g != null) return g;
            g = new GrupoSaida { Nome = nome, Original = nome, Entidade = string.IsNullOrWhiteSpace(entidade) ? "IfcElement" : entidade, Aberto = !PsetsAutodesk.Contains(nome) };
            _grupos.Add(g);
            return g;
        }

        /// <summary>Psets da MetroSP primeiro e os da Autodesk no fim; dentro do Pset, a ordem do arquivo.</summary>
        void Ordenar()
        {
            var ordem = _linhas.Select((l, i) => (l, i))
                .OrderBy(x => PsetsAutodesk.Contains(x.l.Grupo.Nome) ? 1 : 0)
                .ThenBy(x => _grupos.IndexOf(x.l.Grupo)).ThenBy(x => x.i).Select(x => x.l).ToList();
            _linhas.Clear();
            foreach (var l in ordem) _linhas.Add(l);
            foreach (var g in _grupos) g.Contar(_linhas.Count(l => l.Grupo == g));
        }

        /// <summary>Linhas do mapeamento de propriedades do perfil ativo (Pset e propriedade no IFC, ativa e de onde vem).</summary>
        public static List<(string Pset, string Prop, bool Ativa, string Source, string Grupo, string Nome)> LerMapeamento()
        {
            var r = new List<(string, string, bool, string, string, string)>();
            string csv = ConfigPackage.PathOf(ConfigKind.PropertyMappingCsv);
            if (!File.Exists(csv)) return r;
            var linhas = File.ReadAllLines(csv).Where(l => l.Trim().Length > 0).ToList();
            if (linhas.Count == 0) return r;
            char sep = linhas[0].Count(c => c == ';') > linhas[0].Count(c => c == ',') ? ';' : ',';
            foreach (string l in linhas.Skip(1))
            {
                List<string> c = Csv.Split(l, sep);
                string At(int i) => i < c.Count ? c[i].Trim() : "";
                if (At(0).Length == 0) continue;
                r.Add((At(0), At(1), !Igual(At(2), "false"), At(3), At(4), At(5)));
            }
            return r;
        }

        static JsonArray Templates(JsonNode raiz) => raiz as JsonArray ?? raiz?["PropertySetTemplates"] as JsonArray;

        /// <summary>Grava o .csv (de onde vem cada propriedade) e o .json (Psets, entidades e tipos) do perfil ativo.</summary>
        public bool Salvar()
        {
            _lista.Grid.CommitEdit(DataGridEditingUnit.Row, true);
            var validas = _linhas.Where(l => !string.IsNullOrWhiteSpace(l.Propriedade)).ToList();
            var dup = validas.GroupBy(l => l.Grupo.Nome + "\u0001" + l.Propriedade.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.First()).ToList();
            if (dup.Count > 0)
            {
                MessageBox.Show(_dono, "Há propriedades de saída repetidas no mesmo Pset:\n" + string.Join("\n", dup.Take(10).Select(l => l.Grupo.Nome + " › " + l.Propriedade)) +
                    "\n\nRenomeie ou exclua uma delas antes de salvar.", _dono.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            try
            {
                ConfigPackage.EnsureActive();
                // ---- .csv
                var sb = new StringBuilder();
                sb.AppendLine(_cabecalho);
                foreach (LinhaSaida l in validas)
                    sb.AppendLine(string.Join(_sep.ToString(), new[] { l.Grupo.Nome, l.Propriedade.Trim(), l.Ativa ? "TRUE" : "FALSE", l.Source, l.GrupoOrigem, l.NomeOrigem }
                        .Concat(l.Extra ?? new List<string>()).Select(c => Csv.Quote(c ?? "", _sep))));
                ConfigPackage.Write(ConfigKind.PropertyMappingCsv, new UTF8Encoding(true).GetBytes(sb.ToString()));

                // ---- .json
                JsonNode raiz = ConfigPackage.LoadJson(ConfigKind.PropertyTemplates);
                JsonArray tpls = Templates(raiz);
                if (tpls == null)
                {
                    tpls = new JsonArray();
                    raiz = new JsonObject { ["PropertySetTemplates"] = tpls };
                }
                JsonObject Achar(string nome) => tpls.OfType<JsonObject>().FirstOrDefault(t => Igual((string)t["Name"], nome));
                var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (GrupoSaida g in _grupos.Where(g => validas.Any(l => l.Grupo == g)))
                {
                    JsonObject t = Achar(g.Original) ?? Achar(g.Nome);
                    if (t == null)
                    {
                        t = new JsonObject { ["Name"] = g.Nome, ["Description"] = "", ["TemplateType"] = "NOTDEFINED", ["ApplicableEntities"] = g.Entidade, ["PropertyTemplates"] = new JsonArray() };
                        tpls.Add(t);
                    }
                    t["Name"] = g.Nome;
                    t["ApplicableEntities"] = g.Entidade;
                    var antigas = (t["PropertyTemplates"] as JsonArray)?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();
                    if (t["PropertyTemplates"] is JsonArray velho) velho.Clear();
                    var novas = new JsonArray();
                    foreach (LinhaSaida l in validas.Where(l => l.Grupo == g))
                    {
                        JsonObject pt = antigas.FirstOrDefault(x => Igual((string)x["Name"], l.Propriedade))
                            ?? new JsonObject { ["Name"] = l.Propriedade.Trim(), ["Description"] = "" };
                        antigas.Remove(pt);
                        pt["Name"] = l.Propriedade.Trim();
                        pt["PrimaryMeasureType"] = string.IsNullOrWhiteSpace(l.Tipo) ? "IfcLabel" : l.Tipo.Trim();
                        novas.Add(pt);
                    }
                    t["PropertyTemplates"] = novas;
                    usados.Add(g.Nome);
                }
                // Psets que estavam no mapeamento e ficaram sem nenhuma propriedade saem do .json também.
                foreach (JsonObject t in tpls.OfType<JsonObject>().ToList())
                {
                    string n = (string)t["Name"];
                    bool eraDaLista = _psetsOriginais.Contains(n ?? "") || _grupos.Any(g => Igual(g.Original, n));
                    if (eraDaLista && !usados.Contains(n ?? "")) tpls.Remove(t);
                }
                ConfigPackage.SaveJson(ConfigKind.PropertyTemplates, raiz);
            }
            catch (Exception ex)
            {
                MessageBox.Show(_dono, "Não foi possível salvar o mapeamento:\n" + ex.Message, _dono.Title, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            Carregar();
            Salvo?.Invoke();
            return true;
        }

        void Marcar()
        {
            _alterado = true;
            _salvar.IsEnabled = true;
        }

        // ================================================================== filtros e resumo

        bool Passa(LinhaSaida l)
        {
            if (_soSemOrigem.IsChecked == true && !l.SemOrigem) return false;
            if (_soVazios.IsChecked == true && !(l.Vazios > 0)) return false;
            return true;
        }

        void Resumo()
        {
            int saidas = _linhas.Count(l => l.Ativa);
            int sem = _linhas.Count(l => l.Ativa && l.SemOrigem);
            int vaz = _linhas.Count(l => l.Ativa && l.Vazios > 0);
            var partes = new List<string> { $"{_grupos.Count(g => _linhas.Any(l => l.Grupo == g))} Pset(s) no IFC", $"{saidas} propriedade(s) ativa(s)" };
            if (sem > 0) partes.Add($"{sem} sem origem no modelo");
            if (vaz > 0) partes.Add($"{vaz} com objetos vazios (preencha no passo 4)");
            if (_alterado) partes.Add("alterações não salvas");
            _resumo.Text = string.Join(" · ", partes) + " · perfil " + ConfigPackage.PerfilAtivo;
            _resumo.Foreground = Theme.Solid(sem + vaz == 0 && !_alterado ? Theme.Verde : Theme.LaranjaTexto);
        }

        // ================================================================== edição

        LinhaSaida Selecionada() => _lista.Grid.SelectedItem as LinhaSaida;

        void Atualizar(LinhaSaida focar = null)
        {
            foreach (var l in _linhas) l.Recalcular(this);
            Ordenar();
            _lista.Refresh();
            Marcar();
            Resumo();
            if (focar != null)
            {
                _lista.Grid.SelectedItem = focar;
                _lista.Grid.ScrollIntoView(focar);
            }
        }

        void NovoPset()
        {
            string nome = Theme.PedirTexto(_dono, "Novo Pset no IFC", "Nome do Pset como vai aparecer no IFC:", "Pset_MetroSP_")?.Trim();
            if (string.IsNullOrEmpty(nome)) return;
            GrupoSaida g = Grupo(nome);
            g.Aberto = true;
            NovaPropriedade(g);
        }

        void NovaPropriedade(GrupoSaida g)
        {
            if (g == null)
            {
                MessageBox.Show(_dono, "Escolha uma linha do Pset ou crie um Pset com \"+ Pset no IFC\".", _dono.Title);
                return;
            }
            string nome = Theme.PedirTexto(_dono, "Nova propriedade no IFC", $"Nome da propriedade como vai sair no IFC (em {g.Nome}):", "")?.Trim();
            if (string.IsNullOrEmpty(nome)) return;
            var l = new LinhaSaida(g) { Propriedade = nome, Tipo = "IfcLabel", Ativa = true };
            _linhas.Add(l);
            g.Aberto = true;
            Atualizar(l);
        }

        void Renomear(GrupoSaida g)
        {
            string nome = Theme.PedirTexto(_dono, "Renomear Pset", "Nome do Pset no IFC:", g.Nome)?.Trim();
            if (string.IsNullOrEmpty(nome) || nome == g.Nome) return;
            if (_grupos.Any(x => x != g && Igual(x.Nome, nome)))
            {
                MessageBox.Show(_dono, $"Já existe um Pset \"{nome}\" na lista.", _dono.Title);
                return;
            }
            g.Nome = nome;
            Atualizar();
        }

        void Entidade(GrupoSaida g)
        {
            var combo = Theme.Digitavel(new ComboBox { ItemsSource = Entidades, Width = 260, Margin = new Thickness(0, 6, 0, 0) });
            combo.Text = g.Entidade;
            var ok = new Button { Content = "OK", IsDefault = true, Style = Theme.PrimaryButton, MinWidth = 80, Margin = new Thickness(0, 0, 6, 0) };
            var cancelar = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 80 };
            var w = new Window { Title = "MetroSP - Aplica-se a", Owner = _dono, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
            ok.Click += (s, e) => w.DialogResult = true;
            var corpo = new StackPanel { Margin = new Thickness(14) };
            corpo.Children.Add(new TextBlock { Text = $"Entidade IFC a que \"{g.Nome}\" se aplica:" });
            corpo.Children.Add(combo);
            var bt = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            bt.Children.Add(ok);
            bt.Children.Add(cancelar);
            corpo.Children.Add(bt);
            w.Content = corpo;
            if (w.ShowDialog() != true || string.IsNullOrWhiteSpace(combo.Text)) return;
            g.Entidade = combo.Text.Trim();
            Atualizar();
        }

        void MenuMais(Button botao)
        {
            var m = new ContextMenu { PlacementTarget = botao, Placement = PlacementMode.Bottom };
            void Item(string texto, string dica, Action a)
            {
                var i = new MenuItem { Header = texto, ToolTip = dica };
                i.Click += (s, e) => a();
                m.Items.Add(i);
            }
            var sel = _lista.Grid.SelectedItems.OfType<LinhaSaida>().ToList();
            Item($"Excluir as selecionadas ({sel.Count})", "Tira as propriedades de saída escolhidas (nada muda no modelo).", () =>
            {
                if (sel.Count == 0) return;
                foreach (var l in sel) _linhas.Remove(l);
                Atualizar();
            });
            Item("Ativar as selecionadas", "Marca \"Ativa\" nas linhas escolhidas.", () => { foreach (var l in sel) l.Ativa = true; Atualizar(); });
            Item("Desativar as selecionadas", "Desmarca \"Ativa\": a propriedade fica no mapeamento, mas não sai no IFC.", () => { foreach (var l in sel) l.Ativa = false; Atualizar(); });
            m.Items.Add(new Separator());
            Item("Abrir todos os Psets", "", () => { foreach (var g in _grupos) g.Aberto = true; });
            Item("Fechar todos os Psets", "", () => { foreach (var g in _grupos) g.Aberto = false; });
            m.Items.Add(new Separator());
            Item("Descartar alterações", "Lê de novo o mapeamento salvo no perfil.", Carregar);
            m.IsOpen = true;
        }

        static string TipoIfc(TipoPropriedade t) =>
            t == TipoPropriedade.Inteiro ? "IfcInteger" : t == TipoPropriedade.Real ? "IfcReal" : t == TipoPropriedade.SimNao ? "IfcBoolean" : "IfcLabel";

        /// <summary>Escolher várias propriedades do modelo e criar as saídas num Pset (o mesmo nome do property set ou outro).</summary>
        void MapearDoModelo()
        {
            var itens = _psetsModelo().Where(p => p.NoDesenho).OrderBy(p => p.Nome, StringComparer.CurrentCultureIgnoreCase)
                .SelectMany(p => p.Propriedades.Select(pr => (Pset: p.Nome, Prop: pr)))
                .Select(x => new CheckBox
                {
                    Content = x.Pset + " › " + x.Prop.Nome,
                    Tag = x,
                    Margin = new Thickness(0, 1, 0, 1),
                    IsChecked = false,
                    Foreground = _linhas.Any(l => Igual(l.Source, "PropertySet") && Igual(l.GrupoOrigem, x.Pset) && Igual(l.NomeOrigem, x.Prop.Nome)) ? Theme.Solid(Theme.Cinza) : null,
                    ToolTip = _linhas.Where(l => Igual(l.Source, "PropertySet") && Igual(l.GrupoOrigem, x.Pset) && Igual(l.NomeOrigem, x.Prop.Nome))
                        .Select(l => "Já sai em " + l.Grupo.Nome + " › " + l.Propriedade).FirstOrDefault(),
                }).ToList();
            if (itens.Count == 0)
            {
                MessageBox.Show(_dono, "O modelo não tem property sets. Crie-os na vista \"No modelo\".", _dono.Title);
                return;
            }
            const string Mesmo = "(o mesmo nome do property set)";
            var filtro = new TextBox { Margin = new Thickness(0, 4, 0, 6), ToolTip = "Filtrar por parte do nome" };
            var lista = new ListBox { Height = 340, MinWidth = 460 };
            foreach (var c in itens) lista.Items.Add(c);
            filtro.TextChanged += (s, e) =>
            {
                string f = filtro.Text.Trim();
                foreach (var c in itens) c.Visibility = f.Length == 0 || c.Content.ToString().IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0 ? Visibility.Visible : Visibility.Collapsed;
            };
            var destino = Theme.Digitavel(new ComboBox { ItemsSource = new[] { Mesmo }.Concat(_grupos.Select(g => g.Nome)).ToList(), Width = 320, Margin = new Thickness(0, 4, 0, 0) });
            destino.Text = Mesmo;
            var ok = new Button { Content = "Mapear", IsDefault = true, Style = Theme.PrimaryButton, MinWidth = 90, Margin = new Thickness(0, 0, 6, 0) };
            var cancelar = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 80 };
            var w = new Window { Title = "MetroSP - Mapear do modelo", Owner = _dono, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.CanResizeWithGrip, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
            ok.Click += (s, e) => w.DialogResult = true;
            var corpo = new StackPanel { Margin = new Thickness(14) };
            corpo.Children.Add(new TextBlock { Text = "Propriedades do modelo que vão para o IFC (cinza: já sai em algum Pset):" });
            corpo.Children.Add(filtro);
            corpo.Children.Add(lista);
            corpo.Children.Add(new TextBlock { Text = "Pset no IFC:", Margin = new Thickness(0, 10, 0, 0) });
            corpo.Children.Add(destino);
            corpo.Children.Add(Theme.Detalhe("A propriedade sai com o mesmo nome; depois dá para renomear na lista."));
            var bt = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            bt.Children.Add(ok);
            bt.Children.Add(cancelar);
            corpo.Children.Add(bt);
            w.Content = corpo;
            if (w.ShowDialog() != true) return;

            string alvo = (destino.Text ?? "").Trim();
            LinhaSaida ultima = null;
            int n = 0;
            foreach (var c in itens.Where(c => c.IsChecked == true))
            {
                var (pset, prop) = ((string, PropriedadeMontagem))c.Tag;
                GrupoSaida g = Grupo(alvo.Length == 0 || alvo == Mesmo ? pset : alvo);
                g.Aberto = true;
                if (_linhas.Any(l => l.Grupo == g && Igual(l.Propriedade, prop.Nome))) continue;
                ultima = new LinhaSaida(g) { Propriedade = prop.Nome, Tipo = TipoIfc(prop.Tipo), Ativa = true, Source = "PropertySet", GrupoOrigem = pset, NomeOrigem = prop.Nome };
                _linhas.Add(ultima);
                n++;
            }
            if (n > 0) Atualizar(ultima);
        }

        static bool Igual(string a, string b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Um Pset de saída no IFC (o grupo da lista).</summary>
    public class GrupoSaida : INotifyPropertyChanged
    {
        string _nome, _entidade;
        bool _aberto = true;
        int _n;
        public string Original { get; set; }
        public string Nome { get => _nome; set { _nome = value; On(nameof(Nome)); } }
        public string Entidade { get => _entidade; set { _entidade = value; On(nameof(Entidade)); On(nameof(EntidadeTexto)); } }
        public string EntidadeTexto => "Aplica-se a: " + Entidade + " ▾";
        public bool Aberto { get => _aberto; set { _aberto = value; On(nameof(Aberto)); } }
        public string Info => _n == 1 ? "1 propriedade" : _n + " propriedades";
        public void Contar(int n) { _n = n; On(nameof(Info)); }
        public override string ToString() => Nome;
        public event PropertyChangedEventHandler PropertyChanged;
        void On(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    /// <summary>Uma propriedade que sai no IFC e de onde o valor vem no modelo.</summary>
    public class LinhaSaida : INotifyPropertyChanged
    {
        public LinhaSaida(GrupoSaida g) { Grupo = g; }

        public GrupoSaida Grupo { get; }
        public List<string> Extra { get; set; } = new List<string>();

        string _prop, _tipo;
        bool _ativa = true;
        public string Propriedade { get => _prop; set { _prop = value; On(nameof(Propriedade)); } }
        public string Tipo { get => _tipo; set { _tipo = value; On(nameof(Tipo)); } }
        public bool Ativa { get => _ativa; set { _ativa = value; On(nameof(Ativa)); On(nameof(Pendente)); } }

        public string Source { get; set; } = "";
        public string GrupoOrigem { get; set; } = "";
        public string NomeOrigem { get; set; } = "";

        public string VemDe
        {
            get => SaidaIfcPainel.Formatar(Source, GrupoOrigem, NomeOrigem);
            set
            {
                var (s, g, n) = SaidaIfcPainel.Ler(value);
                Source = s; GrupoOrigem = g; NomeOrigem = n;
                On(nameof(VemDe)); On(nameof(VemDeTexto));
            }
        }

        public string VemDeTexto => string.IsNullOrWhiteSpace(NomeOrigem) ? "sem origem" : VemDe + (_naoExiste ? "  (não existe no modelo)" : "");

        bool _naoExiste;
        public bool SemOrigem { get; private set; }
        public int? Preenchidos { get; private set; }
        public int? Vazios { get; private set; }
        public string PreenchidosTexto => Preenchidos?.ToString("N0") ?? "—";
        public string VaziosTexto => Vazios?.ToString("N0") ?? "—";
        public bool Pendente => Ativa && (SemOrigem || Vazios > 0);
        public string Dica { get; private set; }

        internal void Recalcular(SaidaIfcPainel p)
        {
            _naoExiste = false;
            Preenchidos = Vazios = null;
            if (string.IsNullOrWhiteSpace(NomeOrigem) || string.IsNullOrWhiteSpace(Source)) SemOrigem = true;
            else if (string.Equals(Source, "PropertySet", StringComparison.OrdinalIgnoreCase))
            {
                _naoExiste = !p.ExisteNoModelo(GrupoOrigem, NomeOrigem);
                SemOrigem = _naoExiste;
                if (!_naoExiste)
                {
                    int anex = p.Anexados(GrupoOrigem);
                    int vz = Math.Min(anex, p.Vazios(GrupoOrigem, NomeOrigem));
                    Preenchidos = anex - vz;
                    Vazios = vz;
                }
            }
            else SemOrigem = false;
            Dica = SemOrigem
                ? (_naoExiste ? $"O property set {GrupoOrigem} › {NomeOrigem} não existe no modelo: crie na vista No modelo ou escolha outra origem." : "Escolha em \"Vem de\" de qual propriedade do modelo o valor é lido.")
                : $"No IFC: {Grupo.Nome} › {Propriedade} ({Tipo}). Lido de {VemDe}.";
            foreach (var n in new[] { nameof(SemOrigem), nameof(Preenchidos), nameof(Vazios), nameof(PreenchidosTexto), nameof(VaziosTexto), nameof(Pendente), nameof(Dica), nameof(VemDeTexto) }) On(n);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        void On(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }
}
