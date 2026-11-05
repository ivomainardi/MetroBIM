using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MetroSP.Planilha;
using MetroSP.Ui;

namespace MetroSP.Confronto
{
    /// <summary>
    /// Confrontar com planilha (igual no Revit e no Civil 3D): compara dados do modelo com uma planilha externa,
    /// casando cada linha por um campo-chave, mostra o que está diferente e oferece as ações (aplicar, ignorar, selecionar).
    /// Os padrões (planilha, colunas, campos e opções) ficam salvos para usar de novo.
    /// </summary>
    public class ConfrontarWindow : Window
    {
        private readonly IFonteConfronto _fonte;
        private PadraoConfronto _padrao = new PadraoConfronto();
        private IList<CampoConfronto> _campos = new List<CampoConfronto>();

        private readonly ComboBox _padroes;
        private readonly ComboBox _chave;
        private readonly ComboBox _novoCampo;
        private readonly WrapPanel _escolhidos;
        private readonly CheckBox _maiusculas;
        private readonly CheckBox _vazia;
        private readonly CheckBox _fora;
        private readonly MapaColunasPainel _mapa;
        private readonly TabControl _abas;
        private readonly WrapPanel _chips;
        private readonly DockPanel _areaTabela;
        private readonly CheckBox _verResolvidos;
        private readonly Button _aplicar, _ignorar, _desfazer, _selecionar, _relatorio;
        private readonly TextBlock _resumo;
        private FilterableGrid _tabela;
        private ResultadoConfronto _resultado;
        private bool _carregando;

