using DocWiz.Core.Markdown;
using DocWiz.Core.Models;
using Markdig.Extensions.Tables;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace DocWiz.Core.Slides;

/// <summary>
/// Drafts a starting slide deck from an existing chapter (Slides v2): walks the
/// chapter's headings/figures/tables and emits a <see cref="ViewDefinition"/> of LINKED
/// <c>::: embed :::</c> blocks the author then trims live on the canvas.
///
/// This is a BEST-EFFORT starting point, NOT a finished deck. The heuristics below give a
/// sensible first cut (one slide per top-level subsection, media-heavy sections get a
/// media layout); the author is expected to delete, merge, reorder, and re-link.
///
/// Pure and unit-testable: it reads the chapter's already-parsed AST (anchors incl.
/// <c>figure-N</c> / <c>table-N</c> assigned by <see cref="DocWizFiguresExtension"/>) and
/// writes only an in-memory <see cref="ViewDefinition"/>. The caller serializes it.
///
/// EVERY emitted embed reference is a SECTION anchor (<c>chapter#heading-anchor</c>) that
/// resolves both in the validator and at RENDER time, so the drafted view passes
/// <c>docwiz check</c> AND builds. The view audience defaults to one the chapter is tagged
/// for, so embeds do not trip the "not tagged for audience" warning either.
/// </summary>
public static class DeckDrafter
{
    /// <summary>Heuristics (documented; a starting point, not final). The layout is chosen
    /// from the subsection's STRUCTURE; the embed ref is always the SECTION anchor:
    /// <list type="bullet">
    /// <item>chapter H1 → a <c>section-header</c> slide (Title = chapter title);</item>
    /// <item>a top-level subsection (H2) whose body is a LONE figure/table (no prose) →
    ///   <c>big-media</c>, <c>media</c> = the SECTION embed (which, for a lone-media
    ///   section, is just that figure/table — the heading line itself is dropped by the
    ///   transclusion resolver);</item>
    /// <item>otherwise (prose, with or without a figure/table) → <c>title-content</c>,
    ///   <c>body</c> = the SECTION embed.</item>
    /// </list>
    /// <para><b>Embed-granularity decision (the wrinkle, decided + documented).</b> An embed
    /// transcludes through <see cref="TransclusionResolver"/>, which resolves heading anchors
    /// AND element anchors (<c>figure-N</c>/<c>table-N</c>/explicit-id figures and tables,
    /// attachments) — so an element embed (<c>#figure-1</c>) both passes the validator
    /// (it's in <see cref="Chapter.Anchors"/>) and renders. What there is still no anchor for
    /// is "prose only" (prose carries no id), so the spec's <c>two-column</c> "prose embed
    /// left | figure embed right" still cannot be produced from a single subsection via
    /// embeds. The drafter therefore deliberately emits ONE SECTION embed per subsection as
    /// its best-effort default: <b>option (b)</b> — collapse prose+figure to a single
    /// <c>title-content</c> SECTION embed (prose and its figure flow together on one slide),
    /// and reserve <c>big-media</c> for lone-media sections. The drafter does not auto-split
    /// a subsection into element embeds (e.g. a <c>two-column</c> of prose + <c>#figure-1</c>)
    /// because there is no prose-only anchor for the other column; the author adds
    /// <c>two-column</c> live when splitting content (its natural authored use is two
    /// DIFFERENT section embeds, as the demo fixture shows). A clean, simple first cut for a
    /// drafting aid — not a resolvability limit anymore.</para>
    /// </summary>
    /// <param name="audience">View audience; defaults to the chapter's first audience, else
    /// the project's first audience, else empty (the validator then reports it — a chapter
    /// with no audience can't seed a clean deck anyway).</param>
    public static ViewDefinition Draft(DocProject project, Chapter chapter, string? audience = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(chapter);

        var doc = chapter.Parsed
            ?? throw new InvalidOperationException(
                $"Chapter '{chapter.Id}' is not parsed — call DocWizMarkdown.ParseAll first.");

        var resolvedAudience = audience
            ?? chapter.Audiences.FirstOrDefault()
            ?? project.Manifest.Audiences.FirstOrDefault()?.Id
            ?? "";

        var slides = new List<SlideDef>
        {
            // The chapter title becomes the opening section-header (no regions for it).
            new() { Layout = SlideLayout.SectionHeader, Title = chapter.Title },
        };

        foreach (var section in TopLevelSubsections(doc))
            slides.Add(DraftSlide(chapter.Id, section));

        return new ViewDefinition
        {
            Id = UniqueId(project, chapter),
            Title = chapter.Title,
            Audience = resolvedAudience,
            Mode = "slides",
            Slides = slides,
        };
    }

