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
#if NETFRAMEWORK
            // Revit 2022-2024 (.NET Framework): as DLLs que vão junto (System.Text.Json e dependências) ficam na pasta do add-in.
            // Se o Revit já tiver carregado outra versão delas, o .NET Framework procura aqui com este evento.
            AppDomain.CurrentDomain.AssemblyResolve += ResolverDaPastaDoAddin;
#endif
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

            // Painéis na ordem do trabalho: entregar (passos 1, 2 e 3), classificar, cuidar dos dados, manutenção e,
            // por último, o que o coordenador BIM prepara uma vez. O Sobre abre pela logo, à direita da aba.
            CreateEntregaPanel(application);
            CreateClassificacaoPanel(application);
            CreateDadosPanel(application);
            CreateParametrosPanel(application);
            CreateCoordenadorPanel(application);

            // Painel MetroSP: encaixado ao lado do modelo (DockablePane). Registrado aqui, no início do Revit.
            try
            {
                application.RegisterDockablePane(Painel.PainelMetroSP.Id, Painel.PainelMetroSP.Titulo, new Painel.PainelMetroSP());
                application.ViewActivated += (s, e) =>
                {
                    try { Painel.PainelMetroSP.Instancia?.ModeloAtivo(e.Document); } catch { }
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("MetroSP: Painel MetroSP não registrado: " + ex.Message);
            }

            // As logos entram depois que a Ribbon do Revit está pronta (fica à direita, depois dos painéis).
            application.ControlledApplication.ApplicationInitialized += (s, e) => Ui.MarcaRibbon.Adicionar(TabName);
#if !REVIT_ANTES_2024
            try { application.ThemeChanged += (s, e) => Ui.MarcaRibbon.AtualizarTema(); } catch { }
#endif

            return Result.Succeeded;
        }

#if NETFRAMEWORK
        private static Assembly ResolverDaPastaDoAddin(object sender, ResolveEventArgs args)
        {
            try
            {
                string nome = new AssemblyName(args.Name).Name;
                string arquivo = Path.Combine(Path.GetDirectoryName(typeof(App).Assembly.Location) ?? "", nome + ".dll");
                return File.Exists(arquivo) ? Assembly.LoadFrom(arquivo) : null;
            }
            catch { return null; }
        }
