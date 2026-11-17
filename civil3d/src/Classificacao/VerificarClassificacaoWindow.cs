using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using MetroSP.Ui;
using AcDocument = Autodesk.AutoCAD.ApplicationServices.Document;

namespace MetroSP.Classificacao
{
    /// <summary>
    /// Passo 5 (mesmo desenho do Revit): perfil de classificação no alto, uma aba por classificação e, nela, uma tabela por
    /// valor escrito (Está escrito, Na lista, O que muda, Objetos, Ação), com o detalhe da linha ao lado. Mostra também o
    /// caminho até o IFC: a classificação vai num property set, o exportador leva o Pset (se estiver na Saída no IFC) e o
    /// MetroSP cria IfcClassification, IfcClassificationReference e IfcRelAssociatesClassification depois de exportar.
    /// Só objetos que vão para o IFC.
    /// </summary>
    public class VerificarClassificacaoWindow : Window
    {
        private readonly AcDocument _doc;
        private readonly Database _db;
        private readonly List<ObjectId> _selecao;
        private readonly ComboBox _padraoCombo;
        private readonly TextBlock _padraoInfo;
        private readonly StackPanel _abasSistemas;
        private readonly StackPanel _caminhoIfc;
        private readonly CheckBox _mostrarCertos;
        private readonly DockPanel _areaTabela;
        private readonly StackPanel _detalhe;
        private readonly TextBlock _rodapeInfo;
        private readonly Button _aplicar, _exportar;
        private PadraoClassificacao _padrao;
        private bool _trocandoPadrao;
        private Button _escolherPlanilha;
        private List<LinhaVerificacao> _linhas;
        private SistemaClassificacao _sistema;
        private FilterableGrid _tabela;
        private readonly List<LinhaValor> _valores = new List<LinhaValor>();

        internal static string _ultimoPadrao;

