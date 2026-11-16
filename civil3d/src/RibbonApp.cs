using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;
using MetroSP.Ui;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(MetroSP.RibbonApp))]

namespace MetroSP
{
    /// <summary>Cria a aba "MetroSP" na ribbon quando o plugin carrega.</summary>
    public class RibbonApp : IExtensionApplication
    {
        const string TabId = "METROSP_TAB";

        const string MarcaId = "METROSP_MARCA";

        public void Initialize()
        {
            if (ComponentManager.Ribbon != null) CreateTab();
            else ComponentManager.ItemInitialized += OnItemInitialized;

            // Trocar de workspace recria a ribbon e some com a aba.
            AcApp.SystemVariableChanged += (s, e) =>
            {
                if (string.Equals(e.Name, "WSCURRENT", StringComparison.OrdinalIgnoreCase)) CreateTab();
            };
        }

        public void Terminate() { }

        void OnItemInitialized(object sender, RibbonItemEventArgs e)
        {
            if (ComponentManager.Ribbon == null) return;
            ComponentManager.ItemInitialized -= OnItemInitialized;
            CreateTab();
        }

        static void CreateTab()
        {
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null || ribbon.FindTab(TabId) != null) return;

            var tab = new RibbonTab { Title = "MetroSP", Id = TabId };
            ribbon.Tabs.Add(tab);

            // Três painéis, iguais aos do Revit: Entrega (por onde o projetista começa), Ferramentas e Coordenação
            // (o que o coordenador BIM prepara). Os comandos não mudam; só passam para as setinhas.
            tab.Panels.Add(Panel("Entrega",
                SplitMenu(
                    Button("Entrega\npasso a passo", "Passo1", "MSP_ENTREGA",
                        "Por onde começar. Janela única da entrega: as etapas em cima, com setinhas (verde = pronta, laranja = pendências), " +
                        "e embaixo o que falta em cada etapa, com os botões que resolvem e a ferramenta da etapa (Exportador, Property sets, " +
                        "Classes IFC, Dados, Classificação, Alinhamentos, Local e exportar). Na setinha: o Painel MetroSP, ao lado do desenho."),
                    Button("Entrega passo a passo", "Passo1", "MSP_ENTREGA", "Janela única da entrega, com as etapas e o que falta em cada uma."),
                    Button("Painel ao lado do desenho", "Conferir", "MSP_PAINEL",
                        "Abre ao lado do desenho o painel com as etapas da entrega e o semáforo de cada uma. " +
                        "Clique numa etapa para abrir a Entrega passo a passo nela.")),
                ExportSplit()));

            tab.Panels.Add(Panel("Ferramentas",
                Button("Classificação", "ClassVerificar", "MSP_CLASSVERIFICAR",
                    "Confere as classificações dos objetos com as planilhas do padrão. Cada linha tem a ação para corrigir."),
                SplitMenu(
                    Button("Dados", "PropertySets", "MSP_PSETS",
                        "Clique: Property sets (nome, propriedades, tipos e objetos; importa de planilha, leva ao IFC e anexa aos objetos). " +
                        "Na setinha: Confrontar planilha e Corrigir escrita."),
                    Button("Property sets", "PropertySets", "MSP_PSETS",
                        "Monta os property sets do Civil 3D (nome, propriedades, tipos e objetos), importa de planilha, leva ao IFC e anexa aos objetos."),
                    Button("Confrontar planilha", "ConfrontarPlanilha", "MSP_CONFRONTAR",
                        "Compara property sets, nome, descrição e layer dos objetos com uma planilha externa e resolve as diferenças."),
                    Button("Corrigir escrita", "CorrigirEscrita", "MSP_ESCRITA",
                        "Procura em property sets, nome e descrição espaços sobrando ou errados (duplos, nas pontas, na pontuação, entre número " +
                        "e unidade), acentuação e caracteres estranhos. Mostra o valor atual e a sugestão para corrigir ou ignorar."))));

            // O que o coordenador BIM prepara uma vez e distribui, no fim da aba: Padrões, com o resto na setinha.
            tab.Panels.Add(Panel("Coordenação",
                SplitMenu(
                    Button("Padrões", "Padroes", "MSP_PADROES",
                        "Adicionar, editar e excluir padrões: classificação, confronto, dados do projeto, corrigir escrita e configuração IFC " +
                        "(mapeamento, propriedades e opções MetroSP). Arraste os arquivos para a janela; o tipo é reconhecido sozinho. " +
                        "Também troca a planilha de todos de uma vez e leva tudo para a equipe num .zip. " +
                        "Na setinha: Configurar IFC, Exportador e Forma."),
                    Button("Padrões", "Padroes", "MSP_PADROES", "Adicionar, editar e excluir padrões e levar tudo para a equipe num .zip."),
                    Button("Configurar IFC", "IfcConfigurar", "MSP_IFCCONFIG", "Classes IFC, propriedades, projeto, alinhamento e opções avançadas do pacote de configuração."),
                    Button("Exportador", "IfcAtualizar", "MSP_IFCEXTENSAO", "Versão da IFC 4.3 Extension da Autodesk, atualizações e a pasta dos arquivos de configuração do desenho."),
                    Button("Forma", "IfcSincronizar", "MSP_FORMA", "Autodesk Forma: entrar, sair, sincronizar a configuração IFC e a conta (Client ID, projeto e pasta)."))));

            // Marca sempre por último, no lado direito da aba.
            tab.Panels.Add(BrandPanel());
        }

