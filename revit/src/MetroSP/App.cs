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

        /// <summary>Versão do Revit em que o add-in foi carregado (ex.: "2025.4"), para a janela Sobre.</summary>
        internal static string VersaoRevit { get; private set; }

        public Result OnStartup(UIControlledApplication application)
        {
            try { VersaoRevit = application.ControlledApplication.SubVersionNumber; } catch { }
            if (string.IsNullOrEmpty(VersaoRevit)) VersaoRevit = application.ControlledApplication.VersionNumber;

            try
            {
                application.CreateRibbonTab(TabName);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // A aba já existe (outro add-in MetroSP carregado). Seguimos usando ela.
            }

            // Painéis na ordem do trabalho: entregar, classificar, cuidar dos dados e, por último, manutenção.
            // O Sobre abre pela logo, à direita da aba.
            CreateEntregaPanel(application);
            CreateClassificacaoPanel(application);
            CreateDadosPanel(application);
            CreateParametrosPanel(application);

            // As logos entram depois que a Ribbon do Revit está pronta (fica à direita, depois dos painéis).
            application.ControlledApplication.ApplicationInitialized += (s, e) => Ui.MarcaRibbon.Adicionar(TabName);
            try { application.ThemeChanged += (s, e) => Ui.MarcaRibbon.AtualizarTema(); } catch { }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;

        private static void CreateEntregaPanel(UIControlledApplication application)
        {
            RibbonPanel panel = application.CreateRibbonPanel(TabName, "Entrega Metrô");

            AddButton<ConferirEntregaCommand>(panel, "Conferir\nEntrega",
                "Por onde começar: mostra num semáforo o que o Metrô pede e o que falta no modelo (arquivos, parâmetros, classe IFC, " +
                "dados e classificação), seleciona e pinta as pendências e abre a ferramenta que corrige.",
                "Conferir");

            panel.AddSeparator();

            AddButton<LocalizacaoIfcCommand>(panel, "Localização",
                "Passo 1 da entrega IFC. Calcula a latitude e a longitude pelo Project Base Point (PTL do Metrô) e grava em " +
                "Manage › Location, com o nome do IfcSite e os dados do IfcBuilding.",
                "Etapa1");

            PulldownButton configurar = AddPulldown(panel, "Configurar", "Configurar",
                "Passo 2 da entrega IFC. Os arquivos da classe (setup, property sets e mapeamentos) e em que classe IFC cada categoria do Revit sai.",
                "Etapa2");
            AddToPulldown<ArquivosIfcCommand>(configurar, "Arquivos IFC",
                "Setups (.json), property sets e mapeamentos (.txt) de cada classe: traga vários de uma vez, veja e troque as opções " +
                "principais do setup e confira os property sets no modelo aberto.",
                "ArquivosIfc");
            AddToPulldown<CategoriasIfcCommand>(configurar, "Categorias IFC",
                "Em que classe IFC cada categoria do Revit é exportada: uma tabela para IFC4 e outra para IFC 4.3. " +
                "Na exportação, vale a tabela do IFC schema do setup.",
                "CategoriasIfc");

            AddButton<VerificarIdsCommand>(panel, "Verificar\nPadrões",
                "Passo 3 da entrega IFC. Confere, antes de exportar, se os parâmetros e o mapeamento IFC atendem aos padrões de " +
                "informação do Metrô (arquivo .ids).",
                "Etapa3");

            AddButton<ExportarIfcCommand>(panel, "Exportar\nIFC",
                "Passo 4 da entrega IFC. Exporta o IFC com o setup (.json) e os property sets (.txt) da classe, guardados no " +
                "computador ou no Autodesk Forma.",
                "Etapa4");
        }

        private static void CreateClassificacaoPanel(UIControlledApplication application)
        {
            RibbonPanel panel = application.CreateRibbonPanel(TabName, "Classificação");

            AddButton<AplicarClassificacaoCommand>(panel, "Aplicar",
                "Escolha um item da planilha de classificação e aplique nos elementos selecionados (ClassificationCode = Código:Descrição). " +
                "A janela fica aberta enquanto você seleciona no Revit.",
                "ClassAplicar");

            panel.AddStackedItems(
                Dados<VerificarClassificacaoCommand>("Verificar",
                    "Confere se a classificação preenchida está na planilha, com a descrição e a categoria certas, e propõe correções.",
                    "ClassVerificar"),
                Dados<ConsistenciaClassificacaoCommand>("Consistência",
                    "Encontra códigos usados com descrições diferentes no modelo e unifica pela descrição escolhida.",
                    "ClassConsistencia"));
        }

        private static void CreateDadosPanel(UIControlledApplication application)
        {
            RibbonPanel panel = application.CreateRibbonPanel(TabName, "Dados do modelo");

            AddButton<PlanilhaDadosCommand>(panel, "Planilha\nde Dados",
                "Leva parâmetros dos elementos para uma planilha do Excel (uma linha por elemento) e traz de volta só o que mudou, " +
                "mostrando cada alteração antes de gravar.",
                "PlanilhaDados");

            AddButton<ConfrontarPlanilhaCommand>(panel, "Confrontar\nPlanilha",
                "Compara os parâmetros dos elementos com uma planilha externa (você indica a coluna-chave e a coluna de cada dado), " +
                "mostra o que está diferente e deixa aplicar o valor da planilha, ignorar ou selecionar. Os padrões ficam salvos.",
                "ConfrontarPlanilha");

            AddButton<EdicaoMassaCommand>(panel, "Editar\nem Massa",
                "Um parâmetro em muitos elementos: definir valor (juntando outros parâmetros com {Nome}), prefixo, sufixo, " +
                "localizar e substituir ou limpar, com prévia.",
                "EdicaoMassa");

            AddButton<FiltroCorCommand>(panel, "Filtrar\ne Colorir",
                "Encontre elementos por regras sobre parâmetros (E/OU) e selecione, isole na vista ou colora por valor, com legenda.",
                "FiltroCor");

            panel.AddStackedItems(
                Dados<CopiarValoresCommand>("Copiar valores",
                    "Copia valores de um parâmetro para outro, inclusive entre tipos de dado diferentes, com análise antes de gravar.",
                    "Copiar"),
                Dados<ContarElementosCommand>("Contar",
                    "Conta os elementos selecionados (ou todos da vista ativa) por categoria.",
                    "Contar"));
        }

        private static void CreateParametrosPanel(UIControlledApplication application)
        {
            RibbonPanel panel = application.CreateRibbonPanel(TabName, "Parâmetros e famílias");

            PulldownButton parametros = AddPulldown(panel, "Parametros", "Parâmetros",
                "Parâmetros de projeto e compartilhados: ver, exportar, criar pela planilha, pôr em famílias, listar e renomear.",
                "GerenciarParametros");
            AddToPulldown<GerenciarParametrosCommand>(parametros, "Gerenciar parâmetros",
                "Veja e exporte os parâmetros de projeto, crie vários de uma vez a partir de uma planilha (com as categorias) " +
                "e acrescente parâmetros compartilhados em várias famílias.",
                "GerenciarParametros");
            AddToPulldown<ListarCompartilhadosCommand>(parametros, "Listar compartilhados",
                "Todos os parâmetros compartilhados do arquivo aberto, com GUID, tipo, categorias e comparação com o .txt. " +
                "Também troca um parâmetro entre instância e tipo.",
                "Listar");
            AddToPulldown<RenomearParametroCommand>(parametros, "Renomear compartilhado",
                "Renomeia um parâmetro compartilhado (projeto ou família) mantendo o mesmo GUID.",
                "Renomear");

            AddButton<RenomearFamiliasCommand>(panel, "Renomear\nFamílias",
                "Renomeia famílias e tipos por regra (prefixo, sufixo, substituir, modelo com {Nome}), com prévia e conferência de nomes repetidos.",
                "RenomearFamilias");
        }

        private static PushButtonData Dados<TCommand>(string text, string tooltip, string icon) where TCommand : IExternalCommand
        {
            Type type = typeof(TCommand);
            return new PushButtonData(type.FullName, text, AssemblyPath, type.FullName)
            {
                ToolTip = tooltip,
                LargeImage = LoadIcon($"{icon}32.png"),
                Image = LoadIcon($"{icon}16.png"),
            };
        }

        private static PulldownButton AddPulldown(RibbonPanel panel, string name, string text, string tooltip, string icon)
        {
            var data = new PulldownButtonData("MetroSP." + name, text)
            {
                ToolTip = tooltip,
                LargeImage = LoadIcon($"{icon}32.png"),
                Image = LoadIcon($"{icon}16.png"),
            };
            return (PulldownButton)panel.AddItem(data);
        }

        private static void AddToPulldown<TCommand>(PulldownButton menu, string text, string tooltip, string icon) where TCommand : IExternalCommand =>
            menu.AddPushButton(Dados<TCommand>(text, tooltip, icon));

        private static PushButton AddButton<TCommand>(RibbonPanel panel, string text, string tooltip, string icon)
            where TCommand : IExternalCommand
        {
            return (PushButton)panel.AddItem(Dados<TCommand>(text, tooltip, icon));
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
