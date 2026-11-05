using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MetroSP.Ifc;

namespace MetroSP.Ui
{
    /// <summary>Uma linha do Pset MetroSP_Projeto na tabela.</summary>
    public class LinhaPropriedadeProjeto
    {
        public string Chave { get; set; }
        public string Valor { get; set; }
    }

    /// <summary>
    /// Campos do IfcProject, do IfcSite e do IfcBuilding, o Pset MetroSP_Projeto e a barra de padrões.
    /// Igual nos plugins do Revit e do Civil 3D; cada um acrescenta o que é só dele (latitude/longitude,
    /// lista de instalações) pelos espaços <c>extraTerreno</c> e <c>edificio</c>.
    /// </summary>
    public class DadosProjetoPainel : StackPanel
    {
        private readonly TextBox _pNome, _pLongo, _pDesc, _pFase;
        private readonly TextBox _sNome, _sLongo, _sDesc, _sEnd;
        private readonly TextBox _eNome, _eLongo, _eDesc, _eEnd;
        private readonly ObservableCollection<LinhaPropriedadeProjeto> _props = new ObservableCollection<LinhaPropriedadeProjeto>();
        private readonly ComboBox _padroes;
        private readonly TextBlock _infoPadrao;

        /// <summary>Algum campo mudou.</summary>
        public event Action Mudou;
        /// <summary>O usuário aplicou um padrão (o Civil 3D usa para a lista de instalações).</summary>
        public event Action<DadosProjetoIfc> PadraoUsado;
        /// <summary>Quem não usa o IfcBuilding único (Civil 3D) informa aqui o edifício que vai para o padrão salvo.</summary>
        public Func<DadosEntidadeIfc> EdificioParaPadrao { get; set; }

