using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using MetroSP.Planilha;
using MetroSP.Ui;
using Grid = System.Windows.Controls.Grid;
using Control = System.Windows.Controls.Control;

namespace MetroSP.Classificacao
{
    /// <summary>
    /// Cria e edita os padrões de verificação de classificação: uma ou mais classificações (modelos da Autodesk ou
    /// personalizadas), cada uma com o parâmetro onde fica no modelo e a planilha com a coluna de cada dado.
    /// </summary>
    public class PadroesClassificacaoWindow : Window
    {
        private readonly List<string> _parametrosModelo;
        private List<PadraoClassificacao> _padroes;
        private PadraoClassificacao _atual;
        private SistemaClassificacao _sis;
        private bool _alterado;
        private bool _preenchendo;

        private readonly ComboBox _lista;
        private readonly TextBox _nome;
        private readonly ListBox _sistemas;
        private readonly StackPanel _detalhe;
        private readonly ComboBox _modelo;
        private readonly TextBox _sisNome;
        private readonly TextBox _edicao;
        private readonly ComboBox _formato;
        private readonly ComboBox _pCodigo;
        private readonly ComboBox _pDescricao;
        private readonly CheckBox _gravarNome;
        private readonly MapaColunasPainel _mapa;
        private readonly TextBlock _info;
        private readonly Button _remover;

        /// <summary>Nome do padrão aberto ao fechar (para a janela de verificação já usá-lo).</summary>
        public string Escolhido { get; private set; }

