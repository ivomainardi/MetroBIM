using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Autodesk.Revit.DB;
using MetroSP.Ui;

namespace MetroSP.Dados
{
    public class LinhaNome
    {
        public string Situacao { get; set; }
        public string Tipo { get; set; }
        public string Categoria { get; set; }
        public string Familia { get; set; }
        public string Atual { get; set; }
        public string Novo { get; set; }
        public string Motivo { get; set; }
        internal ElementId Id { get; set; }
        /// <summary>Onde o nome precisa ser único (famílias entre si, tipos dentro da família).</summary>
        internal string Grupo { get; set; }
        public bool Grava => Situacao == RenomearFamiliasWindow.Renomeia;
    }

    /// <summary>
    /// Renomear famílias e tipos por regra (prefixo, sufixo, substituir, modelo com {Nome}, {Família}, {Categoria} e
    /// parâmetros), com prévia, conferência de nomes repetidos e de caracteres que o Revit não aceita.
    /// </summary>
    public class RenomearFamiliasWindow : Window
    {
        public const string Renomeia = "Renomeia";
        private const string Conflito = "Nome repetido", Invalido = "Nome inválido";
        private const string OpPrefixo = "Acrescentar prefixo", OpSufixo = "Acrescentar sufixo", OpSubstituir = "Localizar e substituir",
                             OpModelo = "Montar pelo modelo", OpMaiusculas = "MAIÚSCULAS", OpMinusculas = "minúsculas";
        private static readonly char[] Proibidos = { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~' };

        private readonly Document _doc;
        private readonly ComboBox _alvo;
        private readonly SeletorCategorias _categorias;
        private readonly TextBox _contem;
        private readonly CheckBox _soUsados;
        private readonly ComboBox _operacao;
        private readonly TextBox _texto;
        private readonly TextBox _localizar;
        private readonly TextBox _substituir;
        private readonly CheckBox _regex;
        private readonly StackPanel _camposTexto;
        private readonly StackPanel _camposSubstituir;
        private readonly TextBlock _ajuda;
        private readonly WrapPanel _chips;
        private readonly DockPanel _areaTabela;
        private readonly TextBlock _resumo;
        private readonly Button _renomear;

        private List<(Element E, string Tipo, string Categoria, string Familia, string Grupo, bool Usado)> _itens;
        private List<LinhaNome> _linhas = new List<LinhaNome>();
        private FilterableGrid _tabela;

        public RenomearFamiliasWindow(Autodesk.Revit.UI.UIDocument uidoc)
        {
            _doc = uidoc.Document;
            Title = "MetroSP - Renomear famílias e tipos";
            Width = 1100;
            Height = 720;
            MinWidth = 800;
            MinHeight = 480;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _alvo = new ComboBox { Padding = new Thickness(4, 3, 4, 3), MinWidth = 160, Margin = new Thickness(0, 0, 10, 0) };
            foreach (string a in new[] { "Famílias e tipos", "Só famílias", "Só tipos" }) _alvo.Items.Add(a);
            _alvo.SelectedIndex = 0;
            _categorias = new SeletorCategorias { Margin = new Thickness(0, 0, 10, 0) };
            _contem = new TextBox { Width = 180, Margin = new Thickness(0, 0, 10, 0), ToolTip = "Só os nomes que contêm este texto" };
            _soUsados = new CheckBox { Content = "Só os usados no modelo", VerticalAlignment = VerticalAlignment.Center };
            var linha1 = new StackPanel { Orientation = Orientation.Horizontal };
            linha1.Children.Add(Theme.Rotulo("Renomear"));
            linha1.Children.Add(_alvo);
            linha1.Children.Add(_categorias);
            linha1.Children.Add(Theme.Rotulo("Nome contém"));
            linha1.Children.Add(_contem);
            linha1.Children.Add(_soUsados);

            _operacao = new ComboBox { Padding = new Thickness(4, 3, 4, 3), MinWidth = 200, Margin = new Thickness(0, 0, 12, 0) };
            foreach (string op in new[] { OpPrefixo, OpSufixo, OpSubstituir, OpModelo, OpMaiusculas, OpMinusculas }) _operacao.Items.Add(op);
            _operacao.SelectedIndex = 0;
            _texto = new TextBox { Width = 340 };
            _camposTexto = new StackPanel { Orientation = Orientation.Horizontal };
            _camposTexto.Children.Add(Theme.Rotulo("Texto"));
            _camposTexto.Children.Add(_texto);
            _localizar = new TextBox { Width = 200, Margin = new Thickness(0, 0, 10, 0) };
            _substituir = new TextBox { Width = 200, Margin = new Thickness(0, 0, 10, 0) };
            _regex = new CheckBox { Content = "Expressão regular", VerticalAlignment = VerticalAlignment.Center };
            _camposSubstituir = new StackPanel { Orientation = Orientation.Horizontal };
            _camposSubstituir.Children.Add(Theme.Rotulo("Localizar"));
            _camposSubstituir.Children.Add(_localizar);
            _camposSubstituir.Children.Add(Theme.Rotulo("Por"));
            _camposSubstituir.Children.Add(_substituir);
            _camposSubstituir.Children.Add(_regex);
            var linha2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            linha2.Children.Add(Theme.Rotulo("Regra"));
            linha2.Children.Add(_operacao);
            linha2.Children.Add(_camposTexto);
            linha2.Children.Add(_camposSubstituir);
            _ajuda = Theme.Detalhe();
            _ajuda.Margin = new Thickness(0, 4, 0, 0);
            var previa = new Button { Content = "Ver prévia", Style = Theme.PrimaryButton, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 8) };
            previa.Click += (s, e) => Analisar();
            _chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            _areaTabela = new DockPanel();

            var corpo = new DockPanel();
            foreach (UIElement el in new UIElement[] { linha1, linha2, _ajuda, previa, _chips })
            {
                DockPanel.SetDock(el, Dock.Top);
                corpo.Children.Add(el);
            }
            corpo.Children.Add(_areaTabela);

            _resumo = Theme.Detalhe();
            _renomear = new Button { Content = "Renomear", Style = Theme.PrimaryButton, IsEnabled = false, ToolTip = "Renomeia as linhas visíveis na tabela (use os filtros para deixar alguma de fora)." };
            var fechar = new Button { Content = "Fechar", IsCancel = true };
            _renomear.Click += (s, e) => Renomear();
            fechar.Click += (s, e) => Close();
            Theme.Apply(this, "Renomear famílias e tipos", _doc.Title + " · por regra, com prévia e conferência de nomes repetidos", corpo, Theme.Rodape(_resumo, _renomear, fechar));

            _operacao.SelectionChanged += (s, e) => MudouOperacao();
            foreach (TextBox t in new[] { _texto, _localizar, _substituir, _contem }) t.TextChanged += (s, e) => Invalidar();
            _regex.Click += (s, e) => Invalidar();
            _soUsados.Click += (s, e) => Invalidar();
            _alvo.SelectionChanged += (s, e) => Invalidar();
            _categorias.Mudou += Invalidar;
            Coletar();
            MudouOperacao();
        }

