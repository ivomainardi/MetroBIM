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
        private readonly TextBlock _resumo = new TextBlock { FontSize = 11.5 };
        private readonly System.Collections.Generic.List<(string Nome, TextBox Caminho, bool Obrigatorio)> _arquivos =
            new System.Collections.Generic.List<(string, TextBox, bool)>();

        public VerificarIdsWindow(IdsSettings settings)
        {
            Title = "MetroSP - Verificar Padrões Metrô";
            Width = 600;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            MinWidth = 480;

            var panel = new StackPanel();
            var secao = Theme.Secao("Arquivos");
            secao.Margin = new Thickness(0, 0, 0, 5);
            panel.Children.Add(secao);
            panel.Children.Add(Theme.Grupo(out StackPanel lista));
            _ids = AddFileRow(lista, "Padrões (.ids)", settings.IdsFile, "Padrões Metrô (*.ids;*.xml)|*.ids;*.xml", true, "Arquivo .ids (formato IDS da buildingSMART) com as exigências de informação do Metrô.");
            _psets = AddFileRow(lista, "Property sets", settings.PropertySetsFile, "Texto (*.txt)|*.txt", false, "Opcional: .txt de property sets definidos pelo usuário.");
            _categorias = AddFileRow(lista, "Classes IFC", settings.CategoryMappingFile, "Texto (*.txt)|*.txt", false, "Opcional: tabela de classes IFC. Sem ela, vale o padrão do exportador.");

            panel.Children.Add(Theme.Secao("Opções"));
            _valores = new CheckBox
            {
                Content = "Verificar também valores vazios e fora do exigido",
                IsChecked = settings.CheckValues,
            };
            panel.Children.Add(_valores);

            var ok = new Button { Content = "Verificar", IsDefault = true, Style = Theme.PrimaryButton };
            ok.Click += (s, e) =>
            {
                if (!File.Exists(_ids.Text.Trim()))
                {
                    MessageBox.Show(this, "Indique um arquivo de padrões (.ids) válido.", Title);
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

            Theme.Apply(this, "Verificar Padrões Metrô", "Confere parâmetros e mapeamento de exportação IFC antes de exportar", panel, Theme.Rodape(_resumo, cancelar, ok));
            AtualizarResumo();
        }

        private void AtualizarResumo()
        {
            var avisos = new System.Collections.Generic.List<string>();
            foreach (var (nome, caminho, obrigatorio) in _arquivos)
            {
                string t = caminho.Text.Trim();
                if (t.Length == 0 ? obrigatorio : !File.Exists(t)) avisos.Add(nome + (t.Length == 0 ? " não indicado" : " não encontrado"));
            }
            Theme.MudarResumo(_resumo, avisos, "Pronto para verificar");
        }

        /// <summary>Item da lista: nome do arquivo, selo de situação e Procurar; o caminho completo fica na dica.</summary>
        private TextBox AddFileRow(StackPanel lista, string nome, string value, string filter, bool obrigatorio, string dica)
        {
            var arquivo = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, FontFamily = new System.Windows.Media.FontFamily("Consolas") };
            Border selo = Theme.Selo();
            var procurar = new Button { Content = "Procurar..." };
            var box = new TextBox();
            void Mostrar()
            {
                string t = box.Text.Trim();
                arquivo.Text = t.Length == 0 ? (obrigatorio ? "falta indicar" : "nenhum (opcional)") : Path.GetFileName(t);
                arquivo.Foreground = Theme.Solid(t.Length == 0 ? Theme.Cinza : Theme.Preto);
                arquivo.ToolTip = t.Length == 0 ? dica : t;
                if (t.Length == 0) Theme.MudarSelo(selo, obrigatorio ? "obrigatório" : "", obrigatorio ? Theme.Estado.Aviso : Theme.Estado.Neutro);
                else if (File.Exists(t)) Theme.MudarSelo(selo, "ok", Theme.Estado.Ok);
                else Theme.MudarSelo(selo, "não encontrado", Theme.Estado.Aviso, t);
                AtualizarResumo();
            }
            box.TextChanged += (s, e) => Mostrar();
            box.Text = value ?? string.Empty;
            _arquivos.Add((nome, box, obrigatorio));
            lista.Children.Add(Theme.Item(nome, arquivo, selo, procurar, null, lista.Children.Count == 0));
            Mostrar();

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
