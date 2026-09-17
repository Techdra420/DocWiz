using DocWiz.Build;
using DocWiz.Core.Loading;
using DocWiz.Core.Markdown;
using DocWiz.Core.Models;
using DocWiz.Core.Validation;
using Xunit;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DocWiz.Core.Tests;

public class TransclusionResolverTests
{
    private static Chapter MakeChapter(string markdown)
    {
        var chapter = new Chapter
        {
            Id = "ch", Title = "Chapter Title", FilePath = "ch.md",
            SectionSlug = "s", SectionTitle = "S", SortKey = "0", Markdown = markdown,
        };
        chapter.Parsed = Markdig.Markdown.Parse(markdown, DocWizMarkdown.CreatePipeline());
        return chapter;
    }

    private const string Source = """
        # Chapter Title

        Intro paragraph.

        ## First {#first}

        First content.

        ### Sub of first

        Sub content.

        ## Second {#second}

        Second content.
        """;

    [Fact]
    public void Resolve_Anchor_TakesContentUntilNextSameLevelHeading()
    {
        var section = TransclusionResolver.Resolve(MakeChapter(Source), "first");

        Assert.Equal("First", section.Title);
        Assert.Equal("ch#first", section.SourceRef);
        Assert.Contains("First content.", section.Markdown);
        Assert.Contains("Sub content.", section.Markdown);      // deeper heading stays inside
        Assert.DoesNotContain("Second content.", section.Markdown);
        Assert.DoesNotContain("## First", section.Markdown);    // the heading itself is stripped
    }

    [Fact]
    public void Resolve_WholeChapter_DropsLeadingH1()
    {
        var section = TransclusionResolver.Resolve(MakeChapter(Source), null);

        Assert.Equal("Chapter Title", section.Title);
        Assert.DoesNotContain("# Chapter Title", section.Markdown);
        Assert.Contains("Intro paragraph.", section.Markdown);
        Assert.Contains("Second content.", section.Markdown);
    }

    [Fact]
    public void Resolve_LastSection_RunsToEndOfChapter()
    {
        var section = TransclusionResolver.Resolve(MakeChapter(Source), "second");
        Assert.Equal("Second content.", section.Markdown);
    }

    // ---- element anchors (figure-N / table-N / attachment-N and explicit {#id}) ----
    // The live "Insert linked element" picker offers these element anchors (they are in
    // chapter.Anchors). They must resolve to the element's verbatim source so the embed
    // re-renders that figure/table/attachment — not throw, not a heading section.

    private static string RenderFragment(string markdown)
    {
        var pipeline = DocWizMarkdown.CreatePipeline();
        return DocWizMarkdown.RenderHtml(Markdig.Markdown.Parse(markdown, pipeline), pipeline);
    }

    private const string ElementSource = """
        # Chapter Title

        ## Specs {#specs-heading}

        {#specs widths="30% 70%"}
        | Part | Detail |
        |------|--------|
        | Motor | 3 kW |

        Some prose between.

        ![Cell render front view](assets/cell.png){width=640}

        See [the layout plan](../../attachments/plan.pdf) for details.
        """;

    [Fact]
    public void Resolve_TableExplicitId_ReturnsTableSource_RendersTableFigure()
    {
        var section = TransclusionResolver.Resolve(MakeChapter(ElementSource), "specs");

        Assert.Equal("ch#specs", section.SourceRef);
        // Verbatim source must include the {#id widths=…} attribute line so columns survive.
        Assert.Contains("{#specs widths=\"30% 70%\"}", section.Markdown);
        Assert.DoesNotContain("Some prose between.", section.Markdown);

        var html = RenderFragment(section.Markdown);
        Assert.Contains("dw-table-figure", html);
        Assert.Contains("<colgroup>", html);
        Assert.Contains("Motor", html);
        Assert.Contains("3 kW", html);
        Assert.DoesNotContain("dw-image-figure", html);
    }

