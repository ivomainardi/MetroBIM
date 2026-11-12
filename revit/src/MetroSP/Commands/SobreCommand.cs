using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MetroSP.Ui;

namespace MetroSP.Commands
{
    [Transaction(TransactionMode.ReadOnly)]
    public class SobreCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            SobreWindow.Mostrar();
            return Result.Succeeded;
        }
    }
}
