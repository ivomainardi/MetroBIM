using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Autodesk.Revit.DB;
using MetroSP.Ui;
using Binding = Autodesk.Revit.DB.Binding;

namespace MetroSP.Dados
{
    /// <summary>Parâmetro de projeto vinculado (para listar e exportar).</summary>
    public class LinhaParametro
    {
        public string Situacao { get; set; }
        public string Nome { get; set; }
        public string TipoDeDado { get; set; }
        public string Vinculo { get; set; }
        public string Categorias { get; set; }
        public string Grupo { get; set; }
        public string GrupoArquivo { get; set; }
        public string Descricao { get; set; }
        public string Guid { get; set; }
        public string Compartilhado { get; set; }
        public string Motivo { get; set; }
        internal ForgeTypeId Spec { get; set; }
        internal ForgeTypeId GrupoId { get; set; }
        internal List<Category> Cats { get; set; }
        internal Definition Existente { get; set; }
        public bool Grava => Situacao == GerenciarParametrosWindow.Cria || Situacao == GerenciarParametrosWindow.Acrescenta;
    }

    /// <summary>
    /// Gerenciar parâmetros: ver e exportar os parâmetros de projeto do modelo, criar parâmetros a partir de uma planilha
    /// (vinculados às categorias) e acrescentar parâmetros compartilhados em várias famílias de uma vez.
    /// </summary>
    public class GerenciarParametrosWindow : Window
    {
        public const string Cria = "Cria", Acrescenta = "Acrescenta categorias", JaExiste = "Já existe", Erro = "Erro";
        private static readonly string[] Colunas =
        {
            "Nome", "Tipo de dado", "Instância ou tipo", "Categorias", "Grupo de propriedades", "Grupo no arquivo", "Descrição", "GUID", "Compartilhado",
        };

        private readonly Document _doc;
        private readonly Autodesk.Revit.ApplicationServices.Application _app;
        private readonly TextBlock _resumo;
        private readonly TextBlock _arquivoTxt;
        private readonly DockPanel _areaModelo;
        private readonly DockPanel _areaCriar;
        private readonly WrapPanel _chipsCriar;
        private readonly Button _criar;
        private string _compartilhado;
        private List<LinhaParametro> _criacao;

        // Famílias
        private readonly ListBox _familias;
        private readonly ListBox _definicoes;
        private readonly TextBox _buscaFamilia;
        private readonly TextBox _buscaDefinicao;
        private readonly RadioButton _instancia;
        private readonly ComboBox _grupoFamilia;
        private readonly HashSet<long> _familiasMarcadas = new HashSet<long>();
        private readonly HashSet<Guid> _definicoesMarcadas = new HashSet<Guid>();
        private List<Family> _todasFamilias = new List<Family>();
        private List<ExternalDefinition> _todasDefinicoes = new List<ExternalDefinition>();

        /// <summary>Abas da janela: o comando Parâmetros acrescenta as abas dos compartilhados (ver AbasEntreJanelas).</summary>
        internal TabControl Abas { get; }
        internal TabItem AbaModelo { get; }
        internal TabItem AbaCriar { get; }
        internal TabItem AbaFamilias { get; }

        public GerenciarParametrosWindow(Autodesk.Revit.UI.UIDocument uidoc)
        {
            _doc = uidoc.Document;
            _app = _doc.Application;
            _compartilhado = _app.SharedParametersFilename;
            Title = "MetroSP - Parâmetros";
            Width = 1120;
            Height = 720;
            MinWidth = 820;
            MinHeight = 500;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // Arquivo de parâmetros compartilhados (usado para criar e para as famílias).
            _arquivoTxt = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 640, Margin = new Thickness(0, 0, 10, 0) };
            var trocar = Theme.Link("trocar...");
            trocar.Click += (s, e) => TrocarArquivo();
            var topo = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            topo.Children.Add(Theme.Rotulo("Arquivo de compartilhados"));
            topo.Children.Add(_arquivoTxt);
            topo.Children.Add(trocar);

            // ---- aba 1: parâmetros do modelo
            _areaModelo = new DockPanel();
            var exportar = new Button { Content = "Exportar planilha...", Style = Theme.PrimaryButton };
            exportar.Click += (s, e) => ExportarModelo();
            var barraModelo = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            barraModelo.Children.Add(exportar);
            var notaModelo = Theme.Detalhe("A planilha exportada tem as mesmas colunas da criação: serve de modelo para levar parâmetros a outro projeto.");
            notaModelo.Margin = new Thickness(12, 0, 0, 0);
            notaModelo.VerticalAlignment = VerticalAlignment.Center;
            barraModelo.Children.Add(notaModelo);
            var abaModelo = new DockPanel();
            DockPanel.SetDock(barraModelo, Dock.Top);
            abaModelo.Children.Add(barraModelo);
            abaModelo.Children.Add(_areaModelo);