    [Fact]
    public void Resolve_TableAutoAnchor_ResolvesTheTable()
    {
        // A table with no explicit {#id} gets the assigned table-1 anchor (the live
        // picker offers it). It must resolve, including its {widths=…} attribute line.
        var md = "# C\n\nIntro.\n\n{widths=\"40% 60%\"}\n| Part | Detail |\n|------|--------|\n| Motor | 3 kW |\n\nAfter.\n";
        var section = TransclusionResolver.Resolve(MakeChapter(md), "table-1");

        Assert.Equal("ch#table-1", section.SourceRef);
        Assert.Contains("{widths=\"40% 60%\"}", section.Markdown);
        Assert.DoesNotContain("After.", section.Markdown);

        var html = RenderFragment(section.Markdown);
        Assert.Contains("dw-table-figure", html);
        Assert.Contains("<colgroup>", html);
        Assert.Contains("Motor", html);
    }

    [Fact]
    public void Resolve_FigureAutoAnchor_ReturnsImageSource_RendersImageFigure()
    {
        var section = TransclusionResolver.Resolve(MakeChapter(ElementSource), "figure-1");

        Assert.Equal("ch#figure-1", section.SourceRef);
        Assert.Contains("assets/cell.png", section.Markdown);
        Assert.Contains("width=640", section.Markdown); // the {width=…} suffix survives

        var html = RenderFragment(section.Markdown);
        Assert.Contains("dw-image-figure", html);
        Assert.Contains("Cell render front view", html);
        Assert.Contains("width=\"640\"", html);
        Assert.DoesNotContain("dw-table-figure", html);
    }

    [Fact]
    public void Resolve_AttachmentAnchor_ReturnsContainingParagraph()
    {
        var section = TransclusionResolver.Resolve(MakeChapter(ElementSource), "attachment-1");

        Assert.Equal("ch#attachment-1", section.SourceRef);
        var html = RenderFragment(section.Markdown);
        Assert.Contains("plan.pdf", html);
        Assert.Contains("the layout plan", html);
    }

    // A standalone image's EXPLICIT {#id} is attached by Markdig to the inline image
    // (LinkInline), not to the containing paragraph (which keeps its auto figure-N). The
    // id is collected into chapter.Anchors and the picker offers it, so it must resolve to
    // the figure — symmetric with the auto figure-N branch — or check-0 ≠ build-works.

    [Fact]
    public void Resolve_FigureExplicitId_ReturnsImageSource_RendersImageFigure()
    {
        var md = "# C\n\nIntro.\n\n![Hero shot](assets/hero.png){#hero width=480}\n\nAfter.\n";
        var section = TransclusionResolver.Resolve(MakeChapter(md), "hero");

        Assert.Equal("ch#hero", section.SourceRef);
        Assert.Equal("Hero shot", section.Title);           // alt text, like the figure-N branch
        Assert.Contains("assets/hero.png", section.Markdown);
        Assert.Contains("{#hero width=480}", section.Markdown); // the {#id width=…} suffix survives
        Assert.DoesNotContain("After.", section.Markdown);

        var html = RenderFragment(section.Markdown);
        Assert.Contains("dw-image-figure", html);
        Assert.Contains("Hero shot", html);
        Assert.Contains("width=\"480\"", html);
        Assert.Contains("id=\"hero\"", html);               // explicit id survives onto the <img>
        Assert.DoesNotContain("dw-table-figure", html);
    }

    [Fact]
    public void Resolve_InlineImageInProseWithExplicitId_IsNotAFigure_Throws()
    {
        // An explicit-id image INSIDE prose is not a standalone figure paragraph; embedding
        // it would not re-parse as a figure, so it must NOT resolve (falls through to throw,
        // consistent with "not a figure anchor").
        var md = "# C\n\nText with ![inline](assets/x.png){#inlinehero} in the middle of a sentence.\n";
        Assert.Throws<TransclusionResolver.TransclusionException>(
            () => TransclusionResolver.Resolve(MakeChapter(md), "inlinehero"));
    }

    [Fact]
    public void Resolve_ImageInTableCellWithExplicitId_IsNotAFigure_Throws()
    {
        // An image in a TABLE CELL is not a standalone figure (FigureParagraphRenderer and
        // AssignIds both skip TableCell paragraphs), so its explicit id must not resolve.
        var md = "# C\n\n| Item | Pic |\n|------|-----|\n| Motor | ![m](assets/m.png){#cellhero} |\n";
        Assert.Throws<TransclusionResolver.TransclusionException>(
            () => TransclusionResolver.Resolve(MakeChapter(md), "cellhero"));
    }

