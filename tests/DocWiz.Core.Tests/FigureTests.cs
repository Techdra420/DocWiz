using DocWiz.Core.Markdown;
using Xunit;

namespace DocWiz.Core.Tests;

public class FigureTests
{
    private static (string Html, List<string> Anchors) Render(string markdown)
    {
        var pipeline = DocWizMarkdown.CreatePipeline();
        var doc = Markdig.Markdown.Parse(markdown, pipeline);
        return (DocWizMarkdown.RenderHtml(doc, pipeline), DocWizMarkdown.CollectAnchors(doc));
    }

    [Fact]
    public void Table_GetsAutoAnchorAndCaption()
    {
        var (html, anchors) = Render("| A | B |\n|---|---|\n| 1 | 2 |\n");

        Assert.Contains("<figure class=\"dw-figure dw-table-figure\" id=\"table-1\">", html);
        Assert.Contains("<figcaption>Table 1</figcaption>", html);
        Assert.Contains("table-1", anchors);
    }

    [Fact]
    public void Image_GetsFigureWithAltCaption()
    {
        var (html, anchors) = Render("![Cell render front view](assets/cell.png)\n");

        Assert.Contains("id=\"figure-1\"", html);
        Assert.Contains("<figcaption>Figure 1 — Cell render front view</figcaption>", html);
        Assert.Contains("figure-1", anchors);
    }

    [Fact]
    public void AttachmentLink_GetsAutoAnchor()
    {
        var (html, anchors) = Render("See [the plan](../../attachments/plan.pdf) here.\n");

        Assert.Contains("id=\"attachment-1\"", html);
        Assert.Contains("attachment-1", anchors);
    }

    [Fact]
    public void ExplicitId_IsRespected_NumberingStillCounts()
    {
        var md = "{#my-table}\n| A | B |\n|---|---|\n| 1 | 2 |\n\n| C | D |\n|---|---|\n| 3 | 4 |\n";
        var (html, anchors) = Render(md);

        Assert.Contains("id=\"my-table\"", html);
        Assert.Contains("id=\"table-2\"", html);     // second table keeps positional id
        Assert.Contains("<figcaption>Table 1</figcaption>", html);
        Assert.Contains("<figcaption>Table 2</figcaption>", html);
        Assert.Contains("my-table", anchors);
        Assert.Contains("table-2", anchors);
    }

    [Fact]
    public void Strikethrough_RendersAndStaysScoped()
    {
        // Stakeholder-approved flavor addition (2026-06-12): ~~x~~ only — the other
        // EmphasisExtras (sub/sup/inserted/marked) stay OFF (pinned set, ADR-004).
        var (html, _) = Render("Old ~~struck~~ text, single ~tilde~ literal.\n");
        Assert.Contains("<del>struck</del>", html);
        Assert.DoesNotContain("<sub>", html);
        Assert.Contains("~tilde~", html);
    }

    [Fact]
    public void TableWidths_RenderAsColgroup()
    {
        // {widths="…"} above a table sizes its columns (live-editor M3; stakeholder
        // decision 2026-06-12). Space-separated CSS widths → <colgroup>.
        var md = "{widths=\"30% 70%\"}\n| A | B |\n|---|---|\n| 1 | 2 |\n";
        var (html, _) = Render(md);

        Assert.Contains("<colgroup><col style=\"width:30%\"><col style=\"width:70%\"></colgroup>", html);
        // The widths property must not leak as an HTML attribute.
        Assert.DoesNotContain("widths=", html);
    }

    [Fact]
    public void TableWidths_CombinesWithExplicitId()
    {
        var md = "{#sizes widths=\"50% 50%\"}\n| A | B |\n|---|---|\n| 1 | 2 |\n";
        var (html, anchors) = Render(md);

        Assert.Contains("id=\"sizes\"", html);
        Assert.Contains("<colgroup>", html);
        Assert.Contains("sizes", anchors);
    }

    [Fact]
    public void TableWidth_SizesTheFigure_NotTheInnerTable()
    {
        // {width=50%} (L202) sizes the whole table box: the figure carries .dw-table-sized
        // and an inline width; it must NOT become a deprecated <table width> attribute.
        var md = "{width=50%}\n| A | B |\n|---|---|\n| 1 | 2 |\n";
        var (html, _) = Render(md);

        Assert.Contains("<figure class=\"dw-figure dw-table-figure dw-table-sized\" style=\"width:50%\"", html);
        Assert.DoesNotContain(" width=\"", html);   // no width="…" HTML attribute anywhere
    }

    [Fact]
    public void TableWidth_CombinesWithWidthsAndId()
    {
        var md = "{#specs width=60% widths=\"30% 70%\"}\n| A | B |\n|---|---|\n| 1 | 2 |\n";
        var (html, anchors) = Render(md);

        Assert.Contains("dw-table-sized", html);
        Assert.Contains("style=\"width:60%\"", html);
        Assert.Contains("id=\"specs\"", html);
        Assert.Contains("<colgroup><col style=\"width:30%\"><col style=\"width:70%\"></colgroup>", html);
        Assert.Contains("specs", anchors);
        Assert.DoesNotContain(" width=\"", html);
    }

    [Theory]
    [InlineData("center", "dw-box-center")]
    [InlineData("right", "dw-box-right")]
    public void TableAlign_AddsBoxClass_NotAnAttribute(string align, string expectedClass)
    {
        var md = "{width=60% align=" + align + "}\n| A | B |\n|---|---|\n| 1 | 2 |\n";
        var (html, _) = Render(md);

        Assert.Contains(expectedClass, html);
        Assert.DoesNotContain(" align=\"", html);   // never a deprecated <table align> attribute
    }

