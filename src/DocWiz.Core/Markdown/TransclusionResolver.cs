using System.Text.RegularExpressions;
using DocWiz.Core.Models;
using Markdig.Extensions.CustomContainers;
using Markdig.Extensions.Tables;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace DocWiz.Core.Markdown;

/// <summary>A resolved view include: title + the raw Markdown of the referenced section.
/// SourceRef is "chapter-id" or "chapter-id#anchor" for attribution links.</summary>
public sealed record TranscludedSection(string Title, string Markdown, string SourceRef);

/// <summary>
/// Resolves `include: chapter-id[#anchor]` references (docs/CONTENT_MODEL.md) by slicing
/// the chapter's source Markdown using the parsed AST's spans. Returning source text
/// (not AST nodes) keeps the chapter's own AST untouched and lets every consumer re-parse
/// through the one shared pipeline.
/// </summary>
public static partial class TransclusionResolver
{
    public sealed class TransclusionException(string message) : Exception(message);

    /// <summary>Resolve against a parsed chapter. Throws on a missing anchor —
    /// `docwiz check` reports these before any build/render runs.</summary>
    public static TranscludedSection Resolve(Chapter chapter, string? anchor)
    {
        var doc = chapter.Parsed
            ?? throw new TransclusionException($"Chapter '{chapter.Id}' is not parsed.");
        var source = chapter.Markdown;

        if (anchor is null)
        {
            // Whole chapter: drop the leading H1 (the consumer renders its own title).
            var firstHeading = doc.Descendants<HeadingBlock>().FirstOrDefault();
            var start = firstHeading is { Level: 1 } ? ClampedEnd(firstHeading, source) : 0;
            return new TranscludedSection(chapter.Title, source[start..].Trim(), chapter.Id);
        }

        var heading = doc.Descendants<HeadingBlock>()
            .FirstOrDefault(h => h.TryGetAttributes()?.Id == anchor);
        if (heading is not null)
        {
            var contentStart = ClampedEnd(heading, source);
            var nextHeading = doc.Descendants<HeadingBlock>()
                .Where(h => h.Span.Start > heading.Span.Start && h.Level <= heading.Level)
                .OrderBy(h => h.Span.Start)
                .FirstOrDefault();
            var contentEnd = nextHeading?.Span.Start ?? source.Length;

            var title = CleanHeadingText(source[heading.Span.Start..Math.Min(heading.Span.End + 1, source.Length)]);
            return new TranscludedSection(title, source[contentStart..contentEnd].Trim(), $"{chapter.Id}#{anchor}");
        }

        // Not a heading: the anchor may address an embeddable ELEMENT — a figure
        // (standalone-image paragraph), a table, or an attachment link — carrying that id.
        // The id is either explicit ({#id}) or the assigned figure-N / table-N /
        // attachment-N. NOTE on where an explicit id sits: a table's is on the table, but a
        // standalone image's {#id} is attached by Markdig to the INLINE image, not the
        // paragraph (ResolveElement matches both). Return the element's VERBATIM source so it
        // re-parses through the one pipeline into the same figure/table/attachment, attribute
        // lines and all. (decision 4: embeds resolve the transcluded figure/table/section.)
        if (ResolveElement(doc, source, anchor) is { } element)
            return element with { SourceRef = $"{chapter.Id}#{anchor}" };

        throw new TransclusionException($"Chapter '{chapter.Id}' has no anchor '#{anchor}'.");
    }

