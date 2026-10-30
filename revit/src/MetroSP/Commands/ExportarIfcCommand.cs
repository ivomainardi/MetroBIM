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

            if (!setup.EhIfc4)
            {
                TaskDialog.Show("MetroSP", $"O setup {setup.Nome} exporta em {setup.DescricaoVersao}. A exportação MetroSP é em IFC4: " +
                    "escolha ou salve um setup IFC4 (por exemplo IFC 4 Reference View).");
                return Result.Cancelled;
            }

            // O Location (Manage › Location) vira o RefLatitude/RefLongitude do IfcSite: tem que ser o do Project Base Point.
            LocalizacaoIfc local = LocalizacaoIfc.Calcular(doc);
            if (local.Erro != null)
            {
                new TaskDialog("MetroSP")
                {
                    MainInstruction = "Não foi possível corrigir o Location",
                    MainContent = "A exportação foi cancelada porque " + local.Erro,
                    CommonButtons = TaskDialogCommonButtons.Close,
                }.Show();
                return Result.Cancelled;
            }
            bool atualizouLocation = false;
            if (local.PrecisaAtualizar)
            {
                var confirmar = new TaskDialog("MetroSP")
                {
                    MainInstruction = "Atualizar o Location antes de exportar",
                    MainContent = local.Resumo() + "\n\nA atualização do Location não altera o Survey Point, o Project Base Point nem as Shared Coordinates.",
                    FooterText = $"<a href=\"{local.LinkMapa}\">Ver o ponto no mapa</a>",
                    CommonButtons = TaskDialogCommonButtons.Cancel,
                };
                confirmar.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Atualizar o Location e exportar");
                if (confirmar.Show() != TaskDialogResult.CommandLink1) return Result.Cancelled;
                try
                {
                    local.Aplicar(doc);
                    atualizouLocation = true;
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("MetroSP", "Não foi possível atualizar o Location, e o IFC não foi exportado:\n" + ex.Message);
                    return Result.Failed;
                }
            }
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

            LocalizacaoIfc.Conferencia conferencia = local.ConferirIfc(arquivo);
            string log = local.GravarLog(doc, arquivo, setup.Nome, atualizouLocation, conferencia,
                uiapp.Application.VersionName, Ui.SobreWindow.Versao);
            string avisosSetup =
                (setup.SitePlacementDiferente == null ? "" : $"\nO setup usa a base {setup.SitePlacementDiferente}; o IFC saiu com Shared Coordinates.") +
                (setup.EpsgDoSetup == null ? "" : $"\nO setup tem o EPSG {setup.EpsgDoSetup}; o IFC saiu sem EPSG.");

            var fim = new TaskDialog("MetroSP")
            {
                MainInstruction = conferencia.TemProblema ? "IFC exportado, com pendências na conferência" : "IFC exportado",
                MainContent = $"{arquivo}\n\nSetup: {setup.Nome} ({setup.DescricaoVersao}) · Shared Coordinates · sem EPSG\n" +
                    "Property sets: " + (janela.PropertySetsEscolhido ?? "nenhum") +
                    (avisoPsets == null ? "" : "\n" + avisoPsets +
                        (setup.GravaGuid ? " Como o setup grava o GUID IFC, eles ficaram salvos no modelo." : " O modelo voltou a ficar como estava.")) +
                    "\n\nLocation: " + (atualizouLocation ? "atualizado pelo Project Base Point antes da exportação." : "já estava correto.") +
                    "\n" + conferencia + avisosSetup,
                ExpandedContent = local.Resumo() + (log == null ? "" : "\n\nRegistro: " + log),
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
