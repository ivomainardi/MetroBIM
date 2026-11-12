using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Autodesk.Revit.DB;
using MetroSP.Ui;
using Grid = System.Windows.Controls.Grid;

namespace MetroSP.Parametros
{
    /// <summary>
    /// Copiar valores de um parâmetro para outro: escolher escopo, origem, destino e conversão,
    /// analisar (por elemento, impactos e propostas) e só então gravar.
    /// A janela é modal, então as chamadas à API acontecem no contexto do comando.
    /// </summary>
    public class CopiarValoresWindow : Window
    {
        private readonly Autodesk.Revit.UI.UIDocument _uidoc;
        private readonly Document _doc;
        private readonly ICollection<ElementId> _selecao;

        private readonly ComboBox _escopo;
        private readonly ComboBox _categoria;
        private readonly ComboBox _origem;
        private readonly ComboBox _destino;
        private readonly CheckBox _incluirRevit;
        private readonly ComboBox _unidade;
        private readonly CheckBox _comUnidade;
        private readonly CheckBox _arredondar;
        private readonly RadioButton _soVazios;
        private readonly RadioButton _sobrescrever;
        private readonly StackPanel _relatorio;
        private readonly WrapPanel _resumo;
        private readonly DockPanel _areaTabela;
        private readonly TextBlock _rodapeInfo;
        private readonly Button _copiar;
        private readonly Button _reanalisar;
        private readonly Button _exportar;
        private readonly Button _selecionar;

        private List<Element> _elementos = new List<Element>();
        private List<ParamRef> _candidatos = new List<ParamRef>();
        private AnaliseCopia _analise;
        private FilterableGrid _tabela;
        private bool _carregando;

