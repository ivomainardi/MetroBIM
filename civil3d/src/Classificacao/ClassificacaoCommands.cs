using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(MetroSP.Classificacao.ClassificacaoCommands))]

namespace MetroSP.Classificacao
{
    public class ClassificacaoCommands
    {
        /// <summary>Confere as classificações (property sets) com as planilhas do padrão. Usa a seleção atual, se houver.</summary>
        [CommandMethod("MSP_CLASSVERIFICAR", CommandFlags.UsePickSet | CommandFlags.Redraw)]
        public void Verificar()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            try
            {
                List<ObjectId> selecao = null;
                PromptSelectionResult r = doc.Editor.SelectImplied();
                if (r.Status == PromptStatus.OK && r.Value != null) selecao = r.Value.GetObjectIds().ToList();
                AcApp.ShowModalWindow(AcApp.MainWindow.Handle, new VerificarClassificacaoWindow(doc, selecao), false);
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage("\nMetroSP: ERRO - " + ex.Message);
            }
        }
    }
}
