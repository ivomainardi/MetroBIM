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
    /// Tabelas de categorias do Revit → classe IFC, IFC4 e IFC 4.3 lado a lado na mesma grade (uma linha por categoria,
    /// colunas de cada IFC schema). Começa pela tabela salva (sua, do Forma ou instalada) ou, sem ela, pelo mapeamento
    /// que o modelo usa hoje. Salvar grava a sua cópia de cada IFC schema alterado e fecha.
    /// </summary>
    public class CategoriasIfcWindow : Window
    {
        private static readonly string[] Esquemas = { CategoriasIfc.Ifc4, CategoriasIfc.Ifc4x3 };

        /// <summary>Uma categoria com a linha de cada IFC schema (as colunas da grade leem estas propriedades).</summary>
        public class Par
        {
            public CategoriasIfc.Linha L4 { get; set; }
            public CategoriasIfc.Linha L43 { get; set; }
            public string Categoria => L4.Categoria;
            public string Subcategoria => L4.Subcategoria;
            public string Chave => L4.Chave;
            public int NoModelo => L4.NoModelo;

            public bool Exportar4 { get => L4.Exportar; set => L4.Exportar = value; }
            public string Classe4 { get => L4.Classe; set => L4.Classe = value; }
            public string Tipo4 { get => L4.Tipo; set => L4.Tipo = value; }
            public bool ClasseInvalida4 => L4.ClasseInvalida;
            public bool TipoInvalido4 => L4.TipoInvalido;

            public bool Exportar43 { get => L43.Exportar; set => L43.Exportar = value; }
            public string Classe43 { get => L43.Classe; set => L43.Classe = value; }
            public string Tipo43 { get => L43.Tipo; set => L43.Tipo = value; }
            public bool ClasseInvalida43 => L43.ClasseInvalida;
            public bool TipoInvalido43 => L43.TipoInvalido;

            public CategoriasIfc.Linha De(string esquema) => esquema == CategoriasIfc.Ifc4 ? L4 : L43;
            public bool Diferente => !string.Equals(L4.Resumo, L43.Resumo, StringComparison.OrdinalIgnoreCase);
            public bool Erro(string esquema) { CategoriasIfc.Linha l = De(esquema); return l.Exportar && (l.ClasseInvalida || l.TipoInvalido); }
            public bool TemErro => Erro(CategoriasIfc.Ifc4) || Erro(CategoriasIfc.Ifc4x3);
        }

        private readonly DB.Document _doc;
        private readonly Dictionary<string, ObservableCollection<CategoriasIfc.Linha>> _tabelas = new Dictionary<string, ObservableCollection<CategoriasIfc.Linha>>();
        private readonly Dictionary<string, string> _origens = new Dictionary<string, string>();
        private readonly HashSet<string> _alteradas = new HashSet<string>();
        private readonly Dictionary<DataGridColumn, string> _esquemaDaColuna = new Dictionary<DataGridColumn, string>();
        private readonly ObservableCollection<Par> _pares = new ObservableCollection<Par>();
        private readonly Dictionary<string, int> _contagem;
        private readonly DataGrid _grade;
        private readonly CheckBox _soUsadas, _soDiferentes, _soErros;
        private readonly Button _trocar;
        private readonly TextBox _busca;
        private readonly WrapPanel _selos;
        private readonly TextBlock _resumo, _origem;

        public CategoriasIfcWindow(DB.Document doc)
        {
            _doc = doc;
            Title = "MetroSP - Categorias IFC";
            Width = 1180;
            Height = 700;
            MinWidth = 860;
            MinHeight = 440;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _contagem = doc != null ? CategoriasIfc.Contagem(doc) : new Dictionary<string, int>();
            List<CategoriasIfc.Linha> doModelo = null;
            foreach (string e in Esquemas)
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
                    linhas = CategoriasIfc.Padrao(doc, e, doModelo);
                    _origens[e] = "padrão MetroSP (ISO 16739), sem tabela salva";
                }
                // Categorias do modelo que a tabela salva não traz (o Revit acrescenta as novas).
                if (doModelo != null && arq != null)
                    foreach (CategoriasIfc.Linha m in doModelo.Where(m => !linhas.Any(l => l.Chave.Equals(m.Chave, StringComparison.OrdinalIgnoreCase))))
                        linhas.Add(m.Copia());
                foreach (CategoriasIfc.Linha l in linhas) l.Esquema = e;
                _tabelas[e] = new ObservableCollection<CategoriasIfc.Linha>(linhas);
            }
            MontarPares();

            var corpo = new DockPanel();
            var topo = new StackPanel();
            DockPanel.SetDock(topo, Dock.Top);
            _origem = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), TextTrimming = TextTrimming.CharacterEllipsis };
            topo.Children.Add(_origem);

            _selos = new WrapPanel { Margin = new Thickness(0, 8, 0, 6) };
            topo.Children.Add(_selos);
            var filtros = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            _soUsadas = new CheckBox { Content = "Só as categorias usadas no modelo", IsChecked = doc != null, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            _soDiferentes = new CheckBox { Content = "Só as diferentes entre IFC4 e IFC 4.3", Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            _soErros = new CheckBox { Content = "Só os erros", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0),
                ToolTip = "Mostra só as linhas exportadas com classe ou tipo que não existe no IFC schema da coluna (célula em vermelho)." };
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
                SelectionMode = DataGridSelectionMode.Extended, FrozenColumnCount = 2,
                ToolTip = "Clique duas vezes na classe ou no tipo para escolher da lista (digite parte do nome para filtrar). Cada coluna usa a lista do seu IFC schema.",
            };
            Theme.StyleSelection(_grade);
            _grade.Columns.Add(new DataGridTextColumn { Header = "Categoria do Revit", Binding = new Binding(nameof(Par.Categoria)), IsReadOnly = true, Width = new DataGridLength(1.3, DataGridLengthUnitType.Star), MinWidth = 150 });
            _grade.Columns.Add(new DataGridTextColumn { Header = "Subcategoria", Binding = new Binding(nameof(Par.Subcategoria)), IsReadOnly = true, Width = new DataGridLength(0.8, DataGridLengthUnitType.Star), MinWidth = 80 });
            _grade.Columns.Add(new DataGridTextColumn { Header = "No modelo", Binding = new Binding(nameof(Par.NoModelo)), IsReadOnly = true, Width = 72 });
            ColunasDoEsquema(CategoriasIfc.Ifc4, nameof(Par.Exportar4), nameof(Par.Classe4), nameof(Par.Tipo4),
                nameof(Par.ClasseInvalida4), nameof(Par.TipoInvalido4), p => p.L4);
            ColunasDoEsquema(CategoriasIfc.Ifc4x3, nameof(Par.Exportar43), nameof(Par.Classe43), nameof(Par.Tipo43),
                nameof(Par.ClasseInvalida43), nameof(Par.TipoInvalido43), p => p.L43);
            var estilo = new Style(typeof(DataGridRow));
            var dif = new DataTrigger { Binding = new Binding(nameof(Par.Diferente)), Value = true };
            dif.Setters.Add(new Setter(BackgroundProperty, Theme.Solid(Theme.LaranjaSelecao)));
            estilo.Triggers.Add(dif);
            _grade.RowStyle = estilo;
            _grade.CellEditEnding += (s, e) =>
            {
                if (_esquemaDaColuna.TryGetValue(e.Column, out string esq))
                    Dispatcher.BeginInvoke(new Action(() => { _alteradas.Add(esq); Atualizar(); }), System.Windows.Threading.DispatcherPriority.Background);
            };
            _grade.ItemsSource = new ListCollectionView(_pares) { Filter = o => Passa((Par)o) };
            corpo.Children.Add(_grade);

            _resumo = new TextBlock();
            var copiar = new Button { Content = "Copiar entre IFC schemas ▾", ToolTip = "Copia classe, tipo e Exportar de um IFC schema para o outro, nas linhas selecionadas ou em todas." };
            copiar.Click += (s, e) => Menu(copiar,
                ("IFC4 → IFC 4.3", () => Copiar(CategoriasIfc.Ifc4, CategoriasIfc.Ifc4x3)),
                ("IFC 4.3 → IFC4", () => Copiar(CategoriasIfc.Ifc4x3, CategoriasIfc.Ifc4)));
            var importar = new Button { Content = "Importar .txt ▾" };
            importar.Click += (s, e) => Menu(importar,
                ("Para a coluna IFC4...", () => Importar(CategoriasIfc.Ifc4)),
                ("Para a coluna IFC 4.3...", () => Importar(CategoriasIfc.Ifc4x3)));
            var exportar = new Button { Content = "Exportar .txt ▾" };
            exportar.Click += (s, e) => Menu(exportar,
                ("Tabela do IFC4...", () => ExportarTxt(CategoriasIfc.Ifc4)),
                ("Tabela do IFC 4.3...", () => ExportarTxt(CategoriasIfc.Ifc4x3)));
            var padrao = new Button { Content = "Voltar ao padrão ▾", ToolTip = "Padrão recomendado pelo MetroSP (classes do IFC4 e do IFC 4.3 conforme a ISO 16739), ou apagar a sua tabela." };
            padrao.Click += (s, e) => Menu(padrao,
                ("Padrão MetroSP (ISO 16739) nas duas colunas", () => Recomendado(Esquemas)),
                ("Padrão MetroSP só no IFC4", () => Recomendado(new[] { CategoriasIfc.Ifc4 })),
                ("Padrão MetroSP só no IFC 4.3", () => Recomendado(new[] { CategoriasIfc.Ifc4x3 })),
                ("Apagar a minha tabela do IFC4 (volta ao padrão MetroSP)...", () => VoltarAoPadrao(CategoriasIfc.Ifc4)),
                ("Apagar a minha tabela do IFC 4.3 (volta ao padrão MetroSP)...", () => VoltarAoPadrao(CategoriasIfc.Ifc4x3)));
            var salvar = new Button { Content = "Salvar", Style = Theme.PrimaryButton, IsDefault = true, ToolTip = "Grava as tabelas alteradas e fecha." };
            salvar.Click += (s, e) => { if (Salvar()) Close(); };
            var fechar = new Button { Content = "Fechar", IsCancel = true };
            Closing += (s, e) =>
            {
                if (_alteradas.Count == 0) return;
                MessageBoxResult r = MessageBox.Show(this, "Salvar as alterações nas tabelas de categorias?", Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (r == MessageBoxResult.Cancel) e.Cancel = true;
                else if (r == MessageBoxResult.Yes && !Salvar()) e.Cancel = true;
            };

            Theme.Apply(this, "Categorias IFC", "Em que classe IFC cada categoria do Revit é exportada, com IFC4 e IFC 4.3 lado a lado", corpo,
                Theme.Rodape(_resumo, copiar, importar, exportar, padrao, fechar, salvar));
            Atualizar();
        }

        private const string SemClasse = "(vazio)", SemTipo = "(nenhum)";

        private static string NomeEsquema(string e) => e == CategoriasIfc.Ifc4 ? "IFC4" : "IFC 4.3";

        /// <summary>Junta as linhas dos dois IFC schemas pela categoria; a que falta num deles entra vazia (exportar, sem classe).</summary>
        private void MontarPares()
        {
            var de43 = _tabelas[CategoriasIfc.Ifc4x3].GroupBy(l => l.Chave, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var usadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pares = new List<Par>();
            foreach (CategoriasIfc.Linha l4 in _tabelas[CategoriasIfc.Ifc4])
            {
                if (!usadas.Add(l4.Chave)) continue;
                if (!de43.TryGetValue(l4.Chave, out CategoriasIfc.Linha l43))
                {
                    l43 = new CategoriasIfc.Linha { Categoria = l4.Categoria, Subcategoria = l4.Subcategoria, Exportar = true, Classe = "", Tipo = "", Esquema = CategoriasIfc.Ifc4x3 };
                    _tabelas[CategoriasIfc.Ifc4x3].Add(l43);
                }
                pares.Add(new Par { L4 = l4, L43 = l43 });
            }
            foreach (CategoriasIfc.Linha l43 in _tabelas[CategoriasIfc.Ifc4x3].ToList())
            {
                if (!usadas.Add(l43.Chave)) continue;
                var l4 = new CategoriasIfc.Linha { Categoria = l43.Categoria, Subcategoria = l43.Subcategoria, Exportar = true, Classe = "", Tipo = "", Esquema = CategoriasIfc.Ifc4 };
                _tabelas[CategoriasIfc.Ifc4].Add(l4);
                pares.Add(new Par { L4 = l4, L43 = l43 });
            }
            foreach (Par p in pares)
            {
                int n = string.IsNullOrEmpty(p.Subcategoria) && _contagem.TryGetValue(p.Categoria, out int c) ? c : 0;
                p.L4.NoModelo = n;
                p.L43.NoModelo = n;
            }
            _pares.Clear();
            foreach (Par p in pares.OrderBy(p => p.Categoria, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.Subcategoria ?? "", StringComparer.CurrentCultureIgnoreCase))
                _pares.Add(p);
        }

        private void ColunasDoEsquema(string esquema, string exportar, string classe, string tipo, string classeInvalida, string tipoInvalido, Func<Par, CategoriasIfc.Linha> linha)
        {
            string nome = NomeEsquema(esquema);
            List<string> classes = EsquemaIfc.Classes(esquema).ToList();
            var colExportar = new DataGridCheckBoxColumn
            {
                Header = "Exp. " + nome, Width = 70,
                Binding = new Binding(exportar) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            };
            DataGridTemplateColumn colClasse = SeletorLista.Coluna($"Classe {nome}", classe, o => classes, SemClasse,
                classeInvalida, $"Esta classe não existe no {nome}. Escolha uma da lista.",
                new DataGridLength(1.2, DataGridLengthUnitType.Star), 150,
                o => { CategoriasIfc.Linha l = linha((Par)o); if (!EsquemaIfc.TipoValido(esquema, l.Classe, l.Tipo)) l.Tipo = ""; });
            DataGridTemplateColumn colTipo = SeletorLista.Coluna($"Tipo {nome}", tipo,
                o => EsquemaIfc.Tipos(esquema, linha((Par)o).Classe).ToList(), SemTipo,
                tipoInvalido, $"Este tipo não existe para a classe no {nome}. Escolha um da lista.",
                new DataGridLength(0.7, DataGridLengthUnitType.Star), 90);
            foreach (DataGridColumn c in new DataGridColumn[] { colExportar, colClasse, colTipo })
            {
                _grade.Columns.Add(c);
                _esquemaDaColuna[c] = esquema;
            }
        }

        private void Menu(Button botao, params (string Texto, Action Acao)[] itens)
        {
            var menu = new ContextMenu { PlacementTarget = botao, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
            foreach ((string texto, Action acao) in itens)
            {
                var item = new MenuItem { Header = texto };
                item.Click += (s, e) => acao();
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }

        private bool Passa(Par p)
        {
            if (_soUsadas.IsChecked == true && p.NoModelo == 0) return false;
            if (_soDiferentes.IsChecked == true && !p.Diferente) return false;
            if (_soErros.IsChecked == true && !p.TemErro) return false;
            string b = _busca.Text.Trim();
            if (b.Length > 0 && (p.Categoria + " " + p.Subcategoria + " " + p.Classe4 + " " + p.Classe43).IndexOf(b, StringComparison.CurrentCultureIgnoreCase) < 0) return false;
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
            if (refresh) { try { (_grade.ItemsSource as ListCollectionView)?.Refresh(); } catch (InvalidOperationException) { } }

            _origem.Text = $"Tabelas: IFC4 · {_origens[CategoriasIfc.Ifc4]}   |   IFC 4.3 · {_origens[CategoriasIfc.Ifc4x3]}";
            int usadas = _pares.Count(p => p.NoModelo > 0);
            int diferentes = _pares.Count(p => p.Diferente);
            _selos.Children.Clear();
            _selos.Children.Add(Theme.Chip($"{_pares.Count} categorias"));
            if (_doc != null) _selos.Children.Add(Theme.Chip($"{usadas} usadas no modelo"));
            _selos.Children.Add(Theme.Chip($"{diferentes} diferentes entre IFC4 e IFC 4.3", destaque: diferentes > 0));
            foreach (string e in Esquemas)
            {
                // Não exportar é uma escolha, não um erro: só conta a categoria usada que exporta sem classe.
                int semClasse = _pares.Count(p => p.NoModelo > 0 && p.De(e).Exportar && string.IsNullOrWhiteSpace(p.De(e).Classe));
                if (semClasse > 0) _selos.Children.Add(Theme.Chip($"{NomeEsquema(e)}: {semClasse} usadas sem classe", destaque: true));
                int naoExportadas = _pares.Count(p => p.NoModelo > 0 && !p.De(e).Exportar);
                if (naoExportadas > 0) _selos.Children.Add(Theme.Chip($"{NomeEsquema(e)}: {naoExportadas} usadas e não exportadas"));
                int erros = _pares.Count(p => p.Erro(e));
                if (erros > 0) _selos.Children.Add(Theme.Chip($"{NomeEsquema(e)}: {erros} com classe ou tipo fora do IFC schema", destaque: true));
            }

            List<(Par Par, string Esquema)> sugeridas = Sugeridas();
            _trocar.Visibility = sugeridas.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (sugeridas.Count > 0)
            {
                var pares = sugeridas.Select(x => x.Par.De(x.Esquema).Classe.Trim() + " → " + EsquemaIfc.Sugestao(x.Esquema, x.Par.De(x.Esquema).Classe))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                _trocar.Content = pares.Count == 1 ? $"Trocar {pares[0]} ({sugeridas.Count})" : $"Corrigir classes antigas ({sugeridas.Count})";
                _trocar.ToolTip = "Troca nomes que não existem no IFC schema pela classe certa:\n" + string.Join("\n", pares);
            }

            _resumo.Text = _alteradas.Count > 0 ? "Alterações não salvas em " + string.Join(" e ", _alteradas.Select(NomeEsquema))
                : "Na exportação vale a coluna do IFC schema do setup (IFC4, Reference View e Design Transfer View usam a do IFC4).";
            _resumo.Foreground = Theme.Solid(_alteradas.Count > 0 ? Theme.LaranjaTexto : Theme.Cinza);
        }

        private List<(Par, string)> Sugeridas() =>
            _pares.SelectMany(p => Esquemas.Where(e => EsquemaIfc.Sugestao(e, p.De(e).Classe) != null).Select(e => (p, e))).ToList();

        /// <summary>Troca as classes que têm um nome certo conhecido (IfcAssembly → IfcElementAssembly), mantendo o tipo se ele valer.</summary>
        private void TrocarSugeridas()
        {
            _grade.CommitEdit(DataGridEditingUnit.Row, true);
            List<(Par Par, string Esquema)> itens = Sugeridas();
            if (itens.Count == 0) return;
            string lista = string.Join("\n", itens.Take(12).Select(x => $"{NomeEsquema(x.Esquema)} · {Rotulo(x.Par)}: " +
                                                                         $"{x.Par.De(x.Esquema).Classe} → {EsquemaIfc.Sugestao(x.Esquema, x.Par.De(x.Esquema).Classe)}")) +
                           (itens.Count > 12 ? $"\n... e mais {itens.Count - 12}" : "");
            if (MessageBox.Show(this, "Trocar:\n\n" + lista, Title, MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                return;
            foreach ((Par p, string e) in itens)
            {
                CategoriasIfc.Linha l = p.De(e);
                l.Classe = EsquemaIfc.Sugestao(e, l.Classe);
                if (!EsquemaIfc.TipoValido(e, l.Classe, l.Tipo)) l.Tipo = "";
                _alteradas.Add(e);
            }
            Atualizar();
        }

        private static string Rotulo(Par p) => p.Categoria + (string.IsNullOrEmpty(p.Subcategoria) ? "" : " / " + p.Subcategoria);

        private bool Salvar()
        {
            _grade.CommitEdit(DataGridEditingUnit.Row, true);
            if (_alteradas.Count == 0) return true;
            var erradas = _alteradas.SelectMany(e => _pares.Where(p => p.Erro(e))
                .Select(p => $"{NomeEsquema(e)} · {Rotulo(p)}: " +
                             (p.De(e).ClasseInvalida ? p.De(e).Classe : p.De(e).Classe + " · " + p.De(e).Tipo))).ToList();
            if (erradas.Count > 0)
            {
                MessageBox.Show(this, "Estas linhas têm classe ou tipo que não existe no IFC schema (célula em vermelho). " +
                    "Escolha da lista antes de salvar (marque \"Só os erros\" para ver só essas linhas):\n\n" + string.Join("\n", erradas.Take(12)) +
                    (erradas.Count > 12 ? $"\n... e mais {erradas.Count - 12}" : ""),
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
                    _alteradas.Remove(e);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível salvar: " + ex.Message, Title);
                return false;
            }
            Atualizar(refresh: false);
            return true;
        }

        private void Copiar(string de, string para)
        {
            _grade.CommitEdit(DataGridEditingUnit.Row, true);
            List<Par> sel = _grade.SelectedItems.OfType<Par>().ToList();
            string alvo = sel.Count > 0 ? $"as {sel.Count} linhas selecionadas" : "a tabela inteira";
            if (MessageBox.Show(this, $"Copiar do {NomeEsquema(de)} para o {NomeEsquema(para)} {alvo}?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            foreach (Par p in sel.Count > 0 ? sel : _pares.ToList())
            {
                CategoriasIfc.Linha o = p.De(de), d = p.De(para);
                d.Classe = o.Classe;
                d.Tipo = o.Tipo;
                d.Exportar = o.Exportar;
            }
            _alteradas.Add(para);
            Atualizar();
        }

        private void Importar(string esquema)
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "Texto (*.txt)|*.txt", Title = $"Tabela de categorias para o {NomeEsquema(esquema)}" };
            if (d.ShowDialog(this) != true) return;
            List<CategoriasIfc.Linha> lidas;
            try { lidas = CategoriasIfc.Ler(d.FileName); }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível ler: " + ex.Message, Title); return; }
            if (lidas.Count == 0) { MessageBox.Show(this, "O arquivo não tem linhas Categoria / Subcategoria / Classe separadas por tab.", Title); return; }
            var porChave = lidas.GroupBy(l => l.Chave, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            int mudou = 0;
            foreach (CategoriasIfc.Linha l in _tabelas[esquema])
                if (porChave.TryGetValue(l.Chave, out CategoriasIfc.Linha n)) { l.Classe = n.Classe; l.Tipo = n.Tipo; l.Exportar = n.Exportar; porChave.Remove(l.Chave); mudou++; }
            foreach (CategoriasIfc.Linha n in porChave.Values) { n.Esquema = esquema; _tabelas[esquema].Add(n); mudou++; }
            _alteradas.Add(esquema);
            MontarPares();
            Atualizar();
            _resumo.Text = $"{mudou} linhas importadas para o {NomeEsquema(esquema)} de {Path.GetFileName(d.FileName)} (salve para valer).";
        }

        private void ExportarTxt(string esquema)
        {
            _grade.CommitEdit(DataGridEditingUnit.Row, true);
            var d = new Microsoft.Win32.SaveFileDialog { Filter = "Texto (*.txt)|*.txt", FileName = $"Categorias-{esquema}.txt", Title = $"Exportar tabela de categorias do {NomeEsquema(esquema)}" };
            if (d.ShowDialog(this) != true) return;
            try { CategoriasIfc.Gravar(d.FileName, _tabelas[esquema], esquema); _resumo.Text = "Exportado para " + d.FileName; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Title); }
        }

        /// <summary>
        /// Aplica a tabela recomendada (CategoriasRecomendadas, base Revit 2027) nas categorias e subcategorias que ela cobre:
        /// classe, tipo e Exportar. As outras linhas (anotações, por exemplo) ficam como estão. Salvar grava.
        /// </summary>
        private void Recomendado(IEnumerable<string> esquemas)
        {
            _grade.CommitEdit(DataGridEditingUnit.Row, true);
            List<string> lista = esquemas.ToList();
            string nomes = string.Join(" e ", lista.Select(NomeEsquema));
            if (MessageBox.Show(this, $"Aplicar o padrão MetroSP no {nomes}?\n\n" +
                    "Cada categoria de modelo (e subcategorias como Joist, Purlin, Bracing, Bolts) vai para a classe IFC própria dela, " +
                    "conforme o IFC4 (ISO 16739-1:2018) e o IFC 4.3 (ISO 16739-1:2024), sem proxy quando existe classe adequada " +
                    "(ex.: Structural Framing → IfcBeam, Structural Trusses → IfcElementAssembly TRUSS). Categorias de anotação ficam como estão.\n\n" +
                    "Nada é gravado até você clicar em Salvar.", Title, MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                return;
            int mudou = 0, novas = 0;
            foreach (string e in lista)
            {
                Dictionary<string, CategoriasRecomendadas.Item> rec = CategoriasRecomendadas.Para(e, _doc);
                var feitas = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
                foreach (CategoriasIfc.Linha l in _tabelas[e])
                {
                    string chave = l.Categoria + "|" + (l.Subcategoria ?? "");
                    if (!rec.TryGetValue(chave, out CategoriasRecomendadas.Item r)) continue;
                    feitas.Add(chave);
                    if (l.Exportar == r.Exportar && string.Equals(l.Classe ?? "", r.Classe, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(l.Tipo ?? "", r.Tipo, StringComparison.OrdinalIgnoreCase)) continue;
                    l.Classe = r.Classe;
                    l.Tipo = r.Tipo;
                    l.Exportar = r.Exportar;
                    mudou++;
                }
                // Categorias recomendadas que a tabela ainda não tem (ex.: Revit 2022 a 2024, sem o mapeamento do modelo).
                // Só com o modelo aberto, que confirma que a categoria existe nesta versão do Revit.
                if (_doc != null)
                    foreach (CategoriasRecomendadas.Item r in rec.Values.Where(r => !feitas.Contains(r.Chave)))
                    {
                        _tabelas[e].Add(new CategoriasIfc.Linha { Categoria = r.Categoria, Subcategoria = r.Subcategoria, Classe = r.Classe, Tipo = r.Tipo, Exportar = r.Exportar, Esquema = e });
                        novas++;
                    }
                _alteradas.Add(e);
            }
            if (novas > 0) MontarPares();
            Atualizar();
            _resumo.Text = $"Padrão MetroSP aplicado no {nomes}: {mudou} linha(s) mudaram" + (novas > 0 ? $", {novas} acrescentadas" : "") + ". Salve para valer.";
            _resumo.Foreground = Theme.Solid(Theme.LaranjaTexto);
        }

        private void VoltarAoPadrao(string esquema)
        {
            string seu = CategoriasIfc.Destino(esquema);
            if (!File.Exists(seu)) { MessageBox.Show(this, $"Você não tem uma tabela própria do {NomeEsquema(esquema)}.", Title); return; }
            if (MessageBox.Show(this, $"Apagar a sua tabela do {NomeEsquema(esquema)}? (ela fica guardada em Anteriores)", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try { ArquivosIfc.GuardarAnterior(seu); File.Delete(seu); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Title); return; }
            MessageBox.Show(this, "Pronto. Vale o padrão MetroSP; abra a janela de novo para vê-lo.", Title);
            _alteradas.Remove(esquema);
            Close();
        }
    }
}
