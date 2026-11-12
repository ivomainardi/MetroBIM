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

            // Só a Entrega Metrô é numerada: são os passos 1-2-3. O resto fica sem número, como no plugin do Revit.
            tab.Panels.Add(Panel("Entrega Metrô",
                Button("Entrega\npasso a passo", "Passo1", "MSP_ENTREGA",
                    "Passo 1, por onde começar. Janela única da entrega: as etapas em cima, com setinhas (verde = pronta, laranja = pendências), " +
                    "e embaixo o que falta em cada etapa, com os botões que resolvem e a ferramenta da etapa (Exportador, Property sets, " +
                    "Classes IFC, Dados, Classificação, Alinhamentos, Local e exportar)."),
                Button("Projeto e\nLocal", "Passo2", "MSP_IFCPROJETO",
                    "Passo 2. Preenche o IfcProject, o IfcSite, as instalações (a linha como IfcRailway e cada estação como IfcBuilding) " +
                    "e o Pset MetroSP_Projeto. Os dados podem ser salvos como padrão e usados em outros desenhos, também no Revit."),
                ExportSplit()));

            tab.Panels.Add(Panel("Acompanhar",
                Button("Painel\nMetroSP", "Conferir", "MSP_PAINEL",
                    "Abre ao lado do desenho o painel com as etapas da entrega e o semáforo de cada uma. " +
                    "Clique numa etapa para abrir a Entrega passo a passo nela.")));

            tab.Panels.Add(Panel("Classificação",
                Button("Verificar", "ClassVerificar", "MSP_CLASSVERIFICAR",
                    "Confere as classificações dos objetos com as planilhas do padrão. Cada linha tem a ação para corrigir.")));

            tab.Panels.Add(Panel("Dados do modelo",
                Button("Property\nsets", "PropertySets", "MSP_PSETS",
                    "Monta os property sets do Civil 3D (nome, propriedades, tipos e objetos), importa de planilha, leva ao IFC e anexa aos objetos."),
                Button("Confrontar\nplanilha", "ConfrontarPlanilha", "MSP_CONFRONTAR",
                    "Compara property sets, nome, descrição e layer dos objetos com uma planilha externa e resolve as diferenças."),
                Button("Corrigir\nescrita", "CorrigirEscrita", "MSP_ESCRITA",
                    "Procura em property sets, nome e descrição espaços sobrando ou errados (duplos, nas pontas, na pontuação, entre número " +
                    "e unidade), acentuação e caracteres estranhos. Mostra o valor atual e a sugestão para corrigir ou ignorar.")));

            // O que o coordenador BIM prepara uma vez e distribui: botões pequenos, no fim da aba.
            tab.Panels.Add(Panel("Configurar padrões",
                Stack(
                    Small("Padrões e planilhas", "Padroes", "MSP_PADROES",
                        "Todos os padrões salvos (classificação, confronto, dados do projeto, exportação IFC) e as planilhas que eles usam: editar, trocar planilha e levar para a equipe num .zip."),
                    Small("Configurar IFC", "IfcConfigurar", "MSP_IFCCONFIG", "Classes IFC, propriedades, projeto, alinhamento e opções avançadas do pacote de configuração."),
                    Small("Exportador", "IfcAtualizar", "MSP_IFCEXTENSAO", "Versão da IFC 4.3 Extension da Autodesk, atualizações e a pasta dos arquivos de configuração do desenho.")),
                Stack(
                    Small("Forma", "IfcSincronizar", "MSP_FORMA", "Autodesk Forma: entrar, sair, sincronizar a configuração IFC e a conta (Client ID, projeto e pasta)."))));

            // Marca sempre por último, no lado direito da aba.
            tab.Panels.Add(BrandPanel());
        }

        /// <summary>Passo 3: Exportar IFC, com a setinha para exportar só a seleção ou só os alinhamentos.</summary>
        static RibbonSplitButton ExportSplit()
        {
            var tudo = Button("Exportar\nIFC", "Passo3", "MSP_IFCEXPORT",
                "Passo 3. Exporta o desenho em IFC 4.3 com o exportador da Autodesk e completa o arquivo: alinhamento 3D, projeto, local e instalações.");
            var selecao = Button("Só a seleção", "IfcSelecao", "MSP_IFCEXPORTSEL", "Exporta só os objetos selecionados.");
            var alinhamentos = Button("Só os alinhamentos", "Alinhamento3D", "MSP_IFCALINHAMENTO", "IFC separado, só com os alinhamentos 3D.");
            foreach (var b in new[] { selecao, alinhamentos })
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
            return split;
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

        /// <summary>Botão pequeno (16 px, texto ao lado), para empilhar três por coluna.</summary>
        static RibbonButton Small(string text, string icon, string command, string tip)
        {
            var b = Button(text, icon, command, tip);
            b.Size = RibbonItemSize.Standard;
            b.Orientation = System.Windows.Controls.Orientation.Horizontal;
            b.LargeImage = null;
            return b;
        }

        /// <summary>Coluna de botões pequenos, um por linha.</summary>
        static RibbonRowPanel Stack(params RibbonButton[] buttons)
        {
            var row = new RibbonRowPanel();
            for (int i = 0; i < buttons.Length; i++)
            {
                if (i > 0) row.Items.Add(new RibbonRowBreak());
                row.Items.Add(buttons[i]);
            }
            return row;
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
