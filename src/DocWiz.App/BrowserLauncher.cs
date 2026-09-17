using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes; // in-box (shared framework), no NuGet — see Guardrails

namespace DocWiz.App;

/// <summary>Launches the editor UI: a chromeless Edge --app window (so DocWiz reads as
/// a native app), or the default browser tab. Returns the spawned app-window process
/// (for "close = quit" monitoring), or null when it used / fell back to the default
/// browser. No registry use — DocWiz.App targets net8.0 (not -windows), so Edge is
/// located by its known install paths to avoid a Microsoft.Win32.Registry dependency.</summary>
public static class BrowserLauncher
{
    public static Process? Launch(string url, string windowMode)
    {
        if (windowMode != "browser")
        {
            var edge = FindEdge();
            if (edge is not null)
            {
                try
                {
                    var profile = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "DocWiz", "app-window");
                    EnsureOfflineSpellcheck(profile);
                    var psi = new ProcessStartInfo(edge) { UseShellExecute = false };
                    psi.ArgumentList.Add($"--app={url}");
                    psi.ArgumentList.Add($"--user-data-dir={profile}");
                    psi.ArgumentList.Add("--no-first-run");
                    var proc = Process.Start(psi);
                    if (proc is not null)
                    {
                        proc.EnableRaisingEvents = true; // so .Exited fires for "close = quit"
                        return proc;
                    }
                }
                catch
                {
                    // Edge present but launch failed — fall through to the default browser.
                }
            }
        }
        OpenDefaultBrowser(url);
        return null;
    }

    private static void OpenDefaultBrowser(string url) =>
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });

    /// <summary>
    /// Seeds the dedicated app-window Edge profile so spelling corrections work offline.
    /// A fresh Edge profile defaults to the *online* Microsoft Editor spell service: words
    /// get the red squiggle but the right-click context menu lists NO suggestions because
    /// the service is unreachable (DocWiz is offline-first / behind a corporate proxy).
    /// Turning the service off makes Edge fall back to the local Windows dictionaries,
    /// which provide suggestions with no network. Best-effort: any failure leaves the
    /// profile untouched and launch proceeds. Only the unprotected `spellcheck` prefs are
    /// touched (no super-MAC reset), and existing user dictionary choices are preserved.
    /// </summary>
    public static void EnsureOfflineSpellcheck(string profileDir)
    {
        try
        {
            var defaultDir = Path.Combine(profileDir, "Default");
            Directory.CreateDirectory(defaultDir);
            var prefsPath = Path.Combine(defaultDir, "Preferences");

            JsonObject root;
            if (File.Exists(prefsPath))
                root = JsonNode.Parse(File.ReadAllText(prefsPath)) as JsonObject ?? new JsonObject();
            else
                root = new JsonObject();

            if (root["spellcheck"] is not JsonObject spell)
            {
                spell = new JsonObject();
                root["spellcheck"] = spell;
            }

            spell["use_spelling_service"] = false; // online Microsoft Editor off → local dictionaries

            // Only seed a language list when the profile has none yet (don't clobber the
            // user's own picks). English plus the OS UI language covers the common cases.
            if (spell["dictionaries"] is not JsonArray dicts || dicts.Count == 0)
            {
                var langs = new JsonArray { "en-US" };
                var ui = CultureInfo.CurrentUICulture.Name; // e.g. "it-IT", "cs-CZ"
                if (!string.IsNullOrEmpty(ui) && !string.Equals(ui, "en-US", StringComparison.OrdinalIgnoreCase))
                    langs.Add(ui);
                spell["dictionaries"] = langs;
            }

            File.WriteAllText(prefsPath, root.ToJsonString());
        }
        catch
        {
            // Non-fatal: spellcheck stays as Edge defaults it; launching is what matters.
        }
    }

    /// <summary>First existing path from the candidates, or null. Pure -> unit-testable.</summary>
    internal static string? ResolveBrowser(IEnumerable<string> candidates, Func<string, bool> exists)
        => candidates.FirstOrDefault(exists);

    public static string? FindEdge()
    {
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var candidates = new[]
        {
            Path.Combine(pf86, "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(pf, "Microsoft", "Edge", "Application", "msedge.exe"),
        };
        return ResolveBrowser(candidates, File.Exists);
    }
}
