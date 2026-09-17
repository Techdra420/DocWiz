using System.Text.Json;

namespace DocWiz.App;

/// <summary>Per-user editor settings, persisted to %APPDATA%\DocWiz\settings.json.
/// Best-effort — a missing/garbage file or any IO error yields defaults and never
/// blocks startup (mirrors RecentProjects).</summary>
public sealed class AppSettings
{
    /// <summary>How the editor UI is launched: "native" = a DocWiz.exe-owned WebView2 window
    /// (default — the app IS the window); "browser" = the default-browser tab. The chromeless
    /// Edge app-window is no longer a persisted choice — it is only an automatic runtime fallback
    /// when WebView2 is unavailable (see EditorServer.RunAsync). The legacy "app" value migrates
    /// to "native" via <see cref="NormalizedWindowMode"/>.</summary>
    public string WindowMode { get; set; } = "native";

    /// <summary>The persisted <see cref="WindowMode"/> reduced to the two launch modes the app acts
    /// on: "browser", or otherwise "native". Anything that is not "browser" — including a legacy
    /// "app", an unset, or a garbage value — is the native DocWiz window, so old settings and
    /// future additions both land on a working launch.</summary>
    public string NormalizedWindowMode => WindowMode == "browser" ? "browser" : "native";

    // Testability seam: tests point this at a temp file so they never touch real %APPDATA%.
    internal static string? StorePathOverride;

    private static string StorePath => StorePathOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DocWiz", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(StorePath)) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // best effort only
        }
    }
}
