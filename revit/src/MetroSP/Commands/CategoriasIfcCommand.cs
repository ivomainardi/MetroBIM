using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MetroSP.Ids;

namespace MetroSP.Commands
{
    /// <summary>Abre a janela Categorias IFC: categoria do Revit → classe IFC, uma tabela para IFC4 e outra para IFC 4.3.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class CategoriasIfcCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            Document doc = uiapp.ActiveUIDocument?.Document;
            if (doc != null && doc.IsFamilyDocument) doc = null;
            var w = new CategoriasIfcWindow(doc);
            new WindowInteropHelper(w).Owner = uiapp.MainWindowHandle;
            w.ShowDialog();
            return Result.Succeeded;
        }
    }
}
