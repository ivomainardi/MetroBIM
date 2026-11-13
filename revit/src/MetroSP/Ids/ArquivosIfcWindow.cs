using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MetroSP.Ui;
using DB = Autodesk.Revit.DB;

namespace MetroSP.Ids
{
    /// <summary>
    /// Gestão dos arquivos da exportação IFC por classe: setups (.json) com as opções principais editáveis,
    /// property sets (.txt) conferidos no modelo aberto e mapeamentos (.txt). Aceita vários arquivos de uma vez
    /// (arrastando ou escolhendo) e mostra de onde vem cada um: seu, do Forma online ou instalado.
    /// </summary>
    public class ArquivosIfcWindow : Window
    {
        private static readonly ArquivosIfc.Tipo[] Tipos = { ArquivosIfc.Tipo.Setup, ArquivosIfc.Tipo.PropertySets, ArquivosIfc.Tipo.Mapeamento };

        private readonly DB.Document _doc;
        private ModeloIfc _modelo;
        private HashSet<string> _nomes;
        private DateTime _conferidoEm;
        private readonly TabControl _abas;
        private readonly Dictionary<ArquivosIfc.Tipo, ListBox> _listas = new Dictionary<ArquivosIfc.Tipo, ListBox>();
        private readonly Dictionary<ArquivosIfc.Tipo, ContentControl> _detalhes = new Dictionary<ArquivosIfc.Tipo, ContentControl>();
        private readonly Dictionary<ArquivosIfc.Tipo, TextBlock> _titulosAba = new Dictionary<ArquivosIfc.Tipo, TextBlock>();
        private readonly TextBlock _resumo;
        private OpcoesSetup _setup;
        private Button _salvarSetup, _descartarSetup;
        private bool _carregando;

        /// <summary>true se algum arquivo foi gravado, apagado ou trocado de classe (a janela de exportação recarrega).</summary>
        public bool Mudou { get; private set; }

