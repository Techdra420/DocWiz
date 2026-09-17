using DocWiz.Build;
using DocWiz.Core.Markdown;
using DocWiz.Core.Models;
using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>Editable document views: RenderDocument(edit:true) must emit the same editable
/// card/zone structure the slides surface mounts (so DocWizSlideEdit can edit a document view),
/// while read mode stays free of editing hooks, and slide cards carry an explicit
/// data-slide-index.</summary>
public class EditableDocumentViewTests
{
    private static DocProject TestProject(params (string Id, string Markdown)[] chapters)
    {
        var pipeline = DocWizMarkdown.CreatePipeline();
        var chapterList = chapters.Select(c =>
        {
            var ch = new Chapter
            {
                Id = c.Id, Title = c.Id, FilePath = $"{c.Id}.md",
                SectionSlug = "s", SectionTitle = "S", SortKey = "0", Markdown = c.Markdown,
            };
            ch.Parsed = Markdig.Markdown.Parse(c.Markdown, pipeline);
            return ch;
        }).ToList();

        return new DocProject
        {
            Root = Path.GetTempPath(),
            Manifest = new ProjectManifest { Project = "Test Project", Code = "TP-001" },
            Chapters = chapterList,
            Views = [],
        };
    }

    private static ViewDefinition DocView() => new()
    {
        Id = "eng", Title = "Engineering", Audience = "engineering", Mode = "document",
        Slides =
        [
            new SlideDef { Markdown = "## Drawings\n\nSee the attached PDFs." },
            new SlideDef { Include = "intro#overview" },
        ],
    };

    [Fact]
    public void DocumentEditMode_EmitsEditableCardsWithZones()
    {
        var project = TestProject(("intro", "# Intro\n\n## Overview {#overview}\n\nBody text.\n"));
        var html = ViewRenderer.RenderDocument(project, DocView(), DocWizMarkdown.CreatePipeline(), edit: true);

        Assert.Contains("class=\"view-sec\"", html);
        Assert.Contains("data-slide-index=\"0\"", html);
        Assert.Contains("data-slide-index=\"1\"", html);
        Assert.Contains("data-slide-title", html);                            // editable title field
        Assert.Contains("class=\"slide-region\" data-region=\"body\"", html); // editable zone
        Assert.DoesNotContain("srcline", html);                              // no attribution in edit mode
    }

    [Fact]
    public void DocumentReadMode_HasNoEditingHooks()
    {
        var project = TestProject(("intro", "# Intro\n\n## Overview {#overview}\n\nBody text.\n"));
        var html = ViewRenderer.RenderDocument(project, DocView(), DocWizMarkdown.CreatePipeline()); // edit defaults false

        Assert.Contains("class=\"view-sec\"", html);
        Assert.DoesNotContain("data-slide-index", html);
        Assert.DoesNotContain("data-slide-title", html);
    }

    [Fact]
    public void SlidesEditMode_StampsSlideIndexOnEachSlide()
    {
        var project = TestProject(("intro", "# Intro\n\n## Overview {#overview}\n\nBody text.\n"));
        var view = new ViewDefinition
        {
            Id = "deck", Title = "Deck", Audience = "engineering", Mode = "slides",
            Slides = [new SlideDef { Markdown = "Hello" }, new SlideDef { Include = "intro#overview" }],
        };
        var html = ViewRenderer.RenderSlides(project, view, DocWizMarkdown.CreatePipeline(), edit: true);

        Assert.Contains("data-slide-index=\"0\"", html);
        Assert.Contains("data-slide-index=\"1\"", html);
    }

    [Fact]
    public void SlidesReadMode_HasNoSlideIndex()
    {
        var project = TestProject(("intro", "# Intro\n\n## Overview {#overview}\n\nBody text.\n"));
        var view = new ViewDefinition
        {
            Id = "deck", Title = "Deck", Audience = "engineering", Mode = "slides",
            Slides = [new SlideDef { Markdown = "Hello" }],
        };
        var html = ViewRenderer.RenderSlides(project, view, DocWizMarkdown.CreatePipeline()); // read

        Assert.DoesNotContain("data-slide-index", html);
    }
}
