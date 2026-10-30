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
        private readonly TextBlock _infoClasse;
        private readonly TextBlock _infoForma;
        private readonly Button _botaoForma;
        private string _motivoClasse;
        private bool _aplicandoClasse;
        private const string Outros = "Outros arquivos (indicar abaixo)";

        private readonly TextBox _setup;
        private readonly TextBlock _infoSetup;
        private readonly TextBox _psets;
        private readonly TextBlock _infoPsets;
        private readonly TextBox _mapeamento;
        private readonly TextBlock _infoMapeamento;
        private readonly ComboBox _vista;
        private readonly TextBox _saida;
        private readonly TextBox _nome;

        private HashSet<string> _parametrosModelo;
        private List<string> _avisosPsets = new List<string>();

        public IfcExportSetup Setup { get; private set; }
        public string PropertySetsEscolhido { get; private set; }
        public string MapeamentoEscolhido { get; private set; }
        public DB.ElementId Vista { get; private set; } = DB.ElementId.InvalidElementId;
        public string PastaSaida { get; private set; }
        public string NomeArquivo { get; private set; }

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
            Width = 680;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            MinWidth = 540;

            var corpo = new StackPanel();
            var dica = Dica(corpo);
            dica.Text = "Escolha a classe para usar as configurações que vêm com o MetroSP, ou indique onde está cada arquivo " +
                        "(computador, rede ou Forma pelo Desktop Connector). As escolhas ficam lembradas para a próxima exportação.";

            corpo.Children.Add(Theme.Label("Classe"));
            _classe = new ComboBox { Padding = new Thickness(4, 3, 4, 3) };
            corpo.Children.Add(_classe);
            _infoClasse = Dica(corpo);

            // Forma online: pasta com as configurações, lida pela internet (sem Desktop Connector).
            var linhaForma = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            _botaoForma = new Button { Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            _botaoForma.Click += (s, e) => MenuForma();
            DockPanel.SetDock(_botaoForma, Dock.Right);
            linhaForma.Children.Add(_botaoForma);
            _infoForma = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            linhaForma.Children.Add(_infoForma);
            corpo.Children.Add(linhaForma);

            _setup = AddLinha(corpo, "1. Setup de exportação (.json salvo no exportador IFC)", out Button procurarSetup);
            _infoSetup = Dica(corpo);
            _psets = AddLinha(corpo, "2. Property sets definidos pelo usuário (.txt)", out Button procurarPsets);
            var linhaPsets = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
            var editarPsets = new Button { Content = "Editar property sets...", Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            DockPanel.SetDock(editarPsets, Dock.Right);
            linhaPsets.Children.Add(editarPsets);
            _infoPsets = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), TextWrapping = TextWrapping.Wrap };
            linhaPsets.Children.Add(_infoPsets);
            corpo.Children.Add(linhaPsets);
            _mapeamento = AddLinha(corpo, "3. Tabela de mapeamento de parâmetros (.txt) - opcional", out Button procurarMapeamento);
            _infoMapeamento = Dica(corpo);

            // Vista e destino
            corpo.Children.Add(Theme.Label("Elementos a exportar"));
            _vista = new ComboBox { Padding = new Thickness(4, 3, 4, 3) };
            corpo.Children.Add(_vista);

            _saida = AddLinha(corpo, "Pasta de saída", out Button procurarSaida);
            corpo.Children.Add(Theme.Label("Nome do arquivo"));
            _nome = new TextBox { Text = NomePadrao(doc) };
            corpo.Children.Add(_nome);

            PreencherVistas();
            _saida.Text = settings.PastaSaida ?? string.Empty;

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
            else _infoClasse.Text = _classe.Items.Count > 1 ? "" : "Nenhuma classe instalada com o MetroSP: indique os arquivos abaixo.";
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
            procurarSaida.Click += (s, e) => { string p = EscolherPasta(_saida.Text, "Pasta onde salvar o IFC"); if (p != null) _saida.Text = p; };

            var exportar = new Button { Content = "Exportar", IsDefault = true, Style = Theme.PrimaryButton };
            exportar.Click += (s, e) => Confirmar();
            var cancelar = new Button { Content = "Cancelar", IsCancel = true };

            Theme.Apply(this, "Exportar IFC", "Indique o setup e os arquivos de mapeamento de cada exportação", corpo, Theme.Buttons(cancelar, exportar));
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

        /// <summary>Mostra a situação do Forma online e o que o botão faz.</summary>
        public void MostrarForma(string resultadoSync = null)
        {
            FormaOnline.Pasta pasta = FormaOnline.PastaEscolhida();
            if (!FormaOnline.Configurado)
            {
                _infoForma.Text = "Forma online: não configurado neste computador (falta o Client ID do app do MetroSP).";
                _botaoForma.Visibility = Visibility.Collapsed;
                return;
            }
            _botaoForma.Visibility = Visibility.Visible;
            if (!FormaOnline.Conectado)
            {
                _infoForma.Text = "Forma online: entre com a conta Autodesk para usar uma pasta do Forma sem o Desktop Connector.";
                _botaoForma.Content = "Entrar...";
            }
            else if (pasta == null)
            {
                _infoForma.Text = "Forma online: conectado. Escolha a pasta com as configurações de IFC.";
                _botaoForma.Content = "Escolher pasta...";
            }
            else
            {
                _infoForma.Text = "Forma online: " + pasta.Descricao + (resultadoSync != null ? " · " + resultadoSync : "");
                _botaoForma.Content = "Forma...";
            }
            _infoForma.Foreground = Theme.Solid(resultadoSync != null && !char.IsDigit(resultadoSync[0]) ? Theme.Laranja : Theme.Cinza);
        }

        private void MenuForma()
        {
            try
            {
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
                    menu.IsOpen = true;
                    return;
                }
            }
            catch (Exception ex) { ErroForma(ex); }
            MostrarForma();
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
                _infoClasse.Text = "";
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
            if (c.Setup == null) faltam.Add("setup (use o último escolhido ou indique abaixo)");
            if (c.PropertySets == null) faltam.Add("property sets");
            _infoClasse.Text = (_motivoClasse != null ? $"Classe indicada {_motivoClasse}. " : "") +
                               (c.Editada ? "Property sets editados por você (o padrão não foi alterado). "
                                : c.DoForma ? "Property sets da pasta do Forma online. " : "Configuração instalada com o MetroSP. ") +
                               (faltam.Count > 0 ? "A classe não traz: " + string.Join(", ", faltam) + "." : "");
            _infoClasse.Foreground = Theme.Solid(Theme.Cinza);
        }

        /// <summary>Arquivo trocado à mão: deixa de valer a classe.</summary>
        private void ParaOutros()
        {
            if (_aplicandoClasse || ClasseEscolhida == null) return;
            _aplicandoClasse = true;
            _classe.SelectedItem = Outros;
            _infoClasse.Text = "";
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
                    podeRestaurar: daClasse && c.Editada && ClassesIfc.CaminhoPadrao(c.Nome) != null) { Owner = this };
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
                _infoSetup.Text = "No Revit: Exportar › IFC › Modificar configuração › Salvar configuração. Depois indique o .json aqui.";
                _infoSetup.Foreground = Theme.Solid(Theme.Cinza);
                ConferirPsets();
                ConferirMapeamento();
                return;
            }
            if (!File.Exists(arquivo))
            {
                _infoSetup.Text = "Arquivo não encontrado.";
                _infoSetup.Foreground = Theme.Solid(Theme.Laranja);
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
                _infoSetup.Text = "Não foi possível ler o setup: " + ex.Message;
                _infoSetup.Foreground = Theme.Solid(Theme.Laranja);
                ConferirPsets();
                ConferirMapeamento();
                return;
            }

            var partes = new List<string> { Setup.Nome, Setup.DescricaoVersao };
            if (Setup.SoVisiveisNaVista) partes.Add("só elementos visíveis na vista");
            if (Setup.GravaGuid) partes.Add("grava o GUID IFC nos elementos");
            _infoSetup.Text = string.Join(" · ", partes);
            _infoSetup.Foreground = Theme.Solid(Theme.Cinza);

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

        private void ConferirMapeamento()
        {
            string caminho = _mapeamento.Text.Trim();
            bool doSetup = Setup != null && string.Equals(caminho, Setup.MapeamentoParametros.Caminho, StringComparison.OrdinalIgnoreCase);
            _infoMapeamento.Text = caminho.Length == 0 ? "Sem mapeamento: os parâmetros saem com os nomes do Revit."
                : !File.Exists(caminho) ? "Arquivo não encontrado."
                : doSetup ? $"Indicado pelo setup ({Setup.MapeamentoParametros.Situacao})." : "";
            _infoMapeamento.Foreground = Theme.Solid(caminho.Length > 0 && !File.Exists(caminho) ? Theme.Laranja : Theme.Cinza);
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
                _infoPsets.Text = motivo;
                _infoPsets.Foreground = Theme.Solid(Setup != null && Setup.ExportaPsetsUsuario ? Theme.Laranja : Theme.Cinza);
                return;
            }
            if (!File.Exists(caminho))
            {
                _infoPsets.Text = "Arquivo não encontrado.";
                _infoPsets.Foreground = Theme.Solid(Theme.Laranja);
                return;
            }

            var mapa = new ExportMapping();
            _avisosPsets = mapa.Warnings;
            try { mapa.LoadUserDefinedPropertySets(caminho); }
            catch (Exception ex)
            {
                _infoPsets.Text = "Erro ao ler o .txt: " + ex.Message;
                _infoPsets.Foreground = Theme.Solid(Theme.Laranja);
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
            string texto = $"{mapa.Properties.Count} propriedades em {psets} psets{origem}.";
            if (ausentes.Count > 0)
                texto += $" {ausentes.Count} parâmetro(s) não encontrados no modelo (ficam fora do IFC): " +
                    string.Join(", ", ausentes.Take(8)) + (ausentes.Count > 8 ? "..." : "");
            if (mapa.Warnings.Count > 0)
                texto += $" {mapa.Warnings.Count} problema(s) no formato do exportador da Autodesk (use Editar property sets para corrigir): " +
                         string.Join(" ", mapa.Warnings.Take(3)) + (mapa.Warnings.Count > 3 ? " ..." : "");
            _infoPsets.Text = texto;
            _infoPsets.Foreground = Theme.Solid(ausentes.Count > 0 || mapa.Warnings.Count > 0 ? Theme.Laranja : Theme.Cinza);
        }

        private void Confirmar()
        {
            if (Setup == null)
            {
                MessageBox.Show(this, "Indique um setup de exportação (.json) válido.", Title);
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
            string saida = _saida.Text.Trim();
            if (!Directory.Exists(saida))
            {
                MessageBox.Show(this, "Indique uma pasta de saída que exista.", Title);
                return;
            }
            string nome = _nome.Text.Trim();
            if (nome.EndsWith(".ifc", StringComparison.OrdinalIgnoreCase)) nome = nome.Substring(0, nome.Length - 4);
            if (nome.Length == 0 || nome.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show(this, "Nome de arquivo inválido.", Title);
                return;
            }
            if (File.Exists(Path.Combine(saida, nome + ".ifc")) &&
                MessageBox.Show(this, $"{nome}.ifc já existe na pasta. Substituir?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

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
        private static HashSet<string> ParametrosDoModelo(DB.Document doc)
        {
            var nomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Coletar(DB.Element e)
            {
                foreach (DB.Parameter p in e.Parameters)
                {
                    string n = p.Definition?.Name;
                    if (!string.IsNullOrEmpty(n)) nomes.Add(n);
                }
            }
            foreach (DB.Element t in new DB.FilteredElementCollector(doc).WhereElementIsElementType()) Coletar(t);
            var tipos = new HashSet<DB.ElementId>();
            foreach (DB.Element e in new DB.FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                if (e.Category == null) continue;
                DB.ElementId tipo = e.GetTypeId();
                if (tipo == null || tipo == DB.ElementId.InvalidElementId || tipos.Add(tipo)) Coletar(e);
            }
            if (doc.ProjectInformation != null) Coletar(doc.ProjectInformation);
            return nomes;
        }

        private static string NomePadrao(DB.Document doc)
        {
            string nome = doc.Title ?? "Modelo";
            if (nome.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase)) nome = nome.Substring(0, nome.Length - 4);
            return nome;
        }

        private TextBox AddLinha(Panel panel, string label, out Button procurar)
        {
            panel.Children.Add(Theme.Label(label));
            var linha = new DockPanel();
            procurar = new Button { Content = "Procurar...", Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(procurar, Dock.Right);
            linha.Children.Add(procurar);
            var box = new TextBox { VerticalContentAlignment = VerticalAlignment.Center };
            linha.Children.Add(box);
            panel.Children.Add(linha);
            return box;
        }

        private static TextBlock Dica(Panel panel)
        {
            var t = new TextBlock
            {
                Foreground = Theme.Solid(Theme.Cinza),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0),
            };
            panel.Children.Add(t);
            return t;
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

        private string EscolherPasta(string inicial, string titulo)
        {
            var dialogo = new Microsoft.Win32.OpenFolderDialog { Title = titulo };
            if (!string.IsNullOrEmpty(inicial) && Directory.Exists(inicial)) dialogo.InitialDirectory = inicial;
            return dialogo.ShowDialog(this) == true ? dialogo.FolderName : null;
        }
    }
}
