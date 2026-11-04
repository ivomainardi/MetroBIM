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

            Title = $"MetroSP - Padrões Metrô: {ids.Title}";
            Width = 1100;
            Height = 600;
            MinWidth = 700;
            MinHeight = 300;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var dock = new DockPanel();

            int fora = resultados.Where(r => r.Status == CheckStatus.ForaDoIfc).Sum(r => r.QtdElementos);
            int verificacoes = resultados.Count(r => r.Status != CheckStatus.ForaDoIfc);
            int ok = resultados.Count(r => r.Status == CheckStatus.OK);
            int avisos = resultados.Count(r => r.Status == CheckStatus.Aviso || r.Status == CheckStatus.NaoVerificado);
            int problemas = verificacoes - ok - avisos;
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
            chips.Children.Add(Theme.Chip($"{verificacoes} verificações"));
            chips.Children.Add(Theme.Chip($"{ok} OK"));
            chips.Children.Add(Theme.Chip($"{problemas} problemas", destaque: problemas > 0));
            chips.Children.Add(Theme.Chip($"{avisos} avisos", escuro: avisos > 0));
            if (fora > 0)
            {
                Border chipFora = Theme.Chip($"{fora} fora do IFC");
                chipFora.ToolTip = "Elementos que não vão para o IFC e por isso não entram na verificação:\n" +
                    "opção de projeto secundária, demolidos ou de fase posterior, sem geometria 3D\n" +
                    "ou com \"Exportar para IFC = Não\". Estão nas linhas \"(fora do IFC)\", no fim da lista.";
                chips.Children.Add(chipFora);
            }
            topo.Children.Add(chips);
            DockPanel.SetDock(topo, Dock.Top);
            dock.Children.Add(topo);

            var soPendencias = new CheckBox { Content = "Mostrar só pendências", VerticalAlignment = VerticalAlignment.Center };
            var selecionado = new TextBlock
            {
                Foreground = Theme.Solid(Theme.Cinza),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(16, 0, 8, 0),
                ToolTip = "O que o último duplo clique selecionou no Revit (categoria, família e tipo).",
            };
            _handler.Mostrar = texto => Dispatcher.BeginInvoke(new Action(() => selecionado.Text = texto));
            var exportar = new Button { Content = "Exportar CSV..." };
            var fechar = new Button { Content = "Fechar", Style = Theme.PrimaryButton };
            var rodape = new DockPanel();
            StackPanel botoes = Theme.Buttons(exportar, fechar);
            DockPanel.SetDock(botoes, Dock.Right);
            rodape.Children.Add(botoes);
            DockPanel.SetDock(soPendencias, Dock.Left);
            rodape.Children.Add(soPendencias);
            rodape.Children.Add(selecionado);

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
                e.Row.Background = r.Status == CheckStatus.OK || r.Status == CheckStatus.ForaDoIfc ? Theme.LinhaNormal
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
                    ? (Func<object, bool>)(o => ((CheckResult)o).Status != CheckStatus.OK && ((CheckResult)o).Status != CheckStatus.ForaDoIfc)
                    : null;
                _tabela.Refresh();
            };
            exportar.Click += (s, e) => ExportarCsv();
            fechar.Click += (s, e) => Close();
            Closed += (s, e) => _evento.Dispose();

            Theme.Apply(this, "Padrões Metrô", ids.Title, dock, rodape);
        }

        private void ExportarCsv()
        {
            var dialogo = new Microsoft.Win32.SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "verificacao_padroes_metro.csv" };
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
            public Action<string> Mostrar { get; set; }

            public void Execute(UIApplication app)
            {
                UIDocument uidoc = app.ActiveUIDocument;
                if (uidoc == null || Ids == null) return;
                uidoc.Selection.SetElementIds(Ids);
                try { uidoc.ShowElements(Ids); } catch { /* elemento sem vista que o mostre */ }
                Mostrar?.Invoke(Descrever(uidoc.Document, Ids));
            }

            /// <summary>"Selecionado: 1 · Pisos · Laje: Lastro 5 cm (id 123456)" ou o resumo por categoria.</summary>
            private static string Descrever(Document doc, ICollection<ElementId> ids)
            {
                List<Element> elems = ids.Select(doc.GetElement).Where(e => e != null).ToList();
                if (elems.Count == 0) return "Selecionado: nenhum elemento encontrado no modelo.";
                if (elems.Count == 1)
                {
                    Element e = elems[0];
                    string familiaTipo = (doc.GetElement(e.GetTypeId()) as ElementType) is ElementType t
                        ? $"{t.FamilyName}: {t.Name}" : e.Name;
                    return $"Selecionado: 1 · {e.Category?.Name} · {familiaTipo} (id {e.Id})";
                }
                string cats = string.Join(", ", elems.GroupBy(e => e.Category?.Name ?? "sem categoria")
                    .OrderByDescending(g => g.Count()).Take(4).Select(g => $"{g.Count()} {g.Key}"));
                return $"Selecionado: {elems.Count} · {cats}";
            }

            public string GetName() => "MetroSP: selecionar elementos";
        }
    }
}
