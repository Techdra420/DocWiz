using DocWiz.Core.Markdown;
using DocWiz.Core.Models;
using DocWiz.Core.Slides;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Xunit;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DocWiz.Core.Tests;

/// <summary>Task 10 — the pure draft-from-manual generator. Builds chapters through the
/// one shared pipeline (so figure-N / table-N anchors are assigned exactly as a real
/// build sees them), then asserts the SHAPE of the drafted deck: the layout chosen per
/// subsection and that EVERY emitted embed ref resolves to a real anchor (the hard
/// requirement — the output must pass `docwiz check`).</summary>
public class DeckDrafterTests
{
    /// <summary>A DocProject with the given chapters, each parsed through the one shared
    /// pipeline so anchors (incl. figure-N / table-N) are assigned. Mirrors the
    /// SlideLayoutRenderingTests.TestProject helper. Audiences are taken from the chapter
    /// frontmatter list so the manifest declares them (no unknown-audience noise).</summary>
    private static DocProject TestProject(params (string Id, string[] Audiences, string Markdown)[] chapters)
    {
        var pipeline = DocWizMarkdown.CreatePipeline();
        var chapterList = chapters.Select(c =>
        {
            var ch = new Chapter
            {
                Id = c.Id,
                Title = c.Id,
                FilePath = $"{c.Id}.md",
                SectionSlug = "s",
                SectionTitle = "S",
                SortKey = "0",
                Markdown = c.Markdown,
                Audiences = { },
            };
            ch.Audiences.AddRange(c.Audiences);
            ch.Parsed = Markdig.Markdown.Parse(c.Markdown, pipeline);
            ch.Anchors.AddRange(DocWizMarkdown.CollectAnchors(ch.Parsed));
            return ch;
        }).ToList();

        var allAudiences = chapters.SelectMany(c => c.Audiences).Distinct().DefaultIfEmpty("user");

        return new DocProject
        {
            Root = Path.GetTempPath(),
            Manifest = new ProjectManifest
            {
                Project = "Test Project",
                Code = "TP-001",
                Audiences = allAudiences.Select(a => new AudienceDef { Id = a, Title = a }).ToList(),
            },
            Chapters = chapterList,
            Views = [],
        };
    }

    private const string RichChapter = """
        # System Overview

        ## What the system does {#what-it-does}

        The system tracks every item from receipt to dispatch.

        ![Dashboard](assets/dashboard.png)

        One cycle takes about 42 seconds.

        ## Main components {#main-components}

        | Component | Description |
        |-----------|-------------|
        | Application server | Web application and API |
        | Scanner station | Handheld barcode scanner |

        ## Receiving workflow {#receiving-workflow}

        1. Scan the delivery note.
        2. Scan each item.
        3. Confirm the shelf.
        """;

    private static (DocProject Project, Chapter Chapter) RichProject()
    {
        var project = TestProject(("system-overview", ["user", "support"], RichChapter));
        return (project, project.Chapters[0]);
    }

    /// <summary>Every embed reference the drafted deck emits, across all regions, in order.</summary>
    private static List<string> AllEmbedRefs(ViewDefinition view, Markdig.MarkdownPipeline pipeline) =>
        view.Slides
            .SelectMany(s => s.EffectiveRegions().Values)
            .SelectMany(md => DocWizEmbed.FindRefs(md, pipeline))
            .ToList();

    [Fact]
    public void Draft_setsViewMetadata_slidesMode_audienceFromChapter()
    {
        var (project, chapter) = RichProject();
        var view = DeckDrafter.Draft(project, chapter);

        Assert.Equal("slides", view.Mode);
        Assert.Equal("user", view.Audience);          // chapter's first audience
        Assert.False(string.IsNullOrWhiteSpace(view.Id));
        Assert.Contains(chapter.Id, view.Id);             // id derived from the chapter
        Assert.False(string.IsNullOrWhiteSpace(view.Title));
    }

    [Fact]
    public void Draft_explicitAudience_overridesChapterDefault()
    {
        var (project, chapter) = RichProject();
        var view = DeckDrafter.Draft(project, chapter, "support");
        Assert.Equal("support", view.Audience);
    }

    [Fact]
    public void Draft_firstSlide_isSectionHeader_withChapterTitle()
    {
        var (project, chapter) = RichProject();
        var view = DeckDrafter.Draft(project, chapter);

        var first = view.Slides[0];
        Assert.Equal(SlideLayout.SectionHeader, first.EffectiveLayout);
        Assert.Equal(chapter.Title, first.Title);
        // section-header has no regions
        Assert.Empty(SlideLayout.RegionsFor(first.EffectiveLayout));
    }

    [Fact]
    public void Draft_loneTableSubsection_isBigMedia_withSectionEmbed()
    {
        var (project, chapter) = RichProject();
        var view = DeckDrafter.Draft(project, chapter);

        // "Main components" is a lone table (no prose) → big-media. The embed is the
        // SECTION anchor (not table-N): the section transcludes to just the table (the
        // heading line is dropped). A #table-N embed would also render now, but the drafter
        // deliberately defaults to the SECTION embed (simpler, tracks section headings). See
        // the granularity note on DeckDrafter.
        var slide = view.Slides.Single(s => s.Title == "Main components");
        Assert.Equal(SlideLayout.BigMedia, slide.EffectiveLayout);
        var media = slide.EffectiveRegions()["media"];
        Assert.Equal(DocWizEmbed.DirectiveFor("system-overview#main-components"), media);
    }

