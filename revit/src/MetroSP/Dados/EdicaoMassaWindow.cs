using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Autodesk.Revit.DB;
using MetroSP.Parametros;
using MetroSP.Ui;
using Grid = System.Windows.Controls.Grid;

namespace MetroSP.Dados
{
    /// <summary>
    /// Edição em massa de um parâmetro: definir um valor (que pode juntar outros parâmetros com {Nome}), acrescentar
    /// prefixo ou sufixo, localizar e substituir (com expressão regular) ou limpar. Mostra a prévia antes de gravar.
    /// </summary>
    public class EdicaoMassaWindow : Window
    {
        private const string OpDefinir = "Definir valor", OpPrefixo = "Acrescentar prefixo", OpSufixo = "Acrescentar sufixo",
                             OpSubstituir = "Localizar e substituir", OpLimpar = "Limpar";
        private const string JaIgual = "Já igual";

        private readonly Autodesk.Revit.UI.UIDocument _uidoc;
        private readonly Document _doc;
        private readonly EscopoDados _escopo;
        private readonly ComboBox _parametro;
        private readonly CheckBox _incluirRevit;
        private readonly ComboBox _operacao;
        private readonly TextBox _texto;
        private readonly TextBox _localizar;
        private readonly TextBox _substituir;
        private readonly CheckBox _regex;
        private readonly CheckBox _maiusculas;
        private readonly CheckBox _soVazios;
        private readonly StackPanel _camposTexto;
        private readonly StackPanel _camposSubstituir;
        private readonly TextBlock _ajuda;
        private readonly WrapPanel _chips;
        private readonly DockPanel _areaPrevia;
        private readonly TextBlock _resumo;
        private readonly Button _gravar;
        private readonly Button _selecionar;

        private List<ParamRef> _candidatos = new List<ParamRef>();
        private List<LinhaImportacao> _linhas;
        private FilterableGrid _tabela;

        public EdicaoMassaWindow(Autodesk.Revit.UI.UIDocument uidoc)
        {
            _uidoc = uidoc;
            _doc = uidoc.Document;
            Title = "MetroSP - Editar em massa";
            Width = 1060;
            Height = 700;
            MinWidth = 760;
            MinHeight = 480;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _escopo = new EscopoDados(uidoc);
            _parametro = new ComboBox { IsEditable = true, IsTextSearchEnabled = true, Padding = new Thickness(4, 3, 4, 3), MinWidth = 380, Margin = new Thickness(0, 0, 12, 0) };
            TextSearch.SetTextPath(_parametro, nameof(ParamRef.Nome));
            _incluirRevit = new CheckBox { Content = "Incluir parâmetros do Revit", VerticalAlignment = VerticalAlignment.Center };
            _operacao = new ComboBox { Padding = new Thickness(4, 3, 4, 3), MinWidth = 200, Margin = new Thickness(0, 0, 12, 0) };
            foreach (string op in new[] { OpDefinir, OpPrefixo, OpSufixo, OpSubstituir, OpLimpar }) _operacao.Items.Add(op);
            _operacao.SelectedIndex = 0;
            _soVazios = new CheckBox { Content = "Só nos vazios", VerticalAlignment = VerticalAlignment.Center };

            _texto = new TextBox { MinWidth = 420 };
            _camposTexto = new StackPanel { Orientation = Orientation.Horizontal };
            _camposTexto.Children.Add(Theme.Rotulo("Texto"));
            _camposTexto.Children.Add(_texto);
            var inserir = Theme.Link("inserir {parâmetro}");
            inserir.Margin = new Thickness(10, 0, 0, 0);
            inserir.Click += (s, e) => Inserir(inserir);
            _camposTexto.Children.Add(inserir);

            _localizar = new TextBox { Width = 220, Margin = new Thickness(0, 0, 12, 0) };
            _substituir = new TextBox { Width = 220, Margin = new Thickness(0, 0, 12, 0) };
            _regex = new CheckBox { Content = "Expressão regular", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            _maiusculas = new CheckBox { Content = "Diferenciar maiúsculas", VerticalAlignment = VerticalAlignment.Center };
            _camposSubstituir = new StackPanel { Orientation = Orientation.Horizontal };
            _camposSubstituir.Children.Add(Theme.Rotulo("Localizar"));
            _camposSubstituir.Children.Add(_localizar);
            _camposSubstituir.Children.Add(Theme.Rotulo("Por"));
            _camposSubstituir.Children.Add(_substituir);
            _camposSubstituir.Children.Add(_regex);
            _camposSubstituir.Children.Add(_maiusculas);
            _ajuda = Theme.Detalhe();
            _ajuda.Margin = new Thickness(0, 4, 0, 0);

            var linha1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            linha1.Children.Add(Theme.Rotulo("Parâmetro"));
            linha1.Children.Add(_parametro);
            linha1.Children.Add(_incluirRevit);
            var linha2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            linha2.Children.Add(Theme.Rotulo("Operação"));
            linha2.Children.Add(_operacao);
            linha2.Children.Add(_soVazios);
            var linha3 = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            linha3.Children.Add(_camposTexto);
            linha3.Children.Add(_camposSubstituir);
            linha3.Children.Add(_ajuda);
            var previa = new Button { Content = "Ver prévia", Style = Theme.PrimaryButton, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 8) };
            previa.Click += (s, e) => Analisar();
            _chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            _areaPrevia = new DockPanel();

            var corpo = new DockPanel();
            foreach (UIElement el in new UIElement[] { _escopo.Painel, linha1, linha2, linha3, previa, _chips })
            {
                DockPanel.SetDock(el, Dock.Top);
                corpo.Children.Add(el);
            }
            corpo.Children.Add(_areaPrevia);

            _resumo = Theme.Detalhe();
            _selecionar = new Button { Content = "Selecionar no modelo", IsEnabled = false };
            _gravar = new Button { Content = "Gravar", Style = Theme.PrimaryButton, IsEnabled = false };
            var fechar = new Button { Content = "Fechar", IsCancel = true };
            _selecionar.Click += (s, e) => Selecionar();
            _gravar.Click += (s, e) => Gravar();
            fechar.Click += (s, e) => Close();
            Theme.Apply(this, "Editar em massa", _doc.Title + " · um parâmetro em muitos elementos, com prévia", corpo, Theme.Rodape(_resumo, _selecionar, _gravar, fechar));

            _escopo.Mudou += Carregar;
            _incluirRevit.Click += (s, e) => Carregar();
            _operacao.SelectionChanged += (s, e) => MudouOperacao();
            foreach (TextBox t in new[] { _texto, _localizar, _substituir }) t.TextChanged += (s, e) => Invalidar();
            foreach (CheckBox c in new[] { _regex, _maiusculas, _soVazios }) c.Click += (s, e) => Invalidar();
            _parametro.SelectionChanged += (s, e) => Invalidar();
            Carregar();
            MudouOperacao();
        }

