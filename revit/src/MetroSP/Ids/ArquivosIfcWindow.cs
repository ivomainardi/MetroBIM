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
    /// Os padrões de exportação IFC: escolhe-se um padrão na lista e aparecem juntos o setup (.json, com as opções
    /// principais editáveis), os property sets (.txt, conferidos no modelo aberto) e o mapeamento (.txt) dele.
    /// Trocar de padrão troca tudo. Aceita vários arquivos de uma vez (arrastando ou escolhendo) e mostra de onde
    /// vem cada um: seu, do Forma online ou instalado.
    /// </summary>
    public class ArquivosIfcWindow : Window
    {
        private static readonly ArquivosIfc.Tipo[] Tipos = { ArquivosIfc.Tipo.Setup, ArquivosIfc.Tipo.PropertySets, ArquivosIfc.Tipo.Mapeamento };

        private readonly DB.Document _doc;
        private ModeloIfc _modelo;
        private HashSet<string> _nomes;
        private DateTime _conferidoEm;
        private readonly ListBox _lista;
        private readonly TabControl _abas;
        private readonly Dictionary<ArquivosIfc.Tipo, ContentControl> _detalhes = new Dictionary<ArquivosIfc.Tipo, ContentControl>();
        private readonly Dictionary<ArquivosIfc.Tipo, TextBlock> _titulosAba = new Dictionary<ArquivosIfc.Tipo, TextBlock>();
        private readonly Dictionary<ArquivosIfc.Tipo, ArquivosIfc.Arquivo> _doPadrao = new Dictionary<ArquivosIfc.Tipo, ArquivosIfc.Arquivo>();
        private readonly TextBlock _nomePadrao, _infoPadrao;
        private readonly StackPanel _selosPadrao;
        private readonly Button _duplicar, _renomear;
        private readonly TextBlock _resumo;
        private OpcoesSetup _setup;
        private Button _salvarSetup, _descartarSetup;
        private bool _carregando;
        private string _classe;

        /// <summary>true se algum arquivo foi gravado, apagado ou trocado de padrão (a janela de exportação recarrega).</summary>
        public bool Mudou { get; private set; }

        public ArquivosIfcWindow(DB.Document doc, string classe = null, ArquivosIfc.Tipo aba = ArquivosIfc.Tipo.Setup)
        {
            _doc = doc;
            Title = "MetroSP - Perfis de exportação IFC";
            Width = 960;
            Height = 680;
            MinWidth = 720;
            MinHeight = 480;
            ResizeMode = ResizeMode.CanResizeWithGrip;
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
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 0, 8),
            };
            var linhaSolta = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            linhaSolta.Children.Add(new TextBlock { Text = "Arraste aqui um ou vários .json e .txt, ou ", Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center });
            Button escolher = Theme.Link("escolha os arquivos...");
            escolher.Click += (s, e) => Escolher();
            linhaSolta.Children.Add(escolher);
            solta.Child = linhaSolta;
            solta.ToolTip = "O MetroSP reconhece o tipo de cada arquivo e pergunta de qual perfil ele é.";
            DockPanel.SetDock(solta, Dock.Top);
            corpo.Children.Add(solta);

            var grade = new Grid();
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230), MinWidth = 160 });
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 360 });
            corpo.Children.Add(grade);

            // Esquerda: a lista de padrões.
            var esquerda = new DockPanel();
            TextBlock titLista = Theme.Secao("Perfis");
            titLista.Margin = new Thickness(0, 0, 0, 4);
            titLista.ToolTip = "Cada perfil reúne o setup, os property sets e o mapeamento da exportação. Escolha um para ver tudo o que ele leva.";
            DockPanel.SetDock(titLista, Dock.Top);
            esquerda.Children.Add(titLista);
            var botoes = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            _duplicar = new Button { Content = "Duplicar...", Margin = new Thickness(0, 0, 6, 0), ToolTip = "Cria um perfil novo com uma cópia sua dos arquivos deste." };
            _duplicar.Click += (s, e) => Duplicar();
            _renomear = new Button { Content = "Renomear...", ToolTip = "Muda o nome do perfil (os seus arquivos dele mudam de nome juntos)." };
            _renomear.Click += (s, e) => Renomear();
            botoes.Children.Add(_duplicar);
            botoes.Children.Add(_renomear);
            DockPanel.SetDock(botoes, Dock.Bottom);
            esquerda.Children.Add(botoes);
            _lista = new ListBox { BorderBrush = Theme.Solid(Theme.CinzaClaro), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            ScrollViewer.SetHorizontalScrollBarVisibility(_lista, ScrollBarVisibility.Disabled);
            _lista.Resources[SystemColors.HighlightBrushKey] = Theme.Solid(Theme.LaranjaSelecao);
            _lista.Resources[SystemColors.HighlightTextBrushKey] = Theme.Solid(Theme.Preto);
            _lista.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = Theme.Solid(Theme.LaranjaSelecao);
            _lista.SelectionChanged += (s, e) => TrocouPadrao(e);
            esquerda.Children.Add(_lista);
            grade.Children.Add(esquerda);

            var divisor = new GridSplitter { Width = 12, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
            Grid.SetColumn(divisor, 1);
            grade.Children.Add(divisor);

            // Direita: tudo do padrão escolhido.
            var direita = new DockPanel();
            var cab = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            _selosPadrao = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            DockPanel.SetDock(_selosPadrao, Dock.Right);
            cab.Children.Add(_selosPadrao);
            var nomes = new StackPanel();
            _nomePadrao = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 16, TextTrimming = TextTrimming.CharacterEllipsis };
            _infoPadrao = Theme.Detalhe();
            nomes.Children.Add(_nomePadrao);
            nomes.Children.Add(_infoPadrao);
            cab.Children.Add(nomes);
            DockPanel.SetDock(cab, Dock.Top);
            direita.Children.Add(cab);

            _abas = new TabControl();
            foreach (ArquivosIfc.Tipo t in Tipos)
            {
                var titulo = new TextBlock();
                _titulosAba[t] = titulo;
                var detalhe = new ContentControl { Margin = new Thickness(4, 8, 4, 4) };
                _detalhes[t] = detalhe;
                _abas.Items.Add(new TabItem { Header = titulo, Content = detalhe, Tag = t, ToolTip = DicaAba(t) });
            }
            _abas.SelectionChanged += (s, e) =>
            {
                // Só a aba de property sets é montada ao aparecer (a conferência no modelo demora); as outras já estão prontas
                // e remontar o setup perderia o que não foi salvo.
                if (e.Source == _abas && !_carregando && _abas.SelectedItem is TabItem ti && ti.Tag is ArquivosIfc.Tipo t &&
                    t == ArquivosIfc.Tipo.PropertySets && _detalhes[t].Content == null)
                    MostrarDetalhe(t);
            };
            direita.Children.Add(_abas);
            Grid.SetColumn(direita, 2);
            grade.Children.Add(direita);

            _resumo = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), Text = "Ordem de uso: seu › Forma online › instalado" };
            var pasta = new Button { Content = "Abrir pasta dos seus arquivos" };
            pasta.Click += (s, e) => AbrirPasta(ClassesIfc.PastaUsuario);
            var fechar = new Button { Content = "Fechar", IsCancel = true, Style = Theme.PrimaryButton };
            fechar.Click += (s, e) => Close();
            Closing += (s, e) =>
            {
                if (_setup != null && _setup.Alterado && !PerguntarDescartar()) e.Cancel = true;
            };

            Theme.Apply(this, "Perfis de exportação IFC", "Escolha um perfil para ver tudo o que ele leva na exportação", corpo, Theme.Rodape(_resumo, pasta, fechar));

            _abas.SelectedIndex = Array.IndexOf(Tipos, aba);
            Recarregar(classe);
        }

        private static string NomeAba(ArquivosIfc.Tipo t) =>
            t == ArquivosIfc.Tipo.Setup ? "Configurações" : ArquivosIfc.Descricao(t);

        private static string DicaAba(ArquivosIfc.Tipo t) =>
            t == ArquivosIfc.Tipo.Setup ? "Setup da exportação IFC (.json): versão, fase e o que entra no IFC."
            : t == ArquivosIfc.Tipo.PropertySets ? "Property sets do usuário (.txt): as propriedades que vão para o IFC, conferidas no modelo aberto."
            : "Mapeamento de categorias (.txt), opcional: a classe IFC de cada categoria do Revit.";

        // ---------- lista de padrões ----------

        private ArquivosIfc.Tipo AbaAtual => _abas.SelectedItem is TabItem ti && ti.Tag is ArquivosIfc.Tipo t ? t : ArquivosIfc.Tipo.Setup;

        private ArquivosIfc.Arquivo Escolhido(ArquivosIfc.Tipo t) => _doPadrao.TryGetValue(t, out ArquivosIfc.Arquivo a) ? a : null;

        /// <summary>Relê as pastas e mantém o padrão escolhido (ou vai para o indicado).</summary>
        private void Recarregar(string classe = null)
        {
            string manter = classe ?? _classe;
            Dictionary<ArquivosIfc.Tipo, List<ArquivosIfc.Arquivo>> porTipo = Tipos.ToDictionary(t => t, ArquivosIfc.Listar);
            List<string> classes = porTipo.Values.SelectMany(l => l).Select(a => a.Classe)
                .Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase).ToList();
            _carregando = true;
            try
            {
                _lista.Items.Clear();
                foreach (string c in classes)
                {
                    Dictionary<ArquivosIfc.Tipo, ArquivosIfc.Arquivo> arquivos = Tipos.ToDictionary(t => t,
                        t => porTipo[t].FirstOrDefault(a => string.Equals(a.Classe, c, StringComparison.CurrentCultureIgnoreCase)));
                    _lista.Items.Add(ItemDaLista(c, arquivos));
                }
                _lista.SelectedItem = _lista.Items.Cast<ListBoxItem>().FirstOrDefault(i => string.Equals((string)i.Tag, manter, StringComparison.CurrentCultureIgnoreCase))
                                      ?? _lista.Items.Cast<ListBoxItem>().FirstOrDefault();
            }
            finally { _carregando = false; }
            _setup = null;
            MostrarPadrao();
        }

        /// <summary>Um padrão na lista: o nome e uma marca para cada parte (configurações, psets, mapa).</summary>
        private ListBoxItem ItemDaLista(string classe, Dictionary<ArquivosIfc.Tipo, ArquivosIfc.Arquivo> arquivos)
        {
            var p = new StackPanel { Margin = new Thickness(2, 3, 2, 3) };
            p.Children.Add(new TextBlock { Text = classe, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var marcas = new WrapPanel { Margin = new Thickness(0, 1, 0, 0) };
            var dica = new List<string> { classe };
            foreach (ArquivosIfc.Tipo t in Tipos)
            {
                ArquivosIfc.Arquivo a = arquivos[t];
                string curto = t == ArquivosIfc.Tipo.Setup ? "configurações" : t == ArquivosIfc.Tipo.PropertySets ? "psets" : "mapa";
                Color cor = a == null ? Color.FromRgb(0xB8, 0xB8, 0xB8) : Theme.Verde;
                string situacao = a == null ? (t == ArquivosIfc.Tipo.Mapeamento ? "nenhum (opcional)" : "nenhum") : $"{a.Nome} ({a.Origem})";
                if (a != null && t == ArquivosIfc.Tipo.Setup && !SetupAceito(a.Caminho)) { cor = Theme.LaranjaTexto; situacao += ", não é IFC4 nem 4.3"; }
                if (a != null && t == ArquivosIfc.Tipo.PropertySets && AusentesNoModelo(a) is int n && n != 0)
                {
                    cor = Theme.LaranjaTexto;
                    situacao += n < 0 ? ", com erro" : n == 1 ? ", 1 parâmetro não encontrado no modelo" : $", {n} parâmetros não encontrados no modelo";
                }
                marcas.Children.Add(new TextBlock
                {
                    Text = (a == null ? "○ " : "● ") + curto, FontSize = 11, Foreground = Theme.Solid(cor), Margin = new Thickness(0, 0, 8, 0),
                });
                dica.Add($"{NomeAba(t)}: {situacao}");
            }
            p.Children.Add(marcas);
            return new ListBoxItem { Content = p, Tag = classe, ToolTip = string.Join("\n", dica) };
        }

        /// <summary>Parâmetros do .txt não encontrados no modelo (só pelo nome, é rápido); null sem modelo, -1 com erro.</summary>
        private int? AusentesNoModelo(ArquivosIfc.Arquivo a)
        {
            if (_doc == null) return null;
            try { return ConferenciaPsets.Fazer(a.Caminho, Nomes(), null).Ausentes.Count; }
            catch { return -1; }
        }

        private static bool SetupAceito(string caminho)
        {
            try { return OpcoesSetup.Ler(caminho).EhVersaoAceita; } catch { return false; }
        }

        /// <summary>Só os nomes dos parâmetros do modelo (rápido), para as marcas da lista.</summary>
        private HashSet<string> Nomes()
        {
            if (_nomes == null && _doc != null) _nomes = _modelo?.Parametros ?? ModeloIfc.NomesDoDocumento(_doc);
            return _nomes;
        }

        private ModeloIfc Modelo()
        {
            if (_modelo == null && _doc != null)
            {
                Cursor antes = Mouse.OverrideCursor;
                Mouse.OverrideCursor = Cursors.Wait;
                try { _modelo = ModeloIfc.DoDocumento(_doc); _nomes = _modelo.Parametros; _conferidoEm = DateTime.Now; }
                finally { Mouse.OverrideCursor = antes; }
            }
            return _modelo;
        }

        private void TrocouPadrao(SelectionChangedEventArgs e)
        {
            if (_carregando) return;
            if (_setup != null && _setup.Alterado && !PerguntarDescartar())
            {
                // Cancelou: volta para o padrão que estava.
                _carregando = true;
                try { _lista.SelectedItem = e.RemovedItems.Count > 0 ? e.RemovedItems[0] : null; }
                finally { _carregando = false; }
                return;
            }
            _setup = null;
            MostrarPadrao();
        }

        /// <summary>Mostra o padrão escolhido na lista: cabeçalho e as três abas.</summary>
        private void MostrarPadrao()
        {
            _classe = (_lista.SelectedItem as ListBoxItem)?.Tag as string;
            _doPadrao.Clear();
            if (_classe != null)
                foreach (ArquivosIfc.Tipo t in Tipos) _doPadrao[t] = ArquivosIfc.Obter(t, _classe);

            _selosPadrao.Children.Clear();
            if (_classe == null)
            {
                _nomePadrao.Text = "Nenhum perfil";
                _infoPadrao.Text = "Arraste ou escolha os arquivos (.json e .txt) para criar um perfil.";
            }
            else
            {
                _nomePadrao.Text = _classe;
                List<ArquivosIfc.Arquivo> tem = _doPadrao.Values.Where(a => a != null).ToList();
                string origens = string.Join(", ", tem.Select(a => a.Origem).Distinct());
                DateTime alterado = tem.Select(a => File.GetLastWriteTime(a.Caminho)).DefaultIfEmpty().Max();
                _infoPadrao.Text = tem.Count == 0 ? "" : $"{origens} · alterado em {alterado:dd/MM/yyyy HH:mm}";
                _infoPadrao.ToolTip = string.Join("\n", tem.Select(a => a.Caminho));
                ArquivosIfc.Arquivo setup = Escolhido(ArquivosIfc.Tipo.Setup);
                if (setup == null) _selosPadrao.Children.Add(Theme.Selo("sem configurações", Theme.Estado.Aviso));
                else if (!SetupAceito(setup.Caminho)) _selosPadrao.Children.Add(Theme.Selo("a exportação MetroSP exige " + OpcoesSetup.VersoesAceitas, Theme.Estado.Aviso));
                else _selosPadrao.Children.Add(Theme.Selo("pronto para exportar", Theme.Estado.Ok));
                ArquivosIfc.Arquivo psets = Escolhido(ArquivosIfc.Tipo.PropertySets);
                if (psets != null && AusentesNoModelo(psets) is int n && n > 0)
                {
                    Border s = Theme.Selo(n == 1 ? "1 parâmetro não encontrado" : $"{n} parâmetros não encontrados", Theme.Estado.Aviso);
                    s.Margin = new Thickness(6, 0, 0, 0);
                    s.ToolTip = "Parâmetros do property set que não existem no modelo aberto (ficam vazios no IFC). Veja a aba Property sets.";
                    _selosPadrao.Children.Add(s);
                }
            }
            bool todosSeus = _doPadrao.Values.Any(a => a != null) && _doPadrao.Values.All(a => a == null || (a.DoUsuario && !a.TemPadrao));
            _duplicar.IsEnabled = _doPadrao.Values.Any(a => a != null);
            _renomear.IsEnabled = todosSeus;
            _renomear.ToolTip = todosSeus ? "Muda o nome do perfil (os seus arquivos dele mudam de nome juntos)."
                : "Só dá para renomear um perfil que é todo seu. Use Duplicar para criar uma cópia sua com outro nome.";
            ToolTipService.SetShowOnDisabled(_renomear, true);

            foreach (ArquivosIfc.Tipo t in Tipos)
            {
                ArquivosIfc.Arquivo a = Escolhido(t);
                _titulosAba[t].Inlines.Clear();
                _titulosAba[t].Inlines.Add(new System.Windows.Documents.Run(NomeAba(t)));
                if (a == null)
                    _titulosAba[t].Inlines.Add(new System.Windows.Documents.Run("  nenhum") { Foreground = Theme.Solid(Theme.Cinza), FontSize = 11 });
                MostrarDetalhe(t);
            }
        }

        private void MostrarDetalhe(ArquivosIfc.Tipo t)
        {
            ArquivosIfc.Arquivo a = Escolhido(t);
            if (t == ArquivosIfc.Tipo.Setup) _setup = null;
            if (a == null)
            {
                string texto = _classe == null
                    ? "Escolha um perfil na lista."
                    : $"O perfil {_classe} não tem {NomeAba(t).ToLowerInvariant()}" + (t == ArquivosIfc.Tipo.Mapeamento ? " (é opcional)." : ".") +
                      $"\nArraste um {ArquivosIfc.Extensao(t)} para esta janela ou escolha os arquivos, e indique o perfil {_classe}.";
                _detalhes[t].Content = new TextBlock { Text = texto, Foreground = Theme.Solid(Theme.Cinza), TextWrapping = TextWrapping.Wrap };
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

        /// <summary>Cria um padrão novo com uma cópia sua de cada arquivo do padrão escolhido.</summary>
        private void Duplicar()
        {
            if (_classe == null || (_setup != null && _setup.Alterado && !PerguntarDescartar())) return;
            string nova = PedirTexto("Duplicar perfil", $"Nome do novo perfil (cópia de {_classe}):", _classe + " - cópia");
            if (nova == null) return;
            if (!ArquivosIfc.NomeValido(nova)) { MessageBox.Show(this, "Nome de perfil inválido.", Title); return; }
            if (Tipos.Any(t => ArquivosIfc.Obter(t, nova) != null) &&
                MessageBox.Show(this, $"Já existe o perfil {nova}. Substituir os arquivos dele? (os seus atuais ficam em Anteriores)", Title,
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            List<ArquivosIfc.Arquivo> arquivos = _doPadrao.Values.Where(a => a != null).ToList();
            Executar(() => { foreach (ArquivosIfc.Arquivo a in arquivos) ArquivosIfc.Gravar(a.Caminho, a.Tipo, nova); }, nova.Trim());
            _resumo.Text = $"Perfil {nova.Trim()} criado a partir de {arquivos[0].Classe}.";
        }

        /// <summary>Muda o nome do padrão (só quando todos os arquivos dele são seus).</summary>
        private void Renomear()
        {
            if (_classe == null || (_setup != null && _setup.Alterado && !PerguntarDescartar())) return;
            string antigo = _classe;
            string nova = PedirTexto("Renomear perfil", $"Novo nome para o perfil {antigo}:", antigo);
            if (nova == null || string.Equals(nova, antigo, StringComparison.CurrentCulture)) return;
            if (!ArquivosIfc.NomeValido(nova)) { MessageBox.Show(this, "Nome de perfil inválido.", Title); return; }
            if (!string.Equals(nova, antigo, StringComparison.CurrentCultureIgnoreCase) && Tipos.Any(t => ArquivosIfc.Obter(t, nova) != null) &&
                MessageBox.Show(this, $"Já existe o perfil {nova}. Substituir os arquivos dele? (os seus atuais ficam em Anteriores)", Title,
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            List<ArquivosIfc.Arquivo> arquivos = _doPadrao.Values.Where(a => a != null).ToList();
            Executar(() => { foreach (ArquivosIfc.Arquivo a in arquivos) ArquivosIfc.MudarClasse(a.Tipo, antigo, nova); }, nova.Trim());
            _resumo.Text = $"Perfil {antigo} renomeado para {nova.Trim()}.";
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
            nome.Children.Add(new TextBlock { Text = a.Nome, FontWeight = FontWeights.SemiBold });
            string origem = a.Origem == ArquivosIfc.Seu
                ? (a.TemPadrao ? "seu (no lugar do original)" : "seu")
                : a.Origem;
            nome.Children.Add(Theme.Detalhe($"{origem} · alterado em {File.GetLastWriteTime(a.Caminho):dd/MM/yyyy HH:mm}"));
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
                var apagar = new Button { Content = a.TemPadrao ? "Voltar ao original" : "Apagar" };
                apagar.ToolTip = a.TemPadrao ? "Apaga a sua cópia; o perfil volta a usar o arquivo do Forma online ou instalado." : "Apaga o arquivo (ele fica guardado em Anteriores).";
                apagar.Click += (s, e) =>
                {
                    string pergunta = a.TemPadrao ? $"Apagar a sua cópia de {a.Nome} e voltar ao original?" : $"Apagar {a.Nome}?";
                    if (MessageBox.Show(this, pergunta + "\n\nA versão atual fica guardada em " + ArquivosIfc.PastaAnteriores, Title,
                            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    Executar(() => ArquivosIfc.ApagarDoUsuario(a.Tipo, a.Classe), a.Classe);
                };
                Add(apagar);
                var classe = new Button { Content = "Mover para outro perfil...", ToolTip = "Passa este arquivo para outro perfil (o arquivo muda de nome)." };
                classe.Click += (s, e) =>
                {
                    string nova = PedirTexto("Mover para outro perfil", $"Perfil para {a.Nome}:", a.Classe);
                    if (nova == null || string.Equals(nova, a.Classe, StringComparison.CurrentCulture)) return;
                    if (!ArquivosIfc.NomeValido(nova)) { MessageBox.Show(this, "Nome de perfil inválido.", Title); return; }
                    if (File.Exists(ArquivosIfc.Destino(a.Tipo, nova)) &&
                        MessageBox.Show(this, $"Você já tem {ArquivosIfc.Descricao(a.Tipo).ToLowerInvariant()} do perfil {nova}. Substituir? (o atual fica em Anteriores)", Title,
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
            // Opção que o Exportar IFC sempre força (IfcExportSetup.Forcadas): mostra o valor do .json, sem editar.
            UIElement Fixa(string texto, bool valorNoArquivo, bool naExportacao)
            {
                var p = new StackPanel { Orientation = Orientation.Horizontal };
                p.Children.Add(new CheckBox { Content = texto, IsChecked = valorNoArquivo, IsEnabled = false, VerticalAlignment = VerticalAlignment.Center });
                bool diferente = valorNoArquivo != naExportacao;
                p.Children.Add(new TextBlock
                {
                    Text = "🔒 " + (naExportacao ? "sempre exporta" : "nunca exporta"),
                    Margin = new Thickness(10, 0, 0, 0), FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Theme.Solid(diferente ? Theme.LaranjaTexto : Theme.Cinza),
                });
                p.ToolTip = (diferente ? $"O arquivo diz {(valorNoArquivo ? "sim" : "não")}, mas o " : "O ") +
                            $"Exportar IFC do MetroSP {(naExportacao ? "sempre exporta" : "nunca exporta")} esta opção, seja qual for o valor no arquivo.";
                ToolTipService.SetShowOnDisabled(p, true);
                return p;
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
            Linha(null, Fixa("Exportar quantidades base", o.Quantidades, true));
            Linha(null, Caixa("Gravar o GUID IFC no elemento", o.GravaGuid, v => o.GravaGuid = v,
                "Grava o IfcGUID nos elementos do Revit ao exportar (o modelo fica alterado)."));

            Titulo("Propriedades");
            Linha(null, Fixa("Psets do IFC padrão (Pset_WallCommon...)", o.PsetsComuns, true));
            Linha(null, Fixa("Psets internos do Revit", o.PsetsDoRevit, false));
            Linha(null, Fixa("Psets de material", o.PsetsDeMaterial, true));

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
            verificar.Click += (s, e) => { ModeloIfc.Esquecer(_doc); _modelo = null; _nomes = null; Recarregar(a.Classe); };
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
            _resumo.Text = editor.Restaurou ? $"{a.Classe}: voltou ao original." : "Salvo em " + editor.ArquivoSalvo;
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

        /// <summary>Abre a conferência "Trazer arquivos" com estes arquivos assim que a janela aparece (vindo da janela Padrões).</summary>
        public void TrazerAoAbrir(IEnumerable<string> arquivos)
        {
            List<string> lista = arquivos.ToList();
            EventHandler ao = null;
            ao = (s, e) =>
            {
                ContentRendered -= ao;
                Dispatcher.BeginInvoke(new Action(() => Trazer(lista)));
            };
            ContentRendered += ao;
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
            string[] titulos = { "Arquivo", "Perfil", "Tipo", "Situação" };
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
                    else if (!ArquivosIfc.NomeValido(classe)) { texto = "indique o perfil"; estado = Theme.Estado.Aviso; }
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
            Theme.Apply(janela, "Trazer arquivos", "Confira o perfil e o tipo de cada arquivo antes de gravar",
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