        private string Operacao => _operacao.SelectedItem as string;

        private void MudouOperacao()
        {
            string op = Operacao;
            _camposTexto.Visibility = op == OpPrefixo || op == OpSufixo || op == OpModelo ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            _camposSubstituir.Visibility = op == OpSubstituir ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            _ajuda.Text = op == OpModelo ? "Monte o nome com {Nome} (o atual), {Família}, {Categoria} e parâmetros do tipo, por exemplo \"MSP_{Categoria}_{Nome}\"."
                : op == OpSubstituir ? "Troca o trecho em todos os nomes (sem diferenciar maiúsculas). Com expressão regular, use $1, $2... para os grupos."
                : "O Revit não aceita estes caracteres em nomes: \\ : { } [ ] | ; < > ? ` ~";
            Invalidar();
        }

        private void Invalidar()
        {
            if (_renomear == null) return;
            _renomear.IsEnabled = false;
            if (_tabela != null) _tabela.Opacity = 0.5;
        }

        private void Coletar()
        {
            Cursor = Cursors.Wait;
            try
            {
                var usados = new HashSet<ElementId>(new FilteredElementCollector(_doc).WhereElementIsNotElementType().Select(e => e.GetTypeId()).Where(id => id != ElementId.InvalidElementId));
                var familiasUsadas = new HashSet<ElementId>(usados.Select(id => (_doc.GetElement(id) as FamilySymbol)?.Family?.Id).Where(id => id != null));
                _itens = new List<(Element, string, string, string, string, bool)>();
                foreach (Family f in new FilteredElementCollector(_doc).OfClass(typeof(Family)).Cast<Family>())
                {
                    if (f.FamilyCategory == null || f.IsInPlace) continue;
                    _itens.Add((f, "Família", f.FamilyCategory.Name, f.Name, "F", familiasUsadas.Contains(f.Id)));
                }
                foreach (ElementType t in new FilteredElementCollector(_doc).WhereElementIsElementType().Cast<ElementType>())
                {
                    if (t.Category == null || t.Category.CategoryType != CategoryType.Model) continue;
                    if (t is FamilySymbol fs && fs.Family?.IsInPlace == true) continue;
                    string grupo = t is FamilySymbol s ? "S|" + s.Family.Id.Value : "T|" + t.GetType().Name + "|" + t.Category.Id.Value;
                    _itens.Add((t, "Tipo", t.Category.Name, t.FamilyName, grupo, usados.Contains(t.Id)));
                }
            }
            finally { Cursor = null; }
            _categorias.Definir(_itens.GroupBy(i => i.Categoria).ToDictionary(g => g.Key, g => g.Count()));
        }

