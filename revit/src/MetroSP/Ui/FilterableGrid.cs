using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace MetroSP.Ui
{
    /// <summary>
    /// DataGrid com uma linha de filtros (uma caixa por coluna) e ordenação por várias colunas
    /// (clique no cabeçalho; Shift+clique acrescenta colunas à ordenação).
    /// Os filtros usam a ListCollectionView, então a ordenação é mantida ao filtrar.
    /// Colunas ordenadas mostram seta e posição na ordenação (ex.: "Nome ▲1", "Tipo ▼2").
    /// Por padrão cada linha ocupa uma linha só: texto longo termina em "..." e aparece inteiro no tooltip.
    /// </summary>
    public class FilterableGrid : DockPanel
    {
        private readonly ListCollectionView _view;
        private readonly StackPanel _filterRow;
        private readonly List<FiltroColuna> _filters = new List<FiltroColuna>();

        /// <summary>Filtro de uma coluna: texto digitado e/ou lista de valores marcados (como no Excel).</summary>
        private class FiltroColuna
        {
            public PropertyInfo Prop;
            public TextBox Box;
            public Button Botao;
            public string Titulo;
            /// <summary>Valores aceitos; null = todos.</summary>
            public HashSet<string> Valores;
        }

        private const string Vazio = "(vazio)";
        private readonly Type _itemType;
        private readonly Dictionary<DataGridColumn, (string Text, bool Wrap)> _columns = new Dictionary<DataGridColumn, (string, bool)>();
        private bool _singleLine = true;

        public DataGrid Grid { get; }

        /// <summary>Filtro adicional (ex.: "só pendências"). Chame Refresh() depois de alterar.</summary>
        public Func<object, bool> ExtraFilter { get; set; }

        /// <summary>Itens visíveis, já filtrados e na ordem atual da tabela.</summary>
        public IEnumerable<T> VisibleItems<T>() => _view.Cast<T>();

        public FilterableGrid(IList items, Type itemType)
        {
            _itemType = itemType;
            _view = new ListCollectionView(items) { Filter = Pass };

            _filterRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(1, 0, 0, 2) };
            DockPanel.SetDock(_filterRow, Dock.Top);
            Children.Add(_filterRow);

            Grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserSortColumns = true,
                CanUserResizeColumns = true,
                CanUserReorderColumns = false, // mantém os filtros alinhados às colunas
                HeadersVisibility = DataGridHeadersVisibility.Column,
                SelectionMode = DataGridSelectionMode.Extended,
                ClipboardCopyMode = DataGridClipboardCopyMode.ExcludeHeader,
                ItemsSource = _view,
            };
            Children.Add(Grid);

            ((System.Collections.Specialized.INotifyCollectionChanged)_view.SortDescriptions).CollectionChanged +=
                (s, e) => UpdateSortHeaders();
        }

        /// <summary>true = uma linha por item, com "..." e tooltip; false = quebra o texto das colunas longas.</summary>
        public bool SingleLine
        {
            get => _singleLine;
            set
            {
                _singleLine = value;
                foreach (var kv in _columns) ((DataGridTextColumn)kv.Key).ElementStyle = CellStyle(kv.Value.Wrap);
            }
        }

        /// <summary>Caixa de seleção pronta para ligar/desligar o modo "uma linha por item".</summary>
        public CheckBox CreateSingleLineToggle()
        {
            var check = new CheckBox
            {
                Content = "Uma linha por item",
                ToolTip = "Textos longos terminam em \"...\"; passe o mouse para ver o conteúdo completo.",
                IsChecked = SingleLine,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 16, 0),
            };
            check.Click += (s, e) => SingleLine = check.IsChecked == true;
            return check;
        }

        public DataGridTextColumn AddColumn(string header, string property, double width, bool wrap = false, bool fill = false)
        {
            var col = new DataGridTextColumn
            {
                Header = HeaderContent(header, null, 0),
                Binding = new Binding(property),
                SortMemberPath = property,
                Width = fill ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(width),
                MinWidth = 40,
            };
            col.ElementStyle = CellStyle(wrap);
            _columns[col] = (header, wrap);
            Grid.Columns.Add(col);

            var box = new TextBox { ToolTip = $"Filtrar por {header} (contém o texto)" };
            box.TextChanged += (s, e) => Refresh();
            var botao = new Button
            {
                Content = "▾",
                Width = 18,
                Padding = new Thickness(0),
                Margin = new Thickness(1, 0, 0, 0),
                ToolTip = $"Escolher valores de {header}",
                Background = Brushes.White,
                BorderBrush = Theme.Solid(Theme.CinzaClaro),
                Focusable = false,
            };
            var celula = new DockPanel();
            DockPanel.SetDock(botao, Dock.Right);
            celula.Children.Add(botao);
            celula.Children.Add(box);
            celula.SetBinding(WidthProperty, new Binding(nameof(DataGridColumn.ActualWidth)) { Source = col });
            _filterRow.Children.Add(celula);
            var filtro = new FiltroColuna { Prop = _itemType.GetProperty(property), Box = box, Botao = botao, Titulo = header };
            botao.Click += (s, e) => AbrirValores(filtro);
            _filters.Add(filtro);
            return col;
        }

        /// <summary>Disparado depois de cada mudança de filtro.</summary>
        public event Action Filtered;

        /// <summary>Atualiza os cabeçalhos com seta (▲ crescente, ▼ decrescente) e a posição na ordenação.</summary>
        private void UpdateSortHeaders()
        {
            var ordem = _view.SortDescriptions.ToList();
            foreach (var kv in _columns)
            {
                DataGridColumn col = kv.Key;
                int i = ordem.FindIndex(d => d.PropertyName == col.SortMemberPath);
                col.Header = HeaderContent(kv.Value.Text, i >= 0 ? ordem[i].Direction : (System.ComponentModel.ListSortDirection?)null, i + 1);
            }
        }

        private static object HeaderContent(string text, System.ComponentModel.ListSortDirection? direction, int position)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock { Text = text });
            if (direction != null)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = (direction == System.ComponentModel.ListSortDirection.Ascending ? " ▲" : " ▼") + position,
                    Foreground = Theme.Solid(Theme.Laranja),
                    FontWeight = FontWeights.Bold,
                });
            }
            return panel;
        }

        private Style CellStyle(bool wrap)
        {
            var s = new Style(typeof(TextBlock));
            if (wrap && !_singleLine)
            {
                s.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
            }
            else
            {
                s.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.NoWrap));
                s.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
                // Texto completo no tooltip (o "..." indica que a célula tem mais conteúdo).
                s.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,
                    new Binding(nameof(TextBlock.Text)) { RelativeSource = RelativeSource.Self }));
                var vazio = new Trigger { Property = TextBlock.TextProperty, Value = "" };
                vazio.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, null));
                s.Triggers.Add(vazio);
            }
            return s;
        }

        public void Refresh()
        {
            _view.Refresh();
            Filtered?.Invoke();
        }

        private bool Pass(object item) => Pass(item, null);

        /// <summary>Passa em todos os filtros, menos (opcionalmente) o de uma coluna.</summary>
        private bool Pass(object item, FiltroColuna exceto)
        {
            if (ExtraFilter != null && !ExtraFilter(item)) return false;
            foreach (FiltroColuna fc in _filters)
            {
                if (fc == exceto || fc.Prop == null) continue;
                string v = fc.Prop.GetValue(item)?.ToString() ?? "";
                if (fc.Valores != null && !fc.Valores.Contains(v.Length == 0 ? Vazio : v)) return false;
                string f = fc.Box.Text.Trim();
                if (f.Length > 0 && v.IndexOf(f, StringComparison.CurrentCultureIgnoreCase) < 0) return false;
            }
            return true;
        }

        /// <summary>Filtros atuais por título de coluna, para reaplicar numa tabela nova (ex.: ao reanalisar).</summary>
        public Dictionary<string, (string Texto, HashSet<string> Valores)> Estado() =>
            _filters.GroupBy(f => f.Titulo).Select(g => g.First())
                    .ToDictionary(f => f.Titulo, f => (f.Box.Text, f.Valores == null ? null : new HashSet<string>(f.Valores)));

        public void Restaurar(Dictionary<string, (string Texto, HashSet<string> Valores)> estado)
        {
            if (estado == null) return;
            foreach (FiltroColuna fc in _filters)
            {
                if (!estado.TryGetValue(fc.Titulo, out var e)) continue;
                fc.Valores = e.Valores;
                fc.Box.Text = e.Texto ?? "";
                MarcarBotao(fc);
            }
            Refresh();
        }

        /// <summary>Limpa todos os filtros de coluna (texto e valores).</summary>
        public void LimparFiltros()
        {
            foreach (FiltroColuna fc in _filters)
            {
                fc.Valores = null;
                fc.Box.Text = "";
                MarcarBotao(fc);
            }
            Refresh();
        }

        private static void MarcarBotao(FiltroColuna fc)
        {
            bool ativo = fc.Valores != null;
            fc.Botao.Background = ativo ? Theme.Solid(Theme.LaranjaClaro) : Brushes.White;
            fc.Botao.FontWeight = ativo ? FontWeights.Bold : FontWeights.Normal;
        }

        /// <summary>Lista de valores distintos da coluna com caixas de marcar, como o filtro do Excel.</summary>
        private void AbrirValores(FiltroColuna fc)
        {
            if (fc.Prop == null) return;
            // Valores das linhas que passam nos outros filtros, com a contagem.
            var contagem = new Dictionary<string, int>();
            foreach (object item in _view.SourceCollection)
            {
                if (!Pass(item, fc)) continue;
                string v = fc.Prop.GetValue(item)?.ToString() ?? "";
                if (v.Length == 0) v = Vazio;
                contagem[v] = contagem.TryGetValue(v, out int n) ? n + 1 : 1;
            }
            var valores = contagem.Keys.OrderBy(v => v == Vazio ? 0 : 1).ThenBy(v => v, StringComparer.CurrentCultureIgnoreCase).ToList();

            var caixas = new List<CheckBox>();
            var lista = new StackPanel();
            foreach (string v in valores)
            {
                var c = new CheckBox
                {
                    Content = $"{v}   ({contagem[v]})",
                    Tag = v,
                    IsChecked = fc.Valores == null || fc.Valores.Contains(v),
                    Margin = new Thickness(0, 1, 0, 1),
                };
                caixas.Add(c);
                lista.Children.Add(c);
            }
            var tudo = new CheckBox { Content = "(Selecionar tudo)", IsChecked = caixas.All(c => c.IsChecked == true), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 4) };
            tudo.Click += (s, e) => { foreach (CheckBox c in caixas.Where(c => c.Visibility == Visibility.Visible)) c.IsChecked = tudo.IsChecked == true; };
            var busca = new TextBox { ToolTip = "Procurar valor" };
            busca.TextChanged += (s, e) =>
            {
                string t = busca.Text.Trim();
                foreach (CheckBox c in caixas)
                    c.Visibility = t.Length == 0 || ((string)c.Tag).IndexOf(t, StringComparison.CurrentCultureIgnoreCase) >= 0 ? Visibility.Visible : Visibility.Collapsed;
            };

            var popup = new Popup
            {
                PlacementTarget = fc.Botao,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
            };
            var ok = new Button { Content = "OK", Style = Theme.PrimaryButton, MinWidth = 60 };
            var limpar = new Button { Content = "Limpar filtro", MinWidth = 60 };
            ok.Click += (s, e) =>
            {
                var marcados = caixas.Where(c => c.IsChecked == true && c.Visibility == Visibility.Visible).Select(c => (string)c.Tag).ToList();
                fc.Valores = marcados.Count == valores.Count ? null : new HashSet<string>(marcados);
                MarcarBotao(fc);
                popup.IsOpen = false;
                Refresh();
            };
            limpar.Click += (s, e) =>
            {
                fc.Valores = null;
                MarcarBotao(fc);
                popup.IsOpen = false;
                Refresh();
            };

            var corpo = new DockPanel { Width = 280 };
            var titulo = new TextBlock { Text = fc.Titulo, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
            StackPanel botoes = Theme.Buttons(limpar, ok);
            botoes.Margin = new Thickness(0, 8, 0, 0);
            foreach (UIElement el in new UIElement[] { titulo, busca, tudo })
            {
                DockPanel.SetDock(el, Dock.Top);
                corpo.Children.Add(el);
            }
            DockPanel.SetDock(botoes, Dock.Bottom);
            corpo.Children.Add(botoes);
            corpo.Children.Add(new ScrollViewer { Content = lista, MaxHeight = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

            popup.Child = new Border
            {
                Background = Brushes.White,
                BorderBrush = Theme.Solid(Theme.Cinza),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10),
                Child = corpo,
            };
            popup.KeyDown += (s, e) => { if (e.Key == Key.Escape) popup.IsOpen = false; };
            popup.IsOpen = true;
            busca.Focus();
        }
    }
}
