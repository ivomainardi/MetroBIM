using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace MetroSP.Ui
{
    /// <summary>
    /// Junta janelas que já existiam numa ferramenta só, para o projetista: cada janela mostra também as abas das outras.
    /// Clicar numa aba de outra janela fecha esta e abre a outra no mesmo lugar e no mesmo tamanho, já na aba clicada.
    /// As janelas continuam modais e abertas pelo comando, então as transações seguem no contexto da API.
    /// </summary>
    internal static class AbasEntreJanelas
    {
        /// <summary>Aba pedida por cada janela ao ser fechada por uma aba de outra janela.</summary>
        private static readonly Dictionary<Window, string> Pedidos = new Dictionary<Window, string>();

        /// <summary>Acrescenta numa TabControl uma aba que leva a outra janela (<paramref name="destino"/> é a chave usada em <see cref="Rodar"/>).</summary>
        public static void AdicionarAba(TabControl abas, string titulo, string destino, string dica = null, int posicao = -1)
        {
            var aba = new TabItem
            {
                Header = titulo,
                ToolTip = dica,
                Content = new TextBlock { Text = "Abrindo...", Margin = new Thickness(12), Foreground = Theme.Solid(Theme.Cinza) },
            };
            if (posicao < 0 || posicao > abas.Items.Count) abas.Items.Add(aba);
            else abas.Items.Insert(posicao, aba);

            abas.SelectionChanged += (s, e) =>
            {
                // SelectionChanged também sobe das tabelas e listas de dentro das abas: só vale a da própria TabControl.
                if (!ReferenceEquals(e.OriginalSource, abas) || !ReferenceEquals(abas.SelectedItem, aba)) return;
                Window w = Window.GetWindow(abas);
                if (w == null || !w.IsLoaded) return;
                Pedidos[w] = destino;
                w.Dispatcher.BeginInvoke(new Action(w.Close));
            };
        }

        /// <summary>
        /// Abre a janela de <paramref name="inicial"/> e, enquanto o projetista trocar de aba para outra janela, abre a seguinte
        /// no mesmo lugar. <paramref name="criar"/> devolve a janela de cada chave, já na aba certa (null encerra).
        /// </summary>
        public static void Rodar(string inicial, Func<string, Window> criar, IntPtr dono)
        {
            string atual = inicial;
            Rect? lugar = null;
            bool maximizada = false;
            while (atual != null)
            {
                Window w = criar(atual);
                if (w == null) return;
                if (dono != IntPtr.Zero) new WindowInteropHelper(w).Owner = dono;
                if (lugar.HasValue)
                {
                    Rect r = lugar.Value;
                    w.WindowStartupLocation = WindowStartupLocation.Manual;
                    w.Left = r.Left;
                    w.Top = r.Top;
                    w.Width = Math.Max(r.Width, w.MinWidth);
                    w.Height = Math.Max(r.Height, w.MinHeight);
                    if (maximizada) w.WindowState = WindowState.Maximized;
                }

                Rect fim = Rect.Empty;
                bool fimMaximizada = false;
                w.Closing += (s, e) =>
                {
                    fimMaximizada = w.WindowState == WindowState.Maximized;
                    fim = w.WindowState == WindowState.Normal ? new Rect(w.Left, w.Top, w.ActualWidth, w.ActualHeight) : w.RestoreBounds;
                };
                w.ShowDialog();

                atual = Pedidos.TryGetValue(w, out string pedido) ? pedido : null;
                Pedidos.Remove(w);
                if (!fim.IsEmpty && fim.Width > 0 && fim.Height > 0) lugar = fim;
                maximizada = fimMaximizada;
            }
        }
    }
}
