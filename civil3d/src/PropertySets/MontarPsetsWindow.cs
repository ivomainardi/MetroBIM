using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using Autodesk.AutoCAD.DatabaseServices;
using MetroSP.Ui;
using Microsoft.Win32;
using AcDocument = Autodesk.AutoCAD.ApplicationServices.Document;

namespace MetroSP.PropertySets
{
    /// <summary>
    /// Property sets numa lista única (perfil e modelo): cada property set é um grupo com onde está (perfil e modelo, só no
    /// perfil, só no modelo), a quais objetos se aplica e em quantos já está anexado; embaixo, as propriedades com o tipo e
    /// quantos objetos estão com ela vazia. "Criar e anexar o que falta" cria no modelo o que só está no perfil (ou foi
    /// montado aqui) e anexa aos objetos que vão para o IFC.
    /// </summary>
    public class MontarPsetsWindow : Window
    {
        const string NoModelo = "No modelo";   // situação que o PsetsCore.Comparar devolve

        readonly AcDocument _doc;
        readonly Database _db;
        readonly List<ObjectId> _selecao;

        readonly List<PsetMontagem> _psets = new List<PsetMontagem>();
        HashSet<string> _doPerfil = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, HashSet<string>> _propsPerfil = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        List<ObjetoAnexar> _objetos = new List<ObjetoAnexar>();
        Dictionary<string, int> _vazios = new Dictionary<string, int>();
        List<string> _classesModelo;

        readonly ObservableCollection<LinhaPropriedade> _linhas = new ObservableCollection<LinhaPropriedade>();
        readonly FilterableGrid _lista;
        readonly CheckBox _soIfc, _soPendencia;
        readonly TextBlock _resumo = Theme.Detalhe();
        readonly Button _criarAnexar;
        readonly SaidaIfcPainel _saida;
        bool _saidaCarregada;

