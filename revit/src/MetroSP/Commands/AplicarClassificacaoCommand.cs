using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MetroSP.Classificacao;

namespace MetroSP.Commands
{
    /// <summary>Abre a janela (não modal) para escolher um item da planilha e aplicá-lo à seleção.</summary>
    [Transaction(TransactionMode.Manual)]
    public class AplicarClassificacaoCommand : IExternalCommand
    {
        private static AplicarClassificacaoWindow _janela;
        private static bool _assinado;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            Abrir(commandData.Application);

        /// <summary>Abre (ou traz para frente) a janela não modal. Chamar dentro do contexto da API.</summary>
        public static Result Abrir(UIApplication uiapp)
        {
            UIDocument uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null || uidoc.Document.IsFamilyDocument)
            {
                TaskDialog.Show("MetroSP", "Abra um projeto (.rvt).");
                return Result.Cancelled;
            }

            if (_janela != null)
            {
                _janela.Activate();
                return Result.Succeeded;
            }

            // Assina uma vez por sessão; sem janela aberta o evento não faz nada.
            if (!_assinado)
            {
#if REVIT_ANTES_2023
                // Revit 2022 não tem o evento SelectionChanged: confere a seleção quando o Revit fica ocioso.
                string ultima = null;
                uiapp.Idling += (s, e) =>
                {
                    if (_janela == null) { ultima = null; return; }
                    UIApplication app = s as UIApplication ?? uiapp;
                    string atual;
                    try { atual = string.Join(",", app.ActiveUIDocument?.Selection.GetElementIds().Select(i => i.IntegerValue).OrderBy(i => i) ?? Enumerable.Empty<int>()); }
                    catch { return; }
                    if (atual == ultima) return;
                    ultima = atual;
                    _janela.SelecaoMudou(app);
                };
#else
                uiapp.SelectionChanged += (s, e) => _janela?.SelecaoMudou(s as UIApplication ?? uiapp);
#endif
                _assinado = true;
            }

            _janela = new AplicarClassificacaoWindow(ClassSettings.Abrir(), uidoc);
            new WindowInteropHelper(_janela).Owner = uiapp.MainWindowHandle;
            _janela.Closed += (s, e) => _janela = null;
            _janela.Show();
            _janela.SelecaoMudou(uiapp);
            return Result.Succeeded;
        }
    }
}
