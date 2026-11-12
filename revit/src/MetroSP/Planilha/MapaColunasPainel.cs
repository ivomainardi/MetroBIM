using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MetroSP.Ui;
using Grid = System.Windows.Controls.Grid;

namespace MetroSP.Planilha
{
    /// <summary>
    /// Escolha da planilha e de onde está cada dado: arquivo, aba, linha do título e uma lista de colunas por campo,
    /// com prévia das primeiras linhas. Os campos podem ser trocados depois (DefinirCampos). Igual no Revit e no Civil 3D.
    /// </summary>
    public class MapaColunasPainel : StackPanel
    {
        private List<CampoPlanilha> _campos;
        private readonly TextBox _arquivo;
        private readonly ComboBox _aba;
        private readonly ComboBox _linha;
        private readonly Grid _gradeCampos;
        private readonly DataGrid _previa;
        private readonly TextBlock _info;
        private readonly Dictionary<string, ComboBox> _combos = new Dictionary<string, ComboBox>(StringComparer.OrdinalIgnoreCase);
        private bool _carregando;

        public MapaColunas Mapa { get; private set; }
        public Tabela Tabela { get; private set; }
        public IReadOnlyList<CampoPlanilha> Campos => _campos;

        /// <summary>Disparado quando o arquivo, a aba, a linha do título ou alguma coluna muda.</summary>
        public event Action Mudou;

