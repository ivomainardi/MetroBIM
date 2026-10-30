using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MetroSP.Ui;

namespace MetroSP.Ids
{
    /// <summary>Navega pelos hubs, projetos e pastas do Forma online para escolher a pasta das configurações de IFC.</summary>
    public class FormaPastaWindow : Window
    {
        private readonly TreeView _arvore;
        private readonly TextBlock _info;

        public FormaOnline.Pasta Escolhida { get; private set; }

        public FormaPastaWindow()
        {
            Title = "MetroSP - Pasta no Forma online";
            Width = 560;
            Height = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var corpo = new DockPanel();
            _info = new TextBlock
            {
                Text = "Escolha a pasta com as configurações de IFC. Ela pode ter as subpastas Setups, PropertySets e Mapeamentos, " +
                       "ou os .json e .txt soltos. Um Nomenclatura.txt na pasta define a classe pelo nome do modelo.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Theme.Solid(Theme.Cinza),
                Margin = new Thickness(0, 0, 0, 8),
            };
            DockPanel.SetDock(_info, Dock.Top);
            corpo.Children.Add(_info);
            _arvore = new TreeView();
            corpo.Children.Add(_arvore);

            var ok = new Button { Content = "Usar esta pasta", IsDefault = true, Style = Theme.PrimaryButton };
            ok.Click += (s, e) => Confirmar();
            var cancelar = new Button { Content = "Cancelar", IsCancel = true };
            Theme.Apply(this, "Pasta no Forma online", "Os arquivos dela são baixados e atualizados a cada exportação", corpo, Theme.Buttons(cancelar, ok));

            Loaded += (s, e) => Carregar(null, _arvore.Items, () => FormaOnline.Hubs().Cast<FormaOnline.Item>().ToList());
        }

        private static readonly object Carregando = "Carregando...";

        private void Carregar(TreeViewItem pai, ItemCollection destino, Func<List<FormaOnline.Item>> buscar)
        {
            Cursor = Cursors.Wait;
            try
            {
                List<FormaOnline.Item> itens = buscar();
                destino.Clear();
                foreach (FormaOnline.Item i in itens.Where(i => i.Tipo != "arquivo"))
                {
                    var no = new TreeViewItem { Header = Rotulo(i), Tag = i };
                    no.Items.Add(Carregando);
                    no.Expanded += Expandir;
                    destino.Add(no);
                }
                // Arquivos aparecem só para conferência.
                foreach (FormaOnline.Item i in itens.Where(i => i.Tipo == "arquivo"))
                    destino.Add(new TreeViewItem { Header = i.Nome, Foreground = Theme.Solid(Theme.Cinza), Focusable = false });
                if (pai == null && itens.Count == 0) _info.Text = "Nenhum hub do Forma disponível para esta conta (ou o app do MetroSP não foi liberado no ACC).";
            }
            catch (Exception ex)
            {
                destino.Clear();
                if (pai != null) pai.Items.Add(new TreeViewItem { Header = "Erro: " + ex.Message, Foreground = Theme.Solid(Theme.Laranja) });
                else _info.Text = "Erro ao ler o Forma: " + ex.Message;
                _info.Foreground = Theme.Solid(Theme.Laranja);
            }
            finally { Cursor = null; }
        }

        private void Expandir(object sender, RoutedEventArgs e)
        {
            var no = (TreeViewItem)sender;
            if (!ReferenceEquals(e.OriginalSource, no) || no.Items.Count != 1 || !ReferenceEquals(no.Items[0], Carregando)) return;
            var i = (FormaOnline.Item)no.Tag;
            Func<List<FormaOnline.Item>> buscar =
                i.Tipo == "hub" ? () => FormaOnline.Projetos(i) :
                i.Tipo == "projeto" ? () => FormaOnline.PastasRaiz(i) :
                (Func<List<FormaOnline.Item>>)(() => FormaOnline.Conteudo(i.ProjetoId, i.Id));
            Carregar(no, no.Items, buscar);
        }

        private static string Rotulo(FormaOnline.Item i) =>
            i.Tipo == "hub" ? "▣ " + i.Nome : i.Tipo == "projeto" ? "◆ " + i.Nome : "📁 " + i.Nome;

        private void Confirmar()
        {
            if (!(_arvore.SelectedItem is TreeViewItem no) || !(no.Tag is FormaOnline.Item i) || i.Tipo != "pasta")
            {
                MessageBox.Show(this, "Escolha uma pasta (dentro de um projeto).", Title);
                return;
            }
            var caminho = new List<string>();
            for (DependencyObject d = no; d != null && !(d is TreeView); d = System.Windows.Media.VisualTreeHelper.GetParent(d))
                if (d is TreeViewItem t && t.Tag is FormaOnline.Item it) caminho.Insert(0, it.Nome);
            Escolhida = new FormaOnline.Pasta { ProjetoId = i.ProjetoId, PastaId = i.Id, Descricao = string.Join(" › ", caminho) };
            DialogResult = true;
        }
    }
}
