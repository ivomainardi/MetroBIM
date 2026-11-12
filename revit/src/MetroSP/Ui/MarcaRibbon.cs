using System;
using System.Linq;
using System.Windows.Controls;
using AW = Autodesk.Windows;

namespace MetroSP.Ui
{
    /// <summary>
    /// Painel com as logos da Ação BIM e do Metrô no fim (à direita) da aba MetroSP.
    /// A API oficial do Revit só aceita ícones de 32 px; aqui usamos a biblioteca de interface do
    /// próprio Revit (AdWindows). Se ela mudar numa versão futura, o painel simplesmente não aparece.
    /// </summary>
    internal static class MarcaRibbon
    {
        /// <summary>O clique na logo abre a janela Sobre (não usa a API do Revit, então pode rodar fora de um comando).</summary>
        private class AbrirSobre : System.Windows.Input.ICommand
        {
            public event EventHandler CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object parameter) => true;
            public void Execute(object parameter)
            {
                try { SobreWindow.Mostrar(); } catch { }
            }
        }

        private const string Id = "MetroSP_Marca";
        private static AW.RibbonButton _logo;

        /// <summary>
        /// Ícone quadrado (símbolo do Metrô sobre o azul), 32 px como os outros botões: o botão grande da Ribbon
        /// corta imagens mais largas. Aparece igual nos temas claro e escuro do Revit.
        /// </summary>
        private static System.Windows.Media.ImageSource Imagem() => App.LoadIcon("MarcaSobre32.png");

        /// <summary>Chamado quando o usuário troca o tema do Revit.</summary>
        public static void AtualizarTema()
        {
            try
            {
                if (_logo == null) return;
                System.Windows.Media.ImageSource img = Imagem();
                _logo.LargeImage = img;
            }
            catch { }
        }

        public static void Adicionar(string nomeAba)
        {
            try
            {
                AW.RibbonControl ribbon = AW.ComponentManager.Ribbon;
                AW.RibbonTab aba = ribbon?.Tabs.FirstOrDefault(t => t.Id == nomeAba || t.Title == nomeAba);
                if (aba == null || aba.Panels.Any(p => p.Source?.Id == Id)) return;

                var logo = new AW.RibbonButton
                {
                    Id = Id + "_Logos",
                    LargeImage = Imagem(),
                    Size = AW.RibbonItemSize.Large,
                    ShowImage = true,
                    ShowText = true,
                    Text = "Sobre",
                    Orientation = Orientation.Vertical,
                    ToolTip = "MetroSP · desenvolvido pela Ação BIM para o Metrô de São Paulo. Clique para ver a versão e as informações do add-in.",
                    IsCheckable = false,
                    CommandHandler = new AbrirSobre(),
                };
                _logo = logo;
                var fonte = new AW.RibbonPanelSource { Id = Id, Title = "Ação BIM · Metrô SP" };
                fonte.Items.Add(logo);
                aba.Panels.Add(new AW.RibbonPanel { Source = fonte }); // último painel = lado direito
            }
            catch
            {
                // Só decoração: qualquer falha aqui não pode impedir o add-in de carregar.
            }
        }
    }
}
