using DocWiz.App;
using DocWiz.Core.Loading;
using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>
/// "Compare with now" (Stage 6 step 3): blocks are rendered by the one pipeline and matched on
/// their source, so an older version reads like the page with what was removed and added since.
/// </summary>
public class HistoryDiffTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "docwiz-hdiff-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private const string Fm = "---\nid: t\ntitle: T\naudiences: [user]\n---\n";

    [Fact]
    public void Compare_MarksRemovedAndAddedBlocks_KeepsTheRest()
    {
        var (project, _) = TestRepo.LoadDemoProject();
        var older = Fm + "# T\n\nKept paragraph.\n\nRemoved paragraph.\n";
        var now = Fm + "# T\n\nKept paragraph.\n\nAdded paragraph.\n";

        var blocks = HistoryDiff.Compare(project, older, now);

        Assert.Equal([HistoryDiff.Same, HistoryDiff.Same, HistoryDiff.Removed, HistoryDiff.Added], blocks.Select(b => b.State));
        Assert.Contains("Removed paragraph.", blocks[2].Html);
        Assert.Contains("Added paragraph.", blocks[3].Html);
    }

    [Fact]
    public void Compare_IdenticalVersions_HasNoChanges()
    {
        var (project, _) = TestRepo.LoadDemoProject();
        var text = Fm + "# T\n\nOne.\n\nTwo.\n";
        Assert.All(HistoryDiff.Compare(project, text, text), b => Assert.Equal(HistoryDiff.Same, b.State));
    }

    [Fact]
    public void ProjectCopy_LeavesTheHistoryBehind()
    {
        var source = Path.Combine(_root, "src");
        Directory.CreateDirectory(Path.Combine(source, ".docwiz", "history", "files"));
        File.WriteAllText(Path.Combine(source, ".docwiz", "history", "files", "x.md"), "old");
        File.WriteAllText(Path.Combine(source, "docwiz.yaml"), "project: T\n");

        TemplateLibrary.CopyProject(source, Path.Combine(_root, "copy"));

        Assert.True(File.Exists(Path.Combine(_root, "copy", "docwiz.yaml")));
        Assert.False(Directory.Exists(Path.Combine(_root, "copy", ".docwiz")));
    }
}
