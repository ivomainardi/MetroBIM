using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MetroSP.Ui;

namespace MetroSP.Ids
{
    /// <summary>
    /// Mostra o .txt de property sets como tabela: cada propriedade, o tipo, o parâmetro do Revit que ela lê
    /// e se esse parâmetro existe no modelo. Dá para trocar o parâmetro (com sugestões do modelo), acrescentar
    /// e remover propriedades e salvar de volta no formato do exportador IFC.
    /// </summary>
    public class EditarPsetsWindow : Window
    {
        public class Linha : INotifyPropertyChanged
        {
            private string _pset, _aplicacao, _entidades, _propriedade, _tipo, _parametro;
            internal HashSet<string> Modelo;

            public string PropertySet { get => _pset; set => Set(ref _pset, value); }
            /// <summary>"Instância" ou "Tipo".</summary>
            public string Aplicacao { get => _aplicacao; set => Set(ref _aplicacao, value); }
            public string Entidades { get => _entidades; set => Set(ref _entidades, value); }
            public string Propriedade { get => _propriedade; set => Set(ref _propriedade, value); }
            public string TipoDado { get => _tipo; set => Set(ref _tipo, value); }
            public string Parametro { get => _parametro; set => Set(ref _parametro, value); }

            public bool Encontrado => !string.IsNullOrWhiteSpace(Parametro) && Modelo != null && Modelo.Contains(Parametro.Trim());
            public string Situacao => string.IsNullOrWhiteSpace(Parametro) ? "Sem parâmetro" : Encontrado ? "Encontrado" : "Não encontrado no modelo";

