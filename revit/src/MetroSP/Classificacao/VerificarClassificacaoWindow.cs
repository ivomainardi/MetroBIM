using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Autodesk.Revit.DB;
using MetroSP.Ui;
using Grid = System.Windows.Controls.Grid;

namespace MetroSP.Classificacao
{
    /// <summary>
    /// Verifica se o ClassificationCode preenchido confere com a planilha: formato, código existente,
    /// descrição igual à da lista e categoria prevista. Propõe correções e grava as aceitas.
    /// Modal: as transações rodam no contexto do comando.
    /// </summary>
    public class VerificarClassificacaoWindow : Window
    {
        private readonly Autodesk.Revit.UI.UIDocument _uidoc;
        private readonly Document _doc;
        private readonly ClassSettings _config;
        private readonly ICollection<ElementId> _selecao;
        private readonly PlanilhaSeletor _planilha;
        private readonly ComboBox _escopo;
        private readonly SeletorCategorias _categorias = new SeletorCategorias { Margin = new Thickness(0, 0, 12, 0) };
        private readonly Button _reanalisar;
        private List<Element> _base = new List<Element>();
        private bool _ocultarOk;
        private string _situacao; // etiqueta clicada (filtra a tabela)
        private readonly CheckBox _incluirInseguras;
        private readonly WrapPanel _resumo;
        private readonly DockPanel _areaTabela;
        private readonly TextBlock _rodapeInfo;
        private readonly Button _aplicar;
        private readonly Button _aplicarSelecionadas;
        private readonly Button _exportar;
        private readonly Button _selecionar;
        private List<LinhaVerificacao> _linhas;
        private FilterableGrid _tabela;

