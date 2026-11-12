using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MetroSP.Planilha;
using MetroSP.Ui;

namespace MetroSP.Classificacao
{
    /// <summary>Escolhe um item da lista de classificação: busca por código ou descrição, só os da categoria por padrão.</summary>
    public class EscolherItemWindow : Window
    {
        private readonly ClassificationList _lista;
        private readonly string _tipo, _layer;
        private readonly TextBox _busca;
        private readonly CheckBox _soCategoria;
        private readonly ListBox _itens;
        private readonly Button _ok;

        public ClassItem Escolhido { get; private set; }

        public EscolherItemWindow(ClassificationList lista, string sistema, string tipo, string layer, string elemento, string preenchido)
        {
            _lista = lista;
            _tipo = tipo;
            _layer = layer;
            Title = "MetroSP - Escolher classificação";
            Width = 640;
            Height = 560;
            MinWidth = 420;
            MinHeight = 320;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _busca = new TextBox { Margin = new Thickness(0, 0, 0, 6), ToolTip = "Digite parte do código ou da descrição." };
            _busca.TextChanged += (s, e) => Filtrar();
            _soCategoria = new CheckBox
            {
                Content = $"Só os previstos para {tipo} / layer {layer}",
                IsChecked = lista.Itens.Any(i => i.Categorias.Count > 0),
                IsEnabled = lista.Itens.Any(i => i.Categorias.Count > 0),
                Margin = new Thickness(0, 0, 0, 6),
                ToolTip = "Itens cuja coluna de tipos ou layers da planilha inclui este objeto (ou está vazia).",
            };
            _soCategoria.Click += (s, e) => Filtrar();
            _itens = new ListBox { DisplayMemberPath = null };
            _itens.SelectionChanged += (s, e) => _ok.IsEnabled = _itens.SelectedItem is ClassItem;
            _itens.MouseDoubleClick += (s, e) => { if (_itens.SelectedItem is ClassItem) Confirmar(); };

            var corpo = new DockPanel();
            var busca = new DockPanel();
            var rotulo = Theme.Rotulo("Buscar");
            DockPanel.SetDock(rotulo, Dock.Left);
            busca.Children.Add(rotulo);
            busca.Children.Add(_busca);
            foreach (UIElement el in new UIElement[] { busca, _soCategoria })
            {
                DockPanel.SetDock(el, Dock.Top);
                corpo.Children.Add(el);
            }
            corpo.Children.Add(_itens);

            _ok = new Button { Content = "Gravar", Style = Theme.PrimaryButton, IsEnabled = false, IsDefault = true };
            _ok.Click += (s, e) => Confirmar();
            var cancelar = new Button { Content = "Cancelar", IsCancel = true };
            Theme.Apply(this, "Escolher classificação", $"{sistema} · {elemento}" + (string.IsNullOrWhiteSpace(preenchido) ? "" : $" · hoje: {preenchido}"),
                corpo, Theme.Buttons(_ok, cancelar));
            Loaded += (s, e) => { Filtrar(); _busca.Focus(); };
        }

        private void Filtrar()
        {
            string t = MapaColunas.Normalizar(_busca.Text);
            bool soCat = _soCategoria.IsChecked == true;
            var itens = _lista.Itens
                .Where(i => !soCat || i.ValePara(_tipo, _layer))
                .Where(i => t.Length == 0 || MapaColunas.Normalizar(i.Codigo + " " + i.Descricao).Contains(t))
                .Take(2000)
                .ToList();
            _itens.ItemsSource = itens;
            if (itens.Count == 1) _itens.SelectedIndex = 0;
        }

        private void Confirmar()
        {
            Escolhido = _itens.SelectedItem as ClassItem;
            if (Escolhido == null) return;
            DialogResult = true;
        }
    }
}
