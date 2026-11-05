using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using MetroSP.Ui;
using AcDocument = Autodesk.AutoCAD.ApplicationServices.Document;

namespace MetroSP.Classificacao
{
    /// <summary>
    /// Confere as classificações do padrão (uma ou mais por objeto, em property sets) com as planilhas:
    /// formato, código existente, descrição igual à da lista e tipo/layer previsto. Só objetos que vão para o IFC.
    /// Cada linha tem um botão com a ação para a situação; as propostas seguras podem ser aplicadas de uma vez.
    /// </summary>
    public class VerificarClassificacaoWindow : Window
    {
        private readonly AcDocument _doc;
        private readonly Database _db;
        private readonly List<ObjectId> _selecao;
        private readonly ComboBox _padraoCombo;
        private readonly TextBlock _padraoInfo;
        private readonly ComboBox _escopo;
        private readonly WrapPanel _resumo;
        private readonly CheckBox _incluirInseguras;
        private readonly DockPanel _areaTabela;
        private readonly TextBlock _rodapeInfo;
        private readonly Button _aplicar, _aplicarSelecionadas, _exportar, _selecionar, _reanalisar;
        private PadraoClassificacao _padrao;
        private bool _trocandoPadrao;
        private List<LinhaVerificacao> _linhas;
        private FilterableGrid _tabela;
        private string _situacao;
        private bool _ocultarOk;

        private static string _ultimoPadrao;

        public VerificarClassificacaoWindow(AcDocument doc, List<ObjectId> selecao)
        {
            _doc = doc;
            _db = doc.Database;
            _selecao = selecao != null && selecao.Count > 0 ? selecao : null;

            Title = "MetroSP - Verificar classificação";
            Width = 1200;
            Height = 740;
            MinWidth = 820;
            MinHeight = 480;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // Padrão
            var linhaPadrao = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var rotPadrao = Theme.Rotulo("Padrão");
            DockPanel.SetDock(rotPadrao, Dock.Left);
            linhaPadrao.Children.Add(rotPadrao);
            _padraoCombo = new ComboBox { MinWidth = 220, Margin = new Thickness(0, 0, 8, 0), ToolTip = "Conjunto de classificações a conferir, com a planilha de cada uma." };
            _padraoCombo.SelectionChanged += (s, e) => { if (!_trocandoPadrao) UsarPadrao(_padraoCombo.SelectedItem as PadraoClassificacao); };
            DockPanel.SetDock(_padraoCombo, Dock.Left);
            linhaPadrao.Children.Add(_padraoCombo);
            var editar = new Button { Content = "Editar padrões...", ToolTip = "Criar ou mudar padrões: classificações, property sets e as colunas da planilha." };
            editar.Click += (s, e) => EditarPadroes();
            DockPanel.SetDock(editar, Dock.Left);
            linhaPadrao.Children.Add(editar);
            _padraoInfo = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            linhaPadrao.Children.Add(_padraoInfo);

            // Escopo
            _escopo = new ComboBox { Padding = new Thickness(4, 3, 4, 3), MinWidth = 220, Margin = new Thickness(0, 0, 12, 0), ToolTip = "Só objetos que vão para o IFC (layers ligados, fora das anotações e do que a exportação exclui)." };
            if (_selecao != null) _escopo.Items.Add($"Seleção atual ({_selecao.Count} objetos)");
            _escopo.Items.Add("Todo o desenho");
            _escopo.SelectedIndex = 0;
            var verificar = new Button { Content = "Verificar", Style = Theme.PrimaryButton };
            verificar.Click += (s, e) => Verificar();
            var barra = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            barra.Children.Add(Theme.Rotulo("Escopo"));
            barra.Children.Add(_escopo);
            barra.Children.Add(verificar);

            _resumo = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            _incluirInseguras = new CheckBox
            {
                Content = "Incluir propostas que trocam o código (código parecido, texto fora do formato)",
                ToolTip = "Sem esta opção, só são aplicadas as correções de escrita: mesmo código, descrição como na lista.",
                Margin = new Thickness(0, 0, 0, 8),
            };
            _incluirInseguras.Click += (s, e) => AtualizarRodape();
            _areaTabela = new DockPanel();
            _areaTabela.Children.Add(new TextBlock { Text = "Escolha o padrão e clique em \"Verificar\".", Foreground = Theme.Solid(Theme.Cinza) });

            var corpo = new DockPanel();
            foreach (UIElement el in new UIElement[] { linhaPadrao, barra, _resumo, _incluirInseguras })
            {
                DockPanel.SetDock(el, Dock.Top);
                corpo.Children.Add(el);
            }
            corpo.Children.Add(_areaTabela);

            _rodapeInfo = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            _reanalisar = new Button { Content = "Reanalisar", IsEnabled = false, ToolTip = "Lê o desenho de novo, mantendo escopo e filtros da tabela." };
            _exportar = new Button { Content = "Exportar (.csv)", IsEnabled = false };
            _selecionar = new Button { Content = "Selecionar no desenho", IsEnabled = false, ToolTip = "Seleciona no Civil 3D os objetos visíveis na tabela (fica selecionado ao fechar a janela)." };
            _aplicarSelecionadas = new Button { Content = "Aplicar selecionadas", IsEnabled = false, ToolTip = "Aplica só as propostas das linhas selecionadas (Ctrl/Shift+clique). Inclui as que trocam o código." };
            _aplicar = new Button { Content = "Aplicar propostas", Style = Theme.PrimaryButton, IsEnabled = false };
            var fechar = new Button { Content = "Fechar", IsCancel = true };
            _reanalisar.Click += (s, e) => Verificar();
            _exportar.Click += (s, e) => Exportar();
            _selecionar.Click += (s, e) => Selecionar(_tabela.VisibleItems<LinhaVerificacao>().Select(l => l.Id));
            _aplicarSelecionadas.Click += (s, e) => Aplicar(true);
            _aplicar.Click += (s, e) => Aplicar(false);
            fechar.Click += (s, e) => Close();
            var rodape = new DockPanel();
            StackPanel botoes = Theme.Buttons(_reanalisar, _exportar, _selecionar, _aplicarSelecionadas, _aplicar, fechar);
            DockPanel.SetDock(botoes, Dock.Right);
            rodape.Children.Add(botoes);
            rodape.Children.Add(_rodapeInfo);

            Theme.Apply(this, "Verificar classificação", Path.GetFileName(doc.Name) + " · confere as classificações com as planilhas do padrão", corpo, rodape);
            CarregarPadroes(_ultimoPadrao);
        }

