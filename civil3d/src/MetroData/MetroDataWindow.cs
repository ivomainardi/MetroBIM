using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using Autodesk.AutoCAD.DatabaseServices;
using MetroSP.Classificacao;
using MetroSP.PropertySets;
using MetroSP.Ui;
using Microsoft.Win32;
using AcDocument = Autodesk.AutoCAD.ApplicationServices.Document;

namespace MetroSP.MetroData
{
    /// <summary>
    /// Passo 4 da Entrega: as propriedades que a planilha do MetroDATA (Airtable, aba "Propriedades x Objetos") pede para
    /// cada objeto, na disciplina, etapa e classe escolhidas, e quantos objetos do modelo estão com elas preenchidas.
    /// Cada layer (ou bloco) é ligado a um objeto do MetroDATA pela Classe IFC; o botão Objetos deixa escolher.
    /// A planilha é lida de novo a cada conferência (o plugin não guarda nada dela).
    /// </summary>
    public class MetroDataWindow : Window
    {
        const string Todas = PlanilhaMetroData.Todas;

        readonly AcDocument _doc;
        EscolhasMetroData _esc;
        PlanilhaMetroData _md;
        List<GrupoModelo> _grupos = new List<GrupoModelo>();
        readonly ObservableCollection<LinhaMetroData> _linhas = new ObservableCollection<LinhaMetroData>();

        readonly Button _arquivo, _objetos;
        readonly ComboBox _disciplina, _etapa, _classe;
        readonly CheckBox _soPendencia;
        readonly FilterableGrid _lista;
        readonly TextBlock _resumo = Theme.Detalhe();
        readonly DockPanel _corpo;
        bool _carregando;

