using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace MetroSP.Ui
{
    /// <summary>
    /// Caixa de escolha com todos os itens de uma lista (ex.: as classes do esquema IFC). Digitar só filtra (parte do
    /// nome, sem diferenciar maiúsculas); o valor escolhido é sempre um item da lista. O mesmo padrão do Civil 3D.
    /// </summary>
    public static class SeletorLista
    {
        /// <summary>Caixa solta (fora de tabela). O valor escolhido fica em SelectedItem (string) ou null.</summary>
        public static ComboBox Criar(IEnumerable<string> itens, double largura = 280)
        {
            var box = new ComboBox { Width = largura, Padding = new Thickness(4, 2, 4, 2), VerticalAlignment = VerticalAlignment.Center };
            Configurar(box, itens.ToList(), null);
            return box;
        }

        /// <summary>Põe a lista na caixa e liga o filtro. Ao escolher um item, chama escolheu (se houver).</summary>
        public static void Configurar(ComboBox box, IList<string> itens, Action<string> escolheu)
        {
            var vista = new ListCollectionView(itens.ToList());
            box.IsEditable = true;
            box.IsTextSearchEnabled = false;
            box.StaysOpenOnEdit = true;
            box.MaxDropDownHeight = 360;
            box.ItemsSource = vista;
            VirtualizingPanel.SetIsVirtualizing(box, true);
            box.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));

            string ultima = box.SelectedItem as string;
            TextBox texto = null;
            void LigarTexto()
            {
                if (texto != null) return;
                box.ApplyTemplate();
                texto = box.Template?.FindName("PART_EditableTextBox", box) as TextBox;
                if (texto == null) return;
                texto.TextChanged += (s, e) =>
                {
                    if (box.SelectedItem is string sel && sel == texto.Text) return;
                    string f = texto.Text.Trim();
                    vista.Filter = f.Length == 0 ? null : (Predicate<object>)(o => ((string)o).IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!box.IsDropDownOpen && box.IsKeyboardFocusWithin) box.IsDropDownOpen = true;
                    texto.SelectionStart = texto.Text.Length;
                };
            }
            if (box.IsLoaded) LigarTexto(); else box.Loaded += (s, e) => LigarTexto();

            box.SelectionChanged += (s, e) =>
            {
                if (!(box.SelectedItem is string sel)) return;
                ultima = sel;
                escolheu?.Invoke(sel);
            };
            box.LostKeyboardFocus += (s, e) =>
            {
                if (box.IsKeyboardFocusWithin) return;
                Fixar(box, itens, vista, ref ultima);
            };
            box.DropDownClosed += (s, e) => { if (!box.IsKeyboardFocusWithin) Fixar(box, itens, vista, ref ultima); };
        }

        /// <summary>Fica com o item igual ao texto; se o texto não é um item, volta ao último escolhido.</summary>
        private static void Fixar(ComboBox box, IList<string> itens, ListCollectionView vista, ref string ultima)
        {
            string t = box.Text?.Trim();
            string exata = itens.FirstOrDefault(i => i.Equals(t, StringComparison.OrdinalIgnoreCase));
            vista.Filter = null;
            if (exata != null) box.SelectedItem = exata;
            else { box.SelectedItem = ultima; box.Text = ultima ?? ""; }
        }

        /// <summary>
        /// Coluna de tabela com a caixa de escolha. O campo (string) do item da linha só recebe itens da lista;
        /// vazio aparece como o texto de "vazio" (null: sem opção vazia). Com "invalido" (nome de uma propriedade
        /// bool do item), o valor fora da lista aparece em vermelho.
        /// </summary>
        public static DataGridTemplateColumn Coluna(string titulo, string campo, Func<object, IList<string>> itens, string vazio,
            string invalido, string dicaInvalido, DataGridLength largura, double minimo = 120, Action<object> alterou = null)
        {
            // Valor inválido: célula com fundo vermelho claro e texto em vermelho, para achar de longe.
            var fundo = new FrameworkElementFactory(typeof(Border), "fundo");
            fundo.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            var leitura = new FrameworkElementFactory(typeof(TextBlock), "texto");
            leitura.SetBinding(TextBlock.TextProperty, new Binding(campo));
            leitura.SetValue(TextBlock.PaddingProperty, new Thickness(3, 0, 2, 0));
            leitura.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            fundo.AppendChild(leitura);
            var dataTemplateLeitura = new DataTemplate { VisualTree = fundo };
            if (invalido != null)
            {
                var gatilho = new DataTrigger { Binding = new Binding(invalido), Value = true };
                gatilho.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xF8, 0xD4, 0xD4)), "fundo"));
                gatilho.Setters.Add(new Setter(TextBlock.ForegroundProperty, Brushes.Firebrick, "texto"));
                gatilho.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold, "texto"));
                gatilho.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, dicaInvalido, "fundo"));
                dataTemplateLeitura.Triggers.Add(gatilho);
            }

            var edicao = new FrameworkElementFactory(typeof(ComboBox));
            edicao.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler((s, e) =>
            {
                var box = (ComboBox)s;
                if (box.Tag as string == "ok") return;
                box.Tag = "ok";
                object linha = box.DataContext;
                PropertyInfo prop = linha?.GetType().GetProperty(campo);
                if (prop == null) return;
                var lista = new List<string>();
                if (vazio != null) lista.Add(vazio);
                lista.AddRange(itens(linha) ?? Array.Empty<string>());
                string atual = prop.GetValue(linha) as string;
                string inicial = string.IsNullOrWhiteSpace(atual) ? vazio : lista.FirstOrDefault(i => i.Equals(atual.Trim(), StringComparison.OrdinalIgnoreCase));
                box.SelectedItem = null;
                Configurar(box, lista, sel =>
                {
                    string valor = sel == vazio ? "" : sel;
                    if (string.Equals(prop.GetValue(linha) as string ?? "", valor, StringComparison.Ordinal)) return;
                    prop.SetValue(linha, valor);
                    alterou?.Invoke(linha);
                });
                if (inicial != null) box.SelectedItem = inicial;
                else box.Text = atual ?? "";
                box.Focus();
                box.IsDropDownOpen = true;
            }));
            edicao.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler((s, e) =>
            {
                // Enter com o filtro mostrando um item só: escolhe esse item.
                var box = (ComboBox)s;
                if (e.Key != Key.Enter || box.SelectedItem != null) return;
                var vista = box.ItemsSource as ListCollectionView;
                if (vista != null && vista.Count == 1) box.SelectedItem = vista.GetItemAt(0);
            }));

            return new DataGridTemplateColumn
            {
                Header = titulo, Width = largura, MinWidth = minimo,
                CellTemplate = dataTemplateLeitura,
                CellEditingTemplate = new DataTemplate { VisualTree = edicao },
            };
        }
    }
}
