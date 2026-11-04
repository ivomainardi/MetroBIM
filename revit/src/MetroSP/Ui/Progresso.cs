using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace MetroSP.Ui
{
    /// <summary>
    /// Janela de progresso para operações longas (abrir e recarregar famílias, gravar em muitos elementos, exportar).
    ///
    /// O Revit fica ocupado durante a operação e não redesenha as janelas dele, por isso esta roda numa thread
    /// própria: a barra, a etapa, o item atual e o tempo continuam se mexendo. Os métodos podem ser chamados
    /// da thread do Revit. Uso:
    ///   using (var p = Progresso.Abrir("Renomear parâmetro", podeCancelar: true)) { p.Etapa("...", total); p.Avancar("item"); }
    /// </summary>
    public sealed class Progresso : IDisposable
    {
        private readonly ManualResetEventSlim _pronta = new ManualResetEventSlim(false);
        private Dispatcher _ui;
        private Window _janela;
        private TextBlock _etapa, _item, _contagem, _tempo;
        private ProgressBar _barra;
        private ListBox _log;
        private Button _cancelar;
        private readonly Stopwatch _relogio = Stopwatch.StartNew();
        private DispatcherTimer _timer;
        private volatile bool _cancelado;
        private int _total, _feitos;

        private long _ultimoEnvio;
        private string _itemPendente;

        /// <summary>
        /// A janela aberta no momento, para rotinas internas (ex.: ler a fórmula de uma família) informarem o que fazem
        /// sem receber a janela por parâmetro. null quando não há operação em andamento.
        /// </summary>
        public static Progresso Atual { get; private set; }

        /// <summary>true depois que o usuário clicou em Cancelar.</summary>
        public bool Cancelado => _cancelado;

        private Progresso() { }

        public static Progresso Abrir(string titulo, bool podeCancelar = false)
        {
            var p = new Progresso();
            var t = new Thread(() => p.Rodar(titulo, podeCancelar)) { IsBackground = true, Name = "MetroSP progresso" };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            p._pronta.Wait(3000);
            Atual = p;
            return p;
        }

        private void Rodar(string titulo, bool podeCancelar)
        {
            try
            {
                _ui = Dispatcher.CurrentDispatcher;
                // Cores da identidade (iguais às do Theme). Não usa o Theme aqui: os estilos dele pertencem à thread do Revit.
                Brush laranja = new SolidColorBrush(Color.FromRgb(0xFF, 0x99, 0x00)), cinza = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A)),
                      preto = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)), cinzaClaro = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6));

                var raiz = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
                raiz.Children.Add(new TextBlock { Text = titulo, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = preto });
                _etapa = new TextBlock { Text = "Preparando...", Margin = new Thickness(0, 8, 0, 0), FontSize = 13, Foreground = preto, TextWrapping = TextWrapping.Wrap };
                raiz.Children.Add(_etapa);
                _barra = new ProgressBar { Height = 14, Margin = new Thickness(0, 8, 0, 0), IsIndeterminate = true, Foreground = laranja, Background = cinzaClaro, BorderThickness = new Thickness(0) };
                raiz.Children.Add(_barra);

                var linha = new DockPanel { Margin = new Thickness(0, 4, 0, 0), LastChildFill = false };
                _contagem = new TextBlock { Foreground = cinza, FontSize = 11 };
                _tempo = new TextBlock { Foreground = cinza, FontSize = 11 };
                DockPanel.SetDock(_contagem, Dock.Left);
                DockPanel.SetDock(_tempo, Dock.Right);
                linha.Children.Add(_contagem);
                linha.Children.Add(_tempo);
                raiz.Children.Add(linha);

                _item = new TextBlock { Margin = new Thickness(0, 6, 0, 0), Foreground = cinza, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
                raiz.Children.Add(_item);
                _log = new ListBox { Height = 120, Margin = new Thickness(0, 8, 0, 0), FontSize = 11, BorderBrush = cinzaClaro, Foreground = preto };
                raiz.Children.Add(_log);

                _cancelar = new Button
                {
                    Content = "Cancelar", Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 10, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Right, Visibility = podeCancelar ? Visibility.Visible : Visibility.Collapsed,
                };
                _cancelar.Click += (s, e) =>
                {
                    _cancelado = true;
                    _cancelar.IsEnabled = false;
                    _cancelar.Content = "Cancelando...";
                };
                raiz.Children.Add(_cancelar);

                _janela = new Window
                {
                    Title = "MetroSP - " + titulo,
                    Content = raiz,
                    Width = 480,
                    MinWidth = 360,
                    SizeToContent = SizeToContent.Height,
                    ResizeMode = ResizeMode.CanResizeWithGrip,
                    // No alto da tela e sempre à frente, mas sem cobrir o centro, onde o Revit abre os avisos
                    // dele (um aviso escondido atrás desta janela pareceria um travamento).
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = (SystemParameters.WorkArea.Width - 480) / 2 + SystemParameters.WorkArea.Left,
                    Top = SystemParameters.WorkArea.Top + 60,
                    Topmost = true,
                    ShowInTaskbar = false,
                    Background = Brushes.White,
                };
                // Fechar pelo X equivale a cancelar, quando permitido; a janela some só no fim da operação.
                _janela.Closing += (s, e) =>
                {
                    if (_janela.Tag == null) { e.Cancel = true; if (podeCancelar) { _cancelado = true; _cancelar.IsEnabled = false; _cancelar.Content = "Cancelando..."; } }
                };
                _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (s, e) => AtualizarTempo(), _ui);
                _janela.Show();
            }
            finally
            {
                _pronta.Set();
            }
            Dispatcher.Run();
        }

        private void AtualizarTempo()
        {
            TimeSpan t = _relogio.Elapsed;
            string texto = $"{(int)t.TotalMinutes}:{t.Seconds:00}";
            if (_total > 0 && _feitos > 0 && _feitos < _total)
            {
                TimeSpan resta = TimeSpan.FromTicks(t.Ticks / _feitos * (_total - _feitos));
                texto += $"  ·  faltam ~{(int)resta.TotalMinutes}:{resta.Seconds:00}";
            }
            _tempo.Text = texto;
        }

        private void Ui(Action a)
        {
            if (_ui == null || _ui.HasShutdownStarted) return;
            try { _ui.BeginInvoke(a); } catch { /* janela já fechada */ }
        }

        /// <summary>Começa uma etapa. total &gt; 0 mostra a barra com contagem; 0 deixa a barra em movimento contínuo.</summary>
        public void Etapa(string texto, int total = 0)
        {
            _total = total;
            _feitos = 0;
            _itemPendente = null;
            Hora(forcar: true);
            Ui(() =>
            {
                _etapa.Text = texto;
                _item.Text = "";
                _barra.IsIndeterminate = total <= 0;
                _barra.Maximum = Math.Max(total, 1);
                _barra.Value = 0;
                _contagem.Text = total > 0 ? $"0 de {total}" : "";
                Registrar(texto, destaque: true);
            });
        }

        /// <summary>Mostra o item em andamento sem contar como feito.</summary>
        public void Item(string texto)
        {
            _itemPendente = texto;
            if (!Hora()) return;
            Ui(() => _item.Text = _itemPendente ?? "");
        }

        // Limita as atualizações da tela a ~15 por segundo: em laços de milhares de elementos,
        // mandar uma por elemento atrasaria a própria operação.
        private bool Hora(bool forcar = false)
        {
            long agora = _relogio.ElapsedMilliseconds;
            if (!forcar && agora - Interlocked.Read(ref _ultimoEnvio) < 66) return false;
            Interlocked.Exchange(ref _ultimoEnvio, agora);
            return true;
        }

        /// <summary>Conta um item como feito e mostra o próximo (ou o que acabou de terminar).</summary>
        public void Avancar(string texto = null)
        {
            int feitos = Interlocked.Increment(ref _feitos);
            int total = _total;
            if (texto != null) _itemPendente = texto;
            if (!Hora(forcar: total > 0 && feitos >= total)) return;
            Ui(() =>
            {
                if (total > 0)
                {
                    _barra.Value = Math.Min(feitos, total);
                    _contagem.Text = $"{Math.Min(feitos, total)} de {total}";
                }
                if (_itemPendente != null) _item.Text = _itemPendente;
            });
        }

        /// <summary>Acrescenta uma linha ao histórico da janela (ex.: uma falha ou um resultado parcial).</summary>
        public void Log(string texto) => Ui(() => Registrar(texto, destaque: false));

        private void Registrar(string texto, bool destaque)
        {
            var linha = new ListBoxItem { Content = $"{_relogio.Elapsed:m\\:ss}  {texto}" };
            if (destaque) linha.FontWeight = FontWeights.SemiBold;
            _log.Items.Add(linha);
            if (_log.Items.Count > 300) _log.Items.RemoveAt(0);
            _log.ScrollIntoView(linha);
        }

        /// <summary>Lança OperationCanceledException se o usuário pediu para cancelar.</summary>
        public void ParaSeCancelado()
        {
            if (_cancelado) throw new OperationCanceledException("Cancelado pelo usuário.");
        }

        public void Dispose()
        {
            if (Atual == this) Atual = null;
            Dispatcher ui = _ui;
            if (ui == null || ui.HasShutdownStarted) return;
            try
            {
                ui.Invoke(() =>
                {
                    _timer?.Stop();
                    if (_janela != null) { _janela.Tag = "fim"; _janela.Close(); }
                });
                ui.BeginInvokeShutdown(DispatcherPriority.Normal);
            }
            catch { /* thread já encerrada */ }
        }
    }
}
