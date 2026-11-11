using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MetroSP.Ui;
using DB = Autodesk.Revit.DB;

namespace MetroSP.Ids
{
    /// <summary>
    /// Tabelas de categorias do Revit → classe IFC, uma para IFC4 e outra para IFC 4.3. Começa pela tabela salva
    /// (sua, do Forma ou instalada) ou, sem ela, pelo mapeamento que o modelo usa hoje. Salvar grava a sua cópia.
    /// </summary>
    public class CategoriasIfcWindow : Window
    {
        private readonly DB.Document _doc;
        private readonly Dictionary<string, ObservableCollection<CategoriasIfc.Linha>> _tabelas = new Dictionary<string, ObservableCollection<CategoriasIfc.Linha>>();
        private readonly Dictionary<string, string> _origens = new Dictionary<string, string>();
        private readonly HashSet<string> _alteradas = new HashSet<string>();
        private readonly Dictionary<string, int> _contagem;
        private string _esquema = CategoriasIfc.Ifc4;
        private readonly DataGrid _grade;
        private readonly CheckBox _soUsadas, _soDiferentes, _soErros;
        private readonly Button _trocar;
        private readonly TextBox _busca;
        private readonly WrapPanel _selos;
        private readonly TextBlock _resumo, _origem;
        private DataGridTemplateColumn _colClasse;
        private DataGridTextColumn _colOutro;

        public CategoriasIfcWindow(DB.Document doc)
        {
            _doc = doc;
            Title = "MetroSP - Categorias IFC";
            Width = 1000;
            Height = 680;
            MinWidth = 760;
            MinHeight = 440;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _contagem = doc != null ? CategoriasIfc.Contagem(doc) : new Dictionary<string, int>();
            List<CategoriasIfc.Linha> doModelo = null;
            foreach (string e in new[] { CategoriasIfc.Ifc4, CategoriasIfc.Ifc4x3 })
            {
                string arq = CategoriasIfc.Arquivo(e);
                List<CategoriasIfc.Linha> linhas;
                if (arq != null)
                {
                    linhas = CategoriasIfc.Ler(arq);
                    _origens[e] = $"{Path.GetFileName(arq)} ({ArquivosIfc.Origem(arq)})";
                }
                else
                {
                    if (doModelo == null && doc != null)
                        try { doModelo = CategoriasIfc.DoModelo(doc); } catch { doModelo = new List<CategoriasIfc.Linha>(); }
                    linhas = (doModelo ?? new List<CategoriasIfc.Linha>()).Select(l => l.Copia()).ToList();
                    _origens[e] = "sem tabela salva: começa pelo mapeamento atual do modelo";
                }
                // Categorias do modelo que a tabela salva não traz (o Revit acrescenta as novas).
                if (doModelo != null && arq != null)
                    foreach (CategoriasIfc.Linha m in doModelo.Where(m => !linhas.Any(l => l.Chave.Equals(m.Chave, StringComparison.OrdinalIgnoreCase))))
                        linhas.Add(m.Copia());
                foreach (CategoriasIfc.Linha l in linhas)
                {
                    l.NoModelo = string.IsNullOrEmpty(l.Subcategoria) && _contagem.TryGetValue(l.Categoria, out int n) ? n : 0;
                    l.Esquema = e;
                }
                _tabelas[e] = new ObservableCollection<CategoriasIfc.Linha>(linhas);
            }

            var corpo = new DockPanel();
            var topo = new StackPanel();
            DockPanel.SetDock(topo, Dock.Top);
            var linhaEsquema = new DockPanel();
            var esquemas = new StackPanel { Orientation = Orientation.Horizontal };
            esquemas.Children.Add(Theme.Rotulo("IFC schema"));
            var r4 = new RadioButton { Content = "IFC4 (4.0.2.1)", IsChecked = true, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center, GroupName = "esq" };
            var r43 = new RadioButton { Content = "IFC 4.3", VerticalAlignment = VerticalAlignment.Center, GroupName = "esq" };
            r4.Checked += (s, e) => TrocarEsquema(CategoriasIfc.Ifc4);
            r43.Checked += (s, e) => TrocarEsquema(CategoriasIfc.Ifc4x3);
            esquemas.Children.Add(r4);
            esquemas.Children.Add(r43);
            DockPanel.SetDock(esquemas, Dock.Left);
            linhaEsquema.Children.Add(esquemas);
            _origem = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis };
            linhaEsquema.Children.Add(_origem);
            topo.Children.Add(linhaEsquema);

