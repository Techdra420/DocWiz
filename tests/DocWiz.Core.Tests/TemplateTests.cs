using DocWiz.Core.Loading;
using DocWiz.Core.Markdown;
using DocWiz.Core.Validation;
using Xunit;

namespace DocWiz.Core.Tests;

public class TemplateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "docwiz-tpl-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void ChapterFile_TitleWithYamlSpecials_RoundTrips()
    {
        // ':' and '[' in a plain YAML scalar would break the frontmatter.
        Directory.CreateDirectory(Path.Combine(_root, "content", "010-x"));
        Directory.CreateDirectory(Path.Combine(_root, "views"));
        File.WriteAllText(Path.Combine(_root, "docwiz.yaml"),
            """
            project: T
            code: T-1
            audiences:
              - id: user
                title: User
            """);
        File.WriteAllText(Path.Combine(_root, "content", "010-x", "010-c.md"),
            ChapterTemplates.ChapterFile("printer-setup", "Printer: Setup [v2] & 'quotes'", "overview", ["user"]));

        var project = ProjectLoader.Load(_root);
        Assert.Single(project.Chapters);
        Assert.Equal("Printer: Setup [v2] & 'quotes'", project.Chapters[0].Title);
        Assert.Equal("printer-setup", project.Chapters[0].Id);
    }

    [Fact]
    public void SoftwareHandbookTemplate_PassesValidationCleanly()
    {
        ProjectTemplates.Apply(_root, "software-handbook");
        var project = ProjectLoader.Load(_root);
        DocWizMarkdown.ParseAll(project, DocWizMarkdown.CreatePipeline());
        var issues = ProjectValidator.Validate(project);
        Assert.Empty(issues);
    }

    [Fact]
    public void BlankTemplate_ProjectNameWithBrackets_RoundTrips()
    {
        var dir = Path.Combine(_root, "my docs [v2]");
        ProjectScaffolder.CreateNew(dir);
        var project = ProjectLoader.Load(dir);
        Assert.Equal("my docs [v2]", project.Manifest.Project);
    }

    [Fact]
    public void Discover_EmptyOrMissingDir_ReturnsEmpty()
    {
        Assert.Empty(TemplateLibrary.Discover(Path.Combine(_root, "nope")));
        var empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(empty);
        Assert.Empty(TemplateLibrary.Discover(empty));
    }

    [Fact]
    public void Discover_ValidProject_PrefersTemplateMetaThenManifest()
    {
        var lib = Path.Combine(_root, "lib");

        // One template whose title comes from template.yaml.
        var named = Path.Combine(lib, "named");
        ProjectScaffolder.CreateNew(named);
        TemplateLibrary.WriteTemplateMeta(named, "Friendly Name", "From the meta file.");

        // One template with no template.yaml → title falls back to the manifest project.
        var bare = Path.Combine(lib, "bare");
        ProjectScaffolder.CreateNew(bare);

        var found = TemplateLibrary.Discover(lib);
        Assert.Equal(2, found.Count);

        var namedInfo = found.Single(t => t.Id == "named");
        Assert.Equal("Friendly Name", namedInfo.Title);
        Assert.Equal("From the meta file.", namedInfo.Description);
        Assert.Null(namedInfo.Error);

        var bareInfo = found.Single(t => t.Id == "bare");
        Assert.Equal("bare", bareInfo.Title); // manifest project = folder name from scaffolder
        Assert.Null(bareInfo.Error);
    }

    [Fact]
    public void Discover_BrokenProject_SurfacedWithError()
    {
        var lib = Path.Combine(_root, "lib");
        var broken = Path.Combine(lib, "broken");
        Directory.CreateDirectory(broken);
        // A docwiz.yaml that won't parse → load fails, but the template stays visible.
        File.WriteAllText(Path.Combine(broken, "docwiz.yaml"), "project: [unterminated");

        var info = Assert.Single(TemplateLibrary.Discover(lib));
        Assert.Equal("broken", info.Id);
        Assert.NotNull(info.Error);
    }

    [Fact]
    public void CopyProject_CopiesTree_ExcludesOutput_AndValidatesClean()
    {
        var source = Path.Combine(FindRepoRoot(), "samples", "stockroom-demo");
        var dest = Path.Combine(_root, "copy");

        // A build artifact that must NOT be carried into the copy.
        var outFile = Path.Combine(source, "output", "handover", "stale.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
        File.WriteAllText(outFile, "stale");
        try
        {
            TemplateLibrary.CopyProject(source, dest);

            Assert.True(File.Exists(Path.Combine(dest, "docwiz.yaml")));
            Assert.False(Directory.Exists(Path.Combine(dest, "output")));

            var project = ProjectLoader.Load(dest);
            DocWizMarkdown.ParseAll(project, DocWizMarkdown.CreatePipeline());
            var errors = ProjectValidator.Validate(project).Where(i => i.Severity == IssueSeverity.Error);
            Assert.Empty(errors);
        }
        finally
        {
            File.Delete(outFile);
        }
    }

    [Fact]
    public void CopyProject_RefusesNonEmptyDest()
    {
        var source = Path.Combine(FindRepoRoot(), "samples", "stockroom-demo");
        var dest = Path.Combine(_root, "occupied");
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "something.txt"), "x");

        Assert.Throws<DocWizLoadException>(() => TemplateLibrary.CopyProject(source, dest));
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "DocWiz.sln")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return dir!;
    }
}
