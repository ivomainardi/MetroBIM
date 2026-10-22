using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.DB;
using MetroSP.Ui;
using Color = System.Windows.Media.Color;

namespace MetroSP.Classificacao
{
    /// <summary>
    /// Confere se elementos com o mesmo código têm a mesma descrição. Para cada código com mais de
    /// uma descrição, o usuário escolhe a correta (a da planilha vem marcada) e unifica.
    /// Modal: as transações rodam no contexto do comando.
    /// </summary>
    public class ConsistenciaClassificacaoWindow : Window
    {
        private readonly Autodesk.Revit.UI.UIDocument _uidoc;
        private readonly Document _doc;
        private readonly ClassSettings _config;
        private readonly ICollection<ElementId> _selecao;
        private readonly PlanilhaSeletor _planilha;
        private readonly ComboBox _escopo;
        private readonly SeletorCategorias _categorias = new SeletorCategorias { Margin = new Thickness(0, 0, 12, 0) };
        private readonly Button _reanalisar;
        private List<Element> _base = new List<Element>();
        private readonly WrapPanel _resumo;
        private readonly StackPanel _cartoes;
        private readonly TextBlock _rodapeInfo;
        private readonly Button _unificar;
        private readonly Button _exportar;
        private readonly Button _selecionar;
        private List<GrupoCodigo> _grupos;
        private List<(string Descricao, List<string> Codigos)> _mesmaDescricao;
        private readonly HashSet<GrupoCodigo> _marcados = new HashSet<GrupoCodigo>();
        private bool _analisado;

