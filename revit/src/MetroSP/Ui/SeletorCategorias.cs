using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace MetroSP.Ui
{
    /// <summary>
    /// Botão "Categorias: ..." que abre uma lista com caixas de marcar (com a quantidade de elementos),
    /// busca e "todas/nenhuma". Não depende da API do Revit: recebe nomes e contagens.
    /// </summary>
    public class SeletorCategorias : Button
    {
        private Dictionary<string, int> _contagem = new Dictionary<string, int>();
        /// <summary>Categorias marcadas; null = todas (inclusive as que aparecerem depois).</summary>
        private HashSet<string> _marcadas;

        /// <summary>Disparado quando a escolha muda.</summary>
        public event Action Mudou;

        public SeletorCategorias()
        {
            MinWidth = 200;
            HorizontalContentAlignment = HorizontalAlignment.Left;
            Padding = new Thickness(8, 3, 8, 3);
            Background = Brushes.White;
            ToolTip = "Escolha as categorias a considerar.";
            Click += (s, e) => Abrir();
            AtualizarTexto();
        }

        /// <summary>Todas as categorias disponíveis estão marcadas.</summary>
        public bool Todas => _marcadas == null || _contagem.Keys.All(_marcadas.Contains);

        /// <summary>A categoria entra no filtro?</summary>
        public bool Aceita(string categoria) => _marcadas == null || (categoria != null && _marcadas.Contains(categoria));

        public IEnumerable<string> Marcadas => _marcadas == null ? _contagem.Keys : _contagem.Keys.Where(_marcadas.Contains);

        /// <summary>Define as categorias disponíveis (nome → elementos), mantendo a escolha anterior quando possível.</summary>
        public void Definir(IDictionary<string, int> contagem)
        {
            _contagem = new Dictionary<string, int>(contagem);
            AtualizarTexto();
        }

        private void AtualizarTexto()
        {
            int total = _contagem.Count;
            int marcadas = Marcadas.Count();
            string texto = total == 0 ? "Categorias" : Todas ? $"Categorias: todas ({total})"
                : marcadas == 0 ? "Categorias: nenhuma"
                : marcadas <= 2 ? "Categorias: " + string.Join(", ", Marcadas)
                : $"Categorias: {marcadas} de {total}";
            Content = texto + "  ▾";
            FontWeight = Todas ? FontWeights.Normal : FontWeights.SemiBold;
            BorderBrush = Theme.Solid(Todas ? Theme.CinzaClaro : Theme.Laranja);
        }

        private void Abrir()
        {
            var caixas = new List<CheckBox>();
            var lista = new StackPanel();
            foreach (var kv in _contagem.OrderBy(k => k.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                var c = new CheckBox { Content = $"{kv.Key}   ({kv.Value})", Tag = kv.Key, IsChecked = Aceita(kv.Key), Margin = new Thickness(0, 1, 0, 1) };
                caixas.Add(c);
                lista.Children.Add(c);
            }
            if (caixas.Count == 0) lista.Children.Add(new TextBlock { Text = "Nenhuma categoria no escopo.", Foreground = Theme.Solid(Theme.Cinza) });

            var busca = new TextBox { ToolTip = "Procurar categoria" };
            busca.TextChanged += (s, e) =>
            {
                string t = busca.Text.Trim();
                foreach (CheckBox c in caixas)
                    c.Visibility = t.Length == 0 || ((string)c.Tag).IndexOf(t, StringComparison.CurrentCultureIgnoreCase) >= 0 ? Visibility.Visible : Visibility.Collapsed;
            };
            var todas = new Button { Content = "Todas", Padding = new Thickness(8, 1, 8, 1) };
            var nenhuma = new Button { Content = "Nenhuma", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(6, 0, 0, 0) };
            todas.Click += (s, e) => { foreach (CheckBox c in caixas.Where(c => c.Visibility == Visibility.Visible)) c.IsChecked = true; };
            nenhuma.Click += (s, e) => { foreach (CheckBox c in caixas.Where(c => c.Visibility == Visibility.Visible)) c.IsChecked = false; };
            var atalhos = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
            atalhos.Children.Add(todas);
            atalhos.Children.Add(nenhuma);

            var popup = new Popup { PlacementTarget = this, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
            var ok = new Button { Content = "OK", Style = Theme.PrimaryButton, MinWidth = 60 };
            ok.Click += (s, e) =>
            {
                var m = caixas.Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToList();
                _marcadas = m.Count == caixas.Count ? null : new HashSet<string>(m);
                popup.IsOpen = false;
                AtualizarTexto();
                Mudou?.Invoke();
            };
            StackPanel botoes = Theme.Buttons(ok);
            botoes.Margin = new Thickness(0, 8, 0, 0);

            var corpo = new DockPanel { Width = 280 };
            foreach (UIElement el in new UIElement[] { busca, atalhos })
            {
                DockPanel.SetDock(el, Dock.Top);
                corpo.Children.Add(el);
            }
            DockPanel.SetDock(botoes, Dock.Bottom);
            corpo.Children.Add(botoes);
            corpo.Children.Add(new ScrollViewer { Content = lista, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            popup.Child = new Border { Background = Brushes.White, BorderBrush = Theme.Solid(Theme.Cinza), BorderThickness = new Thickness(1), Padding = new Thickness(10), Child = corpo };
            popup.KeyDown += (s, e) => { if (e.Key == Key.Escape) popup.IsOpen = false; };
            popup.IsOpen = true;
            busca.Focus();
        }
    }
}
