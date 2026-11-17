using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                Ferramenta = "Verificar classificação › Editar perfis",
                Editar = doc == null ? null : (dono, item) =>
                    new PadroesClassificacaoWindow(doc.Database, item.Nome) { Owner = dono }.ShowDialog(),
                Reconhece = f => GerenciadorPadroes.JsonCom(f, "Sistemas"),
            },
            new TipoPadrao
            {
                Id = "civil3d-confrontar", Nome = "Confrontar planilha", Pasta = Confronto.Padroes.Pasta("Civil3D"),
                Ferramenta = "Confrontar planilha (lista de perfis no topo da janela)",
                Reconhece = f => GerenciadorPadroes.JsonCom(f, "Comparacoes", "CampoChave"),
            },
            new TipoPadrao
            {
                Id = "metrosp-dados-projeto", Nome = "Dados do projeto IFC", Pasta = Ifc.DadosProjetoIfc.Pasta,
                Ferramenta = "Projeto IFC (barra Perfil no topo da janela); vale também para o Revit",
                Reconhece = f => GerenciadorPadroes.JsonCom(f, "Projeto", "Terreno", "Edificio"),
            },
            new TipoPadrao
            {
                Id = "escrita", Nome = "Corrigir escrita", Pasta = Escrita.PadroesEscrita.Pasta,
                Ferramenta = "Dados › Corrigir escrita (perfil no topo da janela; vale também no Revit)",
                Editar = (dono, item) => Escrita.CorrigirEscritaWindow.EditarArquivo(dono, item.Arquivo),
                Reconhece = f => GerenciadorPadroes.JsonCom(f, "RegrasDesligadas", "Termos", "Correcoes"),
            },
            new TipoPadrao
            {
                Id = "civil3d-exportacao-ifc", Nome = "Exportação IFC", Pasta = ConfigPackage.ActiveDir,
                Ferramenta = "Configurar IFC",
                Editar = (dono, item) => new Ui.ConfigWindow(doc == null ? null : Commands.DrawingFolder(doc)) { Owner = dono }.ShowDialog(),
                Reconhece = EhConfigIfc,
                // Cada arquivo entra com o nome que o exportador lê (o anterior vai para o backup do pacote).
                Trazer = (dono, arquivos) => "Exportação IFC:\n" + string.Join("\n", ConfigPackage.Import(arquivos)),
                TrazerTexto = "entra na configuração IFC ativa",
            },
        };

        /// <summary>Arquivo da configuração IFC: pelo nome que o exportador usa ou pelo conteúdo (mapeamento, propriedades, MetroSP).</summary>
        private static bool EhConfigIfc(string arquivo)
        {
            string nome = Path.GetFileName(arquivo);
            if (ConfigPackage.FileNames.Values.Any(v => nome.EndsWith(v, StringComparison.OrdinalIgnoreCase))) return true;
            if (!nome.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;
            ConfigKind k = ConfigPackage.Detect(arquivo);
            return k == ConfigKind.PropertyTemplates || k == ConfigKind.MetroSP || k == ConfigKind.ObjectMapping;
        }

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
