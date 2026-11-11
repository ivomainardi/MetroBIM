using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MetroSP.Commands
{
    /// <summary>Mostra o Painel MetroSP ao lado do modelo e confere o modelo ativo.</summary>
    [Transaction(TransactionMode.Manual)]
    public class PainelMetroSPCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            Painel.PainelMetroSP.MostrarEConferir(commandData.Application);
    }
}