            _selos = new WrapPanel { Margin = new Thickness(0, 8, 0, 6) };
            topo.Children.Add(_selos);
            var filtros = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            _soUsadas = new CheckBox { Content = "Só as categorias usadas no modelo", IsChecked = doc != null, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            _soDiferentes = new CheckBox { Content = "Só as diferentes do IFC 4.3", VerticalAlignment = VerticalAlignment.Center };
            _soDiferentes.Margin = new Thickness(0, 0, 14, 0);
            _soErros = new CheckBox { Content = "Só os erros", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0),
                ToolTip = "Mostra só as linhas exportadas com classe ou tipo que não existe no IFC schema (célula em vermelho)." };
            _trocar = new Button { Padding = new Thickness(8, 1, 8, 1), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            _trocar.Click += (s, e) => TrocarSugeridas();
            _soUsadas.Click += (s, e) => Filtrar();
            _soDiferentes.Click += (s, e) => Filtrar();
            _soErros.Click += (s, e) => Filtrar();
            foreach (FrameworkElement f in new FrameworkElement[] { _soUsadas, _soDiferentes, _soErros, _trocar })
            {
                DockPanel.SetDock(f, Dock.Left);
                filtros.Children.Add(f);
            }
            _busca = new TextBox { Width = 220, HorizontalAlignment = HorizontalAlignment.Right, ToolTip = "Procurar categoria ou classe" };
            _busca.TextChanged += (s, e) => Filtrar();
            filtros.Children.Add(_busca);
            topo.Children.Add(filtros);
            corpo.Children.Add(topo);

            _grade = new DataGrid
            {
                AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, HeadersVisibility = DataGridHeadersVisibility.Column,
                SelectionMode = DataGridSelectionMode.Extended, ToolTip = "Clique duas vezes na classe ou no tipo para escolher da lista (digite parte do nome para filtrar). A lista muda com o IFC schema.",
            };
            Theme.StyleSelection(_grade);
            _grade.Columns.Add(new DataGridTextColumn { Header = "Categoria do Revit", Binding = new Binding(nameof(CategoriasIfc.Linha.Categoria)), IsReadOnly = true, Width = new DataGridLength(1.3, DataGridLengthUnitType.Star) });
            _grade.Columns.Add(new DataGridTextColumn { Header = "Subcategoria", Binding = new Binding(nameof(CategoriasIfc.Linha.Subcategoria)), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            _grade.Columns.Add(new DataGridCheckBoxColumn { Header = "Exportar", Binding = new Binding(nameof(CategoriasIfc.Linha.Exportar)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 64 });
            _grade.Columns.Add(SeletorLista.Coluna("Tipo predefinido", nameof(CategoriasIfc.Linha.Tipo),
                o => EsquemaIfc.Tipos(_esquema, ((CategoriasIfc.Linha)o).Classe).ToList(), SemTipo,
                nameof(CategoriasIfc.Linha.TipoInvalido), "Este tipo não existe para a classe neste IFC schema. Escolha um da lista.",
                new DataGridLength(150), 110));
            _grade.Columns.Add(new DataGridTextColumn { Header = "No modelo", Binding = new Binding(nameof(CategoriasIfc.Linha.NoModelo)), IsReadOnly = true, Width = 80 });
            _colOutro = new DataGridTextColumn { Header = "No IFC 4.3", Binding = new Binding(nameof(CategoriasIfc.Linha.OutroEsquemaTexto)), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) };
            _grade.Columns.Add(_colOutro);
            var estilo = new Style(typeof(DataGridRow));
            var dif = new DataTrigger { Binding = new Binding(nameof(CategoriasIfc.Linha.Diferente)), Value = true };
            dif.Setters.Add(new Setter(BackgroundProperty, Theme.Solid(Theme.LaranjaSelecao)));
            estilo.Triggers.Add(dif);
            _grade.RowStyle = estilo;
            _grade.CellEditEnding += (s, e) => Dispatcher.BeginInvoke(new Action(() => { _alteradas.Add(_esquema); Atualizar(); }), System.Windows.Threading.DispatcherPriority.Background);
            corpo.Children.Add(_grade);

            _resumo = new TextBlock();
            var copiar = new Button { Content = "Copiar do outro IFC schema...", ToolTip = "Copia a classe e o tipo da tabela do outro IFC schema (IFC4 ↔ IFC 4.3) para esta, nas linhas selecionadas ou em todas." };
            copiar.Click += (s, e) => CopiarDoOutro();
            var importar = new Button { Content = "Importar .txt..." };
            importar.Click += (s, e) => Importar();
            var exportar = new Button { Content = "Exportar .txt..." };
            exportar.Click += (s, e) => ExportarTxt();
            var padrao = new Button { Content = "Voltar ao padrão" };
            padrao.Click += (s, e) => VoltarAoPadrao();
            var salvar = new Button { Content = "Salvar", Style = Theme.PrimaryButton, IsDefault = true };
            salvar.Click += (s, e) => Salvar();
            var fechar = new Button { Content = "Fechar", IsCancel = true };
            Closing += (s, e) =>
            {
                if (_alteradas.Count == 0) return;
                MessageBoxResult r = MessageBox.Show(this, "Salvar as alterações nas tabelas de categorias?", Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (r == MessageBoxResult.Cancel) e.Cancel = true;
                else if (r == MessageBoxResult.Yes && !Salvar()) e.Cancel = true;
            };

            Theme.Apply(this, "Categorias IFC", "Em que classe IFC cada categoria do Revit é exportada, em IFC4 e em IFC 4.3", corpo,
                Theme.Rodape(_resumo, copiar, importar, exportar, padrao, fechar, salvar));
            TrocarEsquema(CategoriasIfc.Ifc4);
        }

        private const string SemClasse = "(vazio)", SemTipo = "(nenhum)";

        private string Outro => _esquema == CategoriasIfc.Ifc4 ? CategoriasIfc.Ifc4x3 : CategoriasIfc.Ifc4;

        private static string NomeEsquema(string e) => e == CategoriasIfc.Ifc4 ? "IFC4" : "IFC 4.3";

        private void TrocarEsquema(string esquema)
        {
            if (_grade == null) return;
            _grade.CommitEdit(DataGridEditingUnit.Row, true);
            _esquema = esquema;
            if (_colClasse != null) _grade.Columns.Remove(_colClasse);
            List<string> classes = EsquemaIfc.Classes(esquema).ToList();
            _colClasse = SeletorLista.Coluna("Classe IFC", nameof(CategoriasIfc.Linha.Classe), o => classes, SemClasse,
                nameof(CategoriasIfc.Linha.ClasseInvalida), $"Esta classe não existe no {NomeEsquema(esquema)}. Escolha uma da lista.",
                new DataGridLength(1.2, DataGridLengthUnitType.Star), 160,
                o => { var l = (CategoriasIfc.Linha)o; if (!EsquemaIfc.TipoValido(_esquema, l.Classe, l.Tipo)) l.Tipo = ""; });
            _grade.Columns.Insert(3, _colClasse);
            var vista = new ListCollectionView(_tabelas[esquema]) { Filter = o => Passa((CategoriasIfc.Linha)o) };
            _grade.ItemsSource = vista;
            _origem.Text = "Tabela: " + _origens[esquema];
            // A última coluna mostra, para comparar, a classe da mesma categoria na tabela do outro esquema.
            _colOutro.Header = "Classe no " + NomeEsquema(Outro);
            _soDiferentes.Content = "Só as diferentes do " + NomeEsquema(Outro);
            Atualizar();
        }

        private bool Passa(CategoriasIfc.Linha l)
        {
            if (_soUsadas.IsChecked == true && l.NoModelo == 0) return false;
            if (_soDiferentes.IsChecked == true && !l.Diferente) return false;
            if (_soErros.IsChecked == true && !(l.Exportar && (l.ClasseInvalida || l.TipoInvalido))) return false;
            string b = _busca.Text.Trim();
            if (b.Length > 0 && (l.Categoria + " " + l.Subcategoria + " " + l.Classe).IndexOf(b, StringComparison.CurrentCultureIgnoreCase) < 0) return false;
            return true;
        }

        private void Filtrar()
        {
            _grade.CommitEdit(DataGridEditingUnit.Row, true);
            try { (_grade.ItemsSource as ListCollectionView)?.Refresh(); } catch (InvalidOperationException) { }
            Atualizar(refresh: false);
        }

        private void Atualizar(bool refresh = true)
        {
            var outro = _tabelas[Outro].GroupBy(l => l.Chave, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Resumo, StringComparer.OrdinalIgnoreCase);
            foreach (CategoriasIfc.Linha l in _tabelas[_esquema])
                l.OutroEsquema = outro.TryGetValue(l.Chave, out string o) ? o : null;
            if (refresh) { try { (_grade.ItemsSource as ListCollectionView)?.Refresh(); } catch (InvalidOperationException) { } }

            ObservableCollection<CategoriasIfc.Linha> t = _tabelas[_esquema];
            int usadas = t.Count(l => l.NoModelo > 0);
            int diferentes = t.Count(l => l.Diferente);
            int semClasse = t.Count(l => l.NoModelo > 0 && (!l.Exportar || string.IsNullOrWhiteSpace(l.Classe)));
            _selos.Children.Clear();
            _selos.Children.Add(Theme.Chip($"{t.Count} categorias"));
            if (_doc != null) _selos.Children.Add(Theme.Chip($"{usadas} usadas no modelo"));
            _selos.Children.Add(Theme.Chip($"{diferentes} diferentes do {NomeEsquema(Outro)}", destaque: diferentes > 0));
            if (semClasse > 0) _selos.Children.Add(Theme.Chip($"{semClasse} usadas sem classe ou não exportadas", destaque: true));
            int foraDoEsquema = t.Count(l => l.Exportar && (l.ClasseInvalida || l.TipoInvalido));
            if (foraDoEsquema > 0) _selos.Children.Add(Theme.Chip($"{foraDoEsquema} com classe ou tipo fora do {NomeEsquema(_esquema)}", destaque: true));
            List<CategoriasIfc.Linha> sugeridas = Sugeridas();
            _trocar.Visibility = sugeridas.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (sugeridas.Count > 0)
            {
                var pares = sugeridas.Select(l => l.Classe.Trim() + " → " + EsquemaIfc.Sugestao(_esquema, l.Classe)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                _trocar.Content = pares.Count == 1 ? $"Trocar {pares[0]} ({sugeridas.Count})" : $"Corrigir classes antigas ({sugeridas.Count})";
                _trocar.ToolTip = "Troca nomes que não existem no " + NomeEsquema(_esquema) + " pela classe certa:\n" + string.Join("\n", pares);
            }
            _resumo.Text = _alteradas.Count > 0 ? "Alterações não salvas em " + string.Join(" e ", _alteradas.Select(NomeEsquema))
                : "Na exportação vale a tabela do IFC schema do setup (IFC4, Reference View e Design Transfer View usam a do IFC4).";
            _resumo.Foreground = Theme.Solid(_alteradas.Count > 0 ? Theme.LaranjaTexto : Theme.Cinza);
        }

        private List<CategoriasIfc.Linha> Sugeridas() =>
            _tabelas[_esquema].Where(l => EsquemaIfc.Sugestao(_esquema, l.Classe) != null).ToList();

        /// <summary>Troca as classes que têm um nome certo conhecido (IfcAssembly → IfcElementAssembly), mantendo o tipo se ele valer.</summary>
        private void TrocarSugeridas()
        {
            _grade.CommitEdit(DataGridEditingUnit.Row, true);
            List<CategoriasIfc.Linha> linhas = Sugeridas();
            if (linhas.Count == 0) return;
            string lista = string.Join("\n", linhas.Take(12).Select(l => $"{l.Categoria}{(string.IsNullOrEmpty(l.Subcategoria) ? "" : " / " + l.Subcategoria)}: " +
                                                                         $"{l.Classe} → {EsquemaIfc.Sugestao(_esquema, l.Classe)}")) +
                           (linhas.Count > 12 ? $"\n... e mais {linhas.Count - 12}" : "");
            if (MessageBox.Show(this, $"Trocar no {NomeEsquema(_esquema)}:\n\n{lista}", Title, MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                return;
            foreach (CategoriasIfc.Linha l in linhas)
            {
                l.Classe = EsquemaIfc.Sugestao(_esquema, l.Classe);
                if (!EsquemaIfc.TipoValido(_esquema, l.Classe, l.Tipo)) l.Tipo = "";
            }
            _alteradas.Add(_esquema);
            Atualizar();
        }

        private bool Salvar()
        {
            _grade.CommitEdit(DataGridEditingUnit.Row, true);
            var erradas = _alteradas.SelectMany(e => _tabelas[e].Where(l => l.Exportar && (l.ClasseInvalida || l.TipoInvalido))
                .Select(l => $"{NomeEsquema(e)} · {l.Categoria}{(string.IsNullOrEmpty(l.Subcategoria) ? "" : " / " + l.Subcategoria)}: " +
                             (l.ClasseInvalida ? l.Classe : l.Classe + " · " + l.Tipo))).ToList();
            if (erradas.Count > 0)
            {
                MessageBox.Show(this, "Estas linhas têm classe ou tipo que não existe no IFC schema (em vermelho na tabela). " +
                    "Escolha da lista antes de salvar (marque \"Só os erros\" para ver só essas linhas):\n\n" + string.Join("\n", erradas.Take(12)) + (erradas.Count > 12 ? $"\n... e mais {erradas.Count - 12}" : ""),
                    Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            try
            {
                foreach (string e in _alteradas.ToList())
                {
                    string destino = CategoriasIfc.Destino(e);
                    CategoriasIfc.Gravar(destino, _tabelas[e], e);
                    _origens[e] = $"{Path.GetFileName(destino)} ({ArquivosIfc.Seu})";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível salvar: " + ex.Message, Title);
                return false;
            }
            int n = _alteradas.Count;
            _alteradas.Clear();
            _origem.Text = "Tabela: " + _origens[_esquema];
            Atualizar(refresh: false);
            if (n > 0)
            {
                _resumo.Text = "Salvo em " + Path.GetDirectoryName(CategoriasIfc.Destino(_esquema));
                _resumo.Foreground = Theme.Solid(Theme.Verde);
            }
            return true;
        }

        private void CopiarDoOutro()
        {
            List<CategoriasIfc.Linha> sel = _grade.SelectedItems.OfType<CategoriasIfc.Linha>().ToList();
            string alvo = sel.Count > 0 ? $"as {sel.Count} linhas selecionadas" : "a tabela inteira";
            if (MessageBox.Show(this, $"Copiar do {NomeEsquema(Outro)} para o {NomeEsquema(_esquema)} {alvo}?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var origem = _tabelas[Outro].GroupBy(l => l.Chave, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            foreach (CategoriasIfc.Linha l in sel.Count > 0 ? sel : _tabelas[_esquema].ToList())
                if (origem.TryGetValue(l.Chave, out CategoriasIfc.Linha o)) { l.Classe = o.Classe; l.Tipo = o.Tipo; l.Exportar = o.Exportar; }
            _alteradas.Add(_esquema);
            Atualizar();
        }

        private void Importar()
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "Texto (*.txt)|*.txt", Title = $"Tabela de categorias para o {NomeEsquema(_esquema)}" };
            if (d.ShowDialog(this) != true) return;
            List<CategoriasIfc.Linha> lidas;
            try { lidas = CategoriasIfc.Ler(d.FileName); }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível ler: " + ex.Message, Title); return; }
            if (lidas.Count == 0) { MessageBox.Show(this, "O arquivo não tem linhas Categoria / Subcategoria / Classe separadas por tab.", Title); return; }
            var porChave = lidas.GroupBy(l => l.Chave, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            int mudou = 0;
            foreach (CategoriasIfc.Linha l in _tabelas[_esquema])
                if (porChave.TryGetValue(l.Chave, out CategoriasIfc.Linha n)) { l.Classe = n.Classe; l.Tipo = n.Tipo; l.Exportar = n.Exportar; porChave.Remove(l.Chave); mudou++; }
            foreach (CategoriasIfc.Linha n in porChave.Values) { n.Esquema = _esquema; _tabelas[_esquema].Add(n); mudou++; }
            _alteradas.Add(_esquema);
            Atualizar();
            _resumo.Text = $"{mudou} linhas importadas de {Path.GetFileName(d.FileName)} (salve para valer).";
        }

        private void ExportarTxt()
        {
            _grade.CommitEdit(DataGridEditingUnit.Row, true);
            var d = new Microsoft.Win32.SaveFileDialog { Filter = "Texto (*.txt)|*.txt", FileName = $"Categorias-{_esquema}.txt", Title = "Exportar tabela de categorias" };
            if (d.ShowDialog(this) != true) return;
            try { CategoriasIfc.Gravar(d.FileName, _tabelas[_esquema], _esquema); _resumo.Text = "Exportado para " + d.FileName; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Title); }
        }

        private void VoltarAoPadrao()
        {
            string seu = CategoriasIfc.Destino(_esquema);
            if (!File.Exists(seu)) { MessageBox.Show(this, $"Você não tem uma tabela própria do {NomeEsquema(_esquema)}.", Title); return; }
            if (MessageBox.Show(this, $"Apagar a sua tabela do {NomeEsquema(_esquema)}? (ela fica guardada em Anteriores)", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try { ArquivosIfc.GuardarAnterior(seu); File.Delete(seu); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Title); return; }
            MessageBox.Show(this, "Pronto. Abra a janela de novo para ver a tabela padrão.", Title);
            _alteradas.Remove(_esquema);
            Close();
        }
    }
}
