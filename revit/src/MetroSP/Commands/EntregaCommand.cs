using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MetroSP.Commands
{
    /// <summary>Abre a janela única da entrega: etapas em cima, pendências e ferramenta da etapa embaixo.</summary>
    [Transaction(TransactionMode.Manual)]
    public class EntregaCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            Painel.EntregaWindow.Abrir(commandData.Application);
    }
}
