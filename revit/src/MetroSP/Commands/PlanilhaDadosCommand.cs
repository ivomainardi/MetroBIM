using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MetroSP.Commands
{
    /// <summary>Abre uma janela modal das ferramentas de dados (as transações rodam no contexto do comando).</summary>
    internal static class JanelaDados
    {
        public static Result Abrir(ExternalCommandData data, System.Func<UIDocument, Window> criar)
        {
            UIApplication uiapp = data.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null || uidoc.Document.IsFamilyDocument)
            {
                TaskDialog.Show("MetroSP", "Abra um projeto (.rvt).");
                return Result.Cancelled;
            }
            Window janela = criar(uidoc);
            new WindowInteropHelper(janela).Owner = uiapp.MainWindowHandle;
            janela.ShowDialog();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class PlanilhaDadosCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            JanelaDados.Abrir(commandData, u => new Dados.PlanilhaDadosWindow(u));
    }
}

namespace MetroSP.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class EdicaoMassaCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            JanelaDados.Abrir(commandData, u => new Dados.EdicaoMassaWindow(u));
    }

    [Transaction(TransactionMode.Manual)]
    public class FiltroCorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            JanelaDados.Abrir(commandData, u => new Dados.FiltroCorWindow(u));
    }

    [Transaction(TransactionMode.Manual)]
    public class RenomearFamiliasCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            JanelaDados.Abrir(commandData, u => new Dados.RenomearFamiliasWindow(u));
    }

    [Transaction(TransactionMode.Manual)]
    public class GerenciarParametrosCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            JanelaDados.Abrir(commandData, u => new Dados.GerenciarParametrosWindow(u));
    }
}

namespace MetroSP.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class ConfrontarPlanilhaCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            JanelaDados.Abrir(commandData, u => new Confronto.ConfrontarWindow(new Confronto.FonteRevit(u)));
    }
}