    [Fact]
    public void Resolve_DanglingAnchor_StillThrows()
    {
        Assert.Throws<TransclusionResolver.TransclusionException>(
            () => TransclusionResolver.Resolve(MakeChapter(ElementSource), "no-such-anchor"));
    }
}

public class AudienceFilterTests
{
    [Fact]
    public void Apply_RemovesNonMatching_UnwrapsMatching()
    {
        var pipeline = DocWizMarkdown.CreatePipeline();
        var doc = Markdig.Markdown.Parse("""
            Always visible.

            ::: audience operator
            Operator text.
            :::

            ::: audience engineering
            Engineering text.
            :::
            """, pipeline);

        AudienceFilter.Apply(doc, "operator");
        var html = DocWizMarkdown.RenderHtml(doc, pipeline);

        Assert.Contains("Always visible.", html);
        Assert.Contains("Operator text.", html);
        Assert.DoesNotContain("Engineering text.", html);
        Assert.DoesNotContain("aud-tag", html);   // matching container unwrapped, no tag box
        Assert.DoesNotContain("data-aud", html);
    }
}

public class SlideLayoutTests
{
    [Fact]
    public void Regions_listsTheKnownLayoutsAndTheirRegionNames()
    {
        Assert.True(SlideLayout.IsKnown("title-content"));
        Assert.True(SlideLayout.IsKnown("two-column"));
        Assert.True(SlideLayout.IsKnown("big-media"));
        Assert.True(SlideLayout.IsKnown("section-header"));
        Assert.False(SlideLayout.IsKnown("free-canvas"));

        Assert.Equal(new[] { "body" }, SlideLayout.RegionsFor("title-content"));
        Assert.Equal(new[] { "left", "right" }, SlideLayout.RegionsFor("two-column"));
        Assert.Equal(new[] { "media" }, SlideLayout.RegionsFor("big-media"));
        Assert.Empty(SlideLayout.RegionsFor("section-header"));
    }
}

public class SlideDefNormalizationTests
{
    [Fact]
    public void LegacyIncludeSlide_normalizesToTitleContentBody()
    {
        var slide = new SlideDef { Include = "ch#first", Title = "T" };
        Assert.Equal(SlideLayout.TitleContent, slide.EffectiveLayout);
        var regions = slide.EffectiveRegions();
        Assert.Equal(DocWizEmbed.DirectiveFor("ch#first"), regions["body"]);
    }

    [Fact]
    public void LegacyMarkdownSlide_normalizesToTitleContentBody()
    {
        var slide = new SlideDef { Markdown = "hello" };
        Assert.Equal(SlideLayout.TitleContent, slide.EffectiveLayout);
        Assert.Equal("hello", slide.EffectiveRegions()["body"]);
    }

    [Fact]
    public void ExplicitRegions_takePrecedenceOverLegacyFields()
    {
        var slide = new SlideDef
        {
            Layout = SlideLayout.TwoColumn,
            Markdown = "ignored",
            Regions = new()
            {
                ["left"] = "L",
                ["right"] = DocWizEmbed.DirectiveFor("ch#fig"),
            },
        };
        Assert.Equal(SlideLayout.TwoColumn, slide.EffectiveLayout);
        var r = slide.EffectiveRegions();
        Assert.Equal("L", r["left"]);
        Assert.Equal(DocWizEmbed.DirectiveFor("ch#fig"), r["right"]);
    }
}

public class SlideLayoutRenderingTests
{
    /// <summary>Builds a minimal DocProject with the given chapters (Id, markdown string).
    /// Each chapter is parsed through the one shared pipeline so transclusion works.</summary>
    private static DocProject TestProject(params (string Id, string Markdown)[] chapters)
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

    [Fact]
    public void TwoColumn_rendersBothRegions_withDataLayoutAndLinkedContent()
    {
        var project = TestProject(("070-screens", "# Screens\n\n## Home {#home}\n\n![Screen](assets/screen.png)"));
        var view = new ViewDefinition
        {
            Id = "ops", Title = "Ops", Audience = "operator", Mode = "slides",
            Slides =
            [
                new SlideDef
                {
                    Layout = SlideLayout.TwoColumn, Title = "Screen",
                    Regions = new()
                    {
                        ["left"] = "- start\n- stop",
                        ["right"] = DocWizEmbed.DirectiveFor("070-screens#home"),
                    },
                },
            ],
        };
        var html = ViewRenderer.RenderSlides(project, view, DocWizMarkdown.CreatePipeline());
        Assert.Contains("data-layout=\"two-column\"", html);
        Assert.Contains("data-region=\"left\"", html);
        Assert.Contains("data-region=\"right\"", html);
        Assert.Contains("<li>start</li>", html);
        Assert.Contains("screen.png", html);
    }