        private string Novo(Element e, string familia, string categoria, Regex re)
        {
            string n = e.Name;
            switch (Operacao)
            {
                case OpPrefixo: return _texto.Text + n;
                case OpSufixo: return n + _texto.Text;
                case OpSubstituir: return re.Replace(n, _regex.IsChecked == true ? _substituir.Text : _substituir.Text.Replace("$", "$$"));
                case OpMaiusculas: return n.ToUpperInvariant();
                case OpMinusculas: return n.ToLowerInvariant();
                case OpModelo:
                    return Regex.Replace(_texto.Text, @"\{([^{}]+)\}", m =>
                    {
                        string k = m.Groups[1].Value.Trim();
                        if (k.Equals("Nome", StringComparison.OrdinalIgnoreCase)) return n;
                        if (k.Equals("Família", StringComparison.OrdinalIgnoreCase) || k.Equals("Familia", StringComparison.OrdinalIgnoreCase)) return familia;
                        if (k.Equals("Categoria", StringComparison.OrdinalIgnoreCase)) return categoria;
                        Parameter p = e.LookupParameter(k);
                        return p == null ? "" : Valores.Texto(_doc, p);
                    });
                default: return n;
            }
        }

        private void Analisar()
        {
            Regex re = null;
            if (Operacao == OpSubstituir)
            {
                if (_localizar.Text.Length == 0) { MessageBox.Show(this, "Informe o texto a localizar.", Title); return; }
                try { re = new Regex(_regex.IsChecked == true ? _localizar.Text : Regex.Escape(_localizar.Text), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
                catch (ArgumentException ex) { MessageBox.Show(this, "Expressão regular inválida:\n" + ex.Message, Title); return; }
            }
            string alvo = _alvo.SelectedItem as string;
            string contem = _contem.Text.Trim();
            var escolhidos = _itens.Where(i => (alvo != "Só famílias" || i.Tipo == "Família") && (alvo != "Só tipos" || i.Tipo == "Tipo") &&
                                               _categorias.Aceita(i.Categoria) && (_soUsados.IsChecked != true || i.Usado) &&
                                               (contem.Length == 0 || i.E.Name.IndexOf(contem, StringComparison.CurrentCultureIgnoreCase) >= 0)).ToList();
            var linhas = new List<LinhaNome>();
            int iguais = 0;
            foreach (var i in escolhidos)
            {
                string novo = Novo(i.E, i.Familia, i.Categoria, re);
                if (novo == i.E.Name) { iguais++; continue; }
                var l = new LinhaNome { Tipo = i.Tipo, Categoria = i.Categoria, Familia = i.Tipo == "Tipo" ? i.Familia : "", Atual = i.E.Name, Novo = novo, Id = i.E.Id, Grupo = i.Grupo, Situacao = Renomeia };
                if (string.IsNullOrWhiteSpace(novo)) { l.Situacao = Invalido; l.Motivo = "nome vazio"; }
                else if (novo.IndexOfAny(Proibidos) >= 0) { l.Situacao = Invalido; l.Motivo = "tem " + string.Join(" ", novo.Where(Proibidos.Contains).Distinct()); }
                linhas.Add(l);
            }
            // Nomes finais por grupo: os que mudam e os que ficam como estão.
            var mudam = new HashSet<ElementId>(linhas.Where(l => l.Grava).Select(l => l.Id));
            var finais = _itens.Where(i => !mudam.Contains(i.E.Id)).Select(i => (i.Grupo, Nome: i.E.Name, Id: i.E.Id))
                .Concat(linhas.Where(l => l.Grava).Select(l => (l.Grupo, Nome: l.Novo, l.Id)))
                .GroupBy(x => x.Grupo + "|" + x.Nome.ToUpperInvariant()).Where(g => g.Count() > 1).ToDictionary(g => g.Key, g => g.Count());
            foreach (LinhaNome l in linhas.Where(l => l.Grava))
                if (finais.ContainsKey(l.Grupo + "|" + l.Novo.ToUpperInvariant()))
                {
                    l.Situacao = Conflito;
                    l.Motivo = l.Tipo == "Família" ? "já existe uma família com este nome" : "já existe um tipo com este nome na família";
                }
            _linhas = linhas;

            _chips.Children.Clear();
            int grava = linhas.Count(l => l.Grava);
            _chips.Children.Add(Theme.Chip($"{grava} renomeiam", destaque: grava > 0));
            _chips.Children.Add(Theme.Chip($"{iguais} sem mudança"));
            int conf = linhas.Count(l => l.Situacao == Conflito), inv = linhas.Count(l => l.Situacao == Invalido);
            if (conf > 0) _chips.Children.Add(Theme.Chip($"{conf} nomes repetidos", escuro: true));
            if (inv > 0) _chips.Children.Add(Theme.Chip($"{inv} nomes inválidos", escuro: true));

            _areaTabela.Children.Clear();
            _tabela = new FilterableGrid(_linhas, typeof(LinhaNome));
            _tabela.AddColumn("Situação", nameof(LinhaNome.Situacao), 110);
            _tabela.AddColumn("O quê", nameof(LinhaNome.Tipo), 70);
            _tabela.AddColumn("Categoria", nameof(LinhaNome.Categoria), 130);
            _tabela.AddColumn("Família", nameof(LinhaNome.Familia), 170);
            _tabela.AddColumn("Nome atual", nameof(LinhaNome.Atual), 220);
            _tabela.AddColumn("Nome novo", nameof(LinhaNome.Novo), 220);
            _tabela.AddColumn("Motivo", nameof(LinhaNome.Motivo), 150, fill: true);
            _tabela.Grid.LoadingRow += (s, e) => e.Row.Background = ((LinhaNome)e.Row.Item).Grava ? Theme.LinhaNormal : Theme.LinhaProblema;
            _tabela.Filtered += Contar;
            Theme.StyleSelection(_tabela.Grid);
            _areaTabela.Children.Add(_tabela);
            Contar();
        }

        private void Contar()
        {
            int n = _tabela?.VisibleItems<LinhaNome>().Count(l => l.Grava) ?? 0;
            _renomear.IsEnabled = n > 0;
            _renomear.Content = n > 0 ? $"Renomear {n}" : "Renomear";
            _resumo.Text = _tabela == null ? "" : $"{_linhas.Count} nomes mudariam · {n} prontos para renomear entre os visíveis";
        }

        private void Renomear()
        {
            List<LinhaNome> g = _tabela.VisibleItems<LinhaNome>().Where(l => l.Grava).ToList();
            if (g.Count == 0) return;
            if (MessageBox.Show(this, $"Renomear {g.Count} famílias e tipos?\n\nÉ uma única operação: pode ser desfeita com Ctrl+Z.", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            int ok = 0;
            var falhas = new List<string>();
            try
            {
                using (var t = new Transaction(_doc, "MetroSP: Renomear famílias e tipos"))
                {
                    t.Start();
                    foreach (LinhaNome l in g)
                    {
                        try { _doc.GetElement(l.Id).Name = l.Novo; ok++; }
                        catch (Exception ex) { falhas.Add($"{l.Atual}: {ex.Message}"); }
                    }
                    t.Commit();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível renomear. Nada foi alterado.\n\n" + ex.Message, Title);
                return;
            }
            MessageBox.Show(this, $"{ok} renomeados." + (falhas.Count > 0 ? $"\n\n{falhas.Count} não puderam ser renomeados:\n" + string.Join("\n", falhas.Take(10)) : ""), Title);
            Coletar();
            Analisar();
        }
    }
}
