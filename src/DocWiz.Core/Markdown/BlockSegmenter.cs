using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.CustomContainers;
using Markdig.Extensions.Tables;
using Markdig.Renderers;
using Markdig.Syntax;

namespace DocWiz.Core.Markdown;

public enum SegmentKind { Gap, Block }

/// <summary>One contiguous piece of a chapter body. <see cref="Start"/> inclusive,
/// <see cref="End"/> exclusive, char offsets into the exact body string that was
/// segmented. <see cref="Source"/> is always the verbatim substring.</summary>
public sealed record BodySegment(SegmentKind Kind, int Start, int End, string Source, string? BlockType);

/// <summary>A segment plus its pipeline-rendered HTML (null for gaps).</summary>
public sealed record RenderedSegment(BodySegment Segment, string? Html);

/// <summary>A write instruction for <see cref="BlockSegmenter.Splice"/>: either copy a
/// span of the original body verbatim, or insert new text.</summary>
public abstract record SpliceOp
{
    private SpliceOp() { }

    public sealed record Copy(int Start, int End) : SpliceOp;

    public sealed record Text(string Value) : SpliceOp;
}

/// <summary>
/// Fidelity foundation of the live editor (ADR-007): partitions a chapter body into an
/// alternating gap/block sequence whose concatenated sources equal the body exactly, so
/// that saves can copy untouched spans byte-for-byte and re-emit only edited blocks.
/// Parse with the one pipeline (ADR-004); never a second parser.
/// </summary>
public static partial class BlockSegmenter
{
    public static IReadOnlyList<BodySegment> Segment(string body, MarkdownPipeline pipeline) =>
        Run(body, pipeline, render: false).Select(r => r.Segment).ToList();

    /// <summary>Segments and renders each block through the one pipeline. A single
    /// renderer instance is reused with a swapped writer so per-page counters
    /// (Table N / Figure N) continue across blocks exactly like on a built page.
    /// <paramref name="mutate"/> runs on the parsed document before rendering —
    /// the editor uses it to rewrite asset URLs (spans are read before mutation,
    /// so source segments stay verbatim).</summary>
    public static IReadOnlyList<RenderedSegment> SegmentAndRender(string body, MarkdownPipeline pipeline,
        Action<MarkdownDocument>? mutate = null) =>
        Run(body, pipeline, render: true, mutate);

    private static List<RenderedSegment> Run(string body, MarkdownPipeline pipeline, bool render,
        Action<MarkdownDocument>? mutate = null)
    {
        var result = new List<RenderedSegment>();
        if (body.Length == 0)
            return result;

        var doc = Markdig.Markdown.Parse(body, pipeline);
        // Classification belongs to the SOURCE: compute it before the mutate hook,
        // which may restructure inlines (e.g. missing image → placeholder span) and
        // would otherwise flip an image-paragraph back to a plain paragraph.
        var kinds = doc.ToDictionary(block => block, Classify);
        mutate?.Invoke(doc);
        HtmlRenderer? renderer = null;
        if (render)
        {
            renderer = new HtmlRenderer(new StringWriter());
            pipeline.Setup(renderer);
        }

        var cursor = 0;
        foreach (var block in doc)
        {
            var start = block.Span.Start;
            var end = block.Span.End + 1; // Markdig spans are end-inclusive
            if (start < 0)
                continue; // synthetic block without a source position
            end = Math.Min(end, body.Length);

            // Markdown blocks are line-based, but some Markdig spans start or end
            // mid-line (tables begin after the first '|'). Snap to whole lines so a
            // block segment always carries its complete source text.
            (start, end) = SnapToLines(body, start, end);

            // A standalone attribute line ("{#id}" / "{.widths=…}") binds to the block
            // below it; editing that block must rewrite the line too, so glue it in.
            start = GlueLeadingAttributeLine(body, cursor, start);

            if (start < cursor)
            {
                // Overlapping span (defensive): extend the previous block instead of
                // breaking the partition invariant.
                var prev = result[^1];
                if (end > prev.Segment.End)
                {
                    var widened = prev.Segment with { End = end, Source = body[prev.Segment.Start..end] };
                    var html = renderer is null ? prev.Html : prev.Html + RenderBlock(renderer, block);
                    result[^1] = new RenderedSegment(widened, html);
                }
                cursor = Math.Max(cursor, end);
                continue;
            }

            if (start > cursor)
                result.Add(new RenderedSegment(
                    new BodySegment(SegmentKind.Gap, cursor, start, body[cursor..start], null), null));
            result.Add(new RenderedSegment(
                new BodySegment(SegmentKind.Block, start, end, body[start..end], kinds[block]),
                renderer is null ? null : RenderBlock(renderer, block)));
            cursor = end;
        }

        if (cursor < body.Length)
            result.Add(new RenderedSegment(
                new BodySegment(SegmentKind.Gap, cursor, body.Length, body[cursor..], null), null));
        return result;
    }

