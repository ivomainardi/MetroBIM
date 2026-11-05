using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using MetroSP.Ui;
using DB = Autodesk.Revit.DB;

namespace MetroSP.Ids
{
    /// <summary>
    /// Etapa 1 da exportação: calcula a lat/long pelo Project Base Point, mostra os dados do IfcSite e do IfcBuilding
    /// e grava nos campos padrão do Revit o que estiver diferente. A exportação (etapa 2) só confere.
    /// </summary>
    public class LocalizacaoWindow : Window
    {
        private readonly LocalizacaoPainel _painel;
        private readonly TextBlock _resumo;

        public LocalizacaoIfc.Escolha Escolha { get; private set; }

        public LocalizacaoWindow(LocalizacaoIfc local)
        {
            Title = "MetroSP - Localização IFC";
            Width = 680;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            MinWidth = 540;

            _painel = new LocalizacaoPainel(local);
            var gravar = new Button { Content = "Gravar no Revit", IsDefault = true, Style = Theme.PrimaryButton, IsEnabled = local.Erro == null };
            gravar.ToolTip = "A atualização do Location não altera o Survey Point, o Project Base Point nem as Shared Coordinates.";
            gravar.Click += (s, e) => Confirmar();
            var cancelar = new Button { Content = "Cancelar", IsCancel = true };
            _resumo = new TextBlock { FontSize = 11.5 };
            Theme.Apply(this, "Localização IFC", "Location pelo Project Base Point e nomes do IfcSite e do IfcBuilding",
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
