using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MetroSP.Conferencia;

namespace MetroSP.Commands
{
    /// <summary>
    /// Confere numa janela só se o modelo tem tudo o que o Metrô pede: arquivos do pacote, parâmetros e mapeamento,
    /// classe IFC, dados preenchidos e classificação. A janela fica aberta enquanto se corrige no Revit.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ConferirEntregaCommand : IExternalCommand
    {
        private static ConferirEntregaWindow _janela;
        private static Document _docJanela;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            Document doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null || doc.IsFamilyDocument)
            {
                TaskDialog.Show("MetroSP", "Abra um projeto (.rvt) para conferir a entrega.");
                return Result.Cancelled;
            }

            // Já aberta para este modelo: traz para frente e confere de novo.
            if (_janela != null && _docJanela != null && _docJanela.IsValidObject && _docJanela.Equals(doc))
            {
                _janela.Activate();
                _janela.Conferir();
                return Result.Succeeded;
            }
            _janela?.Close();

            PacoteMetro pacote = PacoteMetro.Abrir();
            bool novo = !PacoteMetro.Existe(pacote.PropertySets) && !PacoteMetro.Existe(pacote.Ids);
            if (novo)
            {
                // Primeira vez (ou arquivos sumiram): escolher o pacote antes de conferir.
                var escolha = new PacoteWindow(pacote, pacote.Arquivo != null ? System.IO.Path.GetDirectoryName(pacote.Arquivo) : null);
                new WindowInteropHelper(escolha).Owner = uiapp.MainWindowHandle;
                if (escolha.ShowDialog() != true) return Result.Cancelled;
                pacote = escolha.Pacote;
            }
            pacote.SalvarComoAtual();
            pacote.AplicarNasOutrasFerramentas();

            ResultadoConferencia resultado = ConferirEntregaWindow.Executar(uiapp, doc, pacote);

            _janela = new ConferirEntregaWindow(doc, pacote, resultado);
            _docJanela = doc;
            new WindowInteropHelper(_janela).Owner = uiapp.MainWindowHandle;
            _janela.Closed += (s, e) => { _janela = null; _docJanela = null; };
            _janela.Show();
            return Result.Succeeded;
        }
    }
}
