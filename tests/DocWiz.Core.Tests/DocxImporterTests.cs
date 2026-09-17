using DocWiz.App.Import;
using DocWiz.Core.Loading;
using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>Covers the genuinely new import logic — the commit phase (selection, media
/// copy, frontmatter defaults, clean validation) — without needing Pandoc: the commit core
/// (<see cref="DocxImporter.CommitStaged"/>) is fed a <see cref="MarkdownImport.Convert"/>
/// result directly. The Pandoc-driven Preview is exercised by the CLI smoke path.</summary>
public class DocxImporterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "docwiz-imp-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private (ImportResult Result, string StagingDir) Stage(string markdown)
    {
        var staging = Path.Combine(_root, "staging");
        Directory.CreateDirectory(Path.Combine(staging, "media"));
        // A dummy extracted image so media-copy has something to move.
        File.WriteAllBytes(Path.Combine(staging, "media", "img1.png"), [0x89, 0x50, 0x4E, 0x47]);
        return (MarkdownImport.Convert(markdown, "fixture"), staging);
    }

    [Fact]
    public void CommitStaged_WritesOnlySelected_CopiesMedia_DefaultsAudiencesToAll()
    {
        var project = Path.Combine(_root, "project");
        ProjectScaffolder.CreateNew(project);

        var (result, staging) = Stage(
            """
            # Intro
            Some intro text.

            # Handbook
            Installation overview.

            ![diagram](media/img1.png)
            """);

        // Discipline given, audiences null → all of the project's audiences.
        var outcome = DocxImporter.CommitStaged(
            result, staging, project, ["handbook"], "040-imported",
            new ImportDefaults("setup", Audiences: null));

        Assert.Null(outcome.Error);
        var created = Assert.Single(outcome.ChaptersCreated);
        Assert.Equal("handbook", created.Id);

        // The deselected "intro" section was not written.
        var loaded = ProjectLoader.Load(project);
        Assert.DoesNotContain(loaded.Chapters, c => c.Id == "intro");
        var mech = Assert.Single(loaded.Chapters, c => c.Id == "handbook");
        Assert.Equal("setup", mech.Discipline);
        Assert.Equal(["user", "support", "admin"], mech.Audiences);

        // Media copied into assets/<slug>/.
        Assert.True(File.Exists(Path.Combine(project, "assets", "handbook", "img1.png")));
    }

    [Fact]
    public void CommitStaged_ImportedProject_ValidatesClean()
    {
        var project = Path.Combine(_root, "project");
        ProjectScaffolder.CreateNew(project);
        var (result, staging) = Stage(
            """
            # Handbook
            Installation overview.

            ![diagram](media/img1.png)
            """);

        var outcome = DocxImporter.CommitStaged(
            result, staging, project, ["handbook"], "040-imported", new ImportDefaults("", null));

        Assert.Null(outcome.Error);
        Assert.DoesNotContain(outcome.Issues, i => i.Severity == "Error");
    }

    [Fact]
    public void CommitStaged_FrontMatterDeselected_NotWritten()
    {
        var project = Path.Combine(_root, "project");
        ProjectScaffolder.CreateNew(project);
        // Content before the first H1 becomes the quarantined "Front matter (review)" chapter.
        var (result, staging) = Stage(
            """
            Title page junk.

            # Real Chapter
            Body.
            """);
        Assert.Contains(result.Chapters, c => c.Title == "Front matter (review)");

        var outcome = DocxImporter.CommitStaged(
            result, staging, project, ["real-chapter"], "040-imported", new ImportDefaults("", null));

        Assert.Null(outcome.Error);
        var loaded = ProjectLoader.Load(project);
        Assert.DoesNotContain(loaded.Chapters, c => c.Title == "Front matter (review)");
        Assert.Contains(loaded.Chapters, c => c.Id == "real-chapter");
    }

    [Fact]
    public void Commit_UnknownToken_ReturnsError_WritesNothing()
    {
        var project = Path.Combine(_root, "project");
        ProjectScaffolder.CreateNew(project);
        var before = ProjectLoader.Load(project).Chapters.Count;

        var outcome = DocxImporter.Commit("no-such-token", project, ["x"], "040-imported", new ImportDefaults("", null));

        Assert.NotNull(outcome.Error);
        Assert.Empty(outcome.ChaptersCreated);
        Assert.Equal(before, ProjectLoader.Load(project).Chapters.Count);
    }

    // Stub converter: writes a 4-byte PNG and succeeds (no GDI+ needed in CI).
    private static bool StubConvert(string src, string png) { File.WriteAllBytes(png, [0x89, 0x50, 0x4E, 0x47]); return true; }

    [Fact]
    public void CommitStaged_WithConverter_EmfBecomesPng_LinkRewritten_NoEmfShipped()
    {
        var project = Path.Combine(_root, "project");
        ProjectScaffolder.CreateNew(project);

        var staging = Path.Combine(_root, "staging");
        Directory.CreateDirectory(Path.Combine(staging, "media"));
        File.WriteAllBytes(Path.Combine(staging, "media", "image1.emf"), [1, 2, 3]); // fake metafile
        var result = MarkdownImport.Convert(
            "# Handbook\n\n![](media/image1.emf)\n", "fixture");

        var outcome = DocWiz.App.Import.DocxImporter.CommitStaged(
            result, staging, project, ["handbook"], "040-imported",
            new ImportDefaults("", null), StubConvert);

        Assert.Null(outcome.Error);
        // PNG landed, EMF did not.
        Assert.True(File.Exists(Path.Combine(project, "assets", "handbook", "image1.png")));
        Assert.False(File.Exists(Path.Combine(project, "assets", "handbook", "image1.emf")));
        // Body links the PNG (read via the actual created path — the file name is prefixed).
        var created = Assert.Single(outcome.ChaptersCreated);
        var body = File.ReadAllText(Path.Combine(project, created.File.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Contains("image1.png", body);
        Assert.DoesNotContain("image1.emf", body);
        // The stale "vector graphic" advisory is replaced by a converted-summary line.
        Assert.DoesNotContain(outcome.Report, l => l.Contains("is a Word vector graphic"));
        Assert.Contains(outcome.Report, l => l.Contains("converted") && l.Contains("PNG"));
    }

    [Fact]
    public void FindPandoc_DoesNotThrow()
    {
        // May be null on a machine without Pandoc — the contract is "no exception".
        var ex = Record.Exception(() => DocxImporter.FindPandoc());
        Assert.Null(ex);
    }
}