    [Fact]
    public void TableAlign_Left_OrAbsent_HasNoBoxClass()
    {
        var (html, _) = Render("{width=60% align=left}\n| A | B |\n|---|---|\n| 1 | 2 |\n");
        Assert.DoesNotContain("dw-box-", html);     // left is the default → no class
        Assert.DoesNotContain(" align=\"", html);
    }

    [Fact]
    public void Table_With_Caption_Attr_Renders_In_Figcaption()
    {
        // {caption="…"} above a table appends " — caption" to the figcaption (em dash,
        // U+2014 — mirrors "Figure N — alt text") and must not leak onto the inner <table>.
        var md = "{caption=\"Safety symbols\"}\n| A | B |\n|---|---|\n| 1 | 2 |\n";
        var (html, _) = Render(md);

        Assert.Contains("<figcaption>Table 1 — Safety symbols</figcaption>", html);
        Assert.DoesNotContain("caption=\"Safety symbols\"", html); // not leaked onto <table>
    }

    [Fact]
    public void Table_Caption_WithCurlyQuote_RendersAndPreservesOtherAttrs()
    {
        // Regression: a caption value containing a typographic right double quote (U+201D)
        // — the sanitized form of a user-typed straight " — must parse correctly alongside
        // other attrs (widths) so neither the colgroup nor the caption is lost.
        // The straight-quote → curly-quote substitution happens in serialize.js/tableview.js
        // BEFORE the Markdown is stored; this test proves the C# pipeline handles the result.
        var md = "{widths=\"25% 35% 40%\" caption=\"A “quoted” and } brace title\"}\n| A | B | C |\n|---|---|---|\n| 1 | 2 | 3 |\n";
        var (html, _) = Render(md);

        // Caption must appear in figcaption
        Assert.Contains("<figcaption>Table 1 — A “quoted” and } brace title</figcaption>", html);
        // widths must still produce the colgroup (attr block parsed successfully)
        Assert.Contains("<colgroup>", html);
        Assert.Contains("<col style=\"width:25%\">", html);
    }

    [Fact]
    public void Table_Caption_WithWidths_BothRender()
    {
        // Belt-and-suspenders: {widths="…" caption="…"} must produce BOTH colgroup AND caption.
        var md = "{widths=\"50% 50%\" caption=\"A plain title\"}\n| A | B |\n|---|---|\n| 1 | 2 |\n";
        var (html, _) = Render(md);

        Assert.Contains("<figcaption>Table 1 — A plain title</figcaption>", html);
        Assert.Contains("<colgroup><col style=\"width:50%\"><col style=\"width:50%\"></colgroup>", html);
    }

    [Fact]
    public void TableWidth_InvalidToken_IsIgnored()
    {
        var md = "{width=banana}\n| A | B |\n|---|---|\n| 1 | 2 |\n";
        var (html, _) = Render(md);

        Assert.DoesNotContain("dw-table-sized", html);
        Assert.DoesNotContain("style=\"width:", html);
        Assert.DoesNotContain(" width=\"", html);   // and never leaks as an attribute
    }

    [Fact]
    public void ImageWidthAttribute_RendersOnImg()
    {
        var (html, _) = Render("![Layout](assets/x.png){width=640}\n");
        Assert.Contains("width=\"640\"", html);
        Assert.Contains("<figcaption>Figure 1 — Layout</figcaption>", html);
    }

    [Fact]
    public void InlineImageInsideText_IsNotAFigure()
    {
        var (html, _) = Render("An icon ![i](assets/i.png) inline in text.\n");
        Assert.DoesNotContain("dw-image-figure", html);
        Assert.DoesNotContain("figcaption", html);
    }

    [Fact]
    public void Figure_With_NoCaption_Class_Omits_Figcaption_But_Keeps_Alt()
    {
        // .no-caption on the image suppresses the <figcaption> while keeping the
        // alt attribute on the <img> for accessibility (Task 6). The class itself
        // must NOT appear on the rendered <img> element.
        var (html, _) = Render("![Cell layout](assets/cell.png){.no-caption}\n");

        // alt attribute must be retained (accessibility)
        Assert.Contains("alt=\"Cell layout\"", html);
        // figcaption must be absent
        Assert.DoesNotContain("<figcaption>", html);
        // .no-caption must not leak onto <img>
        Assert.DoesNotContain("no-caption", html);
        // The figure wrapper itself must still be present
        Assert.Contains("dw-image-figure", html);
    }

    [Fact]
    public void ImageOnlyTableCell_RendersBareImage_NotAFigure()
    {
        // Regression (2026-06-12): image-only cell paragraphs rendered as a nested
        // <figure id="figure-N"> + caption. The live editor's in-place cell editor
        // cannot model a block figure inside a cell, so EVERY image cell whose asset
        // exists hit the faithfulness lock; the handover showed "Figure N" captions
        // under tiny in-table icons. Cell images render bare; figure numbering and
        // anchors count standalone images only (id and caption stay in lockstep).
        var md = "| ![](assets/icon.png) | Meaning |\n|---|---|\n| ![](assets/warn.png) | Warning |\n\n![Layout](assets/big.png)\n";
        var (html, anchors) = Render(md);

        Assert.Contains("<th><img src=\"assets/icon.png\" alt=\"\" /></th>", html);
        Assert.Contains("<td><img src=\"assets/warn.png\" alt=\"\" /></td>", html);
        // The standalone image below the table is still a figure — and it is the
        // FIRST one, proving both the id assigner and the caption counter skipped
        // the two cell images.
        Assert.Contains("id=\"figure-1\"", html);
        Assert.Contains("<figcaption>Figure 1 — Layout</figcaption>", html);
        Assert.Contains("figure-1", anchors);
        Assert.DoesNotContain("figure-2", anchors);
    }
}