        public VerificarClassificacaoWindow(AcDocument doc, List<ObjectId> selecao)
        {
            _doc = doc;
            _db = doc.Database;
            _selecao = selecao != null && selecao.Count > 0 ? selecao : null;

            Title = "MetroSP - Classificação";
            Width = 1200;
            Height = 740;
            MinWidth = 820;
            MinHeight = 480;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // ---- perfil
            var linhaPadrao = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var rotPadrao = Theme.Rotulo("Perfil");
            DockPanel.SetDock(rotPadrao, Dock.Left);
            linhaPadrao.Children.Add(rotPadrao);
            _padraoCombo = new ComboBox { MinWidth = 220, Margin = new Thickness(0, 0, 8, 0), ToolTip = "Perfil de classificação: as classificações a conferir, com a planilha de cada uma." };
            Theme.Digitavel(_padraoCombo);
            _padraoCombo.SelectionChanged += (s, e) => { if (!_trocandoPadrao && _padraoCombo.SelectedItem is PadraoClassificacao p) UsarPadrao(p); };
            DockPanel.SetDock(_padraoCombo, Dock.Left);
            linhaPadrao.Children.Add(_padraoCombo);
            var editar = new Button { Content = "Classificação...", ToolTip = "Criar ou mudar perfis de classificação: classificações, property sets e as colunas da planilha." };
            editar.Click += (s, e) => EditarPadroes();
            DockPanel.SetDock(editar, Dock.Left);
            linhaPadrao.Children.Add(editar);
            _escolherPlanilha = new Button
            {
                Content = "Escolher planilha...",
                Style = Theme.PrimaryButton,
                Margin = new Thickness(8, 0, 0, 0),
                Visibility = System.Windows.Visibility.Collapsed,
                ToolTip = "Abre a planilha (.xlsx ou .csv) com a lista de classificação. As colunas são sugeridas pelos títulos.",
            };
            _escolherPlanilha.Click += (s, e) => EscolherPlanilha();
            DockPanel.SetDock(_escolherPlanilha, Dock.Left);
            linhaPadrao.Children.Add(_escolherPlanilha);
            _padraoInfo = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            linhaPadrao.Children.Add(_padraoInfo);

            // ---- abas (uma por classificação) e opções
            var linhaAbas = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            _mostrarCertos = new CheckBox { Content = "Mostrar os certos", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            _mostrarCertos.Click += (s, e) => MostrarSistema();
            DockPanel.SetDock(_mostrarCertos, Dock.Right);
            linhaAbas.Children.Add(_mostrarCertos);
            var selTodos = new Button { Content = "Selecionar no modelo", Padding = new Thickness(10, 2, 10, 2), ToolTip = "Seleciona no Civil 3D os objetos das linhas visíveis (ou das escolhidas)." };
            selTodos.Click += (s, e) => Selecionar((_tabela?.Grid.SelectedItems.Count > 0 ? _tabela.Grid.SelectedItems.OfType<LinhaValor>() : _tabela?.VisibleItems<LinhaValor>() ?? Enumerable.Empty<LinhaValor>()).SelectMany(v => v.Linhas.Select(l => l.Id)));
            DockPanel.SetDock(selTodos, Dock.Right);
            linhaAbas.Children.Add(selTodos);
            _abasSistemas = new StackPanel { Orientation = Orientation.Horizontal };
            linhaAbas.Children.Add(_abasSistemas);

            // ---- caminho até o IFC
            _caminhoIfc = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

            // ---- tabela e detalhe
            _detalhe = new StackPanel { Width = 250, Margin = new Thickness(12, 0, 0, 0) };
            DockPanel.SetDock(_detalhe, Dock.Right);
            _areaTabela = new DockPanel();
            _areaTabela.Children.Add(new TextBlock { Text = "Lendo o modelo...", Foreground = Theme.Solid(Theme.Cinza) });
            var meio = new DockPanel();
            meio.Children.Add(_detalhe);
            meio.Children.Add(_areaTabela);

            var corpo = new DockPanel();
            foreach (UIElement el in new UIElement[] { linhaPadrao, linhaAbas, _caminhoIfc })
            {
                DockPanel.SetDock(el, Dock.Top);
                corpo.Children.Add(el);
            }
            corpo.Children.Add(meio);

            _rodapeInfo = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            var reanalisar = new Button { Content = "Ler de novo", ToolTip = "Lê o modelo e as planilhas de novo." };
            _exportar = new Button { Content = "Exportar (.csv)", IsEnabled = false };
            _aplicar = new Button { Content = "Corrigir escrita", Style = Theme.PrimaryButton, IsEnabled = false,
                ToolTip = "Grava as correções seguras da aba (mesmo código, texto como na lista)." };
            var fechar = new Button { Content = "Fechar", IsCancel = true };
            reanalisar.Click += (s, e) => Verificar();
            _exportar.Click += (s, e) => Exportar();
            _aplicar.Click += (s, e) => CorrigirEscrita();
            fechar.Click += (s, e) => Close();
            var rodape = new DockPanel();
            StackPanel botoes = Theme.Buttons(reanalisar, _exportar, _aplicar, fechar);
            DockPanel.SetDock(botoes, Dock.Right);
            rodape.Children.Add(botoes);
            rodape.Children.Add(_rodapeInfo);

            Theme.Apply(this, "Classificação", Path.GetFileName(doc.Name) + " · confere as classificações com as planilhas do perfil", corpo, rodape);
            Loaded += (s, e) => CarregarPadroes(_ultimoPadrao);
        }

        // ------------------------------------------------------------------ perfis

        private void CarregarPadroes(string escolher)
        {
            List<PadraoClassificacao> padroes = PadraoClassificacao.Listar();
            if (padroes.Count == 0) padroes.Add(PadraoClassificacao.Inicial());
            _trocandoPadrao = true;
            _padraoCombo.ItemsSource = padroes;
            _padraoCombo.SelectedItem = padroes.FirstOrDefault(p => p.Nome == escolher) ?? padroes[0];
            _trocandoPadrao = false;
            UsarPadrao(_padraoCombo.SelectedItem as PadraoClassificacao);
        }

        private void UsarPadrao(PadraoClassificacao p)
        {
            _padrao = p;
            if (p == null) { _padraoInfo.Text = ""; return; }
            _ultimoPadrao = p.Nome;
            var cache = new Dictionary<string, Planilha.Tabela>(StringComparer.OrdinalIgnoreCase);
            foreach (SistemaClassificacao s in p.Sistemas) s.Carregar(cache);
            _padraoInfo.Text = string.Join("  ·  ", p.Sistemas.Select(s => s.Lista != null && s.Erro == null
                ? $"{s.Nome}: {s.Lista.Itens.Count} itens" : $"{s.Nome}: {s.Erro}"));
            _padraoInfo.ToolTip = string.Join("\n", p.Sistemas.Select(s =>
                $"{s.Nome}  ·  {s.Propriedades}  ·  {s.Planilha?.NomeArquivo}" + (string.IsNullOrEmpty(s.Planilha?.Aba) ? "" : $" › {s.Planilha.Aba}") +
                (s.Lista?.Avisos.Count > 0 ? "\n   " + string.Join("\n   ", s.Lista.Avisos.Take(10)) : "")));
            _padraoInfo.Foreground = Theme.Solid(p.Sistemas.Any(s => s.Lista == null || s.Erro != null) ? Theme.LaranjaTexto : Theme.Cinza);
            _escolherPlanilha.Visibility = p.Sistemas.Any(s => s.Lista == null) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            if (_sistema == null || !p.Sistemas.Contains(_sistema)) _sistema = p.Sistemas.FirstOrDefault(s => s.Lista != null) ?? p.Sistemas.FirstOrDefault();
            Verificar();
        }

        /// <summary>Escolhe a planilha da primeira classificação sem lista, sugere as colunas e salva o padrão.</summary>
        private void EscolherPlanilha()
        {
            SistemaClassificacao s = _padrao?.Sistemas.FirstOrDefault(x => x.Lista == null);
            if (s == null) return;
            var d = new Microsoft.Win32.OpenFileDialog { Title = $"Planilha da classificação {s.Nome}", Filter = Planilha.LeitorPlanilha.Filtro };
            if (d.ShowDialog(this) != true) return;
            try
            {
                Planilha.Tabela t = Planilha.LeitorPlanilha.Ler(d.FileName);
                var campos = SistemaClassificacao.Campos;
                string aba = t.NomesAbas.FirstOrDefault(a => t.Linhas(a).Count > 1) ?? t.NomesAbas[0];
                var mapa = new Planilha.MapaColunas { Arquivo = d.FileName, Aba = aba, LinhaCabecalho = Planilha.MapaColunas.AcharCabecalho(t, aba, campos) };
                mapa.Sugerir(t, campos);
                s.Planilha = mapa;
                _padrao.Salvar();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível ler a planilha:\n" + ex.Message, Title);
                return;
            }
            UsarPadrao(_padrao);
            if (s.Lista == null)
            {
                MessageBox.Show(this, $"Não achei sozinho as colunas da planilha ({s.Erro}). Indique em qual coluna está cada dado.", Title);
                EditarPadroes();
            }
        }

        private void EditarPadroes()
        {
            var w = new PadroesClassificacaoWindow(_db, _padrao?.Nome) { Owner = this };
            w.ShowDialog();
            CarregarPadroes(w.Escolhido ?? _padrao?.Nome);
        }

        // ------------------------------------------------------------------ verificar

        private void Verificar()
        {
            var sistemas = _padrao?.Sistemas.Where(x => x.Lista != null && x.Lista.Itens.Count > 0).ToList() ?? new List<SistemaClassificacao>();
            _linhas = new List<LinhaVerificacao>();
            if (sistemas.Count > 0)
            {
                Cursor = Cursors.Wait;
                try
                {
                    using (_doc.LockDocument())
                    using (Transaction tr = _db.TransactionManager.StartTransaction())
                    {
                        List<ObjectId> ids = Ifc.FiltroIfc.Objetos(_db, tr, _selecao);
                        foreach (SistemaClassificacao s in sistemas) _linhas.AddRange(ClassificacaoCore.Verificar(_db, tr, ids, s));
                        tr.Commit();
                    }
                }
                catch (Exception ex) { MessageBox.Show(this, "Não foi possível verificar:\n" + ex.Message, Title); }
                finally { Cursor = null; }
            }
            MostrarAbas();
            MostrarSistema();
        }

        private void MostrarAbas()
        {
            _abasSistemas.Children.Clear();
            foreach (SistemaClassificacao s in _padrao?.Sistemas ?? new List<SistemaClassificacao>())
            {
                int erros = _linhas.Count(l => l.SistemaRef == s && l.Situacao != ClassificacaoCore.OK);
                bool on = s == _sistema;
                var b = new Button
                {
                    Content = s.Nome + (erros > 0 ? $"  ({erros:N0})" : ""),
                    Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 0, -1, 0),
                    Background = on ? Theme.Solid(Theme.Preto) : System.Windows.Media.Brushes.White,
                    Foreground = on ? System.Windows.Media.Brushes.White : Theme.Solid(Theme.Preto),
                    FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal,
                    ToolTip = s.Lista == null ? s.Nome + ": " + s.Erro : $"{s.Propriedades} · {s.Lista.Itens.Count} itens",
                };
                var sis = s;
                b.Click += (o, e) => { _sistema = sis; MostrarAbas(); MostrarSistema(); };
                _abasSistemas.Children.Add(b);
            }
        }