        public ArquivosIfcWindow(DB.Document doc, string classe = null, ArquivosIfc.Tipo aba = ArquivosIfc.Tipo.Setup)
        {
            _doc = doc;
            Title = "MetroSP - Arquivos IFC";
            Width = 900;
            Height = 660;
            MinWidth = 700;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            AllowDrop = true;
            Drop += (s, e) =>
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] arquivos) Trazer(arquivos);
            };
            DragOver += (s, e) =>
            {
                e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            };

            var corpo = new DockPanel();

            // Área para soltar arquivos (a janela inteira aceita).
            var solta = new Border
            {
                BorderBrush = Theme.Solid(Color.FromRgb(0xC8, 0xC8, 0xC8)),
                BorderThickness = new Thickness(1.5),
                Background = Theme.Solid(Color.FromRgb(0xFC, 0xFC, 0xFC)),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 8),
            };
            var linhaSolta = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            linhaSolta.Children.Add(new TextBlock { Text = "Arraste aqui um ou vários .json e .txt, ou ", Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center });
            Button escolher = Theme.Link("escolha os arquivos...");
            escolher.Click += (s, e) => Escolher();
            linhaSolta.Children.Add(escolher);
            solta.Child = linhaSolta;
            DockPanel.SetDock(solta, Dock.Top);
            corpo.Children.Add(solta);

            _abas = new TabControl();
            foreach (ArquivosIfc.Tipo t in Tipos)
            {
                var titulo = new TextBlock();
                _titulosAba[t] = titulo;
                var grade = new Grid();
                grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
                grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var lista = new ListBox { Margin = new Thickness(0, 0, 12, 0), BorderBrush = Theme.Solid(Theme.CinzaClaro), HorizontalContentAlignment = HorizontalAlignment.Stretch };
                lista.Resources[SystemColors.HighlightBrushKey] = Theme.Solid(Theme.LaranjaSelecao);
                lista.Resources[SystemColors.HighlightTextBrushKey] = Theme.Solid(Theme.Preto);
                lista.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = Theme.Solid(Theme.LaranjaSelecao);
                ArquivosIfc.Tipo tipo = t;
                lista.SelectionChanged += (s, e) => { if (!_carregando) MostrarDetalhe(tipo); };
                _listas[t] = lista;
                grade.Children.Add(lista);
                var detalhe = new ContentControl();
                Grid.SetColumn(detalhe, 1);
                _detalhes[t] = detalhe;
                grade.Children.Add(detalhe);
                _abas.Items.Add(new TabItem { Header = titulo, Content = grade, Tag = t });
            }
            _abas.SelectionChanged += (s, e) =>
            {
                // Só a aba de property sets é montada ao aparecer (a conferência no modelo demora); as outras já estão prontas
                // e remontar o setup perderia o que não foi salvo.
                if (e.Source == _abas && !_carregando && _abas.SelectedItem is TabItem ti && ti.Tag is ArquivosIfc.Tipo t &&
                    t == ArquivosIfc.Tipo.PropertySets && _detalhes[t].Content == null)
                    MostrarDetalhe(t);
            };
            corpo.Children.Add(_abas);

            _resumo = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), Text = "Ordem de uso: seu › Forma online › instalado" };
            var pasta = new Button { Content = "Abrir pasta dos seus arquivos" };
            pasta.Click += (s, e) => AbrirPasta(ClassesIfc.PastaUsuario);
            var fechar = new Button { Content = "Fechar", IsCancel = true, Style = Theme.PrimaryButton };
            fechar.Click += (s, e) => Close();
            Closing += (s, e) =>
            {
                if (_setup != null && _setup.Alterado && !PerguntarDescartar()) e.Cancel = true;
            };

            Theme.Apply(this, "Arquivos IFC", "Setups, property sets e mapeamentos usados pela exportação", corpo, Theme.Rodape(_resumo, pasta, fechar));

            Recarregar(classe);
            _abas.SelectedIndex = Array.IndexOf(Tipos, aba);
        }

        // ---------- listas ----------

        private ArquivosIfc.Tipo AbaAtual => _abas.SelectedItem is TabItem ti && ti.Tag is ArquivosIfc.Tipo t ? t : ArquivosIfc.Tipo.Setup;

        private ArquivosIfc.Arquivo Escolhido(ArquivosIfc.Tipo t) => (_listas[t].SelectedItem as ListBoxItem)?.Tag as ArquivosIfc.Arquivo;

        /// <summary>Relê as pastas e mantém a classe escolhida em cada aba (ou vai para a indicada).</summary>
        private void Recarregar(string classe = null)
        {
            _carregando = true;
            try
            {
                foreach (ArquivosIfc.Tipo t in Tipos)
                {
                    string manter = classe ?? Escolhido(t)?.Classe;
                    List<ArquivosIfc.Arquivo> arquivos = ArquivosIfc.Listar(t);
                    _titulosAba[t].Text = $"{ArquivosIfc.Descricao(t)} ({ArquivosIfc.Extensao(t)})  {arquivos.Count}";
                    ListBox lista = _listas[t];
                    lista.Items.Clear();
                    foreach (ArquivosIfc.Arquivo a in arquivos) lista.Items.Add(ItemDaLista(a));
                    ListBoxItem sel = lista.Items.Cast<ListBoxItem>().FirstOrDefault(i => string.Equals(((ArquivosIfc.Arquivo)i.Tag).Classe, manter, StringComparison.CurrentCultureIgnoreCase))
                                      ?? lista.Items.Cast<ListBoxItem>().FirstOrDefault();
                    lista.SelectedItem = sel;
                }
            }
            finally { _carregando = false; }
            _setup = null;
            foreach (ArquivosIfc.Tipo t in Tipos) MostrarDetalhe(t);
        }

        private ListBoxItem ItemDaLista(ArquivosIfc.Arquivo a)
        {
            var d = new DockPanel { Margin = new Thickness(2, 3, 2, 3) };
            UIElement direita;
            if (a.Tipo == ArquivosIfc.Tipo.PropertySets && SeloPsets(a) is Border selo) direita = selo;
            else if (a.Tipo == ArquivosIfc.Tipo.Setup && !SetupAceito(a.Caminho)) direita = Theme.Selo("não é IFC4 nem 4.3", Theme.Estado.Aviso);
            else direita = new TextBlock { Text = a.Origem, Foreground = Theme.Solid(Theme.Cinza), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(direita, Dock.Right);
            d.Children.Add(direita);
            d.Children.Add(new TextBlock { Text = a.Classe, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            return new ListBoxItem { Content = d, Tag = a, ToolTip = $"{a.Caminho}\nOrigem: {a.Origem}" };
        }

        /// <summary>Selo da lista de property sets: parâmetros não encontrados no modelo (só pelo nome, é rápido).</summary>
        private Border SeloPsets(ArquivosIfc.Arquivo a)
        {
            if (_doc == null) return null;
            try
            {
                ConferenciaPsets c = ConferenciaPsets.Fazer(a.Caminho, Nomes(), null);
                return c.Ausentes.Count == 0 ? Theme.Selo("ok", Theme.Estado.Ok) : Theme.Selo(c.Ausentes.Count.ToString(), Theme.Estado.Aviso);
            }
            catch { return Theme.Selo("erro", Theme.Estado.Aviso); }
        }

        private static bool SetupAceito(string caminho)
        {
            try { return OpcoesSetup.Ler(caminho).EhVersaoAceita; } catch { return false; }
        }

        /// <summary>Só os nomes dos parâmetros do modelo (rápido), para os selos da lista.</summary>
        private HashSet<string> Nomes()
        {
            if (_nomes == null && _doc != null) _nomes = _modelo?.Parametros ?? ModeloIfc.NomesDosParametros(_doc);
            return _nomes;
        }

        private ModeloIfc Modelo()
        {
            if (_modelo == null && _doc != null)
            {
                Cursor antes = Mouse.OverrideCursor;
                Mouse.OverrideCursor = Cursors.Wait;
                try { _modelo = new ModeloIfc(_doc, _nomes); _nomes = _modelo.Parametros; _conferidoEm = DateTime.Now; }
                finally { Mouse.OverrideCursor = antes; }
            }
            return _modelo;
        }

        private void MostrarDetalhe(ArquivosIfc.Tipo t)
        {
            ArquivosIfc.Arquivo a = Escolhido(t);
            if (t == ArquivosIfc.Tipo.Setup)
            {
                if (_setup != null && _setup.Alterado && a != null && !string.Equals(_setup.Arquivo, a.Caminho, StringComparison.OrdinalIgnoreCase))
                    PerguntarDescartar(podeCancelar: false);
                _setup = null;
            }
            if (a == null)
            {
                _detalhes[t].Content = new TextBlock
                {
                    Text = $"Nenhum arquivo de {ArquivosIfc.Descricao(t).ToLowerInvariant()}. Arraste ou escolha arquivos para trazer.",
                    Foreground = Theme.Solid(Theme.Cinza), TextWrapping = TextWrapping.Wrap,
                };
                return;
            }
            try
            {
                switch (t)
                {
                    case ArquivosIfc.Tipo.Setup: _detalhes[t].Content = PainelSetup(a); break;
                    case ArquivosIfc.Tipo.PropertySets:
                        if (AbaAtual == t) _detalhes[t].Content = PainelPsets(a);
                        else _detalhes[t].Content = null; // a conferência no modelo só roda quando a aba aparece
                        break;
                    default: _detalhes[t].Content = PainelMapeamento(a); break;
                }
            }
            catch (Exception ex)
            {
                _detalhes[t].Content = new TextBlock { Text = "Não foi possível ler " + a.Nome + ":\n" + ex.Message, TextWrapping = TextWrapping.Wrap, Foreground = Theme.Solid(Theme.LaranjaTexto) };
            }
        }

        // ---------- painéis ----------

        private static DockPanel Cabecalho(ArquivosIfc.Arquivo a, params UIElement[] selos)
        {
            var d = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var direita = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (UIElement s in selos.Where(x => x != null))
            {
                if (s is FrameworkElement f) f.Margin = new Thickness(6, 0, 0, 0);
                direita.Children.Add(s);
            }
            DockPanel.SetDock(direita, Dock.Right);
            d.Children.Add(direita);
            var nome = new StackPanel();
            nome.Children.Add(new TextBlock { Text = a.Nome, FontWeight = FontWeights.SemiBold, FontSize = 13 });
            string origem = a.Origem == ArquivosIfc.Seu
                ? (a.TemPadrao ? "seu (no lugar do padrão)" : "seu")
                : a.Origem;
            nome.Children.Add(Theme.Detalhe($"Classe {a.Classe} · {origem} · alterado em {File.GetLastWriteTime(a.Caminho):dd/MM/yyyy HH:mm}"));
            nome.ToolTip = a.Caminho;
            d.Children.Add(nome);
            return d;
        }

        /// <summary>Botões comuns: voltar ao padrão ou apagar a sua cópia, mudar de classe e salvar uma cópia em outro lugar.</summary>
        private WrapPanel AcoesDoArquivo(ArquivosIfc.Arquivo a, params Button[] antes)
        {
            var p = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            void Add(Button b) { b.Margin = new Thickness(0, 0, 8, 6); p.Children.Add(b); }
            foreach (Button b in antes) Add(b);
            if (a.DoUsuario)
            {
                var apagar = new Button { Content = a.TemPadrao ? "Voltar ao padrão" : "Apagar" };
                apagar.ToolTip = a.TemPadrao ? "Apaga a sua cópia; a classe volta a usar o arquivo do Forma online ou instalado." : "Apaga o arquivo (ele fica guardado em Anteriores).";
                apagar.Click += (s, e) =>
                {
                    string pergunta = a.TemPadrao ? $"Apagar a sua cópia de {a.Nome} e voltar ao padrão?" : $"Apagar {a.Nome}?";
                    if (MessageBox.Show(this, pergunta + "\n\nA versão atual fica guardada em " + ArquivosIfc.PastaAnteriores, Title,
                            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    Executar(() => ArquivosIfc.ApagarDoUsuario(a.Tipo, a.Classe), a.Classe);
                };
                Add(apagar);
                var classe = new Button { Content = "Mudar classe..." };
                classe.Click += (s, e) =>
                {
                    string nova = PedirTexto("Mudar classe", $"Nova classe para {a.Nome}:", a.Classe);
                    if (nova == null || string.Equals(nova, a.Classe, StringComparison.CurrentCulture)) return;
                    if (!ArquivosIfc.NomeValido(nova)) { MessageBox.Show(this, "Nome de classe inválido.", Title); return; }
                    if (File.Exists(ArquivosIfc.Destino(a.Tipo, nova)) &&
                        MessageBox.Show(this, $"Você já tem {ArquivosIfc.Descricao(a.Tipo).ToLowerInvariant()} da classe {nova}. Substituir? (o atual fica em Anteriores)", Title,
                            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    Executar(() => ArquivosIfc.MudarClasse(a.Tipo, a.Classe, nova), nova);
                };
                Add(classe);
            }
            var copia = new Button { Content = "Salvar uma cópia..." };
            copia.Click += (s, e) =>
            {
                var d = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = a.Nome,
                    Filter = a.Tipo == ArquivosIfc.Tipo.Setup ? "Setup IFC (*.json)|*.json" : "Texto (*.txt)|*.txt",
                    Title = "Salvar uma cópia de " + a.Nome,
                };
                if (d.ShowDialog(this) != true) return;
                try { File.Copy(a.Caminho, d.FileName, true); _resumo.Text = "Cópia salva em " + d.FileName; }
                catch (Exception ex) { MessageBox.Show(this, "Não foi possível copiar: " + ex.Message, Title); }
            };
            Add(copia);
            var pasta = new Button { Content = "Abrir pasta" };
            pasta.Click += (s, e) => AbrirPasta(Path.GetDirectoryName(a.Caminho), a.Caminho);
            Add(pasta);
            return p;
        }

        private UIElement PainelSetup(ArquivosIfc.Arquivo a)
        {
            OpcoesSetup o = OpcoesSetup.Ler(a.Caminho);
            _setup = o;
            var painel = new StackPanel();
            Border seloVersao = Theme.Selo();
            void AtualizarVersao() => Theme.MudarSelo(seloVersao, o.EhVersaoAceita ? "pronto para exportar" : "a exportação MetroSP exige " + OpcoesSetup.VersoesAceitas,
                o.EhVersaoAceita ? Theme.Estado.Ok : Theme.Estado.Aviso);
            AtualizarVersao();
            painel.Children.Add(Cabecalho(a, seloVersao));

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            void Linha(UIElement esquerda, UIElement direita)
            {
                int r = g.RowDefinitions.Count;
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                if (esquerda != null) { Grid.SetRow(esquerda, r); g.Children.Add(esquerda); }
                if (direita != null)
                {
                    Grid.SetRow(direita, r);
                    if (esquerda == null) Grid.SetColumnSpan(direita, 2); else Grid.SetColumn(direita, 1);
                    if (direita is FrameworkElement f) f.Margin = new Thickness(0, 2, 0, 2);
                    g.Children.Add(direita);
                }
            }
            TextBlock Nome(string t) => new TextBlock { Text = t, VerticalAlignment = VerticalAlignment.Center, Foreground = Theme.Solid(Color.FromRgb(0x44, 0x44, 0x44)) };
            void Titulo(string t)
            {
                TextBlock s = Theme.Secao(t);
                s.Margin = new Thickness(0, g.RowDefinitions.Count == 0 ? 0 : 10, 0, 3);
                Linha(null, s);
            }
            CheckBox Caixa(string texto, bool valor, Action<bool> mudar, string dica = null)
            {
                var c = new CheckBox { Content = texto, IsChecked = valor, ToolTip = dica };
                c.Click += (s, e) => { mudar(c.IsChecked == true); Alterou(); };
                return c;
            }

            Titulo("Geral");
            var versao = new ComboBox();
            foreach (var (nome, texto) in OpcoesSetup.Versoes)
                if (Enum.TryParse(nome, true, out DB.IFCVersion _)) versao.Items.Add(new ComboBoxItem { Content = texto, Tag = nome });
            if (!versao.Items.Cast<ComboBoxItem>().Any(i => string.Equals((string)i.Tag, o.Versao, StringComparison.OrdinalIgnoreCase)))
                versao.Items.Add(new ComboBoxItem { Content = o.Versao + " (do arquivo)", Tag = o.Versao });
            versao.SelectedItem = versao.Items.Cast<ComboBoxItem>().First(i => string.Equals((string)i.Tag, o.Versao, StringComparison.OrdinalIgnoreCase));
            versao.SelectionChanged += (s, e) => { o.Versao = (string)((ComboBoxItem)versao.SelectedItem).Tag; AtualizarVersao(); Alterou(); };
            Linha(Nome("Versão IFC"), versao);

            var fase = new ComboBox();
            fase.Items.Add(new ComboBoxItem { Content = "Fase da vista / do modelo", Tag = -1L });
            if (_doc != null)
                foreach (DB.Phase ph in _doc.Phases) fase.Items.Add(new ComboBoxItem { Content = ph.Name, Tag = Compat.Numero(ph.Id) });
            if (!fase.Items.Cast<ComboBoxItem>().Any(i => (long)i.Tag == o.Fase))
                fase.Items.Add(new ComboBoxItem { Content = $"Fase gravada no arquivo (id {o.Fase}, não existe neste modelo)", Tag = o.Fase });
            fase.SelectedItem = fase.Items.Cast<ComboBoxItem>().First(i => (long)i.Tag == o.Fase);
            fase.SelectionChanged += (s, e) => { o.Fase = (long)((ComboBoxItem)fase.SelectedItem).Tag; Alterou(); };
            fase.ToolTip = "Só vale quando a exportação é do modelo inteiro; com uma vista escolhida, vale a fase da vista.";
            Linha(Nome("Fase"), fase);

            var espacos = new ComboBox();
            foreach (string t in new[] { "Nenhum", "1º nível", "2º nível" }) espacos.Items.Add(t);
            espacos.SelectedIndex = Math.Max(0, Math.Min(2, o.SpaceBoundaries));
            espacos.SelectionChanged += (s, e) => { o.SpaceBoundaries = espacos.SelectedIndex; Alterou(); };
            Linha(Nome("Space boundaries"), espacos);
            Linha(null, Caixa("Dividir paredes e pilares por nível", o.DividirPorNivel, v => o.DividirPorNivel = v));

            Titulo("O que entra");
            Linha(null, Caixa("Só elementos visíveis na vista", o.SoVisiveis, v => o.SoVisiveis = v,
                "Sugestão inicial da janela Exportar IFC; lá você escolhe a vista ou o modelo inteiro."));
            Linha(null, Caixa("Exportar quantidades base", o.Quantidades, v => o.Quantidades = v));
            Linha(null, Caixa("Gravar o GUID IFC no elemento", o.GravaGuid, v => o.GravaGuid = v,
                "Grava o IfcGUID nos elementos do Revit ao exportar (o modelo fica alterado)."));

            Titulo("Propriedades");
            Linha(null, Caixa("Psets do IFC padrão (Pset_WallCommon...)", o.PsetsComuns, v => o.PsetsComuns = v));
            Linha(null, Caixa("Psets internos do Revit", o.PsetsDoRevit, v => o.PsetsDoRevit = v));
            Linha(null, Caixa("Psets de material", o.PsetsDeMaterial, v => o.PsetsDeMaterial = v));
            ArquivosIfc.Arquivo psets = ArquivosIfc.Obter(ArquivosIfc.Tipo.PropertySets, a.Classe);
            ArquivosIfc.Arquivo mapa = ArquivosIfc.Obter(ArquivosIfc.Tipo.Mapeamento, a.Classe);
            Linha(Nome("Property sets"), Theme.Detalhe(psets != null ? $"{psets.Nome} ({psets.Origem}), da classe {a.Classe}" : $"nenhum .txt com o nome {a.Classe}"));
            Linha(Nome("Mapeamento"), Theme.Detalhe(mapa != null ? $"{mapa.Nome} ({mapa.Origem})" : "nenhum (opcional)"));

            Titulo("Localização");
            Linha(null, Theme.Detalhe("🔒 Shared Coordinates e EPSG: definidos pelo MetroSP na exportação (EPSG em branco por padrão)."));
            painel.Children.Add(g);

            _salvarSetup = new Button { Content = "Salvar", Style = Theme.PrimaryButton, IsEnabled = false };
            _salvarSetup.Click += (s, e) => SalvarSetup(a);
            _descartarSetup = new Button { Content = "Descartar", IsEnabled = false };
            _descartarSetup.Click += (s, e) => { _setup = null; MostrarDetalhe(ArquivosIfc.Tipo.Setup); _resumo.Text = "Alterações descartadas."; };
            painel.Children.Add(AcoesDoArquivo(a, _salvarSetup, _descartarSetup));
            if (!a.DoUsuario)
                painel.Children.Add(Theme.Detalhe($"Ao salvar, o MetroSP grava uma cópia sua em {ArquivosIfc.Destino(ArquivosIfc.Tipo.Setup, a.Classe)}, que vale no lugar do arquivo {a.Origem}."));
            return new ScrollViewer { Content = painel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private void Alterou()
        {
            bool alterado = _setup?.Alterado == true;
            if (_salvarSetup != null) _salvarSetup.IsEnabled = alterado;
            if (_descartarSetup != null) _descartarSetup.IsEnabled = alterado;
            _resumo.Text = alterado ? $"Alterações não salvas em {Path.GetFileName(_setup.Arquivo)}" : "Ordem de uso: seu › Forma online › instalado";
            _resumo.Foreground = Theme.Solid(alterado ? Theme.LaranjaTexto : Theme.Cinza);
        }

        private void SalvarSetup(ArquivosIfc.Arquivo a)
        {
            if (_setup == null) return;
            if (!_setup.EhVersaoAceita &&
                MessageBox.Show(this, "A exportação MetroSP exige " + OpcoesSetup.VersoesAceitas + ". Salvar assim mesmo?", Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            string destino = ArquivosIfc.Destino(ArquivosIfc.Tipo.Setup, a.Classe);
            try
            {
                ArquivosIfc.GuardarAnterior(destino);
                _setup.Salvar(destino);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível salvar: " + ex.Message, Title);
                return;
            }
            Mudou = true;
            _setup = null;
            Recarregar(a.Classe);
            _resumo.Text = "Salvo em " + destino;
            _resumo.Foreground = Theme.Solid(Theme.Verde);
        }

        private bool PerguntarDescartar(bool podeCancelar = true)
        {
            if (_setup == null || !_setup.Alterado) return true;
            MessageBoxResult r = MessageBox.Show(this, $"Salvar as alterações em {Path.GetFileName(_setup.Arquivo)}?", Title,
                podeCancelar ? MessageBoxButton.YesNoCancel : MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return false;
            if (r == MessageBoxResult.Yes)
            {
                string classe = Path.GetFileNameWithoutExtension(_setup.Arquivo);
                string destino = ArquivosIfc.Destino(ArquivosIfc.Tipo.Setup, classe);
                try { ArquivosIfc.GuardarAnterior(destino); _setup.Salvar(destino); Mudou = true; }
                catch (Exception ex) { MessageBox.Show(this, "Não foi possível salvar: " + ex.Message, Title); return false; }
            }
            _setup = null;
            return true;
        }

        private UIElement PainelPsets(ArquivosIfc.Arquivo a)
        {
            var painel = new DockPanel();
            ModeloIfc modelo = Modelo();
            ConferenciaPsets c = ConferenciaPsets.Fazer(a.Caminho, modelo?.Parametros, modelo);

            var topo = new StackPanel();
            DockPanel.SetDock(topo, Dock.Top);
            topo.Children.Add(Cabecalho(a));
            var selos = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            selos.Children.Add(Theme.Chip($"{c.Linhas} propriedades"));
            selos.Children.Add(Theme.Chip($"{c.Psets.Count} property sets"));
            if (modelo != null)
            {
                selos.Children.Add(c.Ausentes.Count == 0 ? Theme.Chip("todos os parâmetros encontrados no modelo")
                    : Theme.Chip(c.Ausentes.Count == 1 ? "1 parâmetro não encontrado" : $"{c.Ausentes.Count} parâmetros não encontrados", destaque: true));
                if (c.SemValor > 0) selos.Children.Add(Theme.Chip($"{c.SemValor} sem valor em algum elemento", destaque: true));
            }
            if (c.Avisos.Count > 0) selos.Children.Add(Theme.Chip(c.Avisos.Count == 1 ? "1 problema de formato" : $"{c.Avisos.Count} problemas de formato", destaque: true));
            topo.Children.Add(selos);
            painel.Children.Add(topo);

            var baixo = new StackPanel();
            DockPanel.SetDock(baixo, Dock.Bottom);
            if (modelo != null && c.Ausentes.Count > 0)
                baixo.Children.Add(Theme.Detalhe("Não encontrados no modelo (ficam vazios no IFC): " + string.Join(", ", c.Ausentes.Take(15)) + (c.Ausentes.Count > 15 ? "..." : "")));
            List<ConferenciaPsets.Propriedade> vazias = c.Propriedades.Where(p => p.SemValor).ToList();
            if (vazias.Count > 0)
                baixo.Children.Add(Theme.Detalhe("Sem valor em parte dos elementos: " + string.Join(", ", vazias.Take(8).Select(p =>
                    $"{p.Nome} ({string.Join("/", p.Entidades.Take(2))} {p.Contagem.Value.ComValor} de {p.Contagem.Value.Elementos})")) + (vazias.Count > 8 ? "..." : "")));
            baixo.Children.Add(Theme.Detalhe(modelo != null ? $"Conferido no modelo {modelo.Titulo} às {_conferidoEm:HH:mm}." : "Abra um projeto para conferir os parâmetros no modelo."));

            var editar = new Button { Content = "Editar...", Style = Theme.PrimaryButton };
            editar.Click += (s, e) => EditarPsets(a);
            var verificar = new Button { Content = "Verificar de novo", IsEnabled = _doc != null };
            verificar.Click += (s, e) => { _modelo = null; _nomes = null; Recarregar(a.Classe); };
            baixo.Children.Add(AcoesDoArquivo(a, editar, verificar));
            painel.Children.Add(baixo);

            var grade = new DataGrid
            {
                AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column,
                ItemsSource = c.Psets, Margin = new Thickness(0, 0, 0, 6), SelectionMode = DataGridSelectionMode.Single,
                ToolTip = "Clique duas vezes num property set para abrir o editor nele.",
            };
            Theme.StyleSelection(grade);
            grade.Columns.Add(new DataGridTextColumn { Header = "Property set", Binding = new System.Windows.Data.Binding(nameof(ConferenciaPsets.Pset.Nome)), Width = new DataGridLength(1.2, DataGridLengthUnitType.Star) });
            grade.Columns.Add(new DataGridTextColumn { Header = "Entidades", Binding = new System.Windows.Data.Binding(nameof(ConferenciaPsets.Pset.Entidades)), Width = new DataGridLength(1.5, DataGridLengthUnitType.Star) });
            grade.Columns.Add(new DataGridTextColumn { Header = "Parâmetro no modelo", Binding = new System.Windows.Data.Binding(nameof(ConferenciaPsets.Pset.NoModelo)), Width = 140 });
            if (modelo != null)
                grade.Columns.Add(new DataGridTextColumn { Header = "Sem valor em algum", Binding = new System.Windows.Data.Binding(nameof(ConferenciaPsets.Pset.SemValor)), Width = 130 });
            var estilo = new Style(typeof(DataGridRow));
            var problema = new System.Windows.DataTrigger { Binding = new System.Windows.Data.Binding(nameof(ConferenciaPsets.Pset.Completo)), Value = false };
            problema.Setters.Add(new Setter(BackgroundProperty, Theme.LinhaProblema));
            estilo.Triggers.Add(problema);
            grade.RowStyle = estilo;
            grade.MouseDoubleClick += (s, e) => { if (grade.SelectedItem is ConferenciaPsets.Pset p) EditarPsets(a, p.Nome); };
            painel.Children.Add(grade);
            return painel;
        }

        private void EditarPsets(ArquivosIfc.Arquivo a, string pset = null)
        {
            EditarPsetsWindow editor;
            try
            {
                editor = new EditarPsetsWindow(a.Caminho, Modelo()?.Parametros, ClassesIfc.CaminhoUsuario(a.Classe),
                    podeRestaurar: a.DoUsuario && a.TemPadrao, modelo: Modelo(), psetInicial: pset) { Owner = this };
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Erro ao ler o .txt: " + ex.Message, Title);
                return;
            }
            if (editor.ShowDialog() != true) return;
            if (editor.Restaurou) ArquivosIfc.ApagarDoUsuario(ArquivosIfc.Tipo.PropertySets, a.Classe);
            Mudou = true;
            Recarregar(a.Classe);
            _resumo.Text = editor.Restaurou ? $"{a.Classe}: voltou ao padrão." : "Salvo em " + editor.ArquivoSalvo;
        }

        private UIElement PainelMapeamento(ArquivosIfc.Arquivo a)
        {
            var painel = new DockPanel();
            DockPanel cab = Cabecalho(a);
            DockPanel.SetDock(cab, Dock.Top);
            painel.Children.Add(cab);
            var abrir = new Button { Content = "Abrir no Bloco de notas" };
            abrir.Click += (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo("notepad.exe", "\"" + a.Caminho + "\"") { UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, Title); }
            };
            WrapPanel acoes = AcoesDoArquivo(a, abrir);
            DockPanel.SetDock(acoes, Dock.Bottom);
            painel.Children.Add(acoes);
            List<string> linhas = File.ReadLines(a.Caminho).Take(400).ToList();
            painel.Children.Add(new TextBox
            {
                Text = string.Join(Environment.NewLine, linhas) + (linhas.Count == 400 ? Environment.NewLine + "..." : ""),
                IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 11.5,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            });
            return painel;
        }

        // ---------- trazer arquivos ----------

        private void Escolher()
        {
            var d = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Filter = "Setups e property sets (*.json;*.txt)|*.json;*.txt|Setups IFC (*.json)|*.json|Texto (*.txt)|*.txt",
                Title = "Escolha os setups (.json), property sets e mapeamentos (.txt)",
            };
            if (d.ShowDialog(this) == true) Trazer(d.FileNames);
        }

        private class Trazido
        {
            public string Arquivo;
            public ArquivosIfc.Tipo? Tipo;
            public string Motivo;
            public ComboBox Classe, TipoEscolhido;
            public TextBlock Situacao;
        }

        private void Trazer(IEnumerable<string> arquivos)
        {
            List<string> lista = arquivos.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (lista.Count == 0) return;
            if (_setup != null && _setup.Alterado && !PerguntarDescartar()) return;
            List<string> classes = Tipos.SelectMany(ArquivosIfc.Listar).Select(x => x.Classe).Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();

            var janela = new Window
            {
                Title = "MetroSP - Trazer arquivos", Owner = this, Width = 760, SizeToContent = SizeToContent.Height, MaxHeight = 640,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.CanResizeWithGrip,
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            void Celula(UIElement e, int r, int col)
            {
                Grid.SetRow(e, r);
                Grid.SetColumn(e, col);
                if (e is FrameworkElement f) { f.Margin = new Thickness(0, 3, 8, 3); f.VerticalAlignment = VerticalAlignment.Center; }
                g.Children.Add(e);
            }
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string[] titulos = { "Arquivo", "Classe", "Tipo", "Situação" };
            for (int i = 0; i < titulos.Length; i++) Celula(Theme.Secao(titulos[i]), 0, i);

            var itens = new List<Trazido>();
            var resumo = new TextBlock();
            var ok = new Button { Content = "Trazer", IsDefault = true, Style = Theme.PrimaryButton };
            void Atualizar()
            {
                int validos = 0, substitui = 0;
                foreach (Trazido t in itens)
                {
                    ArquivosIfc.Tipo? tipo = TipoDe(t);
                    string classe = t.Classe.Text?.Trim();
                    Theme.Estado estado;
                    string texto;
                    if (tipo == null) { texto = t.Tipo == null ? "fica de fora: " + t.Motivo : "fica de fora"; estado = Theme.Estado.Neutro; }
                    else if (!ArquivosIfc.NomeValido(classe)) { texto = "indique a classe"; estado = Theme.Estado.Aviso; }
                    else
                    {
                        validos++;
                        ArquivosIfc.Arquivo atual = ArquivosIfc.Obter(tipo.Value, classe);
                        if (atual == null) { texto = "novo"; estado = Theme.Estado.Ok; }
                        else if (atual.DoUsuario) { texto = "substitui o seu (fica em Anteriores)"; estado = Theme.Estado.Aviso; substitui++; }
                        else { texto = $"vale no lugar do {atual.Origem}"; estado = Theme.Estado.Neutro; }
                    }
                    t.Situacao.Text = texto;
                    t.Situacao.Foreground = Theme.Solid(estado == Theme.Estado.Ok ? Theme.Verde : estado == Theme.Estado.Aviso ? Theme.LaranjaTexto : Theme.Cinza);
                    t.Situacao.ToolTip = texto;
                }
                ok.Content = validos == 1 ? "Trazer 1" : $"Trazer {validos}";
                ok.IsEnabled = validos > 0;
                resumo.Text = substitui == 0 ? $"{lista.Count} arquivo(s) escolhido(s)" : $"{substitui} substitui(em) arquivos seus (a versão anterior fica guardada)";
                resumo.Foreground = Theme.Solid(substitui == 0 ? Theme.Cinza : Theme.LaranjaTexto);
            }

            foreach (string f in lista)
            {
                var t = new Trazido { Arquivo = f };
                t.Tipo = ArquivosIfc.Detectar(f, out t.Motivo);
                int r = g.RowDefinitions.Count;
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Celula(new TextBlock { Text = Path.GetFileName(f), FontFamily = new FontFamily("Consolas"), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = f }, r, 0);
                t.Classe = new ComboBox { IsEditable = true, ItemsSource = classes, Text = ArquivosIfc.ClasseSugerida(f, classes) };
                t.Classe.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, e) => Atualizar()));
                t.Classe.SelectionChanged += (s, e) => Dispatcher.BeginInvoke(new Action(Atualizar));
                Celula(t.Classe, r, 1);
                t.TipoEscolhido = new ComboBox();
                bool json = Path.GetExtension(f).Equals(".json", StringComparison.OrdinalIgnoreCase);
                foreach (ArquivosIfc.Tipo tp in Tipos)
                    if ((tp == ArquivosIfc.Tipo.Setup) == json) t.TipoEscolhido.Items.Add(new ComboBoxItem { Content = ArquivosIfc.Descricao(tp), Tag = tp });
                t.TipoEscolhido.Items.Add(new ComboBoxItem { Content = "(deixar de fora)", Tag = null });
                t.TipoEscolhido.SelectedItem = t.TipoEscolhido.Items.Cast<ComboBoxItem>().FirstOrDefault(i => i.Tag is ArquivosIfc.Tipo tp && tp == t.Tipo)
                                               ?? t.TipoEscolhido.Items.Cast<ComboBoxItem>().Last();
                t.TipoEscolhido.SelectionChanged += (s, e) => Atualizar();
                Celula(t.TipoEscolhido, r, 2);
                t.Situacao = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 11, FontWeight = FontWeights.SemiBold };
                Celula(t.Situacao, r, 3);
                itens.Add(t);
            }

            ok.Click += (s, e) => janela.DialogResult = true;
            var cancelar = new Button { Content = "Cancelar", IsCancel = true };
            Atualizar();
            Theme.Apply(janela, "Trazer arquivos", "Confira a classe e o tipo de cada arquivo antes de gravar",
                new ScrollViewer { Content = g, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, Theme.Rodape(resumo, cancelar, ok));
            if (janela.ShowDialog() != true) return;

            var feitos = new List<string>();
            var erros = new List<string>();
            string ultima = null;
            foreach (Trazido t in itens)
            {
                ArquivosIfc.Tipo? tipo = TipoDe(t);
                string classe = t.Classe.Text?.Trim();
                if (tipo == null || !ArquivosIfc.NomeValido(classe)) continue;
                try
                {
                    ArquivosIfc.Gravar(t.Arquivo, tipo.Value, classe);
                    feitos.Add($"{Path.GetFileName(t.Arquivo)} → {ArquivosIfc.Descricao(tipo.Value).ToLowerInvariant()} {classe}");
                    ultima = classe;
                }
                catch (Exception ex) { erros.Add($"{Path.GetFileName(t.Arquivo)}: {ex.Message}"); }
            }
            if (feitos.Count > 0) Mudou = true;
            Recarregar(ultima);
            _resumo.Text = feitos.Count == 1 ? "Trazido: " + feitos[0] : $"{feitos.Count} arquivos trazidos";
            _resumo.Foreground = Theme.Solid(Theme.Verde);
            _resumo.ToolTip = string.Join("\n", feitos);
            if (erros.Count > 0) MessageBox.Show(this, "Não foi possível trazer:\n" + string.Join("\n", erros), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private static ArquivosIfc.Tipo? TipoDe(Trazido t) => (t.TipoEscolhido.SelectedItem as ComboBoxItem)?.Tag as ArquivosIfc.Tipo?;

        // ---------- utilidades ----------

        private void Executar(Action acao, string classe)
        {
            try { acao(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Title); return; }
            Mudou = true;
            Recarregar(classe);
        }

        private string PedirTexto(string titulo, string pergunta, string valor)
        {
            var caixa = new TextBox { Text = valor, Margin = new Thickness(0, 6, 0, 0) };
            var janela = new Window { Title = "MetroSP - " + titulo, Owner = this, Width = 380, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.CanResizeWithGrip, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var corpo = new StackPanel();
            corpo.Children.Add(new TextBlock { Text = pergunta, TextWrapping = TextWrapping.Wrap });
            corpo.Children.Add(caixa);
            var ok = new Button { Content = "OK", IsDefault = true, Style = Theme.PrimaryButton };
            ok.Click += (s, e) => janela.DialogResult = true;
            Theme.Apply(janela, titulo, null, corpo, Theme.Buttons(new Button { Content = "Cancelar", IsCancel = true }, ok));
            janela.Loaded += (s, e) => { caixa.Focus(); caixa.SelectAll(); };
            return janela.ShowDialog() == true ? caixa.Text.Trim() : null;
        }

        private void AbrirPasta(string pasta, string arquivo = null)
        {
            try
            {
                Directory.CreateDirectory(pasta);
                string args = arquivo != null && File.Exists(arquivo) ? "/select,\"" + arquivo + "\"" : "\"" + pasta + "\"";
                Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Title); }
        }
    }
}