        public VerificarClassificacaoWindow(Autodesk.Revit.UI.UIDocument uidoc, ClassSettings config)
        {
            _uidoc = uidoc;
            _doc = uidoc.Document;
            _config = config;
            ICollection<ElementId> sel = uidoc.Selection.GetElementIds();
            _selecao = sel.Count > 0 ? sel : null;

            Title = "MetroSP - Verificar classificação";
            Width = 1200;
            Height = 740;
            MinWidth = 820;
            MinHeight = 480;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _planilha = new PlanilhaSeletor(config);
            _planilha.Carregada += l => { if (_linhas != null) Verificar(); };

            _escopo = new ComboBox { Padding = new Thickness(4, 3, 4, 3), MinWidth = 220, Margin = new Thickness(0, 0, 12, 0) };
            if (_selecao != null) _escopo.Items.Add($"Seleção atual ({_selecao.Count} elementos)");
            _escopo.Items.Add("Vista atual");
            _escopo.Items.Add("Todo o modelo");
            _escopo.SelectedIndex = 0;
            var verificar = new Button { Content = "Verificar", Style = Theme.PrimaryButton };
            verificar.Click += (s, e) => Verificar();
            var barra = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            barra.Children.Add(new TextBlock { Text = "Escopo", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            barra.Children.Add(_escopo);
            barra.Children.Add(_categorias);
            barra.Children.Add(verificar);
            _escopo.SelectionChanged += (s, e) => CarregarEscopo();
            _categorias.Mudou += () => { if (_linhas != null) Verificar(); };

            _resumo = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            _incluirInseguras = new CheckBox
            {
                Content = "Incluir propostas que trocam o código (código parecido, texto fora do formato)",
                ToolTip = "Sem esta opção, só são aplicadas as correções de escrita: mesmo código, descrição como na lista.",
                Margin = new Thickness(0, 0, 0, 8),
            };
            _incluirInseguras.Click += (s, e) => AtualizarRodape();
            _areaTabela = new DockPanel();
            _areaTabela.Children.Add(new TextBlock
            {
                Text = $"Escolha a planilha e clique em \"Verificar\". Lê {config.Parametro} na instância ou, se não houver, no tipo.",
                Foreground = Theme.Solid(Theme.Cinza),
            });

            var corpo = new DockPanel();
            foreach (UIElement el in new UIElement[] { _planilha, barra, _resumo, _incluirInseguras })
            {
                DockPanel.SetDock(el, Dock.Top);
                corpo.Children.Add(el);
            }
            corpo.Children.Add(_areaTabela);

            _rodapeInfo = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            _exportar = new Button { Content = "Exportar (.csv)", IsEnabled = false };
            _selecionar = new Button { Content = "Selecionar no modelo", IsEnabled = false, ToolTip = "Seleciona no Revit os elementos visíveis na tabela." };
            _aplicar = new Button { Content = "Aplicar propostas", Style = Theme.PrimaryButton, IsEnabled = false };
            _reanalisar = new Button { Content = "Reanalisar", IsEnabled = false, ToolTip = "Lê o modelo de novo, mantendo escopo, categorias e filtros da tabela." };
            _reanalisar.Click += (s, e) => Verificar();
            var fechar = new Button { Content = "Fechar", IsCancel = true };
            _exportar.Click += (s, e) => Exportar();
            _selecionar.Click += (s, e) => Selecionar();
            _aplicar.Click += (s, e) => Aplicar(false);
            _aplicarSelecionadas = new Button
            {
                Content = "Aplicar selecionadas",
                IsEnabled = false,
                ToolTip = "Aplica só as propostas das linhas selecionadas na tabela (Ctrl/Shift+clique para escolher várias). Inclui as que trocam o código.",
            };
            _aplicarSelecionadas.Click += (s, e) => Aplicar(true);
            fechar.Click += (s, e) => Close();
            var rodape = new DockPanel();
            StackPanel botoes = Theme.Buttons(_reanalisar, _exportar, _selecionar, _aplicarSelecionadas, _aplicar, fechar);
            DockPanel.SetDock(botoes, Dock.Right);
            rodape.Children.Add(botoes);
            rodape.Children.Add(_rodapeInfo);

            Theme.Apply(this, "Verificar classificação", _doc.Title + $" · confere {config.Parametro} com a planilha", corpo, rodape);
            CarregarEscopo();
            _planilha.Carregar();
        }

        /// <summary>Lê os elementos do escopo e atualiza a lista de categorias.</summary>
        private void CarregarEscopo()
        {
            string esc = _escopo.SelectedItem as string ?? "";
            _base = esc.StartsWith("Seleção") ? ClassificationCore.Elementos(_doc, _selecao, null)
                : esc == "Vista atual" ? ClassificationCore.Elementos(_doc, null, _doc.ActiveView.Id)
                : ClassificationCore.Elementos(_doc, null, null);
            _categorias.Definir(_base.GroupBy(e => e.Category.Name).ToDictionary(g => g.Key, g => g.Count()));
        }

        private List<Element> NoEscopo()
        {
            CarregarEscopo(); // relê o modelo (reanalisar pega o que mudou)
            return _base.Where(e => _categorias.Aceita(e.Category.Name)).ToList();
        }

        private void Verificar()
        {
            if (_planilha.Lista == null)
            {
                MessageBox.Show(this, "Escolha a planilha de classificação primeiro.", Title);
                return;
            }
            Cursor = Cursors.Wait;
            try
            {
                using (Ui.Progresso.Abrir("Verificar classificação"))
                    _linhas = ClassificationCore.Verificar(_doc, NoEscopo(), _planilha.Lista, _config.Parametro);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível verificar:\n" + ex.Message, Title);
                return;
            }
            finally { Cursor = null; }
            Mostrar();
        }

        private static readonly string[] Ordem =
        {
            ClassificationCore.OK, ClassificationCore.DescricaoDiferente, ClassificationCore.ForaDaLista, ClassificationCore.FormatoInvalido,
            ClassificationCore.CategoriaNaoPrevista, ClassificationCore.SemClassificacao, ClassificationCore.SemParametro,
        };

        private void Mostrar()
        {
            if (_situacao != null && !_linhas.Any(l => l.Situacao == _situacao)) _situacao = null;
            MostrarEtiquetas();

            var estado = _tabela?.Estado();
            _areaTabela.Children.Clear();
            _tabela = new FilterableGrid(_linhas, typeof(LinhaVerificacao));
            _tabela.AddColumn("Situação", nameof(LinhaVerificacao.Situacao), 170);
            _tabela.AddColumn("Elemento", nameof(LinhaVerificacao.Elemento), 220);
            _tabela.AddColumn("Id", nameof(LinhaVerificacao.Id), 75);
            _tabela.AddColumn("Categoria", nameof(LinhaVerificacao.Categoria), 115);
            _tabela.AddColumn("Onde", nameof(LinhaVerificacao.Onde), 70);
            _tabela.AddColumn("Preenchido", nameof(LinhaVerificacao.Preenchido), 200);
            _tabela.AddColumn("Na lista", nameof(LinhaVerificacao.NaLista), 200);
            _tabela.AddColumn("Proposta", nameof(LinhaVerificacao.Proposta), 160, fill: true);
            if (_linhas.Any(l => !string.IsNullOrEmpty(l.Bloqueio)))
                _tabela.AddColumn("Bloqueio para gravar", nameof(LinhaVerificacao.Bloqueio), 220);
            _tabela.Grid.LoadingRow += (s, e) =>
            {
                var l = (LinhaVerificacao)e.Row.Item;
                e.Row.Background = l.Situacao == ClassificationCore.OK ? Theme.LinhaNormal
                    : l.Situacao == ClassificationCore.SemClassificacao || l.Situacao == ClassificationCore.SemParametro ? Theme.LinhaAviso
                    : Theme.LinhaProblema;
            };
            _tabela.Filtered += AtualizarRodape;
            _tabela.Grid.SelectionChanged += (s, e) => AtualizarSelecionadas();
            Theme.StyleSelection(_tabela.Grid);
            var opcoes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            opcoes.Children.Add(_tabela.CreateSingleLineToggle());
            var soProblemas = new CheckBox { Content = "Ocultar OK", VerticalAlignment = VerticalAlignment.Center, IsChecked = _ocultarOk };
            void FiltroOk()
            {
                _ocultarOk = soProblemas.IsChecked == true;
                AplicarFiltroExtra();
            }
            soProblemas.Click += (s, e) => FiltroOk();
            FiltroOk();
            _tabela.Restaurar(estado);
            opcoes.Children.Add(soProblemas);
            DockPanel.SetDock(opcoes, Dock.Top);
            _areaTabela.Children.Add(opcoes);
            _areaTabela.Children.Add(_tabela);

            _exportar.IsEnabled = _selecionar.IsEnabled = _reanalisar.IsEnabled = true;
            AtualizarRodape();
        }

        /// <summary>Etiquetas de resumo; clicar numa filtra a tabela por aquela situação (clicar de novo mostra tudo).</summary>
        private void MostrarEtiquetas()
        {
            _resumo.Children.Clear();
            foreach (string s in Ordem)
            {
                int n = _linhas.Count(l => l.Situacao == s);
                if (n == 0 && s != ClassificationCore.OK) continue;
                bool problema = s == ClassificationCore.DescricaoDiferente || s == ClassificationCore.ForaDaLista || s == ClassificationCore.FormatoInvalido;
                Border chip = Theme.Chip($"{n} {s.ToLowerInvariant()}", destaque: problema, escuro: s == ClassificationCore.ForaDaLista);
                chip.Cursor = Cursors.Hand;
                chip.ToolTip = _situacao == s ? "Clique para mostrar todas as situações" : "Clique para ver só estes na tabela (depois, \"Selecionar no modelo\" seleciona no Revit)";
                if (_situacao == s)
                {
                    chip.BorderBrush = Theme.Solid(Theme.Laranja);
                    chip.BorderThickness = new Thickness(2);
                }
                else if (_situacao != null) chip.Opacity = 0.55;
                string sit = s;
                chip.MouseLeftButtonUp += (o, e) =>
                {
                    _situacao = _situacao == sit ? null : sit;
                    MostrarEtiquetas();
                    AplicarFiltroExtra();
                };
                _resumo.Children.Add(chip);
            }

            var semParametro = _linhas.Where(l => l.Situacao == ClassificationCore.SemParametro).Select(l => l.Categoria).Distinct().ToList();
            if (semParametro.Count > 0)
            {
                var criar = new Button
                {
                    Content = $"Criar {_config.Parametro} em {semParametro.Count} {(semParametro.Count == 1 ? "categoria" : "categorias")}",
                    ToolTip = "Sem o parâmetro: " + string.Join(", ", semParametro),
                    Padding = new Thickness(8, 1, 8, 1),
                    Margin = new Thickness(4, 0, 0, 0),
                };
                criar.Click += (o, e) => CriarParametro(semParametro);
                _resumo.Children.Add(criar);
            }

            var comFormula = _linhas.Where(l => l.FamiliaFormula != null).GroupBy(l => l.FamiliaFormula).ToList();
            if (comFormula.Count > 0)
            {
                var remover = new Button
                {
                    Content = $"Remover fórmula em {comFormula.Count} {(comFormula.Count == 1 ? "família" : "famílias")}",
                    ToolTip = string.Join("\n", comFormula.Select(g => g.First().Bloqueio)),
                    Padding = new Thickness(8, 1, 8, 1),
                    Margin = new Thickness(4, 0, 0, 0),
                };
                remover.Click += (o, e) => RemoverFormulas(comFormula.Select(g => g.Key).ToList(), comFormula.Select(g => g.First().Bloqueio).ToList());
                _resumo.Children.Add(remover);
            }
        }

        private void RemoverFormulas(List<ElementId> familias, List<string> descricoes)
        {
            string msg = $"Remover a fórmula de {_config.Parametro} em {familias.Count} {(familias.Count == 1 ? "família" : "famílias")} e recarregá-las no projeto?\n\n" +
                         string.Join("\n", descricoes.Take(8).Select(d => "• " + d.Substring(ClassificationCore.PrefixoFormula.Length))) +
                         (descricoes.Count > 8 ? $"\n• e mais {descricoes.Count - 8}" : "") +
                         $"\n\n{_config.Parametro} continua o mesmo parâmetro (mesmo GUID) e fica com o último valor calculado, agora editável. " +
                         "Os arquivos .rfa da biblioteca não são alterados. Cada recarga pode ser desfeita com Ctrl+Z.";
            if (MessageBox.Show(this, msg, Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            Cursor = Cursors.Wait;
            List<string> ok, falhas;
            try
            {
                using (Ui.Progresso.Abrir("Remover fórmula da família", podeCancelar: true))
                    (ok, falhas) = FormulaFamilia.Remover(_doc, familias.Select(id => _doc.GetElement(id)).OfType<Family>(), _config.Parametro);
            }
            finally { Cursor = null; }
            MessageBox.Show(this, $"Fórmula removida em {ok.Count} {(ok.Count == 1 ? "família" : "famílias")}." +
                (falhas.Count > 0 ? "\n\nNão foi possível em:\n" + string.Join("\n", falhas.Select(f => "• " + f)) : "") +
                "\n\nAgora é só aplicar as propostas.", Title);
            Verificar();
        }

        private void AplicarFiltroExtra()
        {
            if (_tabela == null) return;
            string sit = _situacao;
            bool ocultar = _ocultarOk;
            _tabela.ExtraFilter = sit == null && !ocultar ? (Func<object, bool>)null
                : o => (sit == null || ((LinhaVerificacao)o).Situacao == sit) && (!ocultar || ((LinhaVerificacao)o).Situacao != ClassificationCore.OK);
            _tabela.Refresh();
        }

        private void CriarParametro(List<string> categorias)
        {
            try
            {
                string msg = ParametroClassificacao.Vincular(_doc, _config.Parametro, ParametroClassificacao.Categorias(_doc, categorias));
                if (msg == null) return;
                MessageBox.Show(this, msg, Title);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível criar o parâmetro:\n" + ex.Message, Title);
                return;
            }
            Verificar();
        }

        /// <summary>Propostas das linhas visíveis que serão gravadas, uma por dono (instância ou tipo).</summary>
        /// <param name="soSelecionadas">true = só as linhas selecionadas na tabela (aí entram também as que trocam o código).</param>
        private List<LinhaVerificacao> Propostas(out int conflitos, bool soSelecionadas = false)
        {
            conflitos = 0;
            if (_tabela == null) return new List<LinhaVerificacao>();
            bool todas = soSelecionadas || _incluirInseguras.IsChecked == true;
            IEnumerable<LinhaVerificacao> origem = soSelecionadas
                ? _tabela.Grid.SelectedItems.OfType<LinhaVerificacao>()
                : _tabela.VisibleItems<LinhaVerificacao>();
            var aceitas = origem
                .Where(l => l.ValorProposto != null && l.DonoId != null && (l.PropostaSegura || todas))
                .ToList();
            // Um tipo compartilhado por elementos com propostas diferentes fica de fora.
            var porDono = aceitas.GroupBy(l => l.DonoId).ToList();
            conflitos = porDono.Count(g => g.Select(l => l.ValorProposto).Distinct().Count() > 1);
            return porDono.Where(g => g.Select(l => l.ValorProposto).Distinct().Count() == 1).Select(g => g.First()).ToList();
        }

        private void AtualizarRodape()
        {
            if (_tabela == null) return;
            int v = _tabela.VisibleItems<LinhaVerificacao>().Count();
            var p = Propostas(out int conflitos);
            _rodapeInfo.Text = (v == _linhas.Count ? $"{v} elementos" : $"{v} de {_linhas.Count} visíveis (filtro ativo)") +
                               (conflitos > 0 ? $" · {conflitos} tipos com propostas diferentes ficam de fora" : "");
            _aplicar.IsEnabled = p.Count > 0;
            _aplicar.Content = p.Count == 0 ? "Nada a corrigir" : $"Aplicar todas ({p.Count})";
            AtualizarSelecionadas();
        }

        private void AtualizarSelecionadas()
        {
            int n = _tabela == null ? 0 : Propostas(out _, true).Count;
            _aplicarSelecionadas.IsEnabled = n > 0;
            _aplicarSelecionadas.Content = n > 0 ? $"Aplicar selecionadas ({n})" : "Aplicar selecionadas";
        }

        private void Aplicar(bool soSelecionadas)
        {
            var p = Propostas(out _, soSelecionadas);
            if (p.Count == 0) return;
            int tipos = p.Count(l => l.Onde == "tipo");
            string msg = $"Gravar {p.Count} correções em {_config.Parametro}?" +
                         (tipos > 0 ? $"\n\n{tipos} delas são em tipos e valem para todas as instâncias desses tipos." : "") +
                         "\n\nExemplos:\n" + string.Join("\n", p.Take(5).Select(l => $"• {l.Preenchido}  →  {l.ValorProposto}")) +
                         "\n\nÉ uma única operação: pode ser desfeita com Ctrl+Z.";
            if (MessageBox.Show(this, msg, Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                ResultadoGravacao r = ClassificationCore.Gravar(_doc, _config.Parametro, p.Select(l => (l.DonoId, l.ValorProposto)), "Corrigir classificação", Confirmar);
                MostrarResultado(r, "correções");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não foi possível gravar. Nada foi alterado.\n\n" + ex.Message, Title);
                return;
            }
            Verificar();
        }

        private bool Confirmar(string pergunta) =>
            MessageBox.Show(this, pergunta, Title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

        /// <summary>Mostra o resultado e oferece selecionar no Revit o que não foi gravado.</summary>
        private void MostrarResultado(ResultadoGravacao r, string oQue)
        {
            if (r.Falhas.Count == 0) { MessageBox.Show(this, r.Resumo(oQue), Title); return; }
            if (MessageBox.Show(this, r.Resumo(oQue) + "\n\nSelecionar no Revit os que não foram gravados?", Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                _uidoc.Selection.SetElementIds(r.Falhas.Select(f => f.Id).ToList());
        }

        private void Selecionar()
        {
            if (_tabela == null) return;
            var ids = _tabela.VisibleItems<LinhaVerificacao>().Select(l => l.ElementoId).ToList();
            _uidoc.Selection.SetElementIds(ids);
            _rodapeInfo.Text = $"{ids.Count} elementos selecionados no Revit.";
        }

        private void Exportar()
        {
            if (_tabela == null) return;
            var dialogo = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                FileName = $"verificacao_classificacao_{_doc.Title}.csv".Replace(" ", "_"),
            };
            if (dialogo.ShowDialog(this) != true) return;
            var sb = new StringBuilder();
            sb.AppendLine("Situação;Elemento;Id;Categoria;Onde;Preenchido;Na lista;Proposta;Bloqueio");
            foreach (LinhaVerificacao l in _tabela.VisibleItems<LinhaVerificacao>())
                sb.AppendLine(string.Join(";", new[] { l.Situacao, l.Elemento, l.Id.ToString(), l.Categoria, l.Onde, l.Preenchido, l.NaLista, l.Proposta, l.Bloqueio }
                    .Select(v => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"")));
            try { File.WriteAllText(dialogo.FileName, sb.ToString(), new UTF8Encoding(true)); }
            catch (Exception ex) { MessageBox.Show(this, "Não foi possível gravar o arquivo:\n" + ex.Message, Title); }
        }
    }
}
