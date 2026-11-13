using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MetroSP.Coordenacao;

namespace MetroSP.Commands
{
    /// <summary>
    /// Arquivo de coordenação: o coordenador BIM grava tudo o que ajustou (setup e fase, categorias IFC, classes por família,
    /// property sets, mapeamento e Projeto e Local) num .coord-metrosp, com um ou mais perfis, para enviar ao projetista.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CoordenacaoCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            Document doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null || doc.IsFamilyDocument)
            {
                TaskDialog.Show("MetroSP", "Abra um projeto (.rvt) para montar o arquivo de coordenação.");
                return Result.Cancelled;
            }
            CoordenacaoRevit.Ativar(doc);
            ExportarAlteracoesWindow.Abrir(uiapp.MainWindowHandle, doc);
            return Result.Succeeded;
        }
    }
}