    [Fact]
    public void Draft_proseOnlySubsection_isTitleContent_withSectionEmbed()
    {
        var (project, chapter) = RichProject();
        var view = DeckDrafter.Draft(project, chapter);

        // "Receiving workflow" is prose (a list) only → title-content, body = the SECTION embed.
        var slide = view.Slides.Single(s => s.Title == "Receiving workflow");
        Assert.Equal(SlideLayout.TitleContent, slide.EffectiveLayout);
        var body = slide.EffectiveRegions()["body"];
        Assert.Equal(DocWizEmbed.DirectiveFor("system-overview#receiving-workflow"), body);
    }

    [Fact]
    public void Draft_proseWithFigureSubsection_isTitleContent_withSectionEmbed()
    {
        var (project, chapter) = RichProject();
        var view = DeckDrafter.Draft(project, chapter);

        // "What the system does" has prose + a figure. Documented granularity decision
        // (option b): collapse to a single title-content SECTION embed — prose and its
        // figure flow together on one slide. (two-column "section | element" is NOT
        // auto-emitted: there is no prose-only anchor for the other column, so the author
        // adds two-column live — not because element embeds can't render.)
        var slide = view.Slides.Single(s => s.Title == "What the system does");
        Assert.Equal(SlideLayout.TitleContent, slide.EffectiveLayout);
        var body = slide.EffectiveRegions()["body"];
        Assert.Equal(DocWizEmbed.DirectiveFor("system-overview#what-it-does"), body);
    }

    [Fact]
    public void Draft_neverEmitsTwoColumn_fromADraft()
    {
        // two-column requires a clean prose|figure split that an auto-draft can't express
        // (there is no prose-only anchor for one column) — so a draft never emits it.
        var (project, chapter) = RichProject();
        var view = DeckDrafter.Draft(project, chapter);
        Assert.DoesNotContain(view.Slides, s => s.EffectiveLayout == SlideLayout.TwoColumn);
    }

    [Fact]
    public void Draft_everyEmittedEmbedRef_isAHeadingAnchor_thatResolvesAndRenders()
    {
        // The HARD requirement: every emitted embed ref must be in chapter.Anchors so it
        // passes `docwiz check` AND resolves at render time. The drafter additionally
        // chooses HEADING anchors specifically (its documented simplicity default — element
        // anchors also resolve now, but the drafter tracks section headings). This test
        // pins that current behaviour: every drafted ref is a heading anchor.
        var (project, chapter) = RichProject();
        var pipeline = DocWizMarkdown.CreatePipeline();
        var view = DeckDrafter.Draft(project, chapter);

        var headingAnchors = chapter.Parsed!.Descendants<Markdig.Syntax.HeadingBlock>()
            .Select(h => h.TryGetAttributes()?.Id)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet();

        var refs = AllEmbedRefs(view, pipeline);
        Assert.NotEmpty(refs);
        foreach (var reference in refs)
        {
            var (chapterId, anchor) = LinkRewriter.SplitRef(reference);
            Assert.Equal(chapter.Id, chapterId);
            Assert.NotNull(anchor);
            Assert.Contains(anchor!, chapter.Anchors);   // passes validator and resolves
            Assert.Contains(anchor, headingAnchors);     // drafter's choice: a heading anchor
        }
    }

    [Fact]
    public void Draft_chapterWithNoSubsections_stillProducesSectionHeaderOnly()
    {
        var project = TestProject(("intro", ["user"], "# Intro\n\nJust a paragraph, no subsections."));
        var view = DeckDrafter.Draft(project, project.Chapters[0]);

        Assert.Single(view.Slides);
        Assert.Equal(SlideLayout.SectionHeader, view.Slides[0].EffectiveLayout);
    }

    [Fact]
    public void Draft_idIsCollisionAware_whenADraftAlreadyExists()
    {
        var (project, chapter) = RichProject();
        var first = DeckDrafter.Draft(project, chapter);
        // Simulate the first draft already saved as a view.
        project.Views.Add(new ViewDefinition { Id = first.Id });

        var second = DeckDrafter.Draft(project, chapter);
        Assert.NotEqual(first.Id, second.Id);
    }

    /// <summary>The CLI verb and the editor both serialize the drafted view with the
    /// app's view-YAML config (underscored naming + OmitNull). YamlDotNet may pick a
    /// folded block scalar for the two-line embed directive — this locks that the region
    /// strings round-trip back to the EXACT canonical DocWizEmbed.DirectiveFor form
    /// (so the renderer/validator's embed parsing still sees a clean two-line container).</summary>
    [Fact]
    public void Draft_serializedDeck_roundTripsEmbedDirectivesExactly()
    {
        var (project, chapter) = RichProject();
        var view = DeckDrafter.Draft(project, chapter);

        var yamlOut = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();
        var yamlIn = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var back = yamlIn.Deserialize<ViewDefinition>(yamlOut.Serialize(view));

        // Same number of slides, same layouts, and every region string is byte-identical
        // to the canonical embed directive for its ref.
        Assert.Equal(view.Slides.Count, back.Slides.Count);
        for (var i = 0; i < view.Slides.Count; i++)
        {
            Assert.Equal(view.Slides[i].EffectiveLayout, back.Slides[i].EffectiveLayout);
            var before = view.Slides[i].Regions;
            var after = back.Slides[i].Regions;
            if (before is null)
            {
                Assert.True(after is null || after.Count == 0);
                continue;
            }
            foreach (var (name, md) in before)
                Assert.Equal(md, after![name]);
        }

        // And a sample region is exactly the two-line directive (no blank line crept in).
        var sample = back.Slides.Single(s => s.Title == "What the system does");
        Assert.Equal(DocWizEmbed.DirectiveFor("system-overview#what-it-does"), sample.Regions!["body"]);
    }
}