        // ------------------------------------------------------------------ padrões

        private void CarregarPadroes(string escolher)
        {
            List<PadraoClassificacao> padroes = PadraoClassificacao.Listar();
            if (padroes.Count == 0) padroes.Add(PadraoClassificacao.Inicial());
            _trocandoPadrao = true;
            _padraoCombo.ItemsSource = padroes;
            _padraoCombo.SelectedItem = padroes.FirstOrDefault(p => p.Nome == escolher) ?? padroes[0];
            _trocandoPadrao = false;
            UsarPadrao(_padraoCombo.SelectedItem as PadraoClassificacao);
        }

        private void UsarPadrao(PadraoClassificacao p)
        {
            _padrao = p;
            if (p == null) { _padraoInfo.Text = ""; return; }
            _ultimoPadrao = p.Nome;
            var cache = new Dictionary<string, Planilha.Tabela>(StringComparer.OrdinalIgnoreCase);
            foreach (SistemaClassificacao s in p.Sistemas) s.Carregar(cache);
            _padraoInfo.Text = string.Join("  ·  ", p.Sistemas.Select(s => s.Lista != null && s.Erro == null
                ? $"{s.Nome}: {s.Lista.Itens.Count} itens em {s.Propriedades}" : $"{s.Nome}: {s.Erro}"));
            _padraoInfo.ToolTip = string.Join("\n", p.Sistemas.Select(s =>
                $"{s.Nome}  ·  {s.Propriedades}  ·  {s.Planilha?.NomeArquivo}" + (string.IsNullOrEmpty(s.Planilha?.Aba) ? "" : $" › {s.Planilha.Aba}") +
                (s.Lista?.Avisos.Count > 0 ? "\n   " + string.Join("\n   ", s.Lista.Avisos.Take(10)) : "")));
            _padraoInfo.Foreground = Theme.Solid(p.Sistemas.Any(s => s.Lista == null || s.Erro != null) ? Theme.LaranjaTexto : Theme.Cinza);
            if (_linhas != null) Verificar();
        }

        private void EditarPadroes()
        {
            var w = new PadroesClassificacaoWindow(_db, _padrao?.Nome) { Owner = this };
            w.ShowDialog();
            CarregarPadroes(w.Escolhido ?? _padrao?.Nome);
        }

