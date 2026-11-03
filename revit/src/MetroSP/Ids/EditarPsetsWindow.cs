using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using MetroSP.Ui;

namespace MetroSP.Ids
{
    /// <summary>
    /// Mostra o .txt de property sets como tabela: cada propriedade, o tipo, o parâmetro do Revit que ela lê
    /// e se esse parâmetro existe no modelo. Dá para trocar o parâmetro (com sugestões do modelo), acrescentar
    /// e remover propriedades e salvar de volta no formato do exportador IFC.
    /// </summary>
    public class EditarPsetsWindow : Window
    {
        public class Linha : INotifyPropertyChanged
        {
            private string _pset, _aplicacao, _entidades, _propriedade, _tipo, _parametro;
            internal HashSet<string> Modelo;
            internal ModeloIfc ModeloIfc;
            private ModeloIfc.Contagem? _contagem;

            public string PropertySet { get => _pset; set => Set(ref _pset, value); }
            /// <summary>"Instância" ou "Tipo".</summary>
            public string Aplicacao { get => _aplicacao; set => Set(ref _aplicacao, value); }
            public string Entidades { get => _entidades; set => Set(ref _entidades, value); }
            public string Propriedade { get => _propriedade; set => Set(ref _propriedade, value); }
            public string TipoDado { get => _tipo; set => Set(ref _tipo, value); }
            public string Parametro { get => _parametro; set => Set(ref _parametro, value); }

            public bool ParametroNoModelo => !string.IsNullOrWhiteSpace(Parametro) && Modelo != null && Modelo.Contains(Parametro.Trim());
            public bool TipoOk => ExportMapping.TipoAceito(TipoDado);
            /// <summary>Linha sem problema: parâmetro existe no modelo e o tipo é aceito pelo exportador.</summary>
            public bool Encontrado => ParametroNoModelo && TipoOk;
            public string Situacao
            {
                get
                {
                    var p = new List<string>();
                    if (!TipoOk) p.Add(ExportMapping.TipoCorreto(TipoDado) is string certo ? $"Tipo: use {certo}" : "Tipo não aceito");
                    p.Add(string.IsNullOrWhiteSpace(Parametro) ? "Sem parâmetro" : ParametroNoModelo ? "Encontrado" : "Não encontrado no modelo");
                    return string.Join(" · ", p);
                }
            }

