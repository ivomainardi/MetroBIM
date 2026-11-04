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

            tab.Panels.Add(Panel("IFC 4.3",
                Button("Exportar\nIFC", "IfcExportar", "MSP_IFCEXPORT",
                    "Aplica a configuração IFC ativa (do Forma ou local), exporta o desenho inteiro (IFCINFRAEXPORT) e coloca o alinhamento 3D no mesmo IFC."),
                Button("Exportar\nseleção", "IfcSelecao", "MSP_IFCEXPORTSEL",
                    "Igual ao Exportar IFC, mas só com os objetos selecionados (IFCINFRAEXPORTSELECTED)."),
                Button("Só\nsincronizar", "IfcSincronizar", "MSP_IFCSYNC",
                    "Atualiza os JSON de configuração IFC a partir do Forma sem exportar."),
                Button("Alinhamento\n3D", "Alinhamento3D", "MSP_IFCALINHAMENTO",
                    "Exporta alinhamentos para IFC 4.3 com o perfil aplicado: eixo 3D com as cotas corretas e segmentos horizontais e verticais."),
                Button("Configurar\nIFC", "IfcConfigurar", "MSP_IFCCONFIG",
                    "Mapeamento de classes IFC, property sets, IfcProject/IfcSite/IfcBuilding e alinhamento. Importa e exporta os JSON.")));

            tab.Panels.Add(Panel("Forma",
                Button("Entrar", "Entrar", "MSP_FORMALOGIN",
                    "Entra na conta Autodesk e testa o acesso à pasta de configuração IFC."),
                Button("Sair", "Sair", "MSP_FORMALOGOUT",
                    "Apaga a sessão salva neste computador."),
                Button("Pasta de\nconfig", "ArquivosIfc", "MSP_IFCPASTA",
                    "Abre a pasta onde os JSON foram gravados (pasta do desenho)."),
                Button("Conta e\nForma", "Conta", "MSP_CONFIG",
                    "Edita MetroSP.settings.json (Client ID, projeto e pasta do Forma).")));

            tab.Panels.Add(Panel("Ajuda",
                Button("Sobre", "Sobre", "MSP_SOBRE", "Versão e configuração em uso.")));

            // Marca sempre por último, no lado direito da aba.
            tab.Panels.Add(BrandPanel());
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
