using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MetroSP.Parametros;

namespace MetroSP.Commands
{
    /// <summary>
    /// Lista todos os parâmetros compartilhados do projeto ou da família aberta, com o .txt de origem,
    /// comparação com arquivos de referência e com a base MetroShared, renomear, exportar e deletar os sem uso.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ListarCompartilhadosCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            Abrir(commandData.Application);

        /// <summary>Abre a janela (modal). Chamar dentro do contexto da API (comando ou ExternalEvent).</summary>
        public static Result Abrir(UIApplication uiapp)
        {
            Document doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null)
            {
                TaskDialog.Show("MetroSP", "Abra um projeto ou uma família.");
                return Result.Cancelled;
            }

            List<SharedParameterInfo> itens = SharedParameterCollector.Collect(doc);
            if (itens.Count == 0)
            {
                TaskDialog.Show("MetroSP", "Este arquivo não tem parâmetros compartilhados.");
                return Result.Succeeded;
            }

            // A janela é modal, então ainda estamos no contexto da API e podemos abrir transações daqui.
            bool Renomear(SharedParameterInfo info, string nomeSugerido, string arquivo, bool? atualizarTxt)
            {
                List<ParametroItem> renomeaveis = RenomearParametroCommand.Itens(doc);
                ParametroItem alvo = renomeaveis.FirstOrDefault(i => i.Guid == info.Guid);
                if (alvo == null)
                {
                    TaskDialog.Show("MetroSP", "Este parâmetro não está vinculado ao projeto (vem só de famílias carregadas). " +
                                               "Renomeie nas famílias e recarregue-as no projeto.");
                    return false;
                }
                string msg = null;
                return RenomearParametroCommand.RenomearInterativo(uiapp, doc, renomeaveis, alvo, ref msg,
                    nomeSugerido, arquivo, atualizarTxt) == Result.Succeeded;
            }

            var janela = new ListaCompartilhadosWindow(itens, doc.Title, doc.IsFamilyDocument,
                uiapp.Application.SharedParametersFilename,
                Renomear,
                () => SharedParameterCollector.Collect(doc),
                (lista, caminho) => SharedParameterExporter.ExportarTxt(uiapp.Application, lista, caminho),
                lista => UsoParametros.Analisar(doc, lista),
                lista => UsoParametros.Deletar(doc, lista),
                lista => TrocarVinculo.Analisar(doc, lista),
                (planos, maisComum) => TrocarVinculo.Aplicar(doc, planos, maisComum));
            new WindowInteropHelper(janela).Owner = uiapp.MainWindowHandle;
            janela.ShowDialog();
            return Result.Succeeded;
        }
    }
}