#endif

        public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;

        private static void CreateEntregaPanel(UIControlledApplication application)
        {
            RibbonPanel panel = application.CreateRibbonPanel(TabName, "Entrega Metrô");

            AddButton<PainelMetroSPCommand>(panel, "Painel\nMetroSP",
                "Passo 1, por onde começar. Abre ao lado do modelo o painel com os passos da entrega e o semáforo de cada um " +
                "(arquivos, parâmetros, classe IFC, dados e classificação). Abra um passo para ver as pendências, selecionar e " +
                "pintar os elementos e abrir a ferramenta que corrige. A conferência completa (matriz, IDS) abre pelo painel.",
                "Passo1");

            AddButton<EntregaCommand>(panel, "Entrega\npasso a passo",
                "Janela única da entrega: as etapas em cima, com setinhas, e embaixo o que falta em cada etapa, com os botões " +
                "que resolvem e a ferramenta da etapa (Parâmetros, Categorias IFC, Editar em massa, Classificação, Local e Exportar IFC).",
                "Passo1");

            AddButton<LocalizacaoIfcCommand>(panel, "Projeto e\nLocal",
                "Passo 2. Preenche o IfcProject, o IfcSite e o IfcBuilding (nome, nome longo, descrição, fase, endereço) " +
                "e o Pset MetroSP_Projeto, e calcula a latitude e a longitude pelo Project Base Point (PTL do Metrô). " +
                "Os dados podem ser salvos como padrão e usados em outros modelos, também no Civil 3D.",
                "Passo2");

            AddButton<ExportarIfcCommand>(panel, "Exportar\nIFC",
                "Passo 3. Exporta o IFC com o setup (.json) e os property sets (.txt) da classe, guardados no " +
                "computador ou no Autodesk Forma.",
                "Passo3");
        }

        private static void CreateClassificacaoPanel(UIControlledApplication application)
        {
            RibbonPanel panel = application.CreateRibbonPanel(TabName, "Classificação");

            AddButton<AplicarClassificacaoCommand>(panel, "Aplicar",
                "Escolha um item da planilha de classificação e aplique nos elementos selecionados (ClassificationCode = Código:Descrição). " +
                "A janela fica aberta enquanto você seleciona no Revit.",
                "ClassAplicar");

            AddButton<VerificarClassificacaoCommand>(panel, "Verificar",
                "Confere se a classificação preenchida está na planilha, com a descrição e a categoria certas, e propõe correções. " +
                "Em \"Códigos × descrições...\", encontra códigos usados com descrições diferentes e unifica.",
                "ClassVerificar");
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

            AddButton<CorrigirEscritaCommand>(panel, "Corrigir\nEscrita",
                "Procura nos parâmetros de texto espaços sobrando ou errados (duplos, nas pontas, na pontuação, entre número e unidade), " +
                "acentuação e caracteres estranhos. Mostra o valor atual e a sugestão para corrigir ou ignorar; siglas e códigos " +
                "ficam numa lista de termos que não mudam.",
                "CorrigirEscrita");

            AddButton<EdicaoMassaCommand>(panel, "Editar\nem Massa",
                "Um parâmetro em muitos elementos: definir valor (juntando outros parâmetros com {Nome}), prefixo, sufixo, " +
                "localizar e substituir ou limpar, com prévia.",
                "EdicaoMassa");

            AddButton<FiltroCorCommand>(panel, "Filtrar\ne Colorir",
                "Encontre elementos por regras sobre parâmetros (E/OU) e selecione, isole na vista ou colora por valor, com legenda.",
                "FiltroCor");

            AddButton<CopiarValoresCommand>(panel, "Copiar\nValores",
                "Copia valores de um parâmetro para outro, inclusive entre tipos de dado diferentes, com análise antes de gravar.",
                "Copiar");
        }

        private static void CreateParametrosPanel(UIControlledApplication application)
        {
            RibbonPanel panel = application.CreateRibbonPanel(TabName, "Parâmetros e famílias");

            AddButton<ListarCompartilhadosCommand>(panel, "Parâmetros",
                "Uma janela para os parâmetros: compartilhados do arquivo (GUID, tipo, categorias, renomear mantendo o GUID, " +
                "instância ↔ tipo, deletar os sem uso), comparação com o .txt do MetroShared, parâmetros de projeto, " +
                "criar vários pela planilha e adicionar em famílias.",
                "GerenciarParametros");

            AddButton<RenomearFamiliasCommand>(panel, "Renomear\nFamílias",
                "Renomeia famílias e tipos por regra (prefixo, sufixo, substituir, modelo com {Nome}), com prévia e conferência de nomes repetidos.",
                "RenomearFamilias");
        }

        /// <summary>O que o coordenador BIM prepara uma vez e distribui para a equipe: botões pequenos, no fim da aba.</summary>
        private static void CreateCoordenadorPanel(UIControlledApplication application)
        {
            RibbonPanel panel = application.CreateRibbonPanel(TabName, "Configurar padrões");

            panel.AddStackedItems(
                Dados<PadroesCommand>("Padrões e planilhas",
                    "Todos os padrões salvos (classificação, confronto, dados do projeto, setups, property sets e mapeamentos IFC) e as planilhas que eles usam: " +
                    "editar, trocar a planilha de todos de uma vez e levar para a equipe num .zip.",
                    "Padroes"),
                Dados<ArquivosIfcCommand>("Arquivos IFC",
                    "Setups (.json), property sets e mapeamentos (.txt) de cada classe: traga vários de uma vez, veja e troque as opções " +
                    "principais do setup e confira os property sets no modelo aberto.",
                    "ArquivosIfc"),
                Dados<CategoriasIfcCommand>("Categorias IFC",
                    "Em que classe IFC cada categoria do Revit é exportada: uma tabela para IFC4 e outra para IFC 4.3. " +
                    "Na exportação, vale a tabela do IFC schema do setup.",
                    "CategoriasIfc"));
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