    /// <summary>Resolves an element anchor to the verbatim source of its block. The block
    /// span (with whole-line snap + leading-attribute-line glue) comes from
    /// <see cref="BlockSegmenter.BlockSourceSpan"/> so the fragment round-trips exactly —
    /// a table keeps its <c>{#id widths=…}</c> line, a figure its <c>{width=…}</c> suffix.
    /// Returns null when no element carries the anchor.</summary>
    private static TranscludedSection? ResolveElement(MarkdownDocument doc, string source, string anchor)
    {
        // Block-carried ids: a table, or a standalone-image paragraph (a figure). A figure
        // carries its id in one of two places: the auto figure-N goes on the PARAGRAPH,
        // while a standalone image's explicit {#id} is attached by Markdig to the INLINE
        // image (LinkInline), not the paragraph. Match both so an explicit-id figure embed
        // resolves like the figure-N one — otherwise check-0 ≠ build-works (the id is in
        // chapter.Anchors via the inline, the picker offers it, the validator accepts it).
        foreach (var block in doc.Descendants<Block>())
        {
            switch (block)
            {
                case CustomContainer cc
                    when string.Equals(cc.Info, DocWizGroupExtension.ContainerName, StringComparison.OrdinalIgnoreCase)
                        && cc.TryGetAttributes()?.Id == anchor:
                    return GroupInner(source, cc);

                case Table table when table.TryGetAttributes()?.Id == anchor:
                    return Element(source, table, title: "");

                // Only a GENUINE standalone-image paragraph is an embeddable figure:
                // ImageOf is non-null only for an image-only paragraph (so an inline image
                // inside prose is excluded), and a table-cell image renders as a bare <img>
                // (AssignIds / FigureParagraphRenderer skip it) so it carries no figure
                // anchor and must not resolve here either. The id may be on the paragraph
                // (auto figure-N) or on its image (explicit {#id}).
                case ParagraphBlock paragraph
                    when paragraph.Parent is not Markdig.Extensions.Tables.TableCell
                        && DocWizFiguresExtension.ImageOf(paragraph) is { } image
                        && (paragraph.TryGetAttributes()?.Id == anchor
                            || image.TryGetAttributes()?.Id == anchor):
                    return Element(source, paragraph, DocWizFiguresExtension.AltTextOf(image));
            }
        }

        // Inline-carried ids: an attachment LinkInline. Its embeddable unit is the leaf
        // block that contains it (e.g. the surrounding paragraph). LinkInline.ParentBlock
        // is not populated on a freshly parsed doc, so locate the owner by scanning leaf
        // blocks for an attachment link with this id.
        foreach (var leaf in doc.Descendants<LeafBlock>())
        {
            if (leaf.Inline is null)
                continue;
            foreach (var link in leaf.Inline.Descendants<LinkInline>())
                if (!link.IsImage && link.TryGetAttributes()?.Id == anchor)
                    return Element(source, leaf, title: "");
        }
        return null;
    }

    /// <summary>The verbatim source of a group container's CHILD blocks (the fence lines
    /// excluded), so an embed of a group re-parses to plain content.</summary>
    private static TranscludedSection GroupInner(string source, CustomContainer cc)
    {
        if (cc.Count == 0)
            return new TranscludedSection("", "", "");
        var (start, _) = BlockSegmenter.BlockSourceSpan(source, cc[0]);
        var (_, end) = BlockSegmenter.BlockSourceSpan(source, cc[^1]);
        return new TranscludedSection("", source[start..end].Trim(), "");
    }

    private static TranscludedSection Element(string source, Block block, string title)
    {
        var (start, end) = BlockSegmenter.BlockSourceSpan(source, block);
        // SourceRef is overwritten by the caller with "{chapter.Id}#{anchor}".
        return new TranscludedSection(title, source[start..end].Trim(), "");
    }

    /// <summary>Content starts on the line after the heading line. The heading's span
    /// does not cover a trailing {#anchor} attribute (the attributes parser owns it),
    /// so skipping to the next newline is required to not leak it into the fragment.</summary>
    private static int ClampedEnd(HeadingBlock heading, string source)
    {
        var from = Math.Min(heading.Span.End + 1, source.Length);
        var newline = source.IndexOf('\n', from);
        return newline < 0 ? source.Length : newline + 1;
    }

    /// <summary>"## Daily startup {#daily-startup}" → "Daily startup".</summary>
    internal static string CleanHeadingText(string headingLine)
    {
        var text = HeadingMarkerRegex().Replace(headingLine.Trim(), "");
        text = AttributeSuffixRegex().Replace(text, "");
        return text.TrimEnd('#', ' ');
    }

    [GeneratedRegex(@"^#{1,6}\s*")]
    private static partial Regex HeadingMarkerRegex();

    [GeneratedRegex(@"\s*\{[^}]*\}\s*$")]
    private static partial Regex AttributeSuffixRegex();
}
