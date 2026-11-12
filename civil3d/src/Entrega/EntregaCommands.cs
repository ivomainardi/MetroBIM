using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Autodesk.AutoCAD.Runtime;
using PaletteSet = Autodesk.AutoCAD.Windows.PaletteSet;
using PaletteSetStyles = Autodesk.AutoCAD.Windows.PaletteSetStyles;
using MetroSP.Ui;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDocument = Autodesk.AutoCAD.ApplicationServices.Document;

[assembly: CommandClass(typeof(MetroSP.Entrega.EntregaCommands))]

namespace MetroSP.Entrega
{
    public class EntregaCommands
    {
        /// <summary>Etapa pedida pelo Painel MetroSP (0 = a primeira com pendência).</summary>
        internal static int PassoPedido;

        /// <summary>Passo 1: janela única da entrega.</summary>
        [CommandMethod("MSP_ENTREGA", CommandFlags.Modal | CommandFlags.Redraw)]
        public void Entrega()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            int passo = PassoPedido;
            PassoPedido = 0;
            try { EntregaWindow.Abrir(doc, passo); }
            catch (System.Exception ex)
            {
                var log = Log.Erro("Entrega passo a passo", ex);
                doc.Editor.WriteMessage("\nMetroSP: ERRO - " + ex.Message + (log != null ? $" (detalhes em {log})" : ""));
            }
        }

        /// <summary>Painel MetroSP: as etapas e o semáforo, ao lado do desenho.</summary>
        [CommandMethod("MSP_PAINEL")]
        public void Painel() => PainelMetroSP.Mostrar();