        // ------------------------------------------------------------------ verificar

        private void Verificar()
        {
            var sistemas = _padrao?.Sistemas.Where(x => x.Lista != null && x.Lista.Itens.Count > 0).ToList() ?? new List<SistemaClassificacao>();
            if (sistemas.Count == 0)
            {
                MessageBox.Show(this, "Nenhuma classificação do padrão tem a planilha lida. Clique em \"Editar padrões...\" e escolha a planilha e as colunas.", Title);
                return;
            }
            Cursor = Cursors.Wait;
            try
            {
                using (_doc.LockDocument())
                using (Transaction tr = _db.TransactionManager.StartTransaction())
                {
                    bool selecao = (_escopo.SelectedItem as string ?? "").StartsWith("Seleção");
                    List<ObjectId> ids = Ifc.FiltroIfc.Objetos(_db, tr, selecao ? _selecao : null);
                    var linhas = new List<LinhaVerificacao>();
                    foreach (SistemaClassificacao s in sistemas) linhas.AddRange(ClassificacaoCore.Verificar(_db, tr, ids, s));
                    tr.Commit();
                    _linhas = linhas;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível verificar:\n" + ex.Message, Title);
                return;
            }
            finally { Cursor = null; }
            Mostrar();
        }

        private void Mostrar()
        {
            if (_situacao != null && !_linhas.Any(l => l.Situacao == _situacao)) _situacao = null;
            MostrarEtiquetas();
            var estado = _tabela?.Estado();
            _areaTabela.Children.Clear();
            _tabela = new FilterableGrid(_linhas, typeof(LinhaVerificacao));
            _tabela.AddColumn("Situação", nameof(LinhaVerificacao.Situacao), 160);
            if (_linhas.Select(l => l.SistemaRef).Distinct().Count() > 1)
                _tabela.AddColumn("Classificação", nameof(LinhaVerificacao.Sistema), 120);
            _tabela.AddColumn("Objeto", nameof(LinhaVerificacao.Objeto), 200);
            _tabela.AddColumn("Tipo", nameof(LinhaVerificacao.Tipo), 110);
            _tabela.AddColumn("Layer", nameof(LinhaVerificacao.Layer), 140);
            _tabela.AddColumn("Handle", nameof(LinhaVerificacao.Handle), 70);
            _tabela.AddColumn("Preenchido", nameof(LinhaVerificacao.Preenchido), 200);
            _tabela.AddColumn("Na lista", nameof(LinhaVerificacao.NaLista), 200);
            _tabela.AddColumn("Proposta", nameof(LinhaVerificacao.Proposta), 160, fill: true);
            if (_linhas.Any(l => !string.IsNullOrEmpty(l.Bloqueio)))
                _tabela.AddColumn("Bloqueio para gravar", nameof(LinhaVerificacao.Bloqueio), 200);
            _tabela.Grid.Columns.Insert(1, ColunaAcao());
            _tabela.Grid.LoadingRow += (s, e) =>
            {
                var l = (LinhaVerificacao)e.Row.Item;
                e.Row.Background = l.Situacao == ClassificacaoCore.OK ? Theme.LinhaNormal
                    : l.Situacao == ClassificacaoCore.SemClassificacao || l.Situacao == ClassificacaoCore.SemParametro ? Theme.LinhaAviso
                    : Theme.LinhaProblema;
            };
            _tabela.Filtered += AtualizarRodape;
            _tabela.Grid.SelectionChanged += (s, e) => AtualizarSelecionadas();
            Theme.StyleSelection(_tabela.Grid);
            var opcoes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            opcoes.Children.Add(_tabela.CreateSingleLineToggle());
            var ocultar = new CheckBox { Content = "Ocultar OK", VerticalAlignment = VerticalAlignment.Center, IsChecked = _ocultarOk };
            ocultar.Click += (s, e) => { _ocultarOk = ocultar.IsChecked == true; AplicarFiltroExtra(); };
            opcoes.Children.Add(ocultar);
            AplicarFiltroExtra();
            _tabela.Restaurar(estado);
            DockPanel.SetDock(opcoes, Dock.Top);
            _areaTabela.Children.Add(opcoes);
            _areaTabela.Children.Add(_tabela);
            _exportar.IsEnabled = _selecionar.IsEnabled = _reanalisar.IsEnabled = true;
            AtualizarRodape();
        }

        private void MostrarEtiquetas()
        {
            _resumo.Children.Clear();
            foreach (string s in ClassificacaoCore.Ordem)
            {
                int n = _linhas.Count(l => l.Situacao == s);
                if (n == 0 && s != ClassificacaoCore.OK) continue;
                bool problema = s == ClassificacaoCore.DescricaoDiferente || s == ClassificacaoCore.ForaDaLista || s == ClassificacaoCore.FormatoInvalido;
                Border chip = Theme.Chip($"{n} {s.ToLowerInvariant()}", destaque: problema, escuro: s == ClassificacaoCore.ForaDaLista);
                chip.Cursor = Cursors.Hand;
                chip.ToolTip = _situacao == s ? "Clique para mostrar todas as situações" : "Clique para ver só estes na tabela";
                if (_situacao == s) { chip.BorderBrush = Theme.Solid(Theme.Laranja); chip.BorderThickness = new Thickness(2); }
                else if (_situacao != null) chip.Opacity = 0.55;
                string sit = s;
                chip.MouseLeftButtonUp += (o, e) => { _situacao = _situacao == sit ? null : sit; MostrarEtiquetas(); AplicarFiltroExtra(); };
                _resumo.Children.Add(chip);
            }
            foreach (var g in _linhas.Where(l => l.FaltaPset).GroupBy(l => l.SistemaRef))
            {
                var linhas = g.ToList();
                var anexar = new Button
                {
                    Content = $"Anexar {g.Key.PropertySet} em {linhas.Count} objetos",
                    ToolTip = $"Cria o property set {g.Key.PropertySet} (se não existir) com {g.Key.PropriedadeCodigo}" +
                              (g.Key.Descricao != null ? $" e {g.Key.Descricao}" : "") + " e anexa aos objetos, vazio. Depois é só escolher a classificação.",
                    Padding = new Thickness(8, 1, 8, 1),
                    Margin = new Thickness(4, 0, 0, 0),
                };
                anexar.Click += (o, e) => Anexar(linhas);
                _resumo.Children.Add(anexar);
            }
        }

        private void AplicarFiltroExtra()
        {
            if (_tabela == null) return;
            string sit = _situacao;
            bool ocultar = _ocultarOk;
            _tabela.ExtraFilter = sit == null && !ocultar ? (Func<object, bool>)null
                : o => (sit == null || ((LinhaVerificacao)o).Situacao == sit) && (!ocultar || ((LinhaVerificacao)o).Situacao != ClassificacaoCore.OK);
            _tabela.Refresh();
        }

        // ------------------------------------------------------------------ propostas

        private List<LinhaVerificacao> Propostas(out int conflitos, bool soSelecionadas)
        {
            conflitos = 0;
            if (_tabela == null) return new List<LinhaVerificacao>();
            bool todas = soSelecionadas || _incluirInseguras.IsChecked == true;
            IEnumerable<LinhaVerificacao> origem = soSelecionadas ? _tabela.Grid.SelectedItems.OfType<LinhaVerificacao>() : _tabela.VisibleItems<LinhaVerificacao>();
            return origem.Where(l => l.Gravacoes.Count > 0 && string.IsNullOrEmpty(l.Bloqueio) && (l.PropostaSegura || todas)).ToList();
        }

        private void AtualizarRodape()
        {
            if (_tabela == null) return;
            int v = _tabela.VisibleItems<LinhaVerificacao>().Count();
            var p = Propostas(out _, false);
            _rodapeInfo.Text = v == _linhas.Count ? $"{v} linhas" : $"{v} de {_linhas.Count} visíveis (filtro ativo)";
            _aplicar.IsEnabled = p.Count > 0;
            _aplicar.Content = p.Count == 0 ? "Nada a corrigir" : $"Aplicar todas ({p.Count})";
            AtualizarSelecionadas();
        }

        private void AtualizarSelecionadas()
        {
            int n = _tabela == null ? 0 : Propostas(out _, true).Count;
            _aplicarSelecionadas.IsEnabled = n > 0;
            _aplicarSelecionadas.Content = n > 0 ? $"Aplicar selecionadas ({n})" : "Aplicar selecionadas";
        }

        private void Aplicar(bool soSelecionadas)
        {
            var p = Propostas(out _, soSelecionadas);
            if (p.Count == 0) return;
            string msg = $"Gravar {p.Count} correções?\n\nExemplos:\n" +
                         string.Join("\n", p.Take(5).Select(l => $"• {l.Preenchido}  →  {string.Join(" · ", l.Gravacoes.Select(g => g.Valor))}")) +
                         "\n\nPode ser desfeito com U (desfazer) no Civil 3D.";
            if (MessageBox.Show(this, msg, Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            Gravar(p.Select(l => (l.SistemaRef, l.Id, (IEnumerable<(string, string)>)l.Gravacoes)));
        }

        /// <summary>Grava (por property set), mostra o resumo e confere de novo.</summary>
        private void Gravar(IEnumerable<(SistemaClassificacao Sistema, ObjectId Id, IEnumerable<(string Prop, string Valor)> Valores)> itens)
        {
            var resumo = new List<string>();
            try
            {
                using (_doc.LockDocument())
                using (Transaction tr = _db.TransactionManager.StartTransaction())
                {
                    foreach (var g in itens.GroupBy(i => i.Sistema.PropertySet, StringComparer.OrdinalIgnoreCase))
                        resumo.Add((g.Count() < itens.Count() ? g.Key + ": " : "") +
                                   ClassificacaoCore.Gravar(_db, tr, g.Key, g.SelectMany(i => i.Valores.Select(v => (i.Id, v.Prop, v.Valor)))));
                    tr.Commit();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível gravar. Nada foi alterado.\n\n" + ex.Message, Title);
                return;
            }
            MessageBox.Show(this, string.Join("\n\n", resumo), Title);
            Verificar();
        }

        // ------------------------------------------------------------------ ação por linha

        private DataGridTemplateColumn ColunaAcao()
        {
            var botao = new FrameworkElementFactory(typeof(Button));
            botao.SetBinding(ContentControl.ContentProperty, new Binding(nameof(LinhaVerificacao.Acao)));
            botao.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(LinhaVerificacao.Proposta)));
            botao.SetValue(Control.PaddingProperty, new Thickness(6, 0, 6, 0));
            botao.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 1, 2, 1));
            botao.SetValue(Control.FontSizeProperty, 11.0);
            var estilo = new Style(typeof(Button), TryFindResource(typeof(Button)) as Style);
            var semAcao = new DataTrigger { Binding = new Binding(nameof(LinhaVerificacao.Acao)), Value = null };
            semAcao.Setters.Add(new Setter(UIElement.VisibilityProperty, System.Windows.Visibility.Collapsed));
            estilo.Triggers.Add(semAcao);
            botao.SetValue(FrameworkElement.StyleProperty, estilo);
            botao.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((s, e) =>
            {
                if ((s as FrameworkElement)?.DataContext is LinhaVerificacao l) ExecutarAcao(l);
            }));
            return new DataGridTemplateColumn { Header = "Ação", Width = 130, CellTemplate = new DataTemplate { VisualTree = botao } };
        }

