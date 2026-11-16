using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MetroSP.Ui;
using Microsoft.Win32;

namespace MetroSP.Padroes
{
    /// <summary>
    /// Todos os padrões salvos pelo plugin (classificação, confronto, exportação IFC...) e as planilhas que eles usam,
    /// num lugar só: adicionar (arrastando ou escolhendo arquivos), editar, excluir, trocar a planilha de todos de uma vez
    /// e levar tudo para outro computador num .zip.
    /// </summary>
    public class GerenciarPadroesWindow : Window
    {
        private readonly IList<TipoPadrao> _tipos;
        private readonly string _programa;
        private readonly List<ItemPadrao> _itens = new List<ItemPadrao>();
        private readonly List<PlanilhaUsada> _planilhas = new List<PlanilhaUsada>();
        private readonly FilterableGrid _gridPadroes, _gridPlanilhas;
        private readonly TabControl _abas;
        private readonly TextBlock _resumo;
        private readonly Button _editar, _excluir, _pastaPadrao, _abrirPlanilha, _trocar, _pastaPlanilha;

        public GerenciarPadroesWindow(IList<TipoPadrao> tipos, string programa)
        {
            _tipos = tipos;
            _programa = programa;
            Title = "MetroSP - Padrões";
            Width = 1000;
            Height = 600;
            MinWidth = 640;
            MinHeight = 360;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            AllowDrop = true;
            Drop += (s, e) =>
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] arquivos) Executar(() => Adicionar(arquivos));
            };
            DragOver += (s, e) =>
            {
                e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            };

            // ---- Padrões
            _gridPadroes = new FilterableGrid(_itens, typeof(ItemPadrao));
            _gridPadroes.AddColumn("Tipo", nameof(ItemPadrao.TipoNome), 150);
            _gridPadroes.AddColumn("Nome", nameof(ItemPadrao.Nome), 220);
            _gridPadroes.AddColumn("Planilhas", nameof(ItemPadrao.PlanilhasTexto), 260, fill: true);
            _gridPadroes.AddColumn("Situação", nameof(ItemPadrao.Situacao), 150);
            _gridPadroes.AddColumn("Modificado", nameof(ItemPadrao.ModificadoTexto), 120);
            Theme.StyleSelection(_gridPadroes.Grid);
            _gridPadroes.Grid.LoadingRow += (s, e) =>
                e.Row.Background = e.Row.Item is ItemPadrao i && i.FaltaPlanilha ? Theme.LinhaProblema : Theme.LinhaNormal;
            _gridPadroes.Grid.SelectionChanged += (s, e) => Atualizar();
            _gridPadroes.Grid.MouseDoubleClick += (s, e) => { if (_editar.IsEnabled) Editar(); };

            List<string> extensoes = GerenciadorPadroes.ExtensoesAceitas(_tipos);
            string tiposAceitos = string.Join(", ", _tipos.Where(t => t.Reconhece != null).Select(t => t.Nome)) + " e pacotes .zip";
            Button adicionar = Botao("Adicionar padrões...", "Traz arquivos de padrão para este computador: " + tiposAceitos +
                ". O tipo de cada arquivo é reconhecido sozinho e você confere antes de gravar. Também dá para arrastar os arquivos para a janela.", Escolher);
            adicionar.Style = Theme.PrimaryButton;
            _editar = Botao("Editar...", "Abre o padrão no editor da ferramenta (duplo clique também).", Editar);
            _excluir = Botao("Excluir", "Apaga os padrões selecionados deste computador. As planilhas não são apagadas.", Excluir);
            _pastaPadrao = Botao("Abrir pasta", "Mostra o arquivo do padrão no Explorer.", () => Mostrar(Selecionados().First().Arquivo));
            Button exportar = Botao("Exportar pacote...", "Grava os padrões selecionados (ou todos, se nada estiver selecionado) num .zip para a equipe, com as planilhas que eles usam.", Exportar);
            var barraPadroes = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            foreach (Button b in new[] { adicionar, _editar, _excluir, _pastaPadrao, exportar }) { b.Margin = new Thickness(0, 0, 8, 4); barraPadroes.Children.Add(b); }

            // Área para soltar arquivos (a janela inteira aceita).
            var solta = new Border
            {
                BorderBrush = Theme.Solid(Color.FromRgb(0xC8, 0xC8, 0xC8)),
                BorderThickness = new Thickness(1.5),
                Background = Theme.Solid(Color.FromRgb(0xFC, 0xFC, 0xFC)),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 8),
                ToolTip = "Reconhece: " + tiposAceitos + ". Planilhas (.xlsx, .csv) não são padrões: troque a planilha de um padrão na aba Planilhas.",
            };
            var linhaSolta = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
            linhaSolta.Children.Add(new TextBlock
            {
                Text = "Arraste aqui novos padrões (" + string.Join(", ", extensoes) + ") ou ",
                Foreground = Theme.Solid(Theme.Cinza), VerticalAlignment = VerticalAlignment.Center,
            });
            Button escolher = Theme.Link("escolha os arquivos...");
            escolher.Click += (s, e) => Executar(Escolher);
            linhaSolta.Children.Add(escolher);
            solta.Child = linhaSolta;

            var topo = new StackPanel();
            topo.Children.Add(solta);
            topo.Children.Add(barraPadroes);
            var abaPadroes = new DockPanel();
            DockPanel.SetDock(topo, Dock.Top);
            abaPadroes.Children.Add(topo);
            abaPadroes.Children.Add(_gridPadroes);

            // ---- Planilhas
            _gridPlanilhas = new FilterableGrid(_planilhas, typeof(PlanilhaUsada));
            _gridPlanilhas.AddColumn("Planilha", nameof(PlanilhaUsada.Nome), 220);
            _gridPlanilhas.AddColumn("Situação", nameof(PlanilhaUsada.Situacao), 110);
            _gridPlanilhas.AddColumn("Usada por", nameof(PlanilhaUsada.PadroesTexto), 280, fill: true);
            _gridPlanilhas.AddColumn("Pasta", nameof(PlanilhaUsada.Pasta), 240);
            _gridPlanilhas.AddColumn("Modificada", nameof(PlanilhaUsada.ModificadoTexto), 120);
            Theme.StyleSelection(_gridPlanilhas.Grid);
            _gridPlanilhas.Grid.SelectionMode = DataGridSelectionMode.Single;
            _gridPlanilhas.Grid.LoadingRow += (s, e) =>
                e.Row.Background = e.Row.Item is PlanilhaUsada p && !p.Existe ? Theme.LinhaProblema : Theme.LinhaNormal;
            _gridPlanilhas.Grid.SelectionChanged += (s, e) => Atualizar();
            _gridPlanilhas.Grid.MouseDoubleClick += (s, e) => { if (_abrirPlanilha.IsEnabled) AbrirPlanilha(); };

            _abrirPlanilha = Botao("Abrir planilha", "Abre a planilha no Excel (duplo clique também).", AbrirPlanilha);
            _trocar = Botao("Trocar planilha...", "A planilha mudou de lugar ou de nome? Escolha a nova e todos os padrões que usam esta passam a usar a nova.", Trocar);
            _pastaPlanilha = Botao("Abrir pasta", "Mostra a planilha no Explorer.", () => Mostrar(PlanilhaSelecionada().Caminho));
            var barraPlanilhas = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            foreach (Button b in new[] { _abrirPlanilha, _trocar, _pastaPlanilha }) { b.Margin = new Thickness(0, 0, 8, 4); barraPlanilhas.Children.Add(b); }
            var abaPlanilhas = new DockPanel();
            DockPanel.SetDock(barraPlanilhas, Dock.Top);
            abaPlanilhas.Children.Add(barraPlanilhas);
            abaPlanilhas.Children.Add(_gridPlanilhas);

            _abas = new TabControl();
            _abas.Items.Add(new TabItem { Header = "Padrões", Content = abaPadroes, ToolTip = "Os padrões salvos pelas ferramentas neste computador." });
            _abas.Items.Add(new TabItem { Header = "Planilhas", Content = abaPlanilhas, ToolTip = "As planilhas que os padrões usam. Em laranja, as que não estão mais no lugar." });
            _abas.SelectionChanged += (s, e) => { if (e.Source == _abas) Atualizar(); };

            _resumo = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            var fechar = new Button { Content = "Fechar", IsCancel = true };
            fechar.Click += (s, e) => Close();
            var rodape = new DockPanel();
            StackPanel botoes = Theme.Buttons(fechar);
            DockPanel.SetDock(botoes, Dock.Right);
            rodape.Children.Add(botoes);
            rodape.Children.Add(_resumo);

            string ondeCriar = string.Join("; ", _tipos.Where(t => !string.IsNullOrEmpty(t.Ferramenta)).Select(t => $"{t.Nome}: {t.Ferramenta}"));
            Theme.Apply(this, "Padrões",
                "Adicione, reveja e exclua os padrões deste computador e leve para a equipe",
                _abas, rodape);
            _abas.ToolTip = string.IsNullOrEmpty(ondeCriar) ? null : "Onde se cria cada tipo: " + ondeCriar;
            Recarregar();
        }

        private Button Botao(string texto, string dica, Action acao)
        {
            var b = new Button { Content = texto, ToolTip = dica };
            ToolTipService.SetShowOnDisabled(b, true);
            b.Click += (s, e) => Executar(acao);
            return b;
        }

        private void Executar(Action acao)
        {
            try { acao(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "MetroSP", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private List<ItemPadrao> Selecionados() => _gridPadroes.Grid.SelectedItems.OfType<ItemPadrao>().ToList();
        private PlanilhaUsada PlanilhaSelecionada() => _gridPlanilhas.Grid.SelectedItem as PlanilhaUsada;

        private void Recarregar()
        {
            _itens.Clear();
            _itens.AddRange(GerenciadorPadroes.Listar(_tipos));
            _planilhas.Clear();
            _planilhas.AddRange(GerenciadorPadroes.Planilhas(_itens));
            _gridPadroes.Refresh();
            _gridPlanilhas.Refresh();
            Atualizar();
        }

        private void Atualizar()
        {
            List<ItemPadrao> sel = Selecionados();
            _editar.IsEnabled = sel.Count == 1 && sel[0].Tipo.Editar != null;
            _editar.ToolTip = sel.Count == 1 && sel[0].Tipo.Editar == null && !string.IsNullOrEmpty(sel[0].Tipo.Ferramenta)
                ? $"Este tipo se edita em {sel[0].Tipo.Ferramenta}." : "Abre o padrão no editor da ferramenta (duplo clique também).";
            _excluir.IsEnabled = sel.Count > 0;
            _pastaPadrao.IsEnabled = sel.Count == 1;
            PlanilhaUsada p = PlanilhaSelecionada();
            _abrirPlanilha.IsEnabled = p != null && p.Existe;
            _trocar.IsEnabled = p != null;
            _pastaPlanilha.IsEnabled = p != null && Directory.Exists(p.Pasta);

            int faltando = _planilhas.Count(x => !x.Existe);
            _resumo.Text = $"{_itens.Count} padrão(ões) de {_itens.Select(i => i.Tipo).Distinct().Count()} tipo(s), {_planilhas.Count} planilha(s)" +
                (faltando > 0 ? $", {faltando} não encontrada(s): use Trocar planilha na aba Planilhas." : ".");
        }

        private void Editar()
        {
            ItemPadrao i = Selecionados().FirstOrDefault();
            if (i?.Tipo.Editar == null) return;
            i.Tipo.Editar(this, i);
            Recarregar();
        }

        private void Excluir()
        {
            List<ItemPadrao> sel = Selecionados();
            if (sel.Count == 0) return;
            string lista = string.Join("\n", sel.Take(10).Select(i => $"- {i.Tipo.Nome}: {i.Nome}")) + (sel.Count > 10 ? $"\n... e mais {sel.Count - 10}" : "");
            if (MessageBox.Show(this, $"Apagar {sel.Count} padrão(ões) deste computador?\n\n{lista}\n\nDica: exporte um pacote antes, para ter cópia.",
                    "MetroSP", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            foreach (ItemPadrao i in sel) File.Delete(i.Arquivo);
            Recarregar();
        }

        private void Exportar()
        {
            List<ItemPadrao> sel = Selecionados();
            if (sel.Count == 0) sel = _gridPadroes.VisibleItems<ItemPadrao>().ToList();
            if (sel.Count == 0) { MessageBox.Show(this, "Não há padrões para exportar.", "MetroSP"); return; }
            var d = new SaveFileDialog
            {
                Title = $"Exportar {sel.Count} padrão(ões)",
                Filter = "Pacote de padrões MetroSP (*.zip)|*.zip",
                FileName = $"Padroes MetroSP {_programa} {DateTime.Now:yyyy-MM-dd}.zip",
            };
            if (d.ShowDialog(this) != true) return;
            bool comPlanilhas = sel.Any(i => i.Planilhas.Count > 0) &&
                MessageBox.Show(this, "Levar junto as planilhas que esses padrões usam?\n\nSim: quem importar já recebe as planilhas.\nNão: só os padrões; cada um aponta para a planilha no próprio computador.",
                    "MetroSP", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            string r = GerenciadorPadroes.ExportarPacote(sel, d.FileName, comPlanilhas, _programa);
            MessageBox.Show(this, r, "MetroSP", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Escolher()
        {
            List<string> ext = GerenciadorPadroes.ExtensoesAceitas(_tipos);
            string filtro = string.Join(";", ext.Select(e => "*" + e));
            var d = new OpenFileDialog
            {
                Title = "Adicionar padrões",
                Multiselect = true,
                Filter = $"Padrões MetroSP ({filtro})|{filtro}|Todos os arquivos (*.*)|*.*",
            };
            if (d.ShowDialog(this) == true) Adicionar(d.FileNames);
        }

        /// <summary>Uma linha da conferência antes de gravar.</summary>
        private class Novo
        {
            public string Arquivo;
            public List<TipoPadrao> Candidatos;
            public ComboBox Tipo;
            public TextBlock Situacao;
            public TipoPadrao Escolhido => (Tipo?.SelectedItem as ComboBoxItem)?.Tag as TipoPadrao;
        }

        /// <summary>
        /// Traz arquivos de padrão: os .zip entram como pacote; os outros têm o tipo reconhecido e, depois de conferidos,
        /// são copiados para a pasta do tipo (ou seguem para a ferramenta dele, como os arquivos IFC, que pedem a classe).
        /// </summary>
        private void Adicionar(IEnumerable<string> arquivos)
        {
            List<string> lista = arquivos.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (lista.Count == 0) return;
            var resumos = new List<string>();

            foreach (string zip in lista.Where(f => Path.GetExtension(f).Equals(".zip", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    resumos.Add(Path.GetFileName(zip) + ": " + GerenciadorPadroes.ImportarPacote(zip, _tipos, nome =>
                        MessageBox.Show(this, $"Já existe \"{nome}\" neste computador. Substituir pelo do pacote?", "MetroSP",
                            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes));
                }
                catch (Exception ex) { resumos.Add(Path.GetFileName(zip) + ": " + ex.Message); }
            }

            List<string> soltos = lista.Where(f => !Path.GetExtension(f).Equals(".zip", StringComparison.OrdinalIgnoreCase)).ToList();
            if (soltos.Count > 0)
            {
                List<Novo> novos = soltos.Select(f => new Novo { Arquivo = f, Candidatos = GerenciadorPadroes.Candidatos(f, _tipos) }).ToList();
                if (Conferir(novos))
                {
                    var erros = new List<string>();
                    int copiados = 0;
                    foreach (Novo n in novos.Where(x => x.Escolhido != null && x.Escolhido.Trazer == null))
                    {
                        try { GerenciadorPadroes.Copiar(n.Arquivo, n.Escolhido); copiados++; }
                        catch (Exception ex) { erros.Add($"{Path.GetFileName(n.Arquivo)}: {ex.Message}"); }
                    }
                    if (copiados > 0) resumos.Add($"{copiados} padrão(ões) adicionado(s).");
                    // Os que têm jeito próprio de entrar vão juntos, uma vez por ferramenta (ex.: todos os arquivos IFC numa janela só).
                    foreach (var g in novos.Where(x => x.Escolhido?.Trazer != null).GroupBy(x => x.Escolhido.Trazer))
                    {
                        try
                        {
                            string r = g.Key(this, g.Select(x => x.Arquivo).ToList());
                            if (!string.IsNullOrEmpty(r)) resumos.Add(r);
                        }
                        catch (Exception ex) { erros.Add(ex.Message); }
                    }
                    if (erros.Count > 0) resumos.Add("Não foi possível trazer:\n" + string.Join("\n", erros));
                }
            }

            Recarregar();
            if (resumos.Count > 0) MessageBox.Show(this, string.Join("\n\n", resumos), "MetroSP", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>Mostra cada arquivo com o tipo reconhecido e o que vai acontecer. true = gravar.</summary>
        private bool Conferir(List<Novo> novos)
        {
            var janela = new Window
            {
                Title = "MetroSP - Adicionar padrões", Owner = this, Width = 760, SizeToContent = SizeToContent.Height, MaxHeight = 640,
                MinWidth = 480, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.CanResizeWithGrip,
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
            void Celula(UIElement e, int r, int col)
            {
                Grid.SetRow(e, r);
                Grid.SetColumn(e, col);
                if (e is FrameworkElement f) { f.Margin = new Thickness(0, 3, 8, 3); f.VerticalAlignment = VerticalAlignment.Center; }
                g.Children.Add(e);
            }
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string[] titulos = { "Arquivo", "Tipo", "O que acontece" };
            for (int i = 0; i < titulos.Length; i++) Celula(Theme.Secao(titulos[i]), 0, i);

            var resumo = new TextBlock();
            var ok = new Button { Content = "Adicionar", IsDefault = true, Style = Theme.PrimaryButton };
            void Atualizar()
            {
                int validos = 0, substitui = 0;
                foreach (Novo n in novos)
                {
                    TipoPadrao t = n.Escolhido;
                    string texto;
                    Color cor;
                    if (t == null)
                    {
                        texto = n.Candidatos.Count == 0 ? "fica de fora: não é um padrão conhecido" : "fica de fora";
                        cor = Theme.Cinza;
                    }
                    else if (t.Trazer != null) { texto = t.TrazerTexto ?? "segue para " + t.Ferramenta; cor = Theme.Cinza; validos++; }
                    else if (File.Exists(GerenciadorPadroes.Destino(t, n.Arquivo)))
                    {
                        texto = "substitui o atual (a cópia fica em Anteriores)";
                        cor = Theme.LaranjaTexto;
                        validos++;
                        substitui++;
                    }
                    else { texto = "novo"; cor = Theme.Verde; validos++; }
                    n.Situacao.Text = texto;
                    n.Situacao.ToolTip = texto;
                    n.Situacao.Foreground = Theme.Solid(cor);
                }
                ok.Content = validos == 1 ? "Adicionar 1" : $"Adicionar {validos}";
                ok.IsEnabled = validos > 0;
                resumo.Text = substitui == 0 ? $"{novos.Count} arquivo(s) escolhido(s)" : $"{substitui} substitui(em) padrões que já existem";
                resumo.Foreground = Theme.Solid(substitui == 0 ? Theme.Cinza : Theme.LaranjaTexto);
            }

            foreach (Novo n in novos)
            {
                int r = g.RowDefinitions.Count;
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Celula(new TextBlock { Text = Path.GetFileName(n.Arquivo), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = n.Arquivo }, r, 0);
                n.Tipo = new ComboBox { ToolTip = "Tipo reconhecido pelo conteúdo do arquivo. Troque se houver mais de uma opção." };
                foreach (TipoPadrao t in n.Candidatos) n.Tipo.Items.Add(new ComboBoxItem { Content = t.Nome, Tag = t, ToolTip = t.Ferramenta });
                n.Tipo.Items.Add(new ComboBoxItem { Content = "(deixar de fora)", Tag = null });
                n.Tipo.SelectedIndex = 0;
                n.Tipo.IsEnabled = n.Candidatos.Count > 0;
                n.Tipo.SelectionChanged += (s, e) => Atualizar();
                Celula(n.Tipo, r, 1);
                n.Situacao = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 11, FontWeight = FontWeights.SemiBold };
                Celula(n.Situacao, r, 2);
            }

            ok.Click += (s, e) => janela.DialogResult = true;
            var cancelar = new Button { Content = "Cancelar", IsCancel = true };
            Atualizar();
            Theme.Apply(janela, "Adicionar padrões", "Confira o tipo de cada arquivo antes de gravar",
                new ScrollViewer { Content = g, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, Theme.Rodape(resumo, cancelar, ok));
            return janela.ShowDialog() == true;
        }

        private void AbrirPlanilha()
        {
            PlanilhaUsada p = PlanilhaSelecionada();
            if (p == null || !p.Existe) return;
            Process.Start(new ProcessStartInfo(p.Caminho) { UseShellExecute = true });
        }

        private void Trocar()
        {
            PlanilhaUsada p = PlanilhaSelecionada();
            if (p == null) return;
            var d = new OpenFileDialog
            {
                Title = "Nova planilha no lugar de " + p.Nome,
                Filter = "Planilhas (*.xlsx;*.xlsm;*.csv)|*.xlsx;*.xlsm;*.csv|Todos os arquivos (*.*)|*.*",
                InitialDirectory = Directory.Exists(p.Pasta) ? p.Pasta : null,
                FileName = p.Nome,
            };
            if (d.ShowDialog(this) != true || GerenciadorPadroes.MesmoCaminho(d.FileName, p.Caminho)) return;
            int n = GerenciadorPadroes.TrocarPlanilha(_itens, p.Caminho, d.FileName);
            Recarregar();
            MessageBox.Show(this, $"{n} padrão(ões) passaram a usar {Path.GetFileName(d.FileName)}.\nAs abas e colunas indicadas continuam as mesmas: confira se a nova planilha tem a mesma estrutura.",
                "MetroSP", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static void Mostrar(string arquivo)
        {
            try
            {
                if (File.Exists(arquivo)) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{arquivo}\"") { UseShellExecute = true });
                else if (Directory.Exists(Path.GetDirectoryName(arquivo))) Process.Start(new ProcessStartInfo(Path.GetDirectoryName(arquivo)) { UseShellExecute = true });
            }
            catch { /* sem Explorer */ }
        }
    }
}
