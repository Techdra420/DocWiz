using DocWiz.Core.Loading;
using DocWiz.Core.Markdown;
using DocWiz.Core.Models;
using Xunit;

namespace DocWiz.Core.Tests;

public class ContentOrganizerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "docwiz-org-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Section(string name, params string[] files)
    {
        var dir = Path.Combine(_root, "content", name);
        Directory.CreateDirectory(dir);
        foreach (var f in files)
            File.WriteAllText(Path.Combine(dir, f), "# x\n");
        return dir;
    }

    private static string[] Names(string dir) =>
        Directory.GetFiles(dir, "*.md").Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray()!;

    [Fact]
    public void MoveChapter_Up_SwapsNeighborsAndRenumbers()
    {
        var dir = Section("010-s", "010-a.md", "020-b.md", "030-c.md");
        var moved = ContentOrganizer.MoveChapter(Path.Combine(dir, "020-b.md"), -1);

        Assert.Equal("010-b.md", Path.GetFileName(moved));
        Assert.Equal(["010-b.md", "020-a.md", "030-c.md"], Names(dir));
    }

    [Fact]
    public void MoveChapter_DownAtEnd_IsANoOp()
    {
        var dir = Section("010-s", "010-a.md", "020-b.md");
        var moved = ContentOrganizer.MoveChapter(Path.Combine(dir, "020-b.md"), +1);

        Assert.Equal("020-b.md", Path.GetFileName(moved));
        Assert.Equal(["010-a.md", "020-b.md"], Names(dir));
    }

    [Fact]
    public void MoveChapter_NormalizesUnprefixedNames()
    {
        // "intro.md" sorts after the prefixed names (ordinal) — moving it up gives
        // every file a clean 010/020 prefix without touching the slug.
        var dir = Section("010-s", "010-a.md", "intro.md");
        var moved = ContentOrganizer.MoveChapter(Path.Combine(dir, "intro.md"), -1);

        Assert.Equal("010-intro.md", Path.GetFileName(moved));
        Assert.Equal(["010-intro.md", "020-a.md"], Names(dir));
    }

    [Fact]
    public void MoveSection_SwapsFoldersAndKeepsContents()
    {
        Section("010-overview", "010-a.md");
        Section("020-setup", "010-b.md");

        var newName = ContentOrganizer.MoveSection(_root, "020-setup", -1);

        Assert.Equal("010-setup", newName);
        var folders = Directory.GetDirectories(Path.Combine(_root, "content"))
            .Select(d => Path.GetFileName(d)!).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(["010-setup", "020-overview"], folders);
        Assert.True(File.Exists(Path.Combine(_root, "content", "010-setup", "010-b.md")));
    }

    private static Chapter Ch(string id, string title, string markdown) => new()
    {
        Id = id, Title = title, Markdown = markdown,
        FilePath = "", SectionSlug = "", SectionTitle = "", SortKey = "",
    };

    [Fact]
    public void FindReferences_ReportsLinksAndIncludes_IgnoresLookalikes()
    {
        var project = new DocProject
        {
            Root = _root,
            Manifest = new ProjectManifest(),
            Chapters =
            [
                Ch("safety", "Safety", ""),
                Ch("ops", "Operation", "See [safety](dw:safety#lockout)."),
                Ch("other", "Other", "Links to [x](dw:safety-extended) only."),
            ],
            Views =
            [
                new ViewDefinition
                {
                    Title = "Operator Training",
                    Slides = [new SlideDef { Include = "safety" }, new SlideDef { Include = "safety#lockout" }],
                },
            ],
        };

        var refs = ContentOrganizer.FindReferences(project, "safety");

        Assert.Equal(2, refs.Count);
        Assert.Contains(refs, r => r.Contains("Operation"));
        Assert.Contains(refs, r => r.Contains("Operator Training") && r.Contains("2 slides"));
    }

    [Fact]
    public void FindReferences_ReportsRegionEmbeds()
    {
        var project = new DocProject
        {
            Root = _root,
            Manifest = new ProjectManifest(),
            Chapters = [Ch("safety", "Safety", "")],
            Views =
            [
                new ViewDefinition
                {
                    Title = "Operator Training",
                    Slides =
                    [
                        new SlideDef
                        {
                            Layout = SlideLayout.TwoColumn,
                            Regions = new()
                            {
                                ["left"] = "- authored bullet",
                                ["right"] = DocWizEmbed.DirectiveFor("safety#lockout"),
                            },
                        },
                    ],
                },
            ],
        };

        var refs = ContentOrganizer.FindReferences(project, "safety");

        Assert.Contains(refs, r => r.Contains("Operator Training") && r.Contains("1 slide"));
    }

    [Fact]
    public void FindUsage_ListsLinkingChaptersAndSlideNumbers()
    {
        var project = new DocProject
        {
            Root = _root,
            Manifest = new ProjectManifest(),
            Chapters =
            [
                Ch("safety", "Safety", ""),
                Ch("ops", "Operation", "See [safety](dw:safety)."),
            ],
            Views =
            [
                new ViewDefinition
                {
                    Title = "Operator Training",
                    Slides = [new SlideDef { Include = "ops" }, new SlideDef { Include = "safety" }, new SlideDef { Include = "safety#lockout" }],
                },
                new ViewDefinition { Title = "Quick Reference", Slides = [new SlideDef { Include = "ops" }] },
            ],
        };

        var usage = ContentOrganizer.FindUsage(project, "safety");

        Assert.Equal(["Operation"], usage.LinkedFrom.Select(c => c.Title));
        var view = Assert.Single(usage.Views);
        Assert.Equal("Operator Training", view.View.Title);
        Assert.Equal([2, 3], view.Slides);
    }

    [Fact]
    public void FindReferences_CleanChapter_IsEmpty()
    {
        var project = new DocProject
        {
            Root = _root,
            Manifest = new ProjectManifest(),
            Chapters = [Ch("lonely", "Lonely", "")],
            Views = [],
        };

        Assert.Empty(ContentOrganizer.FindReferences(project, "lonely"));
    }
}