        /// <summary>Autodesk Forma: entrar, sair, sincronizar e conta numa janela só.</summary>
        [CommandMethod("MSP_FORMA")]
        public void Forma()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            string cmd = FormaWindow.Escolher(null);
            if (cmd != null) doc?.SendStringToExecute("_" + cmd + " ", true, false, true);
        }
    }

    /// <summary>Os quatro botões do Forma, que antes ocupavam um painel da Ribbon.</summary>
    public static class FormaWindow
    {
        /// <summary>Devolve o comando escolhido (ou null). O comando roda depois que as janelas fecham.</summary>
        public static string Escolher(Window dono)
        {
            var cfg = Settings.Load();
            string escolhido = null;
            var w = new Window { Title = "MetroSP - Forma", Width = 440, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var corpo = new StackPanel();
            corpo.Children.Add(Theme.Selo(cfg.UsesForma ? "Configuração IFC vem do Forma" : "Configuração IFC local", cfg.UsesForma ? Theme.Estado.Ok : Theme.Estado.Neutro));
            if (cfg.UsesForma) corpo.Children.Add(Theme.Detalhe("Projeto: " + cfg.ProjectId));
            var grade = new WrapPanel();
            void Opcao(string texto, string dica, string comando)
            {
                var b = EntregaWindow.Pilula(texto, primario: comando == "MSP_IFCSYNC" && cfg.UsesForma);
                b.ToolTip = dica;
                b.Margin = new Thickness(0, 10, 8, 0);
                b.Click += (s, e) => { escolhido = comando; w.Close(); };
                grade.Children.Add(b);
            }
            Opcao("Sincronizar", "Baixa a configuração IFC do Forma, sem exportar.", "MSP_IFCSYNC");
            Opcao("Entrar", "Entra na conta Autodesk (o navegador pode abrir).", "MSP_FORMALOGIN");
            Opcao("Sair", "Encerra a sessão neste computador.", "MSP_FORMALOGOUT");
            Opcao("Conta", "Client ID, projeto e pasta do Forma (abre o arquivo de configuração).", "MSP_CONFIG");
            corpo.Children.Add(grade);
            var fechar = new Button { Content = "Fechar", IsCancel = true };
            fechar.Click += (s, e) => w.Close();
            Theme.Apply(w, "Autodesk Forma", "Configuração IFC compartilhada pela equipe", corpo, Theme.Buttons(fechar));
            if (dono != null) { w.Owner = dono; w.ShowDialog(); }
            else AcApp.ShowModalWindow(AcApp.MainWindow.Handle, w, false);
            return escolhido;
        }

    }

    /// <summary>
    /// Painel encaixável (PaletteSet) com as 7 etapas da entrega e o semáforo da última conferência do desenho ativo.
    /// Clique numa etapa para abrir a Entrega passo a passo nela.
    /// </summary>
    public static class PainelMetroSP
    {
        static PaletteSet _ps;
        static StackPanel _etapas;
        static TextBlock _desenho;
        static TextBlock _quando;

        public static void Mostrar()
        {
            if (_ps == null) Criar();
            _ps.Visible = true;
            Atualizar();
        }

        static void Criar()
        {
            _ps = new PaletteSet("MetroSP", "MSP_PAINEL", new Guid("6B0C3E52-7C1F-4B39-9E0D-3F2A6D1E8A41"))
            {
                MinimumSize = new System.Drawing.Size(240, 360),
                Style = PaletteSetStyles.ShowCloseButton | PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.ShowPropertiesMenu,
            };
            var raiz = new DockPanel { Background = Brushes.White };
            var faixa = new Border { Background = Theme.Solid(Theme.Preto), Padding = new Thickness(12, 8, 12, 8) };
            var marca = new TextBlock { Foreground = Brushes.White, FontSize = 13 };
            marca.Inlines.Add(new System.Windows.Documents.Run("MetroSP") { Foreground = Theme.Solid(Theme.Laranja), FontWeight = FontWeights.SemiBold });
            marca.Inlines.Add(new System.Windows.Documents.Run(" · Entrega"));
            faixa.Child = marca;
            DockPanel.SetDock(faixa, Dock.Top);
            raiz.Children.Add(faixa);

            var topo = new StackPanel { Margin = new Thickness(12, 10, 12, 4) };
            _desenho = new TextBlock { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            _quando = new TextBlock { Foreground = Theme.Solid(Theme.Cinza), FontSize = 11 };
            topo.Children.Add(_desenho);
            topo.Children.Add(_quando);
            DockPanel.SetDock(topo, Dock.Top);
            raiz.Children.Add(topo);

            var botoes = new WrapPanel { Margin = new Thickness(12, 8, 12, 12) };
            var conferir = EntregaWindow.Pilula("⟳ Conferir", primario: true);
            conferir.ToolTip = "Confere o desenho ativo nas 7 etapas.";
            conferir.Margin = new Thickness(0, 0, 6, 4);
            conferir.Click += (s, e) => Conferir();
            var abrir = EntregaWindow.Pilula("Abrir a entrega");
            abrir.ToolTip = "Abre a Entrega passo a passo na primeira etapa com pendência.";
            abrir.Margin = new Thickness(0, 0, 6, 4);
            abrir.Click += (s, e) => AbrirEtapa(0);
            botoes.Children.Add(conferir);
            botoes.Children.Add(abrir);
            DockPanel.SetDock(botoes, Dock.Bottom);
            raiz.Children.Add(botoes);

            _etapas = new StackPanel { Margin = new Thickness(8, 4, 8, 4) };
            raiz.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _etapas });
            System.Windows.Documents.TextElement.SetFontFamily(raiz, new FontFamily("Segoe UI"));
            System.Windows.Documents.TextElement.SetFontSize(raiz, 12);

            _ps.AddVisual("Entrega", raiz);
            ConferenciaEntrega.Conferido += r => Atualizar();
            AcApp.DocumentManager.DocumentActivated += (s, e) => Atualizar();
        }

        static void Conferir()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                using (doc.LockDocument()) ConferenciaEntrega.Executar(doc);
            }
            catch (System.Exception ex)
            {
                Log.Erro("conferência pelo painel", ex);
                MessageBox.Show("Não foi possível conferir:\n" + ex.Message, "MetroSP");
            }
            finally { Mouse.OverrideCursor = null; }
            Atualizar();
        }

        static void AbrirEtapa(int n)
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            EntregaCommands.PassoPedido = n;
            doc.SendStringToExecute("\x03\x03_MSP_ENTREGA ", true, false, true);
        }

        static void Atualizar()
        {
            if (_ps == null || _etapas == null) return;
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            _desenho.Text = doc == null ? "Nenhum desenho aberto" : System.IO.Path.GetFileNameWithoutExtension(doc.Name);
            var r = ConferenciaEntrega.Ultima(doc);
            _quando.Text = r == null ? "Ainda não conferido. Clique em Conferir." : $"Conferido às {r.Quando:HH:mm}";
            _etapas.Children.Clear();
            foreach (var (n, nome, dica) in ConferenciaEntrega.Etapas)
            {
                var e = r?.Etapa(n);
                int qtd = e?.Abertas.Count ?? 0;
                Sinal sinal = e == null ? Sinal.Cinza : n == ResultadoEntrega.UltimaEtapa && qtd == 0 ? Sinal.Cinza : e.Sinal;
                Color cor = sinal == Sinal.Verde ? Theme.Verde : sinal == Sinal.Vermelho ? Color.FromRgb(0xB2, 0x22, 0x22)
                    : sinal == Sinal.Amarelo ? Theme.Laranja : Color.FromRgb(0xCC, 0xCC, 0xCC);
                var bola = new Grid { Width = 22, Height = 22, Margin = new Thickness(0, 0, 8, 0) };
                bola.Children.Add(new Ellipse { Fill = sinal == Sinal.Verde ? Theme.Solid(cor) : Brushes.White, Stroke = Theme.Solid(cor), StrokeThickness = 2 });
                bola.Children.Add(new TextBlock
                {
                    Text = sinal == Sinal.Verde ? "✓" : n.ToString(),
                    Foreground = sinal == Sinal.Verde ? Brushes.White : Theme.Solid(Theme.Preto),
                    FontSize = 11, FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                });
                var linha = new DockPanel { Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(0, 3, 0, 3), ToolTip = dica + (e?.Resumo != null ? "\n" + e.Resumo : "") };
                DockPanel.SetDock(bola, Dock.Left);
                linha.Children.Add(bola);
                if (qtd > 0)
                {
                    var q = new TextBlock { Text = qtd.ToString(), Foreground = Theme.Solid(Theme.LaranjaTexto), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
                    DockPanel.SetDock(q, Dock.Right);
                    linha.Children.Add(q);
                }
                linha.Children.Add(new TextBlock { Text = nome, VerticalAlignment = VerticalAlignment.Center });
                int numero = n;
                var item = new Border { Padding = new Thickness(6, 2, 6, 2), CornerRadius = new CornerRadius(4), Child = linha };
                item.MouseEnter += (s, a) => item.Background = Theme.Solid(Theme.LaranjaSelecao);
                item.MouseLeave += (s, a) => item.Background = Brushes.Transparent;
                item.MouseLeftButtonUp += (s, a) => AbrirEtapa(numero);
                _etapas.Children.Add(item);
            }
        }
    }
}