        public CopiarValoresWindow(Autodesk.Revit.UI.UIDocument uidoc)
        {
            _uidoc = uidoc;
            _doc = uidoc.Document;
            ICollection<ElementId> sel = uidoc.Selection.GetElementIds();
            _selecao = sel.Count > 0 ? sel : null;

            Title = "MetroSP - Copiar valores entre parâmetros";
            Width = 1200;
            Height = 760;
            MinWidth = 820;
            MinHeight = 520;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // ---- opções
            _escopo = new ComboBox { Padding = new Thickness(4, 3, 4, 3) };
            if (_selecao != null) _escopo.Items.Add($"Seleção atual ({_selecao.Count} elementos)");
            _escopo.Items.Add("Vista atual");
            _escopo.Items.Add("Todo o modelo");
            _escopo.SelectedIndex = 0;
            _categoria = new ComboBox { Padding = new Thickness(4, 3, 4, 3) };
            _origem = ComboParametro();
            _destino = ComboParametro();
            _incluirRevit = new CheckBox { Content = "Incluir parâmetros do Revit (Comentários, Marca...)", VerticalAlignment = VerticalAlignment.Center };
            _unidade = new ComboBox { Padding = new Thickness(4, 3, 4, 3), MinWidth = 160, IsEnabled = false };
            _comUnidade = new CheckBox { Content = "Texto com unidade", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            _arredondar = new CheckBox { Content = "Arredondar decimais", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            _soVazios = new RadioButton { Content = "Só preencher vazios", GroupName = "sobrescrever", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            _sobrescrever = new RadioButton { Content = "Sobrescrever", GroupName = "sobrescrever", VerticalAlignment = VerticalAlignment.Center };

            var form = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Linha(form, 0, "Escopo", _escopo, "Categoria", _categoria);
            Linha(form, 1, "Copiar de", _origem, "Para", _destino);
            var conversao = new StackPanel { Orientation = Orientation.Horizontal };
            conversao.Children.Add(new TextBlock { Text = "Unidade:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            _unidade.Margin = new Thickness(0, 0, 16, 0);
            conversao.Children.Add(_unidade);
            conversao.Children.Add(_comUnidade);
            conversao.Children.Add(_arredondar);
            var sobre = new StackPanel { Orientation = Orientation.Horizontal };
            sobre.Children.Add(_soVazios);
            sobre.Children.Add(_sobrescrever);
            Linha(form, 2, "Conversão", conversao, "Valores existentes", sobre);
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(_incluirRevit, 3);
            Grid.SetColumn(_incluirRevit, 1);
            _incluirRevit.Margin = new Thickness(0, 6, 0, 0);
            form.Children.Add(_incluirRevit);

            var analisar = new Button { Content = "Analisar", Style = Theme.PrimaryButton, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) };
            analisar.Click += (s, e) => Analisar();

            // ---- análise
            _resumo = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            _relatorio = new StackPanel();
            var caixaRelatorio = new Border
            {
                Background = Theme.Solid(Theme.CinzaFundo),
                BorderBrush = Theme.Solid(Theme.CinzaClaro),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 8),
                Child = new ScrollViewer { Content = _relatorio, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            };
            _areaTabela = new DockPanel();
            _relatorio.Children.Add(new TextBlock
            {
                Text = "Escolha a origem e o destino e clique em \"Analisar\". Nada é alterado até você clicar em \"Copiar\".",
                Foreground = Theme.Solid(Theme.Cinza),
            });

            var corpo = new DockPanel();
            foreach (UIElement el in new UIElement[] { form, analisar, _resumo, caixaRelatorio })
            {
                DockPanel.SetDock(el, Dock.Top);
                corpo.Children.Add(el);
            }
            corpo.Children.Add(_areaTabela);

            // ---- rodapé
            _rodapeInfo = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center };
            _exportar = new Button { Content = "Exportar análise (.csv)", IsEnabled = false };
            _selecionar = new Button { Content = "Selecionar no modelo", IsEnabled = false, ToolTip = "Seleciona no Revit os elementos visíveis na tabela." };
            _copiar = new Button { Content = "Copiar", Style = Theme.PrimaryButton, IsEnabled = false };
            _reanalisar = new Button { Content = "Reanalisar", IsEnabled = false, ToolTip = "Lê o modelo de novo com as mesmas opções, mantendo os filtros da tabela." };
            _reanalisar.Click += (s, e) => Analisar();
            var fechar = new Button { Content = "Fechar", IsCancel = true };
            _exportar.Click += (s, e) => Exportar();
            _selecionar.Click += (s, e) => Selecionar();
            _copiar.Click += (s, e) => Copiar();
            fechar.Click += (s, e) => Close();
            var rodape = new DockPanel();
            StackPanel botoes = Theme.Buttons(_reanalisar, _exportar, _selecionar, _copiar, fechar);
            DockPanel.SetDock(botoes, Dock.Right);
            rodape.Children.Add(botoes);
            rodape.Children.Add(_rodapeInfo);

            Theme.Apply(this, "Copiar valores entre parâmetros", _doc.Title + " · analisa antes de gravar, inclusive entre tipos diferentes", corpo, rodape);

            _escopo.SelectionChanged += (s, e) => CarregarEscopo();
            _categoria.SelectionChanged += (s, e) => { if (!_carregando) CarregarCandidatos(); };
            _incluirRevit.Click += (s, e) => CarregarCandidatos();
            _origem.SelectionChanged += (s, e) => MudouParametros();
            _destino.SelectionChanged += (s, e) => MudouParametros();
            _unidade.SelectionChanged += (s, e) => Invalidar();
            _comUnidade.Click += (s, e) => Invalidar();
            _arredondar.Click += (s, e) => Invalidar();
            _soVazios.Checked += (s, e) => Invalidar();
            _sobrescrever.Checked += (s, e) => Invalidar();

            CarregarEscopo();
        }

        private static ComboBox ComboParametro()
        {
            var c = new ComboBox
            {
                IsEditable = true,
                IsTextSearchEnabled = true,
                Padding = new Thickness(4, 3, 4, 3),
                ToolTip = "Digite para procurar pelo nome.",
            };
            TextSearch.SetTextPath(c, nameof(ParamRef.Nome));
            return c;
        }

        private static void Linha(Grid g, int linha, string r1, UIElement c1, string r2, UIElement c2)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            void Add(FrameworkElement el, int col, double direita)
            {
                el.Margin = new Thickness(0, 3, direita, 3);
                Grid.SetRow(el, linha);
                Grid.SetColumn(el, col);
                g.Children.Add(el);
            }
            Add(new TextBlock { Text = r1, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }, 0, 10);
            Add((FrameworkElement)c1, 1, 0);
            Add(new TextBlock { Text = r2, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }, 3, 10);
            Add((FrameworkElement)c2, 4, 0);
        }

        // ------------------------------------------------------------------ escopo e parâmetros

        private void CarregarEscopo()
        {
            Cursor = Cursors.Wait;
            try
            {
                string esc = _escopo.SelectedItem as string ?? "";
                _elementos = esc.StartsWith("Seleção") ? ValueCopier.Elementos(_doc, _selecao, null)
                    : esc == "Vista atual" ? ValueCopier.Elementos(_doc, null, _doc.ActiveView.Id)
                    : ValueCopier.Elementos(_doc, null, null);

                _carregando = true;
                string anterior = _categoria.SelectedItem as string;
                _categoria.Items.Clear();
                _categoria.Items.Add($"Todas ({_elementos.Count} elementos)");
                foreach (var g in _elementos.GroupBy(e => e.Category.Name).OrderBy(g => g.Key))
                    _categoria.Items.Add($"{g.Key} ({g.Count()})");
                _categoria.SelectedItem = _categoria.Items.Cast<string>().FirstOrDefault(i => anterior != null && Nome(i) == Nome(anterior)) ?? _categoria.Items[0];
                _carregando = false;
                CarregarCandidatos();
            }
            finally { Cursor = null; }
        }

        /// <summary>"Paredes (120)" → "Paredes".</summary>
        private static string Nome(string itemCategoria) =>
            itemCategoria.Contains(" (") ? itemCategoria.Substring(0, itemCategoria.LastIndexOf(" (", StringComparison.Ordinal)) : itemCategoria;

        private List<Element> NoEscopo()
        {
            if (_categoria.SelectedIndex <= 0) return _elementos;
            string cat = Nome((string)_categoria.SelectedItem);
            return _elementos.Where(e => e.Category.Name == cat).ToList();
        }

        private void CarregarCandidatos()
        {
            Cursor = Cursors.Wait;
            try
            {
                string o = (_origem.SelectedItem as ParamRef)?.Chave;
                string d = (_destino.SelectedItem as ParamRef)?.Chave;
                _candidatos = ValueCopier.Candidatos(_doc, NoEscopo())
                    .Where(r => _incluirRevit.IsChecked == true || !r.EhDoRevit)
                    .ToList();
                _origem.ItemsSource = _candidatos;
                _destino.ItemsSource = _candidatos.Where(r => r.Editavel && r.Storage != StorageType.ElementId).ToList();
                _origem.SelectedItem = _candidatos.FirstOrDefault(r => r.Chave == o);
                _destino.SelectedItem = ((List<ParamRef>)_destino.ItemsSource).FirstOrDefault(r => r.Chave == d);
                MudouParametros();
            }
            finally { Cursor = null; }
        }

        private ParamRef Origem => _origem.SelectedItem as ParamRef;
        private ParamRef Destino => _destino.SelectedItem as ParamRef;

        private void MudouParametros()
        {
            // Unidade: só quando um lado é medida e o outro é número/texto.
            ForgeTypeId spec = ValueCopier.SpecDaUnidade(Origem, Destino);
            _unidade.Items.Clear();
            if (spec != null)
            {
                ForgeTypeId padrao = _doc.GetUnits().GetFormatOptions(spec).GetUnitTypeId();
                foreach (ForgeTypeId u in UnitUtils.GetValidUnits(spec))
                {
                    var item = new ComboBoxItem { Content = ValueCopier.Rotulo(u), Tag = u };
                    _unidade.Items.Add(item);
                    if (u.TypeId == padrao.TypeId) _unidade.SelectedItem = item;
                }
            }
            _unidade.IsEnabled = spec != null;
            _comUnidade.IsEnabled = Destino?.Storage == StorageType.String && Origem?.Medida == true;
            _arredondar.IsEnabled = Destino?.Storage == StorageType.Integer && Destino?.SimNao == false;
            Invalidar();
        }

        private OpcoesCopia Opcoes() => new OpcoesCopia
        {
            Unidade = _unidade.IsEnabled ? (_unidade.SelectedItem as ComboBoxItem)?.Tag as ForgeTypeId : null,
            TextoComUnidade = _comUnidade.IsChecked == true,
            Arredondar = _arredondar.IsChecked == true,
            Sobrescrever = _sobrescrever.IsChecked == true,
        };

        private void Invalidar()
        {
            if (_copiar == null) return;
            _analise = null;
            _copiar.IsEnabled = false;
            _copiar.Content = "Copiar";
            if (_tabela != null) _tabela.Opacity = 0.5;
        }

        // ------------------------------------------------------------------ análise

        private void Analisar()
        {
            if (Origem == null || Destino == null)
            {
                MessageBox.Show(this, "Escolha o parâmetro de origem e o de destino.", Title);
                return;
            }
            if (Origem.Chave == Destino.Chave)
            {
                MessageBox.Show(this, "Origem e destino são o mesmo parâmetro.", Title);
                return;
            }

            Cursor = Cursors.Wait;
            try
            {
                List<Element> elems = NoEscopo();
                _analise = ValueCopier.Analisar(_doc, elems, Origem, Destino, Opcoes());
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível analisar:\n" + ex.Message, Title);
                return;
            }
            finally { Cursor = null; }

            MostrarAnalise();
        }

        private void MostrarAnalise()
        {
            AnaliseCopia a = _analise;
            _resumo.Children.Clear();
            _resumo.Children.Add(Theme.Chip($"{a.Conta(ValueCopier.Copia)} recebem valor", destaque: a.Conta(ValueCopier.Copia) > 0));
            _resumo.Children.Add(Theme.Chip($"{a.Conta(ValueCopier.Sobrescreve)} sobrescrevem", escuro: a.Conta(ValueCopier.Sobrescreve) > 0));
            _resumo.Children.Add(Theme.Chip($"{a.Conta(ValueCopier.JaIgual)} já iguais"));
            _resumo.Children.Add(Theme.Chip($"{a.Conta(ValueCopier.Mantido)} mantidos"));
            _resumo.Children.Add(Theme.Chip($"{a.Conta(ValueCopier.NaoConverte)} não convertem", escuro: a.Conta(ValueCopier.NaoConverte) > 0));
            int outros = a.Linhas.Count(l => l.Situacao == ValueCopier.SomenteLeitura || l.Situacao == ValueCopier.SemDestino ||
                                             l.Situacao == ValueCopier.Bloqueado || l.Situacao == ValueCopier.ConflitoTipo);
            _resumo.Children.Add(Theme.Chip($"{outros} impedidos", destaque: outros > 0));
            _resumo.Children.Add(Theme.Chip($"{a.Conta(ValueCopier.OrigemVazia) + a.Conta(ValueCopier.SemOrigem)} sem valor de origem"));

            _relatorio.Children.Clear();
            Secao("Impactos", a.Impactos);
            Secao("Propostas", a.Propostas);

            var estado = _tabela?.Estado();
            if (_tabela != null) _areaTabela.Children.Remove(_tabela);
            _tabela = new FilterableGrid(a.Linhas, typeof(LinhaCopia));
            _tabela.AddColumn("Situação", nameof(LinhaCopia.Situacao), 150);
            _tabela.AddColumn("Elemento", nameof(LinhaCopia.Elemento), 230);
            _tabela.AddColumn("Id", nameof(LinhaCopia.Id), 80);
            _tabela.AddColumn("Categoria", nameof(LinhaCopia.Categoria), 120);
            _tabela.AddColumn("Valor de origem", nameof(LinhaCopia.ValorOrigem), 150);
            _tabela.AddColumn("Destino atual", nameof(LinhaCopia.ValorAtual), 130);
            _tabela.AddColumn("Valor novo", nameof(LinhaCopia.ValorNovo), 130);
            _tabela.AddColumn("Motivo", nameof(LinhaCopia.Motivo), 150, fill: true);
            _tabela.Grid.LoadingRow += (s, e) =>
            {
                var l = (LinhaCopia)e.Row.Item;
                e.Row.Background = l.Situacao == ValueCopier.Sobrescreve || l.Situacao == ValueCopier.NaoConverte || l.Situacao == ValueCopier.ConflitoTipo
                    ? Theme.LinhaProblema
                    : l.Situacao == ValueCopier.Copia ? Theme.LinhaNormal : Theme.LinhaAviso;
            };
            _tabela.Filtered += AtualizarRodape;
            Theme.StyleSelection(_tabela.Grid);
            _areaTabela.Children.Add(_tabela);
            _tabela.Restaurar(estado);

            int gravar = a.Gravacoes.Count;
            _copiar.IsEnabled = gravar > 0;
            _copiar.Content = gravar == 0 ? "Nada a copiar" : Destino.DoTipo ? $"Copiar em {gravar} tipos" : $"Copiar {gravar} valores";
            _exportar.IsEnabled = _selecionar.IsEnabled = _reanalisar.IsEnabled = true;
            AtualizarRodape();
        }

        private void Secao(string titulo, List<string> itens)
        {
            if (itens.Count == 0) return;
            _relatorio.Children.Add(new TextBlock { Text = titulo, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, _relatorio.Children.Count > 0 ? 6 : 0, 0, 2) });
            foreach (string i in itens)
                _relatorio.Children.Add(new TextBlock { Text = "•  " + i, TextWrapping = TextWrapping.Wrap });
        }

        private void AtualizarRodape()
        {
            if (_tabela == null) return;
            int v = _tabela.VisibleItems<LinhaCopia>().Count();
            _rodapeInfo.Text = v == _analise?.Linhas.Count ? $"{v} elementos" : $"{v} de {_analise?.Linhas.Count} visíveis (filtro ativo)";
        }

        // ------------------------------------------------------------------ ações

        private void Copiar()
        {
            if (_analise == null || _analise.Gravacoes.Count == 0) return;
            string msg = $"Copiar \"{Origem.Nome}\" → \"{Destino.Nome}\"?\n\n" + string.Join("\n", _analise.Impactos.Select(i => "• " + i)) +
                         "\n\nÉ uma única operação: pode ser desfeita com Ctrl+Z.";
            if (MessageBox.Show(this, msg, Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            try
            {
                var (ok, falhas) = ValueCopier.Executar(_doc, _analise, Destino);
                MessageBox.Show(this, $"{ok} {(Destino.DoTipo ? "tipos atualizados" : "valores gravados")}." +
                                      (falhas > 0 ? $"\n{falhas} não puderam ser gravados (elementos em grupos ou bloqueados)." : ""), Title);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível copiar. Nada foi alterado.\n\n" + ex.Message, Title);
                return;
            }
            Analisar(); // mostra o estado novo (os copiados aparecem como "Já igual")
        }

        private void Selecionar()
        {
            if (_tabela == null) return;
            var ids = _tabela.VisibleItems<LinhaCopia>().Select(l => l.ElementoId).ToList();
            _uidoc.Selection.SetElementIds(ids);
            _rodapeInfo.Text = $"{ids.Count} elementos selecionados no Revit.";
        }

        private void Exportar()
        {
            if (_tabela == null) return;
            var dialogo = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                FileName = $"copiar_{Origem?.Nome}_para_{Destino?.Nome}.csv".Replace(" ", "_"),
            };
            if (dialogo.ShowDialog(this) != true) return;
            var sb = new StringBuilder();
            sb.AppendLine("Situação;Elemento;Id;Categoria;Valor de origem;Destino atual;Valor novo;Motivo");
            foreach (LinhaCopia l in _tabela.VisibleItems<LinhaCopia>())
                sb.AppendLine(string.Join(";", new[] { l.Situacao, l.Elemento, l.Id.ToString(), l.Categoria, l.ValorOrigem, l.ValorAtual, l.ValorNovo, l.Motivo }
                    .Select(v => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"")));
            try { File.WriteAllText(dialogo.FileName, sb.ToString(), new UTF8Encoding(true)); }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível gravar o arquivo:\n" + ex.Message, Title); }
        }
    }
}