        private ParamRef Parametro => _parametro.SelectedItem as ParamRef;
        private string Operacao => _operacao.SelectedItem as string;

        private void Carregar()
        {
            string antes = Parametro?.Chave;
            Cursor = Cursors.Wait;
            try
            {
                _candidatos = ValueCopier.Candidatos(_doc, _escopo.Elementos())
                    .Where(r => r.Editavel && r.Storage != StorageType.ElementId && r.Storage != StorageType.None && (_incluirRevit.IsChecked == true || !r.EhDoRevit)).ToList();
            }
            finally { Cursor = null; }
            _parametro.ItemsSource = _candidatos;
            _parametro.SelectedItem = _candidatos.FirstOrDefault(r => r.Chave == antes);
            Invalidar();
        }

        private void MudouOperacao()
        {
            string op = Operacao;
            _camposTexto.Visibility = op == OpSubstituir || op == OpLimpar ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
            _camposSubstituir.Visibility = op == OpSubstituir ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            _soVazios.IsEnabled = op == OpDefinir;
            if (op != OpDefinir) _soVazios.IsChecked = false;
            _ajuda.Text = op == OpDefinir ? "Use {Nome do parâmetro} para juntar valores de outros parâmetros, por exemplo \"{Nível}-{Marca}\". Também valem {Categoria}, {Família}, {Tipo} e {Id}."
                : op == OpPrefixo || op == OpSufixo ? "O texto vai junto do valor atual. Aceita {Nome do parâmetro}. Em números e medidas, o resultado precisa continuar sendo número."
                : op == OpSubstituir ? "Troca o trecho em todos os valores. Com expressão regular, use $1, $2... para os grupos."
                : "Apaga o valor (só para parâmetros de texto).";
            Invalidar();
        }

