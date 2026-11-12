using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.DB;
using MetroSP.Parametros;
using MetroSP.Ui;
using Color = Autodesk.Revit.DB.Color;
using Grid = System.Windows.Controls.Grid;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace MetroSP.Dados
{
    /// <summary>Linha do resultado do filtro.</summary>
    public class LinhaFiltro
    {
        public string Elemento { get; set; }
        public long Id { get; set; }
        public string Categoria { get; set; }
        public string Valor { get; set; }
        internal ElementId ElementoId { get; set; }
    }

    /// <summary>
    /// Filtrar e colorir: regras sobre parâmetros (vazio, igual, contém, maior...) combinadas com E ou OU; o resultado
    /// pode ser selecionado, isolado na vista ou colorido por valor de um parâmetro, com legenda.
    /// </summary>
    public class FiltroCorWindow : Window
    {
        private static readonly string[] Operadores =
        {
            "está vazio", "não está vazio", "é igual a", "é diferente de", "contém", "não contém", "começa com", "termina com", "é maior que", "é menor que",
        };

        /// <summary>Campo que uma regra consulta: um parâmetro ou um dado do elemento.</summary>
        private class Campo
        {
            public string Nome { get; set; }
            public ParamRef Ref { get; set; }
            public Func<Element, string> Fixo { get; set; }
            public override string ToString() => Ref == null ? Nome : $"{Ref.Nome}{(Ref.DoTipo ? " [tipo]" : "")}   ·   {Ref.TipoLabel}";
        }

        private class Regra
        {
            public ComboBox Campo, Operador;
            public TextBox Valor;
            public FrameworkElement Linha;
        }

        private static readonly Color[] Paleta =
        {
            new Color(255, 153, 0), new Color(31, 119, 180), new Color(44, 160, 44), new Color(214, 39, 40), new Color(148, 103, 189),
            new Color(23, 190, 207), new Color(188, 189, 34), new Color(227, 119, 194), new Color(140, 86, 75), new Color(127, 127, 127),
            new Color(255, 207, 128), new Color(158, 218, 229), new Color(152, 223, 138), new Color(255, 152, 150), new Color(197, 176, 213),
        };

        private readonly Autodesk.Revit.UI.UIDocument _uidoc;
        private readonly Document _doc;
        private readonly EscopoDados _escopo;
        private readonly StackPanel _regras;
        private readonly RadioButton _todas;
        private readonly ComboBox _corPor;
        private readonly StackPanel _legenda;
        private readonly TextBlock _resultado;
        private readonly DockPanel _areaTabela;
        private readonly TextBlock _resumo;
        private readonly List<Regra> _lista = new List<Regra>();
        private readonly Button[] _acoes;

        private List<Campo> _campos = new List<Campo>();
        private List<Element> _filtrados = new List<Element>();
        private FilterableGrid _tabela;

        public FiltroCorWindow(Autodesk.Revit.UI.UIDocument uidoc)
        {
            _uidoc = uidoc;
            _doc = uidoc.Document;
            Title = "MetroSP - Filtrar e colorir";
            Width = 1100;
            Height = 740;
            MinWidth = 800;
            MinHeight = 520;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _escopo = new EscopoDados(uidoc);
            _regras = new StackPanel();
            _todas = new RadioButton { Content = "Todas as regras (E)", GroupName = "comb", IsChecked = true, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            var qualquer = new RadioButton { Content = "Qualquer regra (OU)", GroupName = "comb", VerticalAlignment = VerticalAlignment.Center };
            var adicionar = Theme.Link("+ adicionar regra");
            adicionar.Margin = new Thickness(18, 0, 0, 0);
            adicionar.Click += (s, e) => NovaRegra();
            var comb = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            comb.Children.Add(Theme.Rotulo("Combinar"));
            comb.Children.Add(_todas);
            comb.Children.Add(qualquer);
            comb.Children.Add(adicionar);
            var filtrar = new Button { Content = "Filtrar", Style = Theme.PrimaryButton, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 8) };
            filtrar.Click += (s, e) => Filtrar();
            _resultado = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };

            // Ações sobre o resultado.
            var selecionar = new Button { Content = "Selecionar" };
            var isolar = new Button { Content = "Isolar na vista", ToolTip = "Isolamento temporário (o óculos da barra da vista desfaz)." };
            var restaurar = new Button { Content = "Restaurar vista", ToolTip = "Desfaz o isolamento temporário da vista ativa." };
            _corPor = new ComboBox { IsEditable = true, IsTextSearchEnabled = true, Padding = new Thickness(4, 3, 4, 3), Width = 260, Margin = new Thickness(0, 0, 8, 0) };
            TextSearch.SetTextPath(_corPor, "Nome");
            var colorir = new Button { Content = "Colorir", Style = Theme.PrimaryButton };
            var limpar = new Button { Content = "Limpar cores", ToolTip = "Tira as substituições gráficas dos elementos filtrados na vista ativa." };
            selecionar.Click += (s, e) => Selecionar();
            isolar.Click += (s, e) => Isolar();
            restaurar.Click += (s, e) => Restaurar();
            colorir.Click += (s, e) => Colorir();
            limpar.Click += (s, e) => LimparCores();
            _acoes = new[] { selecionar, isolar, colorir, limpar };
            var barraAcoes = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            foreach (Button b in new[] { selecionar, isolar, restaurar }) { b.Margin = new Thickness(0, 0, 8, 0); barraAcoes.Children.Add(b); }
            var sep = new Border { Width = 1, Background = Theme.Solid(Theme.CinzaClaro), Margin = new Thickness(8, 2, 16, 2) };
            barraAcoes.Children.Add(sep);
            barraAcoes.Children.Add(Theme.Rotulo("Cor por"));
            barraAcoes.Children.Add(_corPor);
            colorir.Margin = new Thickness(0, 0, 8, 0);
            barraAcoes.Children.Add(colorir);
            barraAcoes.Children.Add(limpar);

            _legenda = new StackPanel();
            var caixaLegenda = new Border
            {
                Width = 260,
                Margin = new Thickness(10, 0, 0, 0),
                BorderBrush = Theme.Solid(Theme.CinzaClaro),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8),
                Child = new ScrollViewer { Content = _legenda, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            };
            _legenda.Children.Add(Theme.Detalhe("A legenda aparece aqui quando você colorir."));
            _areaTabela = new DockPanel();
            var meio = new DockPanel();
            DockPanel.SetDock(caixaLegenda, Dock.Right);
            meio.Children.Add(caixaLegenda);
            meio.Children.Add(_areaTabela);

            var corpo = new DockPanel();
            _escopo.Painel.Margin = new Thickness(0, 0, 0, 4);
            foreach (UIElement el in new UIElement[] { _escopo.Painel, Theme.Secao("Regras"), _regras, comb, filtrar, _resultado, barraAcoes })
            {
                DockPanel.SetDock(el, Dock.Top);
                corpo.Children.Add(el);
            }
            corpo.Children.Add(meio);

            _resumo = Theme.Detalhe();
            var fechar = new Button { Content = "Fechar", IsCancel = true };
            fechar.Click += (s, e) => Close();
            Theme.Apply(this, "Filtrar e colorir", _doc.Title + " · vista ativa: " + _doc.ActiveView.Name, corpo, Theme.Rodape(_resumo, fechar));

            _escopo.Mudou += Carregar;
            Carregar();
            NovaRegra();
            Habilitar();
        }

        private void Habilitar()
        {
            foreach (Button b in _acoes) b.IsEnabled = _filtrados.Count > 0;
        }

        private void Carregar()
        {
            Cursor = Cursors.Wait;
            try
            {
                var campos = new List<Campo>
                {
                    new Campo { Nome = "Categoria", Fixo = e => e.Category?.Name ?? "" },
                    new Campo { Nome = "Família", Fixo = e => Valores.Familia(_doc, e) },
                    new Campo { Nome = "Tipo", Fixo = e => Valores.Tipo(_doc, e) },
                };
                campos.AddRange(ValueCopier.Candidatos(_doc, _escopo.Elementos()).Where(r => r.Storage != StorageType.None).Select(r => new Campo { Nome = r.Nome, Ref = r }));
                _campos = campos;
            }
            finally { Cursor = null; }
            foreach (Regra r in _lista)
            {
                string antes = (r.Campo.SelectedItem as Campo)?.ToString();
                r.Campo.ItemsSource = _campos;
                r.Campo.SelectedItem = _campos.FirstOrDefault(c => c.ToString() == antes);
            }
            string cor = (_corPor.SelectedItem as Campo)?.ToString();
            _corPor.ItemsSource = _campos;
            _corPor.SelectedItem = _campos.FirstOrDefault(c => c.ToString() == cor) ?? _campos.FirstOrDefault(c => c.Nome == "Tipo");
        }

        private void NovaRegra()
        {
            var r = new Regra
            {
                Campo = new ComboBox { IsEditable = true, IsTextSearchEnabled = true, Padding = new Thickness(4, 3, 4, 3), Width = 330, Margin = new Thickness(0, 0, 8, 0), ItemsSource = _campos },
                Operador = new ComboBox { Padding = new Thickness(4, 3, 4, 3), Width = 140, Margin = new Thickness(0, 0, 8, 0), ItemsSource = Operadores, SelectedIndex = 2 },
                Valor = new TextBox { Width = 220, Margin = new Thickness(0, 0, 8, 0) },
            };
            TextSearch.SetTextPath(r.Campo, "Nome");
            r.Operador.SelectionChanged += (s, e) => r.Valor.IsEnabled = r.Operador.SelectedIndex > 1;
            var tirar = Theme.Link("tirar");
            var linha = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            linha.Children.Add(r.Campo);
            linha.Children.Add(r.Operador);
            linha.Children.Add(r.Valor);
            linha.Children.Add(tirar);
            r.Linha = linha;
            tirar.Click += (s, e) => { _lista.Remove(r); _regras.Children.Remove(linha); };
            _lista.Add(r);
            _regras.Children.Add(linha);
        }

        // ------------------------------------------------------------------ avaliação

        private string Texto(Campo c, Element e, out Parameter p)
        {
            p = null;
            if (c.Fixo != null) return c.Fixo(e);
            p = c.Ref.Resolver(_doc, e).Param;
            return p == null ? null : Valores.Texto(_doc, p);
        }

        private bool Numero(Parameter p, string texto, string referencia, out double a, out double b)
        {
            a = b = 0;
            if (p != null && (p.StorageType == StorageType.Double || p.StorageType == StorageType.Integer) && p.HasValue)
            {
                a = p.StorageType == StorageType.Double ? p.AsDouble() : p.AsInteger();
                if (!Valores.Converter(_doc, p, referencia, out object v, out _)) return false;
                b = v is int i ? i : (double)v;
                return true;
            }
            return ValueCopier.TryDouble(texto, out a) && ValueCopier.TryDouble(referencia, out b);
        }

        private bool Atende(Regra r, Element e)
        {
            var c = (Campo)r.Campo.SelectedItem;
            string t = Texto(c, e, out Parameter p);
            string v = r.Valor.Text ?? "";
            int op = r.Operador.SelectedIndex;
            if (t == null) return op == 0 || op == 3 || op == 5; // sem o parâmetro: conta como vazio / diferente
            bool vazio = p != null ? Valores.Vazio(p) : t.Length == 0;
            var cmp = StringComparison.CurrentCultureIgnoreCase;
            switch (op)
            {
                case 0: return vazio;
                case 1: return !vazio;
                case 2:
                case 3:
                    bool igual = string.Equals(t.Trim(), v.Trim(), cmp) || (Numero(p, t, v, out double x, out double y) && Math.Abs(x - y) < 1e-6);
                    return op == 2 ? igual : !igual;
                case 4: return t.IndexOf(v, cmp) >= 0;
                case 5: return t.IndexOf(v, cmp) < 0;
                case 6: return t.StartsWith(v, cmp);
                case 7: return t.EndsWith(v, cmp);
                case 8: return Numero(p, t, v, out double a, out double b) && a > b + 1e-9;
                case 9: return Numero(p, t, v, out double m, out double n) && m < n - 1e-9;
                default: return false;
            }
        }

        private void Filtrar()
        {
            List<Regra> regras = _lista.Where(r => r.Campo.SelectedItem is Campo).ToList();
            if (_lista.Any(r => !(r.Campo.SelectedItem is Campo)))
            {
                MessageBox.Show(this, "Escolha o parâmetro de cada regra (ou tire as regras vazias).", Title);
                return;
            }
            Cursor = Cursors.Wait;
            try
            {
                bool todas = _todas.IsChecked == true;
                _filtrados = _escopo.Elementos().Where(e => regras.Count == 0 || (todas ? regras.All(r => Atende(r, e)) : regras.Any(r => Atende(r, e)))).ToList();
            }
            finally { Cursor = null; }
            _resultado.Text = $"{_filtrados.Count} de {_escopo.Elementos().Count} elementos atendem às regras";
            MostrarTabela();
            Habilitar();
        }

        private void MostrarTabela(Dictionary<ElementId, string> valores = null)
        {
            var linhas = _filtrados.Select(e => new LinhaFiltro
            {
                Elemento = Valores.Nome(_doc, e), Id = e.Id.Value, Categoria = e.Category?.Name, ElementoId = e.Id,
                Valor = valores != null && valores.TryGetValue(e.Id, out string v) ? v : null,
            }).ToList();
            _areaTabela.Children.Clear();
            _tabela = new FilterableGrid(linhas, typeof(LinhaFiltro));
            _tabela.AddColumn("Elemento", nameof(LinhaFiltro.Elemento), 260);
            _tabela.AddColumn("Id", nameof(LinhaFiltro.Id), 80);
            _tabela.AddColumn("Categoria", nameof(LinhaFiltro.Categoria), 140);
            _tabela.AddColumn(valores != null ? "Cor por: " + ((Campo)_corPor.SelectedItem).Nome : "Valor", nameof(LinhaFiltro.Valor), 160, fill: true);
            Theme.StyleSelection(_tabela.Grid);
            _areaTabela.Children.Add(_tabela);
        }

        private List<ElementId> Ids() => (_tabela?.VisibleItems<LinhaFiltro>().Select(l => l.ElementoId) ?? _filtrados.Select(e => e.Id)).ToList();

        // ------------------------------------------------------------------ ações

        private void Selecionar()
        {
            List<ElementId> ids = Ids();
            _uidoc.Selection.SetElementIds(ids);
            _resumo.Text = $"{ids.Count} elementos selecionados no Revit.";
        }

        private bool Transacao(string nome, Action acao)
        {
            try
            {
                using (var t = new Transaction(_doc, "MetroSP: " + nome))
                {
                    t.Start();
                    acao();
                    t.Commit();
                }
                _uidoc.RefreshActiveView();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Não foi possível {nome.ToLowerInvariant()}:\n{ex.Message}", Title);
                return false;
            }
        }

        private void Isolar()
        {
            View v = _doc.ActiveView;
            List<ElementId> ids = Ids().Where(id => _doc.GetElement(id) is Element e && !e.IsHidden(v)).ToList();
            if (ids.Count == 0) { MessageBox.Show(this, "Nenhum dos elementos filtrados aparece na vista ativa.", Title); return; }
            if (Transacao("Isolar na vista", () => v.IsolateElementsTemporary(ids)))
                _resumo.Text = $"{ids.Count} elementos isolados em \"{v.Name}\" (temporário).";
        }

        private void Restaurar()
        {
            View v = _doc.ActiveView;
            if (Transacao("Restaurar vista", () => v.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate)))
                _resumo.Text = "Isolamento temporário desfeito.";
        }

        private ElementId Solido() =>
            new FilteredElementCollector(_doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                .FirstOrDefault(f => f.GetFillPattern().IsSolidFill)?.Id ?? ElementId.InvalidElementId;

        private void Colorir()
        {
            if (!(_corPor.SelectedItem is Campo c)) { MessageBox.Show(this, "Escolha o parâmetro que define a cor.", Title); return; }
            View v = _doc.ActiveView;
            if (!v.AreGraphicsOverridesAllowed()) { MessageBox.Show(this, $"A vista \"{v.Name}\" não aceita substituições gráficas.", Title); return; }
            var ids = new HashSet<ElementId>(Ids());
            var valores = new Dictionary<ElementId, string>();
            foreach (Element e in _filtrados.Where(e => ids.Contains(e.Id)))
            {
                string t = Texto(c, e, out _);
                valores[e.Id] = string.IsNullOrEmpty(t) ? "(vazio)" : t;
            }
            List<IGrouping<string, ElementId>> grupos = valores.GroupBy(kv => kv.Value, kv => kv.Key)
                .OrderBy(g => g.Key == "(vazio)" ? 1 : 0).ThenBy(g => g.Key, new Natural()).ToList();
            if (grupos.Count > Paleta.Length &&
                MessageBox.Show(this, $"São {grupos.Count} valores diferentes e há {Paleta.Length} cores: algumas se repetem. Continuar?", Title, MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;
            ElementId solido = Solido();
            var cores = new Dictionary<string, Color>();
            int i = 0;
            foreach (var g in grupos) cores[g.Key] = g.Key == "(vazio)" ? new Color(200, 200, 200) : Paleta[i++ % Paleta.Length];
            bool ok = Transacao("Colorir por valor", () =>
            {
                foreach (var g in grupos)
                {
                    var o = new OverrideGraphicSettings();
                    Color cor = cores[g.Key];
                    o.SetSurfaceForegroundPatternId(solido);
                    o.SetSurfaceForegroundPatternColor(cor);
                    o.SetCutForegroundPatternId(solido);
                    o.SetCutForegroundPatternColor(cor);
                    foreach (ElementId id in g) v.SetElementOverrides(id, o);
                }
            });
            if (!ok) return;

            _legenda.Children.Clear();
            _legenda.Children.Add(new TextBlock { Text = c.Nome, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            foreach (var g in grupos)
            {
                Color cor = cores[g.Key];
                var linha = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var amostra = new Rectangle { Width = 16, Height = 12, Fill = Theme.Solid(System.Windows.Media.Color.FromRgb(cor.Red, cor.Green, cor.Blue)), Margin = new Thickness(0, 0, 8, 0), Stroke = Theme.Solid(Theme.Cinza), StrokeThickness = 0.5 };
                var n = new TextBlock { Text = g.Count().ToString(CultureInfo.CurrentCulture), Foreground = Theme.Solid(Theme.Cinza), Margin = new Thickness(8, 0, 0, 0) };
                DockPanel.SetDock(amostra, Dock.Left);
                DockPanel.SetDock(n, Dock.Right);
                linha.Children.Add(amostra);
                linha.Children.Add(n);
                linha.Children.Add(new TextBlock { Text = g.Key, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = g.Key });
                var ids2 = g.ToList();
                linha.Cursor = Cursors.Hand;
                linha.ToolTip = "Clique para selecionar estes elementos";
                linha.MouseLeftButtonUp += (s, e) => { _uidoc.Selection.SetElementIds(ids2); _resumo.Text = $"{ids2.Count} elementos com \"{g.Key}\" selecionados."; };
                _legenda.Children.Add(linha);
            }
            MostrarTabela(valores);
            _resumo.Text = $"{valores.Count} elementos coloridos em \"{v.Name}\" · {grupos.Count} valores. Ctrl+Z desfaz.";
        }

        private void LimparCores()
        {
            View v = _doc.ActiveView;
            List<ElementId> ids = Ids();
            if (Transacao("Limpar cores", () => { var o = new OverrideGraphicSettings(); foreach (ElementId id in ids) v.SetElementOverrides(id, o); }))
            {
                _legenda.Children.Clear();
                _resumo.Text = $"Substituições gráficas tiradas de {ids.Count} elementos em \"{v.Name}\".";
            }
        }

        /// <summary>Ordem natural: "P2" antes de "P10".</summary>
        private class Natural : IComparer<string>
        {
            public int Compare(string a, string b)
            {
                bool na = ValueCopier.TryDouble(a, out double x), nb = ValueCopier.TryDouble(b, out double y);
                if (na && nb) return x.CompareTo(y);
                if (na != nb) return na ? -1 : 1;
                return string.Compare(a, b, StringComparison.CurrentCultureIgnoreCase);
            }
        }
    }
}
