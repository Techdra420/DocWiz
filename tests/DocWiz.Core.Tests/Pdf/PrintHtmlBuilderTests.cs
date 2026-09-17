using System.Linq;
using DocWiz.Build.Pdf;
using DocWiz.Core.Markdown;
using DocWiz.Core.Models;
using Xunit;

namespace DocWiz.Core.Tests.Pdf;

public class PrintHtmlBuilderTests
{
    private static PrintHtmlBuilder Builder(DocProject project) => new(project.Root);

    [Fact]
    public void Manual_HasCoverWithProjectMetadata()
    {
        var (project, pipeline) = TestRepo.LoadDemoProject();
        var html = Builder(project).BuildManual(project, pipeline);
        Assert.Contains(project.Manifest.Project, html);
        Assert.Contains(project.Manifest.Code, html);
        Assert.Contains("pdf-cover", html);
    }

    [Fact]
    public void Manual_EveryChapterWrappedWithBreakAndIdAnchor()
    {
        var (project, pipeline) = TestRepo.LoadDemoProject();
        var html = Builder(project).BuildManual(project, pipeline);
        foreach (var ch in project.Chapters)
            Assert.Contains($"id=\"{ch.Id}\"", html);
        Assert.Contains("pdf-chapter", html);
    }

    [Fact]
    public void Manual_NamespacesInnerIds_AvoidingCrossChapterCollision()
    {
        var (project, pipeline) = TestRepo.LoadDemoProject();
        var html = Builder(project).BuildManual(project, pipeline);
        var first = project.Chapters[0].Id;
        Assert.Contains($"id=\"{first}__", html);
    }

    [Fact]
    public void Manual_EmbeddedPdfBecomesReferenceNote()
    {
        var pipeline = DocWizMarkdown.CreatePipeline();
        var project = new DocProject
        {
            Root = @"C:\work\proj",
            Manifest = new ProjectManifest { Project = "P", Code = "C", Language = "en" },
            Chapters =
            [
                new Chapter
                {
                    Id = "ch1",
                    Title = "Chapter One",
                    FilePath = @"C:\work\proj\content\010-s\010-ch1.md",
                    SectionSlug = "s",
                    SectionTitle = "S",
                    SortKey = "010-010",
                    Markdown = "See the [drawing](attachments/x.pdf) here.",
                }
            ],
            Views = [],
        };

        var html = Builder(project).BuildManual(project, pipeline);

        Assert.DoesNotContain("<iframe", html);
        Assert.Contains("pdf-attachment-note", html);
    }

    [Fact]
    public void BuildManual_WithChapterFilter_RendersOnlySelectedChaptersAndSubsetCover()
    {
        var (project, pipeline) = TestRepo.LoadDemoProject();
        var first = project.Chapters[0];
        var other = project.Chapters[1];

        var html = Builder(project).BuildManual(project, pipeline, new[] { first.Id });

        Assert.Contains($"id=\"{first.Id}\"", html);       // selected chapter section present
        Assert.DoesNotContain($"id=\"{other.Id}\"", html); // unselected chapter section absent
        Assert.Contains("Selected chapters", html);         // subset cover marker
    }

    [Fact]
    public void BuildManual_NullFilter_StillRendersEveryChapter()
    {
        var (project, pipeline) = TestRepo.LoadDemoProject();
        var html = Builder(project).BuildManual(project, pipeline, null);
        foreach (var ch in project.Chapters)
            Assert.Contains($"id=\"{ch.Id}\"", html);
        Assert.DoesNotContain("Selected chapters", html);
    }

    [Fact]
    public void SlidesView_EmitsOnePagePerSlideIncludingTitle()
    {
        var (project, pipeline) = TestRepo.LoadDemoProject();
        var view = project.Views.First(v => v.Mode == "slides");
        var html = Builder(project).BuildView(project, view, pipeline);
        // ViewRenderer prepends one auto title slide (class="slide active") + one per view slide.
        // Match slide-page divs only: `slide` followed by a quote or space — NOT the hyphenated
        // inner classes (slide-sub, slide-region) that a bare `class="slide` substring would catch.
        var count = System.Text.RegularExpressions.Regex.Matches(html, "class=\"slide[\" ]").Count;
        Assert.Equal(view.Slides.Count + 1, count);
        Assert.Contains("20in 11.25in", html); // native 1920x1080 page size
    }

    [Fact]
    public void Absolutize_PrefixesDerivedFromProjectLayout_NotHardcodedLiterals()
    {
        // Pins the parity gate (ADR-008, review C1): Absolutize must absolutize URLs whose
        // prefix is ProjectLayout.AssetsFolder/AttachmentsFolder — the same constants
        // LinkRewriter.Rewrite emits. If either constant is renamed, this test fails instead
        // of an exported PDF silently shipping un-absolutized (broken) image/attachment URLs.
        var builder = new PrintHtmlBuilder(@"C:\work\proj");

        var assetHtml = builder.Absolutize($"<img src=\"{ProjectLayout.AssetsFolder}/x.png\">");
        Assert.Contains($"/{ProjectLayout.AssetsFolder}/x.png", assetHtml);
        Assert.DoesNotContain($"\"{ProjectLayout.AssetsFolder}/x.png", assetHtml);

        var attachmentHtml = builder.Absolutize($"<a href=\"{ProjectLayout.AttachmentsFolder}/y.pdf\">");
        Assert.Contains($"/{ProjectLayout.AttachmentsFolder}/y.pdf", attachmentHtml);
        Assert.DoesNotContain($"\"{ProjectLayout.AttachmentsFolder}/y.pdf", attachmentHtml);
    }

    [Fact]
    public void DocumentView_RendersPortraitContentNoCover()
    {
        var (project, pipeline) = TestRepo.LoadDemoProject();
        var view = project.Views.First(v => v.Mode == "document");
        var html = Builder(project).BuildView(project, view, pipeline);
        // The cover is a manual-only element; the document view must not emit it. Match the element
        // markup, not the bare "pdf-cover" substring — the inlined print.css contains the selector.
        Assert.DoesNotContain("class=\"pdf-cover\"", html);
        Assert.Contains(view.Title, html);
    }
}
