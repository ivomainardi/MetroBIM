using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MetroSP.Ui;

namespace MetroSP.Classificacao
{
    /// <summary>Barra "Planilha: [caminho] Procurar... Recarregar", comum às ferramentas de classificação.</summary>
    public class PlanilhaSeletor : DockPanel
    {
        private readonly ClassSettings _config;
        private readonly TextBox _caminho;
        private readonly TextBlock _info;

        public ClassificationList Lista { get; private set; }

        /// <summary>Disparado quando uma planilha é lida com sucesso.</summary>
        public event Action<ClassificationList> Carregada;

        public PlanilhaSeletor(ClassSettings config)
        {
            _config = config;
            Margin = new Thickness(0, 0, 0, 10);

            var rotulo = new TextBlock { Text = "Planilha de classificação", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            DockPanel.SetDock(rotulo, Dock.Left);
            Children.Add(rotulo);

            var procurar = new Button { Content = "Procurar...", Margin = new Thickness(6, 0, 0, 0) };
            var recarregar = new Button { Content = "Recarregar", Margin = new Thickness(6, 0, 0, 0), ToolTip = "Lê a planilha de novo (depois de editá-la no Excel)." };
            procurar.Click += (s, e) => Procurar();
            recarregar.Click += (s, e) => Carregar();
            DockPanel.SetDock(recarregar, Dock.Right);
            DockPanel.SetDock(procurar, Dock.Right);
            Children.Add(recarregar);
            Children.Add(procurar);

            _info = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            DockPanel.SetDock(_info, Dock.Right);
            Children.Add(_info);

            _caminho = new TextBox { Text = config.Planilha ?? "", IsReadOnly = true, VerticalContentAlignment = VerticalAlignment.Center };
            Children.Add(_caminho);
        }

        /// <summary>Lê a planilha configurada (se houver). Chame depois de ligar o evento Carregada.</summary>
        public void Carregar()
        {
            string c = _config.Planilha;
            if (string.IsNullOrEmpty(c) || !File.Exists(c))
            {
                _info.Text = "escolha a planilha (.xlsx ou .csv)";
                return;
            }
            try
            {
                Lista = ClassificationList.Ler(c);
                _info.Text = $"{Lista.Itens.Count} itens" + (Lista.Avisos.Count > 0 ? $" · {Lista.Avisos.Count} avisos" : "");
                _info.ToolTip = Lista.Avisos.Count > 0 ? string.Join("\n", Lista.Avisos) : null;
                Carregada?.Invoke(Lista);
            }
            catch (Exception ex)
            {
                _info.Text = "erro ao ler";
                MessageBox.Show(Window.GetWindow(this), "Não foi possível ler a planilha:\n" + ex.Message +
                    "\n\nA planilha precisa ter as colunas Código e Descrição (opcionais: Código pai, Categorias, Edição).", "MetroSP");
            }
        }

        private void Procurar()
        {
            var dialogo = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Planilha de classificação",
                Filter = "Planilhas (*.xlsx;*.csv;*.txt)|*.xlsx;*.csv;*.txt|Todos os arquivos (*.*)|*.*",
            };
            if (File.Exists(_config.Planilha))
            {
                dialogo.InitialDirectory = Path.GetDirectoryName(_config.Planilha);
                dialogo.FileName = Path.GetFileName(_config.Planilha);
            }
            if (dialogo.ShowDialog(Window.GetWindow(this)) != true) return;
            _config.Planilha = dialogo.FileName;
            _config.Salvar();
            _caminho.Text = dialogo.FileName;
            Carregar();
        }
    }
}
