using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(MetroSP.RibbonApp))]

namespace MetroSP
{
    /// <summary>Cria a aba "MetroSP" na ribbon quando o plugin carrega.</summary>
    public class RibbonApp : IExtensionApplication
    {
        const string TabId = "METROSP_TAB";

        // Azul institucional aproximado; troque pelo do manual de marca.
        static readonly Color Azul = Color.FromRgb(0x00, 0x2D, 0x72);
        static readonly Color Cinza = Color.FromRgb(0x5A, 0x63, 0x70);
        static readonly Color Laranja = Color.FromRgb(0xD9, 0x6B, 0x00);

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
                Button("Exportar\nIFC", "IFC", Azul, "MSP_IFCEXPORT",
                    "Aplica a configuração IFC ativa (do Forma ou local), exporta o desenho inteiro (IFCINFRAEXPORT) e coloca o alinhamento 3D no mesmo IFC."),
                Button("Exportar\nseleção", "SEL", Azul, "MSP_IFCEXPORTSEL",
                    "Baixa do Forma a configuração IFC e exporta só os objetos selecionados (IFCINFRAEXPORTSELECTED)."),
                Button("Só\nsincronizar", "SYN", Azul, "MSP_IFCSYNC",
                    "Atualiza os JSON de configuração IFC a partir do Forma sem exportar."),
                Button("Alinhamento\n3D", "AL3", Azul, "MSP_IFCALINHAMENTO",
                    "Exporta alinhamentos para IFC 4.3 com o perfil aplicado: eixo 3D com as cotas corretas e segmentos horizontais e verticais."),
                Button("Configurar\nIFC", "CFG", Laranja, "MSP_IFCCONFIG",
                    "Mapeamento de classes IFC, property sets, IfcProject/IfcSite/IfcBuilding e alinhamento. Importa e exporta os JSON.")));

            tab.Panels.Add(Panel("Forma",
                Button("Entrar", "IN", Cinza, "MSP_FORMALOGIN",
                    "Entra na conta Autodesk e testa o acesso à pasta de configuração IFC."),
                Button("Sair", "OUT", Cinza, "MSP_FORMALOGOUT",
                    "Apaga a sessão salva neste computador."),
                Button("Pasta de\nconfig", "DIR", Cinza, "MSP_IFCPASTA",
                    "Abre a pasta onde os JSON foram gravados (pasta do desenho)."),
                Button("Conta e\nForma", "SET", Cinza, "MSP_CONFIG",
                    "Edita MetroSP.settings.json (Client ID, projeto e pasta do Forma).")));

            tab.Panels.Add(Panel("Ajuda",
                Button("Sobre", "?", Cinza, "MSP_SOBRE", "Versão e configuração em uso.")));
        }

        static RibbonPanel Panel(string title, params RibbonItem[] items)
        {
            var src = new RibbonPanelSource { Title = title };
            foreach (var i in items) src.Items.Add(i);
            return new RibbonPanel { Source = src };
        }

        static RibbonButton Button(string text, string iconText, Color color, string command, string tip)
        {
            return new RibbonButton
            {
                Text = text,
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                LargeImage = Icon(iconText, color, 32),
                Image = Icon(iconText, color, 16),
                CommandParameter = "\x03\x03_" + command + " ",
                CommandHandler = new MacroHandler(),
                ToolTip = new RibbonToolTip { Title = text.Replace("\n", " "), Content = tip, Command = command }
            };
        }

        // Ícones gerados em tempo de execução, para o esqueleto não depender de PNGs.
        // Para ícones definitivos, troque por BitmapImage carregando PNG 16x16 e 32x32.
        static ImageSource Icon(string text, Color color, int size)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRoundedRectangle(new SolidColorBrush(color), null, new Rect(0, 0, size, size), size * 0.15, size * 0.15);
                var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    size * (text.Length > 2 ? 0.34 : 0.45), Brushes.White, 1.0);
                dc.DrawText(ft, new Point((size - ft.Width) / 2, (size - ft.Height) / 2));
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
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