    [Fact]
    public void Region_rendersAuthoredBulletsAndTranscludedFigureInline()
    {
        var project = TestProject(("070-screens", "# Screens\n\n## Fig {#fig}\n\n![Screen](assets/screen.png)"));
        var view = new ViewDefinition
        {
            Id = "ops", Title = "Ops", Audience = "operator", Mode = "slides",
            Slides =
            [
                new SlideDef
                {
                    Layout = SlideLayout.TitleContent, Title = "Mixed",
                    Regions = new()
                    {
                        ["body"] = "- a\n- b\n\n" + DocWizEmbed.DirectiveFor("070-screens#fig"),
                    },
                },
            ],
        };
        var html = ViewRenderer.RenderSlides(project, view, DocWizMarkdown.CreatePipeline());
        Assert.Contains("<li>a</li>", html);            // authored content survives
        Assert.Contains("<li>b</li>", html);
        Assert.Contains("screen.png", html);               // transcluded figure resolved inline
        Assert.DoesNotContain("::: embed", html);       // directive does not leak
    }

    [Fact]
    public void SectionHeader_rendersTitleAndSubtitle_noRegions()
    {
        var project = TestProject(); // no chapters needed
        var view = new ViewDefinition
        {
            Id = "v", Title = "V", Audience = "operator", Mode = "slides",
            Slides = [new SlideDef { Layout = SlideLayout.SectionHeader, Title = "Maintenance", Subtitle = "Weekly" }],
        };
        var html = ViewRenderer.RenderSlides(project, view, DocWizMarkdown.CreatePipeline());
        Assert.Contains("data-layout=\"section-header\"", html);
        Assert.Contains("Maintenance", html);
        Assert.Contains("Weekly", html);
    }

    [Fact]
    public void LegacyIncludeSlide_stillRendersAsTitleContent()
    {
        var project = TestProject(("010-overview", "# Overview\n\n## Intro {#intro}\n\nHello."));
        var view = new ViewDefinition
        {
            Id = "v", Title = "V", Audience = "operator", Mode = "slides",
            Slides = [new SlideDef { Include = "010-overview#intro" }],
        };
        var html = ViewRenderer.RenderSlides(project, view, DocWizMarkdown.CreatePipeline());
        Assert.Contains("data-layout=\"title-content\"", html);
        Assert.Contains("Hello.", html);
        // FIX 1: bare-include slide must carry the transcluded section's title as <h1>
        Assert.Contains("Intro", html);
        Assert.Contains("<h1>Intro</h1>", html);
    }

    [Fact]
    public void EditMode_BodyLayoutWithEmptyTitle_RendersEditableTitleField_butReadModeOmitsIt()
    {
        // VISION fix: "adding new slides of any type is missing the headline". A freshly-added
        // body-layout slide (empty title, no derived heading) must be AUTHORABLE — the title /
        // subtitle fields are rendered in EDIT mode even when empty (as the .is-empty
        // placeholder) so the author has a headline to click into. The SHIPPED deck (read mode)
        // must be unchanged: it still omits an empty title/subtitle.
        var project = TestProject();
        var view = new ViewDefinition
        {
            Id = "v", Title = "V", Audience = "operator", Mode = "slides",
            Slides = [new SlideDef { Layout = SlideLayout.TitleContent }], // no title, no subtitle
        };

        var editHtml = ViewRenderer.RenderSlides(project, view, DocWizMarkdown.CreatePipeline(), edit: true);
        Assert.Contains("data-slide-title data-placeholder=\"Add a title…\"", editHtml);
        Assert.Contains("data-slide-sub", editHtml);
        Assert.Contains("class=\"is-empty\"", editHtml);            // empty placeholder marker present

        var readHtml = ViewRenderer.RenderSlides(project, view, DocWizMarkdown.CreatePipeline(), edit: false);
        Assert.DoesNotContain("data-slide-title", readHtml);        // ship deck omits the empty title
        Assert.DoesNotContain("data-slide-sub", readHtml);
    }
}

