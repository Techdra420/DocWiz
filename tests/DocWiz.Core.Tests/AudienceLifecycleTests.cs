using DocWiz.App;
using DocWiz.Core.Loading;
using DocWiz.Core.Markdown;
using DocWiz.Core.Models;
using DocWiz.Core.Validation;
using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>
/// Audiences are project-defined (CONTENT_MODEL): creating a chapter, its starter skeleton,
/// and deleting an audience must never leave a tag pointing at an audience the project
/// doesn't have — every such tag is a Check error the author didn't cause.
/// </summary>
public class AudienceLifecycleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "docwiz-aud-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private void WriteProject(string audiencesYaml)
    {
        Directory.CreateDirectory(Path.Combine(_root, "content", "010-overview"));
        Directory.CreateDirectory(Path.Combine(_root, "views"));
        File.WriteAllText(Path.Combine(_root, "docwiz.yaml"), "project: T\ncode: T-1\naudiences:\n" + audiencesYaml);
    }

    private static List<ValidationIssue> Errors(DocProject project)
    {
        DocWizMarkdown.ParseAll(project, DocWizMarkdown.CreatePipeline());
        return ProjectValidator.Validate(project).Where(i => i.Severity == IssueSeverity.Error).ToList();
    }

    [Fact]
    public void NewChapter_WithoutChoice_GetsTheProjectsAudiences_AndPassesCheck()
    {
        WriteProject("  - id: operator\n    title: Operator\n  - id: auditor\n    title: Auditor\n");
        var dto = new NewChapterDto("010-overview", "shift-handover", "shift-handover", "Shift handover", "troubleshooting", []);

        ChapterFactory.Create(_root, dto, ["operator", "auditor"]);

        var project = ProjectLoader.Load(_root);
        Assert.Equal(["operator", "auditor"], Assert.Single(project.Chapters).Audiences);
        Assert.Empty(Errors(project));
    }

    [Fact]
    public void Skeleton_KeepsAdminNote_OnlyWhenTheChapterIsTaggedAdmin()
    {
        var withAdmin = ChapterTemplates.ChapterFile("t", "T", "troubleshooting", ["support", "admin"]);
        var without = ChapterTemplates.ChapterFile("t", "T", "troubleshooting", ["support"]);

        Assert.Contains("::: audience admin", withAdmin);
        Assert.DoesNotContain("::: audience", without);
        Assert.Contains("## Getting help {#getting-help}", without);
        Assert.EndsWith("support request.\n", without);
    }

    [Fact]
    public void Skeleton_WithAdmin_IsUnchangedFromTheDefaultBody()
    {
        foreach (var discipline in new[] { "", "setup", "operations", "troubleshooting", "reference", "integration" })
            Assert.Equal(ChapterTemplates.Body(discipline), ChapterTemplates.Body(discipline, includeAdminNote: true));
    }

    [Fact]
    public void Usage_CountsFrontmatterTags_Blocks_AndViews()
    {
        WriteProject("  - id: user\n    title: User\n  - id: admin\n    title: Administrator\n");
        File.WriteAllText(Path.Combine(_root, "content", "010-overview", "010-a.md"),
            "---\nid: a\ntitle: A\naudiences: [user, admin]\n---\n# A\n\n::: audience admin\nOne.\n:::\n\n::: audience user admin\nTwo.\n:::\n");
        File.WriteAllText(Path.Combine(_root, "content", "010-overview", "020-b.md"),
            "---\nid: b\ntitle: B\naudiences: [user]\n---\n# B\n\nNo blocks.\n");
        File.WriteAllText(Path.Combine(_root, "views", "admin-guide.yaml"),
            "id: admin-guide\ntitle: Administrator Guide\naudience: admin\nmode: document\nslides:\n  - include: a\n");
        var project = ProjectLoader.Load(_root);
        DocWizMarkdown.ParseAll(project, DocWizMarkdown.CreatePipeline());

        var usage = AudienceUsage.Find(project, "admin");

        Assert.Equal("a", Assert.Single(usage.TaggedChapters).Id);
        var block = Assert.Single(usage.BlockChapters);
        Assert.Equal(("a", 2), (block.Chapter.Id, block.Count));
        Assert.Equal("admin-guide", Assert.Single(usage.Views).Id);
    }

    [Fact]
    public void Strip_RemovesOnlyTheTag_KeepingBodyAndUnknownKeys()
    {
        WriteProject("  - id: user\n    title: User\n");
        const string body = "# A\n\n::: audience admin\nKept for the author to resolve.\n:::\n";
        var file = Path.Combine(_root, "content", "010-overview", "010-a.md");
        File.WriteAllText(file, "---\nid: a\ntitle: A\nowner: docs-team\naudiences:\n- user\n- admin\n---\n" + body);
        var project = ProjectLoader.Load(_root);

        var changed = AudienceRemoval.StripFromChapters(project, ["admin"]);

        Assert.Equal(1, changed);
        var reloaded = ProjectLoader.Load(_root);
        Assert.Equal(["user"], reloaded.Chapters[0].Audiences);
        var text = File.ReadAllText(file);
        Assert.Contains("owner: docs-team", text);
        Assert.EndsWith(body, text);
    }

    [Fact]
    public void Strip_LeavesUntaggedChaptersByteIdentical()
    {
        WriteProject("  - id: user\n    title: User\n");
        var file = Path.Combine(_root, "content", "010-overview", "010-a.md");
        const string original = "---\nid: a\ntitle: A\naudiences: [user]\n---\n# A\n";
        File.WriteAllText(file, original);

        Assert.Equal(0, AudienceRemoval.StripFromChapters(ProjectLoader.Load(_root), ["admin"]));
        Assert.Equal(original, File.ReadAllText(file));
    }
}