            public event PropertyChangedEventHandler PropertyChanged;
            private void Set(ref string campo, string valor, [CallerMemberName] string nome = null)
            {
                if (campo == valor) return;
                campo = valor;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nome));
                if (nome == nameof(Parametro))
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Encontrado)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Situacao)));
                }
            }
        }

        private const string Instancia = "Instância", Tipo = "Tipo";
        private static readonly string[] TiposDado =
        {
            "Text", "Label", "Identifier", "Boolean", "Logical", "Integer", "Real", "Length", "PositiveLength", "Area", "Volume",
            "Count", "Ratio", "PositiveRatio", "PlaneAngle", "Currency", "ThermalTransmittance", "MassDensity", "Pressure",
            "Force", "Frequency", "Illuminance", "LuminousFlux", "ElectricalCurrent", "ElectricalVoltage", "PowerMeasure",
            "VolumetricFlowRate", "MassFlowRate", "ThermodynamicTemperature",
        };

        private readonly ObservableCollection<Linha> _linhas = new ObservableCollection<Linha>();
        private readonly HashSet<string> _modelo;
        private readonly string _destino;
        private readonly ListCollectionView _vista;
        private readonly DataGrid _grade;
        private readonly WrapPanel _resumo;
        private readonly CheckBox _soAusentes;

        /// <summary>Arquivo gravado ao salvar (null se cancelou).</summary>
        public string ArquivoSalvo { get; private set; }
        /// <summary>true se o usuário pediu para voltar ao .txt instalado.</summary>
        public bool Restaurou { get; private set; }

        /// <param name="arquivo">.txt a abrir.</param>
        /// <param name="destino">Onde "Salvar" grava; null = pergunta onde salvar.</param>
        /// <param name="podeRestaurar">Mostra "Restaurar padrão" (cópia editada de uma classe instalada).</param>
        public EditarPsetsWindow(string arquivo, ISet<string> parametrosModelo, string destino, bool podeRestaurar)
        {
            _modelo = new HashSet<string>(parametrosModelo ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
            _destino = destino;

            Title = "MetroSP - Property sets";
            Width = 1040;
            Height = 640;
            MinWidth = 720;
            MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var mapa = new ExportMapping();
            mapa.LoadUserDefinedPropertySets(arquivo);
            foreach (PropertyMapping p in mapa.Properties)
                _linhas.Add(new Linha
                {
                    Modelo = _modelo,
                    PropertySet = p.PropertySet,
                    Aplicacao = p.OnType ? Tipo : Instancia,
                    Entidades = string.Join(",", p.Entities),
                    Propriedade = p.Property,
                    TipoDado = p.FileType,
                    Parametro = p.RevitParameter,
                });
            foreach (Linha l in _linhas) l.PropertyChanged += (s, e) => AtualizarResumo();
            _linhas.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null) foreach (Linha l in e.NewItems) l.PropertyChanged += (s2, e2) => AtualizarResumo();
                AtualizarResumo();
            };

            var corpo = new DockPanel();

            var topo = new StackPanel();
            DockPanel.SetDock(topo, Dock.Top);
            topo.Children.Add(new TextBlock { Text = arquivo, Foreground = Theme.Solid(Theme.Cinza), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = arquivo });
            _resumo = new WrapPanel { Margin = new Thickness(0, 8, 0, 6) };
            topo.Children.Add(_resumo);
            _soAusentes = new CheckBox { Content = "Mostrar só os parâmetros não encontrados no modelo", Margin = new Thickness(0, 0, 0, 6) };
            _soAusentes.Click += (s, e) => { _grade.CommitEdit(DataGridEditingUnit.Row, true); _vista.Refresh(); };
            topo.Children.Add(_soAusentes);
            if (mapa.Warnings.Count > 0)
                topo.Children.Add(new TextBlock
                {
                    Text = string.Join(" ", mapa.Warnings.Take(5)) + (mapa.Warnings.Count > 5 ? " ..." : ""),
                    Foreground = Theme.Solid(Theme.Laranja), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
                });
            corpo.Children.Add(topo);

            var acoes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(acoes, Dock.Bottom);
            var adicionar = new Button { Content = "Adicionar propriedade" };
            var remover = new Button { Content = "Remover selecionadas", Margin = new Thickness(8, 0, 0, 0) };
            acoes.Children.Add(adicionar);
            acoes.Children.Add(remover);
            acoes.Children.Add(new TextBlock
            {
                Text = "Clique duas vezes numa célula para editar. A coluna Parâmetro do Revit sugere os parâmetros do modelo.",
                Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
            corpo.Children.Add(acoes);

            _vista = new ListCollectionView(_linhas) { Filter = o => !(_soAusentes.IsChecked == true) || !((Linha)o).Encontrado };
            _grade = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserSortColumns = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                SelectionMode = DataGridSelectionMode.Extended,
                ItemsSource = _vista,
            };
            Theme.StyleSelection(_grade);
            // Com o filtro ligado, a linha corrigida sai da lista quando a edição termina (não durante: o
            // ListCollectionView não aceita Refresh com uma linha em edição).
            _grade.RowEditEnding += (s, e) =>
            {
                if (_soAusentes.IsChecked == true)
                    Dispatcher.BeginInvoke(new Action(() => { try { _vista.Refresh(); } catch (InvalidOperationException) { } }),
                        System.Windows.Threading.DispatcherPriority.Background);
            };
            var estiloLinha = new Style(typeof(DataGridRow));
            var problema = new DataTrigger { Binding = new Binding(nameof(Linha.Encontrado)), Value = false };
            problema.Setters.Add(new Setter(BackgroundProperty, Theme.LinhaProblema));
            estiloLinha.Triggers.Add(problema);
            _grade.RowStyle = estiloLinha;

            _grade.Columns.Add(Texto("Property set", nameof(Linha.PropertySet), 150));
            _grade.Columns.Add(Lista("Aplica em", nameof(Linha.Aplicacao), new[] { Instancia, Tipo }, 80));
            _grade.Columns.Add(Texto("Entidades IFC", nameof(Linha.Entidades), 150));
            _grade.Columns.Add(Texto("Propriedade", nameof(Linha.Propriedade), 150));
            _grade.Columns.Add(Lista("Tipo", nameof(Linha.TipoDado), TiposDado, 90));
            _grade.Columns.Add(Parametro(_modelo.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList()));
            _grade.Columns.Add(new DataGridTextColumn { Header = "Situação", Binding = new Binding(nameof(Linha.Situacao)) { Mode = BindingMode.OneWay }, IsReadOnly = true, Width = 150 });
            corpo.Children.Add(_grade);

            adicionar.Click += (s, e) =>
            {
                var modelo = _grade.SelectedItem as Linha ?? _linhas.LastOrDefault();
                var nova = new Linha
                {
                    Modelo = _modelo,
                    PropertySet = modelo?.PropertySet ?? "Pset_MetroSP",
                    Aplicacao = modelo?.Aplicacao ?? Instancia,
                    Entidades = modelo?.Entidades ?? "",
                    Propriedade = "NovaPropriedade",
                    TipoDado = "Text",
                    Parametro = "",
                };
                int i = modelo != null ? _linhas.IndexOf(modelo) + 1 : _linhas.Count;
                _linhas.Insert(i, nova);
                _grade.SelectedItem = nova;
                _grade.ScrollIntoView(nova);
            };
            remover.Click += (s, e) =>
            {
                foreach (Linha l in _grade.SelectedItems.Cast<Linha>().ToList()) _linhas.Remove(l);
            };

            var salvar = new Button { Content = "Salvar", IsDefault = true, Style = Theme.PrimaryButton };
            salvar.Click += (s, e) => Salvar(_destino ?? PerguntarDestino(arquivo));
            var salvarComo = new Button { Content = "Salvar como..." };
            salvarComo.Click += (s, e) => Salvar(PerguntarDestino(arquivo));
            var cancelar = new Button { Content = "Cancelar", IsCancel = true };
            var botoes = new List<Button>();
            if (podeRestaurar)
            {
                var restaurar = new Button { Content = "Restaurar padrão" };
                restaurar.Click += (s, e) =>
                {
                    if (MessageBox.Show(this, "Descartar as edições desta classe e voltar ao .txt instalado?", Title,
                            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    Restaurou = true;
                    DialogResult = true;
                };
                botoes.Add(restaurar);
            }
            botoes.AddRange(new[] { cancelar, salvarComo, salvar });

            AtualizarResumo();
            Theme.Apply(this, "Property sets definidos pelo usuário",
                "Confira de qual parâmetro do Revit sai cada propriedade do IFC", corpo, Theme.Buttons(botoes.ToArray()));
        }

        private void AtualizarResumo()
        {
            int ausentes = _linhas.Count(l => !l.Encontrado);
            int psets = _linhas.Select(l => l.PropertySet).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            _resumo.Children.Clear();
            _resumo.Children.Add(Theme.Chip($"{_linhas.Count} propriedades"));
            _resumo.Children.Add(Theme.Chip($"{psets} property sets"));
            _resumo.Children.Add(Theme.Chip(ausentes == 0 ? "todos os parâmetros encontrados no modelo"
                : $"{ausentes} parâmetro(s) não encontrados no modelo (ficam fora do IFC)", destaque: ausentes > 0));
        }

        private string PerguntarDestino(string atual)
        {
            var d = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Texto (*.txt)|*.txt",
                Title = "Salvar property sets",
                FileName = Path.GetFileName(atual),
            };
            string pasta = Path.GetDirectoryName(atual);
            if (Directory.Exists(pasta)) d.InitialDirectory = pasta;
            return d.ShowDialog(this) == true ? d.FileName : null;
        }

        private void Salvar(string destino)
        {
            if (destino == null) return;
            _grade.CommitEdit(DataGridEditingUnit.Row, true);
            Linha invalida = _linhas.FirstOrDefault(l => string.IsNullOrWhiteSpace(l.PropertySet) || string.IsNullOrWhiteSpace(l.Propriedade));
            if (invalida != null)
            {
                MessageBox.Show(this, "Toda propriedade precisa de property set e nome.", Title);
                _grade.SelectedItem = invalida;
                _grade.ScrollIntoView(invalida);
                return;
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destino));
                File.WriteAllText(destino, Gerar(_linhas), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível salvar:\n" + ex.Message, Title);
                return;
            }
            ArquivoSalvo = destino;
            DialogResult = true;
        }

        /// <summary>Texto no formato do exportador: um cabeçalho PropertySet por grupo seguido das propriedades.</summary>
        internal static string Gerar(IEnumerable<Linha> linhas)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Property sets definidos pelo usuário - salvo pelo MetroSP em " + DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
            string atual = null;
            foreach (Linha l in linhas)
            {
                string it = l.Aplicacao == Tipo ? "T" : "I";
                string entidades = string.Join(",", (l.Entidades ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(e => e.Trim()));
                string cabecalho = $"PropertySet:\t{l.PropertySet.Trim()}\t{it}\t{entidades}".TrimEnd('\t');
                if (cabecalho != atual)
                {
                    sb.AppendLine(cabecalho);
                    atual = cabecalho;
                }
                string tipo = string.IsNullOrWhiteSpace(l.TipoDado) ? "Text" : l.TipoDado.Trim();
                string parametro = string.IsNullOrWhiteSpace(l.Parametro) ? l.Propriedade.Trim() : l.Parametro.Trim();
                sb.AppendLine($"\t{l.Propriedade.Trim()}\t{tipo}\t{parametro}");
            }
            return sb.ToString();
        }

        private static DataGridTextColumn Texto(string titulo, string campo, double largura) =>
            new DataGridTextColumn { Header = titulo, Binding = new Binding(campo) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = largura };

        private static DataGridComboBoxColumn Lista(string titulo, string campo, IEnumerable<string> valores, double largura) =>
            new DataGridComboBoxColumn { Header = titulo, ItemsSource = valores.ToList(), SelectedItemBinding = new Binding(campo), Width = largura };

        /// <summary>Coluna do parâmetro: caixa editável com os parâmetros do modelo como sugestão.</summary>
        private static DataGridTemplateColumn Parametro(List<string> sugestoes)
        {
            var leitura = new FrameworkElementFactory(typeof(TextBlock));
            leitura.SetBinding(TextBlock.TextProperty, new Binding(nameof(Linha.Parametro)));
            leitura.SetValue(TextBlock.PaddingProperty, new Thickness(2, 0, 2, 0));

            var edicao = new FrameworkElementFactory(typeof(ComboBox));
            edicao.SetValue(ComboBox.IsEditableProperty, true);
            edicao.SetValue(ComboBox.IsTextSearchEnabledProperty, true);
            edicao.SetValue(ItemsControl.ItemsSourceProperty, sugestoes);
            edicao.SetBinding(ComboBox.TextProperty, new Binding(nameof(Linha.Parametro)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });

            return new DataGridTemplateColumn
            {
                Header = "Parâmetro do Revit",
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                MinWidth = 160,
                CellTemplate = new DataTemplate { VisualTree = leitura },
                CellEditingTemplate = new DataTemplate { VisualTree = edicao },
            };
        }
    }
}