            // ---- aba 2: criar pela planilha
            var baixar = new Button { Content = "Planilha em branco..." };
            var escolher = new Button { Content = "Escolher planilha...", Style = Theme.PrimaryButton, Margin = new Thickness(8, 0, 0, 0) };
            baixar.Click += (s, e) => PlanilhaEmBranco();
            escolher.Click += (s, e) => LerCriacao();
            var barraCriar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            barraCriar.Children.Add(baixar);
            barraCriar.Children.Add(escolher);
            _chipsCriar = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            _areaCriar = new DockPanel();
            _areaCriar.Children.Add(Theme.Detalhe(
                "Uma linha por parâmetro: Nome, Tipo de dado (Texto, Inteiro, Número, Comprimento, Área, Sim/Não...), Instância ou tipo, " +
                "Categorias (separadas por ;), Grupo de propriedades, Grupo no arquivo, Descrição, GUID (opcional) e Compartilhado (Sim/Não). " +
                "\"Sim\" grava a definição no arquivo de compartilhados acima; \"Não\" cria o parâmetro sem mexer no seu arquivo (o Revit ainda dá um GUID a ele). " +
                "Se o parâmetro já existe no modelo, as categorias novas são acrescentadas."));
            _criar = new Button { Content = "Criar", Style = Theme.PrimaryButton, IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            _criar.Click += (s, e) => Criar();
            var abaCriar = new DockPanel();
            DockPanel.SetDock(barraCriar, Dock.Top);
            DockPanel.SetDock(_chipsCriar, Dock.Top);
            DockPanel.SetDock(_criar, Dock.Bottom);
            abaCriar.Children.Add(barraCriar);
            abaCriar.Children.Add(_chipsCriar);
            abaCriar.Children.Add(_criar);
            abaCriar.Children.Add(_areaCriar);

            // ---- aba 3: adicionar em famílias
            _buscaFamilia = new TextBox { ToolTip = "Procurar família ou categoria", Margin = new Thickness(0, 0, 0, 4) };
            _familias = new ListBox { BorderBrush = Theme.Solid(Theme.CinzaClaro) };
            _buscaDefinicao = new TextBox { ToolTip = "Procurar parâmetro do arquivo", Margin = new Thickness(0, 0, 0, 4) };
            _definicoes = new ListBox { BorderBrush = Theme.Solid(Theme.CinzaClaro) };
            _instancia = new RadioButton { Content = "Instância", GroupName = "vinc", IsChecked = true, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            var tipo = new RadioButton { Content = "Tipo", GroupName = "vinc", Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
            _grupoFamilia = new ComboBox { Padding = new Thickness(4, 3, 4, 3), MinWidth = 200, ItemsSource = Grupos(), DisplayMemberPath = "Rotulo" };
            _grupoFamilia.SelectedItem = ((List<GrupoItem>)_grupoFamilia.ItemsSource).FirstOrDefault(g => g.Id.TypeId == GroupTypeId.Data.TypeId);
            var adicionar = new Button { Content = "Adicionar nas famílias", Style = Theme.PrimaryButton, Margin = new Thickness(18, 0, 0, 0) };
            adicionar.Click += (s, e) => AdicionarEmFamilias();
            var opcoes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            opcoes.Children.Add(Theme.Rotulo("Como"));
            opcoes.Children.Add(_instancia);
            opcoes.Children.Add(tipo);
            opcoes.Children.Add(Theme.Rotulo("Grupo"));
            opcoes.Children.Add(_grupoFamilia);
            opcoes.Children.Add(adicionar);
            var colunas = new System.Windows.Controls.Grid();
            colunas.ColumnDefinitions.Add(new ColumnDefinition());
            colunas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            colunas.ColumnDefinitions.Add(new ColumnDefinition());
            FrameworkElement Coluna(string titulo, TextBox busca, ListBox lista)
            {
                var d = new DockPanel();
                TextBlock t = Theme.Secao(titulo);
                t.Margin = new Thickness(0, 0, 0, 4);
                DockPanel.SetDock(t, Dock.Top);
                DockPanel.SetDock(busca, Dock.Top);
                d.Children.Add(t);
                d.Children.Add(busca);
                d.Children.Add(lista);
                return d;
            }
            FrameworkElement c1 = Coluna("Famílias carregadas", _buscaFamilia, _familias);
            FrameworkElement c2 = Coluna("Parâmetros do arquivo de compartilhados", _buscaDefinicao, _definicoes);
            System.Windows.Controls.Grid.SetColumn(c2, 2);
            colunas.Children.Add(c1);
            colunas.Children.Add(c2);
            var abaFamilias = new DockPanel();
            DockPanel.SetDock(opcoes, Dock.Bottom);
            abaFamilias.Children.Add(opcoes);
            abaFamilias.Children.Add(colunas);

            var abas = new TabControl();
            AbaModelo = new TabItem { Header = "Parâmetros de projeto", Content = abaModelo, ToolTip = "Todos os parâmetros de projeto do modelo (compartilhados ou não), para ver e exportar." };
            AbaCriar = new TabItem { Header = "Criar pela planilha", Content = abaCriar, ToolTip = "Cria vários parâmetros de uma vez a partir de uma planilha, já com as categorias." };
            AbaFamilias = new TabItem { Header = "Adicionar em famílias", Content = abaFamilias, ToolTip = "Acrescenta parâmetros compartilhados em várias famílias carregadas." };
            abas.Items.Add(AbaModelo);
            abas.Items.Add(AbaCriar);
            abas.Items.Add(AbaFamilias);
            Abas = abas;

            var corpo = new DockPanel();
            DockPanel.SetDock(topo, Dock.Top);
            corpo.Children.Add(topo);
            corpo.Children.Add(abas);

            _resumo = Theme.Detalhe();
            var fechar = new Button { Content = "Fechar", IsCancel = true };
            fechar.Click += (s, e) => Close();
            Theme.Apply(this, "Parâmetros", _doc.Title + " · compartilhados, parâmetros de projeto, criação em lote e famílias", corpo, Theme.Rodape(_resumo, fechar));

            _buscaFamilia.TextChanged += (s, e) => ListarFamilias();
            _buscaDefinicao.TextChanged += (s, e) => ListarDefinicoes();
            MostrarArquivo();
            MostrarModelo();
            CarregarFamilias();
        }

        // ------------------------------------------------------------------ apoio

        private class GrupoItem
        {
            public ForgeTypeId Id { get; set; }
            public string Rotulo { get; set; }
        }

        private static List<GrupoItem> Grupos()
        {
            var r = new List<GrupoItem>();
            foreach (ForgeTypeId g in ParameterUtils.GetAllBuiltInGroups())
            {
                string rot;
                try { rot = LabelUtils.GetLabelForGroup(g); } catch { continue; }
                if (!string.IsNullOrEmpty(rot)) r.Add(new GrupoItem { Id = g, Rotulo = rot });
            }
            return r.OrderBy(g => g.Rotulo, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static string Rotulo(ForgeTypeId spec)
        {
            try { return LabelUtils.GetLabelForSpec(spec); } catch { return spec?.TypeId; }
        }

        private static string RotuloGrupo(ForgeTypeId g)
        {
            try { return LabelUtils.GetLabelForGroup(g); } catch { return g?.TypeId; }
        }

        private static readonly Dictionary<string, ForgeTypeId> Apelidos = new Dictionary<string, ForgeTypeId>(StringComparer.OrdinalIgnoreCase)
        {
            ["Texto"] = SpecTypeId.String.Text, ["Text"] = SpecTypeId.String.Text, ["Inteiro"] = SpecTypeId.Int.Integer, ["Integer"] = SpecTypeId.Int.Integer,
            ["Número"] = SpecTypeId.Number, ["Numero"] = SpecTypeId.Number, ["Number"] = SpecTypeId.Number, ["Comprimento"] = SpecTypeId.Length,
            ["Length"] = SpecTypeId.Length, ["Área"] = SpecTypeId.Area, ["Area"] = SpecTypeId.Area, ["Volume"] = SpecTypeId.Volume,
            ["Ângulo"] = SpecTypeId.Angle, ["Angulo"] = SpecTypeId.Angle, ["Angle"] = SpecTypeId.Angle, ["Sim/Não"] = SpecTypeId.Boolean.YesNo,
            ["Sim/Nao"] = SpecTypeId.Boolean.YesNo, ["Yes/No"] = SpecTypeId.Boolean.YesNo, ["URL"] = SpecTypeId.String.Url,
            ["Texto multilinha"] = SpecTypeId.String.MultilineText, ["Moeda"] = SpecTypeId.Currency, ["Massa"] = SpecTypeId.Mass,
        };

        private static ForgeTypeId AcharSpec(string texto)
        {
            texto = (texto ?? "").Trim();
            if (texto.Length == 0) return SpecTypeId.String.Text;
            if (Apelidos.TryGetValue(texto, out ForgeTypeId a)) return a;
            foreach (ForgeTypeId s in SpecUtils.GetAllSpecs())
            {
                if (string.Equals(s.TypeId, texto, StringComparison.OrdinalIgnoreCase)) return s;
                if (string.Equals(Rotulo(s), texto, StringComparison.CurrentCultureIgnoreCase)) return s;
            }
            return null;
        }

        private static ForgeTypeId AcharGrupo(string texto)
        {
            texto = (texto ?? "").Trim();
            if (texto.Length == 0) return GroupTypeId.Data;
            foreach (ForgeTypeId g in ParameterUtils.GetAllBuiltInGroups())
                if (string.Equals(g.TypeId, texto, StringComparison.OrdinalIgnoreCase) || string.Equals(RotuloGrupo(g), texto, StringComparison.CurrentCultureIgnoreCase))
                    return g;
            return null;
        }

        private List<Category> Vinculaveis() =>
            _doc.Settings.Categories.Cast<Category>().Where(c => c.AllowsBoundParameters).OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        private void MostrarArquivo()
        {
            bool ok = !string.IsNullOrEmpty(_compartilhado) && File.Exists(_compartilhado);
            _arquivoTxt.Text = ok ? _compartilhado : "nenhum (escolha um em \"trocar...\" para criar compartilhados e usar a aba de famílias)";
            _arquivoTxt.ToolTip = _compartilhado;
            _arquivoTxt.Foreground = ok ? Theme.Solid(Theme.Preto) : Theme.Solid(Theme.LaranjaTexto);
        }

        private void TrocarArquivo()
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "Parâmetros compartilhados (*.txt)|*.txt", CheckFileExists = false, FileName = _compartilhado ?? "" };
            if (d.ShowDialog(this) != true) return;
            if (!File.Exists(d.FileName))
            {
                if (MessageBox.Show(this, "O arquivo não existe. Criar um arquivo de compartilhados novo?", Title, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                File.WriteAllText(d.FileName, "");
            }
            _compartilhado = d.FileName;
            MostrarArquivo();
            CarregarDefinicoes();
        }

        /// <summary>Abre um arquivo de compartilhados sem trocar o que o Revit usa (volta ao original no fim).</summary>
        private T ComArquivo<T>(string arquivo, Func<DefinitionFile, T> acao)
        {
            string original = _app.SharedParametersFilename;
            try
            {
                _app.SharedParametersFilename = arquivo;
                DefinitionFile f = _app.OpenSharedParameterFile() ?? throw new InvalidOperationException("O Revit não conseguiu abrir o arquivo de compartilhados " + arquivo);
                return acao(f);
            }
            finally
            {
                try { _app.SharedParametersFilename = original ?? ""; } catch { /* mantém */ }
            }
        }

        // ------------------------------------------------------------------ aba 1: modelo

        private List<LinhaParametro> DoModelo()
        {
            var r = new List<LinhaParametro>();
            DefinitionBindingMapIterator it = _doc.ParameterBindings.ForwardIterator();
            while (it.MoveNext())
            {
                Definition def = it.Key;
                var b = it.Current as ElementBinding;
                if (def == null || b == null) continue;
                var spe = def is InternalDefinition idf ? _doc.GetElement(idf.Id) as SharedParameterElement : null;
                ForgeTypeId spec = null;
                try { spec = def.GetDataType(); } catch { /* sem tipo */ }
                r.Add(new LinhaParametro
                {
                    Nome = def.Name,
                    TipoDeDado = Rotulo(spec),
                    Vinculo = b is TypeBinding ? "Tipo" : "Instância",
                    Categorias = string.Join("; ", b.Categories.Cast<Category>().Select(c => c.Name).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)),
                    Grupo = RotuloGrupo(def.GetGroupTypeId()),
                    Guid = spe?.GuidValue.ToString() ?? "",
                    Compartilhado = spe != null ? "Sim" : "Não",
                    Existente = def,
                    Cats = b.Categories.Cast<Category>().ToList(),
                    Spec = spec,
                });
            }
            return r.OrderBy(l => l.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private void MostrarModelo()
        {
            List<LinhaParametro> l = DoModelo();
            _areaModelo.Children.Clear();
            var t = new FilterableGrid(l, typeof(LinhaParametro));
            t.AddColumn("Nome", nameof(LinhaParametro.Nome), 220);
            t.AddColumn("Tipo de dado", nameof(LinhaParametro.TipoDeDado), 120);
            t.AddColumn("Instância ou tipo", nameof(LinhaParametro.Vinculo), 100);
            t.AddColumn("Grupo", nameof(LinhaParametro.Grupo), 140);
            t.AddColumn("Compartilhado", nameof(LinhaParametro.Compartilhado), 90);
            t.AddColumn("GUID", nameof(LinhaParametro.Guid), 140);
            t.AddColumn("Categorias", nameof(LinhaParametro.Categorias), 200, fill: true);
            Theme.StyleSelection(t.Grid);
            _areaModelo.Children.Add(t);
            _resumo.Text = $"{l.Count} parâmetros de projeto no modelo ({l.Count(x => x.Compartilhado == "Sim")} compartilhados).";
        }

        private void ExportarModelo()
        {
            var d = new Microsoft.Win32.SaveFileDialog { Filter = "Planilha do Excel (*.xlsx)|*.xlsx", FileName = "parametros_" + string.Concat(_doc.Title.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)) + ".xlsx" };
            if (d.ShowDialog(this) != true) return;
            var aba = new Xlsx.Aba { Nome = "Parâmetros" };
            aba.Cabecalho.AddRange(Colunas);
            foreach (LinhaParametro l in DoModelo())
                aba.Linhas.Add(new List<string> { l.Nome, l.TipoDeDado, l.Vinculo, l.Categorias, l.Grupo, "MetroSP", "", l.Guid, l.Compartilhado });
            try
            {
                Xlsx.Gravar(d.FileName, new[] { aba, Listas() });
                _resumo.Text = "Planilha gravada: " + d.FileName;
            }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível gravar a planilha:\n" + ex.Message, Title); }
        }

        /// <summary>Aba com os valores aceitos (tipos de dado, grupos e categorias), para consulta no Excel.</summary>
        private Xlsx.Aba Listas()
        {
            var a = new Xlsx.Aba { Nome = "Valores aceitos" };
            a.Cabecalho.AddRange(new[] { "Tipo de dado", "Grupo de propriedades", "Categorias" });
            List<string> specs = new[] { "Texto", "Inteiro", "Número", "Comprimento", "Área", "Volume", "Ângulo", "Sim/Não", "URL", "Texto multilinha" }
                .Concat(SpecUtils.GetAllSpecs().Select(Rotulo).Where(s => !string.IsNullOrEmpty(s)).OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase)).Distinct().ToList();
            List<string> grupos = Grupos().Select(g => g.Rotulo).ToList();
            List<string> cats = Vinculaveis().Select(c => c.Name).ToList();
            for (int i = 0; i < new[] { specs.Count, grupos.Count, cats.Count }.Max(); i++)
                a.Linhas.Add(new List<string> { i < specs.Count ? specs[i] : "", i < grupos.Count ? grupos[i] : "", i < cats.Count ? cats[i] : "" });
            return a;
        }

        // ------------------------------------------------------------------ aba 2: criar

        private void PlanilhaEmBranco()
        {
            var d = new Microsoft.Win32.SaveFileDialog { Filter = "Planilha do Excel (*.xlsx)|*.xlsx", FileName = "novos_parametros.xlsx" };
            if (d.ShowDialog(this) != true) return;
            var aba = new Xlsx.Aba { Nome = "Parâmetros" };
            aba.Cabecalho.AddRange(Colunas);
            aba.Linhas.Add(new List<string> { "MSP_Exemplo", "Texto", "Instância", "Paredes; Pisos", RotuloGrupo(GroupTypeId.Data), "MetroSP", "Apague esta linha", "", "Sim" });
            try { Xlsx.Gravar(d.FileName, new[] { aba, Listas() }); _resumo.Text = "Planilha em branco gravada: " + d.FileName; }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível gravar a planilha:\n" + ex.Message, Title); }
        }

        private void LerCriacao()
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "Planilha do Excel (*.xlsx)|*.xlsx" };
            if (d.ShowDialog(this) != true) return;
            List<List<string>> linhas;
            try
            {
                Dictionary<string, List<List<string>>> abas = Xlsx.Ler(d.FileName);
                linhas = abas.TryGetValue("Parâmetros", out var p) ? p : abas.Values.FirstOrDefault();
            }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível ler a planilha (se estiver aberta no Excel, salve e tente de novo):\n" + ex.Message, Title); return; }
            if (linhas == null || linhas.Count < 2) { MessageBox.Show(this, "A planilha não tem linhas de parâmetros.", Title); return; }

            // Colunas pelo nome do cabeçalho (podem estar em outra ordem).
            List<string> cab = linhas[0].Select(c => (c ?? "").Trim()).ToList();
            int Col(string nome) => cab.FindIndex(c => string.Equals(c, nome, StringComparison.CurrentCultureIgnoreCase));
            string Cel(List<string> l, string nome) { int i = Col(nome); return i >= 0 && i < l.Count ? (l[i] ?? "").Trim() : ""; }
            if (Col("Nome") < 0) { MessageBox.Show(this, "A planilha precisa de uma coluna \"Nome\".", Title); return; }

            var categorias = Vinculaveis().GroupBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.CurrentCultureIgnoreCase);
            Dictionary<string, LinhaParametro> existentes = DoModelo().GroupBy(l => l.Nome, StringComparer.CurrentCultureIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.CurrentCultureIgnoreCase);
            var r = new List<LinhaParametro>();
            var nomes = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (List<string> l in linhas.Skip(1))
            {
                string nome = Cel(l, "Nome");
                if (nome.Length == 0) continue;
                var p = new LinhaParametro
                {
                    Nome = nome, TipoDeDado = Cel(l, "Tipo de dado"), Vinculo = Cel(l, "Instância ou tipo"), Categorias = Cel(l, "Categorias"),
                    Grupo = Cel(l, "Grupo de propriedades"), GrupoArquivo = Cel(l, "Grupo no arquivo"), Descricao = Cel(l, "Descrição"), Guid = Cel(l, "GUID"),
                    Compartilhado = Cel(l, "Compartilhado"),
                };
                if (p.GrupoArquivo.Length == 0) p.GrupoArquivo = "MetroSP";
                bool tipo = p.Vinculo.StartsWith("T", StringComparison.OrdinalIgnoreCase);
                p.Vinculo = tipo ? "Tipo" : "Instância";
                bool comp = !(p.Compartilhado.StartsWith("N", StringComparison.OrdinalIgnoreCase) || p.Compartilhado == "0");
                p.Compartilhado = comp ? "Sim" : "Não";
                r.Add(p);

                var problemas = new List<string>();
                if (!nomes.Add(nome)) problemas.Add("nome repetido na planilha");
                if (nome.IndexOfAny(new[] { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~' }) >= 0) problemas.Add("caractere que o Revit não aceita no nome");
                p.Spec = AcharSpec(p.TipoDeDado);
                if (p.Spec == null) problemas.Add($"tipo de dado \"{p.TipoDeDado}\" não reconhecido");
                else p.TipoDeDado = Rotulo(p.Spec);
                p.GrupoId = AcharGrupo(p.Grupo);
                if (p.GrupoId == null) problemas.Add($"grupo \"{p.Grupo}\" não reconhecido");
                else p.Grupo = RotuloGrupo(p.GrupoId);
                if (p.Guid.Length > 0 && !System.Guid.TryParse(p.Guid, out _)) problemas.Add("GUID inválido");
                p.Cats = new List<Category>();
                var faltam = new List<string>();
                foreach (string c in p.Categorias.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0))
                {
                    if (categorias.TryGetValue(c, out Category cat)) p.Cats.Add(cat); else faltam.Add(c);
                }
                if (faltam.Count > 0) problemas.Add("categorias não encontradas: " + string.Join(", ", faltam));
                if (p.Cats.Count == 0 && faltam.Count == 0) problemas.Add("sem categorias");
                if (comp && (string.IsNullOrEmpty(_compartilhado) || !File.Exists(_compartilhado))) problemas.Add("escolha o arquivo de compartilhados (ou marque Compartilhado = Não)");

                if (problemas.Count == 0 && existentes.TryGetValue(nome, out LinhaParametro ex))
                {
                    if (ex.Spec != null && p.Spec.TypeId != ex.Spec.TypeId) problemas.Add($"já existe no modelo como {ex.TipoDeDado}");
                    else if (ex.Vinculo != p.Vinculo) problemas.Add($"já existe no modelo como parâmetro de {ex.Vinculo.ToLowerInvariant()}");
                    else if (p.Guid.Length > 0 && ex.Guid.Length > 0 && !string.Equals(p.Guid, ex.Guid, StringComparison.OrdinalIgnoreCase)) problemas.Add("já existe no modelo com outro GUID");
                    else
                    {
                        var novas = p.Cats.Where(c => ex.Cats.All(x => x.Id != c.Id)).ToList();
                        p.Existente = ex.Existente;
                        p.Cats = ex.Cats.Concat(novas).ToList();
                        p.Situacao = novas.Count > 0 ? Acrescenta : JaExiste;
                        p.Motivo = novas.Count > 0 ? "acrescenta " + string.Join(", ", novas.Select(c => c.Name)) : "já está no modelo com estas categorias";
                        continue;
                    }
                }
                p.Situacao = problemas.Count > 0 ? Erro : Cria;
                p.Motivo = string.Join("; ", problemas);
            }
            _criacao = r;

            _chipsCriar.Children.Clear();
            _chipsCriar.Children.Add(Theme.Chip($"{r.Count(x => x.Situacao == Cria)} novos", destaque: true));
            _chipsCriar.Children.Add(Theme.Chip($"{r.Count(x => x.Situacao == Acrescenta)} ganham categorias"));
            _chipsCriar.Children.Add(Theme.Chip($"{r.Count(x => x.Situacao == JaExiste)} já existem"));
            int erros = r.Count(x => x.Situacao == Erro);
            if (erros > 0) _chipsCriar.Children.Add(Theme.Chip($"{erros} com erro", escuro: true));
            _areaCriar.Children.Clear();
            var t = new FilterableGrid(r, typeof(LinhaParametro));
            t.AddColumn("Situação", nameof(LinhaParametro.Situacao), 140);
            t.AddColumn("Nome", nameof(LinhaParametro.Nome), 180);
            t.AddColumn("Tipo de dado", nameof(LinhaParametro.TipoDeDado), 110);
            t.AddColumn("Instância ou tipo", nameof(LinhaParametro.Vinculo), 90);
            t.AddColumn("Grupo", nameof(LinhaParametro.Grupo), 120);
            t.AddColumn("Compartilhado", nameof(LinhaParametro.Compartilhado), 90);
            t.AddColumn("Categorias", nameof(LinhaParametro.Categorias), 170);
            t.AddColumn("Motivo", nameof(LinhaParametro.Motivo), 150, fill: true);
            t.Grid.LoadingRow += (s, e) => e.Row.Background = ((LinhaParametro)e.Row.Item).Situacao == Erro ? Theme.LinhaProblema : Theme.LinhaNormal;
            Theme.StyleSelection(t.Grid);
            _areaCriar.Children.Add(t);
            int n = r.Count(x => x.Grava);
            _criar.IsEnabled = n > 0;
            _criar.Content = n > 0 ? $"Criar {n} parâmetros" : "Nada a criar";
        }

        private void Criar()
        {
            List<LinhaParametro> g = _criacao?.Where(x => x.Grava).ToList() ?? new List<LinhaParametro>();
            if (g.Count == 0) return;
            if (MessageBox.Show(this, $"Criar ou atualizar {g.Count} parâmetros no modelo?" +
                                      (g.Any(x => x.Compartilhado == "Sim" && x.Existente == null) ? $"\n\nAs definições novas compartilhadas entram em:\n{_compartilhado}" : ""),
                    Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            int ok = 0;
            var falhas = new List<string>();
            string temporario = Path.Combine(Path.GetTempPath(), "MetroSP-parametros-" + System.Guid.NewGuid().ToString("N") + ".txt");
            void Vincular(LinhaParametro p, Definition def)
            {
                CategorySet cats = _app.Create.NewCategorySet();
                foreach (Category c in p.Cats) cats.Insert(c);
                Binding b = p.Vinculo == "Tipo" ? (Binding)_app.Create.NewTypeBinding(cats) : _app.Create.NewInstanceBinding(cats);
                bool feito = p.Existente != null
                    ? _doc.ParameterBindings.ReInsert(def, b, p.Existente.GetGroupTypeId())
                    : _doc.ParameterBindings.Insert(def, b, p.GrupoId);
                if (feito) ok++; else falhas.Add($"{p.Nome}: o Revit não aceitou o vínculo (nome já usado por outro parâmetro?)");
            }
            // Definições novas: procuradas ou criadas no arquivo e vinculadas enquanto ele está aberto.
            void Definir(IEnumerable<LinhaParametro> ps, string arquivo)
            {
                ComArquivo(arquivo, f =>
                {
                    foreach (LinhaParametro p in ps)
                    {
                        try
                        {
                            Guid? guid = System.Guid.TryParse(p.Guid, out Guid gg) ? gg : (Guid?)null;
                            ExternalDefinition ex = f.Groups.SelectMany(gr => gr.Definitions.Cast<ExternalDefinition>())
                                .FirstOrDefault(d => guid != null ? d.GUID == guid : string.Equals(d.Name, p.Nome, StringComparison.CurrentCultureIgnoreCase));
                            if (ex != null && ex.GetDataType().TypeId != p.Spec.TypeId) { falhas.Add($"{p.Nome}: no arquivo de compartilhados ele é {Rotulo(ex.GetDataType())}"); continue; }
                            if (ex == null)
                            {
                                DefinitionGroup grupo = f.Groups.get_Item(p.GrupoArquivo) ?? f.Groups.Create(p.GrupoArquivo);
                                var op = new ExternalDefinitionCreationOptions(p.Nome, p.Spec) { Description = p.Descricao ?? "" };
                                if (guid != null) op.GUID = guid.Value;
                                ex = (ExternalDefinition)grupo.Definitions.Create(op);
                            }
                            Vincular(p, ex);
                        }
                        catch (Exception e) { falhas.Add($"{p.Nome}: {e.Message}"); }
                    }
                    return 0;
                });
            }
            try
            {
                using (var t = new Transaction(_doc, "MetroSP: Criar parâmetros"))
                {
                    t.Start();
                    foreach (LinhaParametro p in g.Where(x => x.Existente != null)) Vincular(p, p.Existente);
                    List<LinhaParametro> novos = g.Where(x => x.Existente == null).ToList();
                    if (novos.Any(x => x.Compartilhado == "Sim")) Definir(novos.Where(x => x.Compartilhado == "Sim"), _compartilhado);
                    if (novos.Any(x => x.Compartilhado == "Não"))
                    {
                        File.WriteAllText(temporario, "");
                        Definir(novos.Where(x => x.Compartilhado == "Não"), temporario);
                    }
                    t.Commit();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível criar os parâmetros:\n" + ex.Message, Title);
                return;
            }
            finally { try { File.Delete(temporario); } catch { /* temporário */ } }
            MessageBox.Show(this, $"{ok} parâmetros criados ou atualizados." + (falhas.Count > 0 ? $"\n\n{falhas.Count} com problema:\n" + string.Join("\n", falhas.Take(12)) : ""), Title);
            MostrarModelo();
            _criar.IsEnabled = false;
            CarregarDefinicoes();
        }

        // ------------------------------------------------------------------ aba 3: famílias

        private void CarregarFamilias()
        {
            _todasFamilias = new FilteredElementCollector(_doc).OfClass(typeof(Family)).Cast<Family>()
                .Where(f => f.IsEditable && !f.IsInPlace && f.FamilyCategory != null)
                .OrderBy(f => f.FamilyCategory.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            ListarFamilias();
            CarregarDefinicoes();
        }

        private void ListarFamilias()
        {
            string t = _buscaFamilia.Text.Trim();
            _familias.Items.Clear();
            foreach (Family f in _todasFamilias.Where(f => t.Length == 0 || f.Name.IndexOf(t, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                                                           f.FamilyCategory.Name.IndexOf(t, StringComparison.CurrentCultureIgnoreCase) >= 0))
            {
                var c = new CheckBox { Content = $"{f.Name}   ·   {f.FamilyCategory.Name}", IsChecked = _familiasMarcadas.Contains(f.Id.Value), Margin = new Thickness(0, 1, 0, 1) };
                long id = f.Id.Value;
                c.Click += (s, e) => { if (c.IsChecked == true) _familiasMarcadas.Add(id); else _familiasMarcadas.Remove(id); };
                _familias.Items.Add(c);
            }
        }

        private void CarregarDefinicoes()
        {
            _todasDefinicoes = new List<ExternalDefinition>();
            if (!string.IsNullOrEmpty(_compartilhado) && File.Exists(_compartilhado))
            {
                try
                {
                    _todasDefinicoes = ComArquivo(_compartilhado, f => f.Groups.SelectMany(g => g.Definitions.Cast<ExternalDefinition>())
                        .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
                }
                catch (Exception ex) { _resumo.Text = "Não foi possível ler o arquivo de compartilhados: " + ex.Message; }
            }
            ListarDefinicoes();
        }

        private void ListarDefinicoes()
        {
            string t = _buscaDefinicao.Text.Trim();
            _definicoes.Items.Clear();
            if (_todasDefinicoes.Count == 0)
            {
                _definicoes.Items.Add(new TextBlock { Text = "Escolha um arquivo de compartilhados (acima).", Foreground = Theme.Solid(Theme.Cinza) });
                return;
            }
            foreach (ExternalDefinition d in _todasDefinicoes.Where(d => t.Length == 0 || d.Name.IndexOf(t, StringComparison.CurrentCultureIgnoreCase) >= 0))
            {
                var c = new CheckBox { Content = $"{d.Name}   ·   {Rotulo(d.GetDataType())} · {d.OwnerGroup?.Name}", IsChecked = _definicoesMarcadas.Contains(d.GUID), Margin = new Thickness(0, 1, 0, 1) };
                Guid id = d.GUID;
                c.Click += (s, e) => { if (c.IsChecked == true) _definicoesMarcadas.Add(id); else _definicoesMarcadas.Remove(id); };
                _definicoes.Items.Add(c);
            }
        }

        private void AdicionarEmFamilias()
        {
            List<Family> fams = _todasFamilias.Where(f => _familiasMarcadas.Contains(f.Id.Value)).ToList();
            if (fams.Count == 0 || _definicoesMarcadas.Count == 0) { MessageBox.Show(this, "Marque as famílias e os parâmetros.", Title); return; }
            ForgeTypeId grupo = (_grupoFamilia.SelectedItem as GrupoItem)?.Id ?? GroupTypeId.Data;
            bool instancia = _instancia.IsChecked == true;
            if (MessageBox.Show(this, $"Adicionar {_definicoesMarcadas.Count} parâmetros ({(instancia ? "instância" : "tipo")}) em {fams.Count} famílias?\n\n" +
                                      "Cada família é aberta, alterada e carregada de novo no projeto. Parâmetros que a família já tem ficam como estão.",
                    Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            int alteradas = 0, acrescentados = 0;
            var falhas = new List<string>();
            using (Progresso prog = Progresso.Abrir("Adicionar parâmetros em famílias", podeCancelar: true))
            {
                prog.Etapa("Famílias", fams.Count);
                ComArquivo(_compartilhado, f =>
                {
                    List<ExternalDefinition> defs = f.Groups.SelectMany(g => g.Definitions.Cast<ExternalDefinition>()).Where(d => _definicoesMarcadas.Contains(d.GUID))
                        .GroupBy(d => d.GUID).Select(x => x.First()).ToList();
                    foreach (Family fam in fams)
                    {
                        if (prog.Cancelado) break;
                        prog.Item(fam.Name);
                        Document fd = null;
                        try
                        {
                            fd = _doc.EditFamily(fam);
                            int n = 0;
                            using (var t = new Transaction(fd, "MetroSP: Adicionar parâmetros"))
                            {
                                t.Start();
                                FamilyManager fm = fd.FamilyManager;
                                foreach (ExternalDefinition d in defs)
                                {
                                    if (fm.GetParameters().Any(p => (p.IsShared && p.GUID == d.GUID) || p.Definition.Name == d.Name)) continue;
                                    fm.AddParameter(d, grupo, instancia);
                                    n++;
                                }
                                t.Commit();
                            }
                            if (n > 0)
                            {
                                fd.LoadFamily(_doc, new Classificacao.FormulaFamilia.CarregarMantendoValores());
                                alteradas++;
                                acrescentados += n;
                            }
                            prog.Log($"{fam.Name}: {(n > 0 ? n + " parâmetros acrescentados" : "já tinha todos")}");
                        }
                        catch (Exception ex) { falhas.Add($"{fam.Name}: {ex.Message}"); prog.Log($"{fam.Name}: {ex.Message}"); }
                        finally { try { fd?.Close(false); } catch { /* já fechada */ } }
                        prog.Avancar();
                    }
                    return 0;
                });
            }
            MessageBox.Show(this, $"{alteradas} famílias alteradas, {acrescentados} parâmetros acrescentados." +
                                  (falhas.Count > 0 ? $"\n\n{falhas.Count} famílias com problema:\n" + string.Join("\n", falhas.Take(10)) : ""), Title);
            _resumo.Text = $"{alteradas} famílias alteradas · {acrescentados} parâmetros acrescentados";
        }
    }
}
