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
        private readonly ComboBox _pLonga;
        private readonly TextBlock _exemplo;
        private readonly Border _alertaCodigo;
        private readonly TextBlock _alertaTexto;
        private readonly Button _alertaTrocar;
        private readonly MapaColunasPainel _mapa;
        private readonly TextBlock _info;
        private readonly Button _remover;

        /// <summary>Nome do perfil aberto ao fechar (para a janela de verificação já usá-lo).</summary>
        public string Escolhido { get; private set; }

        public PadroesClassificacaoWindow(Document doc, string abrir)
        {
            _parametrosModelo = ParametrosDeTexto(doc);
            Title = "MetroSP - Perfis de classificação";
            Width = 1000;
            Height = 760;
            MinWidth = 760;
            MinHeight = 520;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            // Perfil
            var barra = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var rotulo = Theme.Rotulo("Perfil");
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
            Acao("Novo", "Cria um perfil vazio.", Novo);
            Acao("Duplicar", "Copia o perfil aberto com outro nome.", Duplicar);
            Acao("Excluir", "Apaga o perfil aberto deste computador (as planilhas não são apagadas).", Excluir);
            DockPanel.SetDock(acoes, Dock.Right);
            barra.Children.Add(acoes);
            _lista = new ComboBox { MinWidth = 200, IsEditable = true, IsTextSearchEnabled = true, ToolTip = "Perfis salvos neste computador. Digite para procurar. Para trazer um perfil recebido ou levar para a equipe: Coordenação › Padrões." };
            TextSearch.SetTextPath(_lista, nameof(PadraoClassificacao.Nome));
            _lista.SelectionChanged += (s, e) => { if (!_preenchendo && _lista.SelectedItem is PadraoClassificacao p) Abrir(p); };
            barra.Children.Add(_lista);

            var linhaNome = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var rotNome = Theme.Rotulo("Nome");
            DockPanel.SetDock(rotNome, Dock.Left);
            linhaNome.Children.Add(rotNome);
            _nome = new TextBox { ToolTip = "Nome do perfil (também é o nome do arquivo salvo)." };
            _nome.TextChanged += (s, e) => { if (!_preenchendo && _atual != null) { _atual.Nome = _nome.Text.Trim(); Alterou(); } };
            linhaNome.Children.Add(_nome);

            // Classificações (esquerda)
            var esquerda = new DockPanel { Margin = new Thickness(0, 0, 14, 0) };
            var tituloSis = Theme.Secao("Classificações");
            tituloSis.ToolTip = "Cada elemento pode ter mais de uma classificação, cada uma no seu parâmetro (ex.: Metrô SP em ClassificationCode e Uniformat em ClassificationCode(2)).";
            tituloSis.Margin = new Thickness(0, 0, 0, 5);
            DockPanel.SetDock(tituloSis, Dock.Top);
            esquerda.Children.Add(tituloSis);
            var botoesSis = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            var adicionar = new Button { Content = "+ Adicionar", ToolTip = "Acrescenta uma classificação ao perfil." };
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

            _sisNome = new TextBox { ToolTip = "Nome da classificação. Na escrita [Nome]Código:Título, vira o nome da IfcClassification no IFC." };
            _edicao = new TextBox { ToolTip = "Opcional: edição ou versão da lista (ex.: 2026)." };
            _sisNome.TextChanged += (s, e) => { if (!Preenchendo()) { _sis.Nome = _sisNome.Text.Trim(); AtualizarLista(); Alterou(); } };
            _edicao.TextChanged += (s, e) => { if (!Preenchendo()) { _sis.Edicao = _edicao.Text.Trim(); Alterou(); } };
            _detalhe.Children.Add(DuasColunas(Campo("Nome", _sisNome), Campo("Edição", _edicao)));

            _formato = new ComboBox();
            _formato.Items.Add(new ComboBoxItem { Content = "Código:Título", ToolTip = "Um parâmetro com \"EST.02.01:Laje maciça\", como o ClassificationCode do exportador IFC." });
            _formato.Items.Add(new ComboBoxItem { Content = "[Nome]Código:Título", ToolTip = "Igual, com o nome da classificação no começo: \"[Metrô SP]EST.02.01:Laje maciça\". O exportador IFC usa o nome como IfcClassification; sem ele, usa a classificação configurada na exportação." });
            _formato.Items.Add(new ComboBoxItem { Content = "Código e título em parâmetros separados", ToolTip = "Um parâmetro para o código e outro (opcional) para o título, como no Classification Manager." });
            _formato.SelectionChanged += (s, e) =>
            {
                if (Preenchendo()) return;
                _sis.Formato = _formato.SelectedIndex == 2 ? FormatoClassificacao.Separados : FormatoClassificacao.CodigoDescricao;
                _sis.GravarNome = _formato.SelectedIndex == 1;
                AtualizarHabilitados();
                Alterou();
            };

            _pCodigo = ComboParametros("Parâmetro de texto que recebe Código:Título (ou só o código, na escrita separada). Instância ou tipo. Digite para procurar.");
            _pDescricao = ComboParametros("Só na escrita separada: parâmetro de texto com o título. Vazio = só o código é conferido.");
            _pLonga = ComboParametros("Opcional: outro parâmetro que recebe a coluna Descrição (texto longo). Vazio = a descrição não é gravada.");
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
            _pLonga.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, e) =>
            {
                if (Preenchendo()) return;
                _sis.ParametroDescricaoLonga = _pLonga.Text.Trim();
                Alterou();
            }));
            _detalhe.Children.Add(Campo("Escrita", _formato));
            _detalhe.Children.Add(DuasColunas(Campo("Parâmetro do código", _pCodigo), Campo("Parâmetro do título", _pDescricao)));
            _detalhe.Children.Add(Campo("Descrição em outro parâmetro (opcional)", _pLonga));
            _detalhe.Children.Add(Theme.Label("Como fica no elemento"));
            _exemplo = new TextBlock
            {
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                Background = Theme.Solid(Theme.CinzaFundo),
                Padding = new Thickness(8, 5, 8, 5),
                TextWrapping = TextWrapping.Wrap,
                ToolTip = "Com o primeiro item da planilha. O plugin também reconhece \"Código - Título\", só o código e outra pontuação no código, e propõe corrigir para esta escrita.",
            };
            _detalhe.Children.Add(_exemplo);

            // Aviso quando a coluna do código repete o mesmo valor (ex.: "[LMB7]" em todas as linhas).
            _alertaTexto = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Theme.Solid(Theme.LaranjaTexto), VerticalAlignment = VerticalAlignment.Center };
            _alertaTrocar = new Button
            {
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "A coluna do Título passa a ser o código e o texto depois dos dois-pontos passa a vir da Descrição. Dá para ajustar na Planilha com a lista.",
            };
            _alertaTrocar.Click += (s, e) => TrocarColunaCodigo();
            var alertaLinha = new DockPanel();
            DockPanel.SetDock(_alertaTrocar, Dock.Right);
            alertaLinha.Children.Add(_alertaTrocar);
            alertaLinha.Children.Add(_alertaTexto);
            _alertaCodigo = new Border
            {
                Background = Theme.Solid(Theme.LaranjaSelecao),
                BorderBrush = Theme.Solid(Theme.Laranja),
                BorderThickness = new Thickness(3, 0, 0, 0),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 6, 0, 0),
                Child = alertaLinha,
                Visibility = Visibility.Collapsed,
            };
            _detalhe.Children.Add(_alertaCodigo);

            var tituloPlanilha = Theme.Secao("Planilha com a lista");
            tituloPlanilha.ToolTip = "Escolha a planilha e indique em qual coluna está cada dado. Pode ser a mesma planilha para várias classificações, em abas ou colunas diferentes.";
            _detalhe.Children.Add(tituloPlanilha);
            _mapa = new MapaColunasPainel(new MapaColunas(), SistemaClassificacao.Campos);
            _mapa.Mudou += () => { if (!_preenchendo) { _exemploItem = null; _exemploLista = null; Alterou(); } };
            _detalhe.Children.Add(_mapa);
            // A planilha vem primeiro: é o que o usuário precisa escolher; o resto já vem preenchido.
            _detalhe.Children.Remove(tituloPlanilha);
            _detalhe.Children.Remove(_mapa);
            tituloPlanilha.Margin = new Thickness(0, 0, 0, 5);
            _detalhe.Children.Insert(0, tituloPlanilha);
            _detalhe.Children.Insert(1, _mapa);
            var tituloOnde = Theme.Secao("Onde fica no modelo");
            _detalhe.Children.Insert(2, tituloOnde);

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
            var salvar = new Button { Content = "Salvar", Style = Theme.PrimaryButton, ToolTip = "Grava o perfil neste computador (" + PadraoClassificacao.Pasta + ")." };
            salvar.Click += (s, e) => Salvar();
            var fechar = new Button { Content = "Fechar", IsCancel = true };
            fechar.Click += (s, e) => Close();
            var rodape = new DockPanel();
            StackPanel botoes = Theme.Buttons(salvar, fechar);
            DockPanel.SetDock(botoes, Dock.Right);
            rodape.Children.Add(botoes);
            rodape.Children.Add(_info);

            Theme.Apply(this, "Perfis de classificação", "Planilha, colunas e parâmetro de cada classificação", corpo, rodape);
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
            var p = new PadraoClassificacao { Nome = NomeLivre("Novo perfil"), Sistemas = { new SistemaClassificacao() } };
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
            if (MessageBox.Show(this, $"Excluir o perfil \"{_atual.Nome}\" deste computador?\n\nAs planilhas que ele usa não são apagadas.",
                    Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try { _atual.Excluir(); }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível excluir:\n" + ex.Message, Title); return; }
            _padroes.Remove(_atual);
            _alterado = false;
            _atual = null;
            if (_padroes.Count == 0) _padroes.Add(new PadraoClassificacao { Sistemas = { new SistemaClassificacao() } });
            PreencherLista(_padroes[0]);
        }

        private bool Salvar()
        {
            if (_atual == null) return true;
            if (string.IsNullOrWhiteSpace(_atual.Nome)) { MessageBox.Show(this, "Dê um nome ao perfil.", Title); return false; }
            if (_padroes.Any(p => p != _atual && string.Equals(p.Nome, _atual.Nome, StringComparison.CurrentCultureIgnoreCase)))
            {
                MessageBox.Show(this, $"Já existe outro perfil chamado \"{_atual.Nome}\".", Title);
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
            _formato.SelectedIndex = s.Formato == FormatoClassificacao.Separados ? 2 : s.GravarNome ? 1 : 0;
            _pCodigo.Text = s.ParametroCodigo ?? "";
            _pDescricao.Text = s.ParametroDescricao ?? "";
            _pLonga.Text = s.ParametroDescricaoLonga ?? "";
            _exemploItem = null; _exemploLista = null;
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
            bool separados = _formato.SelectedIndex == 2;
            _pDescricao.IsEnabled = separados;
        }

        private ClassItem _exemploItem;
        private ClassificationList _exemploLista;

        /// <summary>Primeiro item da planilha com título, para o exemplo (lido de novo só quando as colunas mudam).</summary>
        private ClassItem ItemExemplo()
        {
            if (_exemploItem != null || _sis == null || _mapa.Tabela == null) return _exemploItem;
            _exemploLista = null;
            try
            {
                if (_sis.Planilha?.Coluna(SistemaClassificacao.CampoCodigo.Chave) == null) return null;
                ClassificationList l = ClassificationList.De(_mapa.Tabela, _sis.Planilha, _sis.Nome);
                _exemploLista = l;
                _exemploItem = l.Itens.FirstOrDefault(i => i.Descricao.Length > 0 && i.Pai != null) ?? l.Itens.FirstOrDefault();
            }
            catch { }
            return _exemploItem;
        }

        /// <summary>Coluna do Título com cara de código (sem espaços, curta, um valor por linha), para trocar com a do código.</summary>
        private string ColunaTituloComCaraDeCodigo()
        {
            if (_sis?.Planilha == null || _mapa.Tabela == null) return null;
            string col = _sis.Planilha.Coluna(SistemaClassificacao.CampoTitulo.Chave);
            if (col == null) return null;
            var valores = _sis.Planilha.Linhas(_mapa.Tabela).Select(l => l[SistemaClassificacao.CampoTitulo.Chave]).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
            if (valores.Count == 0) return null;
            bool curtos = valores.Count(v => v.Length <= 30 && !v.Trim().Contains(' ')) * 10 >= valores.Count * 9;
            bool unicos = valores.Distinct(StringComparer.OrdinalIgnoreCase).Count() * 10 >= valores.Count * 9;
            return curtos && unicos ? col : null;
        }

        private void MostrarAlertaCodigo()
        {
            ClassificationList l = _exemploLista;
            if (l == null || !l.CodigoRepetido) { _alertaCodigo.Visibility = Visibility.Collapsed; return; }
            string colTitulo = ColunaTituloComCaraDeCodigo();
            string exemplo = colTitulo == null ? null : _exemploItem?.Descricao;
            _alertaTexto.Text = $"A coluna do código tem \"{l.MaisRepetido}\" em {l.VezesMaisRepetido} de {l.LinhasComCodigo} linhas. " +
                                "O código tem de mudar a cada item" + (string.IsNullOrEmpty(exemplo) ? "." : $", como {exemplo}.");
            _alertaTrocar.Content = colTitulo == null ? null : $"Usar coluna {colTitulo} como código";
            _alertaTrocar.Visibility = colTitulo == null ? Visibility.Collapsed : Visibility.Visible;
            _alertaCodigo.Visibility = Visibility.Visible;
        }

        /// <summary>O Título vira o código; o texto depois dos dois-pontos passa a vir da Descrição.</summary>
        private void TrocarColunaCodigo()
        {
            string col = ColunaTituloComCaraDeCodigo();
            if (col == null || _sis == null) return;
            _sis.Planilha.DefinirColuna(SistemaClassificacao.CampoCodigo.Chave, col);
            _sis.Planilha.DefinirColuna(SistemaClassificacao.CampoTitulo.Chave, null);
            _exemploItem = null; _exemploLista = null;
            _preenchendo = true;
            _mapa.DefinirMapa(_sis.Planilha, SistemaClassificacao.Campos);
            _preenchendo = false;
            Alterou();
        }

        private void AtualizarInfo()
        {
            if (_exemplo != null && _sis != null)
            {
                ClassItem it = ItemExemplo();
                MostrarAlertaCodigo();
                _exemplo.Text = _sis.Exemplo(it) +
                    (!string.IsNullOrWhiteSpace(_sis.ParametroDescricaoLonga) && !string.IsNullOrEmpty(it?.DescricaoLonga) ? $"\n{_sis.ParametroDescricaoLonga} = {it.DescricaoLonga}" : "");
            }
            else if (_alertaCodigo != null) _alertaCodigo.Visibility = Visibility.Collapsed;
            if (_atual == null) { _info.Text = ""; return; }
            string pend = _sis == null ? null : _mapa.Pendencia();
            int n = _atual.Sistemas.Count;
            _info.Text = $"{n} {(n == 1 ? "classificação" : "classificações")}" +
                         (pend != null ? $" · {_sis.Nome}: {pend}" : "") +
                         (_alterado ? " · alterações não salvas" : "");
        }
    }
}