        private void Inserir(Button origem)
        {
            var menu = new ContextMenu { PlacementTarget = origem };
            foreach (string n in new[] { "Categoria", "Família", "Tipo", "Id" }.Concat(_candidatos.Select(r => r.Nome).Distinct().Take(400)))
            {
                var item = new MenuItem { Header = "{" + n + "}" };
                item.Click += (s, e) =>
                {
                    int i = _texto.CaretIndex;
                    _texto.Text = _texto.Text.Insert(i, "{" + n + "}");
                    _texto.CaretIndex = i + n.Length + 2;
                    _texto.Focus();
                };
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }

        private void Invalidar()
        {
            if (_gravar == null) return;
            _gravar.IsEnabled = false;
            if (_tabela != null) _tabela.Opacity = 0.5;
        }

        // ------------------------------------------------------------------ prévia

        private string Novo(Element e, string atual, Regex re, ISet<string> faltando)
        {
            switch (Operacao)
            {
                case OpDefinir: return Valores.Compor(_doc, e, _texto.Text, faltando);
                case OpPrefixo: return Valores.Compor(_doc, e, _texto.Text, faltando) + atual;
                case OpSufixo: return atual + Valores.Compor(_doc, e, _texto.Text, faltando);
                case OpSubstituir: return re.Replace(atual, _regex.IsChecked == true ? _substituir.Text : _substituir.Text.Replace("$", "$$"));
                default: return "";
            }
        }

        private void Analisar()
        {
            ParamRef pr = Parametro;
            if (pr == null) { MessageBox.Show(this, "Escolha o parâmetro.", Title); return; }
            Regex re = null;
            if (Operacao == OpSubstituir)
            {
                if (_localizar.Text.Length == 0) { MessageBox.Show(this, "Informe o texto a localizar.", Title); return; }
                try
                {
                    re = new Regex(_regex.IsChecked == true ? _localizar.Text : Regex.Escape(_localizar.Text),
                                   _maiusculas.IsChecked == true ? RegexOptions.None : RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }
                catch (ArgumentException ex) { MessageBox.Show(this, "Expressão regular inválida:\n" + ex.Message, Title); return; }
            }
            if (Operacao == OpLimpar && pr.Storage != StorageType.String) { MessageBox.Show(this, "Só parâmetros de texto podem ser limpos.", Title); return; }

            Cursor = Cursors.Wait;
            var faltando = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int iguais = 0, semParametro = 0;
            var linhas = new List<LinhaImportacao>();
            var porDono = new Dictionary<ElementId, List<LinhaImportacao>>();
            try
            {
                foreach (Element e in _escopo.Elementos())
                {
                    var (p, dono) = pr.Resolver(_doc, e);
                    if (p == null) { semParametro++; continue; }
                    if (_soVazios.IsChecked == true && !Valores.Vazio(p)) continue;
                    string atual = Valores.Texto(_doc, p);
                    string novo = Novo(e, atual, re, faltando);
                    var l = new LinhaImportacao
                    {
                        Elemento = Valores.Nome(_doc, e), Id = e.Id.Value, ElementoId = e.Id, DonoId = dono.Id, Categoria = e.Category?.Name,
                        Parametro = pr.Nome, Ref = pr, Atual = atual, Novo = novo,
                    };
                    if (novo == atual) { iguais++; continue; }
                    if (p.IsReadOnly) l.Situacao = PlanilhaDadosWindow.SomenteLeitura;
                    else if (!Valores.Converter(_doc, p, novo, out object valor, out string motivo)) { l.Situacao = PlanilhaDadosWindow.NaoConverte; l.Motivo = motivo; }
                    else if (Valores.Igual(p, valor)) { iguais++; continue; }
                    else if (_doc.IsWorkshared && WorksharingUtils.GetCheckoutStatus(_doc, dono.Id) == CheckoutStatus.OwnedByOtherUser) l.Situacao = PlanilhaDadosWindow.Bloqueado;
                    else
                    {
                        l.Valor = valor;
                        l.Situacao = PlanilhaDadosWindow.Altera;
                        if (!porDono.TryGetValue(dono.Id, out var g)) porDono[dono.Id] = g = new List<LinhaImportacao>();
                        g.Add(l);
                    }
                    linhas.Add(l);
                }
                foreach (List<LinhaImportacao> g in porDono.Values.Where(g => g.Select(x => x.Novo).Distinct().Count() > 1))
                    foreach (LinhaImportacao x in g) { x.Situacao = PlanilhaDadosWindow.Conflito; x.Motivo = "instâncias do mesmo tipo dariam valores diferentes"; }
            }
            finally { Cursor = null; }
            _linhas = linhas;

            int grava = Gravacoes().Count;
            _chips.Children.Clear();
            _chips.Children.Add(Theme.Chip(pr.DoTipo ? $"{grava} tipos mudam" : $"{grava} mudam", destaque: grava > 0));
            _chips.Children.Add(Theme.Chip($"{iguais} já iguais"));
            foreach (string s in new[] { PlanilhaDadosWindow.NaoConverte, PlanilhaDadosWindow.SomenteLeitura, PlanilhaDadosWindow.Conflito, PlanilhaDadosWindow.Bloqueado })
            {
                int n = linhas.Count(l => l.Situacao == s);
                if (n > 0) _chips.Children.Add(Theme.Chip($"{n} {s.ToLowerInvariant()}", escuro: true));
            }
            if (semParametro > 0) _chips.Children.Add(Theme.Chip($"{semParametro} sem o parâmetro"));

            _areaPrevia.Children.Clear();
            _tabela = new FilterableGrid(_linhas, typeof(LinhaImportacao));
            _tabela.AddColumn("Situação", nameof(LinhaImportacao.Situacao), 120);
            _tabela.AddColumn("Elemento", nameof(LinhaImportacao.Elemento), 240);
            _tabela.AddColumn("Id", nameof(LinhaImportacao.Id), 80);
            _tabela.AddColumn("Categoria", nameof(LinhaImportacao.Categoria), 120);
            _tabela.AddColumn("Atual", nameof(LinhaImportacao.Atual), 170);
            _tabela.AddColumn("Novo", nameof(LinhaImportacao.Novo), 170);
            _tabela.AddColumn("Motivo", nameof(LinhaImportacao.Motivo), 150, fill: true);
            _tabela.Grid.LoadingRow += (s, e) => e.Row.Background = ((LinhaImportacao)e.Row.Item).Grava ? Theme.LinhaNormal : Theme.LinhaProblema;
            Theme.StyleSelection(_tabela.Grid);
            _areaPrevia.Children.Add(_tabela);

            _gravar.IsEnabled = grava > 0;
            _gravar.Content = grava == 0 ? "Nada a gravar" : pr.DoTipo ? $"Gravar em {grava} tipos" : $"Gravar {grava} valores";
            _selecionar.IsEnabled = linhas.Count > 0;
            var avisos = new List<string>();
            if (faltando.Count > 0) avisos.Add("sem estes parâmetros (ficaram vazios): " + string.Join(", ", faltando.Take(5)));
            if (pr.DoTipo && grava > 0) avisos.Add("parâmetro de tipo: vale para todas as instâncias, inclusive fora do escopo");
            Theme.MudarResumo(_resumo, avisos, $"{linhas.Count + iguais} elementos com o parâmetro no escopo");
        }

        private List<LinhaImportacao> Gravacoes() =>
            _linhas?.Where(l => l.Grava).GroupBy(l => l.DonoId).Select(g => g.First()).ToList() ?? new List<LinhaImportacao>();

        private void Gravar()
        {
            List<LinhaImportacao> g = Gravacoes();
            if (g.Count == 0) return;
            if (MessageBox.Show(this, $"Gravar {g.Count} {(Parametro.DoTipo ? "tipos" : "valores")} em \"{Parametro.Nome}\"?\n\nÉ uma única operação: pode ser desfeita com Ctrl+Z.",
                    Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            int ok = 0, falhas = 0;
            try
            {
                using (var t = new Transaction(_doc, "MetroSP: Editar em massa"))
                {
                    t.Start();
                    foreach (LinhaImportacao l in g)
                        if (Valores.Gravar(l.Ref.Achar(_doc.GetElement(l.DonoId)), l.Valor)) ok++; else falhas++;
                    t.Commit();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível gravar. Nada foi alterado.\n\n" + ex.Message, Title);
                return;
            }
            MessageBox.Show(this, $"{ok} gravados." + (falhas > 0 ? $"\n{falhas} não puderam ser gravados (grupos, fórmulas ou elementos bloqueados)." : ""), Title);
            Analisar();
        }

        private void Selecionar()
        {
            if (_tabela == null) return;
            var ids = _tabela.VisibleItems<LinhaImportacao>().Select(l => l.ElementoId).Distinct().ToList();
            _uidoc.Selection.SetElementIds(ids);
            _resumo.Text = $"{ids.Count} elementos selecionados no Revit.";
        }
    }
}
