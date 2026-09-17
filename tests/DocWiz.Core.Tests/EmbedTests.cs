using DocWiz.Core.Markdown;
using DocWiz.Core.Models;
using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>
/// The `::: embed dw:chapter#anchor :::` directive: a no-body custom container that the
/// project-aware <see cref="DocWizEmbed.Resolve"/> pass replaces in place with the
/// transcluded section's parsed content (re-parsed through the one shared pipeline).
/// Canonical stored form is the TWO-LINE container (`::: embed dw:ch#sec` then `:::`) —
/// the stock CustomContainers parser swallows the trailing `:::` of the one-line form
/// into the arguments and leaves the block open to consume following content.
/// </summary>
public class EmbedTests
{
    /// <summary>Minimal project with chapters parsed through the one shared pipeline.</summary>
    private static DocProject TestProject(params (string Id, string Markdown)[] chapters)
    {
        var pipeline = DocWizMarkdown.CreatePipeline();
        var list = chapters.Select(c =>
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
            Manifest = new ProjectManifest { Project = "T", Code = "T-1" },
            Chapters = list,
            Views = [],
        };
    }

    [Fact]
    public void Resolve_replacesEmbed_withTranscludedContent()
    {
        var project = TestProject(("ch", "# Chapter\n\n## Sec {#sec}\n\nHello."));
        var pipeline = DocWizMarkdown.CreatePipeline();

        var doc = Markdig.Markdown.Parse("::: embed dw:ch#sec\n:::", pipeline);
        DocWizEmbed.Resolve(doc, project, pipeline);
        var html = DocWizMarkdown.RenderHtml(doc, pipeline);

        Assert.Contains("Hello.", html);
        Assert.DoesNotContain("embed", html);       // the directive name must not leak
        Assert.DoesNotContain("dw:ch#sec", html);   // nor the ref
    }

    [Fact]
    public void Resolve_embedSurroundedByContent_keepsOrderAndDoesNotSwallow()
    {
        var project = TestProject(("ch", "# Chapter\n\n## Sec {#sec}\n\nMiddle."));
        var pipeline = DocWizMarkdown.CreatePipeline();

        var doc = Markdig.Markdown.Parse("Before.\n\n::: embed dw:ch#sec\n:::\n\nAfter.", pipeline);
        DocWizEmbed.Resolve(doc, project, pipeline);
        var html = DocWizMarkdown.RenderHtml(doc, pipeline);

        Assert.Contains("Before.", html);
        Assert.Contains("Middle.", html);
        Assert.Contains("After.", html);
        var before = html.IndexOf("Before.", StringComparison.Ordinal);
        var middle = html.IndexOf("Middle.", StringComparison.Ordinal);
        var after = html.IndexOf("After.", StringComparison.Ordinal);
        Assert.True(before < middle && middle < after, "embed must resolve in place, not swallow the following block");
    }

    [Fact]
    public void Resolve_missingChapter_throwsResolvableDiagnostic()
    {
        var project = TestProject(("ch", "# Chapter\n\n## Sec {#sec}\n\nHello."));
        var pipeline = DocWizMarkdown.CreatePipeline();

        var doc = Markdig.Markdown.Parse("::: embed dw:nope#sec\n:::", pipeline);

        var ex = Assert.Throws<DocWizEmbed.EmbedException>(() => DocWizEmbed.Resolve(doc, project, pipeline));
        Assert.Contains("nope", ex.Message);
    }

    [Fact]
    public void Resolve_missingAnchor_throwsResolvableDiagnostic()
    {
        var project = TestProject(("ch", "# Chapter\n\n## Sec {#sec}\n\nHello."));
        var pipeline = DocWizMarkdown.CreatePipeline();

        var doc = Markdig.Markdown.Parse("::: embed dw:ch#nope\n:::", pipeline);

        Assert.Throws<TransclusionResolver.TransclusionException>(() => DocWizEmbed.Resolve(doc, project, pipeline));
    }

    [Fact]
    public void Resolve_acceptsRefWithoutDwPrefix()
    {
        // The validator and editor may store the bare "chapter#anchor" form; accept both.
        var project = TestProject(("ch", "# Chapter\n\n## Sec {#sec}\n\nHello."));
        var pipeline = DocWizMarkdown.CreatePipeline();

        var doc = Markdig.Markdown.Parse("::: embed ch#sec\n:::", pipeline);
        DocWizEmbed.Resolve(doc, project, pipeline);
        var html = DocWizMarkdown.RenderHtml(doc, pipeline);

        Assert.Contains("Hello.", html);
    }

    [Fact]
    public void DirectiveFor_producesTheCanonicalTwoLineForm()
    {
        Assert.Equal("::: embed dw:ch#sec\n:::", DocWizEmbed.DirectiveFor("ch#sec"));
    }