    /// <summary>One H2 subsection: its cleaned title, its heading anchor, how many media
    /// blocks (figure/table) it holds, and whether it has any prose.</summary>
    private readonly record struct Subsection(string Title, string Anchor, int MediaCount, bool HasProse);

    /// <summary>Walks the document's top-level (H2) headings in order. For each, classifies
    /// the blocks between it and the next H2 (or EOF) as media (figure/table) or prose.
    /// Headings without an anchor are skipped — an embed needs a target; the pipeline
    /// assigns auto-ids to every heading, so this only guards the degenerate
    /// "no Parsed anchors" case.</summary>
    private static IEnumerable<Subsection> TopLevelSubsections(MarkdownDocument doc)
    {
        // Top-level blocks only — figures/tables/prose that belong to a subsection are
        // siblings of the heading at the document root (Markdig is not hierarchical).
        var blocks = doc.ToList();
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is not HeadingBlock { Level: 2 } heading)
                continue;
            var anchor = heading.TryGetAttributes()?.Id;
            if (string.IsNullOrEmpty(anchor))
                continue;

            var mediaCount = 0;
            var hasProse = false;
            for (var j = i + 1; j < blocks.Count && blocks[j] is not HeadingBlock { Level: <= 2 }; j++)
            {
                switch (Classify(blocks[j]))
                {
                    case BlockClass.Media:
                        mediaCount++;
                        break;
                    case BlockClass.Prose:
                        hasProse = true;
                        break;
                }
            }

            yield return new Subsection(HeadingText(heading), anchor!, mediaCount, hasProse);
        }
    }

    private static SlideDef DraftSlide(string chapterId, Subsection section)
    {
        var sectionEmbed = DocWizEmbed.DirectiveFor($"{chapterId}#{section.Anchor}");

        // Lone media (exactly one figure/table, no prose) → big-media. The SECTION embed
        // of a lone-media section IS just that figure/table (the resolver drops the heading
        // line), so big-media gets a clean, single large element. (An element #figure-N
        // embed would also render now, but the SECTION embed is the simpler default and
        // tracks the author's section headings.)
        if (!section.HasProse && section.MediaCount == 1)
            return new SlideDef
            {
                Layout = SlideLayout.BigMedia,
                Title = section.Title,
                Regions = new() { [SlideLayout.MediaRegion] = sectionEmbed },
            };

        // Everything else (prose, with or without a figure/table; or empty) → title-content
        // on the SECTION. See the granularity note on Draft for why prose+figure collapses
        // here instead of becoming a two-column with an element embed.
        return new SlideDef
        {
            Layout = SlideLayout.TitleContent,
            Title = section.Title,
            Regions = new() { [SlideLayout.BodyRegion] = sectionEmbed },
        };
    }

    private enum BlockClass { Prose, Media, Ignore }

    /// <summary>Media = a table, or a standalone-image paragraph (the same definition
    /// <see cref="DocWizFiguresExtension"/> uses to assign figure-N). Everything else with
    /// visible content is prose; blank paragraphs are ignored.</summary>
    private static BlockClass Classify(Block block) => block switch
    {
        Table => BlockClass.Media,
        ParagraphBlock p when DocWizFiguresExtension.ImageOf(p) is not null => BlockClass.Media,
        ParagraphBlock p when p.Inline is null || !p.Inline.Any() => BlockClass.Ignore,
        _ => BlockClass.Prose,
    };

    /// <summary>"## Daily startup {#daily-startup}" heading → "Daily startup".</summary>
    private static string HeadingText(HeadingBlock heading)
    {
        if (heading.Inline is null)
            return "";
        var text = string.Concat(heading.Inline.Descendants<Markdig.Syntax.Inlines.LiteralInline>()
            .Select(l => l.Content.ToString()));
        return text.Trim();
    }

    /// <summary>A valid, collision-aware view id derived from the chapter
    /// (<c>&lt;chapterId&gt;-deck</c>, then <c>-deck-2</c>, …) — never collides with an
    /// existing view in the project.</summary>
    private static string UniqueId(DocProject project, Chapter chapter)
    {
        var taken = project.Views.Select(v => v.Id).ToHashSet(StringComparer.Ordinal);
        var baseId = $"{chapter.Id}-deck";
        if (!taken.Contains(baseId))
            return baseId;
        for (var n = 2; ; n++)
        {
            var candidate = $"{baseId}-{n}";
            if (!taken.Contains(candidate))
                return candidate;
        }
    }
}
