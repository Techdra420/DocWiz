using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DocWiz.Cli;

/// <summary>
/// The native DocWiz application window: a WinForms <see cref="Form"/> hosting a WebView2
/// (the embeddable Edge/Chromium engine) that renders the localhost editor. The running app
/// IS DocWiz — own title bar, icon, and taskbar entry — instead of a separate Edge app-window
/// process (VISION L191: "DW app is not running, edge started").
///
/// The split the stakeholder asked for: the EDITOR lives in this window; the READ / present /
/// handover surfaces (everything the editor opens with <c>target="_blank"</c> /
/// <c>window.open</c>) open in the DEFAULT BROWSER. One signal does it —
/// <see cref="CoreWebView2.NewWindowRequested"/>.
///
/// WinForms lives here in the net8.0-windows CLI (alongside <see cref="Splash"/> /
/// <see cref="FolderPick"/>); DocWiz.App stays platform-neutral and receives
/// <see cref="TryLaunch"/> as the injected launchNativeWindow delegate.
/// </summary>
internal sealed class AppWindow : Form
{
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly string _url;
    private readonly string _appOrigin; // scheme+host+port of the localhost editor, e.g. http://127.0.0.1:5210
    private readonly Action _onPainted;
    private bool _painted;
    private bool _maximized; // last maximize state pushed to the web (dedupes Resize spam)

    private AppWindow(string url, Action onPainted, Action onClosed)
    {
        _url = url;
        _appOrigin = new Uri(url).GetLeftPart(UriPartial.Authority);
        _onPainted = onPainted;

        Text = "DocWiz";
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch { /* icon optional */ }
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1280, 860); // restored size when un-maximized
        WindowState = FormWindowState.Maximized;
        BackColor = Color.FromArgb(0x1B, 0x1D, 0x21); // brand dark behind the webview during load
        AutoScaleMode = AutoScaleMode.Dpi;

