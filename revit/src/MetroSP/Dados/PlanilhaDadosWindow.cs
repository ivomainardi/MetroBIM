using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Autodesk.Revit.DB;
using MetroSP.Parametros;
using MetroSP.Ui;
using Grid = System.Windows.Controls.Grid;

namespace MetroSP.Dados
{
    /// <summary>Uma alteração lida da planilha.</summary>
    public class LinhaImportacao
    {
        public string Situacao { get; set; }
        public string Elemento { get; set; }
        public long Id { get; set; }
        public string Categoria { get; set; }
        public string Parametro { get; set; }
        public string Atual { get; set; }
        public string Novo { get; set; }
        public string Motivo { get; set; }
        internal ElementId ElementoId { get; set; }
        internal ElementId DonoId { get; set; }
        internal ParamRef Ref { get; set; }
        internal object Valor { get; set; }
        public bool Grava => Situacao == PlanilhaDadosWindow.Altera;
    }

    /// <summary>
    /// Planilha de dados: leva parâmetros dos elementos para um .xlsx (uma linha por elemento, uma coluna por parâmetro),
    /// e traz de volta só o que mudou, mostrando cada alteração antes de gravar.
    /// </summary>
    public class PlanilhaDadosWindow : Window
    {
        public const string Altera = "Altera";
        public const string SomenteLeitura = "Somente leitura";
        public const string NaoConverte = "Não converte";
        public const string SemElemento = "Elemento não existe";
        public const string SemParametro = "Sem o parâmetro";
        public const string Conflito = "Conflito no tipo";
        public const string Bloqueado = "Com outro usuário";

        private const string AbaDados = "Dados";
        private const string AbaMapa = "_MetroSP";
        private static readonly string[] Fixas = { "ElementId", "Categoria", "Família", "Tipo" };

        private readonly Autodesk.Revit.UI.UIDocument _uidoc;
        private readonly Document _doc;
        private readonly EscopoDados _escopo;
        private readonly TextBox _busca;
        private readonly CheckBox _incluirRevit;
        private readonly CheckBox _soEditaveis;
        private readonly ListBox _lista;
        private readonly TextBlock _contagem;
        private readonly WrapPanel _chips;
        private readonly DockPanel _areaPrevia;
        private readonly TextBlock _resumo;
        private readonly Button _gravar;
        private readonly Button _selecionar;
        private readonly TabControl _abas;

        private List<ParamRef> _candidatos = new List<ParamRef>();
        private readonly HashSet<string> _marcados = new HashSet<string>();
        private List<LinhaImportacao> _linhas;
        private FilterableGrid _tabela;
        private string _arquivo;

