using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MetroSP.Classificacao;

namespace MetroSP.Commands
{
    /// <summary>Abre a janela (não modal) para escolher um item da planilha e aplicá-lo à seleção.</summary>
    [Transaction(TransactionMode.Manual)]
    public class AplicarClassificacaoCommand : IExternalCommand
    {
        private static AplicarClassificacaoWindow _janela;
        private static bool _assinado;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null || uidoc.Document.IsFamilyDocument)
            {
                TaskDialog.Show("MetroSP", "Abra um projeto (.rvt).");
                return Result.Cancelled;
            }

            if (_janela != null)
            {
                _janela.Activate();
                return Result.Succeeded;
            }

            // Assina uma vez por sessão; sem janela aberta o evento não faz nada.
            if (!_assinado)
            {
                uiapp.SelectionChanged += (s, e) => _janela?.SelecaoMudou(s as UIApplication ?? uiapp);
                _assinado = true;
            }

            _janela = new AplicarClassificacaoWindow(ClassSettings.Abrir(), uidoc);
            new WindowInteropHelper(_janela).Owner = uiapp.MainWindowHandle;
            _janela.Closed += (s, e) => _janela = null;
            _janela.Show();
            _janela.SelecaoMudou(uiapp);
            return Result.Succeeded;
        }
    }
}
