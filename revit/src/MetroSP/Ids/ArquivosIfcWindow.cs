using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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
    /// Os perfis de exportação IFC. Cada perfil tem 1 setup (.json), 1 property sets (.txt) e 1 mapeamento (.txt).
    /// Escolhe-se o perfil na lista e aparecem os três arquivos dele, um por linha (abrir, trocar, voltar ao original),
    /// e logo abaixo as opções do setup, que se gravam com "Salvar perfil". Um perfil pode ser criado, excluído,
    /// exportado (.zip com os três arquivos) e importado.
    /// </summary>
    public class ArquivosIfcWindow : Window
    {
        private static readonly ArquivosIfc.Tipo[] Tipos = { ArquivosIfc.Tipo.Setup, ArquivosIfc.Tipo.PropertySets, ArquivosIfc.Tipo.Mapeamento };

        private readonly DB.Document _doc;
        private ModeloIfc _modelo;
        private HashSet<string> _nomes;
        private readonly ListBox _lista;
        private readonly Dictionary<ArquivosIfc.Tipo, ArquivosIfc.Arquivo> _doPadrao = new Dictionary<ArquivosIfc.Tipo, ArquivosIfc.Arquivo>();
        /// <summary>Data de gravação de cada arquivo do perfil, para recarregar quando ele for editado fora (Bloco de notas).</summary>
        private readonly Dictionary<string, DateTime> _datas = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly TextBlock _nomePadrao, _infoPadrao;
        private readonly StackPanel _selosPadrao;
        private readonly StackPanel _linhas;
        private readonly TextBlock _tituloSetup;
        private readonly ContentControl _painelSetup;
        private readonly Button _exportar, _duplicar, _renomear, _excluir, _salvar, _descartar;
        private readonly TextBlock _estado;
        private readonly TextBlock _resumo;
        private OpcoesSetup _setup;
        private bool _carregando;
        private string _classe;

        /// <summary>true se algum arquivo foi gravado, apagado ou trocado de perfil (a janela de exportação recarrega).</summary>
        public bool Mudou { get; private set; }

        /// <param name="aba">Mantido por compatibilidade (a janela não tem mais abas).</param>
        public ArquivosIfcWindow(DB.Document doc, string classe = null, ArquivosIfc.Tipo aba = ArquivosIfc.Tipo.Setup)
        {
            _doc = doc;
            Title = "MetroSP - Perfis de exportação IFC";
            Width = 980;
            Height = 700;
            MinWidth = 760;
            MinHeight = 500;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            AllowDrop = true;
            Drop += (s, e) =>
            {
                if (e.Handled) return;
                if (e.Data.GetData(DataFormats.FileDrop) is string[] arquivos) SoltouNaJanela(arquivos);
            };
            DragOver += (s, e) =>
            {
                e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            };
            // Voltando do Bloco de notas: se um arquivo do perfil mudou, relê.
            Activated += (s, e) => ConferirEditadosFora();

            var grade = new Grid();
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250), MinWidth = 180 });
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 440 });

            // ---- esquerda: a lista de perfis
            var esquerda = new DockPanel();
            TextBlock titLista = Theme.Secao("Perfis");
            titLista.Margin = new Thickness(0, 0, 0, 4);
            titLista.ToolTip = "Cada perfil tem 1 configuração (.json), 1 property sets (.txt) e 1 mapeamento (.txt, opcional).\nEscolha um para ver e trocar os arquivos dele.";
            DockPanel.SetDock(titLista, Dock.Top);
            esquerda.Children.Add(titLista);
            var botoes = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            Button Botao(string texto, string dica, Action acao)
            {
                var b = new Button { Content = texto, Margin = new Thickness(0, 0, 6, 6), ToolTip = dica };
                b.Click += (s, e) => acao();
                ToolTipService.SetShowOnDisabled(b, true);
                botoes.Children.Add(b);
                return b;
            }
            Botao("Novo...", "Cria um perfil: dê o nome e escolha o .json e os .txt dele.", Novo);
            _duplicar = Botao("Duplicar...", "Cria um perfil novo com uma cópia sua dos arquivos deste.", Duplicar);
            _renomear = Botao("Renomear...", "Muda o nome do perfil (os arquivos dele mudam de nome juntos).", Renomear);
            _excluir = Botao("Excluir...", "Exclui o perfil. Os arquivos ficam guardados em Anteriores.", Excluir);
            Botao("Importar...", "Traz um perfil exportado (.zip) ou os arquivos .json e .txt soltos.\nTambém dá para arrastar o .zip para esta janela.", Importar);
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

            // ---- direita: o perfil escolhido
            var direita = new DockPanel();
            var cab = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            _exportar = new Button { Content = "Exportar perfil...", Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top,
                ToolTip = "Salva os 3 arquivos do perfil num .zip, para mandar a outra pessoa.\nQuem recebe usa Importar... (ou arrasta o .zip nesta janela)." };
            _exportar.Click += (s, e) => Exportar();
            ToolTipService.SetShowOnDisabled(_exportar, true);
            DockPanel.SetDock(_exportar, Dock.Right);
            cab.Children.Add(_exportar);
            _selosPadrao = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 0, 0) };
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

            TextBlock titArquivos = Theme.Secao("Arquivos do perfil");
            titArquivos.Margin = new Thickness(0, 0, 0, 4);
            titArquivos.ToolTip = "Um arquivo de cada tipo por perfil. Para trocar, use Trocar... ou arraste o arquivo para a linha dele.\nO arquivo anterior fica guardado em Anteriores.";
            DockPanel.SetDock(titArquivos, Dock.Top);
            direita.Children.Add(titArquivos);
            _linhas = new StackPanel();
            var caixa = new Border { BorderBrush = Theme.Solid(Theme.CinzaClaro), BorderThickness = new Thickness(1), Child = _linhas, Margin = new Thickness(0, 0, 0, 12) };
            DockPanel.SetDock(caixa, Dock.Top);
            direita.Children.Add(caixa);

            // Rodapé do perfil: um botão só grava tudo.
            var rodapePerfil = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            _salvar = new Button { Content = "Salvar perfil", Style = Theme.PrimaryButton, IsEnabled = false, Margin = new Thickness(8, 0, 0, 0),
                ToolTip = "Grava as configurações no .json do perfil (uma cópia sua, se o arquivo for do Forma ou instalado)." };
            _salvar.Click += (s, e) => SalvarSetup();
            _descartar = new Button { Content = "Descartar", IsEnabled = false, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Volta as configurações para o que está no arquivo." };
            _descartar.Click += (s, e) => { _setup = null; MostrarSetup(); _resumo.Text = "Alterações descartadas."; };
            var bs = new StackPanel { Orientation = Orientation.Horizontal };
            bs.Children.Add(_descartar);
            bs.Children.Add(_salvar);
            DockPanel.SetDock(bs, Dock.Right);
            rodapePerfil.Children.Add(bs);
            _estado = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = Theme.Solid(Theme.LaranjaTexto) };
            rodapePerfil.Children.Add(_estado);
            DockPanel.SetDock(rodapePerfil, Dock.Bottom);
            direita.Children.Add(rodapePerfil);

            var setup = new DockPanel { Margin = new Thickness(0, 0, 0, 0) };
            _tituloSetup = Theme.Secao("Configurações");
            _tituloSetup.Margin = new Thickness(0, 0, 0, 4);
            _tituloSetup.ToolTip = "As opções do exportador IFC gravadas no .json do perfil.";
            DockPanel.SetDock(_tituloSetup, Dock.Top);
            setup.Children.Add(_tituloSetup);
            _painelSetup = new ContentControl();
            setup.Children.Add(new ScrollViewer { Content = _painelSetup, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            direita.Children.Add(setup);

            var moldura = new Border { BorderBrush = Theme.Solid(Theme.CinzaClaro), BorderThickness = new Thickness(1), Padding = new Thickness(14, 12, 14, 12), Child = direita };
            Grid.SetColumn(moldura, 2);
            grade.Children.Add(moldura);

            _resumo = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), Text = "Ordem de uso: seu › Forma online › instalado",
                ToolTip = "Se houver mais de um arquivo com o mesmo nome, vale o seu; depois o do Forma online; por último o instalado." };
            var pasta = new Button { Content = "Abrir pasta dos seus arquivos" };
            pasta.Click += (s, e) => AbrirPasta(ClassesIfc.PastaUsuario);
            var fechar = new Button { Content = "Fechar", IsCancel = true, Style = Theme.PrimaryButton };
            fechar.Click += (s, e) => Close();
            Closing += (s, e) =>
            {
                if (_setup != null && _setup.Alterado && !PerguntarDescartar()) e.Cancel = true;
            };

            Theme.Apply(this, "Perfis de exportação IFC", "Cada perfil tem 1 configuração (.json), 1 property sets (.txt) e 1 mapeamento (.txt)", grade, Theme.Rodape(_resumo, pasta, fechar));

            Recarregar(classe);
        }

        private static string NomeTipo(ArquivosIfc.Tipo t) =>
            t == ArquivosIfc.Tipo.Setup ? "Configurações" : t == ArquivosIfc.Tipo.PropertySets ? "Property sets" : "Mapeamento";

        private static string DicaTipo(ArquivosIfc.Tipo t) =>
            t == ArquivosIfc.Tipo.Setup ? "Setup do exportador IFC (.json): versão, fase e o que entra no IFC. Obrigatório."
            : t == ArquivosIfc.Tipo.PropertySets ? "Property sets do usuário (.txt): as propriedades que vão para o IFC e o parâmetro do Revit de cada uma."
            : "Mapeamento de categorias (.txt): a classe IFC de cada categoria do Revit. Opcional.";

        private static string Filtro(ArquivosIfc.Tipo t) =>
            t == ArquivosIfc.Tipo.Setup ? "Setup IFC (*.json)|*.json" : "Texto (*.txt)|*.txt";

        // ---------- lista de perfis ----------

        private ArquivosIfc.Arquivo Escolhido(ArquivosIfc.Tipo t) => _doPadrao.TryGetValue(t, out ArquivosIfc.Arquivo a) ? a : null;

        /// <summary>Relê as pastas e mantém o perfil escolhido (ou vai para o indicado).</summary>
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

        /// <summary>Um perfil na lista: o nome e uma marca para cada arquivo (json, psets, mapa).</summary>
        private ListBoxItem ItemDaLista(string classe, Dictionary<ArquivosIfc.Tipo, ArquivosIfc.Arquivo> arquivos)
        {
            var p = new StackPanel { Margin = new Thickness(2, 3, 2, 3) };
            p.Children.Add(new TextBlock { Text = classe, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var marcas = new WrapPanel { Margin = new Thickness(0, 1, 0, 0) };
            var dica = new List<string> { classe };
            foreach (ArquivosIfc.Tipo t in Tipos)
            {
                ArquivosIfc.Arquivo a = arquivos[t];
                string curto = t == ArquivosIfc.Tipo.Setup ? "json" : t == ArquivosIfc.Tipo.PropertySets ? "psets" : "mapa";
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
                dica.Add($"{NomeTipo(t)}: {situacao}");
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
                try { _modelo = ModeloIfc.DoDocumento(_doc); _nomes = _modelo.Parametros; }
                finally { Mouse.OverrideCursor = antes; }
            }
            return _modelo;
        }

        private void TrocouPadrao(SelectionChangedEventArgs e)
        {
            if (_carregando) return;
            if (_setup != null && _setup.Alterado && !PerguntarDescartar())
            {
                // Cancelou: volta para o perfil que estava.
                _carregando = true;
                try { _lista.SelectedItem = e.RemovedItems.Count > 0 ? e.RemovedItems[0] : null; }
                finally { _carregando = false; }
                return;
            }
            _setup = null;
            MostrarPadrao();
        }

        /// <summary>Todos os arquivos do perfil são do usuário (sem original do Forma ou instalado por baixo).</summary>
        private bool TodosSeus => _doPadrao.Values.Any(a => a != null) && _doPadrao.Values.All(a => a == null || (a.DoUsuario && !a.TemPadrao));

        /// <summary>Mostra o perfil escolhido: cabeçalho, as três linhas de arquivo e as configurações.</summary>
        private void MostrarPadrao()
        {
            _classe = (_lista.SelectedItem as ListBoxItem)?.Tag as string;
            _doPadrao.Clear();
            _datas.Clear();
            if (_classe != null)
                foreach (ArquivosIfc.Tipo t in Tipos)
                {
                    ArquivosIfc.Arquivo a = ArquivosIfc.Obter(t, _classe);
                    _doPadrao[t] = a;
                    if (a != null) _datas[a.Caminho] = File.GetLastWriteTime(a.Caminho);
                }

            _selosPadrao.Children.Clear();
            if (_classe == null)
            {
                _nomePadrao.Text = "Nenhum perfil";
                _infoPadrao.Text = "Use Novo... ou Importar... para criar um perfil.";
                _infoPadrao.ToolTip = null;
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
            }

            bool algum = _doPadrao.Values.Any(a => a != null);
            bool todosSeus = TodosSeus;
            _exportar.IsEnabled = algum;
            _duplicar.IsEnabled = algum;
            _renomear.IsEnabled = todosSeus;
            _renomear.ToolTip = todosSeus ? "Muda o nome do perfil (os arquivos dele mudam de nome juntos)."
                : "Só dá para renomear um perfil que é todo seu. Use Duplicar para criar uma cópia sua com outro nome.";
            _excluir.IsEnabled = todosSeus;
            _excluir.ToolTip = todosSeus ? "Exclui o perfil. Os arquivos ficam guardados em Anteriores."
                : algum ? "Este perfil vem do Forma online ou da instalação e não pode ser excluído aqui. Nos seus arquivos dele, use ⋯ › Voltar ao original."
                : "Escolha um perfil.";

            _linhas.Children.Clear();
            foreach (ArquivosIfc.Tipo t in Tipos) _linhas.Children.Add(LinhaArquivo(t, Escolhido(t), t == Tipos[0]));
            MostrarSetup();
        }

        /// <summary>Uma linha da lista de arquivos do perfil: tipo, arquivo ligado, situação e ações. Aceita soltar um arquivo.</summary>
        private UIElement LinhaArquivo(ArquivosIfc.Tipo t, ArquivosIfc.Arquivo a, bool primeira)
        {
            var g = new Grid { Background = Brushes.White, AllowDrop = _classe != null, ToolTip = null };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var borda = new Border
            {
                BorderBrush = Theme.Solid(Theme.CinzaClaro), BorderThickness = new Thickness(0, primeira ? 0 : 1, 0, 0),
                Padding = new Thickness(10, 7, 10, 7), Child = g,
            };

            // Etiqueta da extensão (laranja quando há arquivo).
            var ext = new Border
            {
                BorderBrush = Theme.Solid(a != null ? Theme.Laranja : Color.FromRgb(0xBB, 0xBB, 0xBB)), BorderThickness = new Thickness(1),
                Padding = new Thickness(0, 1, 0, 1), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = t == ArquivosIfc.Tipo.Setup ? "JSON" : "TXT", FontSize = 11, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = Theme.Solid(a != null ? Theme.LaranjaTexto : Color.FromRgb(0x66, 0x66, 0x66)),
                },
            };
            g.Children.Add(ext);

            var tipo = new StackPanel { VerticalAlignment = VerticalAlignment.Center, ToolTip = DicaTipo(t) };
            tipo.Children.Add(new TextBlock { Text = NomeTipo(t), FontWeight = FontWeights.SemiBold });
            if (t == ArquivosIfc.Tipo.Mapeamento) tipo.Children.Add(Theme.Detalhe("opcional"));
            Grid.SetColumn(tipo, 1);
            g.Children.Add(tipo);

            var arquivo = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            if (a == null)
            {
                arquivo.Children.Add(new TextBlock
                {
                    Text = _classe == null ? "—" : $"nenhum: arraste um {ArquivosIfc.Extensao(t)} aqui",
                    FontStyle = FontStyles.Italic, Foreground = Theme.Solid(Theme.Cinza), TextTrimming = TextTrimming.CharacterEllipsis,
                });
            }
            else
            {
                var linha1 = new WrapPanel();
                var nome = new TextBlock
                {
                    Text = a.Nome, TextDecorations = TextDecorations.Underline, TextTrimming = TextTrimming.CharacterEllipsis, Cursor = Cursors.Hand,
                    ToolTip = a.Caminho + "\n(clique para abrir a pasta)",
                };
                nome.MouseLeftButtonUp += (s, e) => AbrirPasta(Path.GetDirectoryName(a.Caminho), a.Caminho);
                linha1.Children.Add(nome);
                string detalhe = a.Origem == ArquivosIfc.Seu ? (a.TemPadrao ? "seu (no lugar do original)" : "seu") : a.Origem;
                try
                {
                    if (t == ArquivosIfc.Tipo.Setup && !SetupAceito(a.Caminho))
                        linha1.Children.Add(Alerta("não é IFC4 nem 4.3", "A exportação MetroSP exige " + OpcoesSetup.VersoesAceitas + ". Mude a versão nas configurações abaixo."));
                    if (t == ArquivosIfc.Tipo.PropertySets)
                    {
                        ConferenciaPsets c = ConferenciaPsets.Fazer(a.Caminho, Nomes(), null);
                        detalhe += c.Psets.Count == 1 ? " · 1 pset" : $" · {c.Psets.Count} psets";
                        if (_doc != null && c.Ausentes.Count > 0)
                            linha1.Children.Add(Alerta(c.Ausentes.Count == 1 ? "1 não encontrado" : $"{c.Ausentes.Count} não encontrados",
                                "Parâmetros que não existem no modelo aberto (ficam vazios no IFC):\n" + string.Join("\n", c.Ausentes.Take(20)) +
                                (c.Ausentes.Count > 20 ? "\n..." : "") + "\n\nUse Editar... para conferir e corrigir."));
                        if (c.Avisos.Count > 0)
                            linha1.Children.Add(Alerta(c.Avisos.Count == 1 ? "1 problema de formato" : $"{c.Avisos.Count} problemas de formato", string.Join("\n", c.Avisos.Take(10))));
                    }
                }
                catch (Exception ex) { linha1.Children.Add(Alerta("não foi possível ler", ex.Message)); }
                arquivo.Children.Add(linha1);
                arquivo.Children.Add(Theme.Detalhe(detalhe));
            }
            Grid.SetColumn(arquivo, 2);
            g.Children.Add(arquivo);

            var acoes = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Button Acao(string texto, string dica, Action acao, bool ativo = true)
            {
                var b = new Button { Content = texto, Margin = new Thickness(6, 0, 0, 0), ToolTip = dica, IsEnabled = ativo && _classe != null };
                b.Click += (s, e) => acao();
                ToolTipService.SetShowOnDisabled(b, true);
                acoes.Children.Add(b);
                return b;
            }
            if (a != null)
            {
                if (t == ArquivosIfc.Tipo.PropertySets)
                    Acao("Editar...", "Abre o editor de property sets, com a conferência no modelo aberto.", () => EditarPsets(a));
                else
                    Acao("Abrir", "Abre o arquivo no Bloco de notas. Ao voltar, a janela relê o que mudou.", () => AbrirNoBloco(a.Caminho));
                Acao("Trocar...", $"Liga outro {ArquivosIfc.Extensao(t)} a este perfil. O atual fica guardado em Anteriores.", () => EscolherPara(t));
            }
            else
                Acao("Escolher...", $"Liga um {ArquivosIfc.Extensao(t)} a este perfil.", () => EscolherPara(t));
            Button mais = Acao("⋯", "Mais: voltar ao original, salvar uma cópia, abrir pasta.", () => { }, a != null);
            mais.MinWidth = 0;
            mais.Padding = new Thickness(10, 3, 10, 3);
            if (a != null)
            {
                mais.ContextMenu = MenuDoArquivo(a);
                mais.Click += (s, e) => { mais.ContextMenu.PlacementTarget = mais; mais.ContextMenu.IsOpen = true; };
            }
            Grid.SetColumn(acoes, 3);
            g.Children.Add(acoes);

            // Soltar um arquivo na linha liga ele a este perfil, neste tipo.
            g.DragOver += (s, e) =>
            {
                bool ok = _classe != null && e.Data.GetData(DataFormats.FileDrop) is string[] f && f.Length == 1 &&
                          Path.GetExtension(f[0]).Equals(ArquivosIfc.Extensao(t), StringComparison.OrdinalIgnoreCase);
                e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
                g.Background = ok ? Theme.Solid(Theme.LaranjaSelecao) : Brushes.White;
                e.Handled = true;
            };
            g.DragLeave += (s, e) => g.Background = Brushes.White;
            g.Drop += (s, e) =>
            {
                g.Background = Brushes.White;
                e.Handled = true;
                if (e.Data.GetData(DataFormats.FileDrop) is string[] f && f.Length == 1) Dispatcher.BeginInvoke(new Action(() => Ligar(t, f[0])));
            };
            return borda;
        }

        private static TextBlock Alerta(string texto, string dica) => new TextBlock
        {
            Text = "● " + texto, FontSize = 11, Foreground = Theme.Solid(Theme.LaranjaTexto), Margin = new Thickness(8, 1, 0, 0), ToolTip = dica,
            VerticalAlignment = VerticalAlignment.Center,
        };

        /// <summary>O menu ⋯ de um arquivo: voltar ao original ou tirar do perfil, salvar uma cópia, abrir a pasta.</summary>
        private ContextMenu MenuDoArquivo(ArquivosIfc.Arquivo a)
        {
            var m = new ContextMenu();
            MenuItem Item(string texto, string dica, Action acao)
            {
                var i = new MenuItem { Header = texto, ToolTip = dica };
                i.Click += (s, e) => acao();
                m.Items.Add(i);
                return i;
            }
            if (a.DoUsuario)
            {
                if (a.TemPadrao)
                    Item("Voltar ao original", "Apaga a sua cópia; o perfil volta a usar o arquivo do Forma online ou instalado.", () => TirarDoPerfil(a));
                else if (a.Tipo != ArquivosIfc.Tipo.Setup)
                    Item("Tirar do perfil", "O perfil fica sem este arquivo (ele fica guardado em Anteriores).", () => TirarDoPerfil(a));
            }
            Item("Salvar uma cópia...", "Grava uma cópia deste arquivo onde você escolher.", () => SalvarCopia(a));
            Item("Abrir pasta", a.Caminho, () => AbrirPasta(Path.GetDirectoryName(a.Caminho), a.Caminho));
            return m;
        }

        private void TirarDoPerfil(ArquivosIfc.Arquivo a)
        {
            if (a.Tipo == ArquivosIfc.Tipo.Setup && !PerguntarDescartar()) return;
            string pergunta = a.TemPadrao ? $"Apagar a sua cópia de {a.Nome} e voltar ao original?" : $"Tirar {a.Nome} do perfil {a.Classe}?";
            if (MessageBox.Show(this, pergunta + "\n\nA versão atual fica guardada em " + ArquivosIfc.PastaAnteriores, Title,
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            Executar(() => ArquivosIfc.ApagarDoUsuario(a.Tipo, a.Classe), a.Classe);
            _resumo.Text = a.TemPadrao ? $"{a.Nome}: voltou ao original." : $"{a.Nome} saiu do perfil.";
        }

        private void SalvarCopia(ArquivosIfc.Arquivo a)
        {
            var d = new Microsoft.Win32.SaveFileDialog { FileName = a.Nome, Filter = Filtro(a.Tipo), Title = "Salvar uma cópia de " + a.Nome };
            if (d.ShowDialog(this) != true) return;
            try { File.Copy(a.Caminho, d.FileName, true); _resumo.Text = "Cópia salva em " + d.FileName; }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível copiar: " + ex.Message, Title); }
        }

        private void AbrirNoBloco(string caminho)
        {
            try { Process.Start(new ProcessStartInfo("notepad.exe", "\"" + caminho + "\"") { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Title); }
        }

        /// <summary>Se algum arquivo do perfil foi gravado fora (Bloco de notas), relê o perfil (sem perder alterações não salvas).</summary>
        private void ConferirEditadosFora()
        {
            if (_carregando || _classe == null || (_setup != null && _setup.Alterado)) return;
            bool mudou = _datas.Any(k => { try { return File.Exists(k.Key) && File.GetLastWriteTime(k.Key) != k.Value; } catch { return false; } });
            if (!mudou) return;
            Mudou = true;
            Recarregar(_classe);
            _resumo.Text = "Arquivo alterado fora do MetroSP: relido.";
        }

        // ---------- ligar arquivos ao perfil ----------

        private void EscolherPara(ArquivosIfc.Tipo t)
        {
            if (_classe == null) return;
            var d = new Microsoft.Win32.OpenFileDialog { Filter = Filtro(t), Title = $"{NomeTipo(t)} do perfil {_classe}" };
            if (d.ShowDialog(this) == true) Ligar(t, d.FileName);
        }

        /// <summary>Liga o arquivo ao perfil escolhido, no tipo indicado (vira a cópia sua dele; a anterior fica em Anteriores).</summary>
        private bool Ligar(ArquivosIfc.Tipo t, string arquivo, bool perguntar = true)
        {
            if (_classe == null || !File.Exists(arquivo)) return false;
            if (!Path.GetExtension(arquivo).Equals(ArquivosIfc.Extensao(t), StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, $"{NomeTipo(t)} precisa ser um arquivo {ArquivosIfc.Extensao(t)}.", Title);
                return false;
            }
            ArquivosIfc.Tipo? detectado = ArquivosIfc.Detectar(arquivo, out string motivo);
            if (detectado != t)
            {
                string aviso = detectado == null ? $"{Path.GetFileName(arquivo)} {motivo}." : $"{Path.GetFileName(arquivo)} parece ser de {NomeTipo(detectado.Value).ToLowerInvariant()}.";
                if (MessageBox.Show(this, aviso + $"\n\nLigar como {NomeTipo(t).ToLowerInvariant()} do perfil {_classe} assim mesmo?", Title,
                        MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return false;
            }
            if (t == ArquivosIfc.Tipo.Setup && !PerguntarDescartar()) return false;
            ArquivosIfc.Arquivo atual = Escolhido(t);
            if (perguntar && atual != null && atual.DoUsuario &&
                MessageBox.Show(this, $"Trocar {atual.Nome} por {Path.GetFileName(arquivo)} no perfil {_classe}?\n\nO atual fica guardado em {ArquivosIfc.PastaAnteriores}", Title,
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return false;
            string classe = _classe;
            Executar(() => ArquivosIfc.Gravar(arquivo, t, classe), classe);
            _resumo.Text = $"{Path.GetFileName(arquivo)} ligado ao perfil {classe} ({NomeTipo(t).ToLowerInvariant()}).";
            _resumo.Foreground = Theme.Solid(Theme.Verde);
            return true;
        }

        /// <summary>
        /// Arquivos soltos na janela: .zip é importado; até 3 arquivos de tipos diferentes vão para o perfil escolhido;
        /// o resto (vários perfis de uma vez) passa pela conferência "Trazer arquivos".
        /// </summary>
        private void SoltouNaJanela(string[] arquivos)
        {
            List<string> zips = arquivos.Where(f => Path.GetExtension(f).Equals(".zip", StringComparison.OrdinalIgnoreCase)).ToList();
            if (zips.Count > 0) { foreach (string z in zips) ImportarZip(z); return; }
            List<string> soltos = arquivos.Where(File.Exists).ToList();
            if (_classe != null && soltos.Count <= 3)
            {
                var tipos = soltos.Select(f => (Arquivo: f, Tipo: ArquivosIfc.Detectar(f, out _))).ToList();
                if (tipos.All(x => x.Tipo != null) && tipos.Select(x => x.Tipo).Distinct().Count() == tipos.Count)
                {
                    string lista = string.Join("\n", tipos.Select(x => $"• {Path.GetFileName(x.Arquivo)} → {NomeTipo(x.Tipo.Value).ToLowerInvariant()}"));
                    if (MessageBox.Show(this, $"Ligar ao perfil {_classe}?\n\n{lista}\n\nOs arquivos atuais ficam guardados em Anteriores.", Title,
                            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    foreach (var x in tipos) Ligar(x.Tipo.Value, x.Arquivo, perguntar: false);
                    return;
                }
            }
            Trazer(soltos);
        }

        // ---------- novo, duplicar, renomear, excluir ----------

        private string PedirNomeNovo(string titulo, string pergunta, string sugestao)
        {
            string nome = PedirTexto(titulo, pergunta, sugestao);
            if (nome == null) return null;
            if (!ArquivosIfc.NomeValido(nome)) { MessageBox.Show(this, "Nome de perfil inválido.", Title); return null; }
            return nome.Trim();
        }

        private bool ExisteOutro(string nome, string atual = null) =>
            !string.Equals(nome, atual, StringComparison.CurrentCultureIgnoreCase) && Tipos.Any(t => ArquivosIfc.Obter(t, nome) != null);

        /// <summary>Cria um perfil: pede o nome e os arquivos dele (pelo menos um; o tipo de cada um é reconhecido).</summary>
        private void Novo()
        {
            if (!PerguntarDescartar()) return;
            string nome = PedirNomeNovo("Novo perfil", "Nome do perfil (ex.: Metrô Estrutura):", "");
            if (nome == null) return;
            if (ExisteOutro(nome)) { MessageBox.Show(this, $"Já existe o perfil {nome}.", Title); return; }
            var d = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Filter = "Arquivos do perfil (*.json;*.txt)|*.json;*.txt",
                Title = $"Escolha o .json e os .txt do perfil {nome}",
            };
            if (d.ShowDialog(this) != true) return;
            GravarPerfil(nome, d.FileNames.ToDictionary(f => f, f => ArquivosIfc.Detectar(f, out _)));
        }

        /// <summary>Grava os arquivos (já com o tipo) como o perfil indicado; recusa se faltar tipo ou houver dois do mesmo.</summary>
        private bool GravarPerfil(string nome, Dictionary<string, ArquivosIfc.Tipo?> arquivos)
        {
            List<string> fora = arquivos.Where(k => k.Value == null).Select(k => Path.GetFileName(k.Key)).ToList();
            var validos = arquivos.Where(k => k.Value != null).ToList();
            var repetidos = validos.GroupBy(k => k.Value.Value).Where(g => g.Count() > 1).ToList();
            if (repetidos.Count > 0)
            {
                MessageBox.Show(this, "Cada perfil tem só 1 arquivo de cada tipo. Vieram mais de um de: " +
                    string.Join(", ", repetidos.Select(g => NomeTipo(g.Key).ToLowerInvariant())) + ".", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (validos.Count == 0)
            {
                MessageBox.Show(this, "Nenhum arquivo de perfil reconhecido" + (fora.Count > 0 ? ": " + string.Join(", ", fora) : "") + ".", Title);
                return false;
            }
            Executar(() => { foreach (var k in validos) ArquivosIfc.Gravar(k.Key, k.Value.Value, nome); }, nome);
            _resumo.Text = $"Perfil {nome}: " + string.Join(", ", validos.Select(k => NomeTipo(k.Value.Value).ToLowerInvariant())) +
                           (fora.Count > 0 ? $" (ficou de fora: {string.Join(", ", fora)})" : "");
            _resumo.Foreground = Theme.Solid(fora.Count > 0 ? Theme.LaranjaTexto : Theme.Verde);
            return true;
        }

        /// <summary>Cria um perfil novo com uma cópia sua de cada arquivo do perfil escolhido.</summary>
        private void Duplicar()
        {
            if (_classe == null || !PerguntarDescartar()) return;
            string nova = PedirNomeNovo("Duplicar perfil", $"Nome do novo perfil (cópia de {_classe}):", _classe + " - cópia");
            if (nova == null) return;
            if (ExisteOutro(nova) &&
                MessageBox.Show(this, $"Já existe o perfil {nova}. Substituir os arquivos dele? (os seus atuais ficam em Anteriores)", Title,
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            string de = _classe;
            List<ArquivosIfc.Arquivo> arquivos = _doPadrao.Values.Where(a => a != null).ToList();
            Executar(() => { foreach (ArquivosIfc.Arquivo a in arquivos) ArquivosIfc.Gravar(a.Caminho, a.Tipo, nova); }, nova);
            _resumo.Text = $"Perfil {nova} criado a partir de {de}.";
        }

        /// <summary>Muda o nome do perfil (só quando todos os arquivos dele são seus).</summary>
        private void Renomear()
        {
            if (_classe == null || !PerguntarDescartar()) return;
            string antigo = _classe;
            string nova = PedirNomeNovo("Renomear perfil", $"Novo nome para o perfil {antigo}:", antigo);
            if (nova == null || string.Equals(nova, antigo, StringComparison.CurrentCulture)) return;
            if (ExisteOutro(nova, antigo) &&
                MessageBox.Show(this, $"Já existe o perfil {nova}. Substituir os arquivos dele? (os seus atuais ficam em Anteriores)", Title,
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            List<ArquivosIfc.Arquivo> arquivos = _doPadrao.Values.Where(a => a != null).ToList();
            Executar(() => { foreach (ArquivosIfc.Arquivo a in arquivos) ArquivosIfc.MudarClasse(a.Tipo, antigo, nova); }, nova);
            _resumo.Text = $"Perfil {antigo} renomeado para {nova}.";
        }

        /// <summary>Exclui o perfil (só quando todos os arquivos dele são seus). Os arquivos ficam em Anteriores.</summary>
        private void Excluir()
        {
            if (_classe == null || !TodosSeus) return;
            string classe = _classe;
            if (MessageBox.Show(this, $"Excluir o perfil {classe}?\n\nOs arquivos dele ficam guardados em {ArquivosIfc.PastaAnteriores}", "MetroSP - Excluir perfil",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            _setup = null;
            List<ArquivosIfc.Arquivo> arquivos = _doPadrao.Values.Where(a => a != null).ToList();
            _classe = null;
            Executar(() => { foreach (ArquivosIfc.Arquivo a in arquivos) ArquivosIfc.ApagarDoUsuario(a.Tipo, a.Classe); }, null);
            _resumo.Text = $"Perfil {classe} excluído (os arquivos estão em Anteriores).";
        }

        // ---------- exportar e importar ----------

        /// <summary>Salva os arquivos do perfil num .zip (uma pasta por tipo, como no Forma: Setups, PropertySets, Mapeamentos).</summary>
        private void Exportar()
        {
            if (_classe == null || !PerguntarDescartar()) return;
            List<ArquivosIfc.Arquivo> arquivos = _doPadrao.Values.Where(a => a != null).ToList();
            if (arquivos.Count == 0) return;
            var d = new Microsoft.Win32.SaveFileDialog { FileName = _classe + ".zip", Filter = "Perfil MetroSP (*.zip)|*.zip", Title = "Exportar o perfil " + _classe };
            if (d.ShowDialog(this) != true) return;
            try
            {
                if (File.Exists(d.FileName)) File.Delete(d.FileName);
                using (ZipArchive zip = ZipFile.Open(d.FileName, ZipArchiveMode.Create))
                    foreach (ArquivosIfc.Arquivo a in arquivos)
                        zip.CreateEntryFromFile(a.Caminho, ArquivosIfc.Sub(a.Tipo) + "/" + _classe + ArquivosIfc.Extensao(a.Tipo));
                _resumo.Text = $"Perfil {_classe} exportado em {d.FileName}";
                _resumo.Foreground = Theme.Solid(Theme.Verde);
            }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível exportar: " + ex.Message, Title); }
        }

        private void Importar()
        {
            if (!PerguntarDescartar()) return;
            var d = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Filter = "Perfil MetroSP ou arquivos soltos (*.zip;*.json;*.txt)|*.zip;*.json;*.txt|Perfil MetroSP (*.zip)|*.zip",
                Title = "Importar perfil",
            };
            if (d.ShowDialog(this) != true) return;
            List<string> zips = d.FileNames.Where(f => Path.GetExtension(f).Equals(".zip", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (string z in zips) ImportarZip(z);
            List<string> soltos = d.FileNames.Except(zips).ToList();
            if (soltos.Count > 0) Trazer(soltos);
        }

        /// <summary>Lê um perfil exportado (.zip): o tipo vem da pasta (Setups, PropertySets, Mapeamentos) ou do conteúdo.</summary>
        private void ImportarZip(string zip)
        {
            string temp = Path.Combine(Path.GetTempPath(), "MetroSP-perfil-" + Guid.NewGuid().ToString("N"));
            try
            {
                var arquivos = new Dictionary<string, ArquivosIfc.Tipo?>(StringComparer.OrdinalIgnoreCase);
                string sugestao = null;
                using (ZipArchive z = ZipFile.OpenRead(zip))
                {
                    int n = 0;
                    foreach (ZipArchiveEntry e in z.Entries)
                    {
                        string ext = Path.GetExtension(e.Name).ToLowerInvariant();
                        if (e.Length == 0 || (ext != ".json" && ext != ".txt")) continue;
                        string destino = Path.Combine(temp, (n++).ToString(), e.Name);
                        Directory.CreateDirectory(Path.GetDirectoryName(destino));
                        e.ExtractToFile(destino, true);
                        string pasta = Enumerable.Reverse(e.FullName.Replace('\\', '/').Split('/')).Skip(1).FirstOrDefault() ?? "";
                        ArquivosIfc.Tipo? tipo = Tipos.Cast<ArquivosIfc.Tipo?>().FirstOrDefault(t =>
                            string.Equals(ArquivosIfc.Sub(t.Value), pasta, StringComparison.OrdinalIgnoreCase) && ext == ArquivosIfc.Extensao(t.Value));
                        arquivos[destino] = tipo ?? ArquivosIfc.Detectar(destino, out _);
                        if (arquivos[destino] == ArquivosIfc.Tipo.Setup || sugestao == null) sugestao = Path.GetFileNameWithoutExtension(e.Name);
                    }
                }
                if (arquivos.Count == 0) { MessageBox.Show(this, $"{Path.GetFileName(zip)} não tem arquivos de perfil (.json ou .txt).", Title); return; }
                string nome = PedirNomeNovo("Importar perfil", $"Nome do perfil que vem em {Path.GetFileName(zip)}:", sugestao ?? Path.GetFileNameWithoutExtension(zip));
                if (nome == null) return;
                if (ExisteOutro(nome) &&
                    MessageBox.Show(this, $"Já existe o perfil {nome}. Substituir os arquivos dele? (os seus atuais ficam em Anteriores)", Title,
                        MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                GravarPerfil(nome, arquivos);
            }
            catch (Exception ex) { MessageBox.Show(this, $"Não foi possível importar {Path.GetFileName(zip)}:\n{ex.Message}", Title); }
            finally
            {
                try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { /* temporário */ }
            }
        }

        // ---------- configurações (setup .json) ----------

        private void MostrarSetup()
        {
            ArquivosIfc.Arquivo a = Escolhido(ArquivosIfc.Tipo.Setup);
            _setup = null;
            _tituloSetup.Text = a == null ? "CONFIGURAÇÕES" : $"CONFIGURAÇÕES ({a.Nome})";
            if (a == null)
            {
                _painelSetup.Content = new TextBlock
                {
                    Text = _classe == null ? "Escolha um perfil na lista." : "Este perfil ainda não tem configurações. Use Escolher... na linha do .json ou arraste o arquivo para ela.",
                    Foreground = Theme.Solid(Theme.Cinza), TextWrapping = TextWrapping.Wrap,
                };
            }
            else
            {
                try { _painelSetup.Content = PainelSetup(a); }
                catch (Exception ex)
                {
                    _painelSetup.Content = new TextBlock { Text = "Não foi possível ler " + a.Nome + ":\n" + ex.Message, TextWrapping = TextWrapping.Wrap, Foreground = Theme.Solid(Theme.LaranjaTexto) };
                }
            }
            Alterou();
        }

        private UIElement PainelSetup(ArquivosIfc.Arquivo a)
        {
            OpcoesSetup o = OpcoesSetup.Ler(a.Caminho);
            _setup = o;
            var painel = new StackPanel();
            // A versão escolhida muda o selo do cabeçalho do perfil.
            void AtualizarVersao()
            {
                if (_selosPadrao.Children.Count > 0 && _selosPadrao.Children[0] is Border selo)
                    Theme.MudarSelo(selo, o.EhVersaoAceita ? "pronto para exportar" : "a exportação MetroSP exige " + OpcoesSetup.VersoesAceitas,
                        o.EhVersaoAceita ? Theme.Estado.Ok : Theme.Estado.Aviso);
            }

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
            if (!a.DoUsuario)
            {
                TextBlock nota = Theme.Detalhe($"Ao salvar, o MetroSP grava uma cópia sua, que vale no lugar do arquivo {a.Origem}.");
                nota.ToolTip = ArquivosIfc.Destino(ArquivosIfc.Tipo.Setup, a.Classe);
                nota.Margin = new Thickness(0, 10, 0, 0);
                painel.Children.Add(nota);
            }
            return painel;
        }

        private void Alterou()
        {
            bool alterado = _setup?.Alterado == true;
            _salvar.IsEnabled = alterado;
            _descartar.IsEnabled = alterado;
            _estado.Text = alterado ? "● Alterações não salvas" : "";
            _estado.ToolTip = alterado ? "As configurações mudaram e ainda não foram gravadas em " + Path.GetFileName(_setup.Arquivo) : null;
        }

        private void SalvarSetup()
        {
            ArquivosIfc.Arquivo a = Escolhido(ArquivosIfc.Tipo.Setup);
            if (_setup == null || a == null) return;
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
            _resumo.Text = $"Perfil {a.Classe} salvo.";
            _resumo.ToolTip = destino;
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
