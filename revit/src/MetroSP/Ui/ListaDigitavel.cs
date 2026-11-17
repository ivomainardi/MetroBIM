using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace MetroSP.Ui
{
    /// <summary>
    /// Lista suspensa em que também dá para digitar: o que se digita filtra a lista (sem diferenciar acento nem maiúscula).
    /// Só aceita um item da lista: Enter, sair do campo ou escolher na lista confirma; se o texto não for de nenhum item
    /// (nem filtrar para um só), volta ao último escolhido.
    /// </summary>
    public static class ListaDigitavel
    {
        public static ComboBox Criar(IList<string> itens, string atual, Action<string> escolhido, double largura = 220, string dica = null)
        {
            List<string> lista = itens?.Where(i => i != null).ToList() ?? new List<string>();
            var view = new ListCollectionView(lista);
            var cb = new ComboBox
            {
                IsEditable = true,
                IsTextSearchEnabled = false,
                StaysOpenOnEdit = true,
                Width = largura,
                Padding = new Thickness(4, 3, 4, 3),
                ItemsSource = view,
                ToolTip = dica,
            };
            string confirmado = lista.FirstOrDefault(i => Igual(i, atual)) ?? (lista.Count > 0 ? lista[0] : "");
            cb.Text = confirmado;
            bool mudando = false;

            void Mostrar(string texto)
            {
                mudando = true;
                view.Filter = null;
                cb.Text = texto;
                mudando = false;
            }

            void Confirmar()
            {
                string t = (cb.Text ?? "").Trim();
                string achado = lista.FirstOrDefault(i => Igual(i, t));
                if (achado == null)
                {
                    List<string> filtrados = lista.Where(i => Contem(i, t)).ToList();
                    if (filtrados.Count == 1) achado = filtrados[0];
                }
                if (achado == null) { Mostrar(confirmado); return; }
                Mostrar(achado);
                if (achado == confirmado) return;
                confirmado = achado;
                escolhido?.Invoke(achado);
            }

            cb.Loaded += (s, e) =>
            {
                if (!(cb.Template?.FindName("PART_EditableTextBox", cb) is TextBox caixa)) return;
                caixa.TextChanged += (s2, e2) =>
                {
                    if (mudando || !caixa.IsKeyboardFocusWithin) return;
                    string t = caixa.Text;
                    int cursor = caixa.CaretIndex;
                    mudando = true;
                    view.Filter = o => Contem(o as string, t);
                    if (!cb.IsDropDownOpen) cb.IsDropDownOpen = true;
                    // Filtrar pode trocar o texto pelo item selecionado: devolve o que foi digitado.
                    if (caixa.Text != t) { caixa.Text = t; caixa.CaretIndex = Math.Min(cursor, t.Length); }
                    mudando = false;
                };
            };
            cb.DropDownClosed += (s, e) => { if (!mudando && cb.SelectedItem is string sel) { mudando = true; cb.Text = sel; mudando = false; Confirmar(); } };
            cb.LostKeyboardFocus += (s, e) => { if (!cb.IsKeyboardFocusWithin && !cb.IsDropDownOpen) Confirmar(); };
            cb.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { cb.IsDropDownOpen = false; Confirmar(); e.Handled = true; }
                else if (e.Key == Key.Escape) { cb.IsDropDownOpen = false; Mostrar(confirmado); e.Handled = true; }
            };
            return cb;
        }

        private static bool Igual(string a, string b) =>
            a != null && b != null && string.Equals(SemAcento(a), SemAcento(b), StringComparison.OrdinalIgnoreCase);

        private static bool Contem(string item, string texto) =>
            item != null && (string.IsNullOrWhiteSpace(texto) || SemAcento(item).IndexOf(SemAcento(texto.Trim()), StringComparison.OrdinalIgnoreCase) >= 0);

        private static string SemAcento(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s.Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
