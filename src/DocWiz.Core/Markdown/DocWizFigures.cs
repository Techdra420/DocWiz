using System.Text;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace DocWiz.Core.Markdown;

/// <summary>
/// Makes every table, standalone image, and attachment link addressable and captioned:
/// - parse time: assigns deterministic anchors (table-N / figure-N / attachment-N) to
///   elements without an explicit {#id}, so dw: links and the view pickers can target
///   them (they are positional — for long-lived references prefer heading anchors);
/// - render time: wraps tables and standalone images in &lt;figure&gt; with an automatic
///   "Table N" / "Figure N — alt text" caption.
/// Lives in the one shared pipeline, so the editor preview matches the handover.
/// </summary>
public sealed class DocWizFiguresExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        pipeline.DocumentProcessed += AssignIds;
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        if (renderer is HtmlRenderer html)
        {
            html.ObjectRenderers.ReplaceOrAdd<HtmlTableRenderer>(new CaptionedTableRenderer());
            html.ObjectRenderers.ReplaceOrAdd<ParagraphRenderer>(new FigureParagraphRenderer());
        }
    }

    internal static void AssignIds(MarkdownDocument document)
    {
        int tables = 0, figures = 0, attachments = 0;
        foreach (var node in document.Descendants())
        {
            switch (node)
            {
                case Table table:
                    tables++;
                    table.GetAttributes().Id ??= $"table-{tables}";
                    break;
                // Figures are STANDALONE images only: an image-only paragraph inside a
                // table cell renders as the bare <img> (see FigureParagraphRenderer),
                // so it must not consume a figure number/anchor here either — id and
                // caption numbering have to stay in lockstep.
                case ParagraphBlock { Parent: TableCell }:
                    break;
                case ParagraphBlock paragraph when ImageOf(paragraph) is not null:
                    figures++;
                    paragraph.GetAttributes().Id ??= $"figure-{figures}";
                    break;
                case LinkInline { IsImage: false } link
                    when LinkRewriter.TryGetLocalFile(link.Url ?? "", "attachments", out _):
                    attachments++;
                    link.GetAttributes().Id ??= $"attachment-{attachments}";
                    break;
            }
        }
    }

    /// <summary>The image of an image-only paragraph (whitespace allowed), else null.
    /// Public: shared with the editor-doc meta builder.</summary>
    public static LinkInline? ImageOf(ParagraphBlock paragraph)
    {
        if (paragraph.Inline is null)
            return null;
        LinkInline? image = null;
        foreach (var inline in paragraph.Inline)
        {
            switch (inline)
            {
                case LinkInline { IsImage: true } li when image is null:
                    image = li;
                    break;
                case LiteralInline lit when string.IsNullOrWhiteSpace(lit.Content.ToString()):
                    break;
                default:
                    return null;
            }
        }
        return image;
    }

    public static string AltTextOf(LinkInline image)
    {
        var sb = new StringBuilder();
        foreach (var literal in image.Descendants().OfType<LiteralInline>())
            sb.Append(literal.Content.ToString());
        return sb.ToString().Trim();
    }
}