    /// <summary>The verbatim source span [Start, End) of a single block within
    /// <paramref name="body"/>: the same whole-line snap and leading-attribute-line glue
    /// the segmenter applies, so callers (e.g. <see cref="TransclusionResolver"/> resolving
    /// an element anchor) get exactly the bytes that re-parse to that block — including a
    /// table's <c>{#id widths=…}</c> line or a figure's <c>{width=…}</c> suffix. Returns
    /// (0, 0) for a synthetic block with no source position.</summary>
    public static (int Start, int End) BlockSourceSpan(string body, Block block)
    {
        var start = block.Span.Start;
        if (start < 0)
            return (0, 0);
        var end = Math.Min(block.Span.End + 1, body.Length); // Markdig spans are end-inclusive
        (start, end) = SnapToLines(body, start, end);
        // Floor the leading-attribute-line glue at the END of the preceding sibling block, so
        // a single-newline-separated line that LOOKS like an attribute group ("{...}") but
        // belongs to the previous block can never be glued across the block boundary into this
        // span. Without a floor (cursor=0) the glue could swallow the prior block's last line.
        start = GlueLeadingAttributeLine(body, PreviousBlockFloor(block, body), start);
        return (start, end);
    }

    /// <summary>The exclusive char offset just past the preceding sibling block (0 when this
    /// block is the first in its parent / has no positioned predecessor) — the lower bound for
    /// leading-attribute-line glue so it cannot cross into the previous block.</summary>
    private static int PreviousBlockFloor(Block block, string body)
    {
        if (block.Parent is not { } parent)
            return 0;
        var index = parent.IndexOf(block);
        for (var i = index - 1; i >= 0; i--)
        {
            var prevEnd = parent[i].Span.End;
            if (prevEnd >= 0)
                return Math.Min(prevEnd + 1, body.Length); // Markdig spans are end-inclusive
        }
        return 0;
    }

    private static string RenderBlock(HtmlRenderer renderer, Block block)
    {
        var writer = new StringWriter();
        renderer.Writer = writer;
        renderer.Render(block);
        renderer.Writer.Flush();
        return writer.ToString();
    }