        private List<LinhaVerificacao> Junto(LinhaVerificacao l)
        {
            var sel = _tabela.Grid.SelectedItems.OfType<LinhaVerificacao>().ToList();
            return sel.Contains(l) ? sel.Where(x => x.SistemaRef == l.SistemaRef && x.Acao == l.Acao).ToList() : new List<LinhaVerificacao> { l };
        }

        private void ExecutarAcao(LinhaVerificacao l)
        {
            try
            {
                switch (l.Acao)
                {
                    case "Corrigir":
                        var linhas = Junto(l);
                        if (linhas.Count > 1 && MessageBox.Show(this, $"Corrigir as {linhas.Count} linhas selecionadas?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                        Gravar(linhas.Select(x => (x.SistemaRef, x.Id, (IEnumerable<(string, string)>)x.Gravacoes)));
                        break;
                    case "Escolher...": Escolher(l, Junto(l)); break;
                    case "Anexar property set": Anexar(Junto(l)); break;
                    default: Zoom(l); break;
                }
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Title); }
        }

        private void Escolher(LinhaVerificacao l, List<LinhaVerificacao> linhas)
        {
            SistemaClassificacao s = l.SistemaRef;
            var w = new EscolherItemWindow(s.Lista, s.Nome, l.Tipo, l.Layer, linhas.Count > 1 ? $"{linhas.Count} objetos" : l.Objeto, linhas.Count > 1 ? null : l.Preenchido) { Owner = this };
            if (w.ShowDialog() != true || w.Escolhido == null) return;
            ClassItem item = w.Escolhido;
            var valores = new List<(string, string)>();
            if (s.Formato == FormatoClassificacao.CodigoDescricao)
                valores.Add((s.PropriedadeCodigo, ValorClassificacao.Escrever(item.Codigo, item.Descricao, s.GravarNome ? s.Nome : null)));
            else
            {
                valores.Add((s.PropriedadeCodigo, item.Codigo));
                if (s.Descricao != null && item.Descricao.Length > 0) valores.Add((s.Descricao, item.Descricao));
            }
            Gravar(linhas.Select(x => (s, x.Id, (IEnumerable<(string, string)>)valores)));
        }

        private void Anexar(List<LinhaVerificacao> linhas)
        {
            var porSistema = linhas.GroupBy(x => x.SistemaRef).ToList();
            Gravar(porSistema.SelectMany(g => g.Select(x =>
            {
                var vazios = new List<(string, string)> { (g.Key.PropriedadeCodigo, "") };
                if (g.Key.Descricao != null) vazios.Add((g.Key.Descricao, ""));
                return (g.Key, x.Id, (IEnumerable<(string, string)>)vazios);
            })));
        }

        // ------------------------------------------------------------------ desenho

        private void Selecionar(IEnumerable<ObjectId> ids)
        {
            var lista = ids.Distinct().ToArray();
            try { _doc.Editor.SetImpliedSelection(lista); } catch { }
            _rodapeInfo.Text = $"{lista.Length} objetos selecionados no desenho.";
        }

        /// <summary>Seleciona o objeto e aproxima a vista dele.</summary>
        private void Zoom(LinhaVerificacao l)
        {
            Selecionar(new[] { l.Id });
            try
            {
                using (_doc.LockDocument())
                using (Transaction tr = _db.TransactionManager.StartTransaction())
                {
                    var e = (Entity)tr.GetObject(l.Id, OpenMode.ForRead);
                    Extents3d ext = e.GeometricExtents;
                    Editor ed = _doc.Editor;
                    using (ViewTableRecord v = ed.GetCurrentView())
                    {
                        Matrix3d paraDcs = (Matrix3d.Rotation(-v.ViewTwist, v.ViewDirection, v.Target) *
                                            Matrix3d.Displacement(v.Target - Point3d.Origin) *
                                            Matrix3d.PlaneToWorld(v.ViewDirection)).Inverse();
                        ext.TransformBy(paraDcs);
                        double w = Math.Max(ext.MaxPoint.X - ext.MinPoint.X, 1), h = Math.Max(ext.MaxPoint.Y - ext.MinPoint.Y, 1);
                        v.CenterPoint = new Point2d((ext.MinPoint.X + ext.MaxPoint.X) / 2, (ext.MinPoint.Y + ext.MaxPoint.Y) / 2);
                        double aspecto = v.Width / Math.Max(v.Height, 1e-9);
                        v.Height = Math.Max(h, w / aspecto) * 1.4;
                        v.Width = v.Height * aspecto;
                        ed.SetCurrentView(v);
                    }
                    tr.Commit();
                }
            }
            catch { /* sem extensão (objeto vazio): fica só a seleção */ }
            string texto = !string.IsNullOrEmpty(l.Bloqueio) ? "Não dá para gravar: " + l.Bloqueio : l.Proposta;
            _rodapeInfo.Text = $"{l.Objeto} ({l.Handle}) selecionado." + (string.IsNullOrEmpty(texto) ? "" : " " + texto);
        }

        private void Exportar()
        {
            if (_tabela == null) return;
            var d = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                FileName = $"verificacao_classificacao_{Path.GetFileNameWithoutExtension(_doc.Name)}.csv".Replace(" ", "_"),
            };
            if (d.ShowDialog(this) != true) return;
            var sb = new StringBuilder("Situação;Classificação;Property set;Objeto;Tipo;Layer;Handle;Preenchido;Na lista;Proposta;Bloqueio\r\n");
            foreach (LinhaVerificacao l in _tabela.VisibleItems<LinhaVerificacao>())
                sb.Append(string.Join(";", new[] { l.Situacao, l.Sistema, l.SistemaRef?.Propriedades, l.Objeto, l.Tipo, l.Layer, l.Handle, l.Preenchido, l.NaLista, l.Proposta, l.Bloqueio }
                    .Select(v => "\"" + (v ?? "").Replace("\"", "\"\"") + "\""))).Append("\r\n");
            try { File.WriteAllText(d.FileName, sb.ToString(), new UTF8Encoding(true)); }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível gravar o arquivo:\n" + ex.Message, Title); }
        }
    }
}
