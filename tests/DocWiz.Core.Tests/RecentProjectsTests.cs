using DocWiz.App;
using Xunit;

namespace DocWiz.Core.Tests;

public class RecentProjectsTests : IDisposable
{
    private readonly string _tempFile;

    public RecentProjectsTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"docwiz-recent-test-{Guid.NewGuid()}.json");
        RecentProjects.StorePathOverride = _tempFile;
    }

    public void Dispose()
    {
        RecentProjects.StorePathOverride = null;
        try { if (File.Exists(_tempFile)) File.Delete(_tempFile); } catch { }
    }

    [Fact]
    public void Add_then_TogglePin_marks_pinned_and_toggles_back()
    {
        RecentProjects.Add(@"C:\TestProject", "TestProject", "TP");

        RecentProjects.TogglePin(@"C:\TestProject");
        var afterFirstToggle = RecentProjects.Load();
        Assert.Single(afterFirstToggle);
        Assert.True(afterFirstToggle[0].Pinned, "Expected Pinned == true after first toggle");

        RecentProjects.TogglePin(@"C:\TestProject");
        var afterSecondToggle = RecentProjects.Load();
        Assert.Single(afterSecondToggle);
        Assert.False(afterSecondToggle[0].Pinned, "Expected Pinned == false after second toggle");
    }

    [Fact]
    public void Add_preserves_pinned_on_reopen()
    {
        const string projectPath = @"C:\PinnedProject";

        RecentProjects.Add(projectPath, "PinnedProject", "PP");
        RecentProjects.TogglePin(projectPath);

        // Verify pinned before re-add
        Assert.True(RecentProjects.Load().Single(r => r.Path.Equals(projectPath, StringComparison.OrdinalIgnoreCase)).Pinned);

        // Re-add same path (simulating re-open)
        RecentProjects.Add(projectPath, "PinnedProject", "PP");

        var entries = RecentProjects.Load()
            .Where(r => r.Path.Equals(projectPath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Single(entries);
        Assert.True(entries[0].Pinned, "Pinned flag must survive a re-open (Add with same path)");
    }

    [Fact]
    public void Load_legacy_json_without_pinned_defaults_false()
    {
        // Write a JSON file without the Pinned field (legacy format)
        const string legacyJson = """
            [{"Path":"C:\\x","Project":"X","Code":"x","LastOpened":"2026-01-01T00:00:00"}]
            """;
        Directory.CreateDirectory(Path.GetDirectoryName(_tempFile)!);
        File.WriteAllText(_tempFile, legacyJson);

        var entries = RecentProjects.Load();
        Assert.Single(entries);
        Assert.False(entries[0].Pinned, "Legacy entries without Pinned field must default to false");
    }
}