public class SlideValidationTests
{
    /// <summary>Builds a one-slide slides-view DocProject and validates it.
    /// When <paramref name="withIntro"/> is true, adds a chapter "010-overview"
    /// with anchor "intro" tagged for audience "operator".</summary>
    private static List<ValidationIssue> ValidateView(SlideDef slide, string audience, bool withIntro = false)
    {
        var pipeline = DocWizMarkdown.CreatePipeline();

        var chapters = new List<Chapter>();
        if (withIntro)
        {
            const string md = "# Overview\n\n## Intro {#intro}\n\nHello.";
            var ch = new Chapter
            {
                Id = "010-overview",
                Title = "Overview",
                FilePath = "010-overview.md",
                SectionSlug = "overview",
                SectionTitle = "Overview",
                SortKey = "010",
                Markdown = md,
                Audiences = { "operator" },
            };
            ch.Anchors.Add("intro");
            ch.Parsed = Markdig.Markdown.Parse(md, pipeline);
            chapters.Add(ch);
        }

        // Known audiences: the view's audience plus "operator" (the intro chapter's tag)
        // so a cross-audience embed test does not trip the unknown-audience chapter check.
        var audienceIds = new List<string> { audience };
        if (audience != "operator")
            audienceIds.Add("operator");

        var project = new DocProject
        {
            Root = Path.GetTempPath(),
            Manifest = new ProjectManifest
            {
                Project = "Test",
                Code = "T-001",
                Audiences = audienceIds.Select(a => new AudienceDef { Id = a, Title = a }).ToList(),
            },
            Chapters = chapters,
            Views =
            [
                new ViewDefinition
                {
                    Id = "v",
                    Title = "V",
                    Audience = audience,
                    Mode = "slides",
                    FilePath = Path.Combine(Path.GetTempPath(), "v.yaml"),
                    Slides = [slide],
                },
            ],
        };

        return ProjectValidator.Validate(project);
    }

