using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MetroSP.Ui;

namespace MetroSP.Conferencia
{
    /// <summary>
    /// Objeto do MetroDATA de cada categoria do modelo. Sem escolha, vale a classe IFC (quando ela é de um objeto só).
    /// A escolha fica salva com os padrões, para a próxima conferência.
    /// </summary>
    public class ObjetosMetroDataWindow : Window
    {
        public const string PelaClasse = "(pela classe IFC)";

        /// <summary>Categoria → objeto escolhido (válido depois de DialogResult = true).</summary>
        public Dictionary<string, string> Objetos { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public ObjetosMetroDataWindow(List<CategoriaMetroData> categorias, List<string> objetos, Dictionary<string, string> atuais, string destaque = null)
        {
            Title = "MetroSP - Objetos do MetroDATA";
            Width = 860;
            Height = 600;
            MinWidth = 600;
            MinHeight = 320;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            foreach (var k in atuais ?? new Dictionary<string, string>()) Objetos[k.Key] = k.Value;
            var opcoes = new List<string> { PelaClasse };
            opcoes.AddRange(objetos);

            var grade = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

            void Celula(UIElement el, int linha, int coluna)
            {
                Grid.SetRow(el, linha);
                Grid.SetColumn(el, coluna);
                grade.Children.Add(el);
            }
            TextBlock Cab(string t, string d) => new TextBlock { Text = t, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 12, 6), ToolTip = d };

            grade.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Celula(Cab("Categoria", "Categoria do Revit."), 0, 0);
            Celula(Cab("Classe IFC", "Classe IFC com que os elementos vão para o IFC."), 0, 1);
            Celula(Cab("Elementos", "Elementos da categoria que vão para o IFC."), 0, 2);
            Celula(Cab("Objeto do MetroDATA", "Objeto cujas propriedades são conferidas. \"" + PelaClasse + "\" usa a Classe IFC do objeto na planilha."), 0, 3);

            ComboBox focar = null;
            int i = 1;
            foreach (CategoriaMetroData c in categorias.OrderBy(c => c.SemObjeto.Count == 0).ThenByDescending(c => c.Elementos.Count))
            {
                grade.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                bool falta = c.SemObjeto.Count > 0;
                Celula(new TextBlock
                {
                    Text = c.Categoria, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 12, 3),
                    FontWeight = falta ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = Theme.Solid(falta ? Theme.Laranja : Theme.Preto),
                    ToolTip = falta ? $"{c.SemObjeto.Count} elemento(s) sem objeto: escolha um." : null,
                }, i, 0);
                Celula(new TextBlock { Text = string.Join(", ", c.Classes), VerticalAlignment = VerticalAlignment.Center, Foreground = Theme.Solid(Theme.Cinza), Margin = new Thickness(0, 3, 12, 3), TextTrimming = TextTrimming.CharacterEllipsis }, i, 1);
                Celula(new TextBlock { Text = c.Elementos.Count.ToString("N0"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 3, 16, 3) }, i, 2);

                string categoria = c.Categoria;
                Objetos.TryGetValue(categoria, out string atual);
                string dica = c.Candidatos.Count == 0 ? "Pela classe IFC: nenhum objeto da disciplina tem esta classe."
                    : c.Candidatos.Count == 1 ? "Pela classe IFC: " + c.Candidatos[0]
                    : "Pela classe IFC: mais de um objeto (" + string.Join(", ", c.Candidatos) + "). Escolha um.";
                ComboBox combo = ListaDigitavel.Criar(opcoes, string.IsNullOrEmpty(atual) ? PelaClasse : atual, o =>
                {
                    if (o == PelaClasse) Objetos.Remove(categoria);
                    else Objetos[categoria] = o;
                }, double.NaN, dica);
                combo.HorizontalAlignment = HorizontalAlignment.Stretch;
                combo.Margin = new Thickness(0, 2, 0, 2);
                Celula(combo, i, 3);
                if (destaque != null && categoria.Equals(destaque, StringComparison.OrdinalIgnoreCase)) focar = combo;
                i++;
            }

            var painel = new DockPanel();
            var intro = Theme.Detalhe("Sem escolha, vale a Classe IFC do objeto na planilha. Categorias em laranja têm elementos sem objeto.");
            intro.TextWrapping = TextWrapping.Wrap;
            intro.Margin = new Thickness(0, 0, 0, 8);
            DockPanel.SetDock(intro, Dock.Top);
            painel.Children.Add(intro);
            painel.Children.Add(new ScrollViewer { Content = grade, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

            var salvar = new Button { Content = "Salvar e conferir", IsDefault = true, Style = Theme.PrimaryButton };
            salvar.Click += (s, e) => DialogResult = true;
            var cancelar = new Button { Content = "Cancelar", IsCancel = true };
            var rodape = new DockPanel();
            StackPanel direita = Theme.Buttons(cancelar, salvar);
            DockPanel.SetDock(direita, Dock.Right);
            rodape.Children.Add(direita);

            Theme.Apply(this, "Objetos do MetroDATA", "O objeto de cada categoria do modelo", painel, rodape);
            if (focar != null) Loaded += (s, e) => { focar.BringIntoView(); focar.Focus(); };
        }
    }
}
