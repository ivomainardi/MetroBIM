using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using MetroSP.Ui;
using DB = Autodesk.Revit.DB;

namespace MetroSP.Ids
{
    /// <summary>
    /// Etapa 1 da exportação: dados do IfcProject, do IfcSite (com a lat/long calculada pelo Project Base Point) e do
    /// IfcBuilding, gravados nos campos padrão do Revit o que estiver diferente. A exportação só confere e completa o IFC.
    /// </summary>
    public class LocalizacaoWindow : Window
    {
        private readonly LocalizacaoPainel _painel;
        private readonly TextBlock _resumo;

        public LocalizacaoIfc.Escolha Escolha { get; private set; }

        public LocalizacaoWindow(LocalizacaoIfc local)
        {
            Title = "MetroSP - Projeto e localização IFC";
            Width = 960;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            MinWidth = 720;

            _painel = new LocalizacaoPainel(local);
            var gravar = new Button { Content = "Gravar no Revit", IsDefault = true, Style = Theme.PrimaryButton };
            gravar.ToolTip = "Grava nos campos do Revit que o exportador IFC lê. A atualização do Location não altera o Survey Point, " +
                "o Project Base Point nem as Shared Coordinates.";
            gravar.Click += (s, e) => Confirmar();
            var cancelar = new Button { Content = "Cancelar", IsCancel = true };
            _resumo = new TextBlock { FontSize = 11.5 };
            Theme.Apply(this, "Projeto e localização IFC", "IfcProject, IfcSite (com o Location pelo Project Base Point) e IfcBuilding",
                _painel, Theme.Rodape(_resumo, cancelar, gravar));
            _painel.Mudou += AtualizarResumo;
            AtualizarResumo();
        }

        private void AtualizarResumo()
        {
            int n = _painel.CamposAlterados();
            Theme.MudarResumo(_resumo, _painel.Avisos(), n == 0 ? "Nada a alterar no Revit" : $"{n} campo(s) serão gravados no Revit");
        }

        private void Confirmar()
        {
            LocalizacaoIfc.Escolha e = _painel.Ler(out string erro);
            if (e == null)
            {
                MessageBox.Show(this, erro, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            double? desvio = _painel.DesvioDoPbp();
            if (desvio > LocalizacaoIfc.ToleranciaLocation &&
                MessageBox.Show(this, $"A latitude/longitude digitada está a {desvio.Value:0.00} m do ponto calculado pelo Project Base Point. Gravar assim mesmo?",
                    Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            Escolha = e;
            DialogResult = true;
        }

        /// <summary>
        /// Abre a janela e grava a escolha no Revit. Devolve o que mudou (vazio se nada), ou null se o usuário cancelou.
        /// Precisa rodar dentro de um comando do Revit (abre transação).
        /// </summary>
        public static List<string> Executar(DB.Document doc, LocalizacaoIfc local, Window dono, System.IntPtr donoRevit)
        {
            var janela = new LocalizacaoWindow(local);
            if (dono != null) janela.Owner = dono;
            else new WindowInteropHelper(janela).Owner = donoRevit;
            if (janela.ShowDialog() != true) return null;
            return local.Aplicar(doc, janela.Escolha);
        }
    }
}