        /// <param name="dados">Valores iniciais.</param>
        /// <param name="dicas">Onde cada campo fica no programa, por chave ("Projeto.Nome", "Terreno.Endereco"...).</param>
        /// <param name="extraTerreno">Campos a mais no IfcSite, antes do endereço (latitude/longitude...).</param>
        /// <param name="edificio">Substitui a coluna IfcBuilding (vai embaixo, na largura toda).</param>
        public DadosProjetoPainel(DadosProjetoIfc dados, IDictionary<string, string> dicas = null, UIElement extraTerreno = null, UIElement edificio = null)
        {
            dados ??= new DadosProjetoIfc();
            dicas ??= new Dictionary<string, string>();
            string Dica(string chave, string padrao) => dicas.TryGetValue(chave, out string d) && !string.IsNullOrWhiteSpace(d) ? padrao + "\n" + d : padrao;

            // ---------- Padrão: reaproveitar os dados do contrato em outros modelos ----------
            var barra = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            barra.Children.Add(Theme.Rotulo("Padrão"));
            var salvar = Theme.Link("Salvar como padrão…");
            salvar.Margin = new Thickness(12, 0, 0, 0);
            salvar.ToolTip = "Guarda estes dados (sem latitude/longitude) para usar em outros modelos do mesmo contrato, no Revit e no Civil 3D.";
            salvar.Click += (s, e) => SalvarPadrao();
            DockPanel.SetDock(salvar, Dock.Right);
            barra.Children.Add(salvar);
            var usar = new Button { Content = "Usar", Margin = new Thickness(8, 0, 0, 0), MinWidth = 64 };
            usar.ToolTip = "Preenche com o padrão escolhido. Os campos vazios do padrão não apagam o que já está aqui.";
            usar.Click += (s, e) => UsarPadrao();
            DockPanel.SetDock(usar, Dock.Right);
            barra.Children.Add(usar);
            _padroes = new ComboBox { MinWidth = 200, VerticalContentAlignment = VerticalAlignment.Center };
            _padroes.ToolTip = "Padrões em " + DadosProjetoIfc.Pasta + ". Para levar à equipe: Padrões e planilhas.";
            barra.Children.Add(_padroes);
            Children.Add(barra);
            _infoPadrao = Theme.Detalhe();
            _infoPadrao.Margin = new Thickness(0, 3, 0, 0);
            Children.Add(_infoPadrao);
            CarregarListaPadroes(null);

            // ---------- IfcProject | IfcSite | IfcBuilding ----------
            var colunas = new Grid();
            int n = edificio == null ? 3 : 2;
            for (int i = 0; i < n; i++)
            {
                if (i > 0) colunas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
                colunas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }
            Children.Add(colunas);

            StackPanel p = Coluna(colunas, 0, "IfcProject", "O projeto: um por arquivo IFC.");
            _pNome = Campo(p, "Nome", dados.Projeto?.Nome, Dica("Projeto.Nome", "Código ou número do projeto (ex.: o número do contrato ou do desenho)."));
            _pLongo = Campo(p, "Nome longo", dados.Projeto?.NomeLongo, Dica("Projeto.NomeLongo", "Nome por extenso (ex.: Linha 20-Rosa, trecho Lapa–Moema)."));
            _pDesc = Campo(p, "Descrição", dados.Projeto?.Descricao, Dica("Projeto.Descricao", "Uma frase sobre o projeto."));
            _pFase = Campo(p, "Fase", dados.Projeto?.Fase, Dica("Projeto.Fase", "Fase do projeto (ex.: Projeto Básico, Projeto Executivo)."));

            StackPanel st = Coluna(colunas, 2, "IfcSite", "O terreno onde fica a obra.");
            _sNome = Campo(st, "Nome", dados.Terreno?.Nome, Dica("Terreno.Nome", "Nome do terreno ou da área (ex.: Trecho Lapa–Moema)."));
            _sLongo = Campo(st, "Nome longo", dados.Terreno?.NomeLongo, Dica("Terreno.NomeLongo", "Nome por extenso."));
            _sDesc = Campo(st, "Descrição", dados.Terreno?.Descricao, Dica("Terreno.Descricao", "Uma frase sobre o terreno."));
            if (extraTerreno != null) st.Children.Add(extraTerreno);
            _sEnd = Campo(st, "Endereço", dados.Terreno?.Endereco, Dica("Terreno.Endereco", "Endereço do terreno (IfcPostalAddress)."));

            if (edificio == null)
            {
                StackPanel ed = Coluna(colunas, 4, "IfcBuilding", "A edificação (ex.: a estação).");
                _eNome = Campo(ed, "Nome", dados.Edificio?.Nome, Dica("Edificio.Nome", "Nome da edificação (ex.: Estação Moema)."));
                _eLongo = Campo(ed, "Nome longo", dados.Edificio?.NomeLongo, Dica("Edificio.NomeLongo", "Nome por extenso."));
                _eDesc = Campo(ed, "Descrição", dados.Edificio?.Descricao, Dica("Edificio.Descricao", "Uma frase sobre a edificação."));
                _eEnd = Campo(ed, "Endereço", dados.Edificio?.Endereco, Dica("Edificio.Endereco", "Endereço da edificação (IfcPostalAddress)."));
            }
            else Children.Add(edificio);

            // ---------- Pset MetroSP_Projeto ----------
            TextBlock tp = Theme.Secao("Dados do projeto · " + DadosProjetoIfc.NomePset);
            tp.ToolTip = "Property set gravado no IfcProject. Uma linha por propriedade; valor vazio não entra. Para apagar, selecione a linha e tecle Delete.";
            Children.Add(tp);
            foreach (var kv in dados.Propriedades ?? new Dictionary<string, string>())
                _props.Add(new LinhaPropriedadeProjeto { Chave = kv.Key, Valor = kv.Value });
            var grade = new DataGrid
            {
                AutoGenerateColumns = false, CanUserAddRows = true, CanUserDeleteRows = true, ItemsSource = _props,
                MinHeight = 90, MaxHeight = 220, HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, ToolTip = tp.ToolTip,
            };
            grade.Columns.Add(new DataGridTextColumn { Header = "Propriedade", Binding = new Binding(nameof(LinhaPropriedadeProjeto.Chave)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grade.Columns.Add(new DataGridTextColumn { Header = "Valor", Binding = new Binding(nameof(LinhaPropriedadeProjeto.Valor)), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
            Theme.StyleSelection(grade);
            grade.CellEditEnding += (s, e) => Dispatcher.BeginInvoke(new Action(() => Mudou?.Invoke()));
            _props.CollectionChanged += (s, e) => Mudou?.Invoke();
            Children.Add(grade);

            foreach (TextBox t in Caixas()) t.TextChanged += (s, e) => Mudou?.Invoke();
        }

        private IEnumerable<TextBox> Caixas() =>
            new[] { _pNome, _pLongo, _pDesc, _pFase, _sNome, _sLongo, _sDesc, _sEnd, _eNome, _eLongo, _eDesc, _eEnd }.Where(t => t != null);

        /// <summary>Os valores da tela (texto aparado). Sem a coluna IfcBuilding, o Edificio sai vazio.</summary>
        public DadosProjetoIfc Ler()
        {
            static string T(TextBox t) => t?.Text.Trim() ?? "";
            var d = new DadosProjetoIfc
            {
                Projeto = new DadosEntidadeIfc { Nome = T(_pNome), NomeLongo = T(_pLongo), Descricao = T(_pDesc), Fase = T(_pFase) },
                Terreno = new DadosEntidadeIfc { Nome = T(_sNome), NomeLongo = T(_sLongo), Descricao = T(_sDesc), Endereco = T(_sEnd) },
                Edificio = new DadosEntidadeIfc { Nome = T(_eNome), NomeLongo = T(_eLongo), Descricao = T(_eDesc), Endereco = T(_eEnd) },
            };
            foreach (var l in _props.Where(l => !string.IsNullOrWhiteSpace(l.Chave)))
                d.Propriedades[l.Chave.Trim()] = l.Valor ?? "";
            return d;
        }

        /// <summary>Põe os valores na tela (todos os campos, inclusive os vazios).</summary>
        public void Mostrar(DadosProjetoIfc d)
        {
            static void P(TextBox t, string v) { if (t != null) t.Text = v ?? ""; }
            P(_pNome, d.Projeto?.Nome); P(_pLongo, d.Projeto?.NomeLongo); P(_pDesc, d.Projeto?.Descricao); P(_pFase, d.Projeto?.Fase);
            P(_sNome, d.Terreno?.Nome); P(_sLongo, d.Terreno?.NomeLongo); P(_sDesc, d.Terreno?.Descricao); P(_sEnd, d.Terreno?.Endereco);
            P(_eNome, d.Edificio?.Nome); P(_eLongo, d.Edificio?.NomeLongo); P(_eDesc, d.Edificio?.Descricao); P(_eEnd, d.Edificio?.Endereco);
            _props.Clear();
            foreach (var kv in d.Propriedades ?? new Dictionary<string, string>())
                _props.Add(new LinhaPropriedadeProjeto { Chave = kv.Key, Valor = kv.Value });
        }

        // ---------- padrões ----------

        private void CarregarListaPadroes(string escolher)
        {
            List<string> nomes = DadosProjetoIfc.Listar();
            _padroes.ItemsSource = nomes;
            _padroes.SelectedItem = escolher != null && nomes.Contains(escolher) ? escolher : nomes.FirstOrDefault();
            _padroes.IsEnabled = nomes.Count > 0;
            _infoPadrao.Text = nomes.Count == 0 ? "Nenhum padrão salvo ainda. Preencha e use \"Salvar como padrão…\" para reaproveitar em outros modelos." : "";
            _infoPadrao.Visibility = nomes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UsarPadrao()
        {
            if (!(_padroes.SelectedItem is string nome)) return;
            DadosProjetoIfc padrao;
            try { padrao = DadosProjetoIfc.Carregar(nome); }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(this), "Não foi possível ler o padrão:\n" + ex.Message, "MetroSP", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DadosProjetoIfc d = Ler();
            d.Completar(padrao);
            Mostrar(d);
            PadraoUsado?.Invoke(padrao);
        }

        private void SalvarPadrao()
        {
            DadosProjetoIfc d = Ler();
            if (_eNome == null && EdificioParaPadrao != null) d.Edificio = EdificioParaPadrao() ?? new DadosEntidadeIfc();
            string sugestao = !string.IsNullOrWhiteSpace(d.Projeto.NomeLongo) ? d.Projeto.NomeLongo : d.Projeto.Nome;
            string nome = PedirNome(sugestao);
            if (nome == null) return;
            if (DadosProjetoIfc.Listar().Contains(DadosProjetoIfc.NomeValido(nome)) &&
                MessageBox.Show(Window.GetWindow(this), $"Já existe o padrão \"{nome}\". Substituir?", "MetroSP", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            try
            {
                d.Salvar(nome);
                CarregarListaPadroes(DadosProjetoIfc.NomeValido(nome));
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(this), "Não foi possível salvar o padrão:\n" + ex.Message, "MetroSP", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private string PedirNome(string sugestao)
        {
            var w = new Window { Title = "MetroSP - Nome do padrão", Owner = Window.GetWindow(this), Width = 420, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.CanResizeWithGrip };
            var caixa = new TextBox { Text = sugestao ?? "", Padding = new Thickness(4, 3, 4, 3) };
            var ok = new Button { Content = "Salvar", Style = Theme.PrimaryButton, IsDefault = true };
            var cancelar = new Button { Content = "Cancelar", IsCancel = true };
            ok.Click += (s, e) => { w.DialogResult = caixa.Text.Trim().Length > 0; };
            var corpo = new StackPanel();
            corpo.Children.Add(Theme.Secao("Nome do padrão"));
            corpo.Children.Add(caixa);
            Theme.Apply(w, "Salvar padrão", "Ex.: Linha 20-Rosa, Lote 2", corpo, Theme.Rodape(null, ok, cancelar));
            w.Loaded += (s, e) => { caixa.Focus(); caixa.SelectAll(); };
            return w.ShowDialog() == true ? caixa.Text.Trim() : null;
        }

        // ---------- apoio ----------

        private static StackPanel Coluna(Grid colunas, int coluna, string titulo, string dica)
        {
            var p = new StackPanel();
            TextBlock t = Theme.Secao(titulo);
            t.ToolTip = dica;
            p.Children.Add(t);
            Grid.SetColumn(p, coluna);
            colunas.Children.Add(p);
            return p;
        }

        /// <summary>Rótulo + caixa de texto, com a explicação na dica.</summary>
        public static TextBox Campo(Panel painel, string rotulo, string valor, string dica = null)
        {
            TextBlock r = Theme.Label(rotulo);
            r.Margin = new Thickness(0, 4, 0, 3);
            r.ToolTip = dica;
            painel.Children.Add(r);
            var box = new TextBox { Text = valor ?? string.Empty, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = dica };
            painel.Children.Add(box);
            return box;
        }
    }
}
