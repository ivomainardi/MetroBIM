using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(MetroSP.Confronto.ConfrontoCommands))]

namespace MetroSP.Confronto
{
    public class ConfrontoCommands
    {
        /// <summary>Confrontar com planilha: compara property sets, nome, descrição e layer com uma planilha externa.</summary>
        [CommandMethod("MSP_CONFRONTAR", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void Confrontar()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            PromptSelectionResult sel = doc.Editor.SelectImplied();
            var ids = sel.Status == PromptStatus.OK ? sel.Value.GetObjectIds() : null;
            var w = new ConfrontarWindow(new FonteCivil3D(doc, ids));
            AcApp.ShowModalWindow(AcApp.MainWindow.Handle, w, false);
        }
    }
}