        public PlanilhaDadosWindow(Autodesk.Revit.UI.UIDocument uidoc)
        {
            _uidoc = uidoc;
            _doc = uidoc.Document;
            Title = "MetroSP - Planilha de dados";
            Width = 1100;
            Height = 720;
            MinWidth = 760;
            MinHeight = 480;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _escopo = new EscopoDados(uidoc);

            // ---- aba Exportar
            _busca = new TextBox { Width = 220, ToolTip = "Procurar parâmetro", Margin = new Thickness(0, 0, 12, 0) };
            _incluirRevit = new CheckBox { Content = "Incluir parâmetros do Revit", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            _soEditaveis = new CheckBox { Content = "Só editáveis", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            var todos = Theme.Link("marcar visíveis");
            var nenhum = Theme.Link("desmarcar todos");
            nenhum.Margin = new Thickness(10, 0, 0, 0);
            var barra = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            barra.Children.Add(_busca);
            barra.Children.Add(_incluirRevit);
            barra.Children.Add(_soEditaveis);
            barra.Children.Add(todos);
            barra.Children.Add(nenhum);
            _lista = new ListBox { BorderBrush = Theme.Solid(Theme.CinzaClaro) };
            ScrollViewer.SetHorizontalScrollBarVisibility(_lista, ScrollBarVisibility.Disabled);
            _contagem = Theme.Detalhe();
            _contagem.Margin = new Thickness(0, 6, 0, 0);
            var exportar = new Button { Content = "Exportar planilha...", Style = Theme.PrimaryButton, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            exportar.Click += (s, e) => Exportar();
            var exp = new DockPanel();
            DockPanel.SetDock(barra, Dock.Top);
            exp.Children.Add(barra);
            var explica = Theme.Detalhe("Uma linha por elemento e uma coluna por parâmetro. Colunas em cinza são só para consulta " +
                                        "(identificação e parâmetros somente leitura). Parâmetros de tipo aparecem com [tipo].");
            DockPanel.SetDock(explica, Dock.Bottom);
            DockPanel.SetDock(exportar, Dock.Bottom);
            DockPanel.SetDock(_contagem, Dock.Bottom);
            exp.Children.Add(explica);
            exp.Children.Add(exportar);
            exp.Children.Add(_contagem);
            exp.Children.Add(_lista);

            // ---- aba Importar
            var escolher = new Button { Content = "Escolher planilha...", Style = Theme.PrimaryButton, HorizontalAlignment = HorizontalAlignment.Left };
            escolher.Click += (s, e) => Importar(null);
            var reler = new Button { Content = "Ler de novo", Margin = new Thickness(8, 0, 0, 0), ToolTip = "Lê o mesmo arquivo outra vez (depois de salvar no Excel)." };
            reler.Click += (s, e) => { if (_arquivo != null) Importar(_arquivo); };
            var topoImp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            topoImp.Children.Add(escolher);
            topoImp.Children.Add(reler);
            _chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            _areaPrevia = new DockPanel();
            _areaPrevia.Children.Add(Theme.Detalhe("Escolha uma planilha exportada por esta ferramenta. Só as células que mudaram aparecem aqui, " +
                                                   "e nada é gravado até você clicar em \"Gravar alterações\"."));
            var imp = new DockPanel();
            DockPanel.SetDock(topoImp, Dock.Top);
            DockPanel.SetDock(_chips, Dock.Top);
            imp.Children.Add(topoImp);
            imp.Children.Add(_chips);
            imp.Children.Add(_areaPrevia);

            _abas = new TabControl();
            _abas.Items.Add(new TabItem { Header = "Exportar", Content = exp });
            _abas.Items.Add(new TabItem { Header = "Importar", Content = imp });

            var corpo = new DockPanel();
            _escopo.Painel.Margin = new Thickness(0, 0, 0, 8);
            DockPanel.SetDock(_escopo.Painel, Dock.Top);
            corpo.Children.Add(_escopo.Painel);
            corpo.Children.Add(_abas);

            _resumo = Theme.Detalhe();
            _selecionar = new Button { Content = "Selecionar no modelo", IsEnabled = false };
            _gravar = new Button { Content = "Gravar alterações", Style = Theme.PrimaryButton, IsEnabled = false };
            var fechar = new Button { Content = "Fechar", IsCancel = true };
            _selecionar.Click += (s, e) => Selecionar();
            _gravar.Click += (s, e) => Gravar();
            fechar.Click += (s, e) => Close();
            Theme.Apply(this, "Planilha de dados", _doc.Title + " · exporte para o Excel, edite e traga de volta", corpo, Theme.Rodape(_resumo, _selecionar, _gravar, fechar));

            _escopo.Mudou += Carregar;
            _busca.TextChanged += (s, e) => Listar();
            _incluirRevit.Click += (s, e) => Listar();
            _soEditaveis.Click += (s, e) => Listar();
            todos.Click += (s, e) => { foreach (ParamRef r in Visiveis()) _marcados.Add(r.Chave); Listar(); };
            nenhum.Click += (s, e) => { _marcados.Clear(); Listar(); };
            Carregar();
        }

        // ------------------------------------------------------------------ exportar

        private void Carregar()
        {
            Cursor = Cursors.Wait;
            try { _candidatos = ValueCopier.Candidatos(_doc, _escopo.Elementos()).Where(r => r.Storage != StorageType.None).ToList(); }
            finally { Cursor = null; }
            Listar();
        }

        private IEnumerable<ParamRef> Visiveis()
        {
            string t = _busca.Text.Trim();
            return _candidatos.Where(r => (_incluirRevit.IsChecked == true || !r.EhDoRevit) && (_soEditaveis.IsChecked != true || r.Editavel) &&
                                          (t.Length == 0 || r.Nome.IndexOf(t, StringComparison.CurrentCultureIgnoreCase) >= 0));
        }

        private void Listar()
        {
            _lista.Items.Clear();
            foreach (ParamRef r in Visiveis())
            {
                var c = new CheckBox
                {
                    Content = $"{r.Nome}{(r.DoTipo ? "  [tipo]" : "")}   ·   {r.TipoLabel} · {r.Origem}{(r.Editavel ? "" : " · somente leitura")}",
                    IsChecked = _marcados.Contains(r.Chave),
                    Tag = r,
                    Margin = new Thickness(0, 1, 0, 1),
                };
                c.Click += (s, e) => { if (c.IsChecked == true) _marcados.Add(r.Chave); else _marcados.Remove(r.Chave); Contar(); };
                _lista.Items.Add(c);
            }
            Contar();
        }

        private List<ParamRef> Escolhidos() => _candidatos.Where(r => _marcados.Contains(r.Chave)).ToList();

        private void Contar()
        {
            int n = _escopo.Elementos().Count;
            _contagem.Text = $"{Escolhidos().Count} parâmetros marcados · {n} elementos no escopo";
        }

        private static string Cabecalho(ParamRef r) => r.Nome + (r.DoTipo ? " [tipo]" : "");

        private void Exportar()
        {
            List<ParamRef> ps = Escolhidos();
            List<Element> elems = _escopo.Elementos();
            if (ps.Count == 0 || elems.Count == 0)
            {
                MessageBox.Show(this, ps.Count == 0 ? "Marque os parâmetros que vão para a planilha." : "Nenhum elemento no escopo.", Title);
                return;
            }
            var d = new Microsoft.Win32.SaveFileDialog { Filter = "Planilha do Excel (*.xlsx)|*.xlsx", FileName = Limpo(_doc.Title) + "_dados.xlsx" };
            if (d.ShowDialog(this) != true) return;

            var dados = new Xlsx.Aba { Nome = AbaDados };
            dados.Cabecalho.AddRange(Fixas);
            for (int i = 0; i < Fixas.Length; i++) dados.Travadas.Add(i);
            dados.Larguras[0] = 11;
            for (int i = 0; i < ps.Count; i++)
            {
                dados.Cabecalho.Add(Cabecalho(ps[i]));
                if (!ps[i].Editavel || ps[i].Storage == StorageType.ElementId) dados.Travadas.Add(Fixas.Length + i);
            }
            Cursor = Cursors.Wait;
            try
            {
                foreach (Element e in elems.OrderBy(x => x.Category.Name).ThenBy(x => Valores.Nome(_doc, x)))
                {
                    var l = new List<string> { e.Id.Value.ToString(CultureInfo.InvariantCulture), e.Category.Name, Valores.Familia(_doc, e), Valores.Tipo(_doc, e) };
                    foreach (ParamRef r in ps) l.Add(Valores.Texto(_doc, r.Resolver(_doc, e).Param));
                    dados.Linhas.Add(l);
                }
                var mapa = new Xlsx.Aba { Nome = AbaMapa, Oculta = true };
                mapa.Cabecalho.AddRange(new[] { "Coluna", "Chave", "Parâmetro", "Tipo" });
                mapa.Linhas.Add(new List<string> { "", "modelo", _doc.Title, "" });
                for (int i = 0; i < ps.Count; i++)
                    mapa.Linhas.Add(new List<string> { (Fixas.Length + i).ToString(CultureInfo.InvariantCulture), ps[i].Chave, ps[i].Nome, ps[i].DoTipo ? "tipo" : "instância" });
                Xlsx.Gravar(d.FileName, new[] { dados, mapa });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível gravar a planilha:\n" + ex.Message, Title);
                return;
            }
            finally { Cursor = null; }
            _arquivo = d.FileName;
            _resumo.Text = $"Planilha gravada: {elems.Count} elementos × {ps.Count} parâmetros. Edite no Excel, salve e use \"Importar\".";
            if (MessageBox.Show(this, $"Planilha gravada:\n{d.FileName}\n\nAbrir no Excel agora?", Title, MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(d.FileName) { UseShellExecute = true }); } catch { /* sem Excel */ }
            }
        }

        private static string Limpo(string s) => string.Concat((s ?? "modelo").Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

        // ------------------------------------------------------------------ importar

        private void Importar(string arquivo)
        {
            if (arquivo == null)
            {
                var d = new Microsoft.Win32.OpenFileDialog { Filter = "Planilha do Excel (*.xlsx)|*.xlsx", FileName = _arquivo ?? "" };
                if (d.ShowDialog(this) != true) return;
                arquivo = d.FileName;
            }
            _abas.SelectedIndex = 1;
            Cursor = Cursors.Wait;
            try
            {
                Dictionary<string, List<List<string>>> abas = Xlsx.Ler(arquivo);
                if (!abas.TryGetValue(AbaMapa, out List<List<string>> mapa) || !abas.TryGetValue(AbaDados, out List<List<string>> dados) || dados.Count == 0)
                {
                    MessageBox.Show(this, "Esta planilha não foi exportada pela Planilha de dados do MetroSP (faltam as abas \"Dados\" e \"_MetroSP\").", Title);
                    return;
                }
                _arquivo = arquivo;
                _linhas = Analisar(mapa, dados, out List<string> avisos);
                Mostrar(avisos);
            }
            catch (IOException ex)
            {
                MessageBox.Show(this, "Não foi possível ler a planilha. Se ela estiver aberta no Excel, salve e tente de novo.\n\n" + ex.Message, Title);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível ler a planilha:\n" + ex.Message, Title);
            }
            finally { Cursor = null; }
        }

        private static string Celula(List<string> l, int i) => i < l.Count ? l[i] ?? "" : "";

        private List<LinhaImportacao> Analisar(List<List<string>> mapa, List<List<string>> dados, out List<string> avisos)
        {
            avisos = new List<string>();
            // Coluna → chave do parâmetro (pela aba oculta; o cabeçalho visível pode ter sido editado).
            var colunas = new Dictionary<int, string>();
            foreach (List<string> m in mapa.Skip(1))
                if (int.TryParse(Celula(m, 0), NumberStyles.Integer, CultureInfo.InvariantCulture, out int col)) colunas[col] = Celula(m, 1);
            string modelo = mapa.Skip(1).Where(m => Celula(m, 1) == "modelo").Select(m => Celula(m, 2)).FirstOrDefault();
            if (!string.IsNullOrEmpty(modelo) && modelo != _doc.Title) avisos.Add($"a planilha foi exportada de \"{modelo}\"");

            var elementos = new List<(Element E, List<string> Linha)>();
            int naoExiste = 0;
            foreach (List<string> l in dados.Skip(1))
            {
                if (!long.TryParse(Celula(l, 0), NumberStyles.Integer, CultureInfo.InvariantCulture, out long id)) continue;
                Element e = _doc.GetElement(new ElementId(id));
                if (e == null || e is ElementType) { naoExiste++; continue; }
                elementos.Add((e, l));
            }
            if (naoExiste > 0) avisos.Add($"{naoExiste} linhas com elementos que não existem mais no modelo");

            Dictionary<string, ParamRef> refs = ValueCopier.Candidatos(_doc, elementos.Select(x => x.E)).GroupBy(r => r.Chave).ToDictionary(g => g.Key, g => g.First());
            int semColuna = colunas.Values.Count(k => !refs.ContainsKey(k));
            if (semColuna > 0) avisos.Add($"{semColuna} colunas de parâmetros que não estão mais nesses elementos");

            var r = new List<LinhaImportacao>();
            var porDono = new Dictionary<string, List<LinhaImportacao>>();
            foreach (var (e, l) in elementos)
            {
                foreach (var kv in colunas)
                {
                    if (!refs.TryGetValue(kv.Value, out ParamRef pr)) continue;
                    var (p, dono) = pr.Resolver(_doc, e);
                    if (p == null) continue;
                    string novo = Celula(l, kv.Key);
                    string atual = Valores.Texto(_doc, p);
                    if (novo == atual || (novo.Trim() == atual.Trim() && p.StorageType != StorageType.String)) continue;
                    var li = new LinhaImportacao
                    {
                        Elemento = Valores.Nome(_doc, e),
                        Id = e.Id.Value,
                        ElementoId = e.Id,
                        DonoId = dono.Id,
                        Categoria = e.Category?.Name,
                        Parametro = Cabecalho(pr),
                        Ref = pr,
                        Atual = atual,
                        Novo = novo,
                    };
                    r.Add(li);
                    if (!Valores.Editavel(p)) { li.Situacao = SomenteLeitura; continue; }
                    if (!Valores.Converter(_doc, p, novo, out object valor, out string motivo)) { li.Situacao = NaoConverte; li.Motivo = motivo; continue; }
                    if (Valores.Igual(p, valor)) { r.Remove(li); continue; } // só formatação diferente
                    if (_doc.IsWorkshared && WorksharingUtils.GetCheckoutStatus(_doc, dono.Id) == CheckoutStatus.OwnedByOtherUser) { li.Situacao = Bloqueado; continue; }
                    li.Valor = valor;
                    li.Situacao = Altera;
                    string chave = dono.Id.Value + "|" + pr.Chave;
                    if (!porDono.TryGetValue(chave, out var lista)) porDono[chave] = lista = new List<LinhaImportacao>();
                    lista.Add(li);
                }
            }
            // Parâmetro de tipo: linhas do mesmo tipo pedindo valores diferentes.
            foreach (List<LinhaImportacao> g in porDono.Values.Where(g => g.Count > 1 && g.Select(x => x.Novo).Distinct().Count() > 1))
                foreach (LinhaImportacao x in g) { x.Situacao = Conflito; x.Motivo = "linhas do mesmo tipo com valores diferentes"; }
            return r;
        }

        private List<LinhaImportacao> Gravacoes() =>
            _linhas?.Where(l => l.Grava).GroupBy(l => l.DonoId.Value + "|" + l.Ref.Chave).Select(g => g.First()).ToList() ?? new List<LinhaImportacao>();

        private void Mostrar(List<string> avisos)
        {
            _chips.Children.Clear();
            int Conta(string s) => _linhas.Count(l => l.Situacao == s);
            int grava = Gravacoes().Count;
            _chips.Children.Add(Theme.Chip($"{grava} alterações", destaque: grava > 0));
            foreach (string s in new[] { NaoConverte, SomenteLeitura, Conflito, Bloqueado })
                if (Conta(s) > 0) _chips.Children.Add(Theme.Chip($"{Conta(s)} {s.ToLowerInvariant()}", escuro: true));

            _areaPrevia.Children.Clear();
            if (_linhas.Count == 0)
            {
                _areaPrevia.Children.Add(Theme.Detalhe("Nenhuma célula diferente do modelo. Nada a gravar."));
            }
            else
            {
                _tabela = new FilterableGrid(_linhas, typeof(LinhaImportacao));
                _tabela.AddColumn("Situação", nameof(LinhaImportacao.Situacao), 120);
                _tabela.AddColumn("Elemento", nameof(LinhaImportacao.Elemento), 220);
                _tabela.AddColumn("Id", nameof(LinhaImportacao.Id), 80);
                _tabela.AddColumn("Parâmetro", nameof(LinhaImportacao.Parametro), 170);
                _tabela.AddColumn("Atual", nameof(LinhaImportacao.Atual), 150);
                _tabela.AddColumn("Novo", nameof(LinhaImportacao.Novo), 150);
                _tabela.AddColumn("Motivo", nameof(LinhaImportacao.Motivo), 150, fill: true);
                _tabela.Grid.LoadingRow += (s, e) => e.Row.Background = ((LinhaImportacao)e.Row.Item).Grava ? Theme.LinhaNormal : Theme.LinhaProblema;
                Theme.StyleSelection(_tabela.Grid);
                _areaPrevia.Children.Add(_tabela);
            }
            _gravar.IsEnabled = grava > 0;
            _gravar.Content = grava > 0 ? $"Gravar {grava} alterações" : "Gravar alterações";
            _selecionar.IsEnabled = _linhas.Count > 0;
            Theme.MudarResumo(_resumo, avisos, $"{Path.GetFileName(_arquivo)} · {_linhas.Count} células diferentes do modelo");
        }

        private void Gravar()
        {
            List<LinhaImportacao> g = Gravacoes();
            if (g.Count == 0) return;
            int tipos = g.Count(x => x.Ref.DoTipo);
            string msg = $"Gravar {g.Count} alterações no modelo?" + (tipos > 0 ? $"\n\n{tipos} são parâmetros de tipo: valem para todas as instâncias desses tipos." : "") +
                         "\n\nÉ uma única operação: pode ser desfeita com Ctrl+Z.";
            if (MessageBox.Show(this, msg, Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            int ok = 0, falhas = 0;
            try
            {
                using (var t = new Transaction(_doc, "MetroSP: Importar planilha de dados"))
                {
                    t.Start();
                    foreach (LinhaImportacao l in g)
                    {
                        if (Valores.Gravar(l.Ref.Achar(_doc.GetElement(l.DonoId)), l.Valor)) ok++; else falhas++;
                    }
                    t.Commit();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível gravar. Nada foi alterado.\n\n" + ex.Message, Title);
                return;
            }
            MessageBox.Show(this, $"{ok} valores gravados." + (falhas > 0 ? $"\n{falhas} não puderam ser gravados (grupos, fórmulas ou elementos bloqueados)." : ""), Title);
            Importar(_arquivo);
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