    [Fact]
    public void UnknownLayout_isError()
    {
        var issues = ValidateView(new SlideDef { Layout = "free-canvas" }, audience: "operator");
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Error && i.Message.Contains("layout"));
    }

    [Fact]
    public void RegionNotValidForLayout_isError()
    {
        var slide = new SlideDef { Layout = SlideLayout.BigMedia, Regions = new() { ["left"] = "x" } };
        var issues = ValidateView(slide, audience: "operator");
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Error && i.Message.Contains("region 'left'"));
    }

    [Fact]
    public void DanglingRegionInclude_isError()
    {
        var slide = new SlideDef { Layout = SlideLayout.TitleContent, Regions = new() { ["body"] = DocWizEmbed.DirectiveFor("nope#x") } };
        var issues = ValidateView(slide, audience: "operator");
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Error && i.Message.Contains("no chapter"));
    }

    [Fact]
    public void LegacyIncludeSlide_stillValidates()
    {
        var issues = ValidateView(new SlideDef { Include = "010-overview#intro" }, audience: "operator", withIntro: true);
        Assert.DoesNotContain(issues, i => i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void EmptyRegion_isWarning()
    {
        // two-column expects left+right; only left filled → 'right' empty → warning
        var slide = new SlideDef { Layout = SlideLayout.TwoColumn, Regions = new() { ["left"] = "x" } };
        var issues = ValidateView(slide, audience: "operator");
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Warning && i.Message.Contains("right") && i.Message.Contains("empty"));
    }

    [Fact]
    public void RegionIncludeWithDanglingAnchor_isError()
    {
        // chapter exists (010-overview, anchor 'intro') but '#nope' does not
        var slide = new SlideDef { Layout = SlideLayout.TitleContent, Regions = new() { ["body"] = DocWizEmbed.DirectiveFor("010-overview#nope") } };
        var issues = ValidateView(slide, audience: "operator", withIntro: true);
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Error && i.Message.Contains("anchor"));
    }

    [Fact]
    public void CleanRegionEmbed_isNoError()
    {
        var slide = new SlideDef { Layout = SlideLayout.TitleContent, Regions = new() { ["body"] = DocWizEmbed.DirectiveFor("010-overview#intro") } };
        var issues = ValidateView(slide, audience: "operator", withIntro: true);
        Assert.DoesNotContain(issues, i => i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void RegionEmbedToChapterNotTaggedForAudience_isWarning()
    {
        // 010-overview is tagged 'operator'; validate the view as 'engineering' → wrong audience.
        var slide = new SlideDef { Layout = SlideLayout.TitleContent, Regions = new() { ["body"] = DocWizEmbed.DirectiveFor("010-overview#intro") } };
        var issues = ValidateView(slide, audience: "engineering", withIntro: true);
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Warning && i.Message.Contains("not tagged for audience"));
        Assert.DoesNotContain(issues, i => i.Severity == IssueSeverity.Error);
    }

    // ---- inline dw: LINKS in region markdown ----
    // A plain [text](dw:chapter#anchor) link in region/markdown body (NOT a ::: embed) must be
    // validated like a chapter link — `docwiz check` only checked embed includes, so a dangling
    // link rendered broken in the handover with no diagnostic.

    [Fact]
    public void RegionLinkToMissingChapter_isError()
    {
        var slide = new SlideDef { Layout = SlideLayout.TitleContent, Regions = new() { ["body"] = "See the [errors](dw:nope#x) table." } };
        var issues = ValidateView(slide, audience: "operator");
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Error && i.Message.Contains("link 'dw:nope#x'") && i.Message.Contains("no chapter"));
    }

    [Fact]
    public void RegionLinkToMissingAnchor_isError()
    {
        // chapter exists (010-overview, anchor 'intro') but '#nope' does not.
        var slide = new SlideDef { Layout = SlideLayout.TitleContent, Regions = new() { ["body"] = "See [intro](dw:010-overview#nope)." } };
        var issues = ValidateView(slide, audience: "operator", withIntro: true);
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Error && i.Message.Contains("link 'dw:010-overview#nope'") && i.Message.Contains("anchor"));
    }

    [Fact]
    public void CleanRegionLink_isNoError()
    {
        var slide = new SlideDef { Layout = SlideLayout.TitleContent, Regions = new() { ["body"] = "See [intro](dw:010-overview#intro)." } };
        var issues = ValidateView(slide, audience: "operator", withIntro: true);
        Assert.DoesNotContain(issues, i => i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void LegacyMarkdownSlide_LinkToMissingChapter_isError()
    {
        // Legacy free-text markdown slides carry dw: links too (via EffectiveRegions) — same gate.
        var issues = ValidateView(new SlideDef { Markdown = "See [errors](dw:nope#x)." }, audience: "operator");
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Error && i.Message.Contains("link 'dw:nope#x'") && i.Message.Contains("no chapter"));
    }
}

/// <summary>The /api/view round-trip (Task 5): a view's markdown regions must survive
/// serialize→deserialize as identical strings. Mirrors EditorServer's YamlOut/YamlIn
/// config (underscored naming, OmitNull) — those serializers are private to the app, so
/// the test reproduces them rather than reaching in.</summary>
public class ViewYamlRoundTripTests
{
    private static readonly ISerializer YamlOut = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    private static readonly IDeserializer YamlIn = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    [Fact]
    public void MarkdownRegions_surviveSerializeDeserialize()
    {
        var view = new ViewDefinition
        {
            Id = "ops", Title = "Ops", Audience = "operator", Mode = "slides",
            Slides =
            [
                new SlideDef
                {
                    Layout = SlideLayout.TwoColumn, Title = "Screen",
                    Regions = new()
                    {
                        ["left"] = "- a",
                        ["right"] = DocWizEmbed.DirectiveFor("ch#fig"),
                    },
                },
            ],
        };

        var yaml = YamlOut.Serialize(view);
        var back = YamlIn.Deserialize<ViewDefinition>(yaml);

        var regions = back.Slides.Single().Regions!;
        Assert.Equal("- a", regions["left"]);
        Assert.Equal(DocWizEmbed.DirectiveFor("ch#fig"), regions["right"]);
    }
}