        public MetroDataWindow(AcDocument doc)
        {
            _doc = doc;
            Title = "MetroSP - MetroDATA";
            Width = 1180;
            Height = 720;
            MinWidth = 820;
            MinHeight = 460;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // ---- planilha e filtros
            var filtros = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(filtros, Dock.Top);
            filtros.Children.Add(new TextBlock { Text = "MetroDATA", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 4) });
            _arquivo = Theme.Link("Escolher planilha...");
            _arquivo.Margin = new Thickness(0, 0, 16, 4);
            _arquivo.MaxWidth = 320;
            _arquivo.Click += (s, e) => { if (_md != null && File.Exists(_md.Arquivo)) Abrir(_md.Arquivo); else EscolherPlanilha(); };
            filtros.Children.Add(_arquivo);
            ComboBox Lista(string rotulo, double largura, string dica)
            {
                filtros.Children.Add(new TextBlock { Text = rotulo, Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 4) });
                var c = Theme.Digitavel(new ComboBox { Width = largura, Margin = new Thickness(0, 0, 14, 4), ToolTip = dica });
                c.SelectionChanged += (s, e) => { if (!_carregando) Dispatcher.BeginInvoke(new Action(MudouFiltro)); };
                filtros.Children.Add(c);
                return c;
            }
            _disciplina = Lista("Disciplina", 200, "Disciplina do MetroDATA deste modelo.");
            _etapa = Lista("Etapa", 140, "Etapa do projeto: só as propriedades pedidas nela.");
            _classe = Lista("Classe", 130, "Confere as propriedades até esta classe.");
            _objetos = new Button { Content = "Objetos", Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(0, 0, 6, 4), VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Objeto do MetroDATA de cada layer ou bloco (quando a Classe IFC não basta)." };
            _objetos.Click += (s, e) => EscolherObjetos();
            filtros.Children.Add(_objetos);

            // ---- ações
            var acoes = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(acoes, Dock.Top);
            Button Botao(string texto, string dica, Action a)
            {
                var b = new Button { Content = texto, ToolTip = dica, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 4), VerticalAlignment = VerticalAlignment.Center };
                b.Click += (s, e) => a();
                acoes.Children.Add(b);
                return b;
            }
            Botao("Selecionar os vazios", "Seleciona no modelo os objetos com a propriedade vazia nas linhas escolhidas (ou em todas).", SelecionarVazios);
            Botao("Preencher...", "Grava um valor nos objetos das linhas escolhidas (cria o property set no objeto, se faltar).", Preencher);
            var mais = new Button { Content = "Mais ▾", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 14, 4), VerticalAlignment = VerticalAlignment.Center };
            mais.Click += (s, e) => MenuMais(mais);
            acoes.Children.Add(mais);
            _soPendencia = new CheckBox { Content = "Só com vazios", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 4),
                ToolTip = "Só o que tem objeto vazio, sem objeto do MetroDATA ou não sai no IFC." };
            _soPendencia.Click += (s, e) => _lista.Refresh();
            acoes.Children.Add(_soPendencia);

            // ---- lista
            _lista = new FilterableGrid(_linhas, typeof(LinhaMetroData));
            Theme.StyleSelection(_lista.Grid);
            _lista.AddColumn("Objeto do MetroDATA", nameof(LinhaMetroData.ObjetoTexto), 220);
            _lista.AddColumn("Property set.propriedade", nameof(LinhaMetroData.Campo), 300, fill: true);
            void Num(string titulo, string prop, string ordem, double w)
            {
                var c = _lista.AddColumn(titulo, prop, w);
                c.SortMemberPath = ordem;
                c.ElementStyle = new Style(typeof(TextBlock), c.ElementStyle) { Setters = { new Setter(TextBlock.TextAlignmentProperty, System.Windows.TextAlignment.Right) } };
            }
            Num("Classe", nameof(LinhaMetroData.ClasseTexto), nameof(LinhaMetroData.Classe), 60);
            Num("Objetos", nameof(LinhaMetroData.ObjetosTexto), nameof(LinhaMetroData.Objetos), 80);
            Num("Preenchidos", nameof(LinhaMetroData.PreenchidosTexto), nameof(LinhaMetroData.Preenchidos), 150);
            Num("Vazios", nameof(LinhaMetroData.VaziosTexto), nameof(LinhaMetroData.Vazios), 80);
            _lista.ExtraFilter = o => o is LinhaMetroData l && (_soPendencia.IsChecked != true || l.Pendente);
            _lista.Filtered += Resumo;
            var estilo = new Style(typeof(DataGridRow), _lista.Grid.RowStyle);
            var pend = new DataTrigger { Binding = new Binding(nameof(LinhaMetroData.Pendente)), Value = true };
            pend.Setters.Add(new Setter(Control.BackgroundProperty, Theme.LinhaProblema));
            estilo.Triggers.Add(pend);
            estilo.Setters.Add(new Setter(ToolTipProperty, new Binding(nameof(LinhaMetroData.Dica))));
            _lista.Grid.RowStyle = estilo;
            _lista.Grid.MouseDoubleClick += (s, e) => { if (_lista.Grid.SelectedItem is LinhaMetroData l && l.SemObjeto) EscolherObjetos(); };

            _resumo.TextWrapping = TextWrapping.NoWrap;
            _resumo.TextTrimming = TextTrimming.CharacterEllipsis;
            _resumo.Margin = new Thickness(0, 4, 0, 0);
            DockPanel.SetDock(_resumo, Dock.Bottom);
            var nota = Theme.Detalhe("Cada layer (ou bloco) é ligado a um objeto do MetroDATA pela Classe IFC dele; o botão Objetos deixa escolher. " +
                "A planilha é lida de novo a cada conferência. Preencher...: grava um valor nos objetos das linhas escolhidas.");
            nota.Margin = new Thickness(0, 2, 0, 0);
            DockPanel.SetDock(nota, Dock.Bottom);

            _corpo = new DockPanel();
            _corpo.Children.Add(filtros);
            _corpo.Children.Add(acoes);
            _corpo.Children.Add(nota);
            _corpo.Children.Add(_resumo);
            _corpo.Children.Add(_lista);

            var fechar = new Button { Content = "Fechar", IsCancel = true };
            fechar.Click += (s, e) => Close();
            Theme.Apply(this, "MetroDATA", Path.GetFileName(doc.Name) + " · propriedades que a planilha do MetroDATA pede", _corpo, Theme.Rodape(null, fechar));
            Loaded += (s, e) => Carregar();
        }

        // ================================================================== dados

        void Carregar()
        {
            _esc = EscolhasMetroData.Ler(_doc);
            _md = null;
            string erro = null;
            if (!string.IsNullOrWhiteSpace(_esc.Arquivo))
            {
                if (!File.Exists(_esc.Arquivo)) erro = "A planilha não está neste computador: " + _esc.Arquivo;
                else
                {
                    try { _md = PlanilhaMetroData.Ler(_esc.Arquivo); }
                    catch (Exception ex) { erro = ex.Message; }
                }
            }
            _carregando = true;
            try
            {
                _arquivo.Content = _md != null ? Path.GetFileName(_md.Arquivo) : "Escolher planilha...";
                _arquivo.ToolTip = _md != null ? _md.Arquivo + "\nClique para abrir. Trocar: Mais ▾." : "Planilha .xlsx exportada do Airtable (aba \"Propriedades x Objetos\").";
                Encher(_disciplina, _md?.Disciplinas(), _esc.Disciplina);
                Encher(_etapa, _md?.Etapas(), _esc.Etapa);
                var classes = (_md?.Classes().Keys ?? Enumerable.Empty<int>()).Select(n => "até a Classe " + n).ToList();
                Encher(_classe, classes, _esc.Classe > 0 ? "até a Classe " + _esc.Classe : Todas);
            }
            finally { _carregando = false; }
            Analisar(erro);
        }

        static void Encher(ComboBox c, List<string> itens, string atual)
        {
            var l = new List<string> { Todas };
            if (itens != null) l.AddRange(itens);
            c.ItemsSource = l;
            c.SelectedItem = l.FirstOrDefault(x => string.Equals(x, atual, StringComparison.OrdinalIgnoreCase)) ?? Todas;
        }

        void MudouFiltro()
        {
            if (_esc == null) return;
            _esc.Disciplina = Valor(_disciplina);
            _esc.Etapa = Valor(_etapa);
            _esc.Classe = PlanilhaMetroData.NumeroClasse(Valor(_classe));
            Gravar();
            Analisar(null);
        }

        static string Valor(ComboBox c)
        {
            string v = c.SelectedItem as string ?? c.Text;
            return PlanilhaMetroData.EhTodas(v) ? null : v.Trim();
        }

        void Gravar()
        {
            try { _esc.Gravar(_doc); }
            catch (Exception ex) { Log.Erro("gravar escolhas do MetroDATA", ex); }
        }

        void Analisar(string erro)
        {
            _linhas.Clear();
            _grupos = new List<GrupoModelo>();
            if (_md == null)
            {
                _objetos.Content = "Objetos";
                _objetos.IsEnabled = false;
                _resumo.Text = erro ?? "Escolha a planilha do MetroDATA (.xlsx exportado do Airtable) para conferir as propriedades pedidas.";
                _resumo.Foreground = Theme.Solid(Theme.LaranjaTexto);
                return;
            }
            Cursor = Cursors.Wait;
            try
            {
                foreach (var l in ConferenciaMetroData.Analisar(_doc, _md, _esc, out _grupos)) _linhas.Add(l);
            }
            catch (Exception ex) { erro = "Não foi possível conferir: " + ex.Message; }
            finally { Cursor = null; }
            int sem = _grupos.Count(g => g.Objeto == null);
            _objetos.IsEnabled = true;
            _objetos.Content = sem > 0 ? $"Objetos ({sem} sem)" : "Objetos";
            _objetos.Style = sem > 0 ? Theme.PrimaryButton : null;
            _lista.Refresh();
            Resumo();
            if (erro != null) { _resumo.Text = erro; _resumo.Foreground = Theme.Solid(Theme.LaranjaTexto); }
        }

        void Resumo()
        {
            if (_md == null) return;
            int vazios = _linhas.Where(l => l.Vazios > 0).Sum(l => l.Vazios ?? 0);
            int campos = _linhas.Count(l => l.Vazios > 0);
            int naoSai = _linhas.Count(l => l.NaoSaiNoIfc);
            int sem = _linhas.Where(l => l.SemObjeto).Sum(l => l.Objetos);
            var partes = new List<string> { $"{_linhas.Count(l => !l.SemObjeto)} propriedade(s) pedida(s)" };
            if (campos > 0) partes.Add($"{campos} com vazios ({vazios:N0} valores)");
            if (naoSai > 0) partes.Add($"{naoSai} não sai(em) no IFC (passo 3, Saída no IFC)");
            if (sem > 0) partes.Add($"{sem:N0} objeto(s) sem objeto do MetroDATA");
            if (campos + naoSai + sem == 0) partes.Add("tudo preenchido");
            _resumo.Text = string.Join(" · ", partes);
            _resumo.Foreground = Theme.Solid(campos + naoSai + sem == 0 ? Theme.Verde : Theme.LaranjaTexto);
        }

        // ================================================================== ações

        List<LinhaMetroData> Escolhidas()
        {
            var sel = _lista.Grid.SelectedItems.OfType<LinhaMetroData>().ToList();
            return sel.Count > 0 ? sel : _lista.VisibleItems<LinhaMetroData>().ToList();
        }

        void SelecionarVazios()
        {
            var ids = Escolhidas().SelectMany(l => l.SemObjeto ? l.Ids : l.IdsVazios).Distinct().Where(id => id.IsValid && !id.IsErased).ToArray();
            try { _doc.Editor.SetImpliedSelection(ids); } catch { }
            _resumo.Text = $"{ids.Length} objeto(s) selecionado(s) no modelo.";
            _resumo.Foreground = Theme.Solid(Theme.Preto);
        }

        void Preencher()
        {
            var linhas = _lista.Grid.SelectedItems.OfType<LinhaMetroData>().Where(l => l.PsetModelo != null).ToList();
            if (linhas.Count == 0)
            {
                MessageBox.Show(this, "Escolha uma ou mais linhas lidas de um property set do modelo.\n\n" +
                    "As que \"não saem no IFC\" precisam primeiro ser mapeadas (Mais ▾ › Mapear na Saída no IFC).", Title);
                return;
            }
            var valor = new TextBox { MinWidth = 360, Margin = new Thickness(0, 6, 0, 0) };
            var soVazios = new CheckBox { Content = "Só nos objetos com o valor vazio", IsChecked = true, Margin = new Thickness(0, 8, 0, 0) };
            var ok = new Button { Content = "Preencher", IsDefault = true, Style = Theme.PrimaryButton, MinWidth = 90, Margin = new Thickness(0, 0, 6, 0) };
            var cancelar = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 80 };
            var w = new Window { Title = "MetroSP - Preencher", Owner = this, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
            ok.Click += (s, e) => w.DialogResult = true;
            var corpo = new StackPanel { Margin = new Thickness(14) };
            corpo.Children.Add(new TextBlock { Text = "Valor para " + string.Join(", ", linhas.Select(l => l.PsetModelo + " › " + l.PropModelo).Distinct().Take(4)) + (linhas.Count > 4 ? "..." : "") + ":" });
            corpo.Children.Add(valor);
            corpo.Children.Add(soVazios);
            var bt = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            bt.Children.Add(ok);
            bt.Children.Add(cancelar);
            corpo.Children.Add(bt);
            w.Content = corpo;
            w.Loaded += (s, e) => valor.Focus();
            if (w.ShowDialog() != true) return;

            var msgs = new List<string>();
            try
            {
                using (_doc.LockDocument())
                using (var tr = _doc.Database.TransactionManager.StartTransaction())
                {
                    foreach (var g in linhas.GroupBy(l => l.PsetModelo, StringComparer.OrdinalIgnoreCase))
                    {
                        var valores = g.SelectMany(l => (soVazios.IsChecked == true ? l.IdsVazios : l.Ids).Select(id => (id, l.PropModelo, valor.Text))).ToList();
                        if (valores.Count > 0) msgs.Add(g.Key + ": " + ClassificacaoCore.Gravar(_doc.Database, tr, g.Key, valores));
                    }
                    tr.Commit();
                }
            }
            catch (Exception ex) { msgs.Add("Erro: " + ex.Message); }
            Analisar(null);
            if (msgs.Count > 0) MessageBox.Show(this, string.Join("\n\n", msgs), Title);
        }

        void MenuMais(Button botao)
        {
            var m = new ContextMenu { PlacementTarget = botao, Placement = PlacementMode.Bottom };
            void Item(string texto, string dica, Action a, bool ativo = true)
            {
                var i = new MenuItem { Header = texto, ToolTip = dica, IsEnabled = ativo };
                i.Click += (s, e) => a();
                m.Items.Add(i);
            }
            Item("Trocar planilha...", "Escolhe outra planilha do MetroDATA (.xlsx exportado do Airtable).", EscolherPlanilha);
            Item("Abrir a planilha", "", () => Abrir(_md?.Arquivo), _md != null);
            Item("Ler a planilha e o modelo de novo", "", Carregar);
            m.Items.Add(new Separator());
            int naoSai = _linhas.Count(l => l.NaoSaiNoIfc);
            Item($"Mapear na Saída no IFC as que não saem ({naoSai})", "Acrescenta ao perfil de exportação cada propriedade, lida de um property set do modelo de mesmo nome (Pset e propriedade do MetroDATA).", MapearNaoSaem, naoSai > 0);
            Item("Exportar lista (.csv)...", "Salva a lista para abrir no Excel.", Exportar, _linhas.Count > 0);
            m.IsOpen = true;
        }

        void EscolherPlanilha()
        {
            var dlg = new OpenFileDialog { Title = "Planilha do MetroDATA (Airtable)", Filter = "Planilha do Excel (*.xlsx)|*.xlsx|Todos os arquivos (*.*)|*.*" };
            if (_md != null) dlg.InitialDirectory = Path.GetDirectoryName(_md.Arquivo);
            if (dlg.ShowDialog(this) != true) return;
            try { PlanilhaMetroData.Ler(dlg.FileName); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            _esc.Arquivo = dlg.FileName;
            Gravar();
            Carregar();
        }

        static void Abrir(string arquivo)
        {
            if (string.IsNullOrEmpty(arquivo) || !File.Exists(arquivo)) return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(arquivo) { UseShellExecute = true }); } catch { }
        }

        void MapearNaoSaem()
        {
            var psets = _linhas.Where(l => l.NaoSaiNoIfc && !string.IsNullOrWhiteSpace(l.PsetIfc))
                .GroupBy(l => l.PsetIfc, StringComparer.OrdinalIgnoreCase)
                .Select(g => new PsetMontagem
                {
                    Nome = g.Key,
                    Propriedades = g.Select(l => l.PropIfc).Distinct(StringComparer.OrdinalIgnoreCase).Select(p => new PropriedadeMontagem { Nome = p }).ToList(),
                }).ToList();
            if (psets.Count == 0) return;
            string msg;
            try { msg = PsetsCore.LevarParaIfc(psets); }
            catch (Exception ex) { msg = "Não foi possível mapear: " + ex.Message; }
            Analisar(null);
            MessageBox.Show(this, (msg ?? "") + "\n\nO valor é lido do property set de mesmo nome no modelo. Para criar e anexar esse property set, use o passo 3 (Criar e anexar o que falta); " +
                "para ler de outra propriedade, ajuste o \"Vem de\" na Saída no IFC.", Title);
        }

        void Exportar()
        {
            var dlg = new SaveFileDialog { Title = "Exportar lista", Filter = "Planilha (*.csv)|*.csv", FileName = Path.GetFileNameWithoutExtension(_doc.Name) + " - MetroDATA.csv" };
            if (dlg.ShowDialog(this) != true) return;
            string Q(string s) => (s ?? "").IndexOfAny(new[] { ';', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s ?? "";
            var sb = new StringBuilder("Objeto do MetroDATA;Property set.propriedade;Classe;Objetos;Preenchidos;Vazios;Layers\r\n");
            foreach (var l in _linhas)
                sb.AppendLine(string.Join(";", new[] { l.ObjetoTexto, l.Campo, l.ClasseTexto, l.Objetos.ToString(), l.PreenchidosTexto, l.VaziosTexto, l.Layers }.Select(Q)));
            try { File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true)); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Title); }
        }

        // ================================================================== objetos de cada layer

        void EscolherObjetos()
        {
            if (_md == null) return;
            var objetos = _md.Objetos(_esc.Disciplina);
            var linhas = new StackPanel();
            var combos = new List<(GrupoModelo G, ComboBox C)>();
            var cab = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            foreach (var w in new[] { 260.0, 170, 70, 260 }) cab.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
            string[] titulos = { "Layer ou bloco", "Classe IFC", "Objetos", "Objeto do MetroDATA" };
            for (int i = 0; i < titulos.Length; i++)
            {
                var t = new TextBlock { Text = titulos[i], FontWeight = FontWeights.SemiBold };
                Grid.SetColumn(t, i);
                cab.Children.Add(t);
            }
            foreach (GrupoModelo g in _grupos.OrderBy(g => g.Objeto == null ? 0 : 1).ThenByDescending(g => g.Ids.Count))
            {
                var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                foreach (var w in new[] { 260.0, 170, 70, 260 }) row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
                void Txt(string s, int col, bool cinza = false)
                {
                    var t = new TextBlock { Text = s, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = s };
                    if (cinza) t.Foreground = Theme.Solid(Theme.Cinza);
                    Grid.SetColumn(t, col);
                    row.Children.Add(t);
                }
                Txt(g.Tipo.ToLowerInvariant() + " " + g.Nome, 0);
                Txt(string.IsNullOrEmpty(g.ClasseIfc) ? "sem classe" : g.ClasseIfc, 1, true);
                Txt(g.Ids.Count.ToString("N0"), 2, true);
                var itens = new List<string> { "" };
                itens.AddRange(g.Candidatos);
                itens.AddRange(objetos.Where(o => !g.Candidatos.Contains(o, StringComparer.OrdinalIgnoreCase)));
                var c = Theme.Digitavel(new ComboBox { ItemsSource = itens, Margin = new Thickness(0, 0, 4, 0),
                    ToolTip = g.Candidatos.Count > 0 ? "Pela classe IFC: " + string.Join(", ", g.Candidatos) : "Nenhum objeto do MetroDATA com essa classe IFC." });
                c.SelectedItem = itens.FirstOrDefault(x => string.Equals(x, g.Objeto, StringComparison.OrdinalIgnoreCase)) ?? "";
                if (g.Objeto == null) c.Background = Theme.LinhaProblema;
                Grid.SetColumn(c, 3);
                row.Children.Add(c);
                linhas.Children.Add(row);
                combos.Add((g, c));
            }
            var ok = new Button { Content = "Aplicar", IsDefault = true, Style = Theme.PrimaryButton, MinWidth = 90, Margin = new Thickness(0, 0, 6, 0) };
            var cancelar = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 80 };
            var win = new Window { Title = "MetroSP - Objetos do MetroDATA", Owner = this, Width = 820, Height = 560, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
            ok.Click += (s, e) => win.DialogResult = true;
            var dock = new DockPanel { Margin = new Thickness(14) };
            var topo = Theme.Detalhe("Cada layer (ou bloco) recebe as propriedades do objeto do MetroDATA escolhido. Vazio: pela classe IFC, quando só um objeto tem essa classe.");
            topo.Margin = new Thickness(0, 0, 0, 8);
            DockPanel.SetDock(topo, Dock.Top);
            dock.Children.Add(topo);
            DockPanel.SetDock(cab, Dock.Top);
            dock.Children.Add(cab);
            var bt = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            bt.Children.Add(ok);
            bt.Children.Add(cancelar);
            DockPanel.SetDock(bt, Dock.Bottom);
            dock.Children.Add(bt);
            dock.Children.Add(new ScrollViewer { Content = linhas, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            win.Content = dock;
            if (win.ShowDialog() != true) return;

            foreach (var (g, c) in combos)
            {
                string v = (c.SelectedItem as string ?? c.Text ?? "").Trim();
                bool automatico = v.Length == 0 || (g.Candidatos.Count == 1 && string.Equals(v, g.Candidatos[0], StringComparison.OrdinalIgnoreCase) && !g.Escolhido);
                if (automatico) _esc.Objetos.Remove(g.Chave);
                else _esc.Objetos[g.Chave] = v;
            }
            Gravar();
            Analisar(null);
        }
    }
}
