using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using MetroSP.Commands;

namespace MetroSP
{
    /// <summary>
    /// Ponto de entrada do add-in: cria a aba "MetroSP" na Ribbon ao abrir o Revit.
    /// </summary>
    public class App : IExternalApplication
    {
        public const string TabName = "MetroSP";

        private static readonly string AssemblyPath = Assembly.GetExecutingAssembly().Location;

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                application.CreateRibbonTab(TabName);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // A aba já existe (outro add-in MetroSP carregado). Seguimos usando ela.
            }

            CreateGeralPanel(application);
            CreateEntregaPanel(application);
            CreateParametrosPanel(application);
            CreateIfcPanel(application);
            CreateClassificacaoPanel(application);
            CreateModelagemPanel(application);

            // As logos entram depois que a Ribbon do Revit está pronta (fica à direita, depois dos painéis).
            application.ControlledApplication.ApplicationInitialized += (s, e) => Ui.MarcaRibbon.Adicionar(TabName);
            try { application.ThemeChanged += (s, e) => Ui.MarcaRibbon.AtualizarTema(); } catch { }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;

        private static void CreateGeralPanel(UIControlledApplication application)
        {
            RibbonPanel panel = application.CreateRibbonPanel(TabName, "Geral");

            AddButton<SobreCommand>(panel, "Sobre",
                "Informações sobre o add-in MetroSP.",
                "Sobre");
        }

        private static void CreateEntregaPanel(UIControlledApplication application)
        {
            RibbonPanel panel = application.CreateRibbonPanel(TabName, "Entrega");

            AddButton<ConferirEntregaCommand>(panel, "Conferir\nEntrega",
                "Confere numa janela só o que o Metrô pede: arquivos do pacote, parâmetros e mapeamento IFC, classe IFC, " +
                "dados preenchidos e classificação, com semáforo por etapa. Seleciona e pinta as pendências e abre a ferramenta que corrige.",
                "Conferir");
        }

        private static void CreateParametrosPanel(UIControlledApplication application)
        {
            RibbonPanel panel = application.CreateRibbonPanel(TabName, "Parâmetros");

            AddButton<RenomearParametroCommand>(panel, "Renomear\nCompartilhado",
                "Renomeia um parâmetro compartilhado (projeto ou família) mantendo o mesmo GUID.",
                "Renomear");

            AddButton<ListarCompartilhadosCommand>(panel, "Listar\nCompartilhados",
                "Lista todos os parâmetros compartilhados do arquivo aberto, com GUID, tipo, categorias e comparação com o arquivo .txt.",
                "Listar");

            AddButton<CopiarValoresCommand>(panel, "Copiar\nValores",
                "Copia valores de um parâmetro para outro, inclusive entre tipos de dado diferentes. Mostra a análise, os impactos e propostas de conversão antes de gravar.",
                "Copiar");
        }

        private static void CreateIfcPanel(UIControlledApplication application)
        {
            RibbonPanel panel = application.CreateRibbonPanel(TabName, "IFC / IDS");

            AddButton<VerificarIdsCommand>(panel, "Verificar\nMapeamento IDS",
                "Verifica, antes de exportar, se os parâmetros e o mapeamento de exportação IFC atendem a um arquivo IDS.",
                "Ids");

            AddButton<ExportarIfcCommand>(panel, "Exportar\nIFC",
                "Exporta o IFC com o setup (.json) e o mapeamento de property sets (.txt) guardados numa pasta do computador ou no Autodesk Forma.",
                "IfcExportar");
        }

        private static void CreateClassificacaoPanel(UIControlledApplication application)
        {
            RibbonPanel panel = application.CreateRibbonPanel(TabName, "Classificação");

            AddButton<AplicarClassificacaoCommand>(panel, "Aplicar\nClassificação",
                "Escolha um item da planilha de classificação e aplique nos elementos selecionados (ClassificationCode = Código:Descrição). A janela fica aberta enquanto você seleciona no Revit.",
                "ClassAplicar");

            AddButton<VerificarClassificacaoCommand>(panel, "Verificar\nClassificação",
                "Confere se a classificação preenchida está na planilha, com a descrição e a categoria certas, e propõe correções.",
                "ClassVerificar");

            AddButton<ConsistenciaClassificacaoCommand>(panel, "Consistência\nCódigo×Descrição",
                "Encontra códigos usados com descrições diferentes no modelo e unifica pela descrição escolhida.",
                "ClassConsistencia");
        }

        private static void CreateModelagemPanel(UIControlledApplication application)
        {
            RibbonPanel panel = application.CreateRibbonPanel(TabName, "Modelagem");

            AddButton<ContarElementosCommand>(panel, "Contar\nElementos",
                "Conta os elementos selecionados (ou todos da vista ativa) por categoria.",
                "Contar");
        }

        private static PushButton AddButton<TCommand>(RibbonPanel panel, string text, string tooltip, string icon)
            where TCommand : IExternalCommand
        {
            Type type = typeof(TCommand);
            var data = new PushButtonData(type.FullName, text, AssemblyPath, type.FullName)
            {
                ToolTip = tooltip,
                LargeImage = LoadIcon($"{icon}32.png"),
                Image = LoadIcon($"{icon}16.png"),
            };
            return (PushButton)panel.AddItem(data);
        }

        /// <summary>Carrega um PNG embutido em Resources/. Retorna null se não existir.</summary>
        internal static BitmapImage LoadIcon(string fileName)
        {
            Stream stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream($"MetroSP.Resources.{fileName}");
            if (stream == null) return null;

            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = stream;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
    }
}
