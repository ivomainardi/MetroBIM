using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MetroSP.Ids;

namespace MetroSP.Commands
{
    /// <summary>
    /// Etapa 1 da exportação IFC: calcula a lat/long pelo Project Base Point (PTL do Metrô) e grava em Manage › Location,
    /// junto com o nome do IfcSite e os dados do IfcBuilding. Não exporta.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class LocalizacaoIfcCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            Document doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null || doc.IsFamilyDocument)
            {
                TaskDialog.Show("MetroSP", "Abra um projeto (.rvt) para conferir a localização.");
                return Result.Cancelled;
            }

            List<string> mudou;
            try { mudou = LocalizacaoWindow.Executar(doc, LocalizacaoIfc.Calcular(doc), null, uiapp.MainWindowHandle); }
            catch (Exception ex)
            {
                TaskDialog.Show("MetroSP", "Não foi possível gravar a localização no Revit:\n" + ex.Message);
                return Result.Failed;
            }
            if (mudou == null) return Result.Cancelled;

            bool recusado = mudou.Exists(m => m.StartsWith("ATENÇÃO", StringComparison.Ordinal));
            TaskDialog.Show("MetroSP", mudou.Count == 0
                ? "Os campos do Revit já estavam como confirmado. Nada foi alterado."
                : "Gravado no Revit:\n- " + string.Join("\n- ", mudou) + (recusado ? "" : "\n\nAgora é só usar o Exportar IFC."));
            return Result.Succeeded;
        }
    }
}
