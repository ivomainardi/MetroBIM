using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MetroSP.Ui;
using DB = Autodesk.Revit.DB;

namespace MetroSP.Ids
{
    /// <summary>
    /// O usuário escolhe uma classe instalada com o MetroSP (que preenche os arquivos sozinha) ou indica cada
    /// arquivo da exportação: o setup .json, o .txt de property sets e, se usar,
    /// o .txt de mapeamento de parâmetros; depois a vista e o destino. Os arquivos podem estar em qualquer
    /// pasta (computador, rede ou Forma pelo Desktop Connector) e as últimas escolhas ficam lembradas.
    /// Confere o .txt de property sets contra o modelo antes de exportar.
    /// </summary>
    public class ExportarIfcWindow : Window
    {
        private readonly DB.Document _doc;
        private readonly IfcExportSettings _settings;

        private readonly ComboBox _classe;
        private readonly Border _seloClasse;
        private readonly Button _botaoForma;
        private string _motivoClasse;
        private bool _aplicandoClasse;
        private const string Outros = "Outros arquivos (indicar abaixo)";

        // Cada arquivo: nome no item, selo de situação, detalhe em cinza e o caminho (editável só em "Outros").
        private readonly TextBox _setup;
        private readonly TextBlock _arqSetup;
        private readonly Border _seloSetup;
        private readonly TextBlock _infoSetup;
        private readonly TextBox _psets;
        private readonly TextBlock _arqPsets;
        private readonly Border _seloPsets;
        private readonly TextBlock _infoPsets;
        private readonly TextBox _mapeamento;
        private readonly TextBlock _arqMapeamento;
        private readonly Border _seloMapeamento;
        private readonly TextBlock _infoMapeamento;
        private readonly List<FrameworkElement> _caminhos = new List<FrameworkElement>();
        private readonly ComboBox _vista;
        private readonly ComboBox _fase;
        private string _setupGravado;
        private readonly TextBox _arquivo;
        private readonly TextBlock _resumo;
        private readonly Border _seloLocalizacao;
        private readonly TextBlock _resumoPerfil;
        private readonly FrameworkElement _secaoArquivos, _grupoArquivos;
        private readonly Button _gravarLocal;
        private readonly TextBox _ifcProject, _ifcSite, _ifcBuilding;

        private HashSet<string> _parametrosModelo;
        private List<string> _avisosPsets = new List<string>();

        public IfcExportSetup Setup { get; private set; }
        public string PropertySetsEscolhido { get; private set; }
        public string MapeamentoEscolhido { get; private set; }
        public DB.ElementId Vista { get; private set; } = DB.ElementId.InvalidElementId;
        /// <summary>Fase escolhida (-1 = padrão do exportador: a última fase). Só vale para o modelo inteiro.</summary>
        public long Fase { get; private set; } = -1;
        public string PastaSaida { get; private set; }
        public string NomeArquivo { get; private set; }
        /// <summary>Lat/long do Project Base Point e campos atuais do Revit (recalculado depois da etapa Localização).</summary>
        public LocalizacaoIfc Localizacao { get; private set; }
        /// <summary>O que a etapa Localização, aberta daqui, gravou no Revit.</summary>
        public List<string> AlteracoesLocalizacao { get; } = new List<string>();
        /// <summary>
        /// EPSG em branco (padrão): o IFC sai sem código EPSG, sem IfcProjectedCRS e sem IfcMapConversion, mesmo com
        /// sistema GIS no Location, e a falta de dado GIS não é cobrada. Desligado, vale o EPSG do setup ou do exportador.
        /// </summary>
        public bool EpsgEmBranco => _epsgEmBranco.IsChecked == true;
        /// <summary>Sempre exportar os eixos (Grids) como IfcGrid.</summary>
        public bool SempreEixos => _eixos.IsChecked == true;
        private readonly CheckBox _eixos;
        private readonly CheckBox _epsgEmBranco;

        /// <summary>Documento da exportação.</summary>
        public DB.Document Documento => _doc;
        /// <summary>Embutida no passo Exportar da Entrega: o Exportar avisa por <see cref="Confirmada"/> em vez de fechar.</summary>
        public bool Embutida { get; }
        /// <summary>Embutida: as escolhas foram conferidas e é para exportar.</summary>
        public event Action Confirmada;
        /// <summary>Embutida: os avisos mudaram (a Entrega mostra de novo os cartões "O que falta").</summary>
        public event Action AvisosMudaram;

        /// <summary>Um aviso da exportação, para os cartões "O que falta" da Entrega: texto curto, explicação na dica e o botão que resolve.</summary>
        public class Aviso
        {
            public string Titulo { get; set; }
            public string Texto { get; set; }
            public string Dica { get; set; }
            public string Acao { get; set; }
            public Action Executar { get; set; }
        }

        /// <summary>A pessoa pediu para abrir o passo a passo da Entrega (a janela fecha sem exportar).</summary>
        public bool AbrirPassoAPasso { get; private set; }

        private class Opcao
        {
            public string Texto { get; set; }
            public DB.ElementId Id { get; set; }
            public override string ToString() => Texto;
        }

        /// <param name="resultadoForma">Resultado da atualização do Forma online feita ao abrir (null se não houve).</param>
        /// <param name="embutida">Dentro do passo Exportar da Entrega (sem Cancelar e sem o resumo da Entrega).</param>
        public ExportarIfcWindow(DB.Document doc, IfcExportSettings settings, string resultadoForma = null, bool embutida = false)
        {
            _doc = doc;
            _settings = settings;
            Embutida = embutida;

            Title = "MetroSP - Exportar IFC";
            Width = 660;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            MinWidth = 540;

            var corpo = new StackPanel();
            if (!embutida) corpo.Children.Add(ResumoEntrega());

            // ---------- Arquivo de coordenação (antes das classes: a pasta dele entra na frente das outras) ----------
            corpo.Children.Add(new Coordenacao.FaixaCoordenacao(doc, CoordenacaoMudou));

            // ---------- Classe ----------
            var secaoClasse = Theme.Secao("Perfil");
            secaoClasse.Margin = new Thickness(0, 0, 0, 5);
            corpo.Children.Add(secaoClasse);
            var linhaClasse = new DockPanel();
            _classe = new ComboBox { Padding = new Thickness(4, 3, 4, 3), Width = embutida ? 220 : 150, VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Perfil de exportação: carrega o setup, os property sets e o mapeamento dele de uma vez.\nOu \"Outros arquivos\" para indicar cada um." };
            DockPanel.SetDock(_classe, Dock.Left);
            linhaClasse.Children.Add(_classe);
            _seloClasse = Theme.Selo();
            _seloClasse.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(_seloClasse, Dock.Left);
            linhaClasse.Children.Add(_seloClasse);
            // Forma online: link discreto que abre o menu (Client ID, entrar, pasta).
            _botaoForma = Theme.Link("Forma online");
            _botaoForma.HorizontalAlignment = HorizontalAlignment.Right;
            _botaoForma.Margin = new Thickness(12, 0, 0, 0);
            _botaoForma.Click += (s, e) => MenuForma();
            var gerenciar = Theme.Link("Perfis...");
            gerenciar.Margin = new Thickness(12, 0, 0, 0);
            gerenciar.ToolTip = "Abre os perfis de exportação IFC: o setup, os property sets e o mapeamento de cada perfil.";
            gerenciar.Click += (s, e) => Gerenciar();
            DockPanel.SetDock(gerenciar, Dock.Right);
            linhaClasse.Children.Add(gerenciar);
            linhaClasse.Children.Add(_botaoForma);
            // Resumo do que o perfil carrega (setup, property sets e mapeamento); os caminhos ficam na dica.
            _resumoPerfil = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(10, 0, 0, 0) };
            linhaClasse.Children.Add(_resumoPerfil);
            corpo.Children.Add(linhaClasse);

            // ---------- Arquivos ----------
            var secaoArquivos = Theme.Secao("Arquivos da exportação");
            _secaoArquivos = secaoArquivos;
            corpo.Children.Add(secaoArquivos);
            var grupoArquivos = Theme.Grupo(out StackPanel arquivos);
            _grupoArquivos = grupoArquivos;
            corpo.Children.Add(grupoArquivos);
            _setup = ItemArquivo(arquivos, "Setup", "Trocar...", primeiro: true, out _arqSetup, out _seloSetup, out _infoSetup, out Button procurarSetup, out _);
            _psets = ItemArquivo(arquivos, "Property sets", "Trocar...", primeiro: false, out _arqPsets, out _seloPsets, out _infoPsets, out Button procurarPsets, out StackPanel acoesPsets);
            var editarPsets = new Button { Content = "Editar...", Margin = new Thickness(0, 0, 6, 0) };
            acoesPsets.Children.Insert(0, editarPsets);
            _mapeamento = ItemArquivo(arquivos, "Mapeamento", "Indicar...", primeiro: false, out _arqMapeamento, out _seloMapeamento, out _infoMapeamento, out Button procurarMapeamento, out _);