    [Fact]
    public void DirectiveFor_roundTripsThroughResolve()
    {
        var project = TestProject(("ch", "# Chapter\n\n## Sec {#sec}\n\nHello."));
        var pipeline = DocWizMarkdown.CreatePipeline();

        var doc = Markdig.Markdown.Parse(DocWizEmbed.DirectiveFor("ch#sec"), pipeline);
        DocWizEmbed.Resolve(doc, project, pipeline);

        Assert.Contains("Hello.", DocWizMarkdown.RenderHtml(doc, pipeline));
    }

    [Fact]
    public void FindRefs_returnsNormalizedRefsInDocumentOrder()
    {
        var pipeline = DocWizMarkdown.CreatePipeline();
        var markdown =
            "- a\n- b\n\n" +
            DocWizEmbed.DirectiveFor("070-screens#home") + "\n\n" +
            "Some prose.\n\n" +
            DocWizEmbed.DirectiveFor("safety#functions");

        var refs = DocWizEmbed.FindRefs(markdown, pipeline);

        Assert.Equal(new[] { "070-screens#home", "safety#functions" }, refs);
    }

    [Fact]
    public void FindRefs_isEmptyWhenNoEmbeds()
    {
        var pipeline = DocWizMarkdown.CreatePipeline();
        Assert.Empty(DocWizEmbed.FindRefs("- just\n- bullets", pipeline));
    }

    [Fact]
    public void Resolve_embedToFigureAnchor_rendersFigureInline()
    {
        var project = TestProject(("ch",
            "# Chapter\n\n![Cell render](assets/cell.png){width=640}\n"));
        var pipeline = DocWizMarkdown.CreatePipeline();

        var doc = Markdig.Markdown.Parse(DocWizEmbed.DirectiveFor("ch#figure-1"), pipeline);
        DocWizEmbed.Resolve(doc, project, pipeline);
        var html = DocWizMarkdown.RenderHtml(doc, pipeline);

        Assert.Contains("dw-image-figure", html);
        Assert.Contains("Cell render", html);
        Assert.Contains("width=\"640\"", html);
        Assert.DoesNotContain("embed", html);
    }

    [Fact]
    public void Resolve_embedToExplicitImageId_rendersFigureInline()
    {
        // A standalone image's explicit {#hero} lands on the inline image, not the
        // paragraph. The picker offers `hero` and `docwiz check` accepts the embed, so the
        // embed must render the figure (not throw) — closing the check-0 ⇒ build-works gap.
        var project = TestProject(("ch",
            "# Chapter\n\n![Cell render](assets/cell.png){#hero width=640}\n"));
        var pipeline = DocWizMarkdown.CreatePipeline();

        var doc = Markdig.Markdown.Parse(DocWizEmbed.DirectiveFor("ch#hero"), pipeline);
        DocWizEmbed.Resolve(doc, project, pipeline);
        var html = DocWizMarkdown.RenderHtml(doc, pipeline);

        Assert.Contains("dw-image-figure", html);
        Assert.Contains("Cell render", html);
        Assert.Contains("width=\"640\"", html);
        Assert.Contains("id=\"hero\"", html);
        Assert.DoesNotContain("embed", html);
    }

    [Fact]
    public void Resolve_embedToExplicitImageId_isConsistentWith_dwLink()
    {
        // The previously-inconsistent pair: a dw:ch#hero LINK already worked (id emitted on
        // <img id="hero">), but the embed of the same anchor threw. Both must now render the
        // image with that id — embed and link agree.
        var project = TestProject(("ch",
            "# Chapter\n\n![Cell render](assets/cell.png){#hero}\n"));
        var pipeline = DocWizMarkdown.CreatePipeline();

        var embedDoc = Markdig.Markdown.Parse(DocWizEmbed.DirectiveFor("ch#hero"), pipeline);
        DocWizEmbed.Resolve(embedDoc, project, pipeline);
        var embedHtml = DocWizMarkdown.RenderHtml(embedDoc, pipeline);

        Assert.Contains("id=\"hero\"", embedHtml);
        Assert.Contains("dw-image-figure", embedHtml);
    }

    [Fact]
    public void Resolve_embedToTableAnchor_rendersTableInline()
    {
        var project = TestProject(("ch",
            "# Chapter\n\n{#specs widths=\"40% 60%\"}\n| Part | Detail |\n|------|--------|\n| Motor | 3 kW |\n"));
        var pipeline = DocWizMarkdown.CreatePipeline();

        var doc = Markdig.Markdown.Parse(DocWizEmbed.DirectiveFor("ch#specs"), pipeline);
        DocWizEmbed.Resolve(doc, project, pipeline);
        var html = DocWizMarkdown.RenderHtml(doc, pipeline);

        Assert.Contains("dw-table-figure", html);
        Assert.Contains("<colgroup>", html);
        Assert.Contains("Motor", html);
        Assert.DoesNotContain("dw:ch#specs", html);
    }