            /// <summary>Em quantos elementos das entidades o parâmetro tem valor (null sem modelo ou sem parâmetro).</summary>
            public ModeloIfc.Contagem? Contagem
            {
                get
                {
                    if (_contagem == null && ModeloIfc != null && ParametroNoModelo)
                        _contagem = ModeloIfc.Contar(ListaEntidades, Parametro.Trim(), Aplicacao == Tipo);
                    return _contagem;
                }
            }
            public bool SemValor => Contagem is ModeloIfc.Contagem c && c.Elementos > 0 && c.ComValor < c.Elementos;
            public string NoModelo
            {
                get
                {
                    if (ModeloIfc == null) return "";
                    if (!ParametroNoModelo) return "—";
                    ModeloIfc.Contagem c = Contagem.Value;
                    return c.Elementos == 0 ? "nenhum elemento" : c.ComValor == c.Elementos ? $"todos ({c.Elementos})" : $"{c.ComValor} de {c.Elementos}";
                }
            }
            /// <summary>Linha com algo a ver: parâmetro ausente, tipo não aceito ou vazio em parte dos elementos.</summary>
            public bool Problema => !Encontrado || SemValor;
            internal List<string> ListaEntidades =>
                (Entidades ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(e => e.Trim()).ToList();

            public event PropertyChangedEventHandler PropertyChanged;
            private void Set(ref string campo, string valor, [CallerMemberName] string nome = null)
            {
                if (campo == valor) return;
                campo = valor;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nome));
                if (nome == nameof(Parametro) || nome == nameof(TipoDado) || nome == nameof(Entidades) || nome == nameof(Aplicacao))
                {
                    _contagem = null;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Encontrado)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Situacao)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NoModelo)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Problema)));
                }
            }
        }

        private const string Instancia = "Instância", Tipo = "Tipo";
        private static readonly string[] TiposDado = ExportMapping.TiposComuns.Concat(ExportMapping.TiposAceitos.Except(ExportMapping.TiposComuns)).ToArray();

        private readonly ObservableCollection<Linha> _linhas = new ObservableCollection<Linha>();
        private readonly HashSet<string> _modelo;
        private readonly List<string> _cabecalho;
        private readonly string _destino;
        private readonly ListCollectionView _vista;
        private readonly DataGrid _grade;
        private readonly WrapPanel _resumo;
        private readonly CheckBox _soAusentes;
        private readonly ModeloIfc _modeloIfc;
        private readonly TreeView _arvore;
        private readonly Border _detalhe;
        // Filtro da árvore: property set e, opcionalmente, a entidade.
        private string _arvorePset, _arvoreEntidade;

        /// <summary>Arquivo gravado ao salvar (null se cancelou).</summary>
        public string ArquivoSalvo { get; private set; }
        /// <summary>true se o usuário pediu para voltar ao .txt instalado.</summary>
        public bool Restaurou { get; private set; }

        /// <param name="arquivo">.txt a abrir.</param>
        /// <param name="destino">Onde "Salvar" grava; null = pergunta onde salvar.</param>
        /// <param name="podeRestaurar">Mostra "Restaurar padrão" (cópia editada de uma classe instalada).</param>
        /// <param name="modelo">Modelo aberto, para a coluna "No modelo" e as sugestões (null = só confere os nomes).</param>
        /// <param name="psetInicial">Property set já escolhido na árvore ao abrir.</param>
        public EditarPsetsWindow(string arquivo, ISet<string> parametrosModelo, string destino, bool podeRestaurar,
            ModeloIfc modelo = null, string psetInicial = null)
        {
            _modelo = new HashSet<string>(parametrosModelo ?? (ISet<string>)modelo?.Parametros ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
            _modeloIfc = modelo;
            _arvorePset = psetInicial;
            _destino = destino;

            Title = "MetroSP - Property sets";
            Width = 1180;
            Height = 640;
            MinWidth = 720;
            MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var mapa = new ExportMapping();
            mapa.LoadUserDefinedPropertySets(arquivo);
            // Comentários do início (formato, revisões) são mantidos ao salvar.
            _cabecalho = File.ReadLines(arquivo).TakeWhile(l => l.Trim().Length == 0 || l.TrimStart().StartsWith("#"))
                .Where(l => !l.Contains("salvo pelo MetroSP")).ToList();
            foreach (PropertyMapping p in mapa.Properties)
                _linhas.Add(new Linha
                {
                    Modelo = _modelo,
                    ModeloIfc = _modeloIfc,
                    PropertySet = p.PropertySet,
                    Aplicacao = p.OnType ? Tipo : Instancia,
                    Entidades = string.Join(",", p.Entities),
                    Propriedade = p.Property,
                    TipoDado = p.FileType,
                    Parametro = p.RevitParameter,
                });
            foreach (Linha l in _linhas) l.PropertyChanged += (s, e) => AtualizarResumo();
            _linhas.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null) foreach (Linha l in e.NewItems) l.PropertyChanged += (s2, e2) => AtualizarResumo();
                AtualizarResumo();
            };

            var corpo = new DockPanel();

            var topo = new StackPanel();
            DockPanel.SetDock(topo, Dock.Top);
            topo.Children.Add(new TextBlock
            {
                Text = arquivo + (_modeloIfc != null ? $"  ·  conferido no modelo {_modeloIfc.Titulo}" : ""),
                Foreground = Theme.Solid(Theme.Cinza), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = arquivo,
            });
            _resumo = new WrapPanel { Margin = new Thickness(0, 8, 0, 6) };
            topo.Children.Add(_resumo);
            _soAusentes = new CheckBox { Content = "Mostrar só as linhas com problema (parâmetro não encontrado, tipo não aceito ou sem valor em algum elemento)", Margin = new Thickness(0, 0, 0, 6) };
            _soAusentes.Click += (s, e) => AtualizarFiltro();
            topo.Children.Add(_soAusentes);
            if (mapa.Warnings.Count > 0)
                topo.Children.Add(new TextBlock
                {
                    Text = string.Join(" ", mapa.Warnings.Take(5)) + (mapa.Warnings.Count > 5 ? " ..." : ""),
                    Foreground = Theme.Solid(Theme.Laranja), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
                });
            corpo.Children.Add(topo);

            var acoes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(acoes, Dock.Bottom);
            var adicionar = new Button { Content = "Adicionar propriedade" };
            var remover = new Button { Content = "Remover selecionadas", Margin = new Thickness(8, 0, 0, 0) };
            acoes.Children.Add(adicionar);
            acoes.Children.Add(remover);
            var limparFiltros = new Button { Content = "Limpar filtros", Margin = new Thickness(8, 0, 0, 0) };
            limparFiltros.Click += (s, e) => LimparFiltros();
            acoes.Children.Add(limparFiltros);
            corpo.Children.Add(acoes);

            // Detalhe da linha escolhida: por que ela tem problema e parâmetros parecidos que existem no modelo.
            _detalhe = new Border
            {
                BorderBrush = Theme.Solid(Theme.CinzaClaro), BorderThickness = new Thickness(1), Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed,
            };
            DockPanel.SetDock(_detalhe, Dock.Bottom);
            corpo.Children.Add(_detalhe);

            // Árvore property set › entidade: escolher filtra a grade; o número laranja conta as linhas com problema.
            _arvore = new TreeView { Width = 200, Margin = new Thickness(0, 0, 10, 0), BorderBrush = Theme.Solid(Theme.CinzaClaro) };
            _arvore.Resources[SystemColors.HighlightBrushKey] = Theme.Solid(Theme.LaranjaSelecao);
            _arvore.Resources[SystemColors.HighlightTextBrushKey] = Theme.Solid(Theme.Preto);
            _arvore.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = Theme.Solid(Theme.LaranjaSelecao);
            _arvore.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = Theme.Solid(Theme.Preto);
            _arvore.SelectedItemChanged += (s, e) =>
            {
                if (_montandoArvore) return;
                var no = (e.NewValue as TreeViewItem)?.Tag as Tuple<string, string>;
                _arvorePset = no?.Item1;
                _arvoreEntidade = no?.Item2;
                AtualizarFiltro();
            };
            DockPanel.SetDock(_arvore, Dock.Left);
            corpo.Children.Add(_arvore);

            _vista = new ListCollectionView(_linhas) { Filter = o => Passa((Linha)o, null) };
            _grade = new DataGrid
            {
                ToolTip = "Clique duas vezes numa célula para editar. A coluna Parâmetro do Revit sugere os parâmetros do modelo.",
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserSortColumns = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                SelectionMode = DataGridSelectionMode.Extended,
                ItemsSource = _vista,
            };
            Theme.StyleSelection(_grade);
            // Com o filtro ligado, a linha corrigida sai da lista quando a edição termina (não durante: o
            // ListCollectionView não aceita Refresh com uma linha em edição).
            _grade.RowEditEnding += (s, e) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_soAusentes.IsChecked == true || _filtroSelo != null) { try { _vista.Refresh(); } catch (InvalidOperationException) { } }
                    MontarArvore();
                    MostrarDetalhe();
                }), System.Windows.Threading.DispatcherPriority.Background);
            };
            var estiloLinha = new Style(typeof(DataGridRow));
            var problema = new DataTrigger { Binding = new Binding(nameof(Linha.Problema)), Value = true };
            problema.Setters.Add(new Setter(BackgroundProperty, Theme.LinhaProblema));
            estiloLinha.Triggers.Add(problema);
            _grade.RowStyle = estiloLinha;

            _grade.Columns.Add(Texto("Property set", nameof(Linha.PropertySet), 150));
            _grade.Columns.Add(Lista("Aplica em", nameof(Linha.Aplicacao), new[] { Instancia, Tipo }, 80));
            _grade.Columns.Add(Texto("Entidades IFC", nameof(Linha.Entidades), 150));
            _grade.Columns.Add(Texto("Propriedade", nameof(Linha.Propriedade), 150));
            _grade.Columns.Add(ComboEditavel("Tipo", nameof(Linha.TipoDado), TiposDado.ToList(), new DataGridLength(110), 90));
            _grade.Columns.Add(ComboEditavel("Parâmetro do Revit", nameof(Linha.Parametro),
                _modelo.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList(), new DataGridLength(1, DataGridLengthUnitType.Star), 160));
            _grade.Columns.Add(new DataGridTextColumn { Header = "Situação", Binding = new Binding(nameof(Linha.Situacao)) { Mode = BindingMode.OneWay }, IsReadOnly = true, Width = 170 });
            if (_modeloIfc != null)
                _grade.Columns.Add(new DataGridTextColumn
                {
                    Header = "No modelo", Binding = new Binding(nameof(Linha.NoModelo)) { Mode = BindingMode.OneWay }, IsReadOnly = true, Width = 110,
                });

            // Filtro em cada título: o ▾ abre a caixa com busca (filtra enquanto digita) e os valores existentes.
            Func<Linha, string>[] valores =
            {
                l => l.PropertySet, l => l.Aplicacao, l => l.Entidades, l => l.Propriedade, l => l.TipoDado, l => l.Parametro, l => l.Situacao,
                l => l.NoModelo,
            };
            for (int i = 0; i < _grade.Columns.Count; i++)
            {
                DataGridColumn col = _grade.Columns[i];
                var f = new FiltroColuna { Titulo = (string)col.Header, Valor = valores[i] };
                f.Botao = new Button
                {
                    Content = "▾", Width = 18, Height = 18, Padding = new Thickness(0), Margin = new Thickness(6, 0, 0, 0),
                    Background = Brushes.White, BorderBrush = Theme.Solid(Theme.CinzaClaro), Focusable = false,
                    ToolTip = $"Filtrar {f.Titulo}", VerticalAlignment = VerticalAlignment.Center,
                };
                f.Botao.Click += (s, e) => AbrirFiltro(f);
                var cabecalho = new DockPanel { LastChildFill = true };
                DockPanel.SetDock(f.Botao, Dock.Right);
                cabecalho.Children.Add(f.Botao);
                cabecalho.Children.Add(new TextBlock { Text = f.Titulo, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                col.Header = cabecalho;
                _filtros.Add(f);
            }
            // O cabeçalho ocupa a largura da coluna para o ▾ ficar no canto direito.
            // Feito no Loaded: o estilo padrão do título só existe depois do Theme.Apply.
            Loaded += (s, e) =>
            {
                var estiloCab = new Style(typeof(DataGridColumnHeader), TryFindResource(typeof(DataGridColumnHeader)) as Style);
                estiloCab.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
                _grade.ColumnHeaderStyle = estiloCab;
            };
            _grade.SelectionChanged += (s, e) => MostrarDetalhe();
            corpo.Children.Add(_grade);

            adicionar.Click += (s, e) =>
            {
                var modelo = _grade.SelectedItem as Linha ?? _linhas.LastOrDefault();
                var nova = new Linha
                {
                    Modelo = _modelo,
                    ModeloIfc = _modeloIfc,
                    PropertySet = modelo?.PropertySet ?? "Pset_MetroSP",
                    Aplicacao = modelo?.Aplicacao ?? Instancia,
                    Entidades = modelo?.Entidades ?? "",
                    Propriedade = "NovaPropriedade",
                    TipoDado = "Text",
                    Parametro = "",
                };
                int i = modelo != null ? _linhas.IndexOf(modelo) + 1 : _linhas.Count;
                _linhas.Insert(i, nova);
                _grade.SelectedItem = nova;
                _grade.ScrollIntoView(nova);
            };
            remover.Click += (s, e) =>
            {
                foreach (Linha l in _grade.SelectedItems.Cast<Linha>().ToList()) _linhas.Remove(l);
            };

            var salvar = new Button { Content = "Salvar", IsDefault = true, Style = Theme.PrimaryButton };
            salvar.Click += (s, e) => { if (Validar()) Salvar(_destino ?? PerguntarDestino(arquivo)); };
            var salvarComo = new Button { Content = "Salvar como..." };
            salvarComo.Click += (s, e) => { if (Validar()) Salvar(PerguntarDestino(arquivo)); };
            var cancelar = new Button { Content = "Cancelar", IsCancel = true };
            var botoes = new List<Button>();
            if (podeRestaurar)
            {
                var restaurar = new Button { Content = "Restaurar padrão" };
                restaurar.Click += (s, e) =>
                {
                    if (MessageBox.Show(this, "Descartar as edições desta classe e voltar ao .txt instalado?", Title,
                            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    Restaurou = true;
                    DialogResult = true;
                };
                botoes.Add(restaurar);
            }
            botoes.AddRange(new[] { cancelar, salvarComo, salvar });

            AtualizarResumo();
            MontarArvore();
            Theme.Apply(this, "Property sets definidos pelo usuário",
                "Confira de qual parâmetro do Revit sai cada propriedade do IFC", corpo, Theme.Buttons(botoes.ToArray()));
        }

        /// <summary>Filtro de uma coluna: texto digitado (contém) e/ou valores marcados na lista.</summary>
        private class FiltroColuna
        {
            public string Titulo;
            public Func<Linha, string> Valor;
            public string Texto = "";
            /// <summary>Valores aceitos; null = todos.</summary>
            public HashSet<string> Valores;
            public Button Botao;
        }

        private const string Vazio = "(vazio)";

        // Filtro dos selos do topo: "ausentes" (parâmetro não encontrado) ou "tipos" (tipo não aceito).
        private string _filtroSelo;

        private void LimparFiltros()
        {
            foreach (FiltroColuna f in _filtros) { f.Texto = ""; f.Valores = null; MarcarBotao(f); }
            _soAusentes.IsChecked = false;
            _filtroSelo = null;
            _arvorePset = _arvoreEntidade = null;
            if (_arvore.Items.Count > 0 && _arvore.Items[0] is TreeViewItem todos) todos.IsSelected = true;
            AtualizarFiltro();
        }
        private readonly List<FiltroColuna> _filtros = new List<FiltroColuna>();

        /// <summary>A linha passa no "só com problema" e nos filtros das colunas (menos, se pedido, o de uma coluna).</summary>
        private bool Passa(Linha l, FiltroColuna exceto)
        {
            if (_soAusentes?.IsChecked == true && !l.Problema) return false;
            if (_filtroSelo == "semvalor" && !l.SemValor) return false;
            if (_arvorePset != null)
            {
                if (!string.Equals(l.PropertySet?.Trim(), _arvorePset, StringComparison.OrdinalIgnoreCase)) return false;
                if (_arvoreEntidade != null && !l.ListaEntidades.Any(x => string.Equals(x, _arvoreEntidade, StringComparison.OrdinalIgnoreCase))) return false;
            }
            if (_filtroSelo == "ausentes" && l.ParametroNoModelo) return false;
            if (_filtroSelo == "tipos" && l.TipoOk) return false;
            foreach (FiltroColuna f in _filtros)
            {
                if (f == exceto) continue;
                string v = f.Valor(l) ?? "";
                if (f.Valores != null && !f.Valores.Contains(v.Length == 0 ? Vazio : v)) return false;
                if (f.Texto.Length > 0 && v.IndexOf(f.Texto, StringComparison.CurrentCultureIgnoreCase) < 0) return false;
            }
            return true;
        }

        private void AtualizarFiltro()
        {
            _grade.CommitEdit(DataGridEditingUnit.Row, true);
            try { _vista.Refresh(); } catch (InvalidOperationException) { /* linha ainda em edição */ }
            AtualizarResumo();
        }

        private static void MarcarBotao(FiltroColuna f)
        {
            bool ativo = f.Valores != null || f.Texto.Length > 0;
            f.Botao.Background = ativo ? Theme.Solid(Theme.LaranjaClaro) : Brushes.White;
            f.Botao.FontWeight = ativo ? FontWeights.Bold : FontWeights.Normal;
        }

        /// <summary>
        /// Caixa do filtro, aberta pelo ▾ do título: o texto digitado filtra a tabela e a lista na hora;
        /// as caixas de marcar escolhem valores exatos (como no Excel) e valem ao clicar OK.
        /// </summary>
        private void AbrirFiltro(FiltroColuna f)
        {
            _grade.CommitEdit(DataGridEditingUnit.Row, true);
            var contagem = new Dictionary<string, int>();
            foreach (Linha l in _linhas)
            {
                if (!Passa(l, f)) continue;
                string v = f.Valor(l) ?? "";
                if (v.Length == 0) v = Vazio;
                contagem[v] = contagem.TryGetValue(v, out int n) ? n + 1 : 1;
            }
            List<string> valores = contagem.Keys.OrderBy(v => v == Vazio ? 0 : 1).ThenBy(v => v, StringComparer.CurrentCultureIgnoreCase).ToList();

            var caixas = new List<CheckBox>();
            var lista = new StackPanel();
            foreach (string v in valores)
            {
                var c = new CheckBox { Content = $"{v}   ({contagem[v]})", Tag = v, IsChecked = f.Valores == null || f.Valores.Contains(v), Margin = new Thickness(0, 1, 0, 1) };
                caixas.Add(c);
                lista.Children.Add(c);
            }
            var tudo = new CheckBox { Content = "(Selecionar tudo)", IsChecked = caixas.All(c => c.IsChecked == true), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 4) };
            tudo.Click += (s, e) => { foreach (CheckBox c in caixas.Where(c => c.Visibility == Visibility.Visible)) c.IsChecked = tudo.IsChecked == true; };

            var busca = new TextBox { Text = f.Texto, ToolTip = "Digite para filtrar (contém o texto)" };
            void Mostrar()
            {
                string t = busca.Text.Trim();
                foreach (CheckBox c in caixas)
                    c.Visibility = t.Length == 0 || ((string)c.Tag).IndexOf(t, StringComparison.CurrentCultureIgnoreCase) >= 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            busca.TextChanged += (s, e) =>
            {
                f.Texto = busca.Text.Trim();
                Mostrar();
                MarcarBotao(f);
                AtualizarFiltro();
            };
            Mostrar();

            var popup = new Popup { PlacementTarget = f.Botao, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
            var ok = new Button { Content = "OK", Style = Theme.PrimaryButton, MinWidth = 60, IsDefault = true };
            var limpar = new Button { Content = "Limpar filtro", MinWidth = 60 };
            ok.Click += (s, e) =>
            {
                List<string> marcados = caixas.Where(c => c.IsChecked == true && c.Visibility == Visibility.Visible).Select(c => (string)c.Tag).ToList();
                // Marcou tudo o que aparece: vale só o texto digitado.
                f.Valores = marcados.Count == caixas.Count(c => c.Visibility == Visibility.Visible) ? null : new HashSet<string>(marcados);
                MarcarBotao(f);
                popup.IsOpen = false;
                AtualizarFiltro();
            };
            limpar.Click += (s, e) =>
            {
                f.Valores = null;
                f.Texto = "";
                MarcarBotao(f);
                popup.IsOpen = false;
                AtualizarFiltro();
            };

            var corpo = new DockPanel { Width = 300 };
            var titulo = new TextBlock { Text = "Filtrar " + f.Titulo, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
            StackPanel botoes = Theme.Buttons(limpar, ok);
            botoes.Margin = new Thickness(0, 8, 0, 0);
            foreach (UIElement el in new UIElement[] { titulo, busca, tudo })
            {
                DockPanel.SetDock(el, Dock.Top);
                corpo.Children.Add(el);
            }
            DockPanel.SetDock(botoes, Dock.Bottom);
            corpo.Children.Add(botoes);
            corpo.Children.Add(new ScrollViewer { Content = lista, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            popup.Child = new Border { Background = Brushes.White, BorderBrush = Theme.Solid(Theme.Cinza), BorderThickness = new Thickness(1), Padding = new Thickness(10), Child = corpo };
            popup.KeyDown += (s, e) => { if (e.Key == Key.Escape) popup.IsOpen = false; };
            popup.Opened += (s, e) => { busca.Focus(); busca.CaretIndex = busca.Text.Length; };
            popup.IsOpen = true;
        }

        private void AtualizarResumo()
        {
            int ausentes = _linhas.Count(l => !l.ParametroNoModelo);
            int tipos = _linhas.Count(l => !l.TipoOk);
            int psets = _linhas.Select(l => l.PropertySet).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            int visiveis = _vista?.Count ?? _linhas.Count;
            FiltroColuna filtroPset = _filtros.FirstOrDefault(f => f.Titulo == "Property set");
            _resumo.Children.Clear();

            // Os selos funcionam como filtro: o ativo fica escuro e um novo clique desliga.
            _resumo.Children.Add(Selo($"{_linhas.Count} propriedades", visiveis == _linhas.Count, false,
                "Mostrar todas as propriedades (limpa os filtros)", LimparFiltros));
            if (visiveis != _linhas.Count)
                _resumo.Children.Add(Selo($"mostrando {visiveis} com os filtros", true, false, "Limpar os filtros", LimparFiltros));
            _resumo.Children.Add(Selo($"{psets} property sets", filtroPset?.Valores != null, false,
                "Escolher quais property sets mostrar", () => { if (filtroPset != null) AbrirFiltro(filtroPset); }));
            if (ausentes == 0)
                _resumo.Children.Add(Theme.Chip("todos os parâmetros encontrados no modelo"));
            else
                _resumo.Children.Add(Selo($"{ausentes} parâmetro(s) não encontrados no modelo (ficam fora do IFC)", _filtroSelo == "ausentes", true,
                    "Mostrar só as propriedades cujo parâmetro não existe no modelo", () => AlternarSelo("ausentes")));
            if (tipos > 0)
                _resumo.Children.Add(Selo($"{tipos} tipo(s) de dado não aceitos pelo exportador", _filtroSelo == "tipos", true,
                    "Mostrar só as propriedades com tipo de dado não aceito", () => AlternarSelo("tipos")));
            if (_modeloIfc != null)
            {
                int semValor = _linhas.Count(l => l.SemValor);
                if (semValor > 0)
                    _resumo.Children.Add(Selo($"{semValor} sem valor em algum elemento", _filtroSelo == "semvalor", true,
                        "Mostrar só as propriedades cujo parâmetro existe, mas está vazio em parte dos elementos", () => AlternarSelo("semvalor")));
            }
        }

        private bool _montandoArvore;

        /// <summary>Monta a árvore property set › entidades, mantendo o nó escolhido.</summary>
        private void MontarArvore()
        {
            _montandoArvore = true;
            try
            {
                _arvore.Items.Clear();
                TreeViewItem No(string texto, int total, int problemas, bool negrito, Tuple<string, string> chave)
                {
                    var d = new DockPanel { MinWidth = 150 };
                    var n = new TextBlock
                    {
                        Text = problemas > 0 ? problemas.ToString() : total.ToString(), FontSize = 10.5, Margin = new Thickness(8, 0, 0, 0),
                        Foreground = Theme.Solid(problemas > 0 ? Theme.LaranjaTexto : Theme.Cinza), FontWeight = problemas > 0 ? FontWeights.Bold : FontWeights.Normal,
                        ToolTip = problemas > 0 ? $"{problemas} de {total} com problema" : $"{total} propriedades",
                    };
                    DockPanel.SetDock(n, Dock.Right);
                    d.Children.Add(n);
                    d.Children.Add(new TextBlock { Text = texto, FontWeight = negrito ? FontWeights.SemiBold : FontWeights.Normal });
                    var item = new TreeViewItem { Header = d, Tag = chave, IsExpanded = false };
                    if ((chave == null && _arvorePset == null) ||
                        (chave != null && string.Equals(chave.Item1, _arvorePset, StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(chave.Item2, _arvoreEntidade, StringComparison.OrdinalIgnoreCase)))
                        item.IsSelected = true;
                    return item;
                }
                _arvore.Items.Add(No("Todos", _linhas.Count, _linhas.Count(l => l.Problema), true, null));
                foreach (var g in _linhas.Where(l => !string.IsNullOrWhiteSpace(l.PropertySet)).GroupBy(l => l.PropertySet.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    TreeViewItem pset = No(g.Key, g.Count(), g.Count(l => l.Problema), true, Tuple.Create(g.Key, (string)null));
                    pset.IsExpanded = string.Equals(g.Key, _arvorePset, StringComparison.OrdinalIgnoreCase);
                    foreach (var e in g.SelectMany(l => l.ListaEntidades.Select(x => (Entidade: x, Linha: l)))
                                       .GroupBy(x => x.Entidade, StringComparer.OrdinalIgnoreCase)
                                       .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                        pset.Items.Add(No(e.Key, e.Count(), e.Count(x => x.Linha.Problema), false, Tuple.Create(g.Key, e.Key)));
                    _arvore.Items.Add(pset);
                }
            }
            finally { _montandoArvore = false; }
        }

        /// <summary>Painel embaixo da grade para a linha escolhida com problema: o motivo e parâmetros parecidos do modelo.</summary>
        private void MostrarDetalhe()
        {
            var l = _grade.SelectedItems.Count == 1 ? _grade.SelectedItem as Linha : null;
            if (l == null || !l.Problema)
            {
                _detalhe.Visibility = Visibility.Collapsed;
                return;
            }
            var p = new StackPanel();
            p.Children.Add(new TextBlock { Text = $"{l.Propriedade} · {l.PropertySet} · {string.Join(", ", l.ListaEntidades.DefaultIfEmpty("todas as entidades"))}", FontWeight = FontWeights.SemiBold });
            string motivo;
            if (!l.TipoOk) motivo = $"O tipo \"{l.TipoDado}\" não é aceito pelo exportador da Autodesk" + (ExportMapping.TipoCorreto(l.TipoDado) is string certo ? $" (use {certo})." : ".");
            else if (string.IsNullOrWhiteSpace(l.Parametro)) motivo = "A propriedade não indica parâmetro do Revit.";
            else if (!l.ParametroNoModelo) motivo = $"O parâmetro \"{l.Parametro}\" não existe neste modelo, então a propriedade sai vazia no IFC.";
            else motivo = $"O parâmetro \"{l.Parametro}\" tem valor em {l.Contagem.Value.ComValor} de {l.Contagem.Value.Elementos} elementos; nos outros a propriedade sai vazia.";
            p.Children.Add(new TextBlock { Text = motivo, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });

            if (_modeloIfc != null && l.TipoOk)
            {
                List<(string Parametro, ModeloIfc.Contagem Contagem)> sugestoes =
                    _modeloIfc.Sugestoes(l.ListaEntidades, l.Aplicacao == Tipo, l.Parametro, l.Propriedade);
                var linha = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                if (sugestoes.Count == 0)
                    linha.Children.Add(Theme.Detalhe("Nenhum parâmetro com nome parecido e com valor nesses elementos. Escolha outro na coluna Parâmetro do Revit."));
                else
                {
                    linha.Children.Add(new TextBlock { Text = "Parecidos no modelo:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                    foreach (var (nome, c) in sugestoes)
                    {
                        var b = new Button
                        {
                            Content = $"{nome}  ·  {c.ComValor} de {c.Elementos}", Margin = new Thickness(0, 0, 6, 0), MinWidth = 0,
                            ToolTip = $"Usar \"{nome}\" nesta linha",
                        };
                        string escolhido = nome;
                        b.Click += (s, e) => { l.Parametro = escolhido; AtualizarFiltro(); MontarArvore(); MostrarDetalhe(); };
                        linha.Children.Add(b);
                    }
                }
                p.Children.Add(linha);
                string antigo = l.Parametro?.Trim();
                int iguais = string.IsNullOrEmpty(antigo) ? 0 : _linhas.Count(x => x != l && string.Equals(x.Parametro?.Trim(), antigo, StringComparison.OrdinalIgnoreCase));
                if (iguais > 0 && sugestoes.Count > 0)
                    p.Children.Add(Theme.Detalhe($"Outras {iguais} linha(s) também usam \"{antigo}\". Para trocar em todas, selecione as linhas e edite a coluna, ou troque uma a uma."));
            }
            _detalhe.Child = p;
            _detalhe.Visibility = Visibility.Visible;
        }

        private void AlternarSelo(string filtro)
        {
            _filtroSelo = _filtroSelo == filtro ? null : filtro;
            AtualizarFiltro();
        }

        /// <summary>Selo clicável: escuro quando o filtro dele está ativo.</summary>
        private static Border Selo(string texto, bool ativo, bool destaque, string dica, Action clique)
        {
            Border b = Theme.Chip(texto, destaque: destaque && !ativo, escuro: ativo);
            b.Cursor = System.Windows.Input.Cursors.Hand;
            b.ToolTip = dica;
            b.MouseLeftButtonUp += (s, e) => { e.Handled = true; clique(); };
            return b;
        }

        private string PerguntarDestino(string atual)
        {
            var d = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Texto (*.txt)|*.txt",
                Title = "Salvar property sets",
                FileName = Path.GetFileName(atual),
            };
            string pasta = Path.GetDirectoryName(atual);
            if (Directory.Exists(pasta)) d.InitialDirectory = pasta;
            return d.ShowDialog(this) == true ? d.FileName : null;
        }

        /// <summary>
        /// Confere as linhas pelas regras do exportador IFC da Autodesk antes de salvar. Erros que fariam o exportador
        /// ignorar propriedades impedem salvar; tipos com grafia conhecida podem ser corrigidos na hora.
        /// </summary>
        private bool Validar()
        {
            _grade.CommitEdit(DataGridEditingUnit.Row, true);
            foreach (Linha l in _linhas)
            {
                // Tab separa colunas no .txt: dentro de um campo quebraria a linha.
                l.PropertySet = l.PropertySet?.Replace('\t', ' ').Trim();
                l.Propriedade = l.Propriedade?.Replace('\t', ' ').Trim();
                l.Parametro = l.Parametro?.Replace('\t', ' ').Trim();
                l.Entidades = l.Entidades?.Replace('\t', ',').Trim();
            }

            Linha bloqueia = _linhas.FirstOrDefault(l => string.IsNullOrWhiteSpace(l.PropertySet) || string.IsNullOrWhiteSpace(l.Propriedade));
            string motivo = bloqueia != null ? "Toda propriedade precisa de property set e nome." : null;
            if (bloqueia == null)
            {
                bloqueia = _linhas.FirstOrDefault(l => Entidades(l).Count == 0);
                if (bloqueia != null)
                    motivo = $"O property set {bloqueia.PropertySet} não tem entidades IFC. Sem elas o exportador da Autodesk ignora o cabeçalho " +
                             "e as propriedades vão para o property set anterior. Indique ao menos uma (ex.: IfcWall ou IfcElement).";
            }
            if (bloqueia != null)
            {
                MessageBox.Show(this, motivo, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                _grade.SelectedItem = bloqueia;
                _grade.ScrollIntoView(bloqueia);
                return false;
            }

            List<Linha> tipos = _linhas.Where(l => !l.TipoOk).ToList();
            if (tipos.Count > 0)
            {
                List<Linha> corrigiveis = tipos.Where(l => ExportMapping.TipoCorreto(l.TipoDado) != null).ToList();
                string lista = string.Join("\n", tipos.Take(8).Select(l => $"  {l.PropertySet} › {l.Propriedade}: \"{l.TipoDado}\"" +
                    (ExportMapping.TipoCorreto(l.TipoDado) is string c ? $" → {c}" : " (sem sugestão)"))) + (tipos.Count > 8 ? "\n  ..." : "");
                string texto = $"{tipos.Count} propriedade(s) com tipo de dado que o exportador da Autodesk não reconhece (sairiam como Text):\n\n{lista}\n\n" +
                               (corrigiveis.Count > 0 ? "Sim: corrigir as que têm sugestão e salvar.\nNão: salvar como está.\nCancelar: voltar para editar."
                                                      : "Salvar assim mesmo?");
                MessageBoxResult r = MessageBox.Show(this, texto, Title,
                    corrigiveis.Count > 0 ? MessageBoxButton.YesNoCancel : MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (r == MessageBoxResult.Cancel) { _grade.SelectedItem = tipos[0]; _grade.ScrollIntoView(tipos[0]); return false; }
                if (r == MessageBoxResult.Yes) foreach (Linha l in corrigiveis) l.TipoDado = ExportMapping.TipoCorreto(l.TipoDado);
            }

            var avisos = new List<string>();
            foreach (string pset in _linhas.Select(l => l.PropertySet).Distinct(StringComparer.OrdinalIgnoreCase)
                         .Where(p => p.StartsWith("Pset_", StringComparison.OrdinalIgnoreCase)))
                avisos.Add($"\"{pset}\": nomes com Pset_ são reservados aos property sets padrão do IFC; o exportador renomeia os outros.");
            foreach (string e in _linhas.SelectMany(Entidades).Distinct(StringComparer.OrdinalIgnoreCase)
                         .Where(e => !e.StartsWith("Ifc", StringComparison.OrdinalIgnoreCase)))
                avisos.Add($"\"{e}\" não parece uma entidade IFC (deve começar com Ifc).");
            if (avisos.Count > 0 &&
                MessageBox.Show(this, string.Join("\n", avisos.Take(8)) + "\n\nSalvar assim mesmo?", Title,
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return false;
            return true;
        }

        private static List<string> Entidades(Linha l) =>
            (l.Entidades ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(e => e.Trim()).ToList();

        private void Salvar(string destino)
        {
            if (destino == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destino));
                // A versão anterior de uma cópia do usuário fica guardada (Arquivos IFC › Anteriores).
                if (destino.StartsWith(ClassesIfc.PastaUsuario, StringComparison.OrdinalIgnoreCase)) ArquivosIfc.GuardarAnterior(destino);
                File.WriteAllText(destino, Gerar(_linhas, _cabecalho), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível salvar:\n" + ex.Message, Title);
                return;
            }
            ArquivoSalvo = destino;
            DialogResult = true;
        }

        /// <summary>Texto no formato do exportador: um cabeçalho PropertySet por grupo seguido das propriedades.</summary>
        internal static string Gerar(IEnumerable<Linha> linhas, IEnumerable<string> comentarios = null)
        {
            var sb = new StringBuilder();
            foreach (string l in comentarios ?? Enumerable.Empty<string>()) sb.AppendLine(l);
            sb.AppendLine("# Property sets definidos pelo usuário - salvo pelo MetroSP em " + DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
            string atual = null;
            foreach (Linha l in linhas)
            {
                string it = l.Aplicacao == Tipo ? "T" : "I";
                string entidades = string.Join(",", Entidades(l));
                string cabecalho = $"PropertySet:\t{l.PropertySet.Trim()}\t{it}\t{entidades}".TrimEnd('\t');
                if (cabecalho != atual)
                {
                    sb.AppendLine(cabecalho);
                    atual = cabecalho;
                }
                string tipo = string.IsNullOrWhiteSpace(l.TipoDado) ? "Text" : l.TipoDado.Trim();
                string parametro = string.IsNullOrWhiteSpace(l.Parametro) ? l.Propriedade.Trim() : l.Parametro.Trim();
                sb.AppendLine($"\t{l.Propriedade.Trim()}\t{tipo}\t{parametro}");
            }
            return sb.ToString();
        }

        private static DataGridTextColumn Texto(string titulo, string campo, double largura) =>
            new DataGridTextColumn { Header = titulo, Binding = new Binding(campo) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = largura };

        private static DataGridComboBoxColumn Lista(string titulo, string campo, IEnumerable<string> valores, double largura) =>
            new DataGridComboBoxColumn { Header = titulo, ItemsSource = valores.ToList(), SelectedItemBinding = new Binding(campo), Width = largura };

        /// <summary>Coluna com caixa editável e sugestões (aceita também o que não está na lista, para mostrar o que veio do .txt).</summary>
        private static DataGridTemplateColumn ComboEditavel(string titulo, string campo, List<string> sugestoes, DataGridLength largura, double minimo)
        {
            var leitura = new FrameworkElementFactory(typeof(TextBlock));
            leitura.SetBinding(TextBlock.TextProperty, new Binding(campo));
            leitura.SetValue(TextBlock.PaddingProperty, new Thickness(2, 0, 2, 0));

            var edicao = new FrameworkElementFactory(typeof(ComboBox));
            edicao.SetValue(ComboBox.IsEditableProperty, true);
            edicao.SetValue(ComboBox.IsTextSearchEnabledProperty, true);
            edicao.SetValue(ItemsControl.ItemsSourceProperty, sugestoes);
            edicao.SetBinding(ComboBox.TextProperty, new Binding(campo) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });

            return new DataGridTemplateColumn
            {
                Header = titulo,
                Width = largura,
                MinWidth = minimo,
                CellTemplate = new DataTemplate { VisualTree = leitura },
                CellEditingTemplate = new DataTemplate { VisualTree = edicao },
            };
        }
    }
}