            // ---------- Saída: o que exportar e para onde ----------
            corpo.Children.Add(Theme.Secao("Saída"));
            var saida = new Grid();
            saida.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LarguraRotulo) });
            saida.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _vista = new ComboBox { Padding = new Thickness(4, 3, 4, 3),
                ToolTip = "Modelo inteiro, ou só o que está visível numa vista 3D (a vista também define a fase)." };
            LinhaGrade(saida, "Elementos", _vista);
            _fase = new ComboBox { Padding = new Thickness(4, 3, 4, 3) };
            LinhaGrade(saida, "Fase", _fase);
            var linhaArquivo = new DockPanel();
            var procurarSaida = new Button { Content = "Procurar...", Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(procurarSaida, Dock.Right);
            linhaArquivo.Children.Add(procurarSaida);
            _arquivo = new TextBox { VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Pasta e nome do arquivo .ifc" };
            linhaArquivo.Children.Add(_arquivo);
            LinhaGrade(saida, "Arquivo", linhaArquivo);
            corpo.Children.Add(saida);

            // ---------- Extras: projeto e localização (a etapa 1 grava no Revit; aqui só confere), fase e eixos ----------
            Localizacao = LocalizacaoIfc.Calcular(doc);
            corpo.Children.Add(Theme.Secao("Projeto e local"));
            corpo.Children.Add(Theme.Grupo(out StackPanel extras));
            var grade = new Grid { Margin = new Thickness(10, 6, 10, 6) };
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LarguraRotulo) });
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            extras.Children.Add(grade);

            // Localização: o Location do Revit pela lat/long do Project Base Point, gravado com um clique.
            var linhaLocal = new DockPanel();
            _gravarLocal = new Button { Content = "Gravar localização", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Grava no Location do Revit (Manage › Location) a latitude/longitude calculada pelo Project Base Point no PTL do Metrô.\n" +
                          "Não altera o Survey Point, o Project Base Point nem as Shared Coordinates." };
            _gravarLocal.Click += (s, e) => GravarLocalizacao();
            DockPanel.SetDock(_gravarLocal, Dock.Right);
            linhaLocal.Children.Add(_gravarLocal);
            _seloLocalizacao = Theme.Selo();
            _seloLocalizacao.HorizontalAlignment = HorizontalAlignment.Left;
            _seloLocalizacao.VerticalAlignment = VerticalAlignment.Center;
            linhaLocal.Children.Add(_seloLocalizacao);
            LinhaGrade(grade, "Localização", linhaLocal);
            // Na Entrega, a localização aparece no cartão "O que falta", com o botão Gravar localização.
            if (embutida) { linhaLocal.Visibility = Visibility.Collapsed; Rotulo(grade, linhaLocal).Visibility = Visibility.Collapsed; }

            // Nomes do IfcProject, IfcSite e IfcBuilding: gravados nos campos do Revit ao exportar, se mudarem.
            Ifc.DadosProjetoIfc atuais = Localizacao.DadosAtuais;
            TextBox Nome(string valor, string dica) => new TextBox { Text = valor ?? "", VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(3, 2, 3, 2), ToolTip = dica };
            _ifcProject = Nome(atuais.Projeto?.Nome, "Nome do IfcProject (número do projeto).\nNo Revit: Informações do projeto › Número do projeto. Gravado ao exportar, se mudar.");
            _ifcSite = Nome(atuais.Terreno?.Nome, "Nome do IfcSite (o terreno, ex.: a linha ou o trecho).\nNo Revit: Informações do projeto › SiteName. Gravado ao exportar, se mudar.");
            _ifcBuilding = Nome(atuais.Edificio?.Nome, "Nome do IfcBuilding (ex.: a estação).\nNo Revit: Informações do projeto › Nome do edifício. Gravado ao exportar, se mudar.");
            foreach (TextBox t in new[] { _ifcProject, _ifcSite, _ifcBuilding }) t.TextChanged += (s, e) => AtualizarResumo();
            LinhaGrade(grade, "IfcProject", _ifcProject);
            LinhaGrade(grade, "IfcSite", _ifcSite);
            var linhaBuilding = new DockPanel();
            var maisDados = Theme.Link("Mais dados...");
            maisDados.Margin = new Thickness(10, 0, 0, 0);
            maisDados.VerticalAlignment = VerticalAlignment.Center;
            maisDados.ToolTip = "Nome longo, descrição, fase e endereço de cada um, e o Pset MetroSP_Projeto.";
            maisDados.Click += (s, e) => AbrirLocalizacao();
            DockPanel.SetDock(maisDados, Dock.Right);
            linhaBuilding.Children.Add(maisDados);
            linhaBuilding.Children.Add(_ifcBuilding);
            LinhaGrade(grade, "IfcBuilding", linhaBuilding);

            _epsgEmBranco = new CheckBox
            {
                Content = "EPSG em branco",
                Margin = new Thickness(16, 0, 0, 0),
                IsChecked = settings.EpsgEmBranco,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Ligado: o IFC sai sem código EPSG, IfcProjectedCRS e IfcMapConversion (coordenadas no PTL), mesmo que o Location tenha sistema GIS" +
                          (Localizacao.SistemaGis != null ? $" (\"{Localizacao.SistemaGis}\")" : "") +
                          ".\nDesligado: vale o EPSG do setup ou o que o exportador gravar pelo sistema GIS.",
            };


            _eixos = new CheckBox
            {
                Content = "Eixos sempre",
                IsChecked = settings.SempreEixos,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Sempre exportar os eixos (Grids): os eixos do Revit saem no IFC como IfcGrid mesmo exportando só o visível numa vista 3D, " +
                          "e mesmo que a tabela de categorias diga para não exportar a categoria Grids.",
            };
            var opcoes = new DockPanel { LastChildFill = false };
            var correcoes = Theme.Link("O que o MetroSP corrige no IFC...");
            correcoes.Margin = new Thickness(16, 0, 0, 0);
            correcoes.ToolTip = "Depois do exportador do Revit, o MetroSP completa o IFC: classe dos proxies, lances, quantidades (Qto e MetroQto), " +
                                "sistemas MEP, materiais, armaduras, localização e dados do projeto.";
            correcoes.Click += (s, e) => CorrecoesIfcWindow.Mostrar(Window.GetWindow(correcoes));
            DockPanel.SetDock(correcoes, Dock.Right);
            opcoes.Children.Add(correcoes);
            opcoes.Children.Add(_eixos);
            opcoes.Children.Add(_epsgEmBranco);
            LinhaGrade(grade, "Opções", opcoes);

            PreencherVistas();
            SelecionarVista(settings.Vista);
            _vista.SelectionChanged += (s, e) => { if (_vista.SelectedItem != null) { MostrarFase(); AtualizarResumo(); } };
            Digitavel(_vista);
            Digitavel(_classe);
            string pastaSaida = settings.PastaSaida ?? string.Empty;
            _arquivo.Text = pastaSaida.Length > 0 ? Path.Combine(pastaSaida, NomePadrao(doc) + ".ifc") : NomePadrao(doc) + ".ifc";

            // Restaura as últimas escolhas sem trocar os .txt pelos do setup.
            _setup.Text = settings.Setup ?? string.Empty;
            _psets.Text = settings.PropertySets ?? string.Empty;
            _mapeamento.Text = settings.Mapeamento ?? string.Empty;
            CarregarSetup(preencherTxt: string.IsNullOrEmpty(settings.PropertySets));

            // Classe: aplicada de novo ao abrir, para pegar os arquivos instalados mais recentes.
            PreencherClasses(settings.Classe);
            ClasseDoNome();
            ClasseDaCoordenacao();
            OpcoesDaCoordenacao();
            _classe.SelectionChanged += (s, e) => { if (_classe.SelectedItem == null) return; _motivoClasse = null; AplicarClasse(); };
            MostrarForma(resultadoForma);
            if (_classe.SelectedItem is ClassesIfc.Classe) AplicarClasse();
            else MostrarClasse(_classe.Items.Count > 1 ? null : "Nenhum perfil instalado: indique os arquivos.");
            editarPsets.Click += (s, e) => EditarPsets();

            procurarSetup.Click += (s, e) =>
            {
                string f = ProcurarArquivo(_setup.Text, "Setup de exportação IFC (*.json)|*.json|Todos os arquivos (*.*)|*.*", "Onde está o setup de exportação (.json)?");
                if (f == null) return;
                ParaOutros();
                _setup.Text = f;
                CarregarSetup(preencherTxt: true);
            };
            _setup.LostFocus += (s, e) => { if (Setup?.Arquivo != _setup.Text.Trim()) { ParaOutros(); CarregarSetup(preencherTxt: true); } };
            procurarPsets.Click += (s, e) =>
            {
                string f = ProcurarArquivo(Inicial(_psets.Text), "Texto (*.txt)|*.txt|Todos os arquivos (*.*)|*.*", "Onde está o .txt de property sets?");
                if (f != null) { ParaOutros(); _psets.Text = f; ConferirPsets(); }
            };
            _psets.LostFocus += (s, e) =>
            {
                if (ClasseEscolhida != null && !string.Equals(TextoPsets(), ClassesIfc.Obter(ClasseEscolhida.Nome).PropertySets ?? "", StringComparison.OrdinalIgnoreCase))
                    ParaOutros();
                ConferirPsets();
            };
            procurarMapeamento.Click += (s, e) =>
            {
                string f = ProcurarArquivo(Inicial(_mapeamento.Text), "Texto (*.txt)|*.txt|Todos os arquivos (*.*)|*.*", "Onde está o .txt de mapeamento de parâmetros?");
                if (f != null) { ParaOutros(); _mapeamento.Text = f; ConferirMapeamento(); }
            };
            _mapeamento.LostFocus += (s, e) => ConferirMapeamento();
            procurarSaida.Click += (s, e) => SalvarComo();

            var exportar = new Button { Content = "Exportar", IsDefault = true, Style = Theme.PrimaryButton };
            exportar.Click += (s, e) => Confirmar();
            var cancelar = new Button { Content = "Cancelar", IsCancel = true, Visibility = embutida ? Visibility.Collapsed : Visibility.Visible };
            _resumo = new TextBlock { FontSize = 11.5 };

            MostrarLocalizacao();
            MostrarCaminhos();
            // Na Entrega, o Exportar fica no rodapé dela e os avisos nos cartões "O que falta".
            Theme.Apply(this, "Exportar IFC", embutida ? "perfil, saída e dados do projeto" : "Indique o setup e os arquivos de mapeamento de cada exportação",
                corpo, embutida ? null : Theme.Rodape(_resumo, cancelar, exportar));
            AtualizarResumo();
        }

        /// <summary>Janela Arquivos IFC; se algo mudou, recarrega as classes e os arquivos da escolhida.</summary>
        private void Gerenciar()
        {
            string nome = ClasseEscolhida?.Nome;
            var w = new ArquivosIfcWindow(_doc, nome) { Owner = this };
            w.ShowDialog();
            if (!w.Mudou) return;
            PreencherClasses(nome);
            if (_classe.SelectedItem is ClassesIfc.Classe) AplicarClasse();
        }

        private ModeloIfc _modeloIfc;

        /// <summary>Retrato do modelo para a coluna "No modelo" do editor (lido na primeira vez).</summary>
        private ModeloIfc ModeloParaConferir()
        {
            if (_modeloIfc == null)
            {
                if (_parametrosModelo == null) _parametrosModelo = ParametrosDoModelo(_doc);
                System.Windows.Input.Cursor antes = System.Windows.Input.Mouse.OverrideCursor;
                System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                try { _modeloIfc = new ModeloIfc(_doc, _parametrosModelo); }
                catch { /* sem a conferência por elemento, o editor confere só os nomes */ }
                finally { System.Windows.Input.Mouse.OverrideCursor = antes; }
            }
            return _modeloIfc;
        }

        private void PreencherClasses(string lembrada)
        {
            _aplicandoClasse = true;
            _classe.Items.Clear();
            foreach (ClassesIfc.Classe c in ClassesIfc.Listar()) _classe.Items.Add(c);
            _classe.Items.Add(Outros);
            _classe.SelectedItem = _classe.Items.OfType<ClassesIfc.Classe>()
                .FirstOrDefault(c => string.Equals(c.Nome, lembrada, StringComparison.CurrentCultureIgnoreCase)) ?? (object)Outros;
            _aplicandoClasse = false;
        }

        private ClassesIfc.Classe ClasseEscolhida => _classe.SelectedItem as ClassesIfc.Classe;

        /// <summary>Com arquivo de coordenação no modelo, vale a classe do perfil ativo.</summary>
        private void ClasseDaCoordenacao()
        {
            string nome = Coordenacao.CoordenacaoRevit.ClasseAtiva(_doc);
            ClassesIfc.Classe c = nome == null ? null : _classe.Items.OfType<ClassesIfc.Classe>()
                .FirstOrDefault(x => string.Equals(x.Nome, nome, StringComparison.CurrentCultureIgnoreCase));
            if (c == null) return;
            _aplicandoClasse = true;
            _classe.SelectedItem = c;
            _aplicandoClasse = false;
            _motivoClasse = null;
        }

        /// <summary>Abriu, trocou o perfil ou tirou o arquivo de coordenação: lista as classes de novo e recarrega os arquivos.</summary>
        private void CoordenacaoMudou()
        {
            PreencherClasses(ClasseEscolhida?.Nome);
            ClasseDaCoordenacao();
            if (ClasseEscolhida != null) AplicarClasse();
            else MostrarCaminhos();
            OpcoesDaCoordenacao();
            AtualizarResumo();
        }

        /// <summary>Elementos, nome do arquivo, EPSG e eixos do perfil do arquivo de coordenação (o que ele não define fica como está).</summary>
        private void OpcoesDaCoordenacao()
        {
            Coordenacao.OpcoesExportacao o = Coordenacao.CoordenacaoRevit.ClasseAtiva(_doc) == null ? null : Coordenacao.CoordenacaoRevit.Atual?.Perfil?.Exportacao;
            if (o == null) return;
            const string dica = "\n\nEscolha do arquivo de coordenação.";
            if (o.Elementos != null && SelecionarVista(o.Elementos)) _vista.ToolTip = "Elementos exportados." + dica;
            if (o.EpsgEmBranco.HasValue) { _epsgEmBranco.IsChecked = o.EpsgEmBranco.Value; _epsgEmBranco.ToolTip += dica; }
            if (o.SempreEixos.HasValue) { _eixos.IsChecked = o.SempreEixos.Value; _eixos.ToolTip += dica; }
            string nome = o.NomePara(NomePadrao(_doc));
            if (!string.IsNullOrEmpty(nome) && nome.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
            {
                string pasta = "";
                try { pasta = Path.GetDirectoryName(_arquivo.Text.Trim()) ?? ""; } catch { }
                _arquivo.Text = pasta.Length > 0 ? Path.Combine(pasta, nome + ".ifc") : nome + ".ifc";
                _arquivo.ToolTip = "Pasta e nome do arquivo .ifc (nome do arquivo de coordenação).";
            }
        }

        /// <summary>Escolhe "Modelo inteiro" ou a vista 3D pelo nome. Retorna se achou.</summary>
        private bool SelecionarVista(string nome)
        {
            if (string.IsNullOrWhiteSpace(nome)) return false;
            foreach (Opcao o in _vista.Items.OfType<Opcao>())
            {
                string n = o.Id == DB.ElementId.InvalidElementId ? Coordenacao.OpcoesExportacao.ModeloInteiro : _doc.GetElement(o.Id)?.Name;
                if (!string.Equals(n, nome.Trim(), StringComparison.CurrentCultureIgnoreCase)) continue;
                _vista.SelectedItem = o;
                return true;
            }
            return false;
        }

        /// <summary>Se o nome do modelo indica uma classe (Nomenclatura.txt ou trecho do nome), ela é escolhida.</summary>
        private void ClasseDoNome()
        {
            // Padrão escolhido para este modelo (aqui ou no passo Padrões da Entrega) vale antes da regra do nome.
            string guardado = _settings.PadraoDo(_doc.Title);
            if (guardado != null)
            {
                object item = guardado == IfcExportSettings.SemPadrao ? Outros
                    : (object)_classe.Items.OfType<ClassesIfc.Classe>().FirstOrDefault(x => string.Equals(x.Nome, guardado, StringComparison.CurrentCultureIgnoreCase));
                if (item != null)
                {
                    _aplicandoClasse = true;
                    _classe.SelectedItem = item;
                    _aplicandoClasse = false;
                    _motivoClasse = null;
                    return;
                }
            }
            (ClassesIfc.Classe c, string motivo) = ClassesIfc.DoNomeDoModelo(_doc.Title, _classe.Items.OfType<ClassesIfc.Classe>().ToList());
            if (c == null) return;
            _aplicandoClasse = true;
            _classe.SelectedItem = c;
            _aplicandoClasse = false;
            _motivoClasse = motivo;
        }

        /// <summary>Mostra a situação do Forma online no link (o detalhe vai na dica).</summary>
        public void MostrarForma(string resultadoSync = null)
        {
            FormaOnline.Pasta pasta = FormaOnline.PastaEscolhida();
            string texto, dica;
            if (!FormaOnline.Configurado)
            {
                texto = "Forma online: desligado";
                dica = "Informe o Client ID do app do MetroSP na Autodesk para usar uma pasta do Forma sem o Desktop Connector.";
            }
            else if (!FormaOnline.Conectado)
            {
                texto = "Forma online: entrar";
                dica = "Entre com a conta Autodesk para usar uma pasta do Forma sem o Desktop Connector.";
            }
            else if (pasta == null)
            {
                texto = "Forma online: escolher pasta";
                dica = "Conectado. Escolha a pasta com as configurações de IFC.";
            }
            else
            {
                texto = "Forma: " + pasta.Descricao.Split('›').Last().Trim();
                dica = pasta.Descricao + (resultadoSync != null ? "\n" + resultadoSync : "") + "\nClique para atualizar, trocar a pasta ou sair.";
            }
            _botaoForma.Content = texto;
            _botaoForma.ToolTip = dica;
            _botaoForma.Foreground = Theme.Solid(resultadoSync != null && !char.IsDigit(resultadoSync[0]) ? Theme.LaranjaTexto : System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55));
        }

        private void MenuForma()
        {
            try
            {
                if (!FormaOnline.Configurado)
                {
                    if (!InformarClientId()) { MostrarForma(); return; }
                }
                if (!FormaOnline.Conectado)
                {
                    using (var p = Progresso.Abrir("Entrar na Autodesk", podeCancelar: true))
                    {
                        p.Etapa("Conclua o login no navegador que abriu.");
                        FormaOnline.Entrar(() => p.Cancelado);
                    }
                    if (FormaOnline.PastaEscolhida() == null) EscolherPastaForma();
                }
                else if (FormaOnline.PastaEscolhida() == null) EscolherPastaForma();
                else
                {
                    var menu = new ContextMenu { PlacementTarget = _botaoForma, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
                    void Opcao(string texto, Action acao)
                    {
                        var m = new MenuItem { Header = texto };
                        m.Click += (s, e) => { try { acao(); } catch (Exception ex) { ErroForma(ex); } };
                        menu.Items.Add(m);
                    }
                    Opcao("Atualizar agora", AtualizarForma);
                    Opcao("Trocar pasta...", EscolherPastaForma);
                    Opcao("Parar de usar o Forma online", () => { FormaOnline.EscolherPasta(null); RecarregarClasses(); MostrarForma(); });
                    Opcao("Sair da conta Autodesk", () => { FormaOnline.Sair(); RecarregarClasses(); MostrarForma(); });
                    Opcao("Client ID...", () => { if (InformarClientId()) { RecarregarClasses(); MostrarForma(); } });
                    menu.IsOpen = true;
                    return;
                }
            }
            catch (Exception ex) { ErroForma(ex); }
            MostrarForma();
        }

        /// <summary>Pede o Client ID; true se mudou.</summary>
        private bool InformarClientId()
        {
            string antes = FormaOnline.ClientId;
            var w = new FormaClientIdWindow { Owner = this };
            if (w.ShowDialog() != true) return false;
            FormaOnline.GravarClientId(w.ClientId);
            return FormaOnline.ClientId != antes;
        }

        private void ErroForma(Exception ex)
        {
            if (ex is OperationCanceledException) return;
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void EscolherPastaForma()
        {
            var w = new FormaPastaWindow { Owner = this };
            if (w.ShowDialog() != true) return;
            FormaOnline.EscolherPasta(w.Escolhida);
            AtualizarForma();
        }

        private void AtualizarForma()
        {
            FormaOnline.ResultadoSync r;
            using (var p = Progresso.Abrir("Forma online"))
            {
                p.Etapa("Baixando as configurações de IFC do Forma");
                r = FormaOnline.Sincronizar();
            }
            RecarregarClasses();
            MostrarForma(r.ToString());
        }

        /// <summary>Lista as classes de novo (depois de baixar do Forma) mantendo a escolhida, ou a indicada pelo nome.</summary>
        private void RecarregarClasses()
        {
            string atual = ClasseEscolhida?.Nome;
            PreencherClasses(atual);
            if (atual == null || ClasseEscolhida == null) ClasseDoNome();
            if (ClasseEscolhida != null) AplicarClasse();
        }

        /// <summary>Preenche os três arquivos com os da classe; o que a classe não tiver fica como está.</summary>
        private void AplicarClasse()
        {
            if (_aplicandoClasse) return;
            ClassesIfc.Classe c = ClasseEscolhida;
            if (c == null)
            {
                MostrarClasse(null);
                MostrarCaminhos();
                return;
            }
            c = ClassesIfc.Obter(c.Nome);
            _aplicandoClasse = true;
            try
            {
                if (c.Setup != null) _setup.Text = c.Setup;
                CarregarSetup(preencherTxt: false);
                _psets.Text = c.PropertySets ?? string.Empty;
                _mapeamento.Text = c.Mapeamento ?? string.Empty;
                ConferirPsets();
                ConferirMapeamento();
            }
            finally { _aplicandoClasse = false; }

            var faltam = new List<string>();
            if (c.Setup == null) faltam.Add("setup (vale o último escolhido)");
            if (c.PropertySets == null) faltam.Add("property sets");
            string origem = c.DaCoordenacao ? "da coordenação" : c.Editada ? "editado por você" : c.DoForma ? "do Forma online" : "instalado";
            string selo = _motivoClasse != null ? "pelo nome do modelo" : origem;
            string dica = (_motivoClasse != null ? $"Perfil indicado {_motivoClasse}.\n" : "") +
                          (c.DaCoordenacao ? "Setup e property sets do arquivo de coordenação do modelo." :
                           c.Editada ? "Property sets editados por você (o perfil não foi alterado)."
                           : c.DoForma ? "Property sets da pasta do Forma online." : "Configuração instalada com o MetroSP.") +
                          (faltam.Count > 0 ? "\nO perfil não traz: " + string.Join(", ", faltam) + "." : "");
            if (_motivoClasse != null && origem != "instalado") selo += " · " + origem;
            MostrarClasse(selo, dica);
            MostrarCaminhos();
        }

        private void MostrarClasse(string selo, string dica = null) =>
            Theme.MudarSelo(_seloClasse, selo, Theme.Estado.Neutro, dica);

        /// <summary>Em "Outros arquivos" os caminhos aparecem para editar; numa classe ficam só na dica do nome.</summary>
        private void MostrarCaminhos()
        {
            bool outros = ClasseEscolhida == null;
            foreach (FrameworkElement f in _caminhos) f.Visibility = outros ? Visibility.Visible : Visibility.Collapsed;
            // Na Entrega, com um perfil, os arquivos ficam só no resumo ao lado dele (trocar é em Perfis...).
            Visibility arquivos = Embutida && !outros ? Visibility.Collapsed : Visibility.Visible;
            if (_secaoArquivos != null) _secaoArquivos.Visibility = arquivos;
            if (_grupoArquivos != null) _grupoArquivos.Visibility = arquivos;
            AtualizarResumo();
        }

        /// <summary>Monta um item da lista de arquivos e devolve a caixa com o caminho.</summary>
        private TextBox ItemArquivo(StackPanel lista, string nome, string acao, bool primeiro,
            out TextBlock arquivo, out Border selo, out TextBlock info, out Button procurar, out StackPanel acoes)
        {
            arquivo = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, FontFamily = new System.Windows.Media.FontFamily("Consolas") };
            selo = Theme.Selo();
            procurar = new Button { Content = acao };
            acoes = new StackPanel { Orientation = Orientation.Horizontal };
            acoes.Children.Add(procurar);
            info = Theme.Detalhe();
            var caminho = new TextBox { VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
            _caminhos.Add(caminho);
            var detalhe = new StackPanel();
            detalhe.Children.Add(info);
            detalhe.Children.Add(caminho);
            TextBlock a = arquivo;
            caminho.TextChanged += (s, e) =>
            {
                string t = caminho.Text.Trim();
                a.Text = t.Length == 0 ? "nenhum" : Path.GetFileName(t);
                a.Foreground = Theme.Solid(t.Length == 0 ? Theme.Cinza : Theme.Preto);
                a.ToolTip = t.Length == 0 ? null : t;
            };
            arquivo.Text = "nenhum";
            arquivo.Foreground = Theme.Solid(Theme.Cinza);
            lista.Children.Add(Theme.Item(nome, arquivo, selo, acoes, detalhe, primeiro));
            return caminho;
        }

        /// <summary>Soma os avisos no rodapé, ao lado do Exportar.</summary>
        private void AtualizarResumo()
        {
            if (_resumo == null) return;
            var avisos = new List<string>();
            if (Setup == null) avisos.Add("setup não indicado");
            else if (!Setup.VersaoAceita) avisos.Add("setup não é " + OpcoesSetup.VersoesAceitas);
            if (_seloPsets.Visibility == Visibility.Visible && ((TextBlock)_seloPsets.Child).Foreground is System.Windows.Media.SolidColorBrush bp && bp.Color == Theme.LaranjaTexto)
                avisos.Add("property sets: " + ((TextBlock)_seloPsets.Child).Text);
            if (_seloMapeamento.Visibility == Visibility.Visible && ((TextBlock)_seloMapeamento.Child).Foreground is System.Windows.Media.SolidColorBrush bm && bm.Color == Theme.LaranjaTexto)
                avisos.Add("mapeamento não encontrado");
            if (Localizacao != null && (Localizacao.Erro != null || !Localizacao.LocationCorreto)) avisos.Add(Localizacao.Erro != null ? "Location não conferido" : "Location fora do Project Base Point");
            if (_ifcProject != null && string.IsNullOrWhiteSpace(_ifcProject.Text)) avisos.Add("IfcProject sem nome");
            Theme.MudarResumo(_resumo, avisos, "Pronto para exportar");
            MostrarResumoPerfil();
            AvisosMudaram?.Invoke();
        }

        /// <summary>Ao lado do perfil: o setup, quantos psets e o mapeamento; os caminhos completos na dica.</summary>
        private void MostrarResumoPerfil()
        {
            if (_resumoPerfil == null || _psets == null || _mapeamento == null) return;
            bool outros = ClasseEscolhida == null;
            _resumoPerfil.Visibility = outros ? Visibility.Collapsed : Visibility.Visible;
            if (outros) return;
            string psets = TextoPsets(), mapa = _mapeamento.Text.Trim();
            var partes = new List<string>
            {
                Setup?.Nome ?? "sem setup",
                psets.Length == 0 ? "sem property sets" : Path.GetFileNameWithoutExtension(psets),
                mapa.Length == 0 ? "sem mapeamento" : "com mapeamento",
            };
            _resumoPerfil.Text = string.Join(" · ", partes);
            _resumoPerfil.ToolTip = "Setup: " + (Setup?.Arquivo ?? "nenhum") + "\nProperty sets: " + (psets.Length == 0 ? "nenhum" : psets) +
                                    "\nMapeamento: " + (mapa.Length == 0 ? "nenhum (parâmetros com os nomes do Revit)" : mapa) +
                                    "\n\nPara trocar os arquivos: Perfis..., ou \"" + Outros + "\" na lista.";
        }

        /// <summary>Resumo de uma linha para o rodapé da Entrega: perfil, elementos e fase.</summary>
        public string ResumoCurto
        {
            get
            {
                var partes = new List<string> { ClasseEscolhida != null ? "Perfil " + ClasseEscolhida.Nome : "Outros arquivos" };
                if (_vista?.SelectedItem != null) partes.Add(_vista.SelectedItem.ToString());
                if (_fase?.SelectedItem is ComboBoxItem f) partes.Add(f.Content?.ToString().Replace("Padrão do exportador", "fase") ?? "");
                return string.Join(" · ", partes.Where(p => p.Length > 0));
            }
        }

        /// <summary>
        /// Avisos para os cartões "O que falta" da Entrega (o mesmo que o resumo do rodapé, com o botão que resolve).
        /// </summary>
        public List<Aviso> Avisos()
        {
            var r = new List<Aviso>();
            if (Setup == null)
                r.Add(new Aviso { Titulo = "Setup não indicado", Texto = "Sem setup (.json) não dá para exportar.", Dica = _infoSetup.Text,
                    Acao = "Perfis...", Executar = Gerenciar });
            else if (!Setup.VersaoAceita)
                r.Add(new Aviso { Titulo = "Setup em " + Setup.DescricaoVersao, Texto = "A exportação MetroSP é em " + OpcoesSetup.VersoesAceitas + ".",
                    Dica = "Escolha ou salve um setup " + OpcoesSetup.VersoesAceitas + " (por exemplo IFC 4 Reference View).", Acao = "Perfis...", Executar = Gerenciar });
            if (EmAviso(_seloPsets))
                r.Add(new Aviso { Titulo = "Property sets: " + ((TextBlock)_seloPsets.Child).Text, Texto = _infoPsets.Text,
                    Dica = _seloPsets.ToolTip as string, Acao = File.Exists(TextoPsets()) ? "Editar..." : "Perfis...",
                    Executar = File.Exists(TextoPsets()) ? (Action)EditarPsets : Gerenciar });
            if (EmAviso(_seloMapeamento))
                r.Add(new Aviso { Titulo = "Mapeamento não encontrado", Texto = Path.GetFileName(_mapeamento.Text.Trim()),
                    Dica = _mapeamento.Text.Trim(), Acao = "Perfis...", Executar = Gerenciar });
            if (Localizacao != null && Localizacao.Erro != null)
                r.Add(new Aviso { Titulo = "Localização não conferida", Texto = "Não deu para calcular pelo Project Base Point.", Dica = Localizacao.Erro });
            else if (Localizacao != null && !Localizacao.LocationCorreto)
                r.Add(new Aviso { Titulo = "Localização não gravada", Texto = "O Location do Revit não confere com o Project Base Point.",
                    Dica = _seloLocalizacao.ToolTip as string, Acao = "Gravar localização", Executar = GravarLocalizacao });
            if (_ifcProject != null && string.IsNullOrWhiteSpace(_ifcProject.Text))
                r.Add(new Aviso { Titulo = "IfcProject sem nome", Texto = "Preencha o IfcProject ao lado (número do projeto)." });
            return r;
        }

        private static bool EmAviso(Border selo) =>
            selo != null && selo.Visibility == Visibility.Visible && selo.Child is TextBlock t &&
            t.Foreground is System.Windows.Media.SolidColorBrush b && b.Color == Theme.LaranjaTexto;

        /// <summary>Lista digitável: filtra pelo começo do nome; se o texto não bate com nada, volta para a última escolha ao sair.</summary>
        private static void Digitavel(ComboBox c)
        {
            c.IsEditable = true;
            c.IsTextSearchEnabled = true;
            object ultima = c.SelectedItem;
            c.SelectionChanged += (s, e) => { if (c.SelectedItem != null) ultima = c.SelectedItem; };
            c.LostKeyboardFocus += (s, e) =>
            {
                if (c.IsKeyboardFocusWithin || c.SelectedItem != null || ultima == null) return;
                c.SelectedItem = ultima;
            };
        }

        private void SalvarComo()
        {
            string atual = _arquivo.Text.Trim();
            var dialogo = new Microsoft.Win32.SaveFileDialog { Filter = "IFC (*.ifc)|*.ifc", Title = "Salvar o IFC como", OverwritePrompt = false };
            try
            {
                string pasta = Path.GetDirectoryName(atual);
                if (!string.IsNullOrEmpty(pasta) && Directory.Exists(pasta)) dialogo.InitialDirectory = pasta;
                dialogo.FileName = Path.GetFileName(atual);
            }
            catch { /* caminho inválido digitado */ }
            if (dialogo.ShowDialog(this) == true) _arquivo.Text = dialogo.FileName;
        }

        /// <summary>Arquivo trocado à mão: deixa de valer a classe.</summary>
        private void ParaOutros()
        {
            if (_aplicandoClasse || ClasseEscolhida == null) return;
            _aplicandoClasse = true;
            _classe.SelectedItem = Outros;
            MostrarClasse(null);
            MostrarCaminhos();
            _aplicandoClasse = false;
        }

        /// <summary>
        /// Abre o .txt de property sets em tabela. Numa classe instalada, salvar grava a cópia do usuário;
        /// num arquivo indicado à mão, pergunta onde salvar.
        /// </summary>
        private void EditarPsets()
        {
            string caminho = TextoPsets();
            if (caminho.Length == 0 || !File.Exists(caminho))
            {
                MessageBox.Show(this, "Escolha um perfil ou indique um .txt de property sets que exista.", Title);
                return;
            }
            ClassesIfc.Classe c = ClasseEscolhida;
            bool daClasse = c != null && string.Equals(caminho, ClassesIfc.Obter(c.Nome).PropertySets, StringComparison.OrdinalIgnoreCase);
            if (_parametrosModelo == null) _parametrosModelo = ParametrosDoModelo(_doc);

            EditarPsetsWindow editor;
            try
            {
                editor = new EditarPsetsWindow(caminho, _parametrosModelo,
                    destino: daClasse ? ClassesIfc.CaminhoUsuario(c.Nome) : null,
                    podeRestaurar: daClasse && c.Editada && ClassesIfc.CaminhoPadrao(c.Nome) != null, modelo: ModeloParaConferir()) { Owner = this };
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Erro ao ler o .txt: " + ex.Message, Title);
                return;
            }
            if (editor.ShowDialog() != true) return;

            if (daClasse)
            {
                if (editor.Restaurou) ClassesIfc.RestaurarPadrao(c.Nome);
                PreencherClasses(c.Nome);
                AplicarClasse();
                if (editor.ArquivoSalvo != null && !string.Equals(editor.ArquivoSalvo, ClassesIfc.CaminhoUsuario(c.Nome), StringComparison.OrdinalIgnoreCase))
                {
                    // "Salvar como" para outro lugar: passa a usar esse arquivo.
                    ParaOutros();
                    _psets.Text = editor.ArquivoSalvo;
                }
            }
            else if (editor.ArquivoSalvo != null)
            {
                _psets.Text = editor.ArquivoSalvo;
            }
            ConferirPsets();
        }

        /// <summary>Pasta inicial do "Procurar": a do arquivo atual, senão a do setup.</summary>
        private string Inicial(string atual) =>
            !string.IsNullOrWhiteSpace(atual) ? atual : _setup.Text;

        /// <summary>
        /// Lê o setup. Com preencherTxt, os .txt indicados no setup (resolvidos nesta máquina: caminho gravado,
        /// mesmo nome do setup ou achados pelo nome perto dele) preenchem os campos; o usuário pode trocar.
        /// </summary>
        private void CarregarSetup(bool preencherTxt)
        {
            try { CarregarSetupArquivo(preencherTxt); }
            finally { MostrarFase(); }
        }

        private void CarregarSetupArquivo(bool preencherTxt)
        {
            Setup = null;
            string arquivo = _setup.Text.Trim();
            if (arquivo.Length == 0)
            {
                Mostrar(_seloSetup, _infoSetup, "falta indicar", Theme.Estado.Aviso,
                    "No Revit: Exportar › IFC › Modificar configuração › Salvar configuração. Depois indique o .json aqui.");
                ConferirPsets();
                ConferirMapeamento();
                return;
            }
            if (!File.Exists(arquivo))
            {
                Mostrar(_seloSetup, _infoSetup, "não encontrado", Theme.Estado.Aviso, "");
                ConferirPsets();
                ConferirMapeamento();
                return;
            }
            try
            {
                // Setup dentro de uma pasta "Setups": a de cima é tratada como estrutura padrão,
                // e o .txt com o mesmo nome é procurado em PropertySets\ e Mapeamentos\.
                string pasta = Path.GetDirectoryName(arquivo);
                if (string.Equals(Path.GetFileName(pasta), FormaPaths.SubSetups, StringComparison.OrdinalIgnoreCase))
                    pasta = Path.GetDirectoryName(pasta);
                Setup = IfcExportSetup.Load(arquivo, pasta);
            }
            catch (Exception ex)
            {
                Mostrar(_seloSetup, _infoSetup, "erro ao ler", Theme.Estado.Aviso, "Não foi possível ler o setup: " + ex.Message);
                ConferirPsets();
                ConferirMapeamento();
                return;
            }

            var partes = new List<string> { Setup.Nome };
            if (Setup.SoVisiveisNaVista) partes.Add("só elementos visíveis na vista");
            if (Setup.GravaGuid) partes.Add("grava o GUID IFC nos elementos");
            Mostrar(_seloSetup, _infoSetup, Setup.DescricaoVersao, Setup.VersaoAceita ? Theme.Estado.Ok : Theme.Estado.Aviso, string.Join(" · ", partes));

            if (preencherTxt)
            {
                if (Setup.PropertySets.Caminho != null) _psets.Text = Setup.PropertySets.Caminho;
                if (Setup.MapeamentoParametros.Caminho != null) _mapeamento.Text = Setup.MapeamentoParametros.Caminho;
            }

            // Vista: se o setup filtra pela vista, sugere a vista 3D ativa
            if (Setup.SoVisiveisNaVista && _vista.SelectedIndex == 0 && _vista.Items.Count > 1)
            {
                DB.ElementId ativa = _doc.ActiveView?.Id;
                _vista.SelectedItem = _vista.Items.Cast<Opcao>().FirstOrDefault(o => o.Id == ativa) ?? _vista.Items[1];
            }
            ConferirPsets();
            ConferirMapeamento();
        }

        /// <summary>Selo e detalhe de um item de arquivo; depois refaz o resumo do rodapé.</summary>
        private void Mostrar(Border selo, TextBlock info, string textoSelo, Theme.Estado estado, string detalhe, string dica = null)
        {
            Theme.MudarSelo(selo, textoSelo, estado, dica);
            info.Text = detalhe ?? "";
            info.Visibility = string.IsNullOrEmpty(detalhe) ? Visibility.Collapsed : Visibility.Visible;
            AtualizarResumo();
        }

        private void ConferirMapeamento()
        {
            string caminho = _mapeamento.Text.Trim();
            bool doSetup = Setup != null && string.Equals(caminho, Setup.MapeamentoParametros.Caminho, StringComparison.OrdinalIgnoreCase);
            if (caminho.Length == 0) Mostrar(_seloMapeamento, _infoMapeamento, "nomes do Revit", Theme.Estado.Neutro, "Opcional. Sem mapeamento, os parâmetros saem com os nomes do Revit.");
            else if (!File.Exists(caminho)) Mostrar(_seloMapeamento, _infoMapeamento, "não encontrado", Theme.Estado.Aviso, "");
            else Mostrar(_seloMapeamento, _infoMapeamento, "ok", Theme.Estado.Ok, doSetup ? $"Indicado pelo setup ({Setup.MapeamentoParametros.Situacao})." : "");
        }

        /// <summary>Lê o .txt com o mesmo leitor do Verificar IDS e procura os parâmetros no modelo.</summary>
        private void ConferirPsets()
        {
            _avisosPsets = new List<string>();
            string caminho = TextoPsets();
            if (caminho.Length == 0)
            {
                string motivo = Setup == null ? "" :
                    Setup.ExportaPsetsUsuario ? $"O setup pede psets do usuário, mas o .txt não foi achado ({Setup.PropertySets.Situacao})."
                    : "O setup não usa psets do usuário. Escolha um .txt para incluí-los.";
                bool pede = Setup != null && Setup.ExportaPsetsUsuario;
                Mostrar(_seloPsets, _infoPsets, pede ? "falta indicar" : "nenhum", pede ? Theme.Estado.Aviso : Theme.Estado.Neutro, motivo);
                return;
            }
            if (!File.Exists(caminho))
            {
                Mostrar(_seloPsets, _infoPsets, "não encontrado", Theme.Estado.Aviso, "");
                return;
            }

            var mapa = new ExportMapping();
            _avisosPsets = mapa.Warnings;
            try { mapa.LoadUserDefinedPropertySets(caminho); }
            catch (Exception ex)
            {
                Mostrar(_seloPsets, _infoPsets, "erro ao ler", Theme.Estado.Aviso, "Erro ao ler o .txt: " + ex.Message);
                return;
            }

            if (_parametrosModelo == null) _parametrosModelo = ParametrosDoModelo(_doc);
            List<string> ausentes = mapa.Properties
                .Select(p => p.RevitParameter)
                .Where(p => !string.IsNullOrEmpty(p) && !_parametrosModelo.Contains(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            string origem = Setup != null && string.Equals(caminho, Setup.PropertySets.Caminho, StringComparison.OrdinalIgnoreCase)
                ? $" ({Setup.PropertySets.Situacao})" : "";
            int psets = mapa.Properties.Select(p => p.PropertySet).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            string texto = $"{mapa.Properties.Count} propriedades em {psets} psets{origem}";
            var problemas = new List<string>();
            if (ausentes.Count > 0) problemas.Add(ausentes.Count == 1 ? "1 parâmetro faltando" : $"{ausentes.Count} parâmetros faltando");
            if (mapa.Warnings.Count > 0) problemas.Add(mapa.Warnings.Count == 1 ? "1 problema de formato" : $"{mapa.Warnings.Count} problemas de formato");
            var dica = new List<string>();
            if (ausentes.Count > 0) dica.Add("Não encontrados no modelo (ficam fora do IFC): " + string.Join(", ", ausentes.Take(12)) + (ausentes.Count > 12 ? "..." : ""));
            if (mapa.Warnings.Count > 0) dica.Add("Formato do exportador da Autodesk (corrija em Editar): " + string.Join(" ", mapa.Warnings.Take(4)) + (mapa.Warnings.Count > 4 ? " ..." : ""));
            Mostrar(_seloPsets, _infoPsets, problemas.Count > 0 ? string.Join(" · ", problemas) : "ok",
                problemas.Count > 0 ? Theme.Estado.Aviso : Theme.Estado.Ok, texto, string.Join("\n", dica));
        }

        private void Confirmar()
        {
            if (Setup == null)
            {
                MessageBox.Show(this, "Indique um setup de exportação (.json) válido.", Title);
                return;
            }
            if (!Setup.VersaoAceita)
            {
                MessageBox.Show(this, $"O setup {Setup.Nome} exporta em {Setup.DescricaoVersao}. A exportação MetroSP é em {OpcoesSetup.VersoesAceitas}: " +
                    $"escolha ou salve um setup {OpcoesSetup.VersoesAceitas} (por exemplo IFC 4 Reference View).", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string psets = TextoPsets();
            if (psets.Length > 0 && !File.Exists(psets))
            {
                MessageBox.Show(this, $"Arquivo de property sets não encontrado:\n{psets}", Title);
                return;
            }
            if (psets.Length > 0 && _avisosPsets.Count > 0)
            {
                MessageBoxResult r = MessageBox.Show(this,
                    $"O .txt de property sets tem {_avisosPsets.Count} problema(s) no formato do exportador da Autodesk:\n\n" +
                    string.Join("\n", _avisosPsets.Take(8)) + (_avisosPsets.Count > 8 ? "\n..." : "") +
                    "\n\nSim: abrir o editor para corrigir.\nNão: exportar assim mesmo.\nCancelar: voltar.",
                    Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                if (r == MessageBoxResult.Cancel) return;
                if (r == MessageBoxResult.Yes) { EditarPsets(); return; }
            }
            if (psets.Length == 0 && Setup.ExportaPsetsUsuario &&
                MessageBox.Show(this, "O setup pede property sets do usuário, mas nenhum .txt foi indicado. Exportar sem eles?",
                    Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            string mapeamento = _mapeamento.Text.Trim();
            if (mapeamento.Length > 0 && !File.Exists(mapeamento))
            {
                MessageBox.Show(this, $"Tabela de mapeamento não encontrada:\n{mapeamento}", Title);
                return;
            }
            string destino = _arquivo.Text.Trim();
            string saida, nome;
            try
            {
                saida = Path.GetDirectoryName(destino) ?? "";
                nome = Path.GetFileName(destino);
            }
            catch
            {
                MessageBox.Show(this, "Caminho do arquivo inválido.", Title);
                return;
            }
            if (saida.Length == 0 || !Directory.Exists(saida))
            {
                MessageBox.Show(this, "Indique o caminho completo do .ifc, numa pasta que exista (use Procurar...).", Title);
                return;
            }
            if (nome.EndsWith(".ifc", StringComparison.OrdinalIgnoreCase)) nome = nome.Substring(0, nome.Length - 4);
            if (nome.Length == 0 || nome.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show(this, "Nome de arquivo inválido.", Title);
                return;
            }
            if (File.Exists(Path.Combine(saida, nome + ".ifc")) &&
                MessageBox.Show(this, $"{nome}.ifc já existe na pasta. Substituir?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            if (string.IsNullOrWhiteSpace(_ifcProject.Text) &&
                MessageBox.Show(this, "O IfcProject está sem nome. Exportar assim mesmo?", Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            GravarNomes();

            if (!Localizacao.LocationCorreto)
            {
                bool podeGravar = Localizacao.Erro == null;
                MessageBoxResult r = MessageBox.Show(this,
                    Localizacao.TextoConferencia() + "\n\n" +
                    (podeGravar ? "Sim: gravar a localização pelo Project Base Point e exportar.\n" : "") +
                    (podeGravar ? "Não" : "OK") + ": exportar assim mesmo (a MetroSP grava no IfcSite a latitude/longitude do Project Base Point).\nCancelar: voltar.",
                    Title, podeGravar ? MessageBoxButton.YesNoCancel : MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (r == MessageBoxResult.Cancel) return;
                if (podeGravar && r == MessageBoxResult.Yes) GravarLocalizacao();
            }

            PropertySetsEscolhido = psets.Length > 0 ? psets : null;
            MapeamentoEscolhido = mapeamento.Length > 0 ? mapeamento : null;
            Vista = (_vista.SelectedItem as Opcao)?.Id ?? DB.ElementId.InvalidElementId;
            if (Vista == DB.ElementId.InvalidElementId)
            {
                Fase = FaseEscolhida;
                if (Fase != FaseDoSetup && !SalvarFaseNoSetup(Fase)) return;
            }
            PastaSaida = saida;
            NomeArquivo = nome;

            _settings.Classe = ClasseEscolhida?.Nome;
            GuardarPadraoDoModelo();
            _settings.Setup = _setupGravado ?? Setup.Arquivo;
            _settings.PropertySets = psets;
            _settings.Mapeamento = mapeamento;
            _settings.PastaSaida = saida;
            _settings.SempreEixos = SempreEixos;
            _settings.EpsgEmBranco = EpsgEmBranco;
            _settings.Vista = Vista == DB.ElementId.InvalidElementId ? Coordenacao.OpcoesExportacao.ModeloInteiro : _doc.GetElement(Vista)?.Name;
            _settings.NomeArquivo = string.Equals(nome, NomePadrao(_doc), StringComparison.CurrentCultureIgnoreCase) ? Coordenacao.OpcoesExportacao.NomeDoModelo : nome;
            if (Embutida) { Confirmada?.Invoke(); return; }
            DialogResult = true;
        }

        /// <summary>O padrão escolhido fica valendo para este modelo (também no passo Padrões da Entrega).</summary>
        private void GuardarPadraoDoModelo()
        {
            ClassesIfc.Classe c = ClasseEscolhida;
            if (c != null && ClassesIfc.Obter(c.Nome).DaCoordenacao) return;   // quem manda é o arquivo de coordenação
            _settings.GuardarPadrao(_doc.Title, c?.Nome ?? IfcExportSettings.SemPadrao);
        }

        /// <summary>Confere as escolhas e exporta (o mesmo que o botão Exportar).</summary>
        public void Exportar() => Confirmar();

        /// <summary>
        /// Guarda o que foi escolhido sem exportar: os nomes do IfcProject, IfcSite e IfcBuilding vão para o Revit
        /// e a classe, os arquivos, a pasta e os eixos ficam lembrados para a próxima vez.
        /// </summary>
        public void GuardarEscolhas()
        {
            GravarNomes();
            _settings.Classe = ClasseEscolhida?.Nome;
            GuardarPadraoDoModelo();
            if (Setup != null) _settings.Setup = _setupGravado ?? Setup.Arquivo;
            _settings.PropertySets = TextoPsets();
            _settings.Mapeamento = _mapeamento.Text.Trim();
            try
            {
                string pasta = Path.GetDirectoryName(_arquivo.Text.Trim());
                if (!string.IsNullOrEmpty(pasta) && Directory.Exists(pasta)) _settings.PastaSaida = pasta;
            }
            catch { /* caminho inválido digitado: fica a pasta anterior */ }
            _settings.SempreEixos = SempreEixos;
            _settings.EpsgEmBranco = EpsgEmBranco;
            DB.ElementId vista = (_vista.SelectedItem as Opcao)?.Id ?? DB.ElementId.InvalidElementId;
            _settings.Vista = vista == DB.ElementId.InvalidElementId ? Coordenacao.OpcoesExportacao.ModeloInteiro : _doc.GetElement(vista)?.Name;
            try
            {
                string nome = Path.GetFileNameWithoutExtension(_arquivo.Text.Trim());
                if (!string.IsNullOrEmpty(nome))
                    _settings.NomeArquivo = string.Equals(nome, NomePadrao(_doc), StringComparison.CurrentCultureIgnoreCase) ? Coordenacao.OpcoesExportacao.NomeDoModelo : nome;
            }
            catch { /* nome inválido digitado: fica o anterior */ }
        }

        /// <summary>
        /// Resumo da última conferência da Entrega deste modelo (sem conferir de novo) e o link para o passo a passo,
        /// onde dá para resolver as pendências e exportar no fim.
        /// </summary>
        private FrameworkElement ResumoEntrega()
        {
            Conferencia.ResultadoConferencia r = Conferencia.UltimaConferencia.De(_doc);
            var linha = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var passoAPasso = Theme.Link("Passo a passo...");
            passoAPasso.ToolTip = "Fecha esta janela e abre a Entrega passo a passo: confere o modelo, mostra o que falta em cada etapa e exporta no último passo.";
            passoAPasso.VerticalAlignment = VerticalAlignment.Center;
            passoAPasso.Click += (s, e) => { AbrirPassoAPasso = true; DialogResult = false; };
            DockPanel.SetDock(passoAPasso, Dock.Right);
            linha.Children.Add(passoAPasso);
            var rotulo = new TextBlock { Text = "Entrega", Foreground = Theme.Solid(Theme.Preto), VerticalAlignment = VerticalAlignment.Center, Width = LarguraRotulo };
            DockPanel.SetDock(rotulo, Dock.Left);
            linha.Children.Add(rotulo);
            Border selo = Theme.Selo();
            selo.HorizontalAlignment = HorizontalAlignment.Left;
            selo.VerticalAlignment = VerticalAlignment.Center;
            linha.Children.Add(selo);
            if (r == null)
            {
                Theme.MudarSelo(selo, "ainda não conferida", Theme.Estado.Neutro,
                    "O modelo ainda não foi conferido nesta sessão. Use o Passo a passo para ver o que falta antes de exportar.");
                return linha;
            }
            var pendentes = r.Etapas.Where(e => e.Sinal == Conferencia.Sinal.Vermelho || e.Sinal == Conferencia.Sinal.Amarelo).ToList();
            string quando = $" · {r.Quando:HH:mm}";
            string dica = pendentes.Count == 0 ? "Todas as etapas estão certas."
                : string.Join("\n", pendentes.Select(e => $"{e.Numero}. {e.Titulo}" + (string.IsNullOrEmpty(e.Resumo) ? "" : ": " + e.Resumo)));
            dica += $"\nConferido às {r.Quando:HH:mm}. Confira de novo no Passo a passo.";
            if (r.Vermelhas > 0)
                Theme.MudarSelo(selo, (r.Vermelhas == 1 ? "1 etapa com pendência" : $"{r.Vermelhas} etapas com pendência") + quando, Theme.Estado.Aviso, "O IFC sai incompleto.\n" + dica);
            else if (r.Amarelas > 0)
                Theme.MudarSelo(selo, (r.Amarelas == 1 ? "pronta, com 1 aviso" : $"pronta, com {r.Amarelas} avisos") + quando, Theme.Estado.Aviso, dica);
            else
                Theme.MudarSelo(selo, "tudo certo" + quando, Theme.Estado.Ok, dica);
            return linha;
        }

        /// <summary>Mostra se o Location confere com o Project Base Point.</summary>
        private void MostrarLocalizacao()
        {
            LocalizacaoIfc l = Localizacao;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string ponto = $"{l.LatitudeAtual.ToString("F6", ci)}, {l.LongitudeAtual.ToString("F6", ci)}";
            if (l.Erro != null)
                Theme.MudarSelo(_seloLocalizacao, "Location não conferido", Theme.Estado.Aviso, "Não foi possível conferir o Location pelo Project Base Point: " + l.Erro);
            else if (!l.LocationCorreto)
                Theme.MudarSelo(_seloLocalizacao,
                    $"Location a {(l.Diferenca < 1000 ? l.Diferenca.ToString("0.00") + " m" : (l.Diferenca / 1000).ToString("0.0") + " km")} do Project Base Point",
                    Theme.Estado.Aviso, $"Location atual: {ponto}. Clique em Gravar localização antes de exportar.");
            else
                Theme.MudarSelo(_seloLocalizacao, $"Location conferido: {ponto}", Theme.Estado.Ok,
                    $"Confere com o Project Base Point.\nIfcSite: \"{l.SiteAtual}\"\nIfcBuilding: \"{l.EdificioAtual}\"");
            _gravarLocal.IsEnabled = l.Erro == null && !l.LocationCorreto;
            AtualizarResumo();
        }

        /// <summary>Grava no Location a lat/long calculada pelo Project Base Point (os nomes ficam como estão no Revit) e confere de novo.</summary>
        private void GravarLocalizacao()
        {
            LocalizacaoIfc l = LocalizacaoIfc.Calcular(_doc);
            if (l.Erro != null) { MessageBox.Show(this, "Não foi possível calcular pelo Project Base Point: " + l.Erro, Title); return; }
            LocalizacaoIfc.Escolha e = l.Atual();
            e.Latitude = l.Latitude;
            e.Longitude = l.Longitude;
            Gravar(l, e);
        }

        /// <summary>Grava a escolha no Revit (com transação própria), guarda o que mudou e confere de novo.</summary>
        private void Gravar(LocalizacaoIfc l, LocalizacaoIfc.Escolha e)
        {
            try
            {
                List<string> mudou = l.Aplicar(_doc, e);
                AlteracoesLocalizacao.AddRange(mudou);
                string aviso = mudou.FirstOrDefault(m => m.StartsWith("ATENÇÃO", StringComparison.Ordinal));
                if (aviso != null) MessageBox.Show(this, aviso, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível gravar no Revit:\n" + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            Localizacao = LocalizacaoIfc.Calcular(_doc);
            MostrarLocalizacao();
        }

        /// <summary>Nomes digitados no IfcProject, IfcSite e IfcBuilding que diferem do Revit: grava antes de exportar.</summary>
        private void GravarNomes()
        {
            LocalizacaoIfc l = LocalizacaoIfc.Calcular(_doc);
            LocalizacaoIfc.Escolha e = l.Atual();
            e.Dados.Projeto.Nome = _ifcProject.Text.Trim();
            e.Dados.Terreno.Nome = _ifcSite.Text.Trim();
            e.Dados.Edificio.Nome = _ifcBuilding.Text.Trim();
            if (DadosProjetoRevit.Diferencas(l.DadosAtuais, e.Dados) > 0) Gravar(l, e);
        }

        /// <summary>"Mais dados...": abre a etapa completa (nome longo, descrição, endereço, Pset MetroSP_Projeto), grava e confere de novo.</summary>
        private void AbrirLocalizacao()
        {
            GravarNomes();
            try
            {
                List<string> mudou = LocalizacaoWindow.Executar(_doc, LocalizacaoIfc.Calcular(_doc), this, System.IntPtr.Zero);
                if (mudou != null) AlteracoesLocalizacao.AddRange(mudou);
                string aviso = mudou?.FirstOrDefault(m => m.StartsWith("ATENÇÃO", StringComparison.Ordinal));
                if (aviso != null) MessageBox.Show(this, aviso, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível gravar a localização no Revit:\n" + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            Localizacao = LocalizacaoIfc.Calcular(_doc);
            MostrarLocalizacao();
            Ifc.DadosProjetoIfc d = Localizacao.DadosAtuais;
            _ifcProject.Text = d.Projeto?.Nome ?? "";
            _ifcSite.Text = d.Terreno?.Nome ?? "";
            _ifcBuilding.Text = d.Edificio?.Nome ?? "";
        }

        private string TextoPsets() => (_psets.Text ?? string.Empty).Trim();

        private const double LarguraRotulo = 110;

        /// <summary>Linha de uma grade de duas colunas: rótulo à esquerda (largura fixa) e o campo à direita.</summary>
        private static void LinhaGrade(Grid g, string rotulo, FrameworkElement campo)
        {
            int r = g.RowDefinitions.Count;
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var t = new TextBlock { Text = rotulo, VerticalAlignment = VerticalAlignment.Center, Foreground = Theme.Solid(Theme.Preto), Margin = new Thickness(0, 3, 8, 3) };
            Grid.SetRow(t, r);
            g.Children.Add(t);
            campo.Margin = new Thickness(0, 3, 0, 3);
            Grid.SetRow(campo, r);
            Grid.SetColumn(campo, 1);
            g.Children.Add(campo);
        }

        /// <summary>O rótulo da linha da grade onde está o campo.</summary>
        private static UIElement Rotulo(Grid g, FrameworkElement campo) =>
            g.Children.OfType<TextBlock>().First(t => Grid.GetRow(t) == Grid.GetRow(campo) && Grid.GetColumn(t) == 0);

        /// <summary>Fase gravada no setup, se existir neste modelo; senão -1 (padrão do exportador).</summary>
        private long FaseDoSetup =>
            Setup != null && Setup.Fase > 0 && _doc.GetElement(Compat.NovoId(Setup.Fase)) is DB.Phase ? Setup.Fase : -1;

        private long FaseEscolhida => (_fase.SelectedItem as ComboBoxItem)?.Tag is long n ? n : -1;

        /// <summary>
        /// Lista de fases. Modelo inteiro: a do setup (ou a última, padrão do exportador), e dá para trocar.
        /// Com uma vista: vale a fase da vista, só para conferir.
        /// </summary>
        private void MostrarFase()
        {
            if (_fase == null) return;
            _fase.Items.Clear();
            var fases = _doc.Phases.Cast<DB.Phase>().ToList();
            string ultima = fases.Count > 0 ? fases[fases.Count - 1].Name : "nenhuma";
            DB.ElementId vista = (_vista.SelectedItem as Opcao)?.Id ?? DB.ElementId.InvalidElementId;
            if (vista != DB.ElementId.InvalidElementId)
            {
                DB.ElementId daVista = (_doc.GetElement(vista) as DB.View)?.get_Parameter(DB.BuiltInParameter.VIEW_PHASE)?.AsElementId();
                string nome = daVista != null ? _doc.GetElement(daVista)?.Name : null;
                _fase.Items.Add(new ComboBoxItem { Content = "Fase da vista: " + (nome ?? ultima), Tag = -1L });
                _fase.SelectedIndex = 0;
                _fase.IsEnabled = false;
                _fase.ToolTip = "Exportando só o visível numa vista, vale a fase da vista (troque nas propriedades da vista no Revit).";
                return;
            }
            _fase.IsEnabled = true;
            _fase.Items.Add(new ComboBoxItem { Content = $"Padrão do exportador (última: {ultima})", Tag = -1L });
            foreach (DB.Phase ph in fases) _fase.Items.Add(new ComboBoxItem { Content = ph.Name, Tag = Compat.Numero(ph.Id) });
            long atual = FaseDoSetup;
            _fase.SelectedItem = _fase.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (long)i.Tag == atual) ?? _fase.Items[0];
            _fase.ToolTip = "Fase exportada (modelo inteiro). Entra o que existe nela; demolidos e de fases posteriores ficam de fora.\n" +
                            "Ao exportar, a fase escolhida fica gravada no setup (.json)" +
                            (Setup != null && Setup.Fase > 0 && atual < 0 ? $".\nO setup tem a fase de id {Setup.Fase}, que não existe neste modelo." : ".");
        }

        /// <summary>
        /// Grava a fase no setup (.json). Setup de uma classe instalada ou do Forma: grava a sua cópia (como em
        /// Gerenciar arquivos), que passa a valer no lugar dele. Setup indicado à mão: grava no próprio arquivo.
        /// </summary>
        private bool SalvarFaseNoSetup(long fase)
        {
            if (Setup == null) return true;
            try
            {
                string origem = Setup.Arquivo;
                ClassesIfc.Classe c = ClasseEscolhida != null ? ClassesIfc.Obter(ClasseEscolhida.Nome) : null;
                bool daClasse = c != null && string.Equals(c.Setup, origem, StringComparison.OrdinalIgnoreCase);
                string destino = daClasse ? ArquivosIfc.Destino(ArquivosIfc.Tipo.Setup, c.Nome) : origem;
                OpcoesSetup o = OpcoesSetup.Ler(origem);
                o.Fase = fase;
                if (daClasse) ArquivosIfc.GuardarAnterior(destino);
                o.Salvar(destino);
                _setupGravado = destino;
                return true;
            }
            catch (Exception ex)
            {
                return MessageBox.Show(this, "Não foi possível gravar a fase no setup: " + ex.Message + "\n\nExportar assim mesmo (com a fase escolhida)?",
                    Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
            }
        }

        private void PreencherVistas()
        {
            _vista.Items.Add(new Opcao { Texto = "Modelo inteiro", Id = DB.ElementId.InvalidElementId });
            IEnumerable<DB.View3D> vistas = new DB.FilteredElementCollector(_doc)
                .OfClass(typeof(DB.View3D)).Cast<DB.View3D>()
                .Where(v => !v.IsTemplate)
                .OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase);
            foreach (DB.View3D v in vistas)
                _vista.Items.Add(new Opcao { Texto = "Só o visível na vista 3D: " + v.Name, Id = v.Id });
            _vista.SelectedIndex = 0;
        }

        /// <summary>Nomes de parâmetros presentes nos tipos e numa instância de cada tipo.</summary>
        private static HashSet<string> ParametrosDoModelo(DB.Document doc) => ModeloIfc.NomesDosParametros(doc);

        private static string NomePadrao(DB.Document doc)
        {
            string nome = doc.Title ?? "Modelo";
            if (nome.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase)) nome = nome.Substring(0, nome.Length - 4);
            return nome;
        }

        /// <summary>
        /// Abre o seletor de arquivo na pasta do arquivo atual. Sem nada escolhido ainda, começa no projeto
        /// do Forma do modelo aberto (Desktop Connector), se houver.
        /// </summary>
        private string ProcurarArquivo(string atual, string filtro, string titulo)
        {
            var dialogo = new Microsoft.Win32.OpenFileDialog { Filter = filtro, Title = titulo };
            string pasta = null;
            string a = atual?.Trim();
            if (!string.IsNullOrEmpty(a)) pasta = File.Exists(a) ? Path.GetDirectoryName(a) : Directory.Exists(a) ? a : Path.GetDirectoryName(a);
            if (string.IsNullOrEmpty(pasta) || !Directory.Exists(pasta))
            {
                string doModelo = FormaPaths.ProjetoDoModelo(_doc);
                pasta = FormaPaths.Projetos(FormaPaths.Raiz())
                    .FirstOrDefault(p => string.Equals(p.Nome, doModelo, StringComparison.OrdinalIgnoreCase))?.Caminho;
            }
            if (!string.IsNullOrEmpty(pasta) && Directory.Exists(pasta)) dialogo.InitialDirectory = pasta;
            if (File.Exists(a)) dialogo.FileName = Path.GetFileName(a);
            return dialogo.ShowDialog(this) == true ? dialogo.FileName : null;
        }
    }
}
