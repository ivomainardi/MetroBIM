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
    /// Escolha da origem das configurações (pasta local ou Autodesk Forma), do setup .json,
    /// do .txt de property sets, da vista e do destino. Confere o .txt contra o modelo antes de exportar.
    /// </summary>
    public class ExportarIfcWindow : Window
    {
        private readonly DB.Document _doc;
        private readonly IfcExportSettings _settings;
        private readonly string _raizForma;

        private readonly RadioButton _origemLocal;
        private readonly RadioButton _origemForma;
        private readonly StackPanel _painelForma;
        private readonly StackPanel _painelLocal;
        private readonly ComboBox _projeto;
        private readonly TextBox _pastaPadrao;
        private readonly Button _criarPadrao;
        private readonly TextBox _pasta;
        private readonly TextBlock _dicaPasta;
        private readonly ComboBox _setup;
        private readonly TextBlock _infoSetup;
        private readonly ComboBox _psets;
        private readonly TextBlock _infoPsets;
        private readonly TextBox _mapeamento;
        private readonly ComboBox _vista;
        private readonly TextBox _saida;
        private readonly TextBox _nome;

        private HashSet<string> _parametrosModelo;
        private bool _carregando;

        public IfcExportSetup Setup { get; private set; }
        public string PropertySetsEscolhido { get; private set; }
        public string MapeamentoEscolhido { get; private set; }
        public DB.ElementId Vista { get; private set; } = DB.ElementId.InvalidElementId;
        public string PastaSaida { get; private set; }
        public string NomeArquivo { get; private set; }

        private class Opcao
        {
            public string Texto { get; set; }
            public string Caminho { get; set; }
            public DB.ElementId Id { get; set; }
            public override string ToString() => Texto;
        }

        public ExportarIfcWindow(DB.Document doc, IfcExportSettings settings)
        {
            _doc = doc;
            _settings = settings;
            _raizForma = FormaPaths.Raiz();

            Title = "MetroSP - Exportar IFC";
            Width = 680;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            MinWidth = 540;

            var corpo = new StackPanel();

            // Origem
            corpo.Children.Add(Theme.Label("Onde estão os arquivos de configuração"));
            var origens = new StackPanel { Orientation = Orientation.Horizontal };
            _origemLocal = new RadioButton { Content = "Pasta no computador ou na rede", GroupName = "origem", Margin = new Thickness(0, 0, 18, 0) };
            _origemForma = new RadioButton { Content = "Autodesk Forma (Desktop Connector)", GroupName = "origem" };
            origens.Children.Add(_origemLocal);
            origens.Children.Add(_origemForma);
            corpo.Children.Add(origens);

            // Forma: projeto + pasta padrão dentro dele
            _painelForma = new StackPanel();
            _painelForma.Children.Add(Theme.Label("Projeto no Forma"));
            _projeto = new ComboBox { Padding = new Thickness(4, 3, 4, 3) };
            _painelForma.Children.Add(_projeto);
            _painelForma.Children.Add(Theme.Label("Pasta padrão das configurações (dentro do projeto)"));
            var linhaPadrao = new DockPanel();
            _criarPadrao = new Button { Content = "Criar pasta padrão", Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(_criarPadrao, Dock.Right);
            linhaPadrao.Children.Add(_criarPadrao);
            _pastaPadrao = new TextBox { Text = settings.PastaPadraoForma, VerticalContentAlignment = VerticalAlignment.Center };
            linhaPadrao.Children.Add(_pastaPadrao);
            _painelForma.Children.Add(linhaPadrao);
            corpo.Children.Add(_painelForma);

            // Local: pasta qualquer
            _painelLocal = new StackPanel();
            _pasta = AddLinha(_painelLocal, "Pasta de configurações", out Button procurarPasta);
            corpo.Children.Add(_painelLocal);
            _dicaPasta = Dica(corpo);

            // Setup
            corpo.Children.Add(Theme.Label("Setup de exportação (.json salvo no exportador IFC)"));
            var linhaSetup = new DockPanel();
            var atualizar = new Button { Content = "Atualizar", Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(atualizar, Dock.Right);
            linhaSetup.Children.Add(atualizar);
            _setup = new ComboBox { Padding = new Thickness(4, 3, 4, 3) };
            linhaSetup.Children.Add(_setup);
            corpo.Children.Add(linhaSetup);
            _infoSetup = Dica(corpo);

            // Property sets
            corpo.Children.Add(Theme.Label("Property sets definidos pelo usuário (.txt de mapeamento)"));
            var linhaPsets = new DockPanel();
            var procurarPsets = new Button { Content = "Procurar...", Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(procurarPsets, Dock.Right);
            linhaPsets.Children.Add(procurarPsets);
            _psets = new ComboBox { IsEditable = true, Padding = new Thickness(4, 3, 4, 3) };
            linhaPsets.Children.Add(_psets);
            corpo.Children.Add(linhaPsets);
            _infoPsets = Dica(corpo);

            _mapeamento = AddLinha(corpo, "Tabela de mapeamento de parâmetros (.txt) - opcional", out Button procurarMapeamento);

            // Vista e destino
            corpo.Children.Add(Theme.Label("Elementos a exportar"));
            _vista = new ComboBox { Padding = new Thickness(4, 3, 4, 3) };
            corpo.Children.Add(_vista);

            _saida = AddLinha(corpo, "Pasta de saída", out Button procurarSaida);
            corpo.Children.Add(Theme.Label("Nome do arquivo"));
            _nome = new TextBox { Text = NomePadrao(doc) };
            corpo.Children.Add(_nome);

            PreencherVistas();
            PreencherProjetos();
            _pasta.Text = settings.PastaLocal ?? string.Empty;
            _saida.Text = settings.PastaSaida ?? string.Empty;

            // Eventos
            _origemLocal.Checked += (s, e) => TrocarOrigem(OrigemConfiguracao.Local);
            _origemForma.Checked += (s, e) => TrocarOrigem(OrigemConfiguracao.Forma);
            procurarPasta.Click += (s, e) => ProcurarPasta();
            _pasta.LostFocus += (s, e) => { if (!_carregando) ListarSetups(null); };
            _projeto.SelectionChanged += (s, e) => { if (!_carregando) ListarSetups(null); };
            _pastaPadrao.LostFocus += (s, e) => { if (!_carregando) ListarSetups(null); };
            _criarPadrao.Click += (s, e) => CriarPastaPadrao();
            atualizar.Click += (s, e) => ListarSetups((_setup.SelectedItem as Opcao)?.Caminho);
            _setup.SelectionChanged += (s, e) => { if (!_carregando) CarregarSetup(); };
            _psets.SelectionChanged += (s, e) => { if (!_carregando) Dispatcher.BeginInvoke(new Action(ConferirPsets)); };
            _psets.LostFocus += (s, e) => ConferirPsets();
            procurarPsets.Click += (s, e) => { string f = ProcurarArquivo(_psets.Text); if (f != null) { _psets.Text = f; ConferirPsets(); } };
            procurarMapeamento.Click += (s, e) => { string f = ProcurarArquivo(_mapeamento.Text); if (f != null) _mapeamento.Text = f; };
            procurarSaida.Click += (s, e) => { string p = EscolherPasta(_saida.Text, "Pasta onde salvar o IFC"); if (p != null) _saida.Text = p; };

            var exportar = new Button { Content = "Exportar", IsDefault = true, Style = Theme.PrimaryButton };
            exportar.Click += (s, e) => Confirmar();
            var cancelar = new Button { Content = "Cancelar", IsCancel = true };

            Theme.Apply(this, "Exportar IFC", "Usa o setup e o mapeamento guardados no computador ou no Autodesk Forma", corpo, Theme.Buttons(cancelar, exportar));

            // Estado inicial: modelo na nuvem sugere o Forma
            OrigemConfiguracao origem = settings.Origem;
            if (string.IsNullOrEmpty(settings.PastaLocal) && string.IsNullOrEmpty(settings.ProjetoForma) && doc.IsModelInCloud)
                origem = OrigemConfiguracao.Forma;
            if (origem == OrigemConfiguracao.Forma) _origemForma.IsChecked = true; else _origemLocal.IsChecked = true;
        }

        /// <summary>Projetos do Desktop Connector; seleciona o do modelo aberto, senão o último usado.</summary>
        private void PreencherProjetos()
        {
            _carregando = true;
            foreach (FormaPaths.Projeto p in FormaPaths.Projetos(_raizForma)) _projeto.Items.Add(p);
            List<FormaPaths.Projeto> todos = _projeto.Items.Cast<FormaPaths.Projeto>().ToList();
            string doModelo = FormaPaths.ProjetoDoModelo(_doc);
            _projeto.SelectedItem =
                todos.FirstOrDefault(p => string.Equals(p.Nome, doModelo, StringComparison.OrdinalIgnoreCase))
                ?? todos.FirstOrDefault(p => string.Equals(p.Caminho, _settings.ProjetoForma, StringComparison.OrdinalIgnoreCase));
            _carregando = false;
        }

        /// <summary>Pasta onde procurar, conforme a origem escolhida.</summary>
        private string PastaAtual()
        {
            _settings.PastaLocal = _pasta.Text.Trim();
            _settings.ProjetoForma = (_projeto.SelectedItem as FormaPaths.Projeto)?.Caminho;
            string padrao = _pastaPadrao.Text.Trim().Trim('\\', '/');
            _settings.PastaPadraoForma = padrao.Length > 0 ? padrao : FormaPaths.PastaPadraoInicial;
            return _settings.PastaConfiguracoes ?? string.Empty;
        }

        private void TrocarOrigem(OrigemConfiguracao origem)
        {
            _settings.Origem = origem;
            bool forma = origem == OrigemConfiguracao.Forma;
            _painelForma.Visibility = forma ? Visibility.Visible : Visibility.Collapsed;
            _painelLocal.Visibility = forma ? Visibility.Collapsed : Visibility.Visible;
            ListarSetups(_settings.Setup);
        }

        private void ProcurarPasta()
        {
            string p = EscolherPasta(_pasta.Text.Trim(), "Pasta com os arquivos de configuração da exportação IFC");
            if (p == null) return;
            _pasta.Text = p;
            ListarSetups(null);
        }

        private void CriarPastaPadrao()
        {
            string pasta = PastaAtual();
            if (pasta.Length == 0) return;
            if (MessageBox.Show(this, $"Criar a pasta padrão no projeto?\n\n{pasta}\n  Setups\n  PropertySets\n  Mapeamentos\n\n" +
                    "O Desktop Connector envia a pasta para o Forma, e ela fica visível para toda a equipe do projeto.",
                    Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            try { FormaPaths.CriarPastaPadrao(pasta); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível criar a pasta:\n" + ex.Message, Title);
                return;
            }
            ListarSetups(null);
        }

        private void ListarSetups(string selecionar)
        {
            string pasta = PastaAtual();
            bool forma = _settings.Origem == OrigemConfiguracao.Forma;
            bool existe = pasta.Length > 0 && Directory.Exists(pasta);
            _criarPadrao.IsEnabled = forma && pasta.Length > 0 && !IfcExportSetup.EhPastaPadrao(pasta);

            _carregando = true;
            _setup.Items.Clear();
            _psets.Items.Clear();
            if (existe)
            {
                foreach (string f in IfcExportSetup.Procurar(pasta))
                    _setup.Items.Add(new Opcao { Texto = Path.GetFileName(f), Caminho = f });
                foreach (string f in IfcExportSetup.ProcurarTxt(pasta))
                    _psets.Items.Add(new Opcao { Texto = f, Caminho = f });
            }
            Opcao escolhido = _setup.Items.Cast<Opcao>()
                .FirstOrDefault(o => string.Equals(o.Caminho, selecionar, StringComparison.OrdinalIgnoreCase))
                ?? _setup.Items.Cast<Opcao>().FirstOrDefault();
            _setup.SelectedItem = escolhido;
            _carregando = false;

            // Dica sobre a pasta
            if (forma && _raizForma == null)
                _dicaPasta.Text = "Desktop Connector não encontrado. Instale e entre no Autodesk Desktop Connector para ver os projetos do Forma.";
            else if (forma && _projeto.SelectedItem == null)
                _dicaPasta.Text = _projeto.Items.Count == 0 ? "Nenhum projeto sincronizado pelo Desktop Connector." : "Escolha o projeto.";
            else if (forma && !existe)
                _dicaPasta.Text = "Este projeto ainda não tem a pasta padrão. Use \"Criar pasta padrão\" e coloque nela o setup e os .txt.";
            else if (forma)
                _dicaPasta.Text = FormaPaths.Descrever(_raizForma, pasta) +
                    (IfcExportSetup.EhPastaPadrao(pasta) ? "" : "  (sem as subpastas Setups, PropertySets e Mapeamentos: procura na pasta toda)");
            else
                _dicaPasta.Text = "Pode ser uma pasta local ou de rede. Se tiver as subpastas Setups, PropertySets e Mapeamentos, usa a mesma regra da pasta padrão.";

            if (!existe)
                _infoSetup.Text = "";
            else if (_setup.Items.Count == 0)
                _infoSetup.Text = "Nenhum setup .json. No Revit: Exportar › IFC › Modificar configuração › Salvar configuração, e salve " +
                    (IfcExportSetup.EhPastaPadrao(pasta) ? "na subpasta Setups." : "nesta pasta.");
            CarregarSetup();
        }

        private void CarregarSetup()
        {
            Setup = null;
            var item = _setup.SelectedItem as Opcao;
            if (item == null)
            {
                ConferirPsets();
                return;
            }
            try
            {
                Setup = IfcExportSetup.Load(item.Caminho, PastaAtual());
            }
            catch (Exception ex)
            {
                _infoSetup.Text = "Não foi possível ler o setup: " + ex.Message;
                ConferirPsets();
                return;
            }

            var partes = new List<string> { Setup.Nome, Setup.DescricaoVersao };
            if (Setup.SoVisiveisNaVista) partes.Add("só elementos visíveis na vista");
            if (Setup.GravaGuid) partes.Add("grava o GUID IFC nos elementos");
            _infoSetup.Text = string.Join(" · ", partes);

            // .txt de psets: o do setup (resolvido nesta máquina), senão o último escolhido à mão
            _carregando = true;
            string psets = Setup.PropertySets.Caminho;
            if (psets == null && !string.IsNullOrEmpty(_settings.PropertySets) && File.Exists(_settings.PropertySets))
                psets = _settings.PropertySets;
            _psets.Text = psets ?? string.Empty;
            _mapeamento.Text = Setup.MapeamentoParametros.Caminho ?? string.Empty;

            // Vista: se o setup filtra pela vista, sugere a vista 3D ativa
            if (Setup.SoVisiveisNaVista && _vista.SelectedIndex == 0 && _vista.Items.Count > 1)
            {
                DB.ElementId ativa = _doc.ActiveView?.Id;
                _vista.SelectedItem = _vista.Items.Cast<Opcao>().FirstOrDefault(o => o.Id == ativa) ?? _vista.Items[1];
            }
            _carregando = false;
            ConferirPsets();
        }

        /// <summary>Lê o .txt com o mesmo leitor do Verificar IDS e procura os parâmetros no modelo.</summary>
        private void ConferirPsets()
        {
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
            if (mapa.Warnings.Count > 0) texto += " " + string.Join(" ", mapa.Warnings.Take(3));
            _infoPsets.Text = texto;
            _infoPsets.Foreground = Theme.Solid(ausentes.Count > 0 || mapa.Warnings.Count > 0 ? Theme.Laranja : Theme.Cinza);
        }

        private void Confirmar()
        {
            if (Setup == null)
            {
                MessageBox.Show(this, "Escolha um setup de exportação (.json).", Title);
                return;
            }
            string psets = TextoPsets();
            if (psets.Length > 0 && !File.Exists(psets))
            {
                MessageBox.Show(this, $"Arquivo de property sets não encontrado:\n{psets}", Title);
                return;
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

            _settings.Setup = Setup.Arquivo;
            _settings.PropertySets = string.Equals(psets, Setup.PropertySets.Caminho, StringComparison.OrdinalIgnoreCase) ? null : psets;
            _settings.PastaSaida = saida;
            DialogResult = true;
        }

        private string TextoPsets() => ((_psets.SelectedItem as Opcao)?.Caminho ?? _psets.Text ?? string.Empty).Trim();

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

        private string ProcurarArquivo(string atual)
        {
            var dialogo = new Microsoft.Win32.OpenFileDialog { Filter = "Texto (*.txt)|*.txt|Todos os arquivos (*.*)|*.*" };
            string pasta = File.Exists(atual?.Trim()) ? Path.GetDirectoryName(atual.Trim()) : PastaAtual();
            if (Directory.Exists(pasta)) dialogo.InitialDirectory = pasta;
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
