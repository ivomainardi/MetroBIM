using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using MetroSP.Config;
using MetroSP.Ifc;

namespace MetroSP.Ui
{
    /// <summary>
    /// Classes IFC do perfil, como a Categorias IFC do Revit: cada nome do perfil (regra) ou do modelo (layer, bloco ou
    /// baseline) numa linha, com Exportar e a classe no IFC4 e no IFC 4.3 lado a lado. Cada esquema tem a sua tabela
    /// (IfcInfraExportMapping.json para o IFC 4.3 e IfcInfraExportMapping.IFC4.json para o IFC4); ao exportar, vai a do
    /// esquema escolhido no perfil.
    /// </summary>
    public class ClassesIfcWindow : Window
    {
        public const string V4 = ConfigPackage.IfcVersionIfc4, V43 = ConfigPackage.IfcVersionIfc43;
        static readonly string[] Secoes = { ConteudoDesenho.Layers, ConteudoDesenho.Blocos, ConteudoDesenho.Baselines };
        static readonly Dictionary<string, string> Rotulos = new Dictionary<string, string>
        {
            [ConteudoDesenho.Layers] = "Por layer", [ConteudoDesenho.Blocos] = "Por bloco", [ConteudoDesenho.Baselines] = "Por baseline de corredor",
        };

        readonly Dictionary<string, JsonObject> _tabela = new Dictionary<string, JsonObject>();
        readonly Dictionary<string, List<ItemDesenho>> _modelo;
        readonly ObservableCollection<LinhaIfc> _linhas = new ObservableCollection<LinhaIfc>();
        readonly FilterableGrid _lista;
        readonly ComboBox _secaoCombo;
        readonly CheckBox _soUsados, _soDiferentes, _soErros;
        readonly TextBlock _resumo = Theme.Detalhe();
        readonly Button _corrigir;
        readonly string _versaoExportacao;
        string _secao;
        bool _alterado;

        /// <summary>true depois de salvar.</summary>
        public bool Mudou { get; private set; }