    // ---- Linked-box width / reflow (L205) ----

    [Theory]
    [InlineData("dw:ch#sec width=60%", "ch#sec", "60%")]
    [InlineData("dw:ch#sec width=60% :::", "ch#sec", "60%")]   // tolerate the stray one-line form
    [InlineData("ch#sec width=480px", "ch#sec", "480px")]
    [InlineData("dw:ch#sec", "ch#sec", null)]
    [InlineData("dw:ch#sec width=bogus", "ch#sec", null)]      // invalid length → ignored
    public void EmbedArgs_ParseRefAndWidth(string args, string expectedRef, string? expectedWidth)
    {
        Assert.Equal(expectedRef, DocWizEmbed.NormalizeRef(args));
        Assert.Equal(expectedWidth, DocWizEmbed.ParseWidth(args));
    }

    [Theory]
    [InlineData("60%", "60%")]
    [InlineData(" 480px ", "480px")]
    [InlineData("auto", "auto")]
    [InlineData("banana", null)]   // not a CSS length → ignored
    [InlineData("", null)]
    public void NormalizeWidth_AcceptsLengths(string input, string? expected)
        => Assert.Equal(expected, DocWizEmbed.NormalizeWidth(input));

    [Fact]
    public void DirectiveFor_WithWidth_AppendsToken_AndNormalizesBack()
    {
        Assert.Equal("::: embed dw:ch#sec width=60%\n:::", DocWizEmbed.DirectiveFor("ch#sec", "60%"));
        Assert.Equal("::: embed dw:ch#sec\n:::", DocWizEmbed.DirectiveFor("ch#sec", "bogus")); // invalid → bare
        Assert.Equal("ch#sec", DocWizEmbed.NormalizeRef("ch#sec width=60%"));
    }

    [Fact]
    public void WrapResolved_AppliesWidth_OnlyWhenSized()
    {
        Assert.Equal("<div class=\"dw-embed\"><p>x</p></div>", DocWizEmbed.WrapResolved("<p>x</p>"));
        Assert.Equal("<div class=\"dw-embed\" style=\"width:60%;max-width:100%\"><p>x</p></div>",
            DocWizEmbed.WrapResolved("<p>x</p>", "60%"));
        Assert.Equal("<div class=\"dw-embed\"><p>x</p></div>", DocWizEmbed.WrapResolved("<p>x</p>", "bogus"));
    }

    [Theory]
    [InlineData("dw:ch#sec width=60% align=center", "ch#sec", "60%", "center")]
    [InlineData("dw:ch#sec align=right", "ch#sec", null, "right")]
    [InlineData("dw:ch#sec align=left", "ch#sec", null, null)]     // left → no align
    [InlineData("dw:ch#sec align=bogus", "ch#sec", null, null)]
    public void EmbedArgs_ParseRefWidthAndAlign(string args, string eRef, string? eWidth, string? eAlign)
    {
        Assert.Equal(eRef, DocWizEmbed.NormalizeRef(args));
        Assert.Equal(eWidth, DocWizEmbed.ParseWidth(args));
        Assert.Equal(eAlign, DocWizEmbed.ParseAlign(args));
    }

    [Fact]
    public void DirectiveFor_And_WrapResolved_CarryAlign()
    {
        Assert.Equal("::: embed dw:ch#sec width=60% align=center\n:::", DocWizEmbed.DirectiveFor("ch#sec", "60%", "center"));
        Assert.Equal("::: embed dw:ch#sec\n:::", DocWizEmbed.DirectiveFor("ch#sec", null, "left")); // left → bare
        Assert.Equal("<div class=\"dw-embed dw-box-center\" style=\"width:60%;max-width:100%\"><p>x</p></div>",
            DocWizEmbed.WrapResolved("<p>x</p>", "60%", "center"));
        Assert.Equal("<div class=\"dw-embed dw-box-right\"><p>x</p></div>", DocWizEmbed.WrapResolved("<p>x</p>", null, "right"));
    }

    [Fact]
    public void Resolve_sizedEmbed_keepsCleanRef_andResolves()
    {
        // A width token on the directive must not break ref resolution.
        var project = TestProject(("ch", "# Chapter\n\n## Hero {#hero}\n\nHero body.\n"));
        var pipeline = DocWizMarkdown.CreatePipeline();

        var doc = Markdig.Markdown.Parse(DocWizEmbed.DirectiveFor("ch#hero", "60%"), pipeline);
        Assert.Equal("60%", DocWizEmbed.WidthOf(doc));
        Assert.Equal(["ch#hero"], DocWizEmbed.FindRefs(doc));
        DocWizEmbed.Resolve(doc, project, pipeline);
        Assert.Contains("Hero body.", DocWizMarkdown.RenderHtml(doc, pipeline));
    }
}