        public MapaColunasPainel(MapaColunas mapa, IEnumerable<CampoPlanilha> campos)
        {
            Mapa = mapa ?? new MapaColunas();
            _campos = campos?.ToList() ?? new List<CampoPlanilha>();

            // Arquivo
            var linhaArquivo = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var procurar = new Button { Content = "Escolher planilha...", Margin = new Thickness(6, 0, 0, 0), ToolTip = "Abre a planilha (.xlsx ou .csv) com a lista." };
            var recarregar = new Button { Content = "Recarregar", Margin = new Thickness(6, 0, 0, 0), ToolTip = "Lê a planilha de novo (depois de editá-la no Excel)." };
            procurar.Click += (s, e) => Procurar();
            recarregar.Click += (s, e) => Carregar(true);
            DockPanel.SetDock(recarregar, Dock.Right);
            DockPanel.SetDock(procurar, Dock.Right);
            linhaArquivo.Children.Add(recarregar);
            linhaArquivo.Children.Add(procurar);
            _arquivo = new TextBox { IsReadOnly = true, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Planilha .xlsx ou .csv" };
            linhaArquivo.Children.Add(_arquivo);
            Children.Add(linhaArquivo);

            // Aba e linha do título
            var linhaAba = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            linhaAba.Children.Add(Theme.Rotulo("Aba"));
            _aba = new ComboBox { MinWidth = 160, Margin = new Thickness(0, 0, 14, 0), ToolTip = "Aba da planilha com os dados." };
            linhaAba.Children.Add(_aba);
            linhaAba.Children.Add(Theme.Rotulo("Títulos na linha"));
            _linha = new ComboBox { MinWidth = 60, ToolTip = "Linha com os títulos das colunas. Os dados começam na linha de baixo." };
            for (int i = 1; i <= 30; i++) _linha.Items.Add(i);
            linhaAba.Children.Add(_linha);
            _info = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            linhaAba.Children.Add(_info);
            Children.Add(linhaAba);
            _aba.SelectionChanged += (s, e) =>
            {
                if (_carregando || _aba.SelectedItem == null) return;
                Mapa.Aba = _aba.SelectedItem as string;
                Mapa.LinhaCabecalho = MapaColunas.AcharCabecalho(Tabela, Mapa.Aba, _campos);
                Mapa.Colunas.Clear();
                Mapa.Sugerir(Tabela, _campos);
                Atualizar();
            };
            _linha.SelectionChanged += (s, e) =>
            {
                if (_carregando || !(_linha.SelectedItem is int n)) return;
                Mapa.LinhaCabecalho = n;
                Atualizar();
            };

            // Colunas de cada campo
            _gradeCampos = new Grid { Margin = new Thickness(0, 2, 0, 6) };
            _gradeCampos.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _gradeCampos.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 160 });
            _gradeCampos.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _gradeCampos.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 160 });
            Children.Add(_gradeCampos);

            // Prévia
            var tituloPrevia = Theme.Secao("Prévia");
            tituloPrevia.ToolTip = "Primeiras linhas da planilha, como a ferramenta vai ler.";
            Children.Add(tituloPrevia);
            _previa = new DataGrid
            {
                IsReadOnly = true,
                AutoGenerateColumns = true,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                CanUserAddRows = false,
                MaxHeight = 150,
                MinHeight = 60,
                FontSize = 11,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HorizontalGridLinesBrush = Theme.Solid(Theme.CinzaClaro),
            };
            Children.Add(_previa);

            MontarCampos();
            Carregar(false);
        }

        /// <summary>Troca os campos procurados (ex.: o usuário escolheu outros parâmetros). Mantém as colunas já escolhidas.</summary>
        public void DefinirCampos(IEnumerable<CampoPlanilha> campos)
        {
            _campos = campos?.ToList() ?? new List<CampoPlanilha>();
            if (Tabela != null) Mapa.Sugerir(Tabela, _campos);
            MontarCampos();
            Atualizar();
        }

        /// <summary>Troca o mapa mostrado (ex.: outro padrão escolhido na lista).</summary>
        public void DefinirMapa(MapaColunas mapa, IEnumerable<CampoPlanilha> campos = null)
        {
            Mapa = mapa ?? new MapaColunas();
            if (campos != null) _campos = campos.ToList();
            MontarCampos();
            Carregar(false);
        }

        /// <summary>Linhas de dados com o mapa atual (vazio se a planilha não foi lida).</summary>
        public List<LinhaPlanilha> Linhas() => Tabela == null ? new List<LinhaPlanilha>() : Mapa.Linhas(Tabela);

        /// <summary>O que falta para o mapa ficar completo (null = pronto).</summary>
        public string Pendencia()
        {
            if (string.IsNullOrEmpty(Mapa.Arquivo)) return "Escolha a planilha.";
            if (Tabela == null) return "Não foi possível ler a planilha.";
            var faltando = Mapa.Faltando(_campos);
            return faltando.Count == 0 ? null : "Indique a coluna de: " + string.Join(", ", faltando.Select(c => c.Rotulo));
        }

        private void Procurar()
        {
            var d = new Microsoft.Win32.OpenFileDialog { Title = "Planilha", Filter = LeitorPlanilha.Filtro };
            if (File.Exists(Mapa.Arquivo))
            {
                d.InitialDirectory = Path.GetDirectoryName(Mapa.Arquivo);
                d.FileName = Path.GetFileName(Mapa.Arquivo);
            }
            if (d.ShowDialog(Window.GetWindow(this)) != true) return;
            bool outro = !string.Equals(d.FileName, Mapa.Arquivo, StringComparison.OrdinalIgnoreCase);
            Mapa.Arquivo = d.FileName;
            if (outro)
            {
                Mapa.Aba = null;
                Mapa.LinhaCabecalho = 0; // acha de novo
                Mapa.Colunas.Clear();
            }
            Carregar(true);
        }

        /// <summary>Lê o arquivo do mapa; sem cabeçalho definido, acha a linha dos títulos e sugere as colunas.</summary>
        private void Carregar(bool avisar)
        {
            Tabela = null;
            _arquivo.Text = Mapa.Arquivo ?? "";
            if (!string.IsNullOrEmpty(Mapa.Arquivo))
            {
                try
                {
                    Tabela = LeitorPlanilha.Ler(Mapa.Arquivo);
                    if (string.IsNullOrEmpty(Mapa.Aba) || !Tabela.Abas.ContainsKey(Mapa.Aba))
                        Mapa.Aba = Tabela.NomesAbas.FirstOrDefault(a => MapaColunas.AcharCabecalho(Tabela, a, _campos) > 0 &&
                                                                        Tabela.Linhas(a).Count > 1) ?? Tabela.NomesAbas[0];
                    if (Mapa.LinhaCabecalho <= 0) Mapa.LinhaCabecalho = MapaColunas.AcharCabecalho(Tabela, Mapa.Aba, _campos);
                    Mapa.Sugerir(Tabela, _campos);
                }
                catch (Exception ex)
                {
                    Tabela = null;
                    if (avisar) MessageBox.Show(Window.GetWindow(this), "Não foi possível ler a planilha:\n" + ex.Message, "MetroSP");
                }
            }
            if (Mapa.LinhaCabecalho <= 0) Mapa.LinhaCabecalho = 1;

            _carregando = true;
            _aba.Items.Clear();
            if (Tabela != null) foreach (string a in Tabela.NomesAbas) _aba.Items.Add(a);
            _aba.SelectedItem = Tabela == null ? null : Mapa.Aba;
            _aba.IsEnabled = Tabela != null && Tabela.NomesAbas.Count > 1;
            _linha.SelectedItem = Math.Min(30, Mapa.LinhaCabecalho);
            _carregando = false;
            Atualizar();
        }

        private void MontarCampos()
        {
            _gradeCampos.Children.Clear();
            _gradeCampos.RowDefinitions.Clear();
            _combos.Clear();
            for (int i = 0; i < _campos.Count; i++)
            {
                CampoPlanilha c = _campos[i];
                int linha = i / 2, col = (i % 2) * 2;
                if (col == 0) _gradeCampos.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var rotulo = new TextBlock
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(col == 0 ? 0 : 14, 3, 8, 3),
                    ToolTip = c.Dica,
                };
                rotulo.Inlines.Add(new System.Windows.Documents.Run(c.Rotulo));
                if (c.Obrigatorio) rotulo.Inlines.Add(new System.Windows.Documents.Run(" *") { Foreground = Theme.Solid(Theme.Laranja), FontWeight = FontWeights.Bold });
                var combo = new ComboBox { Margin = new Thickness(0, 3, 0, 3), ToolTip = c.Dica };
                string chave = c.Chave;
                combo.SelectionChanged += (s, e) =>
                {
                    if (_carregando) return;
                    Mapa.DefinirColuna(chave, (combo.SelectedItem as ComboBoxItem)?.Tag as string);
                    Atualizar();
                };
                Grid.SetRow(rotulo, linha); Grid.SetColumn(rotulo, col);
                Grid.SetRow(combo, linha); Grid.SetColumn(combo, col + 1);
                _gradeCampos.Children.Add(rotulo);
                _gradeCampos.Children.Add(combo);
                _combos[c.Chave] = combo;
            }
        }

        /// <summary>Atualiza as listas de colunas, os destaques de obrigatório, o resumo e a prévia.</summary>
        private void Atualizar()
        {
            _carregando = true;
            List<string> cab = Tabela == null ? new List<string>() : Mapa.Cabecalho(Tabela);
            int n = Tabela == null ? 0 : Math.Max(cab.Count, Mapa.NumeroColunas(Tabela));
            foreach (CampoPlanilha c in _campos)
            {
                ComboBox combo = _combos[c.Chave];
                combo.Items.Clear();
                combo.Items.Add(new ComboBoxItem { Content = "(nenhuma)", Tag = null, Foreground = Theme.Solid(Theme.Cinza) });
                for (int i = 0; i < n; i++)
                {
                    string titulo = i < cab.Count ? cab[i] : "";
                    combo.Items.Add(new ComboBoxItem { Content = MapaColunas.Letra(i) + " · " + (string.IsNullOrWhiteSpace(titulo) ? "(sem título)" : titulo), Tag = MapaColunas.Letra(i) });
                }
                string letra = Mapa.Coluna(c.Chave);
                // Coluna salva que não existe mais nesta planilha continua aparecendo, para não sumir sem aviso.
                if (letra != null && MapaColunas.Indice(letra) >= n)
                    combo.Items.Add(new ComboBoxItem { Content = letra + " · (vazia)", Tag = letra });
                combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(x => (x.Tag as string) == letra) ?? combo.Items[0];
                bool falta = c.Obrigatorio && letra == null && Tabela != null;
                if (falta) { combo.BorderBrush = Theme.Solid(Theme.Laranja); combo.BorderThickness = new Thickness(2); }
                else { combo.ClearValue(Control.BorderBrushProperty); combo.ClearValue(Control.BorderThicknessProperty); }
                combo.IsEnabled = Tabela != null;
            }
            _carregando = false;

            List<LinhaPlanilha> linhas = Linhas();
            _info.Text = Tabela == null ? (string.IsNullOrEmpty(Mapa.Arquivo) ? "escolha a planilha (.xlsx ou .csv)" : "não foi possível ler")
                : $"{linhas.Count} linhas de dados";
            MostrarPrevia(linhas);
            Mudou?.Invoke();
        }

        private void MostrarPrevia(List<LinhaPlanilha> linhas)
        {
            var dt = new DataTable();
            var mapeados = _campos.Where(c => Mapa.Coluna(c.Chave) != null).ToList();
            dt.Columns.Add("Linha");
            foreach (CampoPlanilha c in mapeados)
            {
                string nome = c.Rotulo;
                while (dt.Columns.Contains(nome)) nome += " ";
                dt.Columns.Add(nome);
            }
            foreach (LinhaPlanilha l in linhas.Take(5))
            {
                DataRow r = dt.NewRow();
                r[0] = l.Numero;
                for (int i = 0; i < mapeados.Count; i++) r[i + 1] = l[mapeados[i].Chave] ?? "";
                dt.Rows.Add(r);
            }
            _previa.ItemsSource = dt.DefaultView;
            _previa.Visibility = mapeados.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
