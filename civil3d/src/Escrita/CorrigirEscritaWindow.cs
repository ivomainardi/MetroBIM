using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MetroSP.Confronto;
using MetroSP.Ui;

namespace MetroSP.Escrita
{
    /// <summary>Um valor com problema de escrita (num objeto, ou no tipo que vários objetos usam).</summary>
    public class LinhaEscrita : INotifyPropertyChanged
    {
        public const string Corrigir = "Corrigir", Revisar = "Revisar", NaoGrava = "Não grava", Ignorado = "Ignorado", Corrigido = "Corrigido";

        private string _situacao, _sugestao;

        public string Situacao { get => _situacao; set { _situacao = value; Mudou(nameof(Situacao)); } }
        public string Elemento { get; set; }
        public string Id { get; set; }
        public string Categoria { get; set; }
        /// <summary>Dica da linha: o que saiu das colunas (situação, motivo, categoria e Id).</summary>
        public string Detalhe => string.Join(" · ", new[] { Situacao, Motivo, Categoria, string.IsNullOrEmpty(Id) ? null : "Id " + Id }.Where(x => !string.IsNullOrEmpty(x)));
        public string Campo { get; set; }
        public string Atual { get; set; }
        /// <summary>O valor atual com os espaços sobrando e os caracteres invisíveis à mostra.</summary>
        public string AtualVisivel => Mostrar(Atual);
        public string Problemas { get; set; }
        public string Motivo { get; set; }

        /// <summary>Editável na tabela: quem corrige pode ajustar a sugestão antes de gravar.</summary>
        public string Sugestao
        {
            get => _sugestao;
            set
            {
                _sugestao = value ?? "";
                if (Editavel && Situacao != Corrigido && Situacao != Ignorado)
                    Situacao = _sugestao == Atual || CorretorEscrita.TemLetraPerdida(_sugestao) ? Revisar : Corrigir;
                Mudou(nameof(Sugestao));
            }
        }

        internal List<ObjetoConfronto> Objetos { get; } = new List<ObjetoConfronto>();
        internal CampoConfronto CampoRef { get; set; }
        internal bool Editavel { get; set; }
        internal string ChaveIgnorar { get; set; }
        internal string SituacaoOriginal { get; set; }
        public bool PodeCorrigir => Editavel && Situacao == Corrigir && Sugestao != Atual;