        /// <summary>Como a classificação desta aba chega ao IFC, e se o Pset dela está na Saída no IFC.</summary>
        private void MostrarCaminhoIfc()
        {
            _caminhoIfc.Children.Clear();
            SistemaClassificacao s = _sistema;
            if (s == null) return;
            string formato = s.Formato == FormatoClassificacao.CodigoDescricao ? "Código:Descrição" : "código" + (s.Descricao != null ? " + " + s.Descricao : "");
            _caminhoIfc.Children.Add(new TextBlock
            {
                Text = $"No IFC: IfcClassification \"{s.Nome}\"" + (string.IsNullOrWhiteSpace(s.Edicao) ? "" : $" ({s.Edicao})") +
                       $"  ←  {s.PropertySet} › {s.PropriedadeCodigo} ({formato})",
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0),
                ToolTip = "A classificação fica num property set. O exportador da Autodesk leva o Pset para o IFC (se estiver na Saída no IFC, passo 3) e " +
                          "o MetroSP cria, depois de exportar, IfcClassification, IfcClassificationReference (código e descrição) e IfcRelAssociatesClassification.",
            });
            var faltam = PropsQueFaltamNoIfc(s);
            if (faltam.Count == 0)
            {
                _caminhoIfc.Children.Add(new TextBlock { Text = "✓ sai no IFC", Foreground = Theme.Solid(Theme.Verde), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                return;
            }
            _caminhoIfc.Children.Add(new TextBlock
            {
                Text = "Não vai para o IFC: " + string.Join(", ", faltam) + " não está(ão) na Saída no IFC.",
                Foreground = Theme.Solid(Theme.LaranjaTexto), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
            });
            var mapear = new Button { Content = "Mapear", Style = Theme.PrimaryButton, Padding = new Thickness(10, 1, 10, 1),
                ToolTip = $"Acrescenta {s.PropertySet} ao perfil de exportação, com o mesmo nome no IFC (o MetroSP procura esse nome depois de exportar)." };
            mapear.Click += (o, e) =>
            {
                string msg;
                try
                {
                    msg = PropertySets.PsetsCore.LevarParaIfc(new[] { new PropertySets.PsetMontagem
                    {
                        Nome = s.PropertySet,
                        Propriedades = new[] { s.PropriedadeCodigo, s.Descricao }.Where(x => !string.IsNullOrWhiteSpace(x))
                            .Select(x => new PropertySets.PropriedadeMontagem { Nome = x }).ToList(),
                    } });
                }
                catch (Exception ex) { msg = "Não foi possível mapear: " + ex.Message; }
                _rodapeInfo.Text = msg ?? "";
                MostrarCaminhoIfc();
            };
            _caminhoIfc.Children.Add(mapear);
        }

        /// <summary>Propriedades da classificação que não saem no IFC com o mesmo Pset e nome (o pós-processamento procura esse nome).</summary>
        internal static List<string> PropsQueFaltamNoIfc(SistemaClassificacao s)
        {
            var saida = PropertySets.SaidaIfcPainel.LerMapeamento();
            bool Sai(string prop) => saida.Any(r => r.Ativa && string.Equals(r.Pset, s.PropertySet, StringComparison.OrdinalIgnoreCase)
                                                  && string.Equals(r.Prop, prop, StringComparison.OrdinalIgnoreCase));
            return new[] { s.PropriedadeCodigo, s.Descricao }.Where(p => !string.IsNullOrWhiteSpace(p) && !Sai(p))
                .Select(p => s.PropertySet + " › " + p).ToList();
        }

        private void MostrarSistema()
        {
            MostrarCaminhoIfc();
            _valores.Clear();
            var estado = _tabela?.Estado();
            _areaTabela.Children.Clear();
            if (_sistema?.Lista == null)
            {
                _areaTabela.Children.Add(new TextBlock { Text = _sistema == null ? "O perfil não tem classificações." : $"{_sistema.Nome}: {_sistema.Erro}. Escolha a planilha.", Foreground = Theme.Solid(Theme.LaranjaTexto) });
                MostrarDetalhe(null);
                _aplicar.IsEnabled = _exportar.IsEnabled = false;
                return;
            }
            var doSistema = _linhas.Where(l => l.SistemaRef == _sistema).ToList();
            foreach (var g in doSistema.GroupBy(l => (l.Situacao, Escrito: l.FaltaPset ? "\u0001" : (l.Preenchido ?? "").Trim())))
                _valores.Add(new LinhaValor(g.ToList()));
            var ordem = ClassificacaoCore.Ordem.ToList();
            var lista = _valores.OrderBy(v => v.Situacao == ClassificacaoCore.OK ? 1 : 0).ThenByDescending(v => v.Objetos).ToList();

            _tabela = new FilterableGrid(lista, typeof(LinhaValor));
            _tabela.AddColumn("Está escrito", nameof(LinhaValor.EstaEscrito), 260);
            _tabela.AddColumn("Na lista", nameof(LinhaValor.NaLista), 260, fill: true);
            _tabela.AddColumn("O que muda", nameof(LinhaValor.OQueMuda), 150);
            var obj = _tabela.AddColumn("Objetos", nameof(LinhaValor.ObjetosTexto), 70);
            obj.SortMemberPath = nameof(LinhaValor.Objetos);
            obj.ElementStyle = new Style(typeof(TextBlock), obj.ElementStyle) { Setters = { new Setter(TextBlock.TextAlignmentProperty, System.Windows.TextAlignment.Right) } };
            _tabela.Grid.Columns.Add(ColunaAcao());
            bool certos = _mostrarCertos.IsChecked == true;
            _tabela.ExtraFilter = o => certos || ((LinhaValor)o).Situacao != ClassificacaoCore.OK;
            var estilo = new Style(typeof(DataGridRow), _tabela.Grid.RowStyle);
            var pend = new DataTrigger { Binding = new Binding(nameof(LinhaValor.Problema)), Value = true };
            pend.Setters.Add(new Setter(Control.BackgroundProperty, Theme.LinhaProblema));
            estilo.Triggers.Add(pend);
            _tabela.Grid.RowStyle = estilo;
            _tabela.Grid.SelectionChanged += (s, e) => MostrarDetalhe(_tabela.Grid.SelectedItem as LinhaValor);
            _tabela.Filtered += AtualizarRodape;
            Theme.StyleSelection(_tabela.Grid);
            _tabela.Refresh();
            _tabela.Restaurar(estado);
            int ok = doSistema.Count(l => l.Situacao == ClassificacaoCore.OK);
            _mostrarCertos.Content = $"Mostrar os certos ({ok:N0} objetos)";
            _areaTabela.Children.Add(_tabela);
            _exportar.IsEnabled = true;
            MostrarDetalhe(null);
            AtualizarRodape();
        }

        private void AtualizarRodape()
        {
            if (_tabela == null || _sistema == null) return;
            var seguras = SeguraDoSistema();
            int erros = _valores.Where(v => v.Situacao != ClassificacaoCore.OK).Sum(v => v.Objetos);
            _rodapeInfo.Text = erros == 0 ? $"{_sistema.Nome}: tudo certo." : $"{_sistema.Nome}: {erros:N0} objeto(s) para corrigir em {_valores.Count(v => v.Situacao != ClassificacaoCore.OK)} valor(es).";
            _aplicar.IsEnabled = seguras.Count > 0;
            _aplicar.Content = seguras.Count > 0 ? $"Corrigir escrita ({seguras.Count:N0})" : "Corrigir escrita";
        }

        private List<LinhaVerificacao> SeguraDoSistema() =>
            _linhas.Where(l => l.SistemaRef == _sistema && l.PropostaSegura && l.Gravacoes.Count > 0 && string.IsNullOrEmpty(l.Bloqueio)).ToList();

        private void CorrigirEscrita()
        {
            var p = SeguraDoSistema();
            if (p.Count == 0) return;
            string msg = $"Corrigir a escrita em {p.Count:N0} objeto(s)?\n\nExemplos:\n" +
                         string.Join("\n", p.GroupBy(l => l.Preenchido).Take(5).Select(g => $"• {g.Key}  →  {string.Join(" · ", g.First().Gravacoes.Select(x => x.Valor))}")) +
                         "\n\nPode ser desfeito com U (desfazer) no Civil 3D.";
            if (MessageBox.Show(this, msg, Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            Gravar(p.Select(l => (l.SistemaRef, l.Id, (IEnumerable<(string, string)>)l.Gravacoes)));
        }

        // ------------------------------------------------------------------ detalhe

        private void MostrarDetalhe(LinhaValor v)
        {
            _detalhe.Children.Clear();
            if (v == null)
            {
                _detalhe.Children.Add(Theme.Detalhe("Escolha uma linha para ver o que muda e em quais layers está."));
                return;
            }
            _detalhe.Children.Add(new TextBlock { Text = $"{v.Objetos:N0} OBJETO(S)", Foreground = Theme.Solid(Theme.LaranjaTexto), FontWeight = FontWeights.Bold, FontSize = 11 });
            void Campo(string rotulo, string texto)
            {
                if (string.IsNullOrEmpty(texto)) return;
                _detalhe.Children.Add(new TextBlock { Text = rotulo, Foreground = Theme.Solid(Theme.Cinza), Margin = new Thickness(0, 8, 0, 0) });
                _detalhe.Children.Add(new TextBox { Text = texto, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    Background = Theme.Solid(Theme.CinzaFundo), BorderThickness = new Thickness(0) });
            }
            Campo("Está escrito", v.EstaEscrito);
            Campo("Vai ficar", v.VaiFicar);
            if (!string.IsNullOrEmpty(v.Proposta)) _detalhe.Children.Add(new TextBlock { Text = v.Proposta, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
            _detalhe.Children.Add(new TextBlock { Text = "Layers", Foreground = Theme.Solid(Theme.Cinza), Margin = new Thickness(0, 8, 0, 0) });
            var layers = v.Linhas.GroupBy(l => l.Layer ?? "").OrderByDescending(g => g.Count()).ToList();
            foreach (var g in layers.Take(3)) _detalhe.Children.Add(new TextBlock { Text = $"{g.Count():N0}  {g.Key}", TextTrimming = TextTrimming.CharacterEllipsis });
            if (layers.Count > 3) _detalhe.Children.Add(new TextBlock { Text = $"{layers.Skip(3).Sum(g => g.Count()):N0} em mais {layers.Count - 3} layer(s)", Foreground = Theme.Solid(Theme.Cinza) });
            var sel = new Button { Content = "Selecionar no modelo", Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            sel.Click += (s, e) => Selecionar(v.Linhas.Select(l => l.Id));
            _detalhe.Children.Add(sel);
            if (v.Linhas.Count == 1)
            {
                var zoom = new Button { Content = "Aproximar", Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
                zoom.Click += (s, e) => Zoom(v.Linhas[0]);
                _detalhe.Children.Add(zoom);
            }
        }

        /// <summary>Grava (por property set), mostra o resumo e confere de novo.</summary>
        private void Gravar(IEnumerable<(SistemaClassificacao Sistema, ObjectId Id, IEnumerable<(string Prop, string Valor)> Valores)> itens)
        {
            var resumo = new List<string>();
            try
            {
                using (_doc.LockDocument())
                using (Transaction tr = _db.TransactionManager.StartTransaction())
                {
                    foreach (var g in itens.GroupBy(i => i.Sistema.PropertySet, StringComparer.OrdinalIgnoreCase))
                        resumo.Add((g.Count() < itens.Count() ? g.Key + ": " : "") +
                                   ClassificacaoCore.Gravar(_db, tr, g.Key, g.SelectMany(i => i.Valores.Select(v => (i.Id, v.Prop, v.Valor)))));
                    tr.Commit();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível gravar. Nada foi alterado.\n\n" + ex.Message, Title);
                return;
            }
            MessageBox.Show(this, string.Join("\n\n", resumo), Title);
            Verificar();
        }

        // ------------------------------------------------------------------ ação por valor

        private DataGridTemplateColumn ColunaAcao()
        {
            var botao = new FrameworkElementFactory(typeof(Button));
            botao.SetBinding(ContentControl.ContentProperty, new Binding(nameof(LinhaValor.Acao)));
            botao.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(LinhaValor.Proposta)));
            botao.SetValue(Control.PaddingProperty, new Thickness(8, 0, 8, 0));
            botao.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 1, 2, 1));
            var estilo = new Style(typeof(Button), TryFindResource(typeof(Button)) as Style);
            var semAcao = new DataTrigger { Binding = new Binding(nameof(LinhaValor.Acao)), Value = null };
            semAcao.Setters.Add(new Setter(UIElement.VisibilityProperty, System.Windows.Visibility.Collapsed));
            estilo.Triggers.Add(semAcao);
            botao.SetValue(FrameworkElement.StyleProperty, estilo);
            botao.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((s, e) =>
            {
                if ((s as FrameworkElement)?.DataContext is LinhaValor v) ExecutarAcao(v);
            }));
            return new DataGridTemplateColumn { Header = "Ação", Width = 130, CellTemplate = new DataTemplate { VisualTree = botao } };
        }

        private void ExecutarAcao(LinhaValor v)
        {
            try
            {
                LinhaVerificacao l = v.Linhas[0];
                switch (l.Acao)
                {
                    case "Corrigir":
                        Gravar(v.Linhas.Where(x => x.Gravacoes.Count > 0 && string.IsNullOrEmpty(x.Bloqueio)).Select(x => (x.SistemaRef, x.Id, (IEnumerable<(string, string)>)x.Gravacoes)));
                        break;
                    case "Escolher...": Escolher(l, v.Linhas); break;
                    case "Anexar property set": Anexar(v.Linhas); break;
                    default: Selecionar(v.Linhas.Select(x => x.Id)); break;
                }
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Title); }
        }

        private void Escolher(LinhaVerificacao l, List<LinhaVerificacao> linhas)
        {
            SistemaClassificacao s = l.SistemaRef;
            var w = new EscolherItemWindow(s.Lista, s.Nome, l.Tipo, l.Layer, linhas.Count > 1 ? $"{linhas.Count} objetos" : l.Objeto, linhas.Count > 1 ? null : l.Preenchido) { Owner = this };
            if (w.ShowDialog() != true || w.Escolhido == null) return;
            ClassItem item = w.Escolhido;
            var valores = new List<(string, string)>();
            if (s.Formato == FormatoClassificacao.CodigoDescricao)
                valores.Add((s.PropriedadeCodigo, ValorClassificacao.Escrever(item.Codigo, item.Descricao, s.GravarNome ? s.Nome : null)));
            else
            {
                valores.Add((s.PropriedadeCodigo, item.Codigo));
                if (s.Descricao != null && item.Descricao.Length > 0) valores.Add((s.Descricao, item.Descricao));
            }
            Gravar(linhas.Select(x => (s, x.Id, (IEnumerable<(string, string)>)valores)));
        }

        private void Anexar(List<LinhaVerificacao> linhas)
        {
            var porSistema = linhas.GroupBy(x => x.SistemaRef).ToList();
            Gravar(porSistema.SelectMany(g => g.Select(x =>
            {
                var vazios = new List<(string, string)> { (g.Key.PropriedadeCodigo, "") };
                if (g.Key.Descricao != null) vazios.Add((g.Key.Descricao, ""));
                return (g.Key, x.Id, (IEnumerable<(string, string)>)vazios);
            })));
        }

        // ------------------------------------------------------------------ desenho

        private void Selecionar(IEnumerable<ObjectId> ids)
        {
            var lista = ids.Distinct().Where(id => id.IsValid && !id.IsErased).ToArray();
            try { _doc.Editor.SetImpliedSelection(lista); } catch { }
            _rodapeInfo.Text = $"{lista.Length:N0} objeto(s) selecionado(s) no modelo.";
        }

        /// <summary>Seleciona o objeto e aproxima a vista dele.</summary>
        private void Zoom(LinhaVerificacao l)
        {
            Selecionar(new[] { l.Id });
            try
            {
                using (_doc.LockDocument())
                using (Transaction tr = _db.TransactionManager.StartTransaction())
                {
                    var e = (Entity)tr.GetObject(l.Id, OpenMode.ForRead);
                    Extents3d ext = e.GeometricExtents;
                    Editor ed = _doc.Editor;
                    using (ViewTableRecord v = ed.GetCurrentView())
                    {
                        Matrix3d paraDcs = (Matrix3d.Rotation(-v.ViewTwist, v.ViewDirection, v.Target) *
                                            Matrix3d.Displacement(v.Target - Point3d.Origin) *
                                            Matrix3d.PlaneToWorld(v.ViewDirection)).Inverse();
                        ext.TransformBy(paraDcs);
                        double w = Math.Max(ext.MaxPoint.X - ext.MinPoint.X, 1), h = Math.Max(ext.MaxPoint.Y - ext.MinPoint.Y, 1);
                        v.CenterPoint = new Point2d((ext.MinPoint.X + ext.MaxPoint.X) / 2, (ext.MinPoint.Y + ext.MaxPoint.Y) / 2);
                        double aspecto = v.Width / Math.Max(v.Height, 1e-9);
                        v.Height = Math.Max(h, w / aspecto) * 1.4;
                        v.Width = v.Height * aspecto;
                        ed.SetCurrentView(v);
                    }
                    tr.Commit();
                }
            }
            catch { /* sem extensão (objeto vazio): fica só a seleção */ }
            string texto = !string.IsNullOrEmpty(l.Bloqueio) ? "Não dá para gravar: " + l.Bloqueio : l.Proposta;
            _rodapeInfo.Text = $"{l.Objeto} ({l.Handle}) selecionado." + (string.IsNullOrEmpty(texto) ? "" : " " + texto);
        }

        private void Exportar()
        {
            if (_tabela == null) return;
            var d = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                FileName = $"verificacao_classificacao_{Path.GetFileNameWithoutExtension(_doc.Name)}.csv".Replace(" ", "_"),
            };
            if (d.ShowDialog(this) != true) return;
            var sb = new StringBuilder("Situação;Classificação;Property set;Objeto;Tipo;Layer;Handle;Preenchido;Na lista;Proposta;Bloqueio\r\n");
            foreach (LinhaVerificacao l in _tabela.VisibleItems<LinhaValor>().SelectMany(v => v.Linhas))
                sb.Append(string.Join(";", new[] { l.Situacao, l.Sistema, l.SistemaRef?.Propriedades, l.Objeto, l.Tipo, l.Layer, l.Handle, l.Preenchido, l.NaLista, l.Proposta, l.Bloqueio }
                    .Select(v => "\"" + (v ?? "").Replace("\"", "\"\"") + "\""))).Append("\r\n");
            try { File.WriteAllText(d.FileName, sb.ToString(), new UTF8Encoding(true)); }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível gravar o arquivo:\n" + ex.Message, Title); }
        }
    }

