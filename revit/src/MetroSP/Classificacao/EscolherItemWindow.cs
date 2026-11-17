using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MetroSP.Ui;

namespace MetroSP.Classificacao
{
    /// <summary>Escolhe um item da lista de classificação (busca por código ou título), com a sugestão já marcada.</summary>
    public class EscolherItemWindow : Window
    {
        private readonly ClassificationList _lista;
        private readonly string _categoria;
        private readonly TextBox _busca;
        private readonly ListBox _itens;
        private readonly Button _ok;

        public ClassItem Escolhido { get; private set; }

        private readonly ClassItem _sugerido;

        /// <param name="sugerido">Item já marcado ao abrir (a proposta da verificação); o usuário pode escolher outro.</param>
        public EscolherItemWindow(ClassificationList lista, string sistema, string categoria, string elemento, string preenchido, ClassItem sugerido = null)
        {
            _lista = lista;
            _categoria = categoria;
            _sugerido = sugerido;
            Title = "MetroSP - Escolher classificação";
            Width = 680;
            Height = 580;
            MinWidth = 420;
            MinHeight = 320;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _busca = new TextBox { Margin = new Thickness(0, 0, 0, 6), ToolTip = "Digite parte do código ou do título (sem diferenciar acentos)." };
            _busca.TextChanged += (s, e) => Filtrar();
            _itens = new ListBox { DisplayMemberPath = null };
            VirtualizingPanel.SetIsVirtualizing(_itens, true);
            _itens.SelectionChanged += (s, e) => _ok.IsEnabled = _itens.SelectedItem is ClassItem;
            _itens.MouseDoubleClick += (s, e) => { if (_itens.SelectedItem is ClassItem) Confirmar(); };

            var corpo = new DockPanel();
            var busca = new DockPanel();
            var rotulo = Theme.Rotulo("Buscar");
            DockPanel.SetDock(rotulo, Dock.Left);
            busca.Children.Add(rotulo);
            busca.Children.Add(_busca);
            DockPanel.SetDock(busca, Dock.Top);
            corpo.Children.Add(busca);
            if (sugerido != null)
            {
                var dica = new TextBlock
                {
                    Text = $"Sugestão marcada: {sugerido.Codigo}  {sugerido.Descricao}. Escolha outro item se preferir.",
                    Foreground = Theme.Solid(Theme.Cinza),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 6),
                };
                DockPanel.SetDock(dica, Dock.Top);
                corpo.Children.Add(dica);
            }
            corpo.Children.Add(_itens);

            _ok = new Button { Content = "Sobrescrever", Style = Theme.PrimaryButton, IsEnabled = false, IsDefault = true, ToolTip = "Grava o item escolhido no lugar do valor atual (Ctrl+Z desfaz)." };
            _ok.Click += (s, e) => Confirmar();
            var cancelar = new Button { Content = "Cancelar", IsCancel = true };
            Theme.Apply(this, "Escolher classificação", $"{sistema} · {elemento}" + (string.IsNullOrWhiteSpace(preenchido) ? "" : $" · hoje: {preenchido}"),
                corpo, Theme.Buttons(_ok, cancelar));
            Loaded += (s, e) => { Filtrar(); _busca.Focus(); };
        }

        private void Filtrar()
        {
            string t = ClassificationCore.Normalizar(_busca.Text);
            var itens = _lista.Itens
                .Where(i => t.Length == 0 || ClassificationCore.Normalizar(i.Codigo + " " + i.Descricao).Contains(t))
                .ToList();
            _itens.ItemsSource = itens;
            if (itens.Count == 1) _itens.SelectedIndex = 0;
            else if (_sugerido != null && itens.Contains(_sugerido))
            {
                _itens.SelectedItem = _sugerido;
                _itens.ScrollIntoView(_sugerido);
            }
        }

        private void Confirmar()
        {
            Escolhido = _itens.SelectedItem as ClassItem;
            if (Escolhido == null) return;
            DialogResult = true;
        }
    }
}