        public event PropertyChangedEventHandler PropertyChanged;
        private void Mudou(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

        internal static string Mostrar(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            int ini = 0, fim = s.Length;
            while (ini < fim && s[ini] == ' ') ini++;
            while (fim > ini && s[fim - 1] == ' ') fim--;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool repetido = c == ' ' && ((i + 1 < s.Length && s[i + 1] == ' ') || (i > 0 && s[i - 1] == ' '));
                if (c == ' ' && (i < ini || i >= fim || repetido)) sb.Append('·');
                else if (c == '\n' || c == '\r') { if (!(c == '\n' && i > 0 && s[i - 1] == '\r')) sb.Append('↵'); }
                else if (c == '\t') sb.Append('→');
                else if (char.IsWhiteSpace(c) || char.IsControl(c) || c == '\u00AD' || (c >= '\u200B' && c <= '\u200D') || c == '\u2060' || c == '\uFEFF') sb.Append('¤');
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Corrigir escrita (igual no Revit e no Civil 3D): procura nos campos de texto dos objetos que vão para o IFC os
    /// espaços sobrando ou errados, a acentuação e os caracteres estranhos, e mostra o valor atual e a sugestão.
    /// A pessoa corrige, ajusta a sugestão ou ignora; o padrão guarda as regras, os termos que não mudam e o que foi ignorado.
    /// </summary>
    public class CorrigirEscritaWindow : Window
    {
        private const string TodosCampos = "Todos os campos de texto";

        private readonly IFonteConfronto _fonte;
        private PadraoEscrita _padrao;
        private readonly ComboBox _padroes;
        private readonly ComboBox _campo;
        private readonly WrapPanel _regras;
        private readonly Button _botaoRegras;
        private readonly WrapPanel _chips;
        private readonly DockPanel _areaTabela;
        private readonly CheckBox _verResolvidos;
        private readonly Button _corrigir, _ignorar, _desfazer, _termo, _selecionar;
        private readonly TextBlock _resumo;
        private FilterableGrid _tabela;
        private List<LinhaEscrita> _linhas;
        private int _conferidos, _noEscopo;
        private bool _carregando;

        public CorrigirEscritaWindow(IFonteConfronto fonte)
        {
            _fonte = fonte;
            Title = "MetroSP - Corrigir escrita";
            Width = 1160;
            Height = 760;
            MinWidth = 780;
            MinHeight = 500;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // ---- padrão e campos
            _padroes = new ComboBox { MinWidth = 200, Margin = new Thickness(0, 0, 8, 0), DisplayMemberPath = nameof(PadraoEscrita.Nome),
                ToolTip = "O perfil guarda as regras ligadas, os termos que não mudam, as correções próprias e o que foi ignorado. Fica salvo sozinho." };
            Theme.Digitavel(_padroes);
            var termos = new Button { Content = "Termos e correções...",
                ToolTip = "Siglas, códigos e nomes que nunca mudam (PTL, Pr_20_30...) e as suas correções (\"errado = certo\")." };
            var novo = Theme.Link("novo perfil");
            var excluir = Theme.Link("excluir");
            novo.Margin = new Thickness(12, 0, 0, 0);
            excluir.Margin = new Thickness(10, 0, 0, 0);
            novo.ToolTip = "Cria um perfil a partir do atual, com outro nome (para outra equipe ou outro lote).";
            _campo = new ComboBox { MinWidth = 260, IsEditable = true, IsTextSearchEnabled = true, StaysOpenOnEdit = true, Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "Procure em todos os campos de texto ou só em um (mais rápido em modelos grandes)." };
            TextSearch.SetTextPath(_campo, nameof(CampoConfronto.Rotulo));
            _campo.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
            // Perfil, campos e regras numa linha; as regras abrem numa lista.
            var barra = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            barra.Children.Add(Theme.Rotulo("Perfil"));
            barra.Children.Add(_padroes);
            _campo.MinWidth = 200;
            barra.Children.Add(Theme.Rotulo("Campos"));
            barra.Children.Add(_campo);

            // ---- regras
            _regras = new WrapPanel { Width = 420, Margin = new Thickness(10) };
            foreach (RegraEscrita r in CorretorEscrita.Regras)
            {
                var cb = new CheckBox { Content = r.Nome, Tag = r.Id, ToolTip = r.Dica, Width = 200, Margin = new Thickness(0, 2, 0, 2) };
                cb.Click += (s, e) => { MudouRegra((CheckBox)s); ContarRegras(); };
                _regras.Children.Add(cb);
            }
            _botaoRegras = new Button { Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Quais regras de escrita conferir (passe o mouse em cada uma para ver exemplos)." };
            var popRegras = new System.Windows.Controls.Primitives.Popup
            {
                PlacementTarget = _botaoRegras, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, StaysOpen = false,
                Child = new Border { Background = System.Windows.Media.Brushes.White, BorderBrush = Theme.Solid(Theme.Cinza), BorderThickness = new Thickness(1), Child = _regras },
            };
            popRegras.Closed += (s, e) => { if (_linhas != null) Procurar(); };
            _botaoRegras.Click += (s, e) => popRegras.IsOpen = true;
            barra.Children.Add(Theme.Rotulo("Regras"));
            barra.Children.Add(_botaoRegras);
            var procurar = new Button { Content = "⟳ Procurar", Padding = new Thickness(10, 2, 10, 2), VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Confere de novo (por exemplo, depois de mudar o modelo)." };
            barra.Children.Add(procurar);

            // ---- resultado
            _chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            _corrigir = new Button { Content = "Corrigir", Style = Theme.PrimaryButton, IsEnabled = false,
                ToolTip = "Grava a sugestão nas linhas selecionadas (Ctrl+A seleciona todas as visíveis). É uma operação só, que pode ser desfeita." };
            _ignorar = new Button { Content = "Ignorar", IsEnabled = false, ToolTip = "O valor está certo assim: deixa de aparecer e fica guardado no perfil." };
            _desfazer = new Button { Content = "Voltar a conferir", IsEnabled = false, ToolTip = "Tira o \"Ignorar\" das linhas selecionadas." };
            _termo = new Button { Content = "Não mexer na palavra...", IsEnabled = false,
                ToolTip = "Guarda as palavras que a correção mudou (sigla, código, nome próprio) como termos do perfil: elas não mudam mais em nenhum valor." };
            _selecionar = new Button { Content = "Selecionar no modelo", IsEnabled = false, ToolTip = $"Seleciona e mostra os {_fonte.Termo}s das linhas selecionadas (ou de todas as visíveis)." };
            _verResolvidos = new CheckBox { Content = "Mostrar ignorados e corrigidos", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var mais = new Button { Content = "Mais ▾" };
            mais.Click += (s, e) =>
            {
                var m = new ContextMenu { PlacementTarget = mais, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
                void Item(string texto, object dica, bool ativo, Action a)
                {
                    var i = new MenuItem { Header = texto, ToolTip = dica, IsEnabled = ativo };
                    i.Click += (s2, e2) => a();
                    m.Items.Add(i);
                }
                Item("Não mexer na palavra...", _termo.ToolTip, _termo.IsEnabled, NaoMexer);
                Item("Voltar a conferir", _desfazer.ToolTip, _desfazer.IsEnabled, () => Ignorar(false));
                m.Items.Add(new Separator());
                Item("Termos e correções...", termos.ToolTip, true, () => { if (EditarPadrao(this, _padrao)) { Guardar(); Reanalisar(); } });
                Item("Novo perfil...", novo.ToolTip, true, Novo);
                Item("Excluir o perfil " + _padrao?.Nome, null, _padroes.Items.Count > 1, Excluir);
                m.IsOpen = true;
            };
            var acoes = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            foreach (Button b in new[] { _corrigir, _ignorar, _selecionar, mais }) { b.Margin = new Thickness(0, 0, 6, 0); b.Padding = new Thickness(10, 2, 10, 2); acoes.Children.Add(b); }
            acoes.Children.Add(_verResolvidos);
            _areaTabela = new DockPanel();
            _areaTabela.Children.Add(Theme.Detalhe("Conferindo..."));

            var corpo = new DockPanel();
            var topo = new List<UIElement> { barra };
            if (_fonte.PainelEscopo != null)
            {
                _fonte.PainelEscopo.Margin = new Thickness(0, 0, 0, 6);
                topo.Add(_fonte.PainelEscopo);
            }
            topo.AddRange(new UIElement[] { _chips, acoes });
            foreach (UIElement el in topo)
            {
                DockPanel.SetDock(el, Dock.Top);
                corpo.Children.Add(el);
            }
            corpo.Children.Add(_areaTabela);

            _resumo = Theme.Detalhe();
            var fechar = new Button { Content = "Fechar", IsCancel = true };
            Theme.Apply(this, "Corrigir escrita", _fonte.NomeDocumento + " · espaços, acentos e caracteres estranhos nos dados que vão para o IFC",
                corpo, Theme.Rodape(_resumo, fechar));

            // ---- eventos
            _padroes.SelectionChanged += (s, e) => { if (!_carregando && _padroes.SelectedItem is PadraoEscrita p) Usar(p); };
            termos.Click += (s, e) => { if (EditarPadrao(this, _padrao)) { Guardar(); Reanalisar(); } };
            novo.Click += (s, e) => Novo();
            excluir.Click += (s, e) => Excluir();
            _fonte.EscopoMudou += () => { CarregarCampos(); _resumo.Text = "O escopo mudou: clique em \"Procurar\" de novo."; };
            procurar.Click += (s, e) => Procurar();
            fechar.Click += (s, e) => Close();
            _corrigir.Click += (s, e) => CorrigirSelecionadas();
            _ignorar.Click += (s, e) => Ignorar(true);
            _desfazer.Click += (s, e) => Ignorar(false);
            Loaded += (s, e) => Dispatcher.BeginInvoke(new Action(Procurar), System.Windows.Threading.DispatcherPriority.Background);
            _termo.Click += (s, e) => NaoMexer();
            _selecionar.Click += (s, e) => Selecionar(Selecionadas().Count > 0 ? Selecionadas() : Visiveis());
            _verResolvidos.Click += (s, e) => { _tabela?.Refresh(); Contar(); };

            ListarPadroes(null);
            CarregarCampos();
        }

        // ------------------------------------------------------------------ padrão

        private void ListarPadroes(string selecionar)
        {
            _carregando = true;
            List<PadraoEscrita> lista = PadroesEscrita.Listar();
            _padroes.ItemsSource = lista;
            PadraoEscrita escolhido = lista.FirstOrDefault(p => p.Nome == (selecionar ?? _padrao?.Nome)) ?? lista[0];
            _padroes.SelectedItem = escolhido;
            _carregando = false;
            Usar(escolhido);
        }

        private void Usar(PadraoEscrita p)
        {
            _padrao = p;
            foreach (CheckBox cb in _regras.Children.OfType<CheckBox>())
                cb.IsChecked = !_padrao.RegrasDesligadas.Contains((string)cb.Tag);
            ContarRegras();
        }

        private void ContarRegras()
        {
            if (_botaoRegras == null) return;
            var cbs = _regras.Children.OfType<CheckBox>().ToList();
            _botaoRegras.Content = $"{cbs.Count(c => c.IsChecked == true)} de {cbs.Count} ▾";
        }

        private void MudouRegra(CheckBox cb)
        {
            string id = (string)cb.Tag;
            _padrao.RegrasDesligadas.Remove(id);
            if (cb.IsChecked != true) _padrao.RegrasDesligadas.Add(id);
            Guardar();
            
        }

        /// <summary>O padrão fica salvo a cada mudança.</summary>
        private void Guardar()
        {
            try { PadroesEscrita.Salvar(_padrao); }
            catch (Exception ex) { _resumo.Text = "Não foi possível salvar o perfil: " + ex.Message; }
        }

        private void Novo()
        {
            string nome = PedirNome(_padrao.Nome + " (cópia)");
            if (string.IsNullOrWhiteSpace(nome)) return;
            if (File.Exists(PadroesEscrita.Caminho(nome)) &&
                MessageBox.Show(this, $"Já existe o perfil \"{nome}\". Substituir?", Title, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            PadraoEscrita p = _padrao.Copia();
            p.Nome = nome;
            try { PadroesEscrita.Salvar(p); }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível salvar o perfil:\n" + ex.Message, Title); return; }
            ListarPadroes(nome);
        }

        private void Excluir()
        {
            if (_padroes.Items.Count <= 1) { _resumo.Text = "É preciso ter pelo menos um perfil."; return; }
            if (MessageBox.Show(this, $"Excluir o perfil \"{_padrao.Nome}\"?", Title, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            try { PadroesEscrita.Excluir(_padrao.Nome); } catch (Exception ex) { _resumo.Text = "Não foi possível excluir: " + ex.Message; return; }
            _padrao = null;
            ListarPadroes(null);
        }

        private string PedirNome(string sugestao)
        {
            var w = new Window { Title = "MetroSP - Nome do perfil", Owner = this, Width = 420, SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.CanResizeWithGrip, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var caixa = new TextBox { Text = sugestao ?? "", Padding = new Thickness(4, 3, 4, 3) };
            var ok = new Button { Content = "Criar", Style = Theme.PrimaryButton, IsDefault = true };
            var cancelar = new Button { Content = "Cancelar", IsCancel = true };
            ok.Click += (s, e) => { w.DialogResult = caixa.Text.Trim().Length > 0; };
            var corpo = new StackPanel();
            corpo.Children.Add(Theme.Secao("Nome do perfil"));
            corpo.Children.Add(caixa);
            Theme.Apply(w, "Novo perfil", "Ex.: Metrô Linha 2, Lote 3", corpo, Theme.Rodape(null, ok, cancelar));
            w.Loaded += (s, e) => { caixa.Focus(); caixa.SelectAll(); };
            return w.ShowDialog() == true ? caixa.Text.Trim() : null;
        }

        /// <summary>Edita os termos e as correções de um padrão. Também abre pela janela Padrões.</summary>
        public static bool EditarPadrao(Window dono, PadraoEscrita p)
        {
            var w = new Window { Title = "MetroSP - Termos e correções", Owner = dono, Width = 760, Height = 560, MinWidth = 520, MinHeight = 360,
                ResizeMode = ResizeMode.CanResizeWithGrip, WindowStartupLocation = dono != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen };
            TextBox Caixa(IEnumerable<string> linhas, string dica) => new TextBox
            {
                Text = string.Join(Environment.NewLine, linhas), AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, ToolTip = dica,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"), Padding = new Thickness(4),
            };
            TextBox termos = Caixa(p.Termos, "Um por linha. Vale a palavra ou o trecho inteiro, sem diferenciar maiúsculas. Ex.: PTL, Pr_20_30, MetroSP, Engevix.");
            TextBox correcoes = Caixa(p.Correcoes, "Uma por linha, no formato errado = certo. Ex.: plataf = plataforma, pre moldado = pré-moldado.");
            var grade = new Grid();
            grade.ColumnDefinitions.Add(new ColumnDefinition());
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grade.ColumnDefinitions.Add(new ColumnDefinition());
            grade.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grade.RowDefinitions.Add(new RowDefinition());
            void Por(UIElement el, int lin, int col) { Grid.SetRow(el, lin); Grid.SetColumn(el, col); grade.Children.Add(el); }
            TextBlock t1 = Theme.Secao("Termos que não mudam"), t2 = Theme.Secao("Correções próprias (errado = certo)");
            t1.ToolTip = termos.ToolTip;
            t2.ToolTip = correcoes.ToolTip;
            Por(t1, 0, 0);
            Por(t2, 0, 2);
            Por(termos, 1, 0);
            Por(correcoes, 1, 2);
            var ok = new Button { Content = "Salvar", Style = Theme.PrimaryButton };
            var cancelar = new Button { Content = "Cancelar", IsCancel = true };
            ok.Click += (s, e) => w.DialogResult = true;
            Theme.Apply(w, "Termos e correções", "Perfil \"" + p.Nome + "\" · passe o mouse nos títulos para ver exemplos", grade, Theme.Rodape(null, ok, cancelar));
            if (w.ShowDialog() != true) return false;
            List<string> Linhas(TextBox t) => t.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim()).Where(l => l.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
            p.Termos = Linhas(termos);
            p.Correcoes = Linhas(correcoes);
            return true;
        }

        /// <summary>Abre o editor para um arquivo de padrão (usado pela janela Padrões).</summary>
        public static void EditarArquivo(Window dono, string arquivo)
        {
            PadraoEscrita p;
            try { p = PadroesEscrita.Ler(arquivo); }
            catch (Exception ex) { MessageBox.Show(dono, "Este arquivo não é um perfil de escrita:\n" + ex.Message, "MetroSP"); return; }
            if (string.IsNullOrWhiteSpace(p.Nome)) p.Nome = Path.GetFileNameWithoutExtension(arquivo);
            if (!EditarPadrao(dono, p)) return;
            try { PadroesEscrita.Gravar(arquivo, p); }
            catch (Exception ex) { MessageBox.Show(dono, "Não foi possível salvar:\n" + ex.Message, "MetroSP"); }
        }

        // ------------------------------------------------------------------ campos e procura

        private void CarregarCampos()
        {
            object antes = _campo.SelectedItem;
            var lista = new List<object> { TodosCampos };
            Cursor = Cursors.Wait;
            try { lista.AddRange(_fonte.Campos().Where(c => c.Texto)); }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível ler os campos do modelo:\n" + ex.Message, Title); }
            finally { Cursor = null; }
            _campo.ItemsSource = lista;
            _campo.SelectedItem = antes is CampoConfronto c ? lista.OfType<CampoConfronto>().FirstOrDefault(x => x.Chave == c.Chave) ?? (object)TodosCampos : TodosCampos;
        }

        private void Procurar()
        {
            Cursor = Cursors.Wait;
            try
            {
                List<CampoConfronto> campos = _fonte.Campos().Where(c => c.Texto).ToList();
                if (_campo.SelectedItem is CampoConfronto so) campos = campos.Where(c => c.Chave == so.Chave).ToList();
                var corretor = new CorretorEscrita(_padrao);
                var ignorados = new HashSet<string>(_padrao.Ignorados);
                var vistos = new HashSet<string>();
                var porChave = new Dictionary<string, LinhaEscrita>();
                _linhas = new List<LinhaEscrita>();
                _conferidos = 0;
                IList<ObjetoConfronto> objetos = _fonte.Objetos();
                _noEscopo = objetos.Count;
                foreach (ObjetoConfronto o in objetos)
                {
                    foreach (CampoConfronto c in campos)
                    {
                        ValorConfronto v = _fonte.Ler(o, c);
                        if (!v.Existe || string.IsNullOrEmpty(v.Texto)) continue;
                        // Um valor do tipo (ou que não pode ser gravado) aparece uma vez, com todos os objetos que o usam.
                        string dono = v.Dono != null ? "t:" + v.Dono : !v.Editavel ? "v:" + v.Texto : "o:" + o.Id;
                        string chave = dono + "|" + c.Chave;
                        if (porChave.TryGetValue(chave, out LinhaEscrita ja))
                        {
                            ja.Objetos.Add(o);
                            if (v.Dono == null) { ja.Elemento = $"{ja.Objetos.Count} {_fonte.Termo}s"; ja.Id = ""; }
                            continue;
                        }
                        if (!vistos.Add(chave)) continue;
                        _conferidos++;
                        ResultadoEscrita r = corretor.Analisar(v.Texto);
                        if (!r.TemProblema) continue;
                        var l = new LinhaEscrita
                        {
                            Elemento = v.DonoRotulo ?? o.Nome,
                            Id = o.Id,
                            Categoria = o.Categoria,
                            Campo = c.Rotulo,
                            CampoRef = c,
                            Atual = v.Texto,
                            Problemas = r.Problemas,
                            Editavel = v.Editavel,
                            ChaveIgnorar = dono + "|" + c.Chave + "|" + v.Texto,
                        };
                        l.Objetos.Add(o);
                        l.Sugestao = r.Sugestao; // nos editáveis, a sugestão define a situação (Corrigir ou Revisar)
                        if (!v.Editavel) l.Situacao = LinhaEscrita.NaoGrava;
                        l.Motivo = !v.Editavel ? v.Motivo : r.Revisar != null ? "Digite a palavra certa na coluna Sugestão" : null;
                        l.SituacaoOriginal = l.Situacao;
                        if (ignorados.Contains(l.ChaveIgnorar)) l.Situacao = LinhaEscrita.Ignorado;
                        porChave[chave] = l;
                        _linhas.Add(l);
                    }
                }
            }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível procurar:\n" + ex.Message, Title); return; }
            finally { Cursor = null; }
            MostrarResultado();
        }

        /// <summary>Depois de mudar termos ou correções, refaz a sugestão das linhas abertas sem ler o modelo de novo.</summary>
        private void Reanalisar()
        {
            if (_linhas == null) return;
            var corretor = new CorretorEscrita(_padrao);
            foreach (LinhaEscrita l in _linhas.Where(x => x.Situacao != LinhaEscrita.Corrigido && x.Situacao != LinhaEscrita.Ignorado).ToList())
            {
                ResultadoEscrita r = corretor.Analisar(l.Atual);
                if (!r.TemProblema) { _linhas.Remove(l); continue; }
                l.Problemas = r.Problemas;
                l.Sugestao = r.Sugestao;
            }
            MostrarResultado();
            _resumo.Text = "Sugestões refeitas com os termos e as correções do perfil.";
        }

        private bool Resolvida(LinhaEscrita l) => l.Situacao == LinhaEscrita.Ignorado || l.Situacao == LinhaEscrita.Corrigido;

        private void MostrarResultado()
        {
            _areaTabela.Children.Clear();
            _tabela = new FilterableGrid(_linhas, typeof(LinhaEscrita));
            _tabela.AddColumn(Primeira(_fonte.Termo), nameof(LinhaEscrita.Elemento), 200);
            _tabela.AddColumn("Campo", nameof(LinhaEscrita.Campo), 160);
            _tabela.AddColumn("Atual", nameof(LinhaEscrita.AtualVisivel), 200);
            var sugestao = _tabela.AddColumn("Sugestão", nameof(LinhaEscrita.Sugestao), 200);
            _tabela.AddColumn("O que tem", nameof(LinhaEscrita.Problemas), 200, fill: true);
            // Só a sugestão é editável (duplo clique ou F2 na célula).
            _tabela.Grid.IsReadOnly = false;
            foreach (DataGridColumn col in _tabela.Grid.Columns) col.IsReadOnly = col != sugestao;
            _tabela.Grid.BeginningEdit += (s, e) => { if (!(e.Row.Item is LinhaEscrita l) || !l.Editavel || Resolvida(l)) e.Cancel = true; };
            _tabela.Grid.CellEditEnding += (s, e) =>
            {
                DataGridRow row = e.Row;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    // Fecha a edição antes de recontar: a lista não pode ser refiltrada no meio de uma edição.
                    _tabela.Grid.CommitEdit(DataGridEditingUnit.Row, true);
                    Pintar(row);
                    Contar();
                }), System.Windows.Threading.DispatcherPriority.Background);
            };
            _tabela.ExtraFilter = o => _verResolvidos.IsChecked == true || !Resolvida((LinhaEscrita)o);
            _tabela.Grid.LoadingRow += (s, e) => { Pintar(e.Row); e.Row.ToolTip = (e.Row.Item as LinhaEscrita)?.Detalhe; };
            _tabela.Grid.SelectionChanged += (s, e) => Habilitar();
            _tabela.Filtered += Contar;
            Theme.StyleSelection(_tabela.Grid);
            _tabela.Refresh();
            if (_linhas.Count == 0)
                _areaTabela.Children.Add(Theme.Detalhe($"Nada a corrigir: {_conferidos} valores conferidos."));
            else
            {
                var legenda = Theme.Detalhe("Na coluna Atual: · espaço sobrando   ¤ espaço especial ou caractere invisível   ↵ quebra de linha. " +
                                            "Dê dois cliques na Sugestão para ajustar antes de corrigir.");
                legenda.Margin = new Thickness(0, 0, 0, 4);
                DockPanel.SetDock(legenda, Dock.Top);
                _areaTabela.Children.Add(legenda);
                _areaTabela.Children.Add(_tabela);
            }
            Contar();
        }

        private static string Primeira(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

        private static void Pintar(DataGridRow row)
        {
            var l = (LinhaEscrita)row.Item;
            row.Background = l.Situacao == LinhaEscrita.Corrigido ? Theme.Solid(Theme.VerdeClaro)
                : l.Situacao == LinhaEscrita.Ignorado ? Theme.LinhaNormal
                : l.Situacao == LinhaEscrita.Corrigir ? Theme.LinhaProblema : Theme.LinhaAviso;
            row.Foreground = l.Situacao == LinhaEscrita.Ignorado ? Theme.Solid(Theme.Cinza) : Theme.Solid(Theme.Preto);
        }

        private void Contar()
        {
            if (_linhas == null) return;
            _chips.Children.Clear();
            var abertas = _linhas.Where(l => !Resolvida(l)).ToList();
            int corrigir = abertas.Count(l => l.Situacao == LinhaEscrita.Corrigir);
            _chips.Children.Add(Theme.Chip($"{_conferidos} valores conferidos"));
            _chips.Children.Add(Theme.Chip($"{corrigir} para corrigir", destaque: corrigir > 0));
            foreach (var g in abertas.Where(l => l.Situacao != LinhaEscrita.Corrigir).GroupBy(l => l.Situacao).OrderByDescending(g => g.Count()))
                _chips.Children.Add(Theme.Chip($"{g.Count()} {g.Key.ToLowerInvariant()}", escuro: true));
            int ign = _linhas.Count(l => l.Situacao == LinhaEscrita.Ignorado), feitos = _linhas.Count(l => l.Situacao == LinhaEscrita.Corrigido);
            if (ign > 0) _chips.Children.Add(Theme.Chip($"{ign} ignorados"));
            if (feitos > 0) _chips.Children.Add(Theme.Chip($"{feitos} corrigidos"));
            var avisos = new List<string>();
            if (abertas.Any(l => l.Situacao == LinhaEscrita.NaoGrava)) avisos.Add("há valores que não podem ser gravados aqui (veja o Motivo)");
            Theme.MudarResumo(_resumo, avisos, $"{_noEscopo} {_fonte.Termo}s no escopo · {abertas.Count} pendências");
            Habilitar();
        }

        private List<LinhaEscrita> Selecionadas() => _tabela?.Grid.SelectedItems.OfType<LinhaEscrita>().ToList() ?? new List<LinhaEscrita>();

        private List<LinhaEscrita> Visiveis() => _tabela?.VisibleItems<LinhaEscrita>().ToList() ?? new List<LinhaEscrita>();

        private void Habilitar()
        {
            List<LinhaEscrita> sel = Selecionadas();
            int n = sel.Count(l => l.PodeCorrigir);
            _corrigir.IsEnabled = n > 0;
            _corrigir.Content = n > 1 ? $"Corrigir ({n})" : "Corrigir";
            _ignorar.IsEnabled = sel.Any(l => !Resolvida(l));
            _desfazer.IsEnabled = sel.Any(l => l.Situacao == LinhaEscrita.Ignorado);
            _termo.IsEnabled = sel.Any(l => !Resolvida(l) && CorretorEscrita.PalavrasMudadas(l.Atual, l.Sugestao).Count > 0);
            _selecionar.IsEnabled = Visiveis().Count > 0;
        }

        // ------------------------------------------------------------------ ações

        private void CorrigirSelecionadas()
        {
            List<LinhaEscrita> g = Selecionadas().Where(l => l.PodeCorrigir).ToList();
            if (g.Count == 0) return;
            int tipos = g.Count(l => l.Objetos.Count > 1);
            string msg = $"Gravar a sugestão em {g.Count} valores?" +
                         (tipos > 0 ? $"\n\n{tipos} estão em tipos ou valores usados por vários {_fonte.Termo}s: a correção vale para todos eles." : "") +
                         "\n\nÉ uma operação só: pode ser desfeita.";
            if (MessageBox.Show(this, msg, Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            var grav = new List<Gravacao>();
            var linhas = new List<LinhaEscrita>();
            foreach (LinhaEscrita l in g)
            {
                if (!_fonte.Preparar(l.Objetos[0], l.CampoRef, l.Sugestao, out object valor, out string motivo))
                {
                    l.Situacao = LinhaEscrita.NaoGrava;
                    l.Motivo = motivo;
                    continue;
                }
                grav.Add(new Gravacao { Objeto = l.Objetos[0], Campo = l.CampoRef, Valor = valor, Texto = l.Sugestao });
                linhas.Add(l);
            }
            (int ok, int falhas) r = (0, 0);
            if (grav.Count > 0)
            {
                Cursor = Cursors.Wait;
                try { r = _fonte.Gravar(grav); }
                catch (Exception ex) { MessageBox.Show(this, "Não foi possível gravar. Nada foi alterado.\n\n" + ex.Message, Title); return; }
                finally { Cursor = null; }
            }
            for (int i = 0; i < linhas.Count; i++)
            {
                if (grav[i].Ok) { linhas[i].Situacao = LinhaEscrita.Corrigido; linhas[i].Motivo = "Gravado no modelo"; }
                else { linhas[i].Motivo = "Não gravou (bloqueado, em grupo, nome repetido ou de outro usuário)"; }
            }
            _tabela.Grid.Items.Refresh();
            _tabela.Refresh();
            Contar();
            _resumo.Text = $"{r.ok} valores corrigidos." + (r.falhas + (g.Count - grav.Count) > 0 ? $" {r.falhas + g.Count - grav.Count} não puderam ser gravados (veja o Motivo)." : "");
        }

        private void Ignorar(bool ignorar)
        {
            foreach (LinhaEscrita l in Selecionadas())
            {
                if (ignorar && !Resolvida(l))
                {
                    if (!_padrao.Ignorados.Contains(l.ChaveIgnorar)) _padrao.Ignorados.Add(l.ChaveIgnorar);
                    l.Situacao = LinhaEscrita.Ignorado;
                }
                else if (!ignorar && l.Situacao == LinhaEscrita.Ignorado)
                {
                    _padrao.Ignorados.Remove(l.ChaveIgnorar);
                    l.Situacao = l.SituacaoOriginal;
                }
            }
            Guardar();
            _tabela.Grid.Items.Refresh();
            _tabela.Refresh();
            Contar();
            _resumo.Text = $"Guardado no perfil \"{_padrao.Nome}\".";
        }

        private void NaoMexer()
        {
            var palavras = Selecionadas().Where(l => !Resolvida(l)).SelectMany(l => CorretorEscrita.PalavrasMudadas(l.Atual, l.Sugestao))
                .Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
            if (palavras.Count == 0) return;
            string lista = string.Join(", ", palavras.Take(20)) + (palavras.Count > 20 ? "..." : "");
            if (MessageBox.Show(this, $"Guardar como termos do perfil \"{_padrao.Nome}\"?\n\n{lista}\n\nEssas palavras não mudam mais em nenhum valor.",
                    Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            foreach (string p in palavras)
                if (!_padrao.Termos.Contains(p, StringComparer.CurrentCultureIgnoreCase)) _padrao.Termos.Add(p);
            Guardar();
            Reanalisar();
        }

        private void Selecionar(List<LinhaEscrita> linhas)
        {
            var objs = linhas.SelectMany(l => l.Objetos).Distinct().ToList();
            if (objs.Count == 0) return;
            try { _fonte.Selecionar(objs, true); _resumo.Text = $"{objs.Count} {_fonte.Termo}s selecionados."; }
            catch (Exception ex) { _resumo.Text = "Não foi possível selecionar: " + ex.Message; }
        }
    }
}
