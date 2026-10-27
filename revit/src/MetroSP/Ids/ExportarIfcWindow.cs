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
    /// O usuário indica cada arquivo da exportação: o setup .json, o .txt de property sets e, se usar,
    /// o .txt de mapeamento de parâmetros; depois a vista e o destino. Os arquivos podem estar em qualquer
    /// pasta (computador, rede ou Forma pelo Desktop Connector) e as últimas escolhas ficam lembradas.
    /// Confere o .txt de property sets contra o modelo antes de exportar.
    /// </summary>
    public class ExportarIfcWindow : Window
    {
        private readonly DB.Document _doc;
        private readonly IfcExportSettings _settings;

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

        public ExportarIfcWindow(DB.Document doc, IfcExportSettings settings)
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
            dica.Text = "Indique onde está cada arquivo. Pode ser uma pasta do computador, da rede ou do Forma (pelo Desktop Connector). " +
                        "As escolhas ficam lembradas para a próxima exportação.";

            _setup = AddLinha(corpo, "1. Setup de exportação (.json salvo no exportador IFC)", out Button procurarSetup);
            _infoSetup = Dica(corpo);
            _psets = AddLinha(corpo, "2. Property sets definidos pelo usuário (.txt)", out Button procurarPsets);
            _infoPsets = Dica(corpo);
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

            procurarSetup.Click += (s, e) =>
            {
                string f = ProcurarArquivo(_setup.Text, "Setup de exportação IFC (*.json)|*.json|Todos os arquivos (*.*)|*.*", "Onde está o setup de exportação (.json)?");
                if (f == null) return;
                _setup.Text = f;
                CarregarSetup(preencherTxt: true);
            };
            _setup.LostFocus += (s, e) => { if (Setup?.Arquivo != _setup.Text.Trim()) CarregarSetup(preencherTxt: true); };
            procurarPsets.Click += (s, e) =>
            {
                string f = ProcurarArquivo(Inicial(_psets.Text), "Texto (*.txt)|*.txt|Todos os arquivos (*.*)|*.*", "Onde está o .txt de property sets?");
                if (f != null) { _psets.Text = f; ConferirPsets(); }
            };
            _psets.LostFocus += (s, e) => ConferirPsets();
            procurarMapeamento.Click += (s, e) =>
            {
                string f = ProcurarArquivo(Inicial(_mapeamento.Text), "Texto (*.txt)|*.txt|Todos os arquivos (*.*)|*.*", "Onde está o .txt de mapeamento de parâmetros?");
                if (f != null) { _mapeamento.Text = f; ConferirMapeamento(); }
            };
            _mapeamento.LostFocus += (s, e) => ConferirMapeamento();
            procurarSaida.Click += (s, e) => { string p = EscolherPasta(_saida.Text, "Pasta onde salvar o IFC"); if (p != null) _saida.Text = p; };

            var exportar = new Button { Content = "Exportar", IsDefault = true, Style = Theme.PrimaryButton };
            exportar.Click += (s, e) => Confirmar();
            var cancelar = new Button { Content = "Cancelar", IsCancel = true };

            Theme.Apply(this, "Exportar IFC", "Indique o setup e os arquivos de mapeamento de cada exportação", corpo, Theme.Buttons(cancelar, exportar));
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
                MessageBox.Show(this, "Indique um setup de exportação (.json) válido.", Title);
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