/// <summary>Default table rendering wrapped in a figure with a "Table N" caption.
/// A {widths="30% 70%"} attribute above the table becomes a &lt;colgroup&gt;
/// (live-editor M3 — stakeholder decision 2026-06-12). One renderer instance per
/// page render, so numbering is per page.</summary>
public sealed class CaptionedTableRenderer : HtmlObjectRenderer<Table>
{
    private static readonly System.Text.RegularExpressions.Regex WidthToken =
        new(@"^([0-9]+(\.[0-9]+)?(%|px|em|rem|ch|fr)?|auto)$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private readonly HtmlTableRenderer _inner = new();
    private int _count;

    protected override void Write(HtmlRenderer renderer, Table table)
    {
        _count++;
        var attributes = table.GetAttributes();
        var props = attributes.Properties;

        // An overall {width=…} (a single CSS length, e.g. 50%) sizes the whole table box
        // narrower than the column. It lives on the FIGURE (so the "Table N" caption tracks
        // the narrower table) and adds .dw-table-sized so CSS drops the default full-column
        // stretch — it is NOT emitted as a deprecated <table width> attribute. An invalid
        // token is ignored here (ProjectValidator warns about it).
        var widthIndex = props?.FindIndex(p => p.Key == "width") ?? -1;
        var figureWidth = widthIndex >= 0 && WidthToken.IsMatch(props![widthIndex].Value ?? "")
            ? props![widthIndex].Value
            : null;

        // {align=center|right} places a SIZED table box left (default) / centre / right within
        // its column or zone — a figure class, never a deprecated <table align> attribute
        // (left = no token). Mirrors the image's {.img-center/.img-right}.
        var alignIndex = props?.FindIndex(p => p.Key == "align") ?? -1;
        var alignClass = (alignIndex >= 0 ? props![alignIndex].Value : null) switch
        {
            "center" => " dw-box-center",
            "right" => " dw-box-right",
            _ => null,
        };

        // {caption="…"} is the human-readable table title: rendered as "Table N — caption"
        // in the figcaption (em dash, mirrors "Figure N — alt text"). Never emitted as an
        // HTML attribute — always stripped from the inner <table> element.
        var captionIndex = props?.FindIndex(p => p.Key == "caption") ?? -1;
        var caption = captionIndex >= 0 ? props![captionIndex].Value : null;

        renderer.Write("<figure class=\"dw-figure dw-table-figure");
        if (figureWidth is not null)
            renderer.Write(" dw-table-sized");
        if (alignClass is not null)
            renderer.Write(alignClass);
        renderer.Write("\"");
        if (figureWidth is not null)
        {
            renderer.Write(" style=\"width:");
            renderer.WriteEscape(figureWidth);
            renderer.Write("\"");
        }
        if (attributes.Id is { } id)
        {
            renderer.Write(" id=\"");
            renderer.WriteEscape(id);
            renderer.Write("\"");
        }
        renderer.WriteLine(">");

        // The id lives on the figure, widths render as a colgroup, width became the figure
        // style — suppress all three on the inner <table>, restore afterwards (parsed docs
        // are re-rendered). Remove by descending index so the saved positions stay valid.
        var savedId = attributes.Id;
        attributes.Id = null;
        var widthsIndex = props?.FindIndex(p => p.Key == "widths") ?? -1;
        var widthsValue = widthsIndex >= 0 ? props![widthsIndex].Value : null;
        var removed = new List<(int idx, KeyValuePair<string, string?> prop)>();
        foreach (var i in new[] { widthsIndex, widthIndex, alignIndex, captionIndex }.Where(i => i >= 0).OrderByDescending(i => i))
        {
            removed.Add((i, props![i]));
            props.RemoveAt(i);
        }

        var outerWriter = renderer.Writer;
        var buffer = new StringWriter();
        renderer.Writer = buffer;
        ((IMarkdownObjectRenderer)_inner).Write(renderer, table);
        renderer.Writer = outerWriter;

        attributes.Id = savedId;
        foreach (var (i, prop) in removed.OrderBy(r => r.idx))
            props!.Insert(i, prop);

        var html = buffer.ToString();
        if (widthsValue is not null && Colgroup(widthsValue) is { } colgroup)
        {
            var tableTag = html.IndexOf("<table", StringComparison.Ordinal);
            var insertAt = tableTag >= 0 ? html.IndexOf('>', tableTag) : -1;
            if (insertAt >= 0)
                html = html.Insert(insertAt + 1, colgroup);
        }
        renderer.Write(html);

        renderer.Write("<figcaption>Table ");
        renderer.Write(_count.ToString());
        if (!string.IsNullOrEmpty(caption))
        {
            renderer.Write(" — ");
            renderer.WriteEscape(caption);
        }
        renderer.WriteLine("</figcaption></figure>");
    }

    /// <summary>Sanitized colgroup from a space-separated widths value; tokens that
    /// are not plain CSS lengths are dropped (the validator warns about them).</summary>
    internal static string? Colgroup(string? widths)
    {
        if (string.IsNullOrWhiteSpace(widths))
            return null;
        var cols = widths.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => WidthToken.IsMatch(w))
            .Select(w => $"<col style=\"width:{w}\">")
            .ToList();
        return cols.Count == 0 ? null : "<colgroup>" + string.Concat(cols) + "</colgroup>";
    }
}

/// <summary>Paragraphs that contain exactly one image render as a figure with a
/// "Figure N — alt text" caption; everything else falls through to the default.</summary>
public sealed class FigureParagraphRenderer : HtmlObjectRenderer<ParagraphBlock>
{
    private readonly ParagraphRenderer _inner = new();
    private int _count;

    protected override void Write(HtmlRenderer renderer, ParagraphBlock paragraph)
    {
        var image = DocWizFiguresExtension.ImageOf(paragraph);
        // A picture in a TABLE CELL is not a standalone figure: the default renderer
        // (ImplicitParagraph, set by Markdig's table renderer) emits the bare <img>.
        // The nested <figure>+<figcaption> this used to produce was unmodelable for
        // the live editor's in-place cell editor — every image cell whose asset
        // exists hit the faithfulness lock — and put "Figure N" noise inside table
        // cells of the handover. AssignIds skips the same paragraphs.
        if (image is null || paragraph.Parent is TableCell)
        {
            ((IMarkdownObjectRenderer)_inner).Write(renderer, paragraph);
            return;
        }

        _count++;

        // .no-caption suppresses the <figcaption> while keeping alt on the <img>
        // (accessibility). Strip the class from the image so it never renders on the
        // <img> element — it is a figure-level instruction, not an image class.
        var imgAttrs = image.GetAttributes();
        var noCaption = imgAttrs.Classes?.Contains("no-caption") == true;
        if (noCaption)
            imgAttrs.Classes!.Remove("no-caption");

        renderer.Write("<figure class=\"dw-figure dw-image-figure\"");
        if (paragraph.GetAttributes().Id is { } id)
        {
            renderer.Write(" id=\"");
            renderer.WriteEscape(id);
            renderer.Write("\"");
        }
        renderer.WriteLine(">");
        renderer.WriteLeafInline(paragraph);
        renderer.WriteLine();
        if (!noCaption)
        {
            renderer.Write("<figcaption>Figure ");
            renderer.Write(_count.ToString());
            var alt = DocWizFiguresExtension.AltTextOf(image);
            if (alt.Length > 0)
            {
                renderer.Write(" — ");
                renderer.WriteEscape(alt);
            }
            renderer.WriteLine("</figcaption>");
        }
        renderer.WriteLine("</figure>");
    }
}
