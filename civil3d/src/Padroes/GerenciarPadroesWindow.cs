using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MetroSP.Ui;
using Microsoft.Win32;

namespace MetroSP.Padroes
{
    /// <summary>
    /// Todos os padrões salvos pelo plugin (classificação, confronto, exportação IFC...) e as planilhas que eles usam,
    /// num lugar só: editar, excluir, trocar a planilha de todos de uma vez e levar tudo para outro computador num .zip.
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
            Title = "MetroSP - Padrões e planilhas";
            Width = 1000;
            Height = 600;
            MinWidth = 640;
            MinHeight = 360;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

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

            _editar = Botao("Editar...", "Abre o padrão no editor da ferramenta (duplo clique também).", Editar);
            _excluir = Botao("Excluir", "Apaga os padrões selecionados deste computador. As planilhas não são apagadas.", Excluir);
            _pastaPadrao = Botao("Abrir pasta", "Mostra o arquivo do padrão no Explorer.", () => Mostrar(Selecionados().First().Arquivo));
            Button exportar = Botao("Exportar pacote...", "Grava os padrões selecionados (ou todos, se nada estiver selecionado) num .zip para a equipe, com as planilhas que eles usam.", Exportar);
            Button importar = Botao("Importar pacote...", "Traz os padrões de um .zip exportado por outra pessoa. As planilhas do pacote vão para " + GerenciadorPadroes.PastaPlanilhasImportadas + ".", Importar);
            var barraPadroes = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            foreach (Button b in new[] { _editar, _excluir, _pastaPadrao, exportar, importar }) { b.Margin = new Thickness(0, 0, 8, 4); barraPadroes.Children.Add(b); }
            var abaPadroes = new DockPanel();
            DockPanel.SetDock(barraPadroes, Dock.Top);
            abaPadroes.Children.Add(barraPadroes);
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
            Theme.Apply(this, "Padrões e planilhas",
                "Todos os padrões salvos neste computador e as planilhas que eles usam. Crie cada padrão na sua ferramenta; aqui você revê, troca planilhas e leva para a equipe.",
                _abas, rodape);
            _abas.ToolTip = string.IsNullOrEmpty(ondeCriar) ? null : "Onde se cria cada tipo: " + ondeCriar;
            Recarregar();
        }

        private static Button Botao(string texto, string dica, Action acao)
        {
            var b = new Button { Content = texto, ToolTip = dica };
            ToolTipService.SetShowOnDisabled(b, true);
            b.Click += (s, e) =>
            {
                try { acao(); }
                catch (Exception ex) { MessageBox.Show(Window.GetWindow(b), ex.Message, "MetroSP", MessageBoxButton.OK, MessageBoxImage.Warning); }
            };
            return b;
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

        private void Importar()
        {
            var d = new OpenFileDialog { Title = "Importar pacote de padrões", Filter = "Pacote de padrões MetroSP (*.zip)|*.zip" };
            if (d.ShowDialog(this) != true) return;
            string r = GerenciadorPadroes.ImportarPacote(d.FileName, _tipos, nome =>
                MessageBox.Show(this, $"Já existe \"{nome}\" neste computador. Substituir pelo do pacote?", "MetroSP",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
            Recarregar();
            MessageBox.Show(this, r, "MetroSP", MessageBoxButton.OK, MessageBoxImage.Information);
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
