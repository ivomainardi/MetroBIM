using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MetroSP.Classificacao;
using MetroSP.Conferencia;
using MetroSP.Ids;
using MetroSP.Padroes;

namespace MetroSP.Commands
{
    /// <summary>Todos os padrões salvos pelo plugin (classificação, confronto, arquivos IFC, padrões Metrô) e as planilhas que eles usam.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class PadroesCommand : IExternalCommand
    {
        /// <summary>Pasta dos padrões Metrô (.ids) trazidos pelo Adicionar padrões.</summary>
        public static string PastaIds =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MetroSP", "PadroesMetro");

        public static List<TipoPadrao> Tipos(Document doc)
        {
            // Os três tipos de arquivo IFC entram juntos na janela Arquivos IFC, que pede a classe de cada um.
            Func<System.Windows.Window, IList<string>, string> trazerIfc = (dono, arquivos) =>
            {
                var w = new ArquivosIfcWindow(doc) { Owner = dono };
                w.TrazerAoAbrir(arquivos);
                w.ShowDialog();
                return null;
            };
            TipoPadrao Ifc(ArquivosIfc.Tipo t, string id, string nome, string filtro) => new TipoPadrao
            {
                Id = id, Nome = nome, Pasta = Path.Combine(ClassesIfc.PastaUsuario, ArquivosIfc.Sub(t)), Filtro = filtro,
                Ferramenta = "Coordenação BIM › Arquivos IFC",
                Editar = (dono, item) => new ArquivosIfcWindow(doc, Path.GetFileNameWithoutExtension(item.Arquivo), t) { Owner = dono }.ShowDialog(),
                Reconhece = f => ArquivosIfc.Detectar(f, out _) == t,
                Trazer = trazerIfc,
                TrazerTexto = "abre Arquivos IFC para indicar a classe",
            };
            return new List<TipoPadrao>
            {
                new TipoPadrao
                {
                    Id = "revit-classificacao", Nome = "Classificação", Pasta = PadraoClassificacao.Pasta,
                    Ferramenta = "Classificação › Verificar › Editar padrões (com um modelo aberto)",
                    Editar = doc == null ? null : (dono, item) => new PadroesClassificacaoWindow(doc, item.Nome) { Owner = dono }.ShowDialog(),
                    Reconhece = f => GerenciadorPadroes.JsonCom(f, "Sistemas"),
                },
                new TipoPadrao
                {
                    Id = "revit-confrontar", Nome = "Confrontar planilha", Pasta = Confronto.Padroes.Pasta("Revit"),
                    Ferramenta = "Confrontar Planilha (lista de padrões no topo da janela)",
                    Reconhece = f => GerenciadorPadroes.JsonCom(f, "Comparacoes", "CampoChave"),
                },
                new TipoPadrao
                {
                    Id = "metrosp-dados-projeto", Nome = "Dados do projeto IFC", Pasta = MetroSP.Ifc.DadosProjetoIfc.Pasta,
                    Ferramenta = "Entrega Metrô › Projeto e Local (barra Padrão no topo da janela); vale também para o Civil 3D",
                    Reconhece = f => GerenciadorPadroes.JsonCom(f, "Projeto", "Terreno", "Edificio"),
                },
                new TipoPadrao
                {
                    Id = "escrita", Nome = "Corrigir escrita", Pasta = Escrita.PadroesEscrita.Pasta,
                    Ferramenta = "Dados do modelo › Corrigir Escrita (padrão no topo da janela; vale também no Civil 3D)",
                    Editar = (dono, item) => Escrita.CorrigirEscritaWindow.EditarArquivo(dono, item.Arquivo),
                    Reconhece = f => GerenciadorPadroes.JsonCom(f, "RegrasDesligadas", "Termos", "Correcoes"),
                },
                Ifc(ArquivosIfc.Tipo.Setup, "revit-setup-ifc", "Setup IFC", "*.json"),
                Ifc(ArquivosIfc.Tipo.PropertySets, "revit-psets-ifc", "Property sets IFC", "*.txt"),
                Ifc(ArquivosIfc.Tipo.Mapeamento, "revit-mapeamento-ifc", "Mapeamento IFC", "*.txt"),
                new TipoPadrao
                {
                    Id = "revit-ids", Nome = "Padrões Metrô (.ids)", Pasta = PastaIds, Filtro = "*.ids", Extensao = ".ids",
                    Ferramenta = "Painel MetroSP › Padrões do Metrô (Requisitos .ids)",
                    Reconhece = EhIds,
                    Trazer = (dono, arquivos) => TrazerIds(arquivos),
                    TrazerTexto = "copia e passa a ser o .ids em uso",
                },
                new TipoPadrao
                {
                    // Não é copiado (os caminhos dentro dele são relativos à pasta dele): só passa a ser o pacote em uso.
                    Id = "revit-pacote-metro", Nome = "Pacote de entrega (.metrosp)", Pasta = null, Filtro = "*" + PacoteMetro.Extensao,
                    Ferramenta = "Painel MetroSP › Padrões do Metrô",
                    Reconhece = f => f.EndsWith(PacoteMetro.Extensao, StringComparison.OrdinalIgnoreCase),
                    Trazer = (dono, arquivos) => UsarPacote(arquivos.Last()),
                    TrazerTexto = "passa a ser o pacote em uso (fica onde está)",
                },
            };
        }

        /// <summary>Arquivo IDS da buildingSMART (.ids, ou .xml com o elemento ids).</summary>
        private static bool EhIds(string arquivo)
        {
            string ext = Path.GetExtension(arquivo).ToLowerInvariant();
            if (ext != ".ids" && ext != ".xml") return false;
            var buffer = new char[8192];
            int n;
            using (var r = new StreamReader(arquivo, Encoding.UTF8, true)) n = r.Read(buffer, 0, buffer.Length);
            string inicio = new string(buffer, 0, n);
            return inicio.IndexOf("<ids", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   inicio.IndexOf("buildingsmart.org/ids", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Copia os .ids para a pasta dos padrões Metrô e passa a usar o último no Verificar IDS e na conferência.</summary>
        private static string TrazerIds(IList<string> arquivos)
        {
            TipoPadrao tipo = Tipos(null).First(t => t.Id == "revit-ids");
            string ultimo = null;
            foreach (string f in arquivos) ultimo = GerenciadorPadroes.Copiar(f, tipo);
            if (ultimo == null) return null;

            IdsSettings ids = IdsSettings.Load();
            ids.IdsFile = ultimo;
            ids.Save();
            string r = $"{arquivos.Count} padrão(ões) Metrô (.ids) adicionado(s). Em uso: {Path.GetFileName(ultimo)}.";
            PacoteMetro pacote = PacoteMetro.Abrir();
            if (pacote.Arquivo == null)
            {
                pacote.Ids = ultimo;
                pacote.SalvarComoAtual();
            }
            else
            {
                r += $"\nA conferência segue o pacote \"{pacote.NomeExibido}\", que tem o próprio .ids. Para trocar, use Padrões do Metrô no Painel MetroSP.";
            }
            return r;
        }

        /// <summary>Passa a usar o pacote .metrosp (sem copiar) na conferência e nas outras ferramentas.</summary>
        private static string UsarPacote(string arquivo)
        {
            PacoteMetro p = PacoteMetro.LerArquivo(arquivo);
            p.SoVistaAtiva = PacoteMetro.Abrir().SoVistaAtiva;
            p.SalvarComoAtual();
            p.AplicarNasOutrasFerramentas();
            return $"Pacote de entrega em uso: {p.NomeExibido}. Os arquivos dele já valem na conferência, no Exportar IFC e na Classificação.";
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
