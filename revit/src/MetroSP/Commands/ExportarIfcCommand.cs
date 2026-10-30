using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MetroSP.Ids;

namespace MetroSP.Commands
{
    /// <summary>
    /// Exporta o IFC com um setup .json e o .txt de property sets guardados numa pasta local ou no Autodesk Forma.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ExportarIfcCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            Document doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null || doc.IsFamilyDocument)
            {
                TaskDialog.Show("MetroSP", "Abra um projeto (.rvt) para exportar o IFC.");
                return Result.Cancelled;
            }

            IfcExportSettings settings = IfcExportSettings.Load();

            // Pasta do Forma online: baixa o que mudou lá desde a última vez (sem rede, vale a última cópia).
            string resultadoForma = null;
            if (FormaOnline.Conectado && FormaOnline.PastaEscolhida() != null)
                using (var p = Ui.Progresso.Abrir("Forma online"))
                {
                    p.Etapa("Conferindo atualizações das configurações de IFC no Forma");
                    resultadoForma = FormaOnline.Sincronizar().ToString();
                }

            var janela = new ExportarIfcWindow(doc, settings, resultadoForma);
            new WindowInteropHelper(janela).Owner = uiapp.MainWindowHandle;
            bool ok = janela.ShowDialog() == true;
            settings.Save();
            if (!ok) return Result.Cancelled;

            IfcExportSetup setup = janela.Setup;
            string arquivo = Path.Combine(janela.PastaSaida, janela.NomeArquivo + ".ifc");
            bool exportou;
            string avisoPsets = null;
            try
            {
                IFCExportOptions opcoes = setup.CriarOpcoes(doc, janela.PropertySetsEscolhido, janela.MapeamentoEscolhido, janela.Vista);

                // O exportador precisa de uma transação aberta; ela é desfeita no fim,
                // a não ser que o setup grave o GUID IFC nos elementos (como faz a janela do Revit).
                using (var t = new Transaction(doc, "Exportar IFC"))
                {
                    t.Start();
                    FailureHandlingOptions falhas = t.GetFailureHandlingOptions();
                    falhas.SetClearAfterRollback(true);
                    t.SetFailureHandlingOptions(falhas);

                    // Revit 2027+: o exportador lê os psets do modelo, não do .txt.
                    if (!string.IsNullOrEmpty(janela.PropertySetsEscolhido))
                        avisoPsets = PsetsNoModelo.Carregar(doc, janela.PropertySetsEscolhido);

                    using (var progresso = Ui.Progresso.Abrir("Exportar IFC"))
                    {
                        progresso.Etapa($"Exportando {janela.NomeArquivo}.ifc com o setup {setup.Nome}");
                        progresso.Item("O exportador do Revit não informa o andamento; em modelos grandes pode levar alguns minutos.");
                        exportou = doc.Export(janela.PastaSaida, janela.NomeArquivo + ".ifc", opcoes);
                    }

                    if (exportou && setup.GravaGuid) t.Commit();
                    else t.RollBack();
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("MetroSP", "Erro ao exportar o IFC:\n" + ex.Message);
                return Result.Failed;
            }

            if (!exportou || !File.Exists(arquivo))
            {
                TaskDialog.Show("MetroSP", "O Revit não conseguiu exportar o IFC. Confira o setup e tente pela janela de exportação do Revit para ver a mensagem do exportador.");
                return Result.Failed;
            }

            var fim = new TaskDialog("MetroSP")
            {
                MainInstruction = "IFC exportado",
                MainContent = $"{arquivo}\n\nSetup: {setup.Nome} ({setup.DescricaoVersao})\n" +
                    "Property sets: " + (janela.PropertySetsEscolhido ?? "nenhum") +
                    (avisoPsets == null ? "" : "\n" + avisoPsets +
                        (setup.GravaGuid ? " Como o setup grava o GUID IFC, eles ficaram salvos no modelo." : " O modelo voltou a ficar como estava.")),
                CommonButtons = TaskDialogCommonButtons.Close,
            };
            fim.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Abrir a pasta");
            if (fim.Show() == TaskDialogResult.CommandLink1)
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{arquivo}\"") { UseShellExecute = true }); }
                catch { /* sem Explorer: ignora */ }
            }
            return Result.Succeeded;
        }
    }
}
