using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace DocWiz.Core.Markdown;

/// <summary>
/// A literal author-written <c>&lt;br&gt;</c>. A dedicated inline (NOT
/// <see cref="LineBreakInline"/>) on purpose: Markdig's pipe-table post-processor treats a
/// <see cref="LineBreakInline"/> inside a cell as a row separator and discards the cell text
/// before it, so a hard break cannot be modelled with one in a table. This ordinary inline
/// survives the cell rebuild exactly like an <c>&lt;em&gt;</c> or code span does, and its
/// renderer emits the same <c>&lt;br /&gt;</c> the core renderer would.
/// </summary>
public sealed class BrInline : Inline
{
}

/// <summary>
/// Lets a literal <c>&lt;br&gt;</c> / <c>&lt;br/&gt;</c> / <c>&lt;br /&gt;</c> render as a hard
/// line break <b>everywhere</b> — body text and, crucially, inside a pipe-table cell, where a
/// cell is one markdown line so the two-space hard-break convention can't break it
/// (VISION L242 "shift+enter in a cell"). Cell-only scoping is not possible here: Markdig
/// assembles pipe tables AFTER inline parsing, so at match time a cell looks like an ordinary
/// paragraph. Making <c>&lt;br&gt;</c> a hard break everywhere is the accepted, uniform rule;
/// the serializer emits <c>&lt;br&gt;</c> for every hard break so it round-trips 1:1.
///
/// Raw HTML stays disabled on purpose (authors are non-programmers; "&lt;24V&gt;" must render
/// literally). This is the ONE explicitly-allowed exception, recognised by a real Markdig
/// inline parser registered on '&lt;': it matches the tag IN PLACE, advances the slice past the
/// matched ORIGINAL bytes, and emits a <see cref="BrInline"/> whose Span covers exactly that
/// text. There is no string replacement and no length change, so the byte offsets
/// <see cref="BlockSegmenter"/> derives for the byte-surgical save (ADR-007) stay 1:1 with the
/// real file bytes.
/// </summary>
public sealed class DocWizLineBreakExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        // '<' is otherwise consumed by the (disabled) HTML parser; an early, idempotent
        // registration is deterministic. Anything other than a clean <br> tag is left for
        // the normal escape path, so "<24V>" still renders literally.
        if (!pipeline.InlineParsers.Contains<BrInlineParser>())
            pipeline.InlineParsers.Insert(0, new BrInlineParser());
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        if (renderer is HtmlRenderer html && !html.ObjectRenderers.Contains<BrInlineRenderer>())
            html.ObjectRenderers.Add(new BrInlineRenderer());
    }
}

/// <summary>
/// Matches <c>&lt;br&gt;</c>, <c>&lt;br/&gt;</c>, <c>&lt;br /&gt;</c> (case-insensitive on the
/// tag name, optional single space before a self-closing slash) at the current '&lt;' and emits
/// a <see cref="BrInline"/> in place. Anything else beginning with '&lt;' is left untouched, so
/// it falls through to the normal escape path and renders literally.
/// </summary>
public sealed class BrInlineParser : InlineParser
{
    public BrInlineParser()
    {
        OpeningCharacters = new[] { '<' };
    }

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        var start = slice.Start; // at '<'
        var c = slice.PeekChar(); // PeekChar() == PeekChar(1): the char at Start+1 ('b'/'B')
        if (c != 'b' && c != 'B')
            return false;
        c = slice.PeekChar(2);
        if (c != 'r' && c != 'R')
            return false;

        // Position just past "<br": accept '>', '/>' or ' />'.
        var i = 3;
        var ch = slice.PeekChar(i);
        if (ch == ' ')
        {
            i++;
            ch = slice.PeekChar(i);
        }
        if (ch == '/')
        {
            i++;
            ch = slice.PeekChar(i);
        }
        if (ch != '>')
            return false;

        var matchLength = i + 1; // include the closing '>'

        var inlineStart = processor.GetSourcePosition(start, out var line, out var column);
        processor.Inline = new BrInline
        {
            // Span maps 1:1 to the ORIGINAL matched bytes — no rewrite, no length change.
            Span = new SourceSpan(inlineStart, inlineStart + matchLength - 1),
            Line = line,
            Column = column,
        };

        // Consume exactly the matched original characters.
        slice.Start += matchLength;
        return true;
    }
}

/// <summary>Emits a self-closing <c>&lt;br /&gt;</c> with NO trailing newline. The core
/// LineBreakInlineRenderer adds an EnsureLine() newline for pretty-printing, but inside a
/// table cell that newline (a) breaks the in-place cell editor's parse→serialize round-trip
/// (the whitespace collapses, the faithfulness gate then locks the cell) and (b) renders as
/// an extra blank line on the live edit surface while the handover shows none — an edit↔ship
/// mismatch. A bare &lt;br /&gt; renders identically and round-trips 1:1 (2026-06-17).</summary>
public sealed class BrInlineRenderer : HtmlObjectRenderer<BrInline>
{
    protected override void Write(HtmlRenderer renderer, BrInline obj)
    {
        if (renderer.EnableHtmlForInline)
            renderer.Write("<br />");
    }
}
