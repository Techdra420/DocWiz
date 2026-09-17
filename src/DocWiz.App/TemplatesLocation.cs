namespace DocWiz.App;

/// <summary>
/// Resolves the file-based template library directory. The visible/portable mental model
/// is <c>&lt;exe folder&gt;\Templates</c>; if that is not writable (e.g. a Program Files
/// install), fall back to <c>%APPDATA%\DocWiz\Templates</c>. The chosen directory is
/// ensured to exist but never seeded — it starts empty.
/// </summary>
public static class TemplatesLocation
{
    /// <summary>Override the resolved directory for testing. Set to a temp path in tests;
    /// reset to null in teardown. Never set in production code.</summary>
    internal static string? OverrideDir { get; set; }

    public static string Resolve()
    {
        if (OverrideDir is not null)
            return Ensure(OverrideDir);

        // Environment.ProcessPath is the real exe even under single-file publish (which
        // extracts to a temp dir) — AppContext.BaseDirectory would point at the temp
        // extraction. Mirrors AppWindow.cs's exe-dir resolution.
        var exePath = Environment.ProcessPath;
        var exeDir = exePath is not null ? Path.GetDirectoryName(exePath) : null;
        if (!string.IsNullOrEmpty(exeDir))
        {
            var beside = Path.Combine(exeDir, "Templates");
            if (TryEnsureWritable(beside))
                return beside;
        }

        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DocWiz", "Templates");
        return Ensure(appData);
    }

    private static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Creates the directory and confirms we can write into it with a probe file.
    /// A locked-down install (Program Files) fails here and the caller falls back.</summary>
    private static bool TryEnsureWritable(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".docwiz-write-probe");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
