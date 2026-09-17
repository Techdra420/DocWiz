using System.Diagnostics;
using System.Drawing;

namespace DocWiz.Cli;

/// <summary>
/// Instant "DocWiz is starting…" window for the double-click launch path. The exe spends
/// several seconds before the editor window appears — single-file extraction on first run,
/// Kestrel startup, then Edge cold-start — with nothing on screen, so the stakeholder
/// couldn't tell it was launching (VISION line 193). This shows a small branded splash
/// immediately on its own STA thread and is dismissed once the editor window is actually up
/// (EditorServer fires the onUiLaunched callback wired in Program.cs).
///
/// Native WinForms: DocWiz.App is platform-neutral, so the splash lives here in the
/// net8.0-windows CLI alongside <see cref="FolderPick"/>. It cannot cover the pre-Main
/// single-file extraction (first run only); it covers everything from launch onward.
/// </summary>
internal sealed class Splash
{
    // The editor window can come up only a moment after the splash finally paints (on a
    // warm/ReadyToRun start), which would flash the splash for ~100 ms and read as broken.
    // Keep it on screen at least this long from first paint so the handoff looks deliberate.
    private const int MinDwellMs = 700;

    private Form? _form;
    private readonly ManualResetEventSlim _shown = new();
    private long _shownAt; // Stopwatch timestamp of first paint; 0 until the form is shown
    private int _closing;  // 0→1 once, so a min-dwell close is scheduled at most once

    /// <summary>Starts the splash on a background STA thread and returns once its form
    /// exists (so a near-immediate <see cref="Close"/> still finds it).</summary>
    public static Splash Show()
    {
        var splash = new Splash();
        var thread = new Thread(splash.Pump)
        {
            IsBackground = true,
            Name = "DocWiz splash",
            // Nudge ahead of the heavy startup JIT racing on the main thread so the splash
            // wins enough CPU to paint promptly during the contended cold start.
            Priority = ThreadPriority.AboveNormal,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        splash._shown.Wait(2000);
        return splash;
    }

    /// <summary>Dismisses the splash (after a minimum on-screen dwell so it never just
    /// flashes). Safe to call repeatedly and from any thread.</summary>
    public void Close()
    {
        var form = _form;
        if (form is null)
            return;
        if (Interlocked.Exchange(ref _closing, 1) == 1)
            return; // already closing — don't stack a second delayed close

        var shownAt = _shownAt;
        var elapsedMs = shownAt == 0
            ? 0
            : (Stopwatch.GetTimestamp() - shownAt) * 1000.0 / Stopwatch.Frequency;
        var remaining = MinDwellMs - elapsedMs;
        if (remaining <= 0)
            CloseNow(form);
        else
            _ = Task.Delay((int)remaining).ContinueWith(_ => CloseNow(form));
    }

    private static void CloseNow(Form form)
    {
        try
        {
            if (form.IsHandleCreated && !form.IsDisposed)
                form.BeginInvoke(form.Close);
        }
        catch
        {
            // Best-effort feedback; never let dismissing the splash break launch.
        }
    }

    private void Pump()
    {
        try
        {
            Run();
        }
        catch
        {
            // A cosmetic splash must NEVER crash the app: any failure here (an unhandled
            // exception on this background STA thread would otherwise terminate the whole
            // process) means simply "no splash" — the editor still launches.
        }
        finally
        {
            _shown.Set(); // never leave Show() blocked for the full timeout on failure
        }
    }

    private void Run()
    {
        // Belt-and-suspenders: route any message-loop exception to a no-op too, so a stray
        // paint/animation error on the splash thread can't bring the app down either.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, _) => { };

        // Brand colours mirror the editor's dark chrome (native WinForms can't read the
        // CSS --dw-* tokens, so these are kept visually consistent by hand).
        var bg = Color.FromArgb(0x1B, 0x1D, 0x21);
        var fg = Color.FromArgb(0xEC, 0xEC, 0xEC);
        var muted = Color.FromArgb(0x9A, 0xA0, 0xA6);
        var accent = Color.FromArgb(0x3F, 0xB9, 0x50); // green brand mark

        _form = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.CenterScreen,
            ClientSize = new Size(380, 200),
            BackColor = bg,
            TopMost = true,
            ShowInTaskbar = true, // a "DocWiz" taskbar entry while it starts
            Text = "DocWiz",
            AutoScaleMode = AutoScaleMode.Dpi,
        };
        try { _form.Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch { /* icon optional */ }

        var title = new Label
        {
            Text = "DocWiz",
            ForeColor = fg,
            Font = new Font("Segoe UI", 22f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Bounds = new Rectangle(0, 52, 380, 48),
        };
        var status = new Label
        {
            Text = "Starting up…",
            ForeColor = muted,
            Font = new Font("Segoe UI", 10.5f),
            TextAlign = ContentAlignment.MiddleCenter,
            Bounds = new Rectangle(0, 104, 380, 26),
        };
        var bar = new ProgressBar
        {
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 25,
            Bounds = new Rectangle(50, 150, 280, 6),
        };

        _form.Controls.Add(title);
        _form.Controls.Add(status);
        _form.Controls.Add(bar);
        _form.Paint += (_, e) =>
            ControlPaint.DrawBorder(e.Graphics, _form.ClientRectangle, accent, ButtonBorderStyle.Solid);

        _form.Shown += (_, _) =>
        {
            _shownAt = Stopwatch.GetTimestamp(); // start the minimum-dwell clock at first paint
            _shown.Set();
        };
        Application.Run(_form);
    }
}
