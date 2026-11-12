using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MetroSP.Parametros;
using MetroSP.Ui;

namespace MetroSP.Commands
{
    /// <summary>
    /// Botão Parâmetros: uma ferramenta só, com as abas Compartilhados, Comparar com .txt, Parâmetros de projeto,
    /// Criar pela planilha e Adicionar em famílias. As duas primeiras são a janela dos compartilhados
    /// (ListaCompartilhadosWindow) e as outras, a de Gerenciar parâmetros; trocar de aba entre elas reabre a outra janela
    /// no mesmo lugar (AbasEntreJanelas). Numa família, só as abas dos compartilhados.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ListarCompartilhadosCommand : IExternalCommand
    {
        // Chaves das abas (AbasEntreJanelas).
        internal const string Compartilhados = "compartilhados", Comparar = "comparar", Modelo = "modelo", Criar = "criar", Familias = "familias";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            Abrir(commandData.Application);

        /// <summary>Abre a ferramenta (modal) na aba pedida. Chamar dentro do contexto da API (comando ou ExternalEvent).</summary>
        public static Result Abrir(UIApplication uiapp, string aba = Compartilhados)
        {
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc?.Document;
            if (doc == null)
            {
                TaskDialog.Show("MetroSP", "Abra um projeto ou uma família.");
                return Result.Cancelled;
            }

            if (doc.IsFamilyDocument)
            {
                // Família: Gerenciar parâmetros não se aplica; só as abas dos compartilhados, sem trocar de janela.
                Window lista = CriarLista(uiapp, doc, aba == Comparar ? Comparar : Compartilhados, false);
                if (lista == null) return Result.Succeeded;
                new WindowInteropHelper(lista).Owner = uiapp.MainWindowHandle;
                lista.ShowDialog();
                return Result.Succeeded;
            }

            AbasEntreJanelas.Rodar(aba, chave =>
                chave == Compartilhados || chave == Comparar
                    ? CriarLista(uiapp, doc, chave, true)
                    : CriarGerenciar(uidoc, chave),
                uiapp.MainWindowHandle);
            return Result.Succeeded;
        }

        private static Window CriarGerenciar(UIDocument uidoc, string aba)
        {
            var janela = new Dados.GerenciarParametrosWindow(uidoc);
            AbasEntreJanelas.AdicionarAba(janela.Abas, "Compartilhados", Compartilhados,
                "Os parâmetros compartilhados do arquivo, com GUID, tipo, categorias e o .txt de origem.", 0);
            AbasEntreJanelas.AdicionarAba(janela.Abas, "Comparar com .txt", Comparar,
                "Compara os parâmetros do arquivo com os .txt de referência e com a base MetroShared.", 1);
            janela.Abas.SelectedItem = aba == Criar ? janela.AbaCriar : aba == Familias ? janela.AbaFamilias : janela.AbaModelo;
            return janela;
        }

        private static Window CriarLista(UIApplication uiapp, Document doc, string aba, bool comAbasDoProjeto)
        {
            List<SharedParameterInfo> itens = SharedParameterCollector.Collect(doc);
            if (itens.Count == 0)
            {
                TaskDialog.Show("MetroSP", "Este arquivo não tem parâmetros compartilhados.");
                return null;
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
            if (comAbasDoProjeto)
            {
                AbasEntreJanelas.AdicionarAba(janela.Abas, "Parâmetros de projeto", Modelo,
                    "Todos os parâmetros de projeto do modelo (compartilhados ou não), para ver e exportar.");
                AbasEntreJanelas.AdicionarAba(janela.Abas, "Criar pela planilha", Criar,
                    "Cria vários parâmetros de uma vez a partir de uma planilha, já com as categorias.");
                AbasEntreJanelas.AdicionarAba(janela.Abas, "Adicionar em famílias", Familias,
                    "Acrescenta parâmetros compartilhados em várias famílias carregadas.");
            }
            janela.Abas.SelectedItem = aba == Comparar ? janela.AbaComparar : janela.AbaArquivo;
            return janela;
        }
    }
}