        // Custom title bar: drop the native frame so the DocWiz header IS the title bar (the
        // min/max/close buttons live in the web header and message us back). We re-add what the
        // frame gave for free — edge-resize + Aero snap via WndProc, drag via the web header,
        // maximize that respects the taskbar (WM_GETMINMAXINFO). A thin form padding strip is
        // the resize grab area (the WebView2 child fills the rest and would otherwise eat the
        // hit-test); it collapses to 0 when maximized.
        FormBorderStyle = FormBorderStyle.None;
        Controls.Add(_web);
        Load += async (_, _) => await InitWebViewAsync();
        FormClosed += (_, _) => { try { onClosed(); } catch { /* never let teardown throw */ } };
        Resize += (_, _) =>
        {
            var maximized = WindowState == FormWindowState.Maximized;
            var pad = maximized ? 0 : ResizeBorder;
            if (Padding.Left != pad) Padding = new Padding(pad);
            // Only push the maximize/restore icon when it actually flips — not on every
            // edge-resize tick (Resize fires continuously while dragging an edge).
            if (maximized != _maximized) { _maximized = maximized; PostWindowState(); }
        };
    }

    private async Task InitWebViewAsync()
    {
        // Own user-data dir (mirrors the Edge app-window profile isolation); nothing fetched —
        // WebView2 only renders the localhost editor.
        var userData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DocWiz", "webview2");
        // Seed offline spellcheck in this WebView2 profile (same Chromium profile layout as the
        // Edge app-window): without it a fresh profile defaults to the online Microsoft Editor
        // service — squiggles but no right-click suggestions when offline (the L182 dogfood fix).
        DocWiz.App.BrowserLauncher.EnsureOfflineSpellcheck(userData);
        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
        await _web.EnsureCoreWebView2Async(env);

        var core = _web.CoreWebView2;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = true; // keep right-click → offline spellcheck suggestions
        core.Settings.IsZoomControlEnabled = false; // our content-only zoom owns Ctrl+wheel / Ctrl +/-

        // Custom title bar: the web header's min/max/close buttons + a drag on its empty areas
        // post these back (only in the native window — the same page in a browser tab ignores it).
        core.WebMessageReceived += (_, e) =>
        {
            string msg;
            try { msg = e.TryGetWebMessageAsString(); } catch { return; }
            switch (msg)
            {
                case "win:minimize": WindowState = FormWindowState.Minimized; break;
                case "win:toggle-maximize": ToggleMaximize(); break;
                case "win:drag":
                    // Hand the move to the OS so Aero snap / multi-monitor work; must be on the UI thread.
                    ReleaseCapture();
                    SendMessage(Handle, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
                    break;
            }
        };

        // _blank / window.open (chapter Read, Read/present deck, handover, attachment "open ↗")
        // → the DEFAULT BROWSER. The editor shell never opens a new window for itself, so a
        // _blank can only ever leave the app — open it whatever the origin (handover/read are
        // SAME-origin localhost). Gate on scheme only (http/https), never blindly shell-execute
        // a file:/javascript:/custom-scheme _blank (security allowlist — review I7).
        core.NewWindowRequested += (_, e) => { e.Handled = true; if (IsHttpUrl(e.Uri)) OpenExternally(e.Uri); };

        // Keep the app pinned to its own UI: a top-level http(s) navigation to another origin
        // (a stray external link that is not _blank) → browser. about:/edge:/data: are left alone.
        core.NavigationStarting += (_, e) =>
        {
            if (IsExternalHttp(e.Uri))
            {
                e.Cancel = true;
                OpenExternally(e.Uri);
            }
        };

        // Editor content actually rendered → dismiss the startup splash (once).
        core.NavigationCompleted += (_, _) =>
        {
            PostWindowState(); // sync the maximize/restore icon (also after a reload)
            if (_painted) return;
            _painted = true;
            try { _onPainted(); } catch { /* splash dismissal is best-effort */ }
        };

        core.Navigate(_url);
    }

    private static bool IsHttpUrl(string uri) =>
        uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private bool IsExternalHttp(string uri) =>
        IsHttpUrl(uri) && !uri.StartsWith(_appOrigin, StringComparison.OrdinalIgnoreCase);

    private static void OpenExternally(string uri)
    {
        // A read/handover that fails to open externally must never crash the editor.
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { /* best effort */ }
    }

    // --- Custom title bar plumbing: edge-resize, taskbar-aware maximize, window-state sync ---

    private const int WmNcHitTest = 0x0084;
    private const int WmNcLButtonDown = 0x00A1;
    private const int WmGetMinMaxInfo = 0x0024;
    private const int MonitorDefaultToNearest = 0x00000002;
    private const int HtCaption = 2, HtLeft = 10, HtRight = 11, HtTop = 12,
        HtTopLeft = 13, HtTopRight = 14, HtBottom = 15, HtBottomLeft = 16, HtBottomRight = 17;

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor; public NativeRect Work; public int Flags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo { public Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }

    /// <summary>Width of the invisible edge strip that grabs a resize (DPI-scaled).</summary>
    private int ResizeBorder => Math.Max(4, (int)(6 * DeviceDpi / 96.0));

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WmGetMinMaxInfo:
                // A borderless maximized window covers the taskbar unless we clamp it to the
                // monitor's WORK area.
                var mi = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (GetMonitorInfo(MonitorFromWindow(m.HWnd, MonitorDefaultToNearest), ref mi))
                {
                    var mmi = Marshal.PtrToStructure<MinMaxInfo>(m.LParam);
                    mmi.MaxPosition = new Point(mi.Work.Left - mi.Monitor.Left, mi.Work.Top - mi.Monitor.Top);
                    mmi.MaxSize = new Point(mi.Work.Right - mi.Work.Left, mi.Work.Bottom - mi.Work.Top);
                    mmi.MinTrackSize = new Point(820, 520);
                    Marshal.StructureToPtr(mmi, m.LParam, false);
                }
                return;

            case WmNcHitTest:
                var hit = HitTestResizeBorders(m.LParam);
                if (hit != 0) { m.Result = (IntPtr)hit; return; }
                break;
        }
        base.WndProc(ref m);
    }

    /// <summary>Maps a point on the thin padding strip to a resize edge/corner so the OS resizes
    /// the window (with snap). Returns 0 (→ default client hit) inside, or when maximized.</summary>
    private int HitTestResizeBorders(IntPtr lParam)
    {
        if (WindowState == FormWindowState.Maximized)
            return 0;
        var b = ResizeBorder;
        var p = PointToClient(new Point(unchecked((short)(long)lParam), unchecked((short)((long)lParam >> 16))));
        bool left = p.X <= b, right = p.X >= ClientSize.Width - b, top = p.Y <= b, bottom = p.Y >= ClientSize.Height - b;
        if (top && left) return HtTopLeft;
        if (top && right) return HtTopRight;
        if (bottom && left) return HtBottomLeft;
        if (bottom && right) return HtBottomRight;
        if (left) return HtLeft;
        if (right) return HtRight;
        if (top) return HtTop;
        if (bottom) return HtBottom;
        return 0;
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;

    /// <summary>Tells the web header whether the window is maximized, so it shows the maximize vs
    /// restore icon.</summary>
    private void PostWindowState()
    {
        var state = WindowState == FormWindowState.Maximized ? "win-state:maximized" : "win-state:normal";
        try { _web.CoreWebView2?.PostWebMessageAsString(state); } catch { /* webview not ready yet */ }
    }

    /// <summary>Opens the editor in a native DocWiz window on its own STA thread (WebView2 needs a
    /// message pump). Returns a CLOSER — call it from any thread to close the window (e.g. when the
    /// server stops via the Quit button) — or <c>null</c> when the WebView2 Runtime is unavailable,
    /// so the caller falls back to the Edge app-window. <paramref name="onPainted"/> fires when the
    /// editor first renders; <paramref name="onClosed"/> when the window is closed (the caller then
    /// stops the server).</summary>
    public static Action? TryLaunch(string url, Action onPainted, Action onClosed)
    {
        // Pre-check: no WebView2 Runtime (or the native loader can't load) → signal fallback
        // before we commit to a native window, so the user always gets an editor.
        try
        {
            if (string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString()))
                return null;
        }
        catch
        {
            return null;
        }

        AppWindow? window = null;
        var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                // A WebView2 failure must mean "no native window", never a process crash
                // (mirrors Splash's guarding).
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (_, _) => { };
                window = new AppWindow(url, onPainted, onClosed);
                window.Shown += (_, _) => ready.Set();
                Application.Run(window);
            }
            catch
            {
                ready.Set();
                try { onClosed(); } catch { /* ignore */ }
            }
        })
        {
            IsBackground = true, // never let the UI thread keep the process alive after Main returns
            Name = "DocWiz app window",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait(8000); // return once the window is on screen (cap so a stuck launch never hangs)

        return () =>
        {
            var w = window;
            if (w is null)
                return;
            try
            {
                if (w.IsHandleCreated && !w.IsDisposed)
                    w.BeginInvoke(new Action(w.Close));
            }
            catch
            {
                // Already gone — nothing to close.
            }
        };
    }
}
