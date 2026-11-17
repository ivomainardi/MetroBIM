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
            // Janelas MetroSP abrem no mesmo monitor do Revit.
            try { Ui.JanelaNoMonitor.Principal = application.MainWindowHandle; } catch { }
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

            // Três painéis, iguais aos do Civil 3D: Entrega (por onde o projetista começa), Ferramentas (menus) e
            // Coordenação (o que o coordenador BIM prepara). O Sobre abre pela logo, à direita da aba.
            CreateEntregaPanel(application);
            CreateFerramentasPanel(application);
            CreateCoordenacaoPanel(application);

            // Arquivo de coordenação do modelo ativo: a pasta do perfil entra na frente das outras configurações IFC.
            application.ViewActivated += (s, e) =>
            {
                try { if (e.Document != e.PreviousActiveView?.Document) Coordenacao.CoordenacaoRevit.Ativar(e.Document); } catch { }
            };

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
            application.ControlledApplication.ApplicationInitialized += (s, e) =>
            {
                Ui.MarcaRibbon.Adicionar(TabName);
                // Aviso de versão nova (uma consulta por dia, em segundo plano; quieto sem internet).
                try { Ui.JanelaVersaoNova.ProcurarAoAbrir("Revit"); } catch { }
            };
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
            RibbonPanel panel = application.CreateRibbonPanel(TabName, "Entrega");

            // Clique: abre a Entrega passo a passo. Na setinha: o Painel MetroSP, ao lado do modelo.
            Split(panel, "Entrega",
                Dados<EntregaCommand>("Entrega\npasso a passo",
                    "Por onde começar. Janela única da entrega: as etapas em cima e, embaixo, o que falta em cada uma, com os botões " +
                    "que resolvem e a ferramenta da etapa (Parâmetros, Categorias IFC, Editar em massa, Classificação, Local e Exportar IFC). " +
                    "Na setinha: o Painel MetroSP, ao lado do modelo.",
                    "Passo1"),
                Dados<PainelMetroSPCommand>("Painel ao lado do modelo",
                    "Abre ao lado do modelo o painel com os passos da entrega e o semáforo de cada um. Abra um passo para ver as " +
                    "pendências, selecionar e pintar os elementos e abrir a ferramenta que corrige.",
                    "Conferir"));

            // Projeto e Local não tem botão: fica no grupo Extras do Exportar IFC (IfcProject, IfcSite, IfcBuilding, localização).
            AddButton<ExportarIfcCommand>(panel, "Exportar\nIFC",
                "Exporta o IFC com o setup (.json) e os property sets (.txt) do perfil escolhido, guardados no " +
                "computador ou no Autodesk Forma. No grupo Extras: Projeto e Local (IfcProject, IfcSite, IfcBuilding e localização).",
                "Passo3");
        }

        /// <summary>As ferramentas do dia a dia, em três menus: Classificação, Dados e Parâmetros.</summary>
        private static void CreateFerramentasPanel(UIControlledApplication application)
        {
            RibbonPanel panel = application.CreateRibbonPanel(TabName, "Ferramentas");

            Menu(panel, "Classificacao", "Classificação",
                "Aplicar e verificar a classificação (ClassificationCode) pela planilha do padrão.", "ClassAplicar",
                Dados<AplicarClassificacaoCommand>("Aplicar",
                    "Escolha um item da planilha de classificação e aplique nos elementos selecionados (ClassificationCode = Código:Descrição). " +
                    "A janela fica aberta enquanto você seleciona no Revit.",
                    "ClassAplicar"),
                Dados<VerificarClassificacaoCommand>("Verificar",
                    "Confere se a classificação preenchida está na planilha, com a descrição e a categoria certas, e propõe correções. " +
                    "Em \"Códigos × descrições...\", encontra códigos usados com descrições diferentes e unifica.",
                    "ClassVerificar"));

            Menu(panel, "Dados", "Dados",
                "Preencher, conferir e corrigir os dados dos elementos: planilha, confronto, escrita, edição em massa, filtros e cópia.", "PlanilhaDados",
                Dados<PlanilhaDadosCommand>("Planilha de dados",
                    "Leva parâmetros dos elementos para uma planilha do Excel (uma linha por elemento) e traz de volta só o que mudou, " +
                    "mostrando cada alteração antes de gravar.",
                    "PlanilhaDados"),
                Dados<ConfrontarPlanilhaCommand>("Confrontar planilha",
                    "Compara os parâmetros dos elementos com uma planilha externa (você indica a coluna-chave e a coluna de cada dado), " +
                    "mostra o que está diferente e deixa aplicar o valor da planilha, ignorar ou selecionar. Os padrões ficam salvos.",
                    "ConfrontarPlanilha"),
                Dados<CorrigirEscritaCommand>("Corrigir escrita",
                    "Procura nos parâmetros de texto espaços sobrando ou errados (duplos, nas pontas, na pontuação, entre número e unidade), " +
                    "acentuação e caracteres estranhos. Mostra o valor atual e a sugestão para corrigir ou ignorar; siglas e códigos " +
                    "ficam numa lista de termos que não mudam.",
                    "CorrigirEscrita"),
                Dados<EdicaoMassaCommand>("Editar em massa",
                    "Um parâmetro em muitos elementos: definir valor (juntando outros parâmetros com {Nome}), prefixo, sufixo, " +
                    "localizar e substituir ou limpar, com prévia.",
                    "EdicaoMassa"),
                Dados<FiltroCorCommand>("Filtrar e colorir",
                    "Encontre elementos por regras sobre parâmetros (E/OU) e selecione, isole na vista ou colora por valor, com legenda.",
                    "FiltroCor"),
                Dados<CopiarValoresCommand>("Copiar valores",
                    "Copia valores de um parâmetro para outro, inclusive entre tipos de dado diferentes, com análise antes de gravar.",
                    "Copiar"));

            // Clique: abre Parâmetros. Na setinha: Renomear famílias.
            Split(panel, "Parametros",
                Dados<ListarCompartilhadosCommand>("Parâmetros",
                    "Uma janela para os parâmetros: compartilhados do arquivo (GUID, tipo, categorias, renomear mantendo o GUID, " +
                    "instância ↔ tipo, deletar os sem uso), comparação com o .txt do MetroShared, parâmetros de projeto, " +
                    "criar vários pela planilha e adicionar em famílias. Na setinha: Renomear famílias.",
                    "GerenciarParametros"),
                Dados<RenomearFamiliasCommand>("Renomear famílias",
                    "Renomeia famílias e tipos por regra (prefixo, sufixo, substituir, modelo com {Nome}), com prévia e conferência de nomes repetidos.",
                    "RenomearFamilias"));
        }

        /// <summary>O que o coordenador BIM prepara e distribui para a equipe, no fim da aba: Padrões, com o resto na setinha.</summary>
        private static void CreateCoordenacaoPanel(UIControlledApplication application)
        {
            RibbonPanel panel = application.CreateRibbonPanel(TabName, "Coordenação");

            // Os padrões de exportação IFC (setup, property sets e mapeamento) ficam em Padrões › Exportação IFC.
            Split(panel, "Padroes",
                Dados<PadroesCommand>("Padrões",
                    "Adicionar, editar e excluir padrões: classificação, confronto, dados do projeto, corrigir escrita, setups, property sets e " +
                    "mapeamentos IFC (botão Exportação IFC), arquivo de coordenação (.coord-metrosp), padrões Metrô (.ids) e pacote de entrega (.metrosp). " +
                    "Arraste os arquivos para a janela; o tipo é reconhecido sozinho. Também troca a planilha de todos de uma vez e leva tudo para a equipe num .zip. " +
                    "Na setinha: Arquivo de coordenação e Categorias IFC.",
                    "Padroes"),
                Dados<CoordenacaoCommand>("Arquivo de coordenação",
                    "Grava tudo o que você ajustou (setup e fase, categorias IFC, classes por família, property sets, mapeamento e Projeto e Local) " +
                    "num arquivo .coord-metrosp, com um ou mais perfis, para enviar ao projetista. Ele abre o arquivo em Entrega passo a passo › " +
                    "Abrir alterações (ou no Exportar IFC), e o Exportar IFC passa a usar tudo o que veio nele.",
                    "Coordenacao"),
                Dados<CategoriasIfcCommand>("Categorias IFC",
                    "Em que classe IFC cada categoria do Revit é exportada: uma tabela para IFC4 e outra para IFC 4.3. " +
                    "Na exportação, vale a tabela do IFC schema do setup.",
                    "CategoriasIfc"));
        }

        /// <summary>Botão grande com setinha: o clique roda o primeiro item, a setinha mostra os outros.</summary>
        private static void Split(RibbonPanel panel, string nome, params PushButtonData[] itens)
        {
            var split = (SplitButton)panel.AddItem(new SplitButtonData("MetroSP_" + nome, itens[0].Text));
            // Sempre mostra o primeiro item (não troca para o último usado), para o botão não mudar de nome.
            split.IsSynchronizedWithCurrentItem = false;
            foreach (PushButtonData item in itens) split.AddPushButton(item);
        }

        /// <summary>Botão grande que só abre o menu com as ferramentas.</summary>
        private static void Menu(RibbonPanel panel, string nome, string texto, string tooltip, string icon, params PushButtonData[] itens)
        {
            var menu = (PulldownButton)panel.AddItem(new PulldownButtonData("MetroSP_" + nome, texto)
            {
                ToolTip = tooltip,
                LargeImage = LoadIcon($"{icon}32.png"),
                Image = LoadIcon($"{icon}16.png"),
            });
            foreach (PushButtonData item in itens) menu.AddPushButton(item);
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
