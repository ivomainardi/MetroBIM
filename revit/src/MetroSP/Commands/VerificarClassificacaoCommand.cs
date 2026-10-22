using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MetroSP.Classificacao;

namespace MetroSP.Commands
{
    /// <summary>Verifica se a classificação preenchida confere com a planilha e propõe correções.</summary>
    [Transaction(TransactionMode.Manual)]
    public class VerificarClassificacaoCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null || uidoc.Document.IsFamilyDocument)
            {
                TaskDialog.Show("MetroSP", "Abra um projeto (.rvt).");
                return Result.Cancelled;
            }

            // Modal: as transações abertas pela janela rodam no contexto deste comando.
            var janela = new VerificarClassificacaoWindow(uidoc, ClassSettings.Abrir());
            new WindowInteropHelper(janela).Owner = uiapp.MainWindowHandle;
            janela.ShowDialog();
            return Result.Succeeded;
        }
    }
}
