using System.Collections.Generic;
using Autodesk.AutoCAD.Runtime;
using MetroSP.Classificacao;
using MetroSP.Config;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(MetroSP.Padroes.PadroesCommands))]

namespace MetroSP.Padroes
{
    public class PadroesCommands
    {
        /// <summary>Os tipos de padrão que o plugin do Civil 3D guarda neste computador.</summary>
        public static List<TipoPadrao> Tipos(Autodesk.AutoCAD.ApplicationServices.Document doc) => new List<TipoPadrao>
        {
            new TipoPadrao
            {
                Id = "civil3d-classificacao", Nome = "Classificação", Pasta = PadraoClassificacao.Pasta,
                Ferramenta = "Verificar classificação › Editar padrões",
                Editar = doc == null ? null : (dono, item) =>
                    new PadroesClassificacaoWindow(doc.Database, item.Nome) { Owner = dono }.ShowDialog(),
            },
            new TipoPadrao
            {
                Id = "civil3d-confrontar", Nome = "Confrontar planilha", Pasta = Confronto.Padroes.Pasta("Civil3D"),
                Ferramenta = "Confrontar planilha (lista de padrões no topo da janela)",
            },
            new TipoPadrao
            {
                Id = "civil3d-exportacao-ifc", Nome = "Exportação IFC", Pasta = ConfigPackage.ActiveDir,
                Ferramenta = "Configurar IFC",
                Editar = (dono, item) => new Ui.ConfigWindow(doc == null ? null : Commands.DrawingFolder(doc)) { Owner = dono }.ShowDialog(),
            },
        };

        /// <summary>Gerenciar todos os padrões salvos e as planilhas que eles usam.</summary>
        [CommandMethod("MSP_PADROES")]
        public void Gerenciar()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            try
            {
                AcApp.ShowModalWindow(AcApp.MainWindow.Handle, new GerenciarPadroesWindow(Tipos(doc), "Civil 3D"), false);
            }
            catch (System.Exception ex)
            {
                doc?.Editor.WriteMessage("\nMetroSP: ERRO - " + ex.Message);
            }
        }
    }
}
