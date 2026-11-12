using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MetroSP.Parametros;

namespace MetroSP.Commands
{
    /// <summary>
    /// Renomeia um parâmetro compartilhado mantendo o GUID, em projeto (.rvt) ou família (.rfa).
    /// Ver SharedParameterRenamer. Opcionalmente, também renomeia a linha correspondente no .txt.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class RenomearParametroCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            Document doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null)
            {
                TaskDialog.Show("MetroSP", "Abra um projeto ou uma família.");
                return Result.Cancelled;
            }

            List<ParametroItem> itens = Itens(doc);
            if (itens.Count == 0)
            {
                TaskDialog.Show("MetroSP", doc.IsFamilyDocument
                    ? "Esta família não tem parâmetros compartilhados."
                    : "Este projeto não tem parâmetros compartilhados vinculados a categorias.");
                return Result.Cancelled;
            }

            return RenomearInterativo(uiapp, doc, itens, null, ref message);
        }

        /// <summary>Parâmetros compartilhados renomeáveis: os da família, ou os vinculados ao projeto.</summary>
        public static List<ParametroItem> Itens(Document doc)
        {
            if (doc.IsFamilyDocument)
            {
                return doc.FamilyManager.GetParameters()
                    .Where(p => p.IsShared)
                    .Select(p => new ParametroItem { Nome = p.Definition.Name, Guid = p.GUID, Tag = p })
                    .ToList();
            }
            // Vinculados ao projeto e também os que só existem nas famílias (renomeados via famílias).
            return new FilteredElementCollector(doc).OfClass(typeof(SharedParameterElement)).Cast<SharedParameterElement>()
                .Select(spe => new ParametroItem { Nome = spe.GetDefinition().Name, Guid = spe.GuidValue, Tag = spe })
                .ToList();
        }

        /// <summary>
        /// Abre a janela de renomear e executa. Usado também pela janela "Listar Compartilhados",
        /// que pode sugerir o novo nome e o .txt de origem do parâmetro.
        /// </summary>
        public static Result RenomearInterativo(UIApplication uiapp, Document doc, List<ParametroItem> itens,
            ParametroItem selecionado, ref string message, string novoNomeSugerido = null,
            string arquivoSugerido = null, bool? atualizarTxt = null)
        {
            Application app = uiapp.Application;
            string arquivoUsuario = File.Exists(arquivoSugerido) ? arquivoSugerido : app.SharedParametersFilename;
            if (!File.Exists(arquivoUsuario)) arquivoUsuario = null;

            var janela = new RenomearParametroWindow(itens, arquivoUsuario, selecionado, novoNomeSugerido, atualizarTxt, projeto: !doc.IsFamilyDocument);
            new WindowInteropHelper(janela).Owner = uiapp.MainWindowHandle;
            if (janela.ShowDialog() != true) return Result.Cancelled;

            ParametroItem item = janela.Selecionado;
            string novoNome = janela.NovoNome;
            arquivoUsuario = janela.ArquivoCompartilhado;

            if (janela.AtualizarArquivo && SharedParameterFile.NameTakenByOtherGuid(arquivoUsuario, item.Guid, novoNome))
            {
                TaskDialog.Show("MetroSP", $"O arquivo de parâmetros compartilhados já tem outro parâmetro (GUID diferente) chamado \"{novoNome}\".");
                return Result.Cancelled;
            }

            var avisos = new List<string>();
            try
            {
                if (item.Tag is FamilyParameter fp)
                {
                    SharedParameterRenamer.RenameInFamily(app, doc, fp, novoNome);
                }
                else
                {
                    if (new FilteredElementCollector(doc).OfClass(typeof(SharedParameterElement)).Cast<SharedParameterElement>()
                        .Any(s => s.GuidValue != item.Guid && s.GetDefinition().Name == novoNome))
                    {
                        TaskDialog.Show("MetroSP", $"Já existe outro parâmetro compartilhado chamado \"{novoNome}\" no projeto.");
                        return Result.Cancelled;
                    }

                    var spe = (SharedParameterElement)item.Tag;
                    ProjectRenamePlan plan = SharedParameterRenamer.PlanProjectRename(doc, spe);
                    if (plan.Binding == null && !janela.RenomearFamilias)
                    {
                        TaskDialog.Show("MetroSP", "Este parâmetro não está vinculado ao projeto: ele vem só de famílias. " +
                            "Marque \"Renomear também em todas as famílias carregadas\".");
                        return Result.Cancelled;
                    }
                    if (!janela.RenomearFamilias)
                    {
                        if (!Confirmar(plan, item.Nome, novoNome)) return Result.Cancelled;
                        using (var progresso = Ui.Progresso.Abrir("Renomear parâmetro compartilhado"))
                        {
                            progresso.Etapa($"Recriando \"{novoNome}\" e restaurando {plan.ElementosComValor} valores");
                            avisos = SharedParameterRenamer.RenameInProject(app, doc, plan, novoNome);
                        }
                    }
                    else
                    {
                        // Projeto e famílias numa única operação: o projeto só mostra o nome novo se o registro
                        // antigo do GUID for apagado e as famílias forem recarregadas já renomeadas.
                        if (plan.Binding != null && !Confirmar(plan, item.Nome, novoNome)) return Result.Cancelled;
                        Guid guid = item.Guid;
                        int familias = janela.IncluirAninhadas ? RenomearEmFamilias.Editaveis(doc).Count : RenomearEmFamilias.ComParametro(doc, guid).Count;
                        var td = new TaskDialog("MetroSP - Renomear nas famílias")
                        {
                            MainInstruction = $"Renomear \"{item.Nome}\" para \"{novoNome}\" nas famílias?",
                            MainContent = (janela.IncluirAninhadas
                                    ? $"Serão abertas as {familias} famílias editáveis do projeto, procurando também nas aninhadas."
                                    : $"{familias} famílias têm o parâmetro.") +
                                "\n\nCada família é aberta e o parâmetro é renomeado (mesmo GUID, valores e fórmulas mantidos). Depois o nome antigo " +
                                "é liberado no projeto, as famílias são recarregadas e os valores de tipos e instâncias são restaurados. " +
                                "Se no final o projeto não mostrar o nome novo, tudo é desfeito. Os arquivos .rfa da biblioteca não são alterados." +
                                "\n\nRecomendado: testar primeiro numa cópia do modelo.",
                            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                            DefaultButton = TaskDialogResult.Yes,
                        };
                        if (td.Show() != TaskDialogResult.Yes) return Result.Cancelled;

                        ResultadoFamilias rf;
                        using (var progresso = Ui.Progresso.Abrir($"Renomear \"{item.Nome}\" → \"{novoNome}\"", podeCancelar: true))
                            rf = RenomearEmFamilias.Executar(app, doc, guid, novoNome, janela.IncluirAninhadas, progresso);
                        if (!rf.Ok)
                        {
                            var detalhes = new List<string> { "Motivo: " + rf.Motivo };
                            detalhes.AddRange(rf.Falhas.Select(f => "Falha: " + f));
                            detalhes.AddRange(rf.Avisos);
                            TaskDialog.Show("MetroSP", $"O parâmetro NÃO foi renomeado e nada foi alterado. O projeto mostra \"{rf.NomeNoProjeto}\"." +
                                "\n\n• " + string.Join("\n• ", detalhes.Take(20)));
                            message = rf.Motivo;
                            return Result.Failed;
                        }
                        avisos.Add($"Famílias: {rf.Renomeadas.Count} renomeadas, {rf.Recarregadas} recarregadas ({rf.Abertas} abertas)" +
                                   (rf.Restaurados > 0 ? $", {rf.Restaurados} valores restaurados" : "") + ".");
                        if (rf.Renomeadas.Count > 0) avisos.Add("Renomeadas: " + string.Join("; ", rf.Renomeadas.Take(15)) + (rf.Renomeadas.Count > 15 ? "..." : ""));
                        avisos.AddRange(rf.Falhas.Select(f => "Falha: " + f));
                        avisos.AddRange(rf.Avisos);
                    }
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("MetroSP", "Não foi possível renomear. Nada foi alterado.\n\n" + ex.Message);
                return Result.Failed;
            }

            if (janela.AtualizarArquivo)
            {
                avisos.Add(SharedParameterFile.Rename(arquivoUsuario, item.Guid, novoNome)
                    ? "Arquivo de parâmetros compartilhados atualizado (backup em .bak)."
                    : "O GUID não foi encontrado no arquivo de parâmetros compartilhados; o arquivo não foi alterado.");
            }

            TaskDialog.Show("MetroSP",
                $"Parâmetro renomeado:\n\"{item.Nome}\" → \"{novoNome}\"\nGUID mantido: {item.Guid}" +
                (avisos.Count > 0 ? "\n\n• " + string.Join("\n• ", avisos) : ""));
            return Result.Succeeded;
        }

        private static bool Confirmar(ProjectRenamePlan plan, string nomeAtual, string novoNome)
        {
            string tabelas = plan.Tabelas.Count == 0 ? "nenhuma" : string.Join(", ", plan.Tabelas.Take(10)) + (plan.Tabelas.Count > 10 ? "..." : "");
            var td = new TaskDialog("MetroSP - Renomear no projeto")
            {
                MainInstruction = $"Renomear \"{nomeAtual}\" para \"{novoNome}\"?",
                MainContent =
                    "O Revit não permite renomear parâmetro compartilhado no projeto. A ferramenta vai recriá-lo com o MESMO GUID " +
                    "e o nome novo, numa única operação (pode ser desfeita com Ctrl+Z):\n\n" +
                    $"• {plan.ElementosComValor} valores serão guardados e restaurados\n" +
                    $"• Campos em tabelas serão recriados: {tabelas}\n" +
                    (plan.FiltrosDeVista.Count > 0
                        ? $"• ATENÇÃO: as regras destes filtros de vista que usam o parâmetro serão perdidas: {string.Join(", ", plan.FiltrosDeVista)}\n"
                        : "") +
                    "\nEm modelos com worksharing, todos os elementos afetados precisam estar disponíveis para edição.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No,
            };
            return td.Show() == TaskDialogResult.Yes;
        }
    }
}
