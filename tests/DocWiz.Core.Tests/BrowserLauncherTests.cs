using System.Text.Json.Nodes;
using DocWiz.App;
using Xunit;

namespace DocWiz.Core.Tests;

public sealed class BrowserLauncherTests
{
    [Fact]
    public void ResolveBrowser_returns_first_existing_candidate()
    {
        var hit = BrowserLauncher.ResolveBrowser(
            new[] { @"C:\nope\a.exe", @"C:\yes\edge.exe", @"C:\also\b.exe" },
            p => p == @"C:\yes\edge.exe");
        Assert.Equal(@"C:\yes\edge.exe", hit);
    }

    [Fact]
    public void ResolveBrowser_returns_null_when_none_exist()
        => Assert.Null(BrowserLauncher.ResolveBrowser(new[] { @"C:\a.exe", @"C:\b.exe" }, _ => false));

    [Fact]
    public void EnsureOfflineSpellcheck_seeds_fresh_profile_with_local_dictionaries()
    {
        var profile = NewTempDir();
        try
        {
            BrowserLauncher.EnsureOfflineSpellcheck(profile);

            var spell = ReadSpellcheck(profile);
            Assert.False((bool)spell["use_spelling_service"]!);
            var dicts = (JsonArray)spell["dictionaries"]!;
            Assert.Contains(dicts, n => (string?)n == "en-US");
        }
        finally { Directory.Delete(profile, recursive: true); }
    }

    [Fact]
    public void EnsureOfflineSpellcheck_preserves_existing_dictionaries_and_unrelated_prefs()
    {
        var profile = NewTempDir();
        try
        {
            var defaultDir = Path.Combine(profile, "Default");
            Directory.CreateDirectory(defaultDir);
            File.WriteAllText(Path.Combine(defaultDir, "Preferences"),
                """{"browser":{"window_placement":1},"spellcheck":{"dictionaries":["cs"],"use_spelling_service":true}}""");

            BrowserLauncher.EnsureOfflineSpellcheck(profile);

            var root = ReadPreferences(profile);
            // unrelated pref untouched
            Assert.Equal(1, (int)root["browser"]!["window_placement"]!);
            var spell = (JsonObject)root["spellcheck"]!;
            // service forced off, user's own dictionary kept (not clobbered with en-US)
            Assert.False((bool)spell["use_spelling_service"]!);
            var dicts = (JsonArray)spell["dictionaries"]!;
            Assert.Single(dicts);
            Assert.Equal("cs", (string?)dicts[0]);
        }
        finally { Directory.Delete(profile, recursive: true); }
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dw-spell-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static JsonObject ReadPreferences(string profile) =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(profile, "Default", "Preferences")))!;

    private static JsonObject ReadSpellcheck(string profile) =>
        (JsonObject)ReadPreferences(profile)["spellcheck"]!;
}