        public PadroesClassificacaoWindow(Document doc, string abrir)
        {
            _parametrosModelo = ParametrosDeTexto(doc);
            Title = "MetroSP - Padrões de classificação";
            Width = 1000;
            Height = 760;
            MinWidth = 760;
            MinHeight = 520;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            // Padrão
            var barra = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var rotulo = Theme.Rotulo("Padrão");
            DockPanel.SetDock(rotulo, Dock.Left);
            barra.Children.Add(rotulo);
            var acoes = new StackPanel { Orientation = Orientation.Horizontal };
            Button Acao(string texto, string dica, Action a)
            {
                var b = new Button { Content = texto, ToolTip = dica, Margin = new Thickness(6, 0, 0, 0) };
                b.Click += (s, e) => a();
                acoes.Children.Add(b);
                return b;
            }
            Acao("Novo", "Cria um padrão vazio.", Novo);
            Acao("Duplicar", "Copia o padrão aberto com outro nome.", Duplicar);
            Acao("Excluir", "Apaga o padrão aberto deste computador (as planilhas não são apagadas).", Excluir);
            Acao("Importar...", "Abre um padrão (.json) recebido de outra pessoa e o guarda neste computador.", Importar);
            Acao("Exportar...", "Salva o padrão aberto num .json para levar a outra máquina ou mandar para a equipe.", Exportar);
            DockPanel.SetDock(acoes, Dock.Right);
            barra.Children.Add(acoes);
            _lista = new ComboBox { MinWidth = 200 };
            _lista.SelectionChanged += (s, e) => { if (!_preenchendo && _lista.SelectedItem is PadraoClassificacao p) Abrir(p); };
            barra.Children.Add(_lista);

            var linhaNome = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var rotNome = Theme.Rotulo("Nome");
            DockPanel.SetDock(rotNome, Dock.Left);
            linhaNome.Children.Add(rotNome);
            _nome = new TextBox { ToolTip = "Nome do padrão (também é o nome do arquivo salvo)." };
            _nome.TextChanged += (s, e) => { if (!_preenchendo && _atual != null) { _atual.Nome = _nome.Text.Trim(); Alterou(); } };
            linhaNome.Children.Add(_nome);

            // Classificações (esquerda)
            var esquerda = new DockPanel { Margin = new Thickness(0, 0, 14, 0) };
            var tituloSis = Theme.Secao("Classificações");
            tituloSis.ToolTip = "Cada elemento pode ter mais de uma classificação (ex.: Metrô SP em ClassificationCode e Uniformat em ClassificationCode(2)).";
            tituloSis.Margin = new Thickness(0, 0, 0, 5);
            DockPanel.SetDock(tituloSis, Dock.Top);
            esquerda.Children.Add(tituloSis);
            var botoesSis = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            var adicionar = new Button { Content = "+ Adicionar", ToolTip = "Acrescenta uma classificação ao padrão." };
            adicionar.Click += (s, e) => Adicionar();
            _remover = new Button { Content = "Remover", Margin = new Thickness(6, 0, 0, 0) };
            _remover.Click += (s, e) => Remover();
            var subir = new Button { Content = "↑", Margin = new Thickness(6, 0, 0, 0), ToolTip = "Sobe na lista." };
            var descer = new Button { Content = "↓", Margin = new Thickness(4, 0, 0, 0), ToolTip = "Desce na lista." };
            subir.Click += (s, e) => Mover(-1);
            descer.Click += (s, e) => Mover(1);
            foreach (Button b in new[] { adicionar, _remover, subir, descer }) botoesSis.Children.Add(b);
            DockPanel.SetDock(botoesSis, Dock.Bottom);
            esquerda.Children.Add(botoesSis);
            _sistemas = new ListBox();
            _sistemas.SelectionChanged += (s, e) => MostrarSistema(_sistemas.SelectedItem as SistemaClassificacao);
            esquerda.Children.Add(_sistemas);

            // Detalhe (direita)
            _detalhe = new StackPanel();
            _modelo = new ComboBox { ToolTip = "Preenche o formato e os parâmetros com um padrão da Autodesk. Depois dá para ajustar." };
            _modelo.Items.Add("Personalizado");
            foreach (ModeloAutodesk m in ModeloAutodesk.Todos()) _modelo.Items.Add(new ComboBoxItem { Content = m.Rotulo, Tag = m, ToolTip = m.Dica });
            _modelo.SelectionChanged += (s, e) => AplicarModelo();
            _detalhe.Children.Add(Campo("Modelo da Autodesk", _modelo));

            _sisNome = new TextBox { ToolTip = "Nome da classificação. Com \"Gravar com [Nome]\", vira o nome da IfcClassification no IFC." };
            _edicao = new TextBox { ToolTip = "Opcional: edição ou versão da lista (ex.: 2026)." };
            _sisNome.TextChanged += (s, e) => { if (!Preenchendo()) { _sis.Nome = _sisNome.Text.Trim(); AtualizarLista(); Alterou(); } };
            _edicao.TextChanged += (s, e) => { if (!Preenchendo()) { _sis.Edicao = _edicao.Text.Trim(); Alterou(); } };
            _detalhe.Children.Add(DuasColunas(Campo("Nome", _sisNome), Campo("Edição", _edicao)));

            _formato = new ComboBox();
            _formato.Items.Add(new ComboBoxItem { Content = "Código:Descrição num parâmetro", ToolTip = "Como o ClassificationCode do exportador IFC: \"EST.02.01:Laje\"." });
            _formato.Items.Add(new ComboBoxItem { Content = "Código e descrição separados", ToolTip = "Um parâmetro para o código e outro (opcional) para a descrição, como no Classification Manager." });
            _formato.SelectionChanged += (s, e) =>
            {
                if (Preenchendo()) return;
                _sis.Formato = _formato.SelectedIndex == 1 ? FormatoClassificacao.Separados : FormatoClassificacao.CodigoDescricao;
                AtualizarHabilitados();
                Alterou();
            };
            _detalhe.Children.Add(Campo("Como fica no modelo", _formato));

            _pCodigo = ComboParametros("Parâmetro de texto com o código (ou Código:Descrição). Instância ou tipo.");
            _pDescricao = ComboParametros("Parâmetro de texto com a descrição. Vazio = só o código é conferido.");
            _pCodigo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, e) =>
            {
                if (Preenchendo()) return;
                _sis.ParametroCodigo = _pCodigo.Text.Trim();
                AtualizarLista();
                Alterou();
            }));
            _pDescricao.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, e) =>
            {
                if (Preenchendo()) return;
                _sis.ParametroDescricao = _pDescricao.Text.Trim();
                Alterou();
            }));
            _detalhe.Children.Add(DuasColunas(Campo("Parâmetro do código", _pCodigo), Campo("Parâmetro da descrição", _pDescricao)));

            _gravarNome = new CheckBox
            {
                Content = "Gravar com [Nome] no começo",
                Margin = new Thickness(0, 8, 0, 0),
                ToolTip = "As correções gravam \"[Nome]Código:Descrição\". O exportador IFC da Autodesk usa o nome como IfcClassification; " +
                          "sem ele, usa a classificação configurada na exportação.",
            };
            _gravarNome.Click += (s, e) => { if (!Preenchendo()) { _sis.GravarNome = _gravarNome.IsChecked == true; Alterou(); } };
            _detalhe.Children.Add(_gravarNome);

            var tituloPlanilha = Theme.Secao("Planilha com a lista");
            tituloPlanilha.ToolTip = "Escolha a planilha e indique em qual coluna está cada dado. Pode ser a mesma planilha para várias classificações, em abas ou colunas diferentes.";
            _detalhe.Children.Add(tituloPlanilha);
            _mapa = new MapaColunasPainel(new MapaColunas(), SistemaClassificacao.Campos);
            _mapa.Mudou += () => { if (!_preenchendo) Alterou(); };
            _detalhe.Children.Add(_mapa);

            var principal = new Grid();
            principal.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            principal.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var rolagem = new ScrollViewer { Content = _detalhe, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetColumn(rolagem, 1);
            principal.Children.Add(esquerda);
            principal.Children.Add(rolagem);

            var corpo = new DockPanel();
            DockPanel.SetDock(barra, Dock.Top);
            DockPanel.SetDock(linhaNome, Dock.Top);
            corpo.Children.Add(barra);
            corpo.Children.Add(linhaNome);
            corpo.Children.Add(principal);

            _info = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            var salvar = new Button { Content = "Salvar", Style = Theme.PrimaryButton, ToolTip = "Grava o padrão neste computador (" + PadraoClassificacao.Pasta + ")." };
            salvar.Click += (s, e) => Salvar();
            var fechar = new Button { Content = "Fechar", IsCancel = true };
            fechar.Click += (s, e) => Close();
            var rodape = new DockPanel();
            StackPanel botoes = Theme.Buttons(salvar, fechar);
            DockPanel.SetDock(botoes, Dock.Right);
            rodape.Children.Add(botoes);
            rodape.Children.Add(_info);

            Theme.Apply(this, "Padrões de classificação", "Classificações a conferir, onde ficam no modelo e de qual planilha vem cada lista", corpo, rodape);
            Closing += (s, e) =>
            {
                if (!_alterado) return;
                MessageBoxResult r = MessageBox.Show(this, $"Salvar as alterações em \"{_atual?.Nome}\"?", Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (r == MessageBoxResult.Cancel) e.Cancel = true;
                else if (r == MessageBoxResult.Yes && !Salvar()) e.Cancel = true;
            };

            _padroes = PadraoClassificacao.Listar();
            if (_padroes.Count == 0) _padroes.Add(PadraoClassificacao.DoLegado(ClassSettings.Abrir()));
            PreencherLista(_padroes.FirstOrDefault(p => p.Nome == abrir) ?? _padroes[0]);
        }

        // ------------------------------------------------------------------ montagem

        private static FrameworkElement Campo(string rotulo, Control controle)
        {
            var p = new StackPanel();
            p.Children.Add(Theme.Label(rotulo));
            p.Children.Add(controle);
            return p;
        }

        private static Grid DuasColunas(FrameworkElement a, FrameworkElement b)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(b, 2);
            g.Children.Add(a);
            g.Children.Add(b);
            return g;
        }

        private ComboBox ComboParametros(string dica)
        {
            var c = new ComboBox { IsEditable = true, IsTextSearchEnabled = true, ToolTip = dica };
            foreach (string p in _parametrosModelo) c.Items.Add(p);
            return c;
        }

        /// <summary>Parâmetros de texto do projeto (compartilhados e de projeto) e os nomes usados pelos modelos da Autodesk.</summary>
        private static List<string> ParametrosDeTexto(Document doc)
        {
            var nomes = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            try
            {
                DefinitionBindingMapIterator it = doc.ParameterBindings.ForwardIterator();
                while (it.MoveNext())
                {
                    Definition d = it.Key;
                    if (d == null) continue;
                    bool texto = true;
                    try { texto = d.GetDataType() == SpecTypeId.String.Text; } catch { }
                    if (texto) nomes.Add(d.Name);
                }
            }
            catch { }
            foreach (ModeloAutodesk m in ModeloAutodesk.Todos())
            {
                nomes.Add(m.Codigo);
                if (!string.IsNullOrEmpty(m.Descricao)) nomes.Add(m.Descricao);
            }
            return nomes.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private bool Preenchendo() => _preenchendo || _sis == null;

        private void Alterou()
        {
            _alterado = true;
            AtualizarInfo();
        }

        // ------------------------------------------------------------------ padrões

        private void PreencherLista(PadraoClassificacao abrir)
        {
            _preenchendo = true;
            _lista.ItemsSource = null;
            _lista.ItemsSource = _padroes;
            _lista.SelectedItem = abrir;
            _preenchendo = false;
            Abrir(abrir);
        }

        private bool PodeTrocar()
        {
            if (!_alterado) return true;
            MessageBoxResult r = MessageBox.Show(this, $"Salvar as alterações em \"{_atual?.Nome}\" antes?", Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return false;
            if (r == MessageBoxResult.Yes) return Salvar();
            // Descarta: relê do disco.
            int i = _padroes.IndexOf(_atual);
            if (i >= 0)
            {
                if (!string.IsNullOrEmpty(_atual.Caminho) && File.Exists(_atual.Caminho))
                {
                    try { _padroes[i] = PadraoClassificacao.Ler(_atual.Caminho); } catch { }
                }
                else _padroes.RemoveAt(i);
            }
            _alterado = false;
            return true;
        }

        private void Abrir(PadraoClassificacao p)
        {
            if (_atual != null && p != _atual && !PodeTrocar())
            {
                _preenchendo = true;
                _lista.SelectedItem = _atual;
                _preenchendo = false;
                return;
            }
            if (p != _atual && _padroes.Contains(p) == false) return;
            _atual = p;
            Escolhido = p.Nome;
            _preenchendo = true;
            _nome.Text = p.Nome;
            _preenchendo = false;
            AtualizarLista();
            _sistemas.SelectedIndex = p.Sistemas.Count > 0 ? 0 : -1;
            if (p.Sistemas.Count == 0) MostrarSistema(null);
            AtualizarInfo();
        }

        private void Novo()
        {
            if (!PodeTrocar()) return;
            var p = new PadraoClassificacao { Nome = NomeLivre("Novo padrão"), Sistemas = { new SistemaClassificacao() } };
            _padroes.Add(p);
            _atual = null;
            PreencherLista(p);
            _alterado = true;
            AtualizarInfo();
        }

        private void Duplicar()
        {
            if (_atual == null || !PodeTrocar()) return;
            PadraoClassificacao p = _atual.Clonar();
            p.Caminho = null;
            p.Nome = NomeLivre(_atual.Nome + " (cópia)");
            _padroes.Add(p);
            _atual = null;
            PreencherLista(p);
            _alterado = true;
            AtualizarInfo();
        }

        private string NomeLivre(string baseNome)
        {
            string n = baseNome;
            for (int i = 2; _padroes.Any(p => string.Equals(p.Nome, n, StringComparison.CurrentCultureIgnoreCase)); i++) n = $"{baseNome} {i}";
            return n;
        }

        private void Excluir()
        {
            if (_atual == null) return;
            if (MessageBox.Show(this, $"Excluir o padrão \"{_atual.Nome}\" deste computador?\n\nAs planilhas que ele usa não são apagadas.",
                    Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try { _atual.Excluir(); }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível excluir:\n" + ex.Message, Title); return; }
            _padroes.Remove(_atual);
            _alterado = false;
            _atual = null;
            if (_padroes.Count == 0) _padroes.Add(new PadraoClassificacao { Sistemas = { new SistemaClassificacao() } });
            PreencherLista(_padroes[0]);
        }

        private void Importar()
        {
            if (!PodeTrocar()) return;
            var d = new Microsoft.Win32.OpenFileDialog { Title = "Importar padrão de classificação", Filter = "Padrão de classificação (*.json)|*.json" };
            if (d.ShowDialog(this) != true) return;
            try
            {
                PadraoClassificacao p = PadraoClassificacao.Ler(d.FileName);
                p.Caminho = null;
                if (_padroes.Any(x => string.Equals(x.Nome, p.Nome, StringComparison.CurrentCultureIgnoreCase)))
                {
                    if (MessageBox.Show(this, $"Já existe um padrão \"{p.Nome}\". Substituir?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                        p.Nome = NomeLivre(p.Nome);
                    else _padroes.RemoveAll(x => string.Equals(x.Nome, p.Nome, StringComparison.CurrentCultureIgnoreCase));
                }
                p.Salvar();
                _padroes.Add(p);
                _atual = null;
                PreencherLista(p);
                var semPlanilha = p.Sistemas.Where(s => !File.Exists(s.Planilha?.Arquivo)).Select(s => s.Nome).ToList();
                if (semPlanilha.Count > 0)
                    MessageBox.Show(this, "Padrão importado. Estas classificações apontam para uma planilha que não existe neste computador; " +
                        "escolha a planilha de novo (as colunas indicadas são mantidas):\n\n• " + string.Join("\n• ", semPlanilha), Title);
            }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível importar:\n" + ex.Message, Title); }
        }

        private void Exportar()
        {
            if (_atual == null) return;
            var d = new Microsoft.Win32.SaveFileDialog { Title = "Exportar padrão de classificação", Filter = "Padrão de classificação (*.json)|*.json", FileName = _atual.Nome + ".json" };
            if (d.ShowDialog(this) != true) return;
            try { _atual.Exportar(d.FileName); }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível exportar:\n" + ex.Message, Title); }
        }

        private bool Salvar()
        {
            if (_atual == null) return true;
            if (string.IsNullOrWhiteSpace(_atual.Nome)) { MessageBox.Show(this, "Dê um nome ao padrão.", Title); return false; }
            if (_padroes.Any(p => p != _atual && string.Equals(p.Nome, _atual.Nome, StringComparison.CurrentCultureIgnoreCase)))
            {
                MessageBox.Show(this, $"Já existe outro padrão chamado \"{_atual.Nome}\".", Title);
                return false;
            }
            var semParametro = _atual.Sistemas.Where(s => string.IsNullOrWhiteSpace(s.ParametroCodigo)).Select(s => s.Nome).ToList();
            if (semParametro.Count > 0)
            {
                MessageBox.Show(this, "Indique o parâmetro do código em: " + string.Join(", ", semParametro), Title);
                return false;
            }
            try { _atual.Salvar(); }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível salvar:\n" + ex.Message, Title); return false; }
            _alterado = false;
            Escolhido = _atual.Nome;
            _preenchendo = true;
            _lista.Items.Refresh();
            _lista.SelectedItem = _atual;
            _preenchendo = false;
            AtualizarInfo();
            return true;
        }

        // ------------------------------------------------------------------ classificações

        private void AtualizarLista()
        {
            object sel = _sistemas.SelectedItem;
            bool antes = _preenchendo;
            _preenchendo = true;
            _sistemas.Items.Clear();
            _sistemas.DisplayMemberPath = nameof(SistemaClassificacao.Rotulo);
            if (_atual != null)
                foreach (SistemaClassificacao s in _atual.Sistemas) _sistemas.Items.Add(s);
            if (sel != null && _atual != null && _atual.Sistemas.Contains(sel)) _sistemas.SelectedItem = sel;
            _preenchendo = antes;
            _remover.IsEnabled = _atual != null && _atual.Sistemas.Count > 1;
        }

        private void Adicionar()
        {
            if (_atual == null) return;
            int n = _atual.Sistemas.Count + 1;
            var s = new SistemaClassificacao
            {
                Nome = "Classificação " + n,
                ParametroCodigo = _atual.Sistemas.Any(x => x.ParametroCodigo == "ClassificationCode") ? $"ClassificationCode({n})" : "ClassificationCode",
            };
            // Mesma planilha da anterior, para facilitar listas em abas da mesma pasta de trabalho.
            SistemaClassificacao ultima = _atual.Sistemas.LastOrDefault();
            if (ultima != null) s.Planilha = new MapaColunas { Arquivo = ultima.Planilha?.Arquivo, LinhaCabecalho = 0 };
            _atual.Sistemas.Add(s);
            AtualizarLista();
            _sistemas.SelectedItem = s;
            Alterou();
        }

        private void Remover()
        {
            if (_sis == null || _atual.Sistemas.Count <= 1) return;
            int i = _atual.Sistemas.IndexOf(_sis);
            _atual.Sistemas.Remove(_sis);
            AtualizarLista();
            _sistemas.SelectedIndex = Math.Min(i, _atual.Sistemas.Count - 1);
            Alterou();
        }

        private void Mover(int d)
        {
            if (_sis == null) return;
            int i = _atual.Sistemas.IndexOf(_sis), j = i + d;
            if (j < 0 || j >= _atual.Sistemas.Count) return;
            SistemaClassificacao s = _sis;
            _atual.Sistemas.RemoveAt(i);
            _atual.Sistemas.Insert(j, s);
            AtualizarLista();
            _sistemas.SelectedItem = s;
            Alterou();
        }

        private void MostrarSistema(SistemaClassificacao s)
        {
            if (_preenchendo) return;
            _sis = s;
            _detalhe.IsEnabled = s != null;
            if (s == null) return;
            _preenchendo = true;
            ModeloAutodesk modelo = ModeloAutodesk.Todos().FirstOrDefault(m => m.Formato == s.Formato &&
                string.Equals(m.Codigo, s.ParametroCodigo, StringComparison.CurrentCultureIgnoreCase) &&
                (s.Formato == FormatoClassificacao.CodigoDescricao || string.Equals(m.Descricao ?? "", s.ParametroDescricao ?? "", StringComparison.CurrentCultureIgnoreCase)));
            _modelo.SelectedItem = modelo == null ? _modelo.Items[0] : _modelo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => ((ModeloAutodesk)i.Tag).Rotulo == modelo.Rotulo);
            _sisNome.Text = s.Nome ?? "";
            _edicao.Text = s.Edicao ?? "";
            _formato.SelectedIndex = s.Formato == FormatoClassificacao.Separados ? 1 : 0;
            _pCodigo.Text = s.ParametroCodigo ?? "";
            _pDescricao.Text = s.ParametroDescricao ?? "";
            _gravarNome.IsChecked = s.GravarNome;
            _mapa.DefinirMapa(s.Planilha ??= new MapaColunas(), SistemaClassificacao.Campos);
            _preenchendo = false;
            AtualizarHabilitados();
            AtualizarInfo();
        }

        private void AplicarModelo()
        {
            if (Preenchendo() || !(_modelo.SelectedItem is ComboBoxItem item) || !(item.Tag is ModeloAutodesk m)) return;
            _sis.Formato = m.Formato;
            _sis.ParametroCodigo = m.Codigo;
            _sis.ParametroDescricao = m.Descricao;
            if (string.IsNullOrWhiteSpace(_sis.Nome) || _sis.Nome.StartsWith("Classificação ") || _sis.Nome == "Metrô SP" && m.Nome != "Metrô SP") _sis.Nome = m.Nome;
            SistemaClassificacao s = _sis;
            _sis = null; // força preencher de novo
            MostrarSistema(s);
            AtualizarLista();
            Alterou();
        }

        private void AtualizarHabilitados()
        {
            bool separados = _formato.SelectedIndex == 1;
            _pDescricao.IsEnabled = separados;
            _gravarNome.IsEnabled = !separados;
        }

        private void AtualizarInfo()
        {
            if (_atual == null) { _info.Text = ""; return; }
            string pend = _sis == null ? null : _mapa.Pendencia();
            int n = _atual.Sistemas.Count;
            _info.Text = $"{n} {(n == 1 ? "classificação" : "classificações")}" +
                         (pend != null ? $" · {_sis.Nome}: {pend}" : "") +
                         (_alterado ? " · alterações não salvas" : "");
        }
    }
}
