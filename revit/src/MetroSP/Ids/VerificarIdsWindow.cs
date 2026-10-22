using System.IO;
using System.Windows;
using System.Windows.Controls;
using MetroSP.Ui;

namespace MetroSP.Ids
{
    /// <summary>Janela de entrada: escolher o IDS e os arquivos de mapeamento da exportação IFC.</summary>
    public class VerificarIdsWindow : Window
    {
        private readonly TextBox _ids;
        private readonly TextBox _psets;
        private readonly TextBox _categorias;
        private readonly CheckBox _valores;

        public VerificarIdsWindow(IdsSettings settings)
        {
            Title = "MetroSP - Verificar mapeamento IDS";
            Width = 600;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            MinWidth = 480;

            var panel = new StackPanel();
            _ids = AddFileRow(panel, "Arquivo IDS (.ids)", settings.IdsFile, "IDS (*.ids;*.xml)|*.ids;*.xml");
            _psets = AddFileRow(panel, "Property sets definidos pelo usuário (.txt) - opcional", settings.PropertySetsFile, "Texto (*.txt)|*.txt");
            _categorias = AddFileRow(panel, "Tabela de classes IFC (.txt) - opcional, senão usa o padrão do exportador", settings.CategoryMappingFile, "Texto (*.txt)|*.txt");

            _valores = new CheckBox
            {
                Content = "Verificar também valores vazios e fora do exigido",
                IsChecked = settings.CheckValues,
                Margin = new Thickness(0, 10, 0, 4),
            };
            panel.Children.Add(_valores);

            var ok = new Button { Content = "Verificar", IsDefault = true, Style = Theme.PrimaryButton };
            ok.Click += (s, e) =>
            {
                if (!File.Exists(_ids.Text.Trim()))
                {
                    MessageBox.Show(this, "Indique um arquivo IDS válido.", Title);
                    return;
                }
                foreach (TextBox opcional in new[] { _psets, _categorias })
                {
                    string caminho = opcional.Text.Trim();
                    if (caminho.Length > 0 && !File.Exists(caminho))
                    {
                        MessageBox.Show(this, $"Arquivo não encontrado:\n{caminho}", Title);
                        return;
                    }
                }
                settings.IdsFile = _ids.Text.Trim();
                settings.PropertySetsFile = _psets.Text.Trim();
                settings.CategoryMappingFile = _categorias.Text.Trim();
                settings.CheckValues = _valores.IsChecked == true;
                DialogResult = true;
            };
            var cancelar = new Button { Content = "Cancelar", IsCancel = true };

            Theme.Apply(this, "Verificar mapeamento IDS", "Confere parâmetros e mapeamento de exportação IFC antes de exportar", panel, Theme.Buttons(cancelar, ok));
        }

        private TextBox AddFileRow(Panel panel, string label, string value, string filter)
        {
            panel.Children.Add(Theme.Label(label));
            var linha = new DockPanel();
            var procurar = new Button { Content = "Procurar...", Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(procurar, Dock.Right);
            linha.Children.Add(procurar);
            var box = new TextBox { Text = value ?? string.Empty, VerticalContentAlignment = VerticalAlignment.Center };
            linha.Children.Add(box);
            panel.Children.Add(linha);

            procurar.Click += (s, e) =>
            {
                var dialogo = new Microsoft.Win32.OpenFileDialog { Filter = filter + "|Todos os arquivos (*.*)|*.*" };
                if (File.Exists(box.Text.Trim()))
                {
                    dialogo.InitialDirectory = Path.GetDirectoryName(box.Text.Trim());
                    dialogo.FileName = Path.GetFileName(box.Text.Trim());
                }
                if (dialogo.ShowDialog(this) == true) box.Text = dialogo.FileName;
            };
            return box;
        }
    }
}