    /// <summary>Um valor escrito numa classificação (com a situação) e os objetos que o têm.</summary>
    public class LinhaValor
    {
        public LinhaValor(List<LinhaVerificacao> linhas) { Linhas = linhas; }
        public List<LinhaVerificacao> Linhas { get; }
        LinhaVerificacao P => Linhas[0];
        public string Situacao => P.Situacao;
        public int Objetos => Linhas.Count;
        public string ObjetosTexto => Objetos.ToString("N0");
        public string EstaEscrito => P.FaltaPset ? "(sem o property set)" : string.IsNullOrWhiteSpace(P.Preenchido) ? "(vazio)" : P.Preenchido;
        public string NaLista => P.NaLista;
        public string Acao => P.Acao;
        public string Proposta => P.Proposta;
        public bool Problema => Situacao != ClassificacaoCore.OK;
        public string VaiFicar => P.Gravacoes.Count > 0 ? string.Join(" · ", P.Gravacoes.Select(g => g.Valor)) : null;
        public string OQueMuda =>
            Situacao == ClassificacaoCore.OK ? "" :
            Situacao == ClassificacaoCore.DescricaoDiferente ? "texto da descrição" :
            Situacao == ClassificacaoCore.ForaDaLista ? "código" :
            Situacao == ClassificacaoCore.FormatoInvalido ? "formato" :
            Situacao == ClassificacaoCore.CategoriaNaoPrevista ? "tipo ou layer não previsto" :
            Situacao == ClassificacaoCore.SemClassificacao ? "escolher" :
            Situacao == ClassificacaoCore.SemParametro ? "anexar property set" : Situacao;
    }
}
