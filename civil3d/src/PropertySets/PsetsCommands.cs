using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(MetroSP.PropertySets.PsetsCommands))]

namespace MetroSP.PropertySets
{
    public class PsetsCommands
    {
        /// <summary>Monta os property sets do Civil 3D (Property Set Definitions) e anexa aos objetos. Usa a seleção atual, se houver.</summary>
        [CommandMethod("MSP_PSETS", CommandFlags.UsePickSet | CommandFlags.Redraw)]
        public void Montar()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            try
            {
                List<ObjectId> selecao = null;
                PromptSelectionResult r = doc.Editor.SelectImplied();
                if (r.Status == PromptStatus.OK && r.Value != null) selecao = r.Value.GetObjectIds().ToList();
                AcApp.ShowModalWindow(AcApp.MainWindow.Handle, new MontarPsetsWindow(doc, selecao), false);
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage("\nMetroSP: ERRO - " + ex.Message);
            }
        }
    }
}
