using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DocWiz.Cli;

/// <summary>
/// Native folder picker, run in a hidden `__pickfolder` helper process and launched by
/// the server via <see cref="PickViaHelper"/>.
///
/// Showing the dialog in front is the hard part: Windows only lets a process call
/// SetForegroundWindow if it (roughly) owns the current foreground, which a
/// server-launched background process does not — so the dialog opens behind the browser.
/// The chromeless Edge `--app` window (added 2026-06-14) broke the original fix, whose
/// premise was that a freshly-spawned helper process gets foreground rights; against a
/// browser tab that held, against the app window it did not.
///
/// The fix that works (measured: the dialog ends up WS_EX_TOPMOST and ahead of every Edge
/// window in the Z-order) is three layers combined:
///   (1) the load-bearing one — a watcher forces EVERY visible top-level window this helper
///       shows to HWND_TOPMOST; a topmost window sits above all non-topmost windows (the
///       Edge `--app` window is not topmost) regardless of foreground rights. An earlier
///       attempt only forced the single front-most window.
///   (2) own the dialog by the window the user clicked "Open" in (GetForegroundWindow,
///       captured before we create any window of our own) so it also centers on the app.
///   (3) zero the system foreground-lock timeout for the duration
///       (SPI_SETFOREGROUNDLOCKTIMEOUT) + the AttachThreadInput dance, so the topmost
///       window also takes focus rather than only flashing the taskbar.
/// A taskbar/Alt-Tab owner form is created only when there is no foreground window to own
/// by, as a manual fallback. Running in a separate process keeps all of this off the
/// server thread (and ended the zombie/exe-lock problem).
/// </summary>
public static class FolderPick
{
    public static string? PickViaHelper(string? initialDir)
    {
        var exe = Environment.ProcessPath;
        if (exe is null)
            return null;

        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("__pickfolder");
        if (!string.IsNullOrWhiteSpace(initialDir))
            psi.ArgumentList.Add(initialDir);

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return null;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(300_000);
            var path = output.Trim();
            return path.Length > 0 ? path : null;
        }
        catch
        {
            return null;
        }
    }

    public static int RunHelper(string[] args)
    {
        var initialDir = args.Length > 1 ? args[1] : null;
        string? picked = null;

        var thread = new Thread(() => picked = ShowDialog(initialDir));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (picked is not null)
            Console.Out.Write(picked);
        return 0;
    }

    private static string? ShowDialog(string? initialDir)
    {
        // Without this, SetForegroundWindow from a non-foreground process only flashes the
        // taskbar; the dialog stays behind the Edge `--app` window. Zero the lock for the
        // dialog's lifetime, then restore the user's setting.
        uint savedLockTimeout = 0;
        SystemParametersInfo(SpiGetForegroundLockTimeout, 0, ref savedLockTimeout, 0);
        SystemParametersInfo(SpiSetForegroundLockTimeout, 0, IntPtr.Zero, SpifSendChange);
        try
        {
            return ShowDialogCore(initialDir);
        }
        finally
        {
            SystemParametersInfo(SpiSetForegroundLockTimeout, 0, (IntPtr)savedLockTimeout, SpifSendChange);
        }
    }

    private static string? ShowDialogCore(string? initialDir)
    {
        // The window the user clicked "Open" in — the Edge `--app` window (or their browser
        // tab). Capture it FIRST, before creating any window of our own. We own the dialog
        // by it (Windows renders a modal above and centered on its owner) AND force-raise it.
        var appWindow = GetForegroundWindow();

        // Fallback owner only when there is no usable foreground window: a real form with a
        // taskbar/Alt-Tab entry the user can click to.
        Form? fallbackOwner = null;
        if (appWindow == IntPtr.Zero)
        {
            fallbackOwner = new Form
            {
                Text = "DocWiz — select your project folder",
                StartPosition = FormStartPosition.CenterScreen,
                Size = new System.Drawing.Size(1, 1),
                ShowInTaskbar = true,
                TopMost = true,
            };
            fallbackOwner.Show();
            ForceToForeground(fallbackOwner.Handle);
        }

        // The shell folder dialog runs its own modal loop, so a watcher keeps forcing EVERY
        // top-level window this process shows to topmost+foreground for a few seconds
        // (topmost defeats Z-order regardless of foreground rights; the lock timeout is
        // already zeroed).
        var stop = false;
        var watcher = new Thread(() =>
        {
            for (var i = 0; i < 60 && !stop; i++) // ~4.8s at 80ms
            {
                Thread.Sleep(80);
                foreach (var w in VisibleProcessWindows())
                    ForceToForeground(w);
            }
        }) { IsBackground = true };
        watcher.Start();

        try
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select your DocWiz project folder",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true,
            };
            if (!string.IsNullOrWhiteSpace(initialDir) && Directory.Exists(initialDir))
                dialog.SelectedPath = initialDir;

            IWin32Window owner = (IWin32Window?)fallbackOwner ?? new WindowWrapper(appWindow);
            var result = dialog.ShowDialog(owner);
            return result == DialogResult.OK ? dialog.SelectedPath : null;
        }
        finally
        {
            stop = true;
            fallbackOwner?.Dispose();
        }
    }

    /// <summary>Wraps an arbitrary (possibly cross-process) HWND as a dialog owner.</summary>
    private sealed class WindowWrapper(IntPtr handle) : IWin32Window
    {
        public IntPtr Handle { get; } = handle;
    }

    /// <summary>Every visible, titled top-level window owned by this helper process.</summary>
    private static List<IntPtr> VisibleProcessWindows()
    {
        var pid = (uint)Environment.ProcessId;
        var found = new List<IntPtr>();
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out var wPid);
            if (wPid == pid && IsWindowVisible(hWnd) && GetWindowTextLength(hWnd) > 0)
                found.Add(hWnd);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static void ForceToForeground(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
            return;
        var foreground = GetForegroundWindow();
        var fgThread = GetWindowThreadProcessId(foreground, out _);
        var thisThread = GetCurrentThreadId();

        var attached = fgThread != thisThread && fgThread != 0 &&
                       AttachThreadInput(thisThread, fgThread, true);

        SetWindowPos(hWnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShowWindow);
        BringWindowToTop(hWnd);
        SetForegroundWindow(hWnd);

        if (attached)
            AttachThreadInput(thisThread, fgThread, false);
    }

    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpShowWindow = 0x0040;

    private const uint SpiGetForegroundLockTimeout = 0x2000;
    private const uint SpiSetForegroundLockTimeout = 0x2001;
    private const uint SpifSendChange = 0x0002;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfo(uint action, uint param, ref uint vparam, uint init);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfo(uint action, uint param, IntPtr vparam, uint init);
}
