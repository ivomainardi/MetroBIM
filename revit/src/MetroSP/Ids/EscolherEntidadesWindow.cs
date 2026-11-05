using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MetroSP.Ui;

namespace MetroSP.Ids
{
    /// <summary>
    /// Escolha das entidades IFC de um property set: todas as do IFC4 e do IFC 4.3 (inclui abstratas como IfcElement e
    /// os tipos, como IfcWallType), marcadas numa lista com busca. Nada é digitado, então não sai entidade errada.
    /// </summary>
    public class EscolherEntidadesWindow : Window
    {
        private class Item
        {
            public string Nome { get; set; }
            public string Onde { get; set; }
            public bool Marcado { get; set; }
            public bool Fora { get; set; }
        }

        private readonly List<Item> _itens;
        private readonly ListCollectionView _vista;
        private readonly TextBlock _resumo;
        private readonly CheckBox _soMarcadas;
        private readonly TextBox _busca;

        /// <summary>As entidades escolhidas, separadas por vírgula (como no .txt).</summary>
        public string Resultado { get; private set; }

        public EscolherEntidadesWindow(string atuais, string propertySet)
        {
            Title = "MetroSP - Entidades IFC";
            Width = 560;
            Height = 620;
            MinWidth = 420;
            MinHeight = 360;
            ResizeMode = ResizeMode.CanResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var marcadas = (atuais ?? "").Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(e => e.Trim()).ToList();
            var noEsquema = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _itens = EsquemaIfc.Entidades().Select(e => new Item { Nome = e, Onde = Onde(e) }).ToList();
            foreach (Item i in _itens) noEsquema.Add(i.Nome);
            foreach (string m in marcadas)
            {
                Item i = _itens.FirstOrDefault(x => x.Nome.Equals(m, StringComparison.OrdinalIgnoreCase));
                if (i != null) i.Marcado = true;
                // A entidade que veio do .txt e não existe nos esquemas aparece marcada, em vermelho, para desmarcar.
                else if (!_itens.Any(x => x.Fora && x.Nome.Equals(m, StringComparison.OrdinalIgnoreCase)))
                    _itens.Insert(0, new Item { Nome = m, Onde = "não existe no IFC4 nem no IFC 4.3", Marcado = true, Fora = true });
            }
            _vista = new ListCollectionView(_itens) { Filter = o => Passa((Item)o) };

            var corpo = new DockPanel();
            var topo = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(topo, Dock.Top);
            _soMarcadas = new CheckBox { Content = "Só as marcadas", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), IsChecked = marcadas.Count > 0 };
            _soMarcadas.Click += (s, e) => _vista.Refresh();
            DockPanel.SetDock(_soMarcadas, Dock.Right);
            topo.Children.Add(_soMarcadas);
            _busca = new TextBox { ToolTip = "Digite parte do nome para filtrar (ex.: Wall, Element, Type)" };
            _busca.TextChanged += (s, e) =>
            {
                if (_busca.Text.Trim().Length > 0 && _soMarcadas.IsChecked == true) _soMarcadas.IsChecked = false;
                _vista.Refresh();
            };
            topo.Children.Add(_busca);
            corpo.Children.Add(topo);

            var lista = new ListBox { ItemsSource = _vista, BorderBrush = Theme.Solid(Theme.CinzaClaro) };
            VirtualizingPanel.SetIsVirtualizing(lista, true);
            var linha = new FrameworkElementFactory(typeof(DockPanel));
            var marca = new FrameworkElementFactory(typeof(CheckBox));
            marca.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(nameof(Item.Marcado)) { Mode = BindingMode.TwoWay });
            marca.SetBinding(ContentControl.ContentProperty, new Binding(nameof(Item.Nome)));
            marca.SetValue(FrameworkElement.MinWidthProperty, 260.0);
            marca.AddHandler(System.Windows.Controls.Primitives.ToggleButton.ClickEvent, new RoutedEventHandler((s, e) => Contar()));
            linha.AppendChild(marca);
            var onde = new FrameworkElementFactory(typeof(TextBlock));
            onde.SetBinding(TextBlock.TextProperty, new Binding(nameof(Item.Onde)));
            onde.SetValue(TextBlock.ForegroundProperty, Theme.Solid(Theme.Cinza));
            onde.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));
            linha.AppendChild(onde);
            var modelo = new DataTemplate { VisualTree = linha };
            var fora = new DataTrigger { Binding = new Binding(nameof(Item.Fora)), Value = true };
            fora.Setters.Add(new Setter(Control.ForegroundProperty, System.Windows.Media.Brushes.Firebrick, null));
            modelo.Triggers.Add(fora);
            lista.ItemTemplate = modelo;
            corpo.Children.Add(lista);

            _resumo = new TextBlock();
            var limpar = new Button { Content = "Desmarcar todas" };
            limpar.Click += (s, e) => { foreach (Item i in _itens) i.Marcado = false; _vista.Refresh(); Contar(); };
            var ok = new Button { Content = "OK", Style = Theme.PrimaryButton, IsDefault = true };
            ok.Click += (s, e) => Confirmar();
            var cancelar = new Button { Content = "Cancelar", IsCancel = true };

            Theme.Apply(this, "Entidades IFC", string.IsNullOrWhiteSpace(propertySet) ? "Em que entidades o property set é exportado"
                : $"Em que entidades o {propertySet} é exportado", corpo, Theme.Rodape(_resumo, limpar, cancelar, ok));
            Contar();
            Loaded += (s, e) => _busca.Focus();
        }

        private static string Onde(string entidade)
        {
            string o = EsquemaIfc.OndeExiste(entidade);
            return o == "IFC4 e IFC 4.3" ? "" : o;
        }

        private bool Passa(Item i)
        {
            if (_soMarcadas?.IsChecked == true && !i.Marcado) return false;
            string b = _busca?.Text.Trim() ?? "";
            return b.Length == 0 || i.Nome.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void Contar()
        {
            int n = _itens.Count(i => i.Marcado);
            int fora = _itens.Count(i => i.Marcado && i.Fora);
            _resumo.Text = fora > 0 ? $"{n} marcadas; {fora} fora dos esquemas (desmarque)" : $"{n} marcadas";
            _resumo.Foreground = Theme.Solid(fora > 0 ? Theme.LaranjaTexto : Theme.Cinza);
        }

        private void Confirmar()
        {
            List<Item> marcadas = _itens.Where(i => i.Marcado).ToList();
            if (marcadas.Count == 0)
            {
                MessageBox.Show(this, "Marque ao menos uma entidade (ex.: IfcWall ou IfcElement).", Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (marcadas.Any(i => i.Fora))
            {
                MessageBox.Show(this, "Desmarque as entidades em vermelho: elas não existem no IFC4 nem no IFC 4.3.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Resultado = string.Join(",", marcadas.Select(i => i.Nome));
            DialogResult = true;
        }
    }
}
