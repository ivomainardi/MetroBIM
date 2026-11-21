using System.Windows;
using System.Windows.Controls;
using MetroSP.Ui;

namespace MetroSP.Ids
{
    /// <summary>
    /// Explica o que o MetroSP corrige e completa no IFC depois do exportador do Revit (link "O que o MetroSP corrige no
    /// IFC..." no Exportar IFC). Pouco texto na linha; o detalhe vai na dica de cada item.
    /// </summary>
    public class CorrecoesIfcWindow : Window
    {
        private static readonly (string Secao, (string Nome, string Texto, string Dica)[] Itens)[] Conteudo =
        {
            ("Projeto e local", new[]
            {
                ("Localização", "Latitude e longitude do IfcSite pelo Project Base Point; sem EPSG quando pedido.",
                 "O IfcSite sai sempre com a latitude e a longitude do Project Base Point, mesmo que o Location do Revit não confira. " +
                 "Com EPSG em branco, o IfcMapConversion e o IfcProjectedCRS saem do arquivo."),
                ("Dados do projeto e classificação", "IfcProject, IfcSite, IfcBuilding, Pset MetroSP_Projeto e IfcClassificationReference.",
                 "Nomes, descrições e endereço da etapa Projeto e local, e a classificação dos padrões gravada como IfcClassificationReference " +
                 "(o exportador deixa só em property set)."),
                ("Status da fase", "Status no Pset_*Common e Estado no Pset Metro, na fase exportada.",
                 "Novo, existente ou demolido, conforme a fase escolhida no Exportar."),
            }),
            ("Elementos", new[]
            {
                ("Classe IFC", "O que virou IfcBuildingElementProxy volta para a classe do \"Exportar para IFC como\".",
                 "O exportador do Revit ignora a classe pedida em algumas categorias (sancas, frisos, famílias no local). " +
                 "O MetroSP troca a classe e o tipo pré-definido no IFC, junto com o tipo (IfcXxxType)."),
                ("Escadas e rampas", "Lances que saíram como laje viram IfcStairFlight ou IfcRampFlight.",
                 "Rampas e escadas com vários lances: as partes que não são patamar saíam como IfcSlab dentro da IfcRamp ou da IfcStair."),
                ("Sistemas MEP", "Tubos, dutos, equipamentos e circuitos no IfcDistributionSystem do Revit.",
                 "Elementos de distribuição que saíram fora de qualquer sistema entram no sistema do Revit (nome, tipo e classificação " +
                 "do sistema), ligado ao IfcBuilding."),
                ("Materiais", "Material do Revit nos elementos que saíram sem material.",
                 "Um material: IfcMaterial. Vários: IfcMaterialConstituentSet, com a fração de volume de cada um. " +
                 "Elementos que já têm material (neles ou no tipo) ficam como estão."),
                ("Ligações metálicas", "Parafusos, chapas e soldas como elementos próprios, com diâmetro e comprimento.",
                 "No IFC4 e no 4.3 o exportador junta as peças das ligações de aço; o MetroSP separa e agrupa na ligação."),
            }),
            ("Quantidades", new[]
            {
                ("Quantidades base (Qto)", "Volume, área e comprimento que faltam ou saíram zerados, calculados pelo Revit.",
                 "Escadas, lances e patamares, rampas, paredes, lajes, coberturas, pilares, vigas, fundações, estacas, placas, " +
                 "revestimentos, guarda-corpos, fachadas cortina, portas, janelas, proxies, tubos, dutos e eletrocalhas. " +
                 "Os valores \"Net\" de volume e área que diferem mais de 10% do Revit também são trocados; o valor do exportador " +
                 "fica no registro da exportação."),
                ("MetroQto", "Os mesmos valores no MetroQto, nas propriedades que o MetroDATA pede.",
                 "Volume, Area, AreaSuperficie, Secao, Comprimento, Largura, Altura, Espessura e Peso, quando o parâmetro do Revit " +
                 "não existe no elemento. As propriedades vêm da planilha MetroDATA da Entrega e do .txt de property sets."),
                ("Armaduras e telas", "Quantidade, comprimento e peso de cada barra; diâmetro, aço e superfície; anéis; telas.",
                 "Qto_ReinforcingElementBaseQuantities das barras (inclusive conjuntos de comprimento variável e armaduras de área e " +
                 "de caminho), diâmetro, área da seção, comprimento, aço, superfície e função da barra e do tipo (Pset_ReinforcingBarCommon), " +
                 "anéis fechados em arcos de 3 pontos, MetroQto da própria barra nos conjuntos variáveis, aviso quando o material do Revit " +
                 "não diz o aço da classificação e dimensões, diâmetros e espaçamentos das telas soldadas (IfcReinforcingMesh) pelo Revit."),
            }),
        };

        public static void Mostrar(Window dona)
        {
            var w = new CorrecoesIfcWindow();
            if (dona != null && dona.IsLoaded) w.Owner = dona;
            w.ShowDialog();
        }

        public CorrecoesIfcWindow()
        {
            Title = "MetroSP - Correções no IFC";
            Width = 640;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var corpo = new StackPanel();
            corpo.Children.Add(new TextBlock
            {
                Text = "Valem só para os elementos que vão para o IFC. O resultado de cada exportação aparece no resumo do fim.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Theme.Solid(Theme.Cinza),
            });
            foreach (var (secao, itens) in Conteudo)
            {
                corpo.Children.Add(Theme.Secao(secao));
                Border grupo = Theme.Grupo(out StackPanel lista);
                bool primeiro = true;
                foreach (var (nome, texto, dica) in itens)
                {
                    var linha = new DockPanel { Margin = new Thickness(10, 7, 10, 7), ToolTip = dica, Background = System.Windows.Media.Brushes.Transparent };
                    var ok = new Border
                    {
                        Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = Theme.Solid(Theme.VerdeClaro),
                        VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 10, 0),
                        Child = new TextBlock { Text = "✓", Foreground = Theme.Solid(Theme.Verde), FontWeight = FontWeights.Bold,
                                                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                    };
                    DockPanel.SetDock(ok, Dock.Left);
                    linha.Children.Add(ok);
                    var textos = new StackPanel();
                    textos.Children.Add(new TextBlock { Text = nome, FontWeight = FontWeights.SemiBold });
                    textos.Children.Add(new TextBlock { Text = texto, TextWrapping = TextWrapping.Wrap, Foreground = Theme.Solid(System.Windows.Media.Color.FromRgb(0x44, 0x44, 0x44)) });
                    linha.Children.Add(textos);
                    var caixa = new Border
                    {
                        BorderBrush = Theme.Solid(Theme.CinzaClaro),
                        BorderThickness = new Thickness(0, primeiro ? 0 : 1, 0, 0),
                        Child = linha,
                    };
                    lista.Children.Add(caixa);
                    primeiro = false;
                }
                corpo.Children.Add(grupo);
            }
            var fechar = new Button { Content = "Fechar", IsCancel = true, IsDefault = true };
            fechar.Click += (s, e) => Close();
            Theme.Apply(this, "Correções no IFC", "o que o MetroSP completa depois do exportador do Revit", corpo, Theme.Rodape(null, fechar));
        }
    }
}
