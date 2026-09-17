using System.Text;
using System.Text.RegularExpressions;
using DocWiz.Core.Markdown;
using DocWiz.Core.Models;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace DocWiz.Build;

/// <summary>
/// Renders one or more chapters into a single HTML body: each chapter parsed through the one
/// pipeline (ADR-004), embedded PDF links replaced by an in-place attachment note, dw: links
/// resolved to in-page #anchors, inner ids namespaced to avoid cross-chapter collisions, each
/// wrapped in a &lt;section class="pdf-chapter"&gt;. Emits RELATIVE assets/… paths; each caller
/// applies its own asset strategy (print → file://; single-file → base64). One source for the
/// multi-chapter assembly shared by PrintHtmlBuilder and SingleFileExporter (ADR-008).
/// </summary>
public static class ChapterDocumentAssembler
{
    /// <param name="chapterIds">null = all chapters (full manual); otherwise only chapters whose
    /// Id is in the set, in project (book) order.</param>
    public static string AssembleBody(DocProject project, MarkdownPipeline pipeline,
                                      IReadOnlyCollection<string>? chapterIds = null)
    {
        var body = new StringBuilder();
        foreach (var chapter in project.Chapters)
        {
            if (chapterIds is not null && !chapterIds.Contains(chapter.Id)) continue;

            var doc = Markdig.Markdown.Parse(chapter.Markdown, pipeline);
            ReplaceEmbeddedPdfs(doc);
            LinkRewriter.Rewrite(doc, project, id => $"{id}.html",
                dwResolver: (cid, anchor) => "#" + (anchor is null ? cid : $"{cid}__{anchor}"),
                fragmentResolver: anchor => $"#{chapter.Id}__{anchor}");

            var inner = NamespaceIds(DocWizMarkdown.RenderHtml(doc, pipeline), chapter.Id);
            body.Append($"<section class=\"pdf-chapter\" id=\"{chapter.Id}\">{inner}</section>");
        }
        return body.ToString();
    }

    /// <summary>Prefix every id="x" with "{chapterId}__" so anchors are unique across chapters.</summary>
    internal static string NamespaceIds(string html, string chapterId) =>
        Regex.Replace(html, "id=\"([^\"]+)\"", m => $"id=\"{chapterId}__{m.Groups[1].Value}\"");

    /// <summary>Replace inline links to local .pdf attachments with a reference note (single-file
    /// output and print cannot carry external PDFs — ADR-003).</summary>
    private static void ReplaceEmbeddedPdfs(MarkdownDocument doc)
    {
        foreach (var link in doc.Descendants().OfType<LinkInline>().ToList())
        {
            var url = link.Url ?? "";
            if (!link.IsImage && url.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                var file = System.IO.Path.GetFileName(url);
                link.Url = "#";
                link.Title = null;
                link.GetAttributes().AddClass("pdf-attachment-note");
                link.AppendChild(new LiteralInline($" (see attachment: {file})"));
            }
        }
    }
}