    /// <summary>Applies copy/insert operations against the original body. Copies are
    /// verbatim substrings — untouched content can never be rewritten by accident.</summary>
    public static string Splice(string original, IEnumerable<SpliceOp> ops)
    {
        var sb = new StringBuilder(original.Length + 256);
        foreach (var op in ops)
        {
            switch (op)
            {
                case SpliceOp.Copy(var start, var end):
                    if (start < 0 || end < start || end > original.Length)
                        throw new ArgumentOutOfRangeException(nameof(ops),
                            $"Copy span [{start},{end}) is outside the original body (length {original.Length}).");
                    sb.Append(original, start, end - start);
                    break;
                case SpliceOp.Text(var text):
                    sb.Append(text);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Widens [start, end) to full-line boundaries: start moves back to the
    /// beginning of its line; end moves forward to the end of its line (the following
    /// newline stays in the next gap).</summary>
    private static (int Start, int End) SnapToLines(string body, int start, int end)
    {
        var lineStart = body.LastIndexOf('\n', Math.Max(start - 1, 0));
        start = start == 0 ? 0 : (lineStart < 0 ? 0 : Math.Min(lineStart + 1, start));
        if (end > 0 && end <= body.Length)
        {
            var next = body.IndexOf('\n', end - 1);
            if (next >= 0)
            {
                // Stop before the newline; trim a preceding '\r' stays with its '\n'.
                var lineEnd = next > 0 && body[next - 1] == '\r' ? next - 1 : next;
                end = Math.Max(end, lineEnd);
            }
            else
            {
                end = body.Length;
            }
        }
        return (start, end);
    }

    /// <summary>If the line directly above <paramref name="blockStart"/> is a standalone
    /// attribute group, returns its start offset; otherwise returns <paramref name="blockStart"/>.</summary>
    private static int GlueLeadingAttributeLine(string body, int cursor, int blockStart)
    {
        if (blockStart <= cursor)
            return blockStart;
        var lineEnd = blockStart;
        // Step back over exactly one newline (the separator between the attribute line
        // and its block — a blank line in between detaches the attributes).
        if (lineEnd > cursor && body[lineEnd - 1] == '\n')
            lineEnd--;
        if (lineEnd > cursor && body[lineEnd - 1] == '\r')
            lineEnd--;
        var lineStart = body.LastIndexOf('\n', Math.Max(lineEnd - 1, 0));
        lineStart = lineStart < cursor ? cursor : lineStart + 1;
        if (lineEnd <= lineStart)
            return blockStart;
        return AttributeLineRegex().IsMatch(body[lineStart..lineEnd]) ? lineStart : blockStart;
    }

    /// <summary>Block-type names the editor builds as PM-native nodes (mirrored by
    /// <c>NATIVE</c> in <c>wwwroot/editor2/ingest.js</c>). figure/table are structured atoms
    /// in the editor, not in this set. ADR-008 single source: <c>ingest.js</c>'s NATIVE set is
    /// pinned to this by <c>BlockTypeParityTests</c>, and the CLI L1 fidelity scan uses
    /// <see cref="ModeledBlockTypes"/> — neither keeps its own literal list. Names match
    /// <see cref="Classify"/>'s output.</summary>
    public static readonly IReadOnlySet<string> EditorNativeBlockTypes =
        new HashSet<string>(StringComparer.Ordinal)
        { "heading", "paragraph", "container", "code", "list", "quote", "hr" };

    /// <summary>Block types the L1 fidelity scan treats as modeled (round-trippable without a
    /// raw atom): the editor-native set plus the structured atoms figure/table. Everything
    /// else <see cref="Classify"/> can emit (e.g. <c>code-indented</c>, <c>linkref</c>) is
    /// intentionally a raw atom.</summary>
    public static readonly IReadOnlySet<string> ModeledBlockTypes =
        new HashSet<string>(EditorNativeBlockTypes, StringComparer.Ordinal) { "figure", "table" };

    private static string Classify(Block block) => block switch
    {
        HeadingBlock => "heading",
        ParagraphBlock p => DocWizFiguresExtension.ImageOf(p) is not null ? "figure" : "paragraph",
        Table => "table",
        CustomContainer => "container",
        FencedCodeBlock => "code",
        CodeBlock => "code-indented",
        ListBlock => "list",
        QuoteBlock => "quote",
        ThematicBreakBlock => "hr",
        LinkReferenceDefinitionGroup => "linkref",
        _ => block.GetType().Name,
    };

    [GeneratedRegex(@"^\{[^{}\r\n]+\}[ \t]*$")]
    private static partial Regex AttributeLineRegex();
}
