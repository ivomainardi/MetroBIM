using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using MetroSP.Comum;

namespace MetroSP.Ui
{
    /// <summary>
    /// Janela "Versão nova do MetroSP", igual no Revit e no Civil 3D (mesmo Theme da identidade MetroSP).
    /// Este arquivo fica em comum/ e entra nos dois projetos como link.
    /// </summary>
    public class JanelaVersaoNova : Window
    {
        /// <summary>
        /// Chamar uma vez ao abrir o software, na thread da interface: procura em segundo plano e, se houver versão nova,
        /// mostra a janela. Sem internet, sem instalador ou se já consultou hoje, não faz nada.
        /// </summary>
        public static void ProcurarAoAbrir(string software)
        {
            Dispatcher interfaceGrafica = Dispatcher.CurrentDispatcher;
            Task.Run(async () =>
            {
                var n = await VersaoNova.ProcurarAsync().ConfigureAwait(false);
                if (!VersaoNova.TemNova(n)) return;
                // Espera o software terminar de abrir (prioridade de fundo) para não atrapalhar o início.
                interfaceGrafica.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    try { Mostrar(n, software); } catch { }
                }));
            });
        }

        /// <summary>Pelo Sobre: procura agora e diz o resultado, mesmo quando já está na última versão.</summary>
        public static async void ProcurarAgora(string software, Window dono)
        {
            VersaoNova.Novidade n;
            try { n = await VersaoNova.ProcurarAsync(manual: true); }
            catch (Exception ex)
            {
                MessageBox.Show(dono, "Não foi possível consultar a última versão.\n\n" + ex.Message +
                    "\n\nConfira a internet ou abra a página das versões:\n" + VersaoNova.PaginaReleases,
                    "MetroSP", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (VersaoNova.TemNova(n)) { Mostrar(n, software, dono); return; }
            string texto = n?.Instalada == null
                ? $"A última versão é a {n?.Nova}. Esta cópia não foi instalada pelo instalador do MetroSP, então não dá para comparar."
                : $"Você já está na última versão ({n.Instalada}).";
            MessageBox.Show(dono, texto, "MetroSP", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public static void Mostrar(VersaoNova.Novidade n, string software, Window dono = null)
        {
            var w = new JanelaVersaoNova(n, software);
            if (dono != null) { w.Owner = dono; w.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            else
            {
                try { new WindowInteropHelper(w).Owner = Process.GetCurrentProcess().MainWindowHandle; } catch { }
            }
            w.ShowDialog();
        }

        private JanelaVersaoNova(VersaoNova.Novidade n, string software)
        {
            Title = "MetroSP - Versão nova";
            Width = 560;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var corpo = new StackPanel();
            var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            chips.Children.Add(Theme.Chip("Nova: " + n.Nova, escuro: true));
            if (!string.IsNullOrEmpty(n.Instalada)) chips.Children.Add(Theme.Chip("Instalada: " + n.Instalada));
            corpo.Children.Add(chips);

            if (!string.IsNullOrEmpty(n.Notas))
            {
                corpo.Children.Add(Theme.Secao("O que mudou"));
                corpo.Children.Add(new TextBox
                {
                    Text = n.Notas,
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    MaxHeight = 280,
                    BorderThickness = new Thickness(0),
                    Background = System.Windows.Media.Brushes.Transparent,
                });
            }

            var pagina = Theme.Link("Ver a página da versão");
            pagina.ToolTip = "Abre no navegador a página da versão, com as notas completas e o instalador.";
            pagina.Click += (s, e) => VersaoNova.Abrir(n.Pagina);
            pagina.Margin = new Thickness(0, 8, 0, 0);
            pagina.HorizontalAlignment = HorizontalAlignment.Left;
            corpo.Children.Add(pagina);

            var ignorar = new Button { Content = "Não avisar desta" };
            ignorar.ToolTip = $"Não mostra mais o aviso da versão {n.Nova}. Volta a avisar quando sair uma mais nova.";
            ignorar.Click += (s, e) => { VersaoNova.Ignorar(n.Nova); Close(); };

            var depois = new Button { Content = "Agora não", IsCancel = true };
            depois.ToolTip = "Fecha o aviso. Ele volta amanhã, ao abrir o software.";
            depois.Click += (s, e) => Close();

            var baixar = new Button { Content = "Baixar instalador", IsDefault = true, Style = Theme.PrimaryButton };
            baixar.ToolTip = $"Baixa o MetroSP-Setup-{n.Nova}.exe pelo navegador. Feche o {software} antes de rodar o instalador; " +
                             "ele atualiza todos os plugins MetroSP do computador, sem precisar de administrador.";
            baixar.Click += (s, e) => { VersaoNova.Abrir(n.Instalador); Close(); };

            Theme.Apply(this, "Versão nova do MetroSP", $"A versão {n.Nova} já pode ser instalada", corpo, Theme.Buttons(ignorar, depois, baixar));
        }
    }
}
