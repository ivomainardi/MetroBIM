using System.Collections.Generic;
using System.IO;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MetroSP.Classificacao;
using MetroSP.Ids;
using MetroSP.Padroes;

namespace MetroSP.Commands
{
    /// <summary>Todos os padrões salvos pelo plugin (classificação, confronto, arquivos IFC) e as planilhas que eles usam.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class PadroesCommand : IExternalCommand
    {
        public static List<TipoPadrao> Tipos(Document doc)
        {
            TipoPadrao Ifc(ArquivosIfc.Tipo t, string id, string nome, string filtro) => new TipoPadrao
            {
                Id = id, Nome = nome, Pasta = Path.Combine(ClassesIfc.PastaUsuario, ArquivosIfc.Sub(t)), Filtro = filtro,
                Ferramenta = "Configurar › Arquivos IFC",
                Editar = (dono, item) => new ArquivosIfcWindow(doc, Path.GetFileNameWithoutExtension(item.Arquivo), t) { Owner = dono }.ShowDialog(),
            };
            return new List<TipoPadrao>
            {
                new TipoPadrao
                {
                    Id = "revit-classificacao", Nome = "Classificação", Pasta = PadraoClassificacao.Pasta,
                    Ferramenta = "Classificação › Verificar › Editar padrões (com um modelo aberto)",
                    Editar = doc == null ? null : (dono, item) => new PadroesClassificacaoWindow(doc, item.Nome) { Owner = dono }.ShowDialog(),
                },
                new TipoPadrao
                {
                    Id = "revit-confrontar", Nome = "Confrontar planilha", Pasta = Confronto.Padroes.Pasta("Revit"),
                    Ferramenta = "Confrontar Planilha (lista de padrões no topo da janela)",
                },
                new TipoPadrao
                {
                    Id = "escrita", Nome = "Corrigir escrita", Pasta = Escrita.PadroesEscrita.Pasta,
                    Ferramenta = "Dados do modelo › Corrigir Escrita (padrão no topo da janela; vale também no Civil 3D)",
                    Editar = (dono, item) => Escrita.CorrigirEscritaWindow.EditarArquivo(dono, item.Arquivo),
                },
                Ifc(ArquivosIfc.Tipo.Setup, "revit-setup-ifc", "Setup IFC", "*.json"),
                Ifc(ArquivosIfc.Tipo.PropertySets, "revit-psets-ifc", "Property sets IFC", "*.txt"),
                Ifc(ArquivosIfc.Tipo.Mapeamento, "revit-mapeamento-ifc", "Mapeamento IFC", "*.txt"),
            };
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            Document doc = uiapp.ActiveUIDocument?.Document;
            if (doc != null && doc.IsFamilyDocument) doc = null;
            var w = new GerenciarPadroesWindow(Tipos(doc), "Revit");
            new WindowInteropHelper(w).Owner = uiapp.MainWindowHandle;
            w.ShowDialog();
            return Result.Succeeded;
        }
    }
}
