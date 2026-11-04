using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace MetroSP.Ui
{
    /// <summary>Estado da IFC 4.3 Extension da Autodesk e atalho para as atualizações na conta Autodesk.</summary>
    public static class ExtensionWindow
    {
        /// <param name="blocking">true quando aberta porque a exportação não pôde começar.</param>
        public static void Show(Settings cfg, string command, bool blocking)
        {
            var installed = AutodeskIfcExtension.FindInstalled();
            var loaded = AutodeskIfcExtension.CommandDefined(command);
            var ok = installed.Count > 0 || loaded;
            var oficial = string.IsNullOrWhiteSpace(cfg.ExtensaoAutodeskUrl) ? AutodeskIfcExtension.DownloadsUrl : cfg.ExtensaoAutodeskUrl;
            var github = cfg.VersoesMetroSpUrl;

            var w = NewWindow("Exportador IFC", 500);
            var body = new StackPanel();
            if (blocking && !ok)
                body.Children.Add(new TextBlock
                {
                    Text = "A exportação não começou: o comando " + command + " não existe neste Civil 3D.",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 8)
                });

            // Caminho oficial: a extensão da Autodesk.
            body.Children.Add(Theme.Secao("IFC 4.3 Extension for Civil 3D · oficial"));
            body.Children.Add(Theme.Selo(ok ? "Instalada" : "Não instalada", ok ? Theme.Estado.Ok : Theme.Estado.Aviso));
            foreach (var i in installed)
            {
                body.Children.Add(Theme.Detalhe(i.Name + (i.Version != null ? " · versão " + i.Version : "")));
                body.Children.Add(Theme.Detalhe(i.Folder));
            }
            if (installed.Count == 0 && loaded)
                body.Children.Add(Theme.Detalhe("Comando " + command + " carregado (versão não identificada)."));
            if (!ok)
                body.Children.Add(new TextBlock
                {
                    Text = "Baixe na conta Autodesk (Produtos e serviços › Atualizações) a extensão da mesma versão do Civil 3D, instale e reinicie o Civil 3D.",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0)
                });
            body.Children.Add(LinkTo(oficial));

            // Caminho GitHub: as versões do plugin MetroSP.
            if (!string.IsNullOrWhiteSpace(github))
            {
                var v = typeof(ExtensionWindow).Assembly.GetName().Version;
                var sec = Theme.Secao("Plugin MetroSP · GitHub");
                sec.Margin = new Thickness(0, 14, 0, 4);
                body.Children.Add(sec);
                body.Children.Add(Theme.Selo("Versão " + v.Major + "." + v.Minor + "." + v.Build, Theme.Estado.Neutro));
                body.Children.Add(LinkTo(github));
            }

            var open = new Button { Content = ok ? "Atualizações Autodesk" : "Baixar na conta Autodesk", Style = Theme.PrimaryButton, IsDefault = true };
            open.Click += (s, e) => Open(oficial);
            var buttons = new List<Button> { open };
            if (!string.IsNullOrWhiteSpace(github))
            {
                var gh = new Button { Content = "Versões no GitHub" };
                gh.Click += (s, e) => Open(github);
                buttons.Add(gh);
            }
            var close = new Button { Content = "Fechar", IsCancel = true };
            close.Click += (s, e) => w.Close();
            buttons.Add(close);
            Theme.Apply(w, "Exportador IFC", "Caminho oficial e caminho GitHub", body, Theme.Buttons(buttons.ToArray()));
            AcApp.ShowModalWindow(AcApp.MainWindow.Handle, w, false);
        }

        static Button LinkTo(string url)
        {
            var l = Theme.Link(url.Replace("https://", ""));
            l.HorizontalAlignment = HorizontalAlignment.Left;
            l.Margin = new Thickness(0, 4, 0, 0);
            l.ToolTip = url;
            l.Click += (s, e) => Open(url);
            return l;
        }

        static void Open(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { Clipboard.SetText(url); MessageBox.Show("Não foi possível abrir o navegador. O endereço foi copiado:\n" + url, "MetroSP"); }
        }

        internal static Window NewWindow(string title, double width) => new Window
        {
            Title = title,
            Width = width,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
    }

    /// <summary>Uma pergunta só, antes da exportação: qual perfil usar em cada alinhamento com mais de um.</summary>
    public static class ProfileWindow
    {
        public class Row
        {
            public string Alignment { get; set; }
            public List<string> Profiles { get; set; }
            public string Chosen { get; set; }
        }

        /// <returns>false se o usuário cancelou.</returns>
        public static bool Ask(IList<Row> rows, out bool remember)
        {
            var w = ExtensionWindow.NewWindow("Perfis do alinhamento 3D", 560);
            var body = new StackPanel();
            body.Children.Add(new TextBlock
            {
                Text = "Estes alinhamentos têm mais de um perfil. Escolha o que dá as cotas no IFC.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var h1 = Theme.Secao("Alinhamento");
            var h2 = Theme.Secao("Perfil");
            Grid.SetColumn(h2, 1);
            grid.Children.Add(h1);
            grid.Children.Add(h2);
            var combos = new List<ComboBox>();
            foreach (var r in rows)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var row = grid.RowDefinitions.Count - 1;
                var name = new TextBlock { Text = r.Alignment, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 8, 3), TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetRow(name, row);
                grid.Children.Add(name);
                var combo = new ComboBox { ItemsSource = r.Profiles, SelectedItem = r.Chosen, Margin = new Thickness(0, 3, 0, 3) };
                Grid.SetRow(combo, row);
                Grid.SetColumn(combo, 1);
                grid.Children.Add(combo);
                combos.Add(combo);
            }
            body.Children.Add(new ScrollViewer { Content = grid, MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

            var rememberBox = new CheckBox { Content = "Lembrar para este desenho", IsChecked = true, Margin = new Thickness(0, 10, 0, 0) };
            body.Children.Add(rememberBox);

            bool accepted = false;
            var go = new Button { Content = "Continuar", Style = Theme.PrimaryButton, IsDefault = true };
            go.Click += (s, e) => { accepted = true; w.Close(); };
            var cancel = new Button { Content = "Cancelar", IsCancel = true };
            cancel.Click += (s, e) => w.Close();
            Theme.Apply(w, "Perfis do alinhamento 3D", rows.Count == 1 ? "1 alinhamento" : rows.Count + " alinhamentos", body, Theme.Buttons(go, cancel));
            AcApp.ShowModalWindow(AcApp.MainWindow.Handle, w, false);

            for (int i = 0; i < rows.Count; i++) rows[i].Chosen = combos[i].SelectedItem as string ?? rows[i].Chosen;
            remember = rememberBox.IsChecked == true;
            return accepted;
        }
    }
}
