using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MetroSP.Commands
{
    /// <summary>
    /// Comando de exemplo: conta elementos por categoria na seleção atual,
    /// ou na vista ativa quando nada está selecionado.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class ContarElementosCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null)
            {
                message = "Abra um projeto antes de usar este comando.";
                return Result.Failed;
            }

            Document doc = uidoc.Document;
            ICollection<ElementId> selection = uidoc.Selection.GetElementIds();

            IEnumerable<Element> source = selection.Count > 0
                ? selection.Select(doc.GetElement)
                : new FilteredElementCollector(doc, doc.ActiveView.Id).WhereElementIsNotElementType();

            var counts = source
                .Where(e => e?.Category != null)
                .GroupBy(e => e.Category.Name)
                .OrderByDescending(g => g.Count())
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine(selection.Count > 0 ? "Elementos selecionados:" : $"Elementos na vista \"{doc.ActiveView.Name}\":");
            sb.AppendLine();
            foreach (var group in counts)
                sb.AppendLine($"{group.Key}: {group.Count()}");
            sb.AppendLine();
            sb.AppendLine($"Total: {counts.Sum(g => g.Count())}");

            TaskDialog.Show("MetroSP - Contar Elementos", sb.ToString());
            return Result.Succeeded;
        }
    }
}