        /// <summary>Exportar IFC, com a setinha para exportar só a seleção ou só os alinhamentos.</summary>
        static RibbonSplitButton ExportSplit()
        {
            var tudo = Button("Exportar\nIFC", "Passo3", "MSP_IFCEXPORT",
                "Exporta o desenho em IFC 4.3 com o exportador da Autodesk e completa o arquivo: alinhamento 3D, projeto, local e instalações.");
            var selecao = Button("Só a seleção", "IfcSelecao", "MSP_IFCEXPORTSEL", "Exporta só os objetos selecionados.");
            var alinhamentos = Button("Só os alinhamentos", "Alinhamento3D", "MSP_IFCALINHAMENTO", "IFC separado, só com os alinhamentos 3D.");
            var projeto = Button("Projeto e Local", "Passo2", "MSP_IFCPROJETO",
                "Preenche o IfcProject, o IfcSite, as instalações (a linha como IfcRailway e cada estação como IfcBuilding) " +
                "e o Pset MetroSP_Projeto. Os dados podem ser salvos como padrão e usados em outros desenhos, também no Revit.");
            foreach (var b in new[] { selecao, alinhamentos, projeto })
            {
                b.Size = RibbonItemSize.Standard;
                b.Orientation = System.Windows.Controls.Orientation.Horizontal;
                b.LargeImage = null;
            }
            var split = new RibbonSplitButton
            {
                Text = "Exportar\nIFC",
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                LargeImage = tudo.LargeImage,
                Image = tudo.Image,
                IsSplit = true,
                IsSynchronizedWithCurrentItem = false,
                ListButtonStyle = Autodesk.Private.Windows.RibbonListButtonStyle.SplitButton,
                ToolTip = tudo.ToolTip,
                // Clique na parte de cima: exporta o desenho todo (o mesmo comando do primeiro item).
                CommandParameter = tudo.CommandParameter,
                CommandHandler = new MacroHandler(),
            };
            split.Items.Add(tudo);
            split.Items.Add(selecao);
            split.Items.Add(alinhamentos);
            split.Items.Add(projeto);
            return split;
        }

        /// <summary>
        /// Botão grande com setinha: o clique roda o comando do botão principal (que dá o nome, o ícone e a dica) e a setinha
        /// mostra os itens, pequenos. O comando do clique também vai como primeiro item, com o nome numa linha só.
        /// </summary>
        static RibbonSplitButton SplitMenu(RibbonButton principal, params RibbonButton[] itens)
        {
            var split = new RibbonSplitButton
            {
                Text = principal.Text,
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                LargeImage = principal.LargeImage,
                Image = principal.Image,
                IsSplit = true,
                IsSynchronizedWithCurrentItem = false,
                ListButtonStyle = Autodesk.Private.Windows.RibbonListButtonStyle.SplitButton,
                ToolTip = principal.ToolTip,
                CommandParameter = principal.CommandParameter,
                CommandHandler = new MacroHandler(),
            };
            foreach (var b in itens) split.Items.Add(Pequeno(b));
            return split;
        }

        static RibbonButton Pequeno(RibbonButton b)
        {
            b.Size = RibbonItemSize.Standard;
            b.Orientation = System.Windows.Controls.Orientation.Horizontal;
            b.LargeImage = null;
            return b;
        }

        static RibbonPanel Panel(string title, params RibbonItem[] items)
        {
            var src = new RibbonPanelSource { Title = title };
            foreach (var i in items) src.Items.Add(i);
            return new RibbonPanel { Source = src };
        }

        /// <summary>Botão grande no padrão MetroSP: ícone em Resources/{icon}32.png e {icon}16.png.</summary>
        static RibbonButton Button(string text, string icon, string command, string tip)
        {
            return new RibbonButton
            {
                Text = text,
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                LargeImage = Icones.Carregar(icon + "32.png"),
                Image = Icones.Carregar(icon + "16.png"),
                CommandParameter = "\x03\x03_" + command + " ",
                CommandHandler = new MacroHandler(),
                ToolTip = new RibbonToolTip { Title = text.Replace("\n", " "), Content = tip, Command = command }
            };
        }



        /// <summary>Painel "Ação BIM · Metrô SP" com o selo azul; clicar abre o Sobre.</summary>
        static RibbonPanel BrandPanel()
        {
            ImageSource img = Icones.Carregar("marca_ribbon_botao.png");
            var logo = new RibbonButton
            {
                Id = MarcaId + "_Logos",
                LargeImage = img,
                Size = RibbonItemSize.Large,
                ShowImage = true,
                ShowText = false,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                ToolTip = "Desenvolvido pela Ação BIM para o Metrô de São Paulo. Clique para ver a versão e as informações do add-in.",
                CommandParameter = "\x03\x03_MSP_SOBRE ",
                CommandHandler = new MacroHandler()
            };
            if (img != null)
            {
                // O botão grande corta a imagem; com a largura e a altura dela o selo aparece inteiro.
                logo.Width = img.Width + 6;
                logo.MinWidth = img.Width + 6;
                logo.MinHeight = img.Height + 6;
            }
            var src = new RibbonPanelSource { Id = MarcaId, Title = "Ação BIM · Metrô SP" };
            src.Items.Add(logo);
            return new RibbonPanel { Source = src };
        }

        class MacroHandler : ICommand
        {
            public event EventHandler CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object parameter) => true;

            public void Execute(object parameter)
            {
                if (parameter is RibbonButton b && b.CommandParameter is string macro)
                    AcApp.DocumentManager.MdiActiveDocument?.SendStringToExecute(macro, true, false, true);
            }
        }
    }
}
