using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MetroSP.Ui
{
    /// <summary>
    /// Regra da identidade: toda janela MetroSP abre no mesmo monitor do software (Revit ou Civil 3D),
    /// inclusive as de progresso. Este arquivo fica em comum/ e entra nos dois projetos como link.
    ///
    /// Como funciona (chamado pelo Theme.Apply, então vale para quase todas as janelas sem mexer em cada uma):
    ///  - Janela sem dono, na thread do software, passa a ter como dono a janela principal do software,
    ///    e "centro da tela" vira "centro do dono".
    ///  - Na hora de aparecer (WM_WINDOWPOSCHANGING com SWP_SHOWWINDOW, antes de ficar visível), se a janela
    ///    estiver em outro monitor que não o do dono (ou do software), é centralizada sobre o dono. Se já estiver
    ///    no monitor certo (ex.: posição salva), fica onde está; só é puxada para dentro da área útil se sobrar para fora.
    ///  - A janela de progresso roda em outra thread e não ganha dono (um dono em outra thread prenderia a
    ///    janela quando o software está ocupado). Ela só é posicionada no monitor do software.
    /// Tudo em pixels do Windows, por isso funciona com monitores de escalas diferentes.
    /// </summary>
    public static class JanelaNoMonitor
    {
        private static IntPtr _principal;
        private static readonly ConditionalWeakTable<Window, object> Preparadas = new ConditionalWeakTable<Window, object>();

        /// <summary>
        /// Janela principal do software. O add-in informa ao iniciar (Revit: UIControlledApplication.MainWindowHandle;
        /// Civil 3D: Application.MainWindow.Handle). Sem isso, usa a janela principal do processo.
        /// </summary>
        public static IntPtr Principal
        {
            get
            {
                if (_principal != IntPtr.Zero && IsWindow(_principal)) return _principal;
                try { return Process.GetCurrentProcess().MainWindowHandle; } catch { return IntPtr.Zero; }
            }
            set { _principal = value; }
        }

        /// <summary>
        /// Prepara a janela para abrir no monitor do software. Chamar antes de Show/ShowDialog (o Theme.Apply já chama).
        /// noAlto: em vez de centralizar, fica no alto do monitor, centralizada na horizontal (janela de progresso,
        /// para não cobrir os avisos que o software abre no centro).
        /// </summary>
        public static void Preparar(Window w, bool noAlto = false)
        {
            if (w == null) return;
            lock (Preparadas)
            {
                if (Preparadas.TryGetValue(w, out _)) return;
                Preparadas.Add(w, new object());
            }
            bool posicionada = false;

            w.SourceInitialized += (s, e) =>
            {
                try
                {
                    var interop = new WindowInteropHelper(w);
                    IntPtr principal = Principal;
                    bool mesmaThread = principal != IntPtr.Zero && GetWindowThreadProcessId(principal, out _) == GetCurrentThreadId();
                    if (w.Owner == null && interop.Owner == IntPtr.Zero && mesmaThread)
                        interop.Owner = principal;
                    if (w.WindowStartupLocation == WindowStartupLocation.CenterScreen && (w.Owner != null || interop.Owner != IntPtr.Zero))
                        w.WindowStartupLocation = WindowStartupLocation.CenterOwner;

                    HwndSource.FromHwnd(interop.Handle)?.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                    {
                        if (msg != WM_WINDOWPOSCHANGING || posicionada) return IntPtr.Zero;
                        try
                        {
                            var pos = (WINDOWPOS)Marshal.PtrToStructure(lParam, typeof(WINDOWPOS));
                            if ((pos.flags & SWP_SHOWWINDOW) == 0 || IsWindowVisible(hwnd)) return IntPtr.Zero;
                            posicionada = true;
                            if (Ajustar(hwnd, ref pos, noAlto)) Marshal.StructureToPtr(pos, lParam, false);
                        }
                        catch { }
                        return IntPtr.Zero;
                    });
                }
                catch { }
            };

            // Segurança: se a janela apareceu sem passar pelo ajuste acima, corrige assim que carrega.
            w.Loaded += (s, e) =>
            {
                if (posicionada) return;
                posicionada = true;
                try
                {
                    IntPtr hwnd = new WindowInteropHelper(w).Handle;
                    var pos = new WINDOWPOS { hwnd = hwnd, flags = SWP_NOMOVE | SWP_NOSIZE };
                    if (Ajustar(hwnd, ref pos, noAlto))
                        SetWindowPos(hwnd, IntPtr.Zero, pos.x, pos.y, pos.cx, pos.cy, (pos.flags & (SWP_NOMOVE | SWP_NOSIZE)) | SWP_NOZORDER | SWP_NOACTIVATE);
                }
                catch { }
            };
        }

        /// <summary>Calcula a posição final (em pixels). Devolve true se mudou algo em pos.</summary>
        private static bool Ajustar(IntPtr hwnd, ref WINDOWPOS pos, bool noAlto)
        {
            // Referência: o dono da janela (outra janela MetroSP ou o software); sem dono, o software.
            IntPtr alvo = GetWindow(hwnd, GW_OWNER);
            if (alvo == IntPtr.Zero || !IsWindowVisible(alvo) || IsIconic(alvo)) alvo = Principal;
            if (alvo == IntPtr.Zero || alvo == hwnd || IsIconic(alvo)) return false;

            IntPtr monitor = MonitorFromWindow(alvo, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return false;
            RECT area = info.rcWork;

            if (!GetWindowRect(hwnd, out RECT atual)) return false;
            int x = (pos.flags & SWP_NOMOVE) == 0 ? pos.x : atual.Left;
            int y = (pos.flags & SWP_NOMOVE) == 0 ? pos.y : atual.Top;
            int cx = (pos.flags & SWP_NOSIZE) == 0 ? pos.cx : atual.Right - atual.Left;
            int cy = (pos.flags & SWP_NOSIZE) == 0 ? pos.cy : atual.Bottom - atual.Top;
            int x0 = x, y0 = y, cx0 = cx, cy0 = cy;

            // Nunca maior que a área útil do monitor.
            cx = Math.Min(cx, area.Right - area.Left);
            cy = Math.Min(cy, area.Bottom - area.Top);

            var janela = new RECT { Left = x, Top = y, Right = x + cx, Bottom = y + cy };
            if (MonitorFromRect(ref janela, MONITOR_DEFAULTTONEAREST) != monitor)
            {
                // Em outro monitor: centraliza sobre a referência (ou no alto, para o progresso).
                GetWindowRect(alvo, out RECT r);
                x = r.Left + (r.Right - r.Left - cx) / 2;
                y = noAlto ? area.Top + (int)(60 * Escala(monitor)) : r.Top + (r.Bottom - r.Top - cy) / 2;
            }

            // Inteira dentro da área útil.
            x = Math.Max(area.Left, Math.Min(x, area.Right - cx));
            y = Math.Max(area.Top, Math.Min(y, area.Bottom - cy));

            bool moveu = x != x0 || y != y0, redimensionou = cx != cx0 || cy != cy0;
            if (!moveu && !redimensionou) return false;
            pos.x = x; pos.y = y; pos.cx = cx; pos.cy = cy;
            if (moveu) pos.flags &= ~SWP_NOMOVE;
            if (redimensionou) pos.flags &= ~SWP_NOSIZE;
            return true;
        }

        private static double Escala(IntPtr monitor)
        {
            try { if (GetDpiForMonitor(monitor, 0, out uint dx, out _) == 0 && dx > 0) return dx / 96.0; } catch { }
            return 1.0;
        }

        private const int WM_WINDOWPOSCHANGING = 0x0046;
        private const int SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;
        private const uint GW_OWNER = 4, MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy, flags; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public int dwFlags; }

        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, int flags);
        [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
    }
}