        public MontarPsetsWindow(AcDocument doc, List<ObjectId> selecao)
        {
            _doc = doc;
            _db = doc.Database;
            _selecao = selecao != null && selecao.Count > 0 ? selecao : null;

            Title = "MetroSP - Property sets";
            Width = 1180;
            Height = 760;
            MinWidth = 820;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = Theme.Solid(Theme.Preto);

            // ---- barra
            var barra = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(barra, Dock.Top);
            Button Botao(string texto, string dica, Action a, bool primario = false)
            {
                var b = new Button { Content = texto, ToolTip = dica, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 4), VerticalAlignment = VerticalAlignment.Center };
                if (primario) b.Style = Theme.PrimaryButton;
                b.Click += (s, e) => a();
                barra.Children.Add(b);
                return b;
            }
            _criarAnexar = Botao("Criar e anexar o que falta",
                "Cria no modelo os property sets e as propriedades que só estão no perfil (ou que você montou aqui) e anexa os " +
                "property sets do perfil aos objetos que vão para o IFC" + (_selecao != null ? " (só nos selecionados)" : "") + ", conforme o \"Aplica-se a\" de cada um. Nada é apagado.",
                CriarEAnexar, primario: true);
            Botao("Selecionar objetos", "Seleciona no modelo os objetos que vão para o IFC sem algum property set do perfil " +
                "(ou, com uma linha escolhida, os objetos a que o property set dela se aplica).", SelecionarObjetos);
            Botao("+ Property set", "Monta um property set novo na lista (fica \"só aqui\" até Criar).", NovoPset);
            var mais = new Button { Content = "Mais ▾", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 14, 4), VerticalAlignment = VerticalAlignment.Center };
            mais.Click += (s, e) => MenuMais(mais);
            barra.Children.Add(mais);
            _soIfc = new CheckBox { Content = "Só o que vai para o IFC", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 4),
                ToolTip = "Esconde os property sets que só estão no modelo e não estão anexados a nenhum objeto que vai para o IFC." };
            _soPendencia = new CheckBox { Content = "Só com pendência", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 4),
                ToolTip = "Só o que falta criar, anexar ou preencher." };
            _soIfc.Click += (s, e) => _lista.Refresh();
            _soPendencia.Click += (s, e) => _lista.Refresh();
            barra.Children.Add(_soIfc);
            barra.Children.Add(_soPendencia);

            // ---- lista
            _lista = new FilterableGrid(_linhas, typeof(LinhaPropriedade));
            _lista.Grid.IsReadOnly = false;
            Theme.StyleSelection(_lista.Grid);
            VirtualizingPanel.SetIsVirtualizingWhenGrouping(_lista.Grid, true);
            _lista.AddColumn("Propriedade", nameof(LinhaPropriedade.Propriedade), 220, fill: true);
            _lista.AddColumn("Onde", nameof(LinhaPropriedade.Onde), 120).IsReadOnly = true;
            var tipo = _lista.AddColumn("Tipo", nameof(LinhaPropriedade.Tipo), 90);
            int iTipo = _lista.Grid.Columns.IndexOf(tipo);
            _lista.Grid.Columns.Remove(tipo);
            _lista.Grid.Columns.Insert(iTipo, new DataGridComboBoxColumn
            {
                Header = tipo.Header, ItemsSource = PsetsCore.NomesTipos, Width = 90, SortMemberPath = nameof(LinhaPropriedade.Tipo),
                SelectedItemBinding = new Binding(nameof(LinhaPropriedade.Tipo)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            });
            _lista.AddColumn("Valor padrão", nameof(LinhaPropriedade.Padrao), 130);
            var vaz = _lista.AddColumn("Vazios", nameof(LinhaPropriedade.VaziosTexto), 70);
            vaz.IsReadOnly = true;
            vaz.SortMemberPath = nameof(LinhaPropriedade.Vazios);
            vaz.ElementStyle = new Style(typeof(TextBlock), vaz.ElementStyle) { Setters = { new Setter(TextBlock.TextAlignmentProperty, System.Windows.TextAlignment.Right) } };
            _lista.Grid.BeginningEdit += (s, e) => { if (e.Row.Item is LinhaPropriedade l && !l.Editavel) e.Cancel = true; };
            _lista.Grid.CellEditEnding += (s, e) => Dispatcher.BeginInvoke(new Action(Atualizar), System.Windows.Threading.DispatcherPriority.Background);
            _lista.ExtraFilter = o => o is LinhaPropriedade l && Passa(l);
            _lista.Filtered += Resumo;
            _lista.Grid.ToolTip = "Clique na célula para editar as propriedades que ainda não estão no modelo. No título do grupo: Aplica-se a, renomear e + propriedade.";

            var estilo = new Style(typeof(DataGridRow), _lista.Grid.RowStyle);
            var pend = new DataTrigger { Binding = new Binding(nameof(LinhaPropriedade.Pendente)), Value = true };
            pend.Setters.Add(new Setter(BackgroundProperty, Theme.LinhaProblema));
            estilo.Triggers.Add(pend);
            var soModelo = new DataTrigger { Binding = new Binding(nameof(LinhaPropriedade.SoNoModelo)), Value = true };
            soModelo.Setters.Add(new Setter(ForegroundProperty, Theme.Solid(Theme.Cinza)));
            estilo.Triggers.Add(soModelo);
            _lista.Grid.RowStyle = estilo;
            _lista.Grid.GroupStyle.Add(EstiloGrupo());
            if (_lista.Grid.ItemsSource is ICollectionView v) v.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LinhaPropriedade.Grupo)));

            _resumo.TextWrapping = TextWrapping.NoWrap;
            _resumo.TextTrimming = TextTrimming.CharacterEllipsis;
            _resumo.Margin = new Thickness(0, 4, 0, 0);
            DockPanel.SetDock(_resumo, Dock.Bottom);

            var vistaModelo = new DockPanel();
            vistaModelo.Children.Add(barra);
            vistaModelo.Children.Add(_resumo);
            vistaModelo.Children.Add(_lista);

            // ---- Saída no IFC: nome de saída de cada propriedade e de qual propriedade do modelo o valor vem.
            _saida = new SaidaIfcPainel(this, () => _psets,
                pset => _objetos.Count(o => o.Nomes.Contains(pset)),
                (pset, prop) => _vazios.TryGetValue(pset + "\u0001" + prop, out int n) ? n : 0,
                PropriedadesDoModelo);
            _saida.Salvo += RecarregarPerfil;

            var area = new ContentControl { Content = vistaModelo };
            var seg = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(seg, Dock.Top);
            Button Aba(string texto, string dica)
            {
                var b = new Button { Content = texto, ToolTip = dica, Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 0, -1, 0) };
                seg.Children.Add(b);
                return b;
            }
            var abaModelo = Aba("No modelo", "Os property sets do modelo e os que o perfil pede: criar, anexar e ver os vazios.");
            var abaSaida = Aba("Saída no IFC", "Cada propriedade que sai no IFC: o nome no IFC e de qual propriedade do modelo o valor vem.");
            void Mostrar(bool saida)
            {
                area.Content = saida ? (UIElement)_saida : vistaModelo;
                foreach (var (b, on) in new[] { (abaModelo, !saida), (abaSaida, saida) })
                {
                    b.Background = on ? Theme.Solid(Theme.Preto) : System.Windows.Media.Brushes.White;
                    b.Foreground = on ? System.Windows.Media.Brushes.White : Theme.Solid(Theme.Preto);
                    b.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
                }
                if (saida && !_saidaCarregada) { _saidaCarregada = true; _saida.Carregar(); }
            }
            abaModelo.Click += (s, e) => Mostrar(false);
            abaSaida.Click += (s, e) => Mostrar(true);
            Mostrar(false);

            var corpo = new DockPanel();
            corpo.Children.Add(seg);
            corpo.Children.Add(area);
            Closing += (s, e) =>
            {
                if (!_saida.Alterado) return;
                var r = MessageBox.Show(this, "A Saída no IFC tem alterações não salvas. Salvar no perfil antes de fechar?", Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (r == MessageBoxResult.Cancel || (r == MessageBoxResult.Yes && !_saida.Salvar())) e.Cancel = true;
            };

            var fechar = new Button { Content = "Fechar", IsCancel = true };
            fechar.Click += (s, e) => Close();
            Theme.Apply(this, "Property sets", Path.GetFileName(doc.Name) + " · o que o perfil pede e o que está no modelo", corpo, Theme.Rodape(null, fechar));
            Loaded += (s, e) => Carregar();
        }

        // ================================================================== grupo (property set)

        GroupStyle EstiloGrupo()
        {
            var cab = new FrameworkElementFactory(typeof(DockPanel));
            cab.SetValue(MarginProperty, new Thickness(0, 4, 0, 2));
            cab.SetBinding(DataContextProperty, new Binding("Name"));

            FrameworkElementFactory Texto(string prop, bool negrito = false, double margem = 14)
            {
                var t = new FrameworkElementFactory(typeof(TextBlock));
                t.SetBinding(TextBlock.TextProperty, new Binding(prop));
                t.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
                t.SetValue(MarginProperty, new Thickness(0, 0, margem, 0));
                if (negrito) t.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
                return t;
            }
            FrameworkElementFactory Link(string texto, string dica, Action<PsetVista, FrameworkElement> acao)
            {
                var b = new FrameworkElementFactory(typeof(Button));
                b.SetValue(ContentControl.ContentProperty, texto);
                b.SetValue(ToolTipProperty, dica);
                b.SetValue(Control.PaddingProperty, new Thickness(8, 0, 8, 1));
                b.SetValue(MarginProperty, new Thickness(0, 0, 6, 0));
                b.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
                b.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((s, e) =>
                {
                    if (((FrameworkElement)s).DataContext is PsetVista p) acao(p, (FrameworkElement)s);
                    e.Handled = true;
                }));
                return b;
            }

            var nome = Texto(nameof(PsetVista.Nome), negrito: true);
            nome.SetValue(ToolTipProperty, "Clique duas vezes para renomear (só o que ainda não está no modelo).");
            nome.AddHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler((s, e) =>
            {
                if (e.ClickCount == 2 && ((FrameworkElement)s).DataContext is PsetVista p) { Renomear(p); e.Handled = true; }
            }));
            var onde = Texto(nameof(PsetVista.Onde));
            onde.SetValue(TextBlock.ForegroundProperty, Theme.Solid(Theme.Cinza));
            var anexado = Texto(nameof(PsetVista.Anexado));
            anexado.SetBinding(ToolTipProperty, new Binding(nameof(PsetVista.AnexadoDica)));
            var ifc = Texto(nameof(PsetVista.Situacao));
            ifc.SetValue(TextBlock.ForegroundProperty, Theme.Solid(Theme.LaranjaTexto));

            foreach (var f in new[] { nome, onde, anexado })
            {
                f.SetValue(DockPanel.DockProperty, Dock.Left);
                cab.AppendChild(f);
            }
            var aplica = Link("", "A quais tipos de objeto o property set se aplica (só eles podem recebê-lo).", (p, b) => EscolherAplica(p, b));
            aplica.SetBinding(ContentControl.ContentProperty, new Binding(nameof(PsetVista.AplicaTexto)));
            aplica.SetValue(DockPanel.DockProperty, Dock.Left);
            cab.AppendChild(aplica);
            var mais = Link("+ propriedade", "Acrescenta uma propriedade a este property set.", (p, b) => NovaPropriedade(p));
            mais.SetValue(DockPanel.DockProperty, Dock.Left);
            cab.AppendChild(mais);
            var tirar = Link("tirar", "Tira da lista o que ainda não está no modelo (o que já está fica; para apagar, use o Style Manager).", (p, b) => Tirar(p));
            tirar.SetBinding(VisibilityProperty, new Binding(nameof(PsetVista.PodeTirar)) { Converter = new BooleanToVisibilityConverter() });
            tirar.SetValue(DockPanel.DockProperty, Dock.Left);
            cab.AppendChild(tirar);
            cab.AppendChild(ifc);

            var exp = new FrameworkElementFactory(typeof(Expander));
            exp.SetValue(Expander.IsExpandedProperty, true);
            exp.SetValue(Control.BackgroundProperty, Theme.Solid(Theme.CinzaClaro));
            exp.SetValue(Expander.HeaderTemplateProperty, new DataTemplate { VisualTree = cab });
            exp.SetBinding(HeaderedContentControl.HeaderProperty, new Binding());
            exp.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
            var cont = new Style(typeof(GroupItem));
            cont.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(GroupItem)) { VisualTree = exp }));
            return new GroupStyle { ContainerStyle = cont };
        }

        // ================================================================== dados

        void Carregar()
        {
            Cursor = Cursors.Wait;
            try
            {
                _psets.Clear();
                _psets.AddRange(PsetsCore.LerDesenho(_db));
                List<PsetMontagem> perfil;
                try { perfil = PsetsCore.DoMapeamentoIfc(); } catch { perfil = new List<PsetMontagem>(); }
                _doPerfil = new HashSet<string>(perfil.Select(p => p.Nome), StringComparer.OrdinalIgnoreCase);
                _propsPerfil = perfil.GroupBy(p => p.Nome, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => new HashSet<string>(g.SelectMany(p => p.Propriedades).Select(x => x.Nome), StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
                Juntar(perfil);
                LerObjetos();
            }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível ler os property sets do modelo:\n" + ex.Message, Title); }
            finally { Cursor = null; }
            Atualizar();
        }

        void LerObjetos()
        {
            try
            {
                _objetos = PsetsCore.Objetos(_db, _selecao);
                _vazios = PsetsCore.Vazios(_db, _objetos, _psets);
            }
            catch { _objetos = new List<ObjetoAnexar>(); _vazios = new Dictionary<string, int>(); }
        }

        /// <summary>Junta property sets à lista: os de mesmo nome ganham as propriedades que faltam. Devolve (psets, propriedades) novos.</summary>
        (int, int) Juntar(IEnumerable<PsetMontagem> novos)
        {
            int ps = 0, props = 0;
            foreach (PsetMontagem n in novos)
            {
                PsetMontagem e = _psets.FirstOrDefault(x => Igual(x.Nome, n.Nome));
                if (e == null) { _psets.Add(n); ps++; props += n.Propriedades.Count; continue; }
                foreach (PropriedadeMontagem pr in n.Propriedades)
                    if (!e.Propriedades.Any(x => Igual(x.Nome, pr.Nome))) { e.Propriedades.Add(pr); props++; }
                if (!n.TodosObjetos && !e.TodosObjetos)
                    foreach (string c in n.Classes) if (!e.Classes.Contains(c, StringComparer.OrdinalIgnoreCase)) e.Classes.Add(c);
                if (string.IsNullOrEmpty(e.Descricao)) e.Descricao = n.Descricao;
            }
            return (ps, props);
        }

        static bool Igual(string a, string b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        bool Aplica(PsetMontagem p, ObjetoAnexar o) =>
            p.TodosObjetos || p.Classes.Count == 0 || p.Classes.Any(c => o.Linhagem != null && o.Linhagem.Contains(c));

        /// <summary>Compara com o modelo e monta as linhas de novo (guarda a seleção).</summary>
        void Atualizar()
        {
            _lista.Grid.CommitEdit(DataGridEditingUnit.Row, true);
            try { PsetsCore.Comparar(_db, _psets); } catch { }
            var sel = new HashSet<string>(_lista.Grid.SelectedItems.OfType<LinhaPropriedade>().Select(l => l.Chave));
            var linhas = new List<LinhaPropriedade>();
            foreach (PsetMontagem p in _psets.OrderBy(p => Ordem(p)).ThenBy(p => p.Nome, StringComparer.CurrentCultureIgnoreCase))
            {
                var aplicaveis = _objetos.Where(o => Aplica(p, o)).ToList();
                int anexados = aplicaveis.Count(o => o.Nomes.Contains(p.Nome));
                int emQualquer = _objetos.Count(o => o.Nomes.Contains(p.Nome));
                var g = new PsetVista(p, _doPerfil.Contains(p.Nome), aplicaveis.Count, anexados, emQualquer);
                _propsPerfil.TryGetValue(p.Nome, out var doPerfil);
                if (p.Propriedades.Count == 0) linhas.Add(new LinhaPropriedade(g, null, false, 0));
                foreach (PropriedadeMontagem pr in p.Propriedades)
                {
                    _vazios.TryGetValue(p.Nome + "\u0001" + pr.Nome, out int vz);
                    linhas.Add(new LinhaPropriedade(g, pr, doPerfil != null && doPerfil.Contains(pr.Nome), vz));
                }
            }
            _linhas.Clear();
            foreach (LinhaPropriedade l in linhas) _linhas.Add(l);
            foreach (LinhaPropriedade l in _linhas.Where(l => sel.Contains(l.Chave))) _lista.Grid.SelectedItems.Add(l);
            Resumo();
        }

        /// <summary>Propriedades personalizadas do modelo (Propriedades do desenho), para o "Vem de".</summary>
        IEnumerable<string> PropriedadesDoModelo()
        {
            var r = new List<string>();
            try
            {
                var e = _db.SummaryInfo.CustomProperties;
                while (e.MoveNext()) if (e.Key is string k) r.Add(k);
            }
            catch { }
            return r;
        }

        /// <summary>Depois de salvar a Saída no IFC: o que o perfil pede pode ter mudado.</summary>
        void RecarregarPerfil()
        {
            List<PsetMontagem> perfil;
            try { perfil = PsetsCore.DoMapeamentoIfc(); } catch { perfil = new List<PsetMontagem>(); }
            _doPerfil = new HashSet<string>(perfil.Select(p => p.Nome), StringComparer.OrdinalIgnoreCase);
            _propsPerfil = perfil.GroupBy(p => p.Nome, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => new HashSet<string>(g.SelectMany(p => p.Propriedades).Select(x => x.Nome), StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
            Juntar(perfil);
            Atualizar();
        }

        static int Ordem(PsetMontagem p) => p.NoDesenho ? 1 : 0;

        bool Passa(LinhaPropriedade l)
        {
            if (_soIfc.IsChecked == true && !l.Grupo.DoPerfil && l.Grupo.EmQualquer == 0 && l.Grupo.M.NoDesenho) return false;
            if (_soPendencia.IsChecked == true && !l.Pendente && !l.Grupo.Pendente) return false;
            return true;
        }

        void Resumo()
        {
            var grupos = _linhas.Select(l => l.Grupo).Distinct().ToList();
            int criar = grupos.Count(g => g.M.Situacao != NoModelo);
            int faltaAnexar = grupos.Where(g => g.DoPerfil || !g.M.NoDesenho).Sum(g => g.Faltam);
            int vazios = _linhas.Where(l => l.Vazios > 0).Select(l => l.Grupo).Distinct().Count();
            var partes = new List<string> { $"{grupos.Count} property set(s)" };
            if (criar > 0) partes.Add($"{criar} para criar ou completar no modelo");
            if (faltaAnexar > 0) partes.Add($"falta anexar {faltaAnexar} vez(es)");
            if (vazios > 0) partes.Add($"{vazios} com propriedade vazia (preencha no passo 4)");
            if (criar + faltaAnexar == 0) partes.Add("tudo criado e anexado");
            _resumo.Text = string.Join(" · ", partes) + $" · {_objetos.Count} objeto(s) que vão para o IFC" + (_selecao != null ? " (seleção)" : "");
            _resumo.Foreground = Theme.Solid(criar + faltaAnexar == 0 ? Theme.Verde : Theme.LaranjaTexto);
            _criarAnexar.IsEnabled = criar + faltaAnexar > 0;
        }

        // ================================================================== edição

        void NovoPset()
        {
            string nome = Theme.PedirTexto(this, "Novo property set", "Nome do property set (o mesmo do Pset no IFC):", "Novo property set")?.Trim();
            if (string.IsNullOrEmpty(nome)) return;
            if (_psets.Any(p => Igual(p.Nome, nome))) { MessageBox.Show(this, "Já existe um property set com esse nome.", Title); return; }
            _psets.Add(new PsetMontagem { Nome = nome, Propriedades = { new PropriedadeMontagem { Nome = "Propriedade" } } });
            _soIfc.IsChecked = false;
            Atualizar();
        }

        void Renomear(PsetVista g)
        {
            if (g.M.NoDesenho) { MessageBox.Show(this, "Este property set já está no modelo: renomeie no Style Manager.", Title); return; }
            string nome = Theme.PedirTexto(this, "Renomear property set", "Nome novo:", g.M.Nome)?.Trim();
            if (string.IsNullOrEmpty(nome) || Igual(nome, g.M.Nome)) return;
            if (_psets.Any(p => Igual(p.Nome, nome))) { MessageBox.Show(this, "Já existe um property set com esse nome.", Title); return; }
            g.M.Nome = nome;
            Atualizar();
        }

        void NovaPropriedade(PsetVista g)
        {
            string nome = Theme.PedirTexto(this, "Nova propriedade", "Nome da propriedade em " + g.M.Nome + ":", "")?.Trim();
            if (string.IsNullOrEmpty(nome)) return;
            if (g.M.Propriedades.Any(x => Igual(x.Nome, nome))) { MessageBox.Show(this, "Essa propriedade já existe.", Title); return; }
            g.M.Propriedades.Add(new PropriedadeMontagem { Nome = nome });
            Atualizar();
        }

        void Tirar(PsetVista g)
        {
            if (!g.M.NoDesenho) _psets.Remove(g.M);
            else g.M.Propriedades.RemoveAll(pr => !(pr.Situacao ?? "").StartsWith(NoModelo, StringComparison.Ordinal));
            Atualizar();
        }

        void EscolherAplica(PsetVista g, FrameworkElement alvo)
        {
            if (_classesModelo == null) _classesModelo = _objetos.Select(o => o.Classe).Distinct().ToList();
            var painel = new StackPanel { Margin = new Thickness(10) };
            var todos = new CheckBox { Content = "Todos os objetos", IsChecked = g.M.TodosObjetos || g.M.Classes.Count == 0, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
            painel.Children.Add(todos);
            var lista = new WrapPanel { Width = 380 };
            var marcadas = new HashSet<string>(g.M.Classes, StringComparer.OrdinalIgnoreCase);
            foreach (var (rx, nome) in PsetsCore.OpcoesClasses(_classesModelo.Concat(marcadas)))
            {
                var c = new CheckBox { Content = nome, Tag = rx, IsChecked = marcadas.Contains(rx), Width = 185, Margin = new Thickness(0, 2, 0, 2), ToolTip = rx };
                lista.Children.Add(c);
            }
            lista.IsEnabled = todos.IsChecked != true;
            todos.Click += (s, e) => lista.IsEnabled = todos.IsChecked != true;
            painel.Children.Add(lista);
            var ok = new Button { Content = "Aplicar", Style = Theme.PrimaryButton, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0), MinWidth = 80 };
            painel.Children.Add(ok);
            var pop = new Popup
            {
                PlacementTarget = alvo, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true,
                Child = new Border { Background = System.Windows.Media.Brushes.White, BorderBrush = Theme.Solid(Theme.Cinza), BorderThickness = new Thickness(1), Child = painel },
            };
            ok.Click += (s, e) =>
            {
                g.M.TodosObjetos = todos.IsChecked == true;
                g.M.Classes = g.M.TodosObjetos ? new List<string>() : lista.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToList();
                if (!g.M.TodosObjetos && g.M.Classes.Count == 0) g.M.TodosObjetos = true;
                pop.IsOpen = false;
                Atualizar();
            };
            pop.IsOpen = true;
        }

        void MenuMais(Button botao)
        {
            var m = new ContextMenu { PlacementTarget = botao, Placement = PlacementMode.Bottom };
            MenuItem Item(string texto, string dica, Action a)
            {
                var i = new MenuItem { Header = texto, ToolTip = dica };
                i.Click += (s, e) => a();
                m.Items.Add(i);
                return i;
            }
            var modelos = new MenuItem { Header = "Modelo pronto" };
            foreach (var (rotulo, dica, criar) in PsetsCore.Modelos())
            {
                var i = new MenuItem { Header = rotulo, ToolTip = dica };
                i.Click += (s, e) => { Juntar(new[] { criar() }); _soIfc.IsChecked = false; Atualizar(); };
                modelos.Items.Add(i);
            }
            m.Items.Add(modelos);
            Item("Importar planilha...", "Cria a estrutura a partir de uma planilha (uma linha por propriedade). Também lê o IFCInfraExportPropertyMapping.csv da Autodesk.", Importar);
            Item("Exportar planilha...", "Salva a lista em .csv para editar no Excel ou mandar para a equipe.", Exportar);
            m.Items.Add(new Separator());
            Item("Levar para o perfil IFC os só no modelo", "Acrescenta ao mapeamento do perfil (Configurar IFC › Propriedades) os property sets que estão no modelo mas o perfil não pede.", LevarParaPerfil);
            Item("Ler o modelo de novo", "Descarta o que não foi criado e lê de novo os property sets e os objetos.", Carregar);
            m.IsOpen = true;
        }

        void Importar()
        {
            var dlg = new OpenFileDialog { Title = "Importar property sets", Filter = Planilha.LeitorPlanilha.Filtro };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                List<PsetMontagem> lidos = PsetsCore.SeMapeamentoAutodesk(dlg.FileName);
                if (lidos == null)
                {
                    var w = new ImportarPsetsWindow(dlg.FileName) { Owner = this };
                    if (w.ShowDialog() != true) return;
                    lidos = w.Resultado;
                }
                if (lidos.Count == 0) { MessageBox.Show(this, "Nenhum property set encontrado na planilha.", Title); return; }
                var (ps, props) = Juntar(lidos);
                _soIfc.IsChecked = false;
                Atualizar();
                _resumo.Text = $"{Path.GetFileName(dlg.FileName)}: {ps} property set(s) e {props} propriedade(s) entraram na lista. Clique em \"Criar e anexar o que falta\".";
            }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível ler a planilha:\n" + ex.Message, Title); }
        }

        void Exportar()
        {
            if (_psets.Count == 0) return;
            var dlg = new SaveFileDialog { Title = "Exportar property sets", Filter = "Planilha CSV (*.csv)|*.csv", FileName = Path.GetFileNameWithoutExtension(_doc.Name) + " - property sets.csv" };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                var visiveis = new HashSet<PsetMontagem>(_lista.VisibleItems<LinhaPropriedade>().Select(l => l.Grupo.M));
                PsetsCore.ExportarPlanilha(dlg.FileName, _psets.Where(visiveis.Contains));
                _resumo.Text = "Planilha salva: " + dlg.FileName;
            }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível salvar:\n" + ex.Message, Title); }
        }

        void LevarParaPerfil()
        {
            var so = _psets.Where(p => p.NoDesenho && !_doPerfil.Contains(p.Nome) && _objetos.Any(o => o.Nomes.Contains(p.Nome))).ToList();
            if (so.Count == 0) { MessageBox.Show(this, "Nenhum property set anexado a objetos do IFC está fora do perfil.", Title); return; }
            foreach (PsetMontagem p in so) { p.LevarIfc = true; p.EntidadeIfc = "IfcProduct"; }
            string msg;
            try { msg = PsetsCore.LevarParaIfc(so) ?? ""; }
            catch (Exception ex) { msg = ex.Message; }
            MessageBox.Show(this, msg, Title);
            Carregar();
        }

        // ================================================================== criar, anexar, selecionar

        void CriarEAnexar()
        {
            _lista.Grid.CommitEdit(DataGridEditingUnit.Row, true);
            var dupProp = _psets.SelectMany(p => p.Propriedades.GroupBy(x => (x.Nome ?? "").Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => p.Nome + " › " + g.Key)).FirstOrDefault();
            if (dupProp != null) { MessageBox.Show(this, $"Propriedade repetida: {dupProp}.", Title); return; }
            if (_psets.Any(p => p.Propriedades.Any(x => string.IsNullOrWhiteSpace(x.Nome)))) { MessageBox.Show(this, "Há propriedade sem nome.", Title); return; }

            var criar = _psets.Where(p => p.Situacao != NoModelo).ToList();
            var anexar = new HashSet<string>(_psets.Where(p => _doPerfil.Contains(p.Nome) || !p.NoDesenho).Select(p => p.Nome.Trim()), StringComparer.OrdinalIgnoreCase);
            Cursor = Cursors.Wait;
            var msg = new List<string>();
            try
            {
                using (_doc.LockDocument())
                {
                    if (criar.Count > 0) msg.Add(PsetsCore.CriarNoDesenho(_db, criar));
                    List<ObjetoAnexar> objs = PsetsCore.Objetos(_db, _selecao);
                    PsetsCore.CalcularFaltas(_db, objs, anexar.ToList());
                    msg.Add(objs.Any(o => !string.IsNullOrEmpty(o.Faltam))
                        ? PsetsCore.Anexar(_db, objs) + "\nOs valores ficam em Propriedades › Extended Data."
                        : "Os objetos que vão para o IFC já tinham os property sets do perfil.");
                }
                // O que foi montado aqui (fora do perfil) entra no mapeamento IFC do perfil.
                foreach (PsetMontagem p in criar) p.LevarIfc = !_doPerfil.Contains(p.Nome);
                string ifc = PsetsCore.LevarParaIfc(criar.Where(p => p.LevarIfc));
                if (ifc != null) msg.Add(ifc);
            }
            catch (Exception ex) { msg.Add("Não foi possível terminar:\n" + ex.Message); }
            finally { Cursor = null; }
            Carregar();
            MessageBox.Show(this, string.Join("\n\n", msg), Title);
        }

        void SelecionarObjetos()
        {
            var grupos = _lista.Grid.SelectedItems.OfType<LinhaPropriedade>().Select(l => l.Grupo.M).Distinct().ToList();
            List<ObjectId> ids;
            if (grupos.Count > 0)
                ids = _objetos.Where(o => grupos.Any(p => Aplica(p, o))).Select(o => o.Id).ToList();
            else
            {
                var perfil = _psets.Where(p => _doPerfil.Contains(p.Nome)).ToList();
                ids = _objetos.Where(o => perfil.Any(p => Aplica(p, o) && !o.Nomes.Contains(p.Nome))).Select(o => o.Id).ToList();
            }
            try { _doc.Editor.SetImpliedSelection(ids.Where(id => id.IsValid && !id.IsErased).ToArray()); } catch { }
            _resumo.Text = $"{ids.Count} objeto(s) selecionado(s) no modelo.";
        }
    }

    /// <summary>Um property set na lista (o cabeçalho do grupo).</summary>
    public class PsetVista
    {
        public PsetMontagem M { get; }
        public bool DoPerfil { get; }
        public int Aplicaveis { get; }
        public int Anexados { get; }
        public int EmQualquer { get; }

        public PsetVista(PsetMontagem m, bool doPerfil, int aplicaveis, int anexados, int emQualquer)
        {
            M = m; DoPerfil = doPerfil; Aplicaveis = aplicaveis; Anexados = anexados; EmQualquer = emQualquer;
        }

        public string Nome => M.Nome;
        public string Onde => DoPerfil ? (M.NoDesenho ? "perfil e modelo" : "só no perfil") : M.NoDesenho ? "só no modelo" : "montado aqui";
        public string AplicaTexto => "Aplica-se a: " + M.AplicaA + " ▾";
        public int Faltam => M.NoDesenho ? Math.Max(0, Aplicaveis - Anexados) : Aplicaveis;
        public string Anexado => !M.NoDesenho ? "" : $"anexado em {Anexados.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"))} de {Aplicaveis.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"))}";
        public string AnexadoDica => $"{Aplicaveis} objeto(s) que vão para o IFC são de um tipo a que este property set se aplica; {Anexados} já têm ele anexado.";
        public string Situacao => M.Situacao == "No modelo" ? "" : M.Situacao == "Novo" ? "falta criar no modelo" : M.Situacao.Replace("Muda: ", "falta no modelo: ");
        public bool Pendente => M.Situacao != "No modelo" || ((DoPerfil || !M.NoDesenho) && Faltam > 0);
        public bool PodeTirar => !M.NoDesenho || M.Propriedades.Any(p => !(p.Situacao ?? "").StartsWith("No modelo", StringComparison.Ordinal));
        public override string ToString() => Nome;
    }

    /// <summary>Uma propriedade de um property set na lista.</summary>
    public class LinhaPropriedade
    {
        readonly PropriedadeMontagem _p;
        readonly bool _doPerfil;

        public LinhaPropriedade(PsetVista grupo, PropriedadeMontagem p, bool doPerfil, int vazios)
        {
            Grupo = grupo; _p = p; _doPerfil = doPerfil; Vazios = vazios;
        }

        public PsetVista Grupo { get; }
        public string Chave => Grupo.Nome + "\u0001" + (_p?.Nome ?? "");
        bool NoModelo => _p != null && (_p.Situacao ?? "").StartsWith("No modelo", StringComparison.Ordinal);
        public bool Editavel => _p != null && !NoModelo && !_p.Automatica;

        public string Propriedade
        {
            get => _p?.Nome ?? "(sem propriedades)";
            set { if (Editavel && !string.IsNullOrWhiteSpace(value)) _p.Nome = value.Trim(); }
        }
        public string Tipo
        {
            get => _p == null ? "" : _p.TipoTexto;
            set { if (Editavel && value != null) _p.TipoTexto = value; }
        }
        public string Padrao
        {
            get => _p?.Padrao ?? "";
            set { if (Editavel) _p.Padrao = value ?? ""; }
        }
        public string Onde => _p == null ? "" : _doPerfil ? (NoModelo ? "perfil e modelo" : "só no perfil") : NoModelo ? "só no modelo" : "montada aqui";
        public int Vazios { get; }
        public string VaziosTexto => !NoModelo ? "—" : Vazios.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"));
        public bool Pendente => _p != null && (!NoModelo || (Vazios > 0 && _doPerfil));
        public bool SoNoModelo => !Grupo.DoPerfil && Grupo.M.NoDesenho;
    }
}
