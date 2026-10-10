using DocWiz.Build;
using DocWiz.Core.Markdown;
using DocWiz.Core.Models;
using DocWiz.Core.Validation;
using Xunit;

namespace DocWiz.Core.Tests;

public class SlideChapterExpansionTests
{
    // A minimal project: one chapter with two H2s, the first having one H3.
    private static DocProject Project(List<ViewDefinition>? views = null)
    {
        var md =
            "# Intro\n\nLead text.\n\n## Alpha {#alpha}\n\nAlpha body.\n\n" +
            "### Alpha-One {#alpha-one}\n\nA1 body.\n\n## Beta {#beta}\n\nBeta body.\n";
        var chapter = new Chapter
        {
            Id = "intro", Title = "Intro", Discipline = "overview", Audiences = ["operator"],
            FilePath = "content/010-overview/010-intro.md",
            SectionSlug = "010-overview", SectionTitle = "Overview",
            SortKey = "010-overview/010-intro", Markdown = md,
        };
        return new DocProject
        {
            Root = Path.Combine(Path.GetTempPath(), "docwiz-chapexp"),
            Manifest = new ProjectManifest
            {
                Project = "T", Code = "T-1",
                Audiences = [new AudienceDef { Id = "operator", Title = "Operator" }],
            },
            Chapters = [chapter],
            Views = views ?? [],
        };
    }

    [Fact]
    public void ChapterDirective_ExpandsToOneSlidePerHeading_WithSrcAnchors()
    {
        var project = Project();
        var pipeline = DocWizMarkdown.CreatePipeline();
        DocWizMarkdown.ParseAll(project, pipeline);
        var view = new ViewDefinition
        {
            Id = "v", Title = "V", Audience = "operator", Mode = "slides",
            Slides = [new SlideDef { Chapter = "intro" }],
        };

        var html = ViewRenderer.RenderSlides(project, view, pipeline);

        // One slide per H2/H3 = Alpha, Alpha-One, Beta, each titled + carrying data-src-anchor.
        Assert.Contains("<h1>Alpha</h1>", html);
        Assert.Contains("<h1>Alpha-One</h1>", html);
        Assert.Contains("<h1>Beta</h1>", html);
        Assert.Contains("data-src-anchor=\"intro#alpha\"", html);
        Assert.Contains("data-src-anchor=\"intro#alpha-one\"", html);
        Assert.Contains("data-src-anchor=\"intro#beta\"", html);
        // The Alpha slide must hold ONLY its own body, NOT its H3 child's body.
        var alpha = Slice(html, "intro#alpha\"", "intro#alpha-one\"");
        Assert.Contains("Alpha body.", alpha);
        Assert.DoesNotContain("A1 body.", alpha);
    }

    [Fact]
    public void ChapterDirective_EditMode_StampsItsEntrySlotAndReadOnlyMarker()
    {
        // The editor maps each expanded slide back to its ONE slides[] entry (rail, canvas,
        // zone collect) and must not mount zone editors on it. Read/handover stays unstamped.
        var project = Project();
        var pipeline = DocWizMarkdown.CreatePipeline();
        DocWizMarkdown.ParseAll(project, pipeline);
        var view = new ViewDefinition
        {
            Id = "v", Title = "V", Audience = "operator", Mode = "slides",
            Slides = [new SlideDef { Layout = "title-content", Title = "First" }, new SlideDef { Chapter = "intro" }],
        };

        var edit = ViewRenderer.RenderSlides(project, view, pipeline, edit: true);
        Assert.Equal(3, Count(edit, "data-slide-index=\"1\" data-chapter-slide")); // Alpha, Alpha-One, Beta
        Assert.Equal(1, Count(edit, "data-slide-index=\"0\""));

        var read = ViewRenderer.RenderSlides(project, view, pipeline);
        Assert.DoesNotContain("data-chapter-slide", read);
        Assert.DoesNotContain("data-slide-index", read);
    }

    [Fact]
    public void ChapterDirective_UnknownChapter_FailsValidation()
    {
        var project = Project(
        [
            new ViewDefinition
            {
                Id = "v", Title = "V", Audience = "operator", Mode = "slides",
                FilePath = "views/v.yaml", Slides = [new SlideDef { Chapter = "nope" }],
            },
        ]);
        DocWizMarkdown.ParseAll(project, DocWizMarkdown.CreatePipeline());
        var issues = ProjectValidator.Validate(project);
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Error && i.Message.Contains("nope"));
    }

    [Fact]
    public void ChapterDirective_CombinedWithRegions_FailsValidation()
    {
        var project = Project(
        [
            new ViewDefinition
            {
                Id = "v", Title = "V", Audience = "operator", Mode = "slides",
                FilePath = "views/v.yaml",
                Slides = [new SlideDef { Chapter = "intro", Markdown = "x" }],
            },
        ]);
        DocWizMarkdown.ParseAll(project, DocWizMarkdown.CreatePipeline());
        var issues = ProjectValidator.Validate(project);
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Error);
    }

    private static int Count(string s, string needle) =>
        (s.Length - s.Replace(needle, "").Length) / needle.Length;

    private static string Slice(string s, string from, string to)
    {
        var a = s.IndexOf(from, System.StringComparison.Ordinal);
        if (a < 0) return "";
        var b = s.IndexOf(to, a + from.Length, System.StringComparison.Ordinal);
        return b < 0 ? s[a..] : s[a..b];
    }
}