        public ConfrontarWindow(IFonteConfronto fonte)
        {
            _fonte = fonte;
            Title = "MetroSP - Confrontar com planilha";
            Width = 1120;
            Height = 760;
            MinWidth = 760;
            MinHeight = 500;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // ---- padrão salvo
            _padroes = new ComboBox { MinWidth = 240, Margin = new Thickness(0, 0, 8, 0), ToolTip = "Padrões salvos: planilha, colunas, campos e opções. Escolha um para usar de novo." };
            var salvar = new Button { Content = "Salvar", ToolTip = "Salva o padrão com o nome atual (ou pede um nome)." };
            var salvarComo = new Button { Content = "Salvar como...", Margin = new Thickness(6, 0, 0, 0) };
            var excluir = Theme.Link("excluir");
            var importar = Theme.Link("importar");
            var exportar = Theme.Link("exportar");
            excluir.Margin = new Thickness(12, 0, 0, 0);
            importar.Margin = exportar.Margin = new Thickness(10, 0, 0, 0);
            importar.ToolTip = "Traz um padrão (.json) recebido de outra pessoa.";
            exportar.ToolTip = "Grava o padrão num arquivo .json para compartilhar.";
            var barraPadrao = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            barraPadrao.Children.Add(Theme.Rotulo("Padrão"));
            barraPadrao.Children.Add(_padroes);
            barraPadrao.Children.Add(salvar);
            barraPadrao.Children.Add(salvarComo);
            barraPadrao.Children.Add(excluir);
            barraPadrao.Children.Add(importar);
            barraPadrao.Children.Add(exportar);

            // ---- aba 1: o que conferir
            _chave = new ComboBox { MinWidth = 320, IsEditable = true, IsTextSearchEnabled = true, StaysOpenOnEdit = true,
                ToolTip = $"Campo do modelo que tem o mesmo valor da coluna-chave da planilha (código, marca, nome, Id...). " +
                          $"Uma linha pode casar com vários {_fonte.Termo}s (ex.: chave pelo tipo)." };
            _novoCampo = new ComboBox { MinWidth = 320, IsEditable = true, IsTextSearchEnabled = true, StaysOpenOnEdit = true, ToolTip = "Digite para procurar o campo." };
            var adicionar = new Button { Content = "Adicionar", Margin = new Thickness(6, 0, 0, 0) };
            var sugerir = Theme.Link("sugerir pelos títulos da planilha");
            sugerir.Margin = new Thickness(12, 0, 0, 0);
            sugerir.ToolTip = "Acrescenta os campos do modelo que têm o mesmo nome de uma coluna da planilha.";
            var limpar = Theme.Link("limpar");
            limpar.Margin = new Thickness(10, 0, 0, 0);
            _escolhidos = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            _maiusculas = new CheckBox { Content = "Ignorar maiúsculas e espaços", IsChecked = true, Margin = new Thickness(0, 0, 16, 0),
                ToolTip = "\"Concreto  armado\" e \"concreto armado\" contam como iguais." };
            _vazia = new CheckBox { Content = "Célula vazia não confere", IsChecked = true, Margin = new Thickness(0, 0, 16, 0),
                ToolTip = "Desmarque para apontar também os campos preenchidos no modelo que estão vazios na planilha." };
            _fora = new CheckBox { Content = $"Mostrar {_fonte.Termo}s fora da planilha", Margin = new Thickness(0, 0, 16, 0),
                ToolTip = $"{Primeira(_fonte.Termo)}s do escopo cuja chave não aparece em nenhuma linha da planilha." };

            var linhaChave = new WrapPanel();
            linhaChave.Children.Add(_chave);
            var linhaNovo = new WrapPanel();
            linhaNovo.Children.Add(_novoCampo);
            linhaNovo.Children.Add(adicionar);
            linhaNovo.Children.Add(sugerir);
            linhaNovo.Children.Add(limpar);
            var opcoes = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            opcoes.Children.Add(_maiusculas);
            opcoes.Children.Add(_vazia);
            opcoes.Children.Add(_fora);

            foreach (ComboBox cb in new[] { _chave, _novoCampo })
            {
                TextSearch.SetTextPath(cb, nameof(CampoConfronto.Rotulo));
                // Centenas de parâmetros: lista virtualizada para abrir rápido.
                cb.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
            }

            _mapa = new MapaColunasPainel(_padrao.Mapa, CamposPlanilha());
            var conferir = new StackPanel();
            conferir.Children.Add(Primeiro(Theme.Secao("Identificar cada linha pelo")));
            conferir.Children.Add(linhaChave);
            conferir.Children.Add(Theme.Secao("Dados a conferir"));
            conferir.Children.Add(linhaNovo);
            conferir.Children.Add(_escolhidos);
            conferir.Children.Add(opcoes);
            conferir.Children.Add(Theme.Secao("Planilha e colunas"));
            conferir.Children.Add(_mapa);
            var rolagem = new ScrollViewer { Content = conferir, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

            // ---- aba 2: diferenças
            _chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            _aplicar = new Button { Content = "Aplicar a planilha", Style = Theme.PrimaryButton, IsEnabled = false,
                ToolTip = "Grava o valor da planilha no modelo, nas linhas selecionadas (numa operação só, que pode ser desfeita)." };
            _ignorar = new Button { Content = "Ignorar", IsEnabled = false, ToolTip = "O modelo está certo: a diferença deixa de aparecer e fica salva no padrão." };
            _desfazer = new Button { Content = "Voltar a conferir", IsEnabled = false, ToolTip = "Tira o \"Ignorar\" das linhas selecionadas." };
            _selecionar = new Button { Content = "Selecionar no modelo", IsEnabled = false, ToolTip = $"Seleciona e mostra os {_fonte.Termo}s das linhas selecionadas (ou de todas as visíveis)." };
            _verResolvidos = new CheckBox { Content = "Mostrar ignorados e aplicados", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var acoes = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            foreach (Button b in new[] { _aplicar, _ignorar, _desfazer, _selecionar }) { b.Margin = new Thickness(0, 0, 6, 0); acoes.Children.Add(b); }
            acoes.Children.Add(_verResolvidos);
            _areaTabela = new DockPanel();
            _areaTabela.Children.Add(Theme.Detalhe("Escolha a chave, os dados a conferir e a planilha, e clique em \"Confrontar\"."));
            var dif = new DockPanel();
            DockPanel.SetDock(_chips, Dock.Top);
            DockPanel.SetDock(acoes, Dock.Top);
            dif.Children.Add(_chips);
            dif.Children.Add(acoes);
            dif.Children.Add(_areaTabela);

            _abas = new TabControl();
            _abas.Items.Add(new TabItem { Header = "O que conferir", Content = rolagem });
            _abas.Items.Add(new TabItem { Header = "Diferenças", Content = dif });

            var corpo = new DockPanel();
            DockPanel.SetDock(barraPadrao, Dock.Top);
            corpo.Children.Add(barraPadrao);
            if (_fonte.PainelEscopo != null)
            {
                _fonte.PainelEscopo.Margin = new Thickness(0, 0, 0, 8);
                DockPanel.SetDock(_fonte.PainelEscopo, Dock.Top);
                corpo.Children.Add(_fonte.PainelEscopo);
            }
            corpo.Children.Add(_abas);

            _resumo = Theme.Detalhe();
            _relatorio = new Button { Content = "Relatório...", IsEnabled = false, ToolTip = "Grava as linhas visíveis numa planilha .csv (abre no Excel), para quem vai corrigir." };
            var confrontar = new Button { Content = "Confrontar", Style = Theme.PrimaryButton, IsDefault = true };
            var fechar = new Button { Content = "Fechar", IsCancel = true };
            Theme.Apply(this, "Confrontar com planilha", _fonte.NomeDocumento + " · compare o modelo com uma planilha e resolva as diferenças",
                corpo, Theme.Rodape(_resumo, _relatorio, confrontar, fechar));

            // ---- eventos
            _padroes.SelectionChanged += (s, e) => { if (!_carregando && _padroes.SelectedItem is PadraoConfronto p) Usar(p.Copia()); };
            salvar.Click += (s, e) => Salvar(false);
            salvarComo.Click += (s, e) => Salvar(true);
            excluir.Click += (s, e) => Excluir();
            importar.Click += (s, e) => Importar();
            exportar.Click += (s, e) => Exportar();
            _chave.SelectionChanged += (s, e) =>
            {
                if (_carregando || !(_chave.SelectedItem is CampoConfronto c)) return;
                _padrao.CampoChave = c.Chave;
                _padrao.CampoChaveNome = c.Rotulo;
                _mapa.DefinirCampos(CamposPlanilha());
            };
            adicionar.Click += (s, e) => Adicionar(_novoCampo.SelectedItem as CampoConfronto
                ?? _campos.FirstOrDefault(c => string.Equals(c.Rotulo, _novoCampo.Text, StringComparison.CurrentCultureIgnoreCase)));
            _novoCampo.KeyDown += (s, e) => { if (e.Key == Key.Enter) { adicionar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; } };
            sugerir.Click += (s, e) => SugerirCampos();
            limpar.Click += (s, e) => { _padrao.Comparacoes.Clear(); MostrarEscolhidos(); _mapa.DefinirCampos(CamposPlanilha()); };
            _maiusculas.Click += (s, e) => _padrao.IgnorarMaiusculas = _maiusculas.IsChecked == true;
            _vazia.Click += (s, e) => _padrao.VaziaNaoConfere = _vazia.IsChecked == true;
            _fora.Click += (s, e) => _padrao.MostrarForaDaPlanilha = _fora.IsChecked == true;
            _fonte.EscopoMudou += () => { CarregarCampos(); _resumo.Text = "O escopo mudou: clique em \"Confrontar\" de novo."; };
            confrontar.Click += (s, e) => Confrontar();
            fechar.Click += (s, e) => Close();
            _aplicar.Click += (s, e) => Aplicar();
            _ignorar.Click += (s, e) => Ignorar(true);
            _desfazer.Click += (s, e) => Ignorar(false);
            _selecionar.Click += (s, e) => Selecionar(Selecionadas().Count > 0 ? Selecionadas() : Visiveis());
            _relatorio.Click += (s, e) => Relatorio();
            _verResolvidos.Click += (s, e) => { _tabela?.Refresh(); Contar(); };

            CarregarCampos();
            ListarPadroes(null);
            Usar(_padrao);
        }

        private static string Primeira(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

        private static TextBlock Primeiro(TextBlock t) { t.Margin = new Thickness(0, 0, 0, 5); return t; }

        // ------------------------------------------------------------------ campos

        private void CarregarCampos()
        {
            Cursor = Cursors.Wait;
            try { _campos = _fonte.Campos(); }
            catch (Exception ex) { _campos = new List<CampoConfronto>(); MessageBox.Show(this, "Não foi possível ler os campos do modelo:\n" + ex.Message, Title); }
            finally { Cursor = null; }
            _carregando = true;
            object chave = _chave.SelectedItem;
            _chave.ItemsSource = _campos;
            _novoCampo.ItemsSource = _campos;
            _chave.SelectedItem = Confrontador.Achar(_campos, _padrao.CampoChave, _padrao.CampoChaveNome) ?? chave;
            _carregando = false;
            MostrarEscolhidos();
        }

        private CampoConfronto CampoChave() => Confrontador.Achar(_campos, _padrao.CampoChave, _padrao.CampoChaveNome);

        /// <summary>Campos que o painel da planilha procura: a chave e cada dado a conferir.</summary>
        private List<CampoPlanilha> CamposPlanilha()
        {
            CampoConfronto ch = CampoChave();
            var r = new List<CampoPlanilha>
            {
                new CampoPlanilha(PadraoConfronto.ChaveColuna, "Chave" + (ch != null ? ": " + ch.Nome : ""), true,
                    "Coluna com o valor que identifica a linha no modelo.", ch?.Nome ?? "chave", "código", "codigo", "id"),
            };
            foreach (ComparacaoPadrao c in _padrao.Comparacoes)
            {
                string nome = Confrontador.Achar(_campos, c.Campo, c.CampoNome)?.Nome ?? c.CampoNome;
                r.Add(new CampoPlanilha(c.Campo, c.CampoNome, false, "Coluna da planilha com o valor esperado deste campo.", nome));
            }
            return r;
        }

        private void Adicionar(CampoConfronto c)
        {
            if (c == null) { _resumo.Text = "Escolha um campo da lista."; return; }
            if (_padrao.Comparacoes.All(x => x.Campo != c.Chave))
                _padrao.Comparacoes.Add(new ComparacaoPadrao { Campo = c.Chave, CampoNome = c.Rotulo });
            _novoCampo.Text = "";
            MostrarEscolhidos();
            _mapa.DefinirCampos(CamposPlanilha());
        }

        private void SugerirCampos()
        {
            if (_mapa.Tabela == null) { _resumo.Text = "Escolha a planilha primeiro."; return; }
            var titulos = new HashSet<string>(_mapa.Mapa.Cabecalho(_mapa.Tabela).Select(MapaColunas.Normalizar).Where(t => t.Length > 0));
            int n = 0;
            foreach (CampoConfronto c in _campos.Where(c => c.Editavel && titulos.Contains(MapaColunas.Normalizar(c.Nome))).GroupBy(c => MapaColunas.Normalizar(c.Nome)).Select(g => g.First()))
            {
                if (c.Chave == _padrao.CampoChave || _padrao.Comparacoes.Any(x => x.Campo == c.Chave)) continue;
                _padrao.Comparacoes.Add(new ComparacaoPadrao { Campo = c.Chave, CampoNome = c.Rotulo });
                n++;
            }
            MostrarEscolhidos();
            _mapa.DefinirCampos(CamposPlanilha());
            _resumo.Text = n == 0 ? "Nenhum título da planilha tem o nome de um campo do modelo." : $"{n} campos acrescentados pelos títulos da planilha.";
        }

        private void MostrarEscolhidos()
        {
            _escolhidos.Children.Clear();
            if (_padrao.Comparacoes.Count == 0)
            {
                _escolhidos.Children.Add(Theme.Detalhe("Nenhum dado escolhido. Adicione os campos que a planilha deve conferir."));
                return;
            }
            foreach (ComparacaoPadrao c in _padrao.Comparacoes.ToList())
            {
                bool existe = Confrontador.Achar(_campos, c.Campo, c.CampoNome) != null;
                Border chip = Theme.Chip(c.CampoNome + "   ×", destaque: existe);
                chip.Margin = new Thickness(0, 0, 6, 6);
                chip.Cursor = Cursors.Hand;
                chip.ToolTip = existe ? "Clique para tirar." : $"Este campo não existe nos {_fonte.Termo}s do escopo. Clique para tirar.";
                chip.MouseLeftButtonUp += (s, e) => { _padrao.Comparacoes.Remove(c); MostrarEscolhidos(); _mapa.DefinirCampos(CamposPlanilha()); };
                _escolhidos.Children.Add(chip);
            }
        }

        // ------------------------------------------------------------------ padrões

        private void ListarPadroes(string selecionar)
        {
            _carregando = true;
            var lista = Padroes.Listar(_fonte.Programa);
            _padroes.ItemsSource = null;
            _padroes.DisplayMemberPath = nameof(PadraoConfronto.Nome);
            _padroes.ItemsSource = lista;
            _padroes.SelectedItem = lista.FirstOrDefault(p => p.Nome == selecionar);
            _carregando = false;
        }

        private void Usar(PadraoConfronto p)
        {
            _padrao = p ?? new PadraoConfronto();
            if (_padrao.Mapa == null) _padrao.Mapa = new MapaColunas();
            _carregando = true;
            _maiusculas.IsChecked = _padrao.IgnorarMaiusculas;
            _vazia.IsChecked = _padrao.VaziaNaoConfere;
            _fora.IsChecked = _padrao.MostrarForaDaPlanilha;
            _chave.SelectedItem = CampoChave();
            _carregando = false;
            MostrarEscolhidos();
            _mapa.DefinirMapa(_padrao.Mapa, CamposPlanilha());
        }

        private void Salvar(bool comoNovo)
        {
            string nome = _padrao.Nome;
            if (comoNovo || string.IsNullOrWhiteSpace(nome))
            {
                nome = PedirNome(string.IsNullOrWhiteSpace(nome) ? Path.GetFileNameWithoutExtension(_padrao.Mapa?.Arquivo ?? "") : nome + " (cópia)");
                if (string.IsNullOrWhiteSpace(nome)) return;
                if (File.Exists(Padroes.Caminho(_fonte.Programa, nome)) &&
                    MessageBox.Show(this, $"Já existe o padrão \"{nome}\". Substituir?", Title, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            }
            _padrao.Nome = nome.Trim();
            _padrao.Mapa = _mapa.Mapa;
            try { Padroes.Salvar(_fonte.Programa, _padrao); }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível salvar o padrão:\n" + ex.Message, Title); return; }
            ListarPadroes(_padrao.Nome);
            _resumo.Text = $"Padrão \"{_padrao.Nome}\" salvo.";
        }

        private void Excluir()
        {
            if (string.IsNullOrWhiteSpace(_padrao.Nome) || !File.Exists(Padroes.Caminho(_fonte.Programa, _padrao.Nome))) { _resumo.Text = "Escolha um padrão salvo."; return; }
            if (MessageBox.Show(this, $"Excluir o padrão \"{_padrao.Nome}\"?", Title, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            Padroes.Excluir(_fonte.Programa, _padrao.Nome);
            _padrao.Nome = "";
            ListarPadroes(null);
        }

        private void Importar()
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "Padrão MetroSP (*.json)|*.json" };
            if (d.ShowDialog(this) != true) return;
            try
            {
                PadraoConfronto p = Padroes.Ler(d.FileName);
                if (string.IsNullOrWhiteSpace(p.Nome)) p.Nome = Path.GetFileNameWithoutExtension(d.FileName);
                Padroes.Salvar(_fonte.Programa, p);
                ListarPadroes(p.Nome);
                Usar(p.Copia());
            }
            catch (Exception ex) { MessageBox.Show(this, "Este arquivo não é um padrão do Confrontar:\n" + ex.Message, Title); }
        }

        private void Exportar()
        {
            _padrao.Mapa = _mapa.Mapa;
            var d = new Microsoft.Win32.SaveFileDialog { Filter = "Padrão MetroSP (*.json)|*.json", FileName = Padroes.NomeArquivo(string.IsNullOrWhiteSpace(_padrao.Nome) ? "padrao" : _padrao.Nome) + ".json" };
            if (d.ShowDialog(this) != true) return;
            try { Padroes.Gravar(d.FileName, _padrao); _resumo.Text = "Padrão gravado em " + d.FileName; }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível gravar:\n" + ex.Message, Title); }
        }

        private string PedirNome(string sugestao)
        {
            var w = new Window { Title = "MetroSP - Nome do padrão", Owner = this, Width = 420, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var caixa = new TextBox { Text = sugestao ?? "", Padding = new Thickness(4, 3, 4, 3) };
            var ok = new Button { Content = "Salvar", Style = Theme.PrimaryButton, IsDefault = true };
            var cancelar = new Button { Content = "Cancelar", IsCancel = true };
            ok.Click += (s, e) => { w.DialogResult = caixa.Text.Trim().Length > 0; };
            var corpo = new StackPanel();
            corpo.Children.Add(Primeiro(Theme.Secao("Nome do padrão")));
            corpo.Children.Add(caixa);
            Theme.Apply(w, "Salvar padrão", "Ex.: Quantitativo estrutura, Materiais do lote 2", corpo, Theme.Rodape(null, ok, cancelar));
            w.Loaded += (s, e) => { caixa.Focus(); caixa.SelectAll(); };
            return w.ShowDialog() == true ? caixa.Text.Trim() : null;
        }

        // ------------------------------------------------------------------ confrontar

        private void Confrontar()
        {
            _padrao.Mapa = _mapa.Mapa;
            string falta = CampoChave() == null ? "Escolha o campo que identifica cada linha."
                : _mapa.Pendencia() ?? (_padrao.Comparacoes.Count == 0 ? "Adicione os dados a conferir." :
                   _padrao.Comparacoes.All(c => _mapa.Mapa.Coluna(c.Campo) == null) ? "Indique a coluna de pelo menos um dado." : null);
            if (falta != null) { _abas.SelectedIndex = 0; Theme.MudarResumo(_resumo, new[] { falta }, ""); return; }

            Cursor = Cursors.Wait;
            try { _resultado = Confrontador.Analisar(_fonte, _padrao, _mapa.Linhas()); }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível confrontar:\n" + ex.Message, Title); return; }
            finally { Cursor = null; }
            MostrarResultado();
            _abas.SelectedIndex = 1;
        }

        private bool Resolvida(LinhaConfronto l) => l.Situacao == LinhaConfronto.Ignorado || l.Situacao == LinhaConfronto.Aplicado;

        private void MostrarResultado()
        {
            _areaTabela.Children.Clear();
            List<LinhaConfronto> linhas = _resultado.Linhas;
            _tabela = new FilterableGrid(linhas, typeof(LinhaConfronto));
            _tabela.AddColumn("Situação", nameof(LinhaConfronto.Situacao), 120);
            _tabela.AddColumn(Primeira(_fonte.Termo), nameof(LinhaConfronto.Elemento), 210);
            _tabela.AddColumn("Chave", nameof(LinhaConfronto.Chave), 110);
            _tabela.AddColumn("Campo", nameof(LinhaConfronto.Campo), 150);
            _tabela.AddColumn("No modelo", nameof(LinhaConfronto.NoModelo), 140);
            _tabela.AddColumn("Na planilha", nameof(LinhaConfronto.NaPlanilha), 140);
            _tabela.AddColumn("O que fazer", nameof(LinhaConfronto.OQueFazer), 220, fill: true);
            _tabela.AddColumn("Linha", nameof(LinhaConfronto.Linha), 60);
            _tabela.AddColumn("Categoria", nameof(LinhaConfronto.Categoria), 120);
            _tabela.AddColumn("Id", nameof(LinhaConfronto.Id), 80);
            _tabela.ExtraFilter = o => _verResolvidos.IsChecked == true || !Resolvida((LinhaConfronto)o);
            _tabela.Grid.LoadingRow += (s, e) => Pintar(e.Row);
            _tabela.Grid.SelectionChanged += (s, e) => Habilitar();
            _tabela.Grid.MouseDoubleClick += (s, e) => { if (_tabela.Grid.SelectedItem is LinhaConfronto l) Selecionar(new List<LinhaConfronto> { l }); };
            _tabela.Filtered += Contar;
            Theme.StyleSelection(_tabela.Grid);
            _tabela.Refresh();
            if (linhas.Count == 0)
                _areaTabela.Children.Add(Theme.Detalhe($"Tudo igual: {_resultado.Iguais} valores conferidos em {_resultado.ObjetosAchados} {_fonte.Termo}s."));
            else
                _areaTabela.Children.Add(_tabela);
            Contar();
        }

        private static void Pintar(DataGridRow row)
        {
            var l = (LinhaConfronto)row.Item;
            row.Background = l.Situacao == LinhaConfronto.Aplicado ? Theme.Solid(Theme.VerdeClaro)
                : l.Situacao == LinhaConfronto.Ignorado ? Theme.LinhaNormal
                : l.PodeAplicar ? Theme.LinhaProblema : Theme.LinhaAviso;
            row.Foreground = l.Situacao == LinhaConfronto.Ignorado ? Theme.Solid(Theme.Cinza) : Theme.Solid(Theme.Preto);
        }

        private void Contar()
        {
            if (_resultado == null) return;
            _chips.Children.Clear();
            var abertas = _resultado.Linhas.Where(l => !Resolvida(l)).ToList();
            _chips.Children.Add(Theme.Chip($"{_resultado.Iguais} iguais"));
            int aplicaveis = abertas.Count(l => l.PodeAplicar);
            _chips.Children.Add(Theme.Chip($"{aplicaveis} para aplicar", destaque: aplicaveis > 0));
            foreach (var g in abertas.Where(l => !l.PodeAplicar).GroupBy(l => l.Situacao).OrderByDescending(g => g.Count()))
                _chips.Children.Add(Theme.Chip($"{g.Count()} {g.Key.ToLowerInvariant()}", escuro: true));
            int ign = _resultado.Linhas.Count(l => l.Situacao == LinhaConfronto.Ignorado);
            int apl = _resultado.Linhas.Count(l => l.Situacao == LinhaConfronto.Aplicado);
            if (ign > 0) _chips.Children.Add(Theme.Chip($"{ign} ignorados"));
            if (apl > 0) _chips.Children.Add(Theme.Chip($"{apl} aplicados"));
            Theme.MudarResumo(_resumo, _resultado.Avisos,
                $"{_resultado.LinhasPlanilha} linhas da planilha · {_resultado.ObjetosAchados} {_fonte.Termo}s achados · {abertas.Count} pendências");
            Habilitar();
        }

        private List<LinhaConfronto> Selecionadas() => _tabela?.Grid.SelectedItems.OfType<LinhaConfronto>().ToList() ?? new List<LinhaConfronto>();

        private List<LinhaConfronto> Visiveis() => _tabela?.VisibleItems<LinhaConfronto>().ToList() ?? new List<LinhaConfronto>();

        private void Habilitar()
        {
            List<LinhaConfronto> sel = Selecionadas();
            int ap = sel.Count(l => l.PodeAplicar);
            _aplicar.IsEnabled = ap > 0;
            _aplicar.Content = ap > 1 ? $"Aplicar a planilha ({ap})" : "Aplicar a planilha";
            _ignorar.IsEnabled = sel.Any(l => l.EhDiferenca || l.Situacao == LinhaConfronto.SemCampo);
            _desfazer.IsEnabled = sel.Any(l => l.Situacao == LinhaConfronto.Ignorado);
            _selecionar.IsEnabled = Visiveis().Any(l => l.Objetos.Count > 0);
            _relatorio.IsEnabled = _resultado != null && _resultado.Linhas.Count > 0;
        }

        private void Aplicar()
        {
            List<LinhaConfronto> g = Selecionadas().Where(l => l.PodeAplicar).ToList();
            if (g.Count == 0) return;
            int tipos = g.Count(l => l.Grupo != null);
            string msg = $"Gravar o valor da planilha em {g.Count} campos?" +
                         (tipos > 0 ? $"\n\n{tipos} estão no tipo: valem para todos os {_fonte.Termo}s desse tipo." : "") +
                         "\n\nÉ uma operação só: pode ser desfeita.";
            if (MessageBox.Show(this, msg, Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var grav = g.Select(l => new Gravacao { Objeto = l.Objetos[0], Campo = l.CampoRef, Valor = l.Valor, Texto = l.NaPlanilha }).ToList();
            (int ok, int falhas) r;
            Cursor = Cursors.Wait;
            try { r = _fonte.Gravar(grav); }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível gravar. Nada foi alterado.\n\n" + ex.Message, Title); return; }
            finally { Cursor = null; }
            // As que não gravaram ficam como estavam.
            for (int i = 0; i < g.Count; i++)
            {
                if (!grav[i].Ok) continue;
                g[i].Situacao = LinhaConfronto.Aplicado;
                g[i].OQueFazer = "Gravado no modelo";
                g[i].NoModelo = g[i].NaPlanilha;
                g[i].PodeAplicar = false;
            }
            _tabela.Grid.Items.Refresh();
            _tabela.Refresh();
            Contar();
            _resumo.Text = $"{r.ok} valores gravados." + (r.falhas > 0 ? $" {r.falhas} não puderam ser gravados (bloqueados, em grupo ou de outro usuário)." : "");
        }

        private void Ignorar(bool ignorar)
        {
            List<LinhaConfronto> sel = Selecionadas();
            foreach (LinhaConfronto l in sel)
            {
                if (l.ChaveIgnorar == null) continue;
                if (ignorar && (l.EhDiferenca || l.Situacao == LinhaConfronto.SemCampo))
                {
                    if (!_padrao.Ignorados.Contains(l.ChaveIgnorar)) _padrao.Ignorados.Add(l.ChaveIgnorar);
                    l.Situacao = LinhaConfronto.Ignorado;
                    l.OQueFazer = "Ignorado neste padrão";
                    l.PodeAplicar = false;
                }
                else if (!ignorar && l.Situacao == LinhaConfronto.Ignorado)
                {
                    _padrao.Ignorados.Remove(l.ChaveIgnorar);
                    (l.Situacao, l.OQueFazer, l.PodeAplicar) = l.Original;
                    if (l.Situacao == LinhaConfronto.Ignorado) (l.Situacao, l.OQueFazer) = (LinhaConfronto.Diferente, "Confrontar de novo para ver a ação");
                }
            }
            bool salvo = !string.IsNullOrWhiteSpace(_padrao.Nome) && File.Exists(Padroes.Caminho(_fonte.Programa, _padrao.Nome));
            if (salvo) { try { _padrao.Mapa = _mapa.Mapa; Padroes.Salvar(_fonte.Programa, _padrao); } catch { salvo = false; } }
            _tabela.Grid.Items.Refresh();
            _tabela.Refresh();
            Contar();
            _resumo.Text = salvo ? $"Guardado no padrão \"{_padrao.Nome}\"." : "Salve o padrão para lembrar das diferenças ignoradas.";
        }

        private void Selecionar(List<LinhaConfronto> linhas)
        {
            var objs = linhas.SelectMany(l => l.Objetos).Distinct().ToList();
            if (objs.Count == 0) { _resumo.Text = $"Estas linhas não têm {_fonte.Termo} no modelo."; return; }
            try { _fonte.Selecionar(objs, true); _resumo.Text = $"{objs.Count} {_fonte.Termo}s selecionados."; }
            catch (Exception ex) { _resumo.Text = "Não foi possível selecionar: " + ex.Message; }
        }

        private void Relatorio()
        {
            var d = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Planilha (*.csv)|*.csv",
                FileName = Padroes.NomeArquivo(_fonte.NomeDocumento) + "_confronto.csv",
            };
            if (d.ShowDialog(this) != true) return;
            try { Confrontador.Relatorio(d.FileName, Visiveis(), Primeira(_fonte.Termo)); }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível gravar (o arquivo está aberto no Excel?):\n" + ex.Message, Title); return; }
            if (MessageBox.Show(this, $"Relatório gravado:\n{d.FileName}\n\nAbrir agora?", Title, MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(d.FileName) { UseShellExecute = true }); } catch { /* sem programa */ }
            }
        }
    }
}
