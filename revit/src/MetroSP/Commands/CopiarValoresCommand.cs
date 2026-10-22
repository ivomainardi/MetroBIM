using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MetroSP.Parametros;

namespace MetroSP.Commands
{
    /// <summary>Copia valores de um parâmetro para outro, com análise e conversão entre tipos de dado.</summary>
    [Transaction(TransactionMode.Manual)]
    public class CopiarValoresCommand : IExternalCommand
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
            var janela = new CopiarValoresWindow(uidoc);
            new WindowInteropHelper(janela).Owner = uiapp.MainWindowHandle;
            janela.ShowDialog();
            return Result.Succeeded;
        }
    }
}
