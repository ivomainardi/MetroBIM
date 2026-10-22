using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MetroSP.Ui;

namespace MetroSP.Ids
{
    /// <summary>
    /// Janela não modal com o resultado. Duplo clique numa linha seleciona os elementos no Revit
    /// (via ExternalEvent, porque a API só pode ser chamada no contexto do Revit).
    /// </summary>
    public class ResultadosIdsWindow : Window
    {
        private readonly FilterableGrid _tabela;
        private readonly DataGrid _grid;
        private readonly SelecionarHandler _handler = new SelecionarHandler();
        private readonly ExternalEvent _evento;

        public ResultadosIdsWindow(IdsDocument ids, List<CheckResult> resultados)
        {
            _evento = ExternalEvent.Create(_handler);

            Title = $"MetroSP - Mapeamento IDS: {ids.Title}";
            Width = 1100;
            Height = 600;
            MinWidth = 700;
            MinHeight = 300;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var dock = new DockPanel();

            int ok = resultados.Count(r => r.Status == CheckStatus.OK);
            int avisos = resultados.Count(r => r.Status == CheckStatus.Aviso || r.Status == CheckStatus.NaoVerificado);
            int problemas = resultados.Count - ok - avisos;
            var topo = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var dica = new TextBlock
            {
                Text = "Shift+clique no cabeçalho ordena por várias colunas · duplo clique seleciona os elementos",
                Foreground = Theme.Solid(Theme.Cinza),
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(dica, Dock.Right);
            topo.Children.Add(dica);
            var chips = new WrapPanel();
            chips.Children.Add(Theme.Chip($"{resultados.Count} verificações"));
            chips.Children.Add(Theme.Chip($"{ok} OK"));
            chips.Children.Add(Theme.Chip($"{problemas} problemas", destaque: problemas > 0));
            chips.Children.Add(Theme.Chip($"{avisos} avisos", escuro: avisos > 0));
            topo.Children.Add(chips);
            DockPanel.SetDock(topo, Dock.Top);
            dock.Children.Add(topo);

            var soPendencias = new CheckBox { Content = "Mostrar só pendências", VerticalAlignment = VerticalAlignment.Center };
            var exportar = new Button { Content = "Exportar CSV..." };
            var fechar = new Button { Content = "Fechar", Style = Theme.PrimaryButton };
            var rodape = new DockPanel();
            StackPanel botoes = Theme.Buttons(exportar, fechar);
            DockPanel.SetDock(botoes, Dock.Right);
            rodape.Children.Add(botoes);
            rodape.Children.Add(soPendencias);

            _tabela = new FilterableGrid(resultados, typeof(CheckResult));
            _tabela.AddColumn("Especificação", nameof(CheckResult.Especificacao), 180, wrap: true);
            _tabela.AddColumn("Entidade", nameof(CheckResult.Entidade), 120);
            _tabela.AddColumn("Requisito", nameof(CheckResult.Requisito), 220, wrap: true);
            _tabela.AddColumn("Status", nameof(CheckResult.Status), 120);
            _tabela.AddColumn("Elementos", nameof(CheckResult.QtdElementos), 70);
            _tabela.AddColumn("Detalhe", nameof(CheckResult.Detalhe), 300, wrap: true, fill: true);
            _grid = _tabela.Grid;
            _grid.LoadingRow += (s, e) =>
            {
                var r = (CheckResult)e.Row.Item;
                e.Row.Background = r.Status == CheckStatus.OK ? Theme.LinhaNormal
                    : r.Status == CheckStatus.Aviso || r.Status == CheckStatus.NaoVerificado ? Theme.LinhaAviso
                    : Theme.LinhaProblema;
            };
            _grid.MouseDoubleClick += (s, e) =>
            {
                if (_grid.SelectedItem is CheckResult r && r.Elementos.Count > 0)
                {
                    _handler.Ids = r.Elementos;
                    _evento.Raise();
                }
            };
            Theme.StyleSelection(_grid);
            dock.Children.Add(_tabela);

            CheckBox umaLinha = _tabela.CreateSingleLineToggle();
            DockPanel.SetDock(umaLinha, Dock.Right);
            topo.Children.Insert(1, umaLinha);

            soPendencias.Click += (s, e) =>
            {
                _tabela.ExtraFilter = soPendencias.IsChecked == true
                    ? (Func<object, bool>)(o => ((CheckResult)o).Status != CheckStatus.OK)
                    : null;
                _tabela.Refresh();
            };
            exportar.Click += (s, e) => ExportarCsv();
            fechar.Click += (s, e) => Close();
            Closed += (s, e) => _evento.Dispose();

            Theme.Apply(this, "Mapeamento IDS", ids.Title, dock, rodape);
        }

        private void ExportarCsv()
        {
            var dialogo = new Microsoft.Win32.SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "verificacao_ids.csv" };
            if (dialogo.ShowDialog(this) != true) return;

            var sb = new StringBuilder();
            sb.AppendLine("Especificação;Entidade;Requisito;Status;Elementos;Detalhe;IDs");
            foreach (CheckResult r in _tabela.VisibleItems<CheckResult>())
            {
                sb.AppendLine(string.Join(";", new[]
                {
                    r.Especificacao, r.Entidade, r.Requisito, r.Status.ToString(), r.QtdElementos.ToString(), r.Detalhe,
                    string.Join(",", r.Elementos.Select(id => id.ToString())),
                }.Select(Csv)));
            }
            // BOM para o Excel abrir acentos corretamente
            File.WriteAllText(dialogo.FileName, sb.ToString(), new UTF8Encoding(true));
        }

        private static string Csv(string v) => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";

        private class SelecionarHandler : IExternalEventHandler
        {
            public ICollection<ElementId> Ids { get; set; }

            public void Execute(UIApplication app)
            {
                UIDocument uidoc = app.ActiveUIDocument;
                if (uidoc == null || Ids == null) return;
                uidoc.Selection.SetElementIds(Ids);
                uidoc.ShowElements(Ids);
            }

            public string GetName() => "MetroSP: selecionar elementos";
        }
    }
}
