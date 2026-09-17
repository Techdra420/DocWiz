using DocWiz.Core.Models;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace DocWiz.Core.Markdown;

/// <summary>
/// The one Markdown pipeline (ADR-004). The builder and the future editor preview must
/// both come through here — never construct a second pipeline elsewhere.
/// </summary>
public static class DocWizMarkdown
{
    public static MarkdownPipeline CreatePipeline() => new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseTaskLists()
        // Strikethrough ONLY (stakeholder-approved flavor addition, 2026-06-12) —
        // the other EmphasisExtras (sub/sup/inserted/marked) stay off the pinned set.
        .UseEmphasisExtras(Markdig.Extensions.EmphasisExtras.EmphasisExtraOptions.Strikethrough)
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .UseGenericAttributes()
        .UseCustomContainers()
        // Raw HTML is disabled on purpose: authors are non-programmers — text like
        // "<24V>" or "<n.c.>" must render literally, not vanish as an unknown tag
        // (and pasted <script> must never execute in a handover).
        .DisableHtml()
        // The ONE allowed exception to DisableHtml: a literal <br>/<br/>/<br /> becomes a
        // hard line break (incl. inside a table cell) via an in-place inline parser — no
        // source rewrite, so byte-surgical save offsets stay 1:1 (see DocWizLineBreak.cs).
        .Use(new DocWizLineBreakExtension())
        .Use(new DocWizRenderingExtension())
        .Use(new DocWizFiguresExtension())
        .Use(new DocWizGroupExtension())
        .Build();

    /// <summary>Parses every chapter, stores the AST on the chapter, and fills its anchor list.</summary>
    public static void ParseAll(DocProject project, MarkdownPipeline pipeline)
    {
        foreach (var chapter in project.Chapters)
        {
            var doc = Markdig.Markdown.Parse(chapter.Markdown, pipeline);
            chapter.Parsed = doc;
            chapter.Anchors.Clear();
            chapter.Anchors.AddRange(CollectAnchors(doc));
        }
    }

    /// <summary>Every addressable anchor in the document: heading ids (explicit and
    /// auto), plus the table-N / figure-N / attachment-N ids assigned by
    /// <see cref="DocWizFiguresExtension"/>. Used by link validation and the pickers.</summary>
    public static List<string> CollectAnchors(MarkdownDocument doc) =>
        doc.Descendants()
            .Select(node => node.TryGetAttributes()?.Id)
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .Distinct()
            .ToList();

    public static string RenderHtml(MarkdownDocument doc, MarkdownPipeline pipeline)
    {
        var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        pipeline.Setup(renderer);
        renderer.Render(doc);
        writer.Flush();
        return writer.ToString();
    }
}