        public ClassesIfcWindow()
        {
            Title = "MetroSP - Classes IFC";
            Width = 1120;
            Height = 680;
            MinWidth = 820;
            MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = Theme.Solid(Theme.Preto);

            ConfigPackage.EnsureActive();
            _versaoExportacao = ConfigPackage.IfcVersionDoPerfil();
            _tabela[V43] = ConfigPackage.LoadMapping(V43);
            _tabela[V4] = ConfigPackage.LoadMapping(V4);
            try { _modelo = ConteudoDesenho.Ler(); }
            catch { _modelo = new Dictionary<string, List<ItemDesenho>>(); }

            var corpo = new DockPanel();
            var topo = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(topo, Dock.Top);
            _secaoCombo = Theme.Digitavel(new ComboBox { Width = 200, Margin = new Thickness(0, 0, 14, 4) });
            foreach (string k in Secoes.Union(_tabela.Values.SelectMany(t => t.Where(kv => kv.Value is JsonArray).Select(kv => kv.Key))))
                _secaoCombo.Items.Add(new ComboBoxItem { Content = Rotulos.TryGetValue(k, out string r) ? r : k, Tag = k, ToolTip = k });
            _secaoCombo.SelectionChanged += (s, e) =>
            {
                if ((_secaoCombo.SelectedItem as ComboBoxItem)?.Tag is string k) { _secao = k; Recalcular(); }
            };
            topo.Children.Add(_secaoCombo);
            _soUsados = new CheckBox { Content = "Só os usados no modelo", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 4),
                ToolTip = "Esconde as regras do perfil que não pegam nada no modelo." };
            _soDiferentes = new CheckBox { Content = "Só os diferentes entre IFC4 e IFC 4.3", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 4) };
            _soUsados.Click += (s, e) => _lista.Refresh();
            _soDiferentes.Click += (s, e) => _lista.Refresh();
            _soErros = new CheckBox { Content = "Só os erros", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 4),
                ToolTip = "Só as linhas exportadas sem classe ou com classe que não existe no esquema da coluna." };
            _soErros.Click += (s, e) => _lista.Refresh();
            topo.Children.Add(_soUsados);
            topo.Children.Add(_soDiferentes);
            topo.Children.Add(_soErros);
            corpo.Children.Add(topo);

            _lista = new FilterableGrid(_linhas, typeof(LinhaIfc));
            _lista.Grid.IsReadOnly = false;
            _lista.Grid.FrozenColumnCount = 1;
            Theme.StyleSelection(_lista.Grid);
            _lista.AddColumn("Nome", nameof(LinhaIfc.NomeTexto), 200, fill: true).IsReadOnly = true;
            var obj = _lista.AddColumn("No modelo", nameof(LinhaIfc.NoModelo), 80);
            obj.IsReadOnly = true;
            obj.SortMemberPath = nameof(LinhaIfc.Objetos);
            obj.ElementStyle = new Style(typeof(TextBlock), obj.ElementStyle) { Setters = { new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right) } };
            ColunaExportar("Exp. IFC4", nameof(LinhaIfc.Exportar4));
            ColunaClasse("Classe IFC4", nameof(LinhaIfc.Classe4), nameof(LinhaIfc.Texto4), nameof(LinhaIfc.Invalida4), nameof(LinhaIfc.Dica4), IfcCatalog.Ifc4);
            ColunaExportar("Exp. IFC 4.3", nameof(LinhaIfc.Exportar43));
            ColunaClasse("Classe IFC 4.3", nameof(LinhaIfc.Classe43), nameof(LinhaIfc.Texto43), nameof(LinhaIfc.Invalida43), nameof(LinhaIfc.Dica43), IfcCatalog.Ifc4x3);
            _lista.ExtraFilter = o => o is LinhaIfc l && (_soUsados.IsChecked != true || l.Objetos > 0) && (_soDiferentes.IsChecked != true || l.Diferente) && (_soErros.IsChecked != true || l.Erro);
            _lista.Filtered += AtualizarResumo;
            _lista.Grid.ToolTip = "Clique duas vezes na classe para escolher da lista (digite parte do nome para filtrar). " +
                                  "Mudar a classe de um nome que segue uma regra geral (ex.: ^A-.*) cria uma regra só para ele.";
            var estilo = new Style(typeof(DataGridRow), _lista.Grid.RowStyle);
            var sem = new DataTrigger { Binding = new Binding(nameof(LinhaIfc.SemRegra)), Value = true };
            sem.Setters.Add(new Setter(BackgroundProperty, Theme.LinhaProblema));
            sem.Setters.Add(new Setter(ToolTipProperty, "Nenhuma regra do perfil pega este nome: sai com a classe que a Autodesk escolher."));
            estilo.Triggers.Add(sem);
            var soPerfil = new DataTrigger { Binding = new Binding(nameof(LinhaIfc.SoNoPerfil)), Value = true };
            soPerfil.Setters.Add(new Setter(ForegroundProperty, Theme.Solid(Theme.Cinza)));
            soPerfil.Setters.Add(new Setter(ToolTipProperty, "Regra do perfil que não pega nada neste modelo."));
            estilo.Triggers.Add(soPerfil);
            _lista.Grid.RowStyle = estilo;
            _lista.Grid.CellEditEnding += (s, e) => Dispatcher.BeginInvoke(new Action(Recalcular), System.Windows.Threading.DispatcherPriority.Background);
            _resumo.TextWrapping = TextWrapping.NoWrap;
            _resumo.TextTrimming = TextTrimming.CharacterEllipsis;
            _resumo.Margin = new Thickness(0, 4, 0, 0);
            DockPanel.SetDock(_resumo, Dock.Bottom);
            corpo.Children.Add(_resumo);
            corpo.Children.Add(_lista);

            var copiar = new Button { Content = "Copiar entre IFC schemas ▾", ToolTip = "Copia classe e Exportar de um IFC schema para o outro: só nas linhas selecionadas (Ctrl ou Shift + clique para escolher várias) ou na tabela inteira." };
            copiar.Click += (s, e) =>
            {
                int n = Selecionadas().Count;
                var itens = new List<(string, Action)>();
                if (n > 0)
                {
                    string sel = n == 1 ? "só a linha selecionada" : $"só as {n} linhas selecionadas";
                    itens.Add(($"IFC4 → IFC 4.3: {sel}", () => Copiar(V4, V43, true)));
                    itens.Add(($"IFC 4.3 → IFC4: {sel}", () => Copiar(V43, V4, true)));
                }
                itens.Add(("IFC4 → IFC 4.3: tabela inteira", () => Copiar(V4, V43, false)));
                itens.Add(("IFC 4.3 → IFC4: tabela inteira", () => Copiar(V43, V4, false)));
                Menu(copiar, itens.ToArray());
            };
            string perfil = ConfigPackage.PerfilAtivo;
            var padrao = new Button { Content = "Voltar ao padrão ▾", ToolTip = "Sobrescreve com o perfil " + (ConfigPackage.PreConfigurado(perfil) ? perfil : ConfigPackage.PerfilMetroSP) + " como veio no plugin." };
            padrao.Click += (s, e) =>
            {
                int n = Selecionadas().Count;
                var itens = new List<(string, Action)>();
                if (n > 0)
                {
                    string sel = n == 1 ? "na linha selecionada" : $"nas {n} linhas selecionadas";
                    itens.Add(($"IFC4 e IFC 4.3 {sel}", () => Padrao(new[] { V4, V43 }, true)));
                    itens.Add(($"Só IFC4 {sel}", () => Padrao(new[] { V4 }, true)));
                    itens.Add(($"Só IFC 4.3 {sel}", () => Padrao(new[] { V43 }, true)));
                }
                itens.Add(("IFC4 e IFC 4.3: tabela inteira", () => Padrao(new[] { V4, V43 }, false)));
                Menu(padrao, itens.ToArray());
            };
            var regras = new Button { Content = "Regras ▾", ToolTip = "Regras gerais por nome ou expressão regular (ex.: ^U-3D-.*). Vale a primeira que casar, de cima para baixo." };
            regras.Click += (s, e) => Menu(regras,
                ("Nova regra (nome ou expressão)...", NovaRegra),
                ("Excluir a regra das linhas selecionadas", ExcluirRegras));
            _corrigir = new Button { Content = "Corrigir classes" };
            _corrigir.Click += (s, e) => CorrigirClasses();
            foreach (Button b in new[] { copiar, padrao, regras, _corrigir })
            {
                b.Padding = new Thickness(10, 2, 10, 2);
                b.Margin = new Thickness(0, 0, 6, 4);
                b.VerticalAlignment = VerticalAlignment.Center;
                topo.Children.Add(b);
            }
            var salvar = new Button { Content = "Salvar", Style = Theme.PrimaryButton, IsDefault = true, ToolTip = "Grava as duas tabelas no perfil e fecha." };
            salvar.Click += (s, e) => { if (Salvar()) Close(); };
            var fechar = new Button { Content = "Fechar", IsCancel = true };
            Closing += (s, e) =>
            {
                if (!_alterado) return;
                var r = MessageBox.Show(this, "Salvar as alterações nas classes IFC?", Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (r == MessageBoxResult.Cancel) e.Cancel = true;
                else if (r == MessageBoxResult.Yes && !Salvar()) e.Cancel = true;
            };

            Theme.Apply(this, "Classes IFC", $"Perfil {perfil}: em que classe cada nome do perfil e do modelo sai no IFC4 e no IFC 4.3. " +
                                             $"A exportação usa o {IfcCatalog.Label(_versaoExportacao)}.",
                corpo, Theme.Rodape(null, fechar, salvar));
            _secaoCombo.SelectedIndex = 0;
        }

        // ================================================================== colunas

        void ColunaExportar(string titulo, string prop)
        {
            var marca = new FrameworkElementFactory(typeof(CheckBox));
            marca.SetBinding(ToggleButton.IsCheckedProperty, new Binding(prop) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            marca.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            marca.SetValue(ToolTipProperty, "Desmarcado: não vai para o IFC neste esquema.");
            marca.AddHandler(ToggleButton.ClickEvent, new RoutedEventHandler((s, e) =>
                Dispatcher.BeginInvoke(new Action(Recalcular), System.Windows.Threading.DispatcherPriority.Background)));
            _lista.Grid.Columns.Add(new DataGridTemplateColumn { Header = titulo, CellTemplate = new DataTemplate { VisualTree = marca }, Width = 72, IsReadOnly = true, SortMemberPath = prop });
        }

        /// <summary>Coluna de classe com filtro no título; edita numa lista em que também se escreve.</summary>
        void ColunaClasse(string titulo, string prop, string mostrar, string invalida, string dica, string[] catalogo)
        {
            DataGridTextColumn texto = _lista.AddColumn(titulo, prop, 240);
            int i = _lista.Grid.Columns.IndexOf(texto);
            _lista.Grid.Columns.Remove(texto);

            var ver = new FrameworkElementFactory(typeof(TextBlock));
            ver.SetBinding(TextBlock.TextProperty, new Binding(mostrar));
            ver.SetBinding(ToolTipProperty, new Binding(dica));
            ver.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            ver.SetValue(MarginProperty, new Thickness(2, 0, 2, 0));
            var estilo = new Style(typeof(TextBlock));
            var t = new DataTrigger { Binding = new Binding(invalida), Value = true };
            t.Setters.Add(new Setter(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.Firebrick));
            t.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
            estilo.Triggers.Add(t);
            var vazia = new DataTrigger { Binding = new Binding(prop), Value = null };
            vazia.Setters.Add(new Setter(TextBlock.ForegroundProperty, Theme.Solid(Theme.LaranjaTexto)));
            vazia.Setters.Add(new Setter(TextBlock.FontStyleProperty, FontStyles.Italic));
            estilo.Triggers.Add(vazia);
            var vazia2 = new DataTrigger { Binding = new Binding(prop), Value = "" };
            foreach (var st in vazia.Setters) vazia2.Setters.Add(st);
            estilo.Triggers.Add(vazia2);
            ver.SetValue(StyleProperty, estilo);

            var editar = new FrameworkElementFactory(typeof(ComboBox));
            editar.SetValue(ComboBox.IsEditableProperty, true);
            editar.SetValue(ComboBox.IsTextSearchEnabledProperty, true);
            editar.SetValue(ComboBox.StaysOpenOnEditProperty, true);
            editar.SetValue(ComboBox.MaxDropDownHeightProperty, 360.0);
            editar.SetBinding(ComboBox.TextProperty, new Binding(prop) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus });
            editar.AddHandler(LoadedEvent, new RoutedEventHandler((s, e) =>
            {
                var c = (ComboBox)s;
                string atual = c.Text;
                c.ItemsSource = new[] { "" }.Concat(catalogo).ToList();
                c.Text = atual;
                c.Focus();
            }));
            _lista.Grid.Columns.Insert(i, new DataGridTemplateColumn
            {
                Header = texto.Header, SortMemberPath = prop, Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 160,
                CellTemplate = new DataTemplate { VisualTree = ver },
                CellEditingTemplate = new DataTemplate { VisualTree = editar },
            });
        }

        // ================================================================== tabelas

        JsonArray Regras(string esquema, bool criar = false)
        {
            JsonObject t = _tabela[esquema];
            if (t[_secao] is JsonArray a) return a;
            if (!criar) return new JsonArray();
            t[_secao] = a = new JsonArray();
            return a;
        }

        static string NomeDe(JsonObject r) => ((string)r["Name"] ?? "").Trim();

        static readonly Dictionary<string, Regex> _regex = new Dictionary<string, Regex>();

        /// <summary>Nome igual (sem diferenciar maiúsculas) ou expressão regular que casa, como o exportador.</summary>
        public static bool Casa(string regra, string nome)
        {
            if (string.IsNullOrWhiteSpace(regra) || nome == null) return false;
            if (regra.Trim().Equals(nome, StringComparison.OrdinalIgnoreCase)) return true;
            if (!_regex.TryGetValue(regra, out Regex re))
            {
                try { re = new Regex(regra.Trim(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
                catch { re = null; }
                _regex[regra] = re;
            }
            return re != null && re.IsMatch(nome);
        }

        /// <summary>Regra que vale para um nome do modelo: a primeira que casa.</summary>
        static JsonObject QueVale(JsonArray regras, string nome) => regras.OfType<JsonObject>().FirstOrDefault(r => Casa(NomeDe(r), nome));

        /// <summary>Regra com exatamente este nome.</summary>
        static JsonObject Exata(JsonArray regras, string nome) =>
            regras.OfType<JsonObject>().FirstOrDefault(r => EhDoNome(NomeDe(r), nome));

        /// <summary>Regra só para um nome: ^nome$ (o exportador lê o nome como expressão regular).</summary>
        public static string Propria(string nome) => "^" + Regex.Escape(nome) + "$";

        /// <summary>A regra é deste nome: igual a ele ou ^nome$.</summary>
        public static bool EhDoNome(string regra, string nome) =>
            regra != null && (regra.Trim().Equals(nome, StringComparison.OrdinalIgnoreCase) || regra.Trim().Equals(Propria(nome), StringComparison.OrdinalIgnoreCase));

        /// <summary>Monta as linhas da seção: os nomes do modelo e as regras do perfil que não são nome do modelo.</summary>
        void Recalcular()
        {
            if (_secao == null) return;
            var selecionadas = new HashSet<string>(Selecionadas().Select(l => l.Nome), StringComparer.OrdinalIgnoreCase);
            JsonArray r4 = Regras(V4), r43 = Regras(V43);
            List<ItemDesenho> itens = ConteudoDesenho.Le(_secao) && _modelo.TryGetValue(_secao, out var lst) ? lst : new List<ItemDesenho>();
            var nomes = new HashSet<string>(itens.Select(i => i.Nome), StringComparer.OrdinalIgnoreCase);
            var novas = new List<LinhaIfc>();
            var objetosDaRegra = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (ItemDesenho it in itens)
            {
                JsonObject q4 = QueVale(r4, it.Nome), q43 = QueVale(r43, it.Nome);
                foreach (JsonObject q in new[] { q43 ?? q4 }.Where(q => q != null && !EhDoNome(NomeDe(q), it.Nome)))
                    objetosDaRegra[NomeDe(q)] = (objetosDaRegra.TryGetValue(NomeDe(q), out int n) ? n : 0) + it.Objetos;
                novas.Add(new LinhaIfc(this, it.Nome, it.Objetos, q4, q43, true));
            }
            var vistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonObject r in r43.OfType<JsonObject>().Concat(r4.OfType<JsonObject>()))
            {
                string n = NomeDe(r);
                if (n.Length == 0 || nomes.Contains(n) || itens.Any(it => EhDoNome(n, it.Nome)) || !vistas.Add(n)) continue;
                novas.Add(new LinhaIfc(this, n, objetosDaRegra.TryGetValue(n, out int o) ? o : 0, Exata(r4, n), Exata(r43, n), false));
            }
            _linhas.Clear();
            foreach (LinhaIfc l in novas.OrderBy(l => l.DoModelo ? 0 : 1).ThenBy(l => l.Nome, StringComparer.CurrentCultureIgnoreCase)) _linhas.Add(l);
            foreach (LinhaIfc l in _linhas.Where(l => selecionadas.Contains(l.Nome))) _lista.Grid.SelectedItems.Add(l);
            AtualizarResumo();
        }

        void AtualizarResumo()
        {
            var vis = _lista.VisibleItems<LinhaIfc>().ToList();
            var (um, varios) = ConteudoDesenho.Palavra(_secao);
            int doModelo = _linhas.Count(l => l.DoModelo && l.Objetos > 0), semRegra = _linhas.Count(l => l.SemRegra && l.Objetos > 0), dif = _linhas.Count(l => l.Diferente);
            int invalidas = _linhas.Count(l => l.Invalida4 || l.Invalida43);
            _resumo.Text = $"{doModelo} {(doModelo == 1 ? um : varios)} com objetos no modelo · {semRegra} sem regra · {dif} diferente(s) entre IFC4 e IFC 4.3" +
                           (invalidas > 0 ? $" · {invalidas} com classe fora do esquema" : "") + (vis.Count < _linhas.Count ? $" · mostrando {vis.Count}" : "");
            int corrigiveis = Corrigiveis().Count;
            _corrigir.Visibility = corrigiveis > 0 ? Visibility.Visible : Visibility.Collapsed;
            _corrigir.Content = $"Corrigir {corrigiveis} classe(s)";
            _corrigir.ToolTip = "Troca as classes fora do esquema pela correção óbvia (ex.: IfcBuiltElement.USERDEFINED → IfcBuiltElement; no IFC4, as classes que não existem nele pela equivalente).";
        }

        List<LinhaIfc> Selecionadas() => _lista?.Grid.SelectedItems.OfType<LinhaIfc>().ToList() ?? new List<LinhaIfc>();

        /// <summary>
        /// Muda a classe e/ou Exportar de uma linha num esquema. Linha com regra própria muda a regra; nome do modelo que segue
        /// uma regra geral (ou nenhuma) ganha uma regra só para ele, no topo, para valer antes das gerais.
        /// </summary>
        internal void Definir(LinhaIfc l, string esquema, string classe, bool? exportar)
        {
            JsonArray regras = Regras(esquema, criar: true);
            JsonObject r = Exata(regras, l.Nome);
            if (r == null)
            {
                JsonObject vale = l.DoModelo ? QueVale(regras, l.Nome) : null;
                r = new JsonObject
                {
                    ["Name"] = l.DoModelo ? Propria(l.Nome) : l.Nome,
                    ["IfcExportAs"] = (string)vale?["IfcExportAs"] ?? "",
                    ["Export"] = (bool?)vale?["Export"] ?? true,
                };
                regras.Insert(0, r);
            }
            if (classe != null) r["IfcExportAs"] = classe.Trim();
            if (exportar != null) r["Export"] = exportar.Value;
            _alterado = true;
        }

        void Copiar(string de, string para, bool soSelecionadas)
        {
            _lista.Grid.CommitEdit(DataGridEditingUnit.Row, true);
            string nomeDe = de == V4 ? "IFC4" : "IFC 4.3", nomePara = para == V4 ? "IFC4" : "IFC 4.3";
            if (!soSelecionadas)
            {
                if (MessageBox.Show(this, $"Copiar a tabela inteira ({(Rotulos.TryGetValue(_secao, out string r) ? r.ToLowerInvariant() : _secao)}) do {nomeDe} para o {nomePara}?",
                        Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                _tabela[para][_secao] = Regras(de).DeepClone();
                _alterado = true;
                Recalcular();
                return;
            }
            foreach (LinhaIfc l in Selecionadas())
                Definir(l, para, l.Classe(de) ?? "", l.Exporta(de));
            Recalcular();
        }

        void Padrao(string[] esquemas, bool soSelecionadas)
        {
            _lista.Grid.CommitEdit(DataGridEditingUnit.Row, true);
            string perfil = ConfigPackage.PerfilAtivo;
            string dir = ConfigPackage.OrigemPreConfigurada(perfil) ?? ConfigPackage.DefaultDir;
            JsonObject p43 = Ler(Path.Combine(dir, ConfigPackage.FileNames[ConfigKind.ObjectMapping])) ?? new JsonObject();
            JsonObject p4 = Ler(Path.Combine(dir, ConfigPackage.FileNames[ConfigKind.ObjectMappingIfc4])) ?? ConfigPackage.ParaIfc4(p43);
            var padrao = new Dictionary<string, JsonArray> { [V43] = p43[_secao] as JsonArray ?? new JsonArray(), [V4] = p4[_secao] as JsonArray ?? new JsonArray() };
            List<LinhaIfc> linhas = Selecionadas();
            string alvo = soSelecionadas ? (linhas.Count == 1 ? "a linha " + linhas[0].Nome : $"as {linhas.Count} linhas selecionadas") : "a tabela inteira";
            if (MessageBox.Show(this, $"Sobrescrever {alvo} com o padrão ({string.Join(" e ", esquemas.Select(e => e == V4 ? "IFC4" : "IFC 4.3"))})?\n\nNada é gravado até você clicar em Salvar.",
                    Title, MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            foreach (string e in esquemas)
            {
                if (!soSelecionadas) { _tabela[e][_secao] = padrao[e].DeepClone(); continue; }
                foreach (LinhaIfc l in linhas)
                {
                    JsonObject p = l.DoModelo ? QueVale(padrao[e], l.Nome) : Exata(padrao[e], l.Nome);
                    JsonArray regras = Regras(e, criar: true);
                    if (p != null) { Definir(l, e, (string)p["IfcExportAs"] ?? "", (bool?)p["Export"] ?? true); continue; }
                    JsonObject propria = Exata(regras, l.Nome);   // o padrão não tem regra para ele: tira a própria
                    if (propria != null) regras.Remove(propria);
                }
            }
            _alterado = true;
            Recalcular();
        }

        static JsonObject Ler(string caminho)
        {
            try { return File.Exists(caminho) ? JsonNode.Parse(File.ReadAllText(caminho), null, new System.Text.Json.JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = System.Text.Json.JsonCommentHandling.Skip }) as JsonObject : null; }
            catch { return null; }
        }

        void NovaRegra()
        {
            string nome = Theme.PedirTexto(this, "Nova regra", "Nome ou expressão regular (ex.: ^U-3D-DORMENTE.*). Entra no topo, nos dois esquemas:", "")?.Trim();
            if (string.IsNullOrEmpty(nome)) return;
            foreach (string e in new[] { V4, V43 })
                if (Exata(Regras(e), nome) == null)
                    Regras(e, criar: true).Insert(0, new JsonObject { ["Name"] = nome, ["IfcExportAs"] = "", ["Export"] = true });
            _alterado = true;
            _soUsados.IsChecked = false;
            Recalcular();
        }

        void ExcluirRegras()
        {
            List<LinhaIfc> linhas = Selecionadas().Where(l => l.TemRegraPropria).ToList();
            if (linhas.Count == 0) { MessageBox.Show(this, "As linhas selecionadas não têm regra própria (seguem uma regra geral ou nenhuma).", Title); return; }
            if (MessageBox.Show(this, $"Excluir a regra de {linhas.Count} linha(s) nos dois esquemas?", Title, MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            foreach (LinhaIfc l in linhas)
                foreach (string e in new[] { V4, V43 })
                {
                    JsonObject r = Exata(Regras(e), l.Nome);
                    if (r != null) Regras(e).Remove(r);
                }
            _alterado = true;
            Recalcular();
        }

        List<(JsonObject Regra, string Nova)> Corrigiveis()
        {
            var r = new List<(JsonObject, string)>();
            foreach (var (esquema, cat) in new[] { (V4, IfcCatalog.Ifc4), (V43, IfcCatalog.Ifc4x3) })
                foreach (var a in _tabela[esquema].Select(kv => kv.Value).OfType<JsonArray>())
                    foreach (JsonObject o in a.OfType<JsonObject>())
                    {
                        string c = (string)o["IfcExportAs"];
                        if (IfcCatalog.IsValid(c, cat)) continue;
                        string nova = esquema == V4 ? IfcCatalog.ParaIfc4(c) : IfcCatalog.Sugestao(c, cat);
                        if (nova != null && !nova.Equals(c)) r.Add((o, nova));
                    }
            return r;
        }

        void CorrigirClasses()
        {
            var lista = Corrigiveis();
            if (lista.Count == 0) return;
            foreach (var (o, nova) in lista) o["IfcExportAs"] = nova;
            _alterado = true;
            Recalcular();
        }

        bool Salvar()
        {
            _lista.Grid.CommitEdit(DataGridEditingUnit.Row, true);
            try
            {
                ConfigPackage.SaveJson(ConfigKind.ObjectMapping, _tabela[V43]);
                ConfigPackage.SaveJson(ConfigKind.ObjectMappingIfc4, _tabela[V4]);
                _alterado = false;
                Mudou = true;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        void Menu(Button botao, params (string Texto, Action Acao)[] itens)
        {
            var m = new ContextMenu { PlacementTarget = botao, Placement = PlacementMode.Top };
            foreach (var (texto, acao) in itens)
            {
                var i = new MenuItem { Header = texto };
                i.Click += (s, e) => acao();
                m.Items.Add(i);
            }
            m.IsOpen = true;
        }
    }

    /// <summary>Linha de Classes IFC: um nome do modelo ou uma regra do perfil, com a classe que vale em cada esquema.</summary>
    public class LinhaIfc
    {
        readonly ClassesIfcWindow _janela;
        readonly JsonObject _r4, _r43;

        public LinhaIfc(ClassesIfcWindow janela, string nome, int objetos, JsonObject r4, JsonObject r43, bool doModelo)
        {
            _janela = janela; Nome = nome; Objetos = objetos; _r4 = r4; _r43 = r43; DoModelo = doModelo;
        }

        public string Nome { get; }
        /// <summary>O nome e, se ele segue uma regra geral, a regra entre parênteses.</summary>
        public string NomeTexto
        {
            get
            {
                JsonObject r = _r43 ?? _r4;
                return r != null && DoModelo && !Propria(r) ? $"{Nome}  (regra {(string)r["Name"]})" : Nome;
            }
        }
        /// <summary>Exportado sem classe, ou com classe fora do esquema, em algum dos dois.</summary>
        public bool Erro => (Exportar4 && (Invalida4 || string.IsNullOrWhiteSpace(Classe4))) || (Exportar43 && (Invalida43 || string.IsNullOrWhiteSpace(Classe43)));
        public int Objetos { get; }
        public bool DoModelo { get; }
        public string NoModelo => Objetos > 0 ? Objetos.ToString("N0", CultureInfo.GetCultureInfo("pt-BR")) : DoModelo ? "0" : "—";
        public bool SemRegra => DoModelo && _r4 == null && _r43 == null;
        public bool SoNoPerfil => !DoModelo && Objetos == 0;

        JsonObject R(string e) => e == ClassesIfcWindow.V4 ? _r4 : _r43;
        public string Classe(string e) => (string)R(e)?["IfcExportAs"];
        public bool Exporta(string e) => (bool?)R(e)?["Export"] ?? true;
        bool Propria(JsonObject r) => r != null && ClassesIfcWindow.EhDoNome((string)r["Name"], Nome);
        public bool TemRegraPropria => Propria(_r4) || Propria(_r43);

        string Dica(string e)
        {
            JsonObject r = R(e);
            if (r == null) return "Sem regra: a classe que a Autodesk escolher. Clique duas vezes para escolher.";
            return Propria(r) ? "Regra própria. Clique duas vezes para trocar." : "Pela regra " + (string)r["Name"] + ". Trocar aqui cria uma regra só para " + Nome + ".";
        }

        public string Classe4 { get => Classe(ClassesIfcWindow.V4); set { if ((value ?? "") != (Classe4 ?? "")) _janela.Definir(this, ClassesIfcWindow.V4, value ?? "", null); } }
        public string Classe43 { get => Classe(ClassesIfcWindow.V43); set { if ((value ?? "") != (Classe43 ?? "")) _janela.Definir(this, ClassesIfcWindow.V43, value ?? "", null); } }
        public bool Exportar4 { get => Exporta(ClassesIfcWindow.V4); set { if (value != Exportar4) _janela.Definir(this, ClassesIfcWindow.V4, null, value); } }
        public bool Exportar43 { get => Exporta(ClassesIfcWindow.V43); set { if (value != Exportar43) _janela.Definir(this, ClassesIfcWindow.V43, null, value); } }
        static string Texto(string classe, bool exporta) => !exporta ? (string.IsNullOrWhiteSpace(classe) ? "(não exporta)" : classe)
            : string.IsNullOrWhiteSpace(classe) ? "sem classe (Autodesk escolhe)" : classe;
        public string Texto4 => Texto(Classe4, Exportar4);
        public string Texto43 => Texto(Classe43, Exportar43);
        public bool Invalida4 => !IfcCatalog.IsValid(Classe4, IfcCatalog.Ifc4);
        public bool Invalida43 => !IfcCatalog.IsValid(Classe43, IfcCatalog.Ifc4x3);
        public string Dica4 => (Invalida4 ? "Esta classe não existe no IFC4. " : "") + Dica(ClassesIfcWindow.V4);
        public string Dica43 => (Invalida43 ? "Esta classe não existe no IFC 4.3. " : "") + Dica(ClassesIfcWindow.V43);
        public bool Diferente => !string.Equals(Classe4 ?? "", Classe43 ?? "", StringComparison.OrdinalIgnoreCase) || Exportar4 != Exportar43;
    }
}