        public ConsistenciaClassificacaoWindow(Autodesk.Revit.UI.UIDocument uidoc, ClassSettings config)
        {
            _uidoc = uidoc;
            _doc = uidoc.Document;
            _config = config;
            ICollection<ElementId> sel = uidoc.Selection.GetElementIds();
            _selecao = sel.Count > 0 ? sel : null;

            Title = "MetroSP - Consistência código × descrição";
            Width = 980;
            Height = 720;
            MinWidth = 700;
            MinHeight = 460;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _planilha = new PlanilhaSeletor(config);
            _planilha.Carregada += l => { if (_analisado) Analisar(); };

            _escopo = new ComboBox { Padding = new Thickness(4, 3, 4, 3), MinWidth = 220, Margin = new Thickness(0, 0, 12, 0) };
            if (_selecao != null) _escopo.Items.Add($"Seleção atual ({_selecao.Count} elementos)");
            _escopo.Items.Add("Vista atual");
            _escopo.Items.Add("Todo o modelo");
            _escopo.SelectedIndex = _selecao != null ? 0 : _escopo.Items.Count - 1;
            var analisar = new Button { Content = "Analisar", Style = Theme.PrimaryButton };
            analisar.Click += (s, e) => Analisar();
            var barra = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            barra.Children.Add(new TextBlock { Text = "Escopo", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            barra.Children.Add(_escopo);
            barra.Children.Add(_categorias);
            barra.Children.Add(analisar);
            _escopo.SelectionChanged += (s, e) => CarregarEscopo();
            _categorias.Mudou += () => { if (_analisado) Analisar(); };

            _resumo = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            _cartoes = new StackPanel();
            _cartoes.Children.Add(new TextBlock
            {
                Text = "Clique em \"Analisar\". A planilha é opcional aqui: quando o código está nela, a descrição da lista vem marcada.",
                Foreground = Theme.Solid(Theme.Cinza),
                TextWrapping = TextWrapping.Wrap,
            });

            var corpo = new DockPanel();
            foreach (UIElement el in new UIElement[] { _planilha, barra, _resumo })
            {
                DockPanel.SetDock(el, Dock.Top);
                corpo.Children.Add(el);
            }
            corpo.Children.Add(new ScrollViewer { Content = _cartoes, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

            _rodapeInfo = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            _exportar = new Button { Content = "Exportar (.csv)", IsEnabled = false };
            _selecionar = new Button { Content = "Selecionar divergentes", IsEnabled = false, ToolTip = "Seleciona no Revit os elementos cuja descrição difere da escolhida." };
            _unificar = new Button { Content = "Unificar", Style = Theme.PrimaryButton, IsEnabled = false };
            _reanalisar = new Button { Content = "Reanalisar", IsEnabled = false, ToolTip = "Lê o modelo de novo, mantendo escopo e categorias." };
            _reanalisar.Click += (s, e) => Analisar();
            var fechar = new Button { Content = "Fechar", IsCancel = true };
            _exportar.Click += (s, e) => Exportar();
            _selecionar.Click += (s, e) => Selecionar();
            _unificar.Click += (s, e) => Unificar();
            fechar.Click += (s, e) => Close();
            var rodape = new DockPanel();
            StackPanel botoes = Theme.Buttons(_reanalisar, _exportar, _selecionar, _unificar, fechar);
            DockPanel.SetDock(botoes, Dock.Right);
            rodape.Children.Add(botoes);
            rodape.Children.Add(_rodapeInfo);

            Theme.Apply(this, "Consistência código × descrição", _doc.Title + $" · mesmo código em {config.Parametro} deve ter a mesma descrição", corpo, rodape);
            CarregarEscopo();
            _planilha.Carregar();
        }

        /// <summary>Lê os elementos do escopo e atualiza a lista de categorias.</summary>
        private void CarregarEscopo()
        {
            string esc = _escopo.SelectedItem as string ?? "";
            _base = esc.StartsWith("Seleção") ? ClassificationCore.Elementos(_doc, _selecao, null)
                : esc == "Vista atual" ? ClassificationCore.Elementos(_doc, null, _doc.ActiveView.Id)
                : ClassificationCore.Elementos(_doc, null, null);
            _categorias.Definir(_base.GroupBy(e => e.Category.Name).ToDictionary(g => g.Key, g => g.Count()));
        }

        private List<Element> NoEscopo()
        {
            CarregarEscopo(); // relê o modelo (reanalisar pega o que mudou)
            return _base.Where(e => _categorias.Aceita(e.Category.Name)).ToList();
        }

        private void Analisar()
        {
            Cursor = Cursors.Wait;
            try { _grupos = ClassificationCore.Consistencia(_doc, NoEscopo(), _planilha.Lista, _config.Parametro, out _mesmaDescricao); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível analisar:\n" + ex.Message, Title);
                return;
            }
            finally { Cursor = null; }
            _analisado = true;
            _marcados.Clear();
            foreach (GrupoCodigo g in _grupos) _marcados.Add(g);
            Mostrar();
        }

        private void Mostrar()
        {
            _resumo.Children.Clear();
            _resumo.Children.Add(Theme.Chip($"{_grupos.Count} códigos com descrições diferentes", destaque: _grupos.Count > 0));
            _resumo.Children.Add(Theme.Chip($"{_grupos.Sum(Divergentes)} elementos divergentes", escuro: _grupos.Count > 0));
            _resumo.Children.Add(Theme.Chip($"{_mesmaDescricao.Count} descrições com mais de um código"));

            _cartoes.Children.Clear();
            if (_grupos.Count == 0)
                _cartoes.Children.Add(new TextBlock { Text = "Tudo consistente: cada código usado no modelo tem uma só descrição.", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
            if (_grupos.Count > 0)
            {
                var todos = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
                todos.Children.Add(new TextBlock { Text = "Marcar como válida em todos os códigos:", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                var daLista = new Button { Content = "Descrição da planilha", ToolTip = "Códigos fora da planilha ficam com a mais usada.", Margin = new Thickness(0, 0, 6, 0) };
                var maisUsada = new Button { Content = "A mais usada no modelo" };
                daLista.Click += (s, e) => MarcarTodos(g => g.NaLista?.Descricao ?? g.Descricoes[0].Descricao);
                maisUsada.Click += (s, e) => MarcarTodos(g => g.Descricoes[0].Descricao);
                todos.Children.Add(daLista);
                todos.Children.Add(maisUsada);
                _cartoes.Children.Add(todos);
            }
            foreach (GrupoCodigo g in _grupos) _cartoes.Children.Add(Cartao(g));

            if (_mesmaDescricao.Count > 0)
            {
                _cartoes.Children.Add(new TextBlock { Text = "Para conferir: mesma descrição com códigos diferentes", FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(0, 10, 0, 4) });
                _cartoes.Children.Add(new TextBlock
                {
                    Text = "Pode estar certo (itens diferentes com o mesmo nome), mas costuma ser código digitado errado. Não é corrigido automaticamente.",
                    Foreground = Theme.Solid(Theme.Cinza),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 6),
                });
                foreach (var (desc, codigos) in _mesmaDescricao)
                    _cartoes.Children.Add(new TextBlock { Text = $"•  \"{desc}\" usada com {string.Join(", ", codigos)}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) });
            }

            _exportar.IsEnabled = _reanalisar.IsEnabled = true;
            _selecionar.IsEnabled = _grupos.Count > 0;
            AtualizarRodape();
        }

        private void MarcarTodos(Func<GrupoCodigo, string> escolha)
        {
            foreach (GrupoCodigo g in _grupos) g.Escolhida = escolha(g);
            Mostrar();
        }

        private static int Divergentes(GrupoCodigo g) =>
            g.Descricoes.Where(d => d.Descricao != g.Escolhida).Sum(d => d.Elementos.Count);

        private UIElement Cartao(GrupoCodigo g)
        {
            var conteudo = new StackPanel();

            var topo = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var marcar = new CheckBox { Content = "Unificar", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
            marcar.Click += (s, e) =>
            {
                if (marcar.IsChecked == true) _marcados.Add(g); else _marcados.Remove(g);
                AtualizarRodape();
            };
            DockPanel.SetDock(marcar, Dock.Right);
            topo.Children.Add(marcar);
            var titulo = new StackPanel { Orientation = Orientation.Horizontal };
            titulo.Children.Add(new TextBlock { Text = g.Codigo, FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold, FontSize = 13, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
            titulo.Children.Add(g.NaLista != null
                ? Theme.Chip("na lista: " + g.NaLista.Descricao)
                : Theme.Chip("fora da lista", destaque: true));
            topo.Children.Add(titulo);
            conteudo.Children.Add(topo);
            conteudo.Children.Add(new TextBlock
            {
                Text = "Qual descrição é a válida? Os elementos com as outras serão corrigidos para ela.",
                Foreground = Theme.Solid(Theme.Cinza),
                Margin = new Thickness(0, 0, 0, 4),
            });

            string grupo = "cod_" + g.GetHashCode();
            var opcoes = g.Descricoes.Select(d => (d.Descricao, Rotulo: $"{d.Descricao}    ({d.Elementos.Count} elementos{(d.Donos.Count != d.Elementos.Count ? $", {d.Donos.Count} tipos/instâncias" : "")})")).ToList();
            if (g.NaLista != null && opcoes.All(o => o.Descricao != g.NaLista.Descricao))
                opcoes.Insert(0, (g.NaLista.Descricao, $"{g.NaLista.Descricao}    (descrição da planilha, nenhum elemento usa)"));
            var textos = new List<(RadioButton Rb, TextBlock Marca)>();
            void Marcas()
            {
                foreach (var (rb, marca) in textos)
                {
                    marca.Text = rb.IsChecked == true ? "  válida" : "  → será corrigida";
                    marca.Foreground = Theme.Solid(rb.IsChecked == true ? Color.FromRgb(0x8A, 0x45, 0x00) : Theme.Cinza);
                    marca.FontWeight = rb.IsChecked == true ? FontWeights.SemiBold : FontWeights.Normal;
                }
            }
            foreach (var (desc, rotulo) in opcoes)
            {
                var linha = new TextBlock { TextWrapping = TextWrapping.Wrap };
                linha.Inlines.Add(rotulo + (g.NaLista != null && desc == g.NaLista.Descricao ? "  ✓ planilha" : ""));
                var marcaBloco = new TextBlock();
                var conteudoRb = new StackPanel { Orientation = Orientation.Horizontal };
                conteudoRb.Children.Add(linha);
                conteudoRb.Children.Add(marcaBloco);
                var rb = new RadioButton { GroupName = grupo, IsChecked = desc == g.Escolhida, Margin = new Thickness(0, 2, 0, 2), Content = conteudoRb };
                rb.Checked += (s, e) => { g.Escolhida = desc; Marcas(); AtualizarRodape(); };
                textos.Add((rb, marcaBloco));
                conteudo.Children.Add(rb);
            }

            // Outra descrição, digitada (ex.: corrigir a escrita de todas).
            bool outraAtiva = opcoes.All(o => o.Descricao != g.Escolhida);
            var outraTexto = new System.Windows.Controls.TextBox { MinWidth = 280, Text = outraAtiva ? g.Escolhida : "", Margin = new Thickness(6, 0, 0, 0) };
            var outraMarca = new TextBlock();
            var outraLinha = new StackPanel { Orientation = Orientation.Horizontal };
            outraLinha.Children.Add(new TextBlock { Text = "Outra descrição:", VerticalAlignment = VerticalAlignment.Center });
            outraLinha.Children.Add(outraTexto);
            outraLinha.Children.Add(outraMarca);
            var outra = new RadioButton { GroupName = grupo, IsChecked = outraAtiva, Margin = new Thickness(0, 4, 0, 2), Content = outraLinha, VerticalContentAlignment = VerticalAlignment.Center };
            void UsarOutra()
            {
                string t = outraTexto.Text.Trim();
                if (t.Length > 0) g.Escolhida = t;
                Marcas();
                AtualizarRodape();
            }
            outra.Checked += (s, e) => UsarOutra();
            outraTexto.GotKeyboardFocus += (s, e) => outra.IsChecked = true;
            outraTexto.TextChanged += (s, e) => { if (outra.IsChecked == true) UsarOutra(); };
            textos.Add((outra, outraMarca));
            conteudo.Children.Add(outra);
            Marcas();

            return new Border
            {
                Background = Brushes.White,
                BorderBrush = Theme.Solid(Theme.CinzaClaro),
                BorderThickness = new Thickness(1, 1, 1, 1),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 8),
                Child = conteudo,
            };
        }

        /// <summary>O que será gravado: para cada dono com descrição diferente da escolhida, Código:Escolhida.</summary>
        private List<(ElementId Dono, string Valor)> Gravacoes()
        {
            var r = new List<(ElementId, string)>();
            if (_grupos == null) return r;
            foreach (GrupoCodigo g in _grupos.Where(_marcados.Contains))
            {
                string codigo = g.NaLista?.Codigo ?? g.Codigo;
                string valor = ValorClassificacao.Escrever(codigo, g.Escolhida);
                foreach (var d in g.Descricoes.Where(d => d.Descricao != g.Escolhida))
                    r.AddRange(d.Donos.Select(id => (id, valor)));
            }
            return r;
        }

        private void AtualizarRodape()
        {
            var gravar = Gravacoes();
            int elems = _grupos?.Where(_marcados.Contains).Sum(Divergentes) ?? 0;
            _rodapeInfo.Text = _grupos == null ? "" : $"{_marcados.Count} de {_grupos.Count} códigos marcados · {elems} elementos mudam";
            _unificar.IsEnabled = gravar.Count > 0;
            _unificar.Content = gravar.Count == 0 ? "Nada a unificar" : $"Unificar {_marcados.Count} códigos";
        }

        private void Unificar()
        {
            var gravar = Gravacoes();
            if (gravar.Count == 0) return;
            var marcados = _grupos.Where(_marcados.Contains).ToList();
            string msg = $"Unificar a descrição de {marcados.Count} códigos ({gravar.Count} gravações em instâncias ou tipos)?\n\n" +
                         string.Join("\n", marcados.Take(6).Select(g => $"• {g.Codigo}  →  {g.Escolhida}")) +
                         (marcados.Count > 6 ? $"\n• e mais {marcados.Count - 6}" : "") +
                         "\n\nÉ uma única operação: pode ser desfeita com Ctrl+Z.";
            if (MessageBox.Show(this, msg, Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                ResultadoGravacao r = ClassificationCore.Gravar(_doc, _config.Parametro, gravar, "Unificar descrição da classificação",
                    q => MessageBox.Show(this, q, Title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
                if (r.Falhas.Count == 0) MessageBox.Show(this, r.Resumo(), Title);
                else if (MessageBox.Show(this, r.Resumo() + "\n\nSelecionar no Revit os que não foram gravados?", Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                    _uidoc.Selection.SetElementIds(r.Falhas.Select(f => f.Id).ToList());
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível gravar. Nada foi alterado.\n\n" + ex.Message, Title);
                return;
            }
            Analisar();
        }

        private void Selecionar()
        {
            if (_grupos == null) return;
            var ids = _grupos.SelectMany(g => g.Descricoes.Where(d => d.Descricao != g.Escolhida).SelectMany(d => d.Elementos)).Distinct().ToList();
            _uidoc.Selection.SetElementIds(ids);
            _rodapeInfo.Text = $"{ids.Count} elementos divergentes selecionados no Revit.";
        }

        private void Exportar()
        {
            if (_grupos == null) return;
            var dialogo = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                FileName = $"consistencia_classificacao_{_doc.Title}.csv".Replace(" ", "_"),
            };
            if (dialogo.ShowDialog(this) != true) return;
            string Q(string v) => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";
            var sb = new StringBuilder();
            sb.AppendLine("Código;Descrição no modelo;Elementos;Descrição na lista;Escolhida;Ids");
            foreach (GrupoCodigo g in _grupos)
                foreach (var d in g.Descricoes)
                    sb.AppendLine(string.Join(";", Q(g.Codigo), Q(d.Descricao), d.Elementos.Count.ToString(), Q(g.NaLista?.Descricao ?? "(fora da lista)"),
                        Q(d.Descricao == g.Escolhida ? "sim" : ""), Q(string.Join(" ", d.Elementos.Select(id => id.Value)))));
            if (_mesmaDescricao.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Descrição;Códigos usados");
                foreach (var (desc, codigos) in _mesmaDescricao) sb.AppendLine(Q(desc) + ";" + Q(string.Join(", ", codigos)));
            }
            try { File.WriteAllText(dialogo.FileName, sb.ToString(), new UTF8Encoding(true)); }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível gravar o arquivo:\n" + ex.Message, Title); }
        }
    }
}