/// <summary>
/// The check⇔build invariant for MISSING-asset ELEMENT embeds (the Slides v2 build-path
/// correctness bug). A standalone image whose asset file does not exist validates clean
/// (`docwiz check` = 0 errors — the figure anchor is still in chapter.Anchors), so a slides
/// view embedding that figure (`::: embed dw:ch#figure-1`) MUST build without throwing and
/// render the missing-image placeholder — never a TransclusionException.
///
/// These tests drive the FULL <see cref="HandoverBuilder.Build"/> on a real temp project,
/// because the bug only appears when the chapter-page <see cref="LinkRewriter.Rewrite"/> pass
/// has already mutated the shared chapter AST (removing the missing-image inline) BEFORE the
/// views loop reads it for transclusion. A unit test on a pristine, separately-parsed AST
/// (as the other ViewRenderer tests use) would not reproduce it.
/// </summary>
public class MissingAssetElementEmbedBuildTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "docwiz-embed-" + Guid.NewGuid().ToString("N"));

    public MissingAssetElementEmbedBuildTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "content", "010-cell"));
        Directory.CreateDirectory(Path.Combine(_root, "views"));
        Directory.CreateDirectory(Path.Combine(_root, "assets"));

        File.WriteAllText(Path.Combine(_root, "docwiz.yaml"),
            """
            project: Embed Test Cell
            code: ET-001
            audiences:
              - id: operator
                title: Operator
            """);

        // present.png exists; missing.png intentionally does NOT (the placeholder case).
        File.WriteAllText(Path.Combine(_root, "assets", "present.png"), "not-a-real-png-but-the-file-exists");

        // figure-1 = missing asset, figure-2 = present asset, plus a heading section.
        File.WriteAllText(Path.Combine(_root, "content", "010-cell", "010-cell.md"),
            """
            ---
            id: cell
            title: Cell
            audiences: [operator]
            ---

            # Cell

            ## Procedure {#procedure}

            Switch the main disconnect to OFF before opening any panel.

            ![Cell render](assets/missing.png){width=640}

            ![Home screen](assets/present.png){width=320}
            """);

        File.WriteAllText(Path.Combine(_root, "views", "training.yaml"),
            """
            id: training
            title: Operator Training
            audience: operator
            mode: slides
            slides:
              - layout: title-content
                title: Missing-asset figure
                regions:
                  body: |
                    ::: embed dw:cell#figure-1
                    :::
              - layout: title-content
                title: Present-asset figure
                regions:
                  body: |
                    ::: embed dw:cell#figure-2
                    :::
              - layout: title-content
                title: Heading section
                regions:
                  body: |
                    ::: embed dw:cell#procedure
                    :::
            """);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Check_passesForMissingAssetElementEmbed_butBuildRendersPlaceholder_notThrow()
    {
        var project = ProjectLoader.Load(_root);
        var pipeline = DocWizMarkdown.CreatePipeline();
        DocWizMarkdown.ParseAll(project, pipeline);

        // check: 0 errors for the embed (the missing asset is a warning only).
        var issues = ProjectValidator.Validate(project);
        Assert.DoesNotContain(issues, i => i.Severity == IssueSeverity.Error);
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Warning && i.Message.Contains("missing.png"));

        // build: must not throw, and the slides view must show the SAME missing-image
        // placeholder the chapter page shows — never a TransclusionException.
        var outDir = Path.Combine(_root, "output", "handover");
        HandoverBuilder.Build(project, pipeline, outDir);

        var viewHtml = File.ReadAllText(Path.Combine(outDir, "_site", "view-training.html"));
        Assert.DoesNotContain("::: embed", viewHtml);                                   // directive resolved
        Assert.Contains(LinkRewriter.MissingImagePlaceholder("missing.png"), viewHtml); // figure-1 placeholder
        Assert.Contains("assets/present.png", viewHtml);                                // figure-2 still renders
        Assert.Contains("Switch the main disconnect", viewHtml);                        // heading section embed
    }

    [Fact]
    public void ChapterPage_stillRendersMissingImagePlaceholder_afterFix()
    {
        // No-regression: the chapter PAGE output is unchanged — missing image becomes the
        // placeholder, present image keeps its rewritten assets/ URL.
        var project = ProjectLoader.Load(_root);
        var pipeline = DocWizMarkdown.CreatePipeline();
        DocWizMarkdown.ParseAll(project, pipeline);

        var outDir = Path.Combine(_root, "output", "handover");
        HandoverBuilder.Build(project, pipeline, outDir);

        var chapterHtml = File.ReadAllText(Path.Combine(outDir, "_site", "cell.html"));
        Assert.Contains(LinkRewriter.MissingImagePlaceholder("missing.png"), chapterHtml);
        Assert.Contains("assets/present.png", chapterHtml);
    }
}
