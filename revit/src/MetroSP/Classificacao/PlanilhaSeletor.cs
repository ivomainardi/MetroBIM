using System;
using System.IO;
using System.Threading.Tasks;
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

            var rotulo = Theme.Rotulo("Planilha de classificação");
            DockPanel.SetDock(rotulo, Dock.Left);
            Children.Add(rotulo);

            var procurar = new Button { Content = "Procurar...", Margin = new Thickness(6, 0, 0, 0) };
            var recarregar = new Button { Content = "Recarregar", Margin = new Thickness(6, 0, 0, 0), ToolTip = "Lê a planilha de novo (depois de editá-la no Excel)." };
            procurar.Click += (s, e) => Procurar();
            recarregar.Click += (s, e) => Carregar(forcar: true);
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

        /// <summary>
        /// Última planilha lida (caminho, data e tamanho do arquivo): reabrir a janela não lê o .xlsx de novo
        /// enquanto o arquivo não mudar.
        /// </summary>
        private static (string Caminho, DateTime Data, long Tamanho, ClassificationList Lista)? _ultima;

        private static ClassificationList DoCache(string caminho)
        {
            var u = _ultima;
            if (u == null || !string.Equals(u.Value.Caminho, caminho, StringComparison.OrdinalIgnoreCase)) return null;
            var f = new FileInfo(caminho);
            return f.LastWriteTimeUtc == u.Value.Data && f.Length == u.Value.Tamanho ? u.Value.Lista : null;
        }

        private static ClassificationList LerComCache(string caminho)
        {
            var f = new FileInfo(caminho);
            DateTime data = f.LastWriteTimeUtc;
            long tamanho = f.Length;
            ClassificationList l = ClassificationList.Ler(caminho);
            _ultima = (caminho, data, tamanho, l);
            return l;
        }

        /// <summary>Lê a planilha configurada (se houver). Chame depois de ligar o evento Carregada.</summary>
        /// <param name="forcar">true lê o arquivo mesmo que ele não tenha mudado (botão Recarregar).</param>
        public void Carregar(bool forcar = false)
        {
            string c = _config.Planilha;
            if (string.IsNullOrEmpty(c) || !File.Exists(c))
            {
                _info.Text = "escolha a planilha (.xlsx ou .csv)";
                return;
            }
            try
            {
                Concluir((!forcar ? DoCache(c) : null) ?? LerComCache(c));
            }
            catch (Exception ex)
            {
                Falhou(ex);
            }
        }

        /// <summary>
        /// Como Carregar, mas lê o arquivo fora da tela: a janela abre na hora e a árvore aparece quando a leitura termina.
        /// </summary>
        public async void CarregarEmSegundoPlano()
        {
            string c = _config.Planilha;
            if (string.IsNullOrEmpty(c) || !File.Exists(c))
            {
                _info.Text = "escolha a planilha (.xlsx ou .csv)";
                return;
            }
            ClassificationList pronta = DoCache(c);
            if (pronta != null) { Concluir(pronta); return; }
            _info.Text = "lendo a planilha...";
            try
            {
                ClassificationList l = await Task.Run(() => LerComCache(c));
                if (_config.Planilha == c) Concluir(l); // o usuário pode ter escolhido outra planilha enquanto lia
            }
            catch (Exception ex)
            {
                Falhou(ex);
            }
        }

        private void Concluir(ClassificationList l)
        {
            Lista = l;
            _info.Text = $"{Lista.Itens.Count} itens" + (Lista.Avisos.Count > 0 ? $" · {Lista.Avisos.Count} avisos" : "");
            _info.ToolTip = Lista.Avisos.Count > 0 ? string.Join("\n", Lista.Avisos) : null;
            Carregada?.Invoke(Lista);
        }

        private void Falhou(Exception ex)
        {
            _info.Text = "erro ao ler";
            MessageBox.Show(Window.GetWindow(this), "Não foi possível ler a planilha:\n" + ex.Message +
                "\n\nA planilha precisa ter as colunas Código e Descrição (opcionais: Código pai, Categorias, Edição).", "MetroSP");
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
            Carregar(forcar: true);
        }
    }
}
