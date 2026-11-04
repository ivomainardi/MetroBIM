using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MetroSP.Ids;

namespace MetroSP.Commands
{
    /// <summary>
    /// Abre a janela Arquivos IFC: setups (.json), property sets e mapeamentos (.txt) de cada classe, com as opções
    /// principais do setup e a conferência dos property sets no modelo aberto.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class ArquivosIfcCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            Document doc = uiapp.ActiveUIDocument?.Document;
            if (doc != null && doc.IsFamilyDocument) doc = null;
            var w = new ArquivosIfcWindow(doc);
            new WindowInteropHelper(w).Owner = uiapp.MainWindowHandle;
            w.ShowDialog();
            return Result.Succeeded;
        }
    }
}
