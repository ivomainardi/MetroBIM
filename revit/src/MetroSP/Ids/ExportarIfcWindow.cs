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
        private readonly TextBox _arquivo;
        private readonly TextBlock _resumo;
        private readonly Border _seloLocalizacao;

        private HashSet<string> _parametrosModelo;
        private List<string> _avisosPsets = new List<string>();

        public IfcExportSetup Setup { get; private set; }
        public string PropertySetsEscolhido { get; private set; }
        public string MapeamentoEscolhido { get; private set; }
        public DB.ElementId Vista { get; private set; } = DB.ElementId.InvalidElementId;
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
        private readonly CheckBox _epsgEmBranco;

        private class Opcao
        {
            public string Texto { get; set; }
            public DB.ElementId Id { get; set; }
            public override string ToString() => Texto;
        }

        /// <param name="resultadoForma">Resultado da atualização do Forma online feita ao abrir (null se não houve).</param>
        public ExportarIfcWindow(DB.Document doc, IfcExportSettings settings, string resultadoForma = null)
        {
            _doc = doc;
            _settings = settings;

            Title = "MetroSP - Exportar IFC";
            Width = 660;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            MinWidth = 540;

            var corpo = new StackPanel();

            // ---------- Classe ----------
            var secaoClasse = Theme.Secao("Classe");
            secaoClasse.Margin = new Thickness(0, 0, 0, 5);
            corpo.Children.Add(secaoClasse);
            var linhaClasse = new DockPanel();
            _classe = new ComboBox { Padding = new Thickness(4, 3, 4, 3), Width = 150, VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Classe instalada com o MetroSP (preenche o setup e os .txt sozinha), ou \"Outros arquivos\" para indicar cada um." };
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
            var gerenciar = Theme.Link("Gerenciar arquivos...");
            gerenciar.Margin = new Thickness(12, 0, 0, 0);
            gerenciar.ToolTip = "Abre a janela Arquivos IFC: setups, property sets e mapeamentos de cada classe.";
            gerenciar.Click += (s, e) => Gerenciar();
            DockPanel.SetDock(gerenciar, Dock.Right);
            linhaClasse.Children.Add(gerenciar);
            linhaClasse.Children.Add(_botaoForma);
            corpo.Children.Add(linhaClasse);

            // ---------- Arquivos ----------
            corpo.Children.Add(Theme.Secao("Arquivos da exportação"));
            corpo.Children.Add(Theme.Grupo(out StackPanel arquivos));
            _setup = ItemArquivo(arquivos, "Setup", "Trocar...", primeiro: true, out _arqSetup, out _seloSetup, out _infoSetup, out Button procurarSetup, out _);
            _psets = ItemArquivo(arquivos, "Property sets", "Trocar...", primeiro: false, out _arqPsets, out _seloPsets, out _infoPsets, out Button procurarPsets, out StackPanel acoesPsets);
            var editarPsets = new Button { Content = "Editar...", Margin = new Thickness(0, 0, 6, 0) };
            acoesPsets.Children.Insert(0, editarPsets);
            _mapeamento = ItemArquivo(arquivos, "Mapeamento", "Indicar...", primeiro: false, out _arqMapeamento, out _seloMapeamento, out _infoMapeamento, out Button procurarMapeamento, out _);

            // ---------- Localização: a etapa 1 (botão Localização IFC) grava o Location; aqui só confere ----------
            Localizacao = LocalizacaoIfc.Calcular(doc);
            corpo.Children.Add(Theme.Secao("Localização"));
            var linhaLocal = new DockPanel();
            var abrirLocal = new Button { Content = "Localização IFC...", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            abrirLocal.Click += (s, e) => AbrirLocalizacao();
            DockPanel.SetDock(abrirLocal, Dock.Right);
            linhaLocal.Children.Add(abrirLocal);
            _seloLocalizacao = Theme.Selo();
            linhaLocal.Children.Add(_seloLocalizacao);
            corpo.Children.Add(linhaLocal);
            _epsgEmBranco = new CheckBox
            {
                Content = "Exportar com o EPSG em branco",
                IsChecked = true,
                ToolTip = "Ligado: o IFC sai sem código EPSG, IfcProjectedCRS e IfcMapConversion (coordenadas no PTL), mesmo que o Location tenha sistema GIS" +
                          (Localizacao.SistemaGis != null ? $" (\"{Localizacao.SistemaGis}\")" : "") +
                          ".\nDesligado: vale o EPSG do setup ou o que o exportador gravar pelo sistema GIS.",
                Margin = new Thickness(0, 6, 0, 0),
            };
            corpo.Children.Add(_epsgEmBranco);

            // ---------- Saída ----------
            corpo.Children.Add(Theme.Secao("Saída"));
            _vista = new ComboBox { Padding = new Thickness(4, 3, 4, 3), Margin = new Thickness(0, 0, 0, 6) };
            corpo.Children.Add(_vista);
            var linhaArquivo = new DockPanel();
            var procurarSaida = new Button { Content = "Procurar...", Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(procurarSaida, Dock.Right);
            linhaArquivo.Children.Add(procurarSaida);
            _arquivo = new TextBox { VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Pasta e nome do arquivo .ifc" };
            linhaArquivo.Children.Add(_arquivo);
            corpo.Children.Add(linhaArquivo);

            PreencherVistas();
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
            _classe.SelectionChanged += (s, e) => { _motivoClasse = null; AplicarClasse(); };
            MostrarForma(resultadoForma);
            if (_classe.SelectedItem is ClassesIfc.Classe) AplicarClasse();
            else MostrarClasse(_classe.Items.Count > 1 ? null : "Nenhuma classe instalada: indique os arquivos.");
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
            var cancelar = new Button { Content = "Cancelar", IsCancel = true };
            _resumo = new TextBlock { FontSize = 11.5 };

            MostrarLocalizacao();
            MostrarCaminhos();
            Theme.Apply(this, "Exportar IFC", "Indique o setup e os arquivos de mapeamento de cada exportação", corpo, Theme.Rodape(_resumo, cancelar, exportar));
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

        /// <summary>Se o nome do modelo indica uma classe (Nomenclatura.txt ou trecho do nome), ela é escolhida.</summary>
        private void ClasseDoNome()
        {
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
            string origem = c.Editada ? "editada por você" : c.DoForma ? "do Forma online" : "instalada";
            string selo = _motivoClasse != null ? "pelo nome do modelo" : origem;
            string dica = (_motivoClasse != null ? $"Classe indicada {_motivoClasse}.\n" : "") +
                          (c.Editada ? "Property sets editados por você (o padrão não foi alterado)."
                           : c.DoForma ? "Property sets da pasta do Forma online." : "Configuração instalada com o MetroSP.") +
                          (faltam.Count > 0 ? "\nA classe não traz: " + string.Join(", ", faltam) + "." : "");
            if (_motivoClasse != null && origem != "instalada") selo += " · " + origem;
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
            else if (!Setup.EhIfc4) avisos.Add("setup não é IFC4");
            if (_seloPsets.Visibility == Visibility.Visible && ((TextBlock)_seloPsets.Child).Foreground is System.Windows.Media.SolidColorBrush bp && bp.Color == Theme.LaranjaTexto)
                avisos.Add("property sets: " + ((TextBlock)_seloPsets.Child).Text);
            if (_seloMapeamento.Visibility == Visibility.Visible && ((TextBlock)_seloMapeamento.Child).Foreground is System.Windows.Media.SolidColorBrush bm && bm.Color == Theme.LaranjaTexto)
                avisos.Add("mapeamento não encontrado");
            if (Localizacao != null && (Localizacao.Erro != null || !Localizacao.LocationCorreto)) avisos.Add("Location fora do Project Base Point");
            Theme.MudarResumo(_resumo, avisos, "Pronto para exportar");
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
                MessageBox.Show(this, "Escolha uma classe ou indique um .txt de property sets que exista.", Title);
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
            Mostrar(_seloSetup, _infoSetup, Setup.DescricaoVersao, Setup.EhIfc4 ? Theme.Estado.Ok : Theme.Estado.Aviso, string.Join(" · ", partes));

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
            if (!Setup.EhIfc4)
            {
                MessageBox.Show(this, $"O setup {Setup.Nome} exporta em {Setup.DescricaoVersao}. A exportação MetroSP é em IFC4: " +
                    "escolha ou salve um setup IFC4 (por exemplo IFC 4 Reference View).", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
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

            if (!Localizacao.LocationCorreto)
            {
                MessageBoxResult r = MessageBox.Show(this,
                    "O Location do Revit não está no ponto do Project Base Point.\n\n" +
                    "Sim: abrir a etapa Localização para corrigir.\nNão: exportar assim mesmo (a MetroSP grava no IfcSite a latitude/longitude do Project Base Point).\nCancelar: voltar.",
                    Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                if (r == MessageBoxResult.Cancel) return;
                if (r == MessageBoxResult.Yes) { AbrirLocalizacao(); return; }
            }

            PropertySetsEscolhido = psets.Length > 0 ? psets : null;
            MapeamentoEscolhido = mapeamento.Length > 0 ? mapeamento : null;
            Vista = (_vista.SelectedItem as Opcao)?.Id ?? DB.ElementId.InvalidElementId;
            PastaSaida = saida;
            NomeArquivo = nome;

            _settings.Classe = ClasseEscolhida?.Nome;
            _settings.Setup = Setup.Arquivo;
            _settings.PropertySets = psets;
            _settings.Mapeamento = mapeamento;
            _settings.PastaSaida = saida;
            DialogResult = true;
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
                    Theme.Estado.Aviso, $"Location atual: {ponto}. Use Localização IFC... antes de exportar.");
            else
                Theme.MudarSelo(_seloLocalizacao, $"Location conferido: {ponto}", Theme.Estado.Ok,
                    $"Confere com o Project Base Point.\nIfcSite: \"{l.SiteAtual}\"\nIfcBuilding: \"{l.EdificioAtual}\"");
            AtualizarResumo();
        }

        /// <summary>Abre a etapa Localização, grava no Revit e confere de novo.</summary>
        private void AbrirLocalizacao()
        {
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
        }

        private string TextoPsets() => (_psets.Text ?? string.Empty).Trim();

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
