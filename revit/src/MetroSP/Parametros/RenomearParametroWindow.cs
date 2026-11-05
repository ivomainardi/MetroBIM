using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MetroSP.Ui;

namespace MetroSP.Parametros
{
    public class ParametroItem
    {
        public string Nome { get; set; }
        public System.Guid Guid { get; set; }
        public object Tag { get; set; }
        public override string ToString() => $"{Nome}   ({Guid})";
    }

    /// <summary>Janela simples: escolher o parâmetro compartilhado e digitar o novo nome.</summary>
    public class RenomearParametroWindow : Window
    {
        private readonly ComboBox _combo;
        private readonly TextBox _novoNome;
        private readonly TextBox _arquivo;
        private readonly CheckBox _atualizarArquivo;
        private readonly CheckBox _familias;
        private readonly CheckBox _aninhadas;

        public ParametroItem Selecionado => _combo.SelectedItem as ParametroItem;
        public string NovoNome => _novoNome.Text.Trim();
        public string ArquivoCompartilhado => _arquivo.Text.Trim();
        /// <summary>Projeto: renomear também nas famílias carregadas (mesmo GUID).</summary>
        public bool RenomearFamilias => _familias?.IsChecked == true;
        public bool IncluirAninhadas => _aninhadas?.IsChecked == true;
        public bool AtualizarArquivo => _atualizarArquivo.IsChecked == true && File.Exists(ArquivoCompartilhado);

        public RenomearParametroWindow(IEnumerable<ParametroItem> itens, string arquivoCompartilhado,
            ParametroItem selecionado = null, string novoNome = null, bool? atualizarTxt = null, bool projeto = false)
        {
            Title = "MetroSP - Renomear parâmetro compartilhado";
            Width = 540;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            MinWidth = 440;

            var panel = new StackPanel();

            panel.Children.Add(Theme.Label("Parâmetro compartilhado"));
            _combo = new ComboBox { ItemsSource = itens.OrderBy(i => i.Nome).ToList(), Padding = new Thickness(4, 3, 4, 3) };
            _combo.SelectionChanged += (s, e) => { if (Selecionado != null) _novoNome.Text = Selecionado.Nome; };
            panel.Children.Add(_combo);

            panel.Children.Add(Theme.Label("Novo nome"));
            _novoNome = new TextBox();
            panel.Children.Add(_novoNome);

            panel.Children.Add(Theme.Label("Arquivo de parâmetros compartilhados (.txt)"));
            var linhaArquivo = new DockPanel();
            var procurar = new Button { Content = "Procurar...", Margin = new Thickness(6, 0, 0, 0) };
            procurar.Click += (s, e) => ProcurarArquivo();
            DockPanel.SetDock(procurar, Dock.Right);
            linhaArquivo.Children.Add(procurar);
            _arquivo = new TextBox { Text = arquivoCompartilhado ?? string.Empty, VerticalContentAlignment = VerticalAlignment.Center };
            linhaArquivo.Children.Add(_arquivo);
            panel.Children.Add(linhaArquivo);

            _atualizarArquivo = new CheckBox
            {
                Content = "Atualizar também o .txt (backup em .bak)",
                IsChecked = atualizarTxt ?? !string.IsNullOrEmpty(arquivoCompartilhado),
                Margin = new Thickness(0, 12, 0, 4),
            };
            panel.Children.Add(_atualizarArquivo);

            if (projeto)
            {
                _familias = new CheckBox
                {
                    Content = "Renomear também em todas as famílias carregadas (mesmo GUID, valores mantidos)",
                    IsChecked = true,
                    Margin = new Thickness(0, 4, 0, 4),
                    ToolTip = "Abre cada família que tem o parâmetro, renomeia e recarrega no projeto. Os .rfa da biblioteca não são alterados.",
                };
                _aninhadas = new CheckBox
                {
                    Content = "Incluir famílias aninhadas (abre todas as famílias do projeto; mais lento)",
                    Margin = new Thickness(20, 0, 0, 4),
                };
                _familias.Click += (s, e) => _aninhadas.IsEnabled = _familias.IsChecked == true;
                panel.Children.Add(_familias);
                panel.Children.Add(_aninhadas);
            }

            var ok = new Button { Content = "Renomear", IsDefault = true, Style = Theme.PrimaryButton };
            ok.Click += (s, e) =>
            {
                if (Selecionado == null || string.IsNullOrWhiteSpace(NovoNome) || NovoNome == Selecionado.Nome)
                {
                    MessageBox.Show(this, "Escolha um parâmetro e informe um nome diferente do atual.", Title);
                    return;
                }
                if (_atualizarArquivo.IsChecked == true && !File.Exists(ArquivoCompartilhado))
                {
                    MessageBox.Show(this, "Arquivo de parâmetros compartilhados não encontrado. Use \"Procurar...\" para indicar o .txt.", Title);
                    return;
                }
                DialogResult = true;
            };
            var cancelar = new Button { Content = "Cancelar", IsCancel = true };

            Theme.Apply(this, "Renomear parâmetro", "O GUID será mantido", panel, Theme.Buttons(cancelar, ok));

            if (selecionado != null)
            {
                _combo.SelectedItem = ((List<ParametroItem>)_combo.ItemsSource).FirstOrDefault(i => i.Guid == selecionado.Guid);
                _combo.IsEnabled = false;
            }
            if (!string.IsNullOrEmpty(novoNome)) _novoNome.Text = novoNome;
        }

        private void ProcurarArquivo()
        {
            var dialogo = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Arquivo de parâmetros compartilhados",
                Filter = "Parâmetros compartilhados (*.txt)|*.txt|Todos os arquivos (*.*)|*.*",
            };
            if (File.Exists(ArquivoCompartilhado))
            {
                dialogo.InitialDirectory = Path.GetDirectoryName(ArquivoCompartilhado);
                dialogo.FileName = Path.GetFileName(ArquivoCompartilhado);
            }

            if (dialogo.ShowDialog(this) == true)
            {
                _arquivo.Text = dialogo.FileName;
                _atualizarArquivo.IsChecked = true;
            }
        }
    }
}
