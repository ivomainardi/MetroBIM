using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MetroSP.Commands
{
    [Transaction(TransactionMode.ReadOnly)]
    public class SobreCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            string version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            string revit = commandData.Application.Application.VersionNumber;

            TaskDialog.Show("MetroSP",
                $"Ferramentas MetroSP para Revit\n\nVersão do add-in: {version}\nRevit: {revit}");

            return Result.Succeeded;
        }
    }
}
