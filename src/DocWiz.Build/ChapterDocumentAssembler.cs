using System.Text;
using DocWiz.Core.Markdown;
using DocWiz.Core.Models;
using Markdig;

namespace DocWiz.Build;

/// <summary>
/// Renders one or more chapters into a single HTML body: each chapter parsed through the one
/// pipeline (ADR-004), its links rewritten by <see cref="StandaloneLinks"/> (a link to an
/// included chapter jumps in-page; one to a chapter left out, or to a project file, becomes
/// plain text — print and single-file output cannot carry external files, ADR-003), inner ids
/// namespaced to avoid cross-chapter collisions, each wrapped in a
/// &lt;section class="pdf-chapter"&gt;. Emits RELATIVE assets/… paths; each caller
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
        // Parse every included chapter before rewriting any link: a link may point forward.
        var chapters = project.Chapters
            .Where(c => chapterIds is null || chapterIds.Contains(c.Id))
            .Select(c => (c.Id, Doc: Markdig.Markdown.Parse(c.Markdown, pipeline)))
            .ToList();
        var links = new StandaloneLinks();
        foreach (var (id, doc) in chapters)
        {
            links.Add(id, null, StandaloneLinks.Id(id, null));
            links.AddAnchors(id, DocWizMarkdown.CollectAnchors(doc));
        }

        var body = new StringBuilder();
        foreach (var (id, doc) in chapters)
        {
            links.Rewrite(doc, project, id);
            var inner = StandaloneLinks.NamespaceIds(DocWizMarkdown.RenderHtml(doc, pipeline), id);
            body.Append($"<section class=\"pdf-chapter\" id=\"{StandaloneLinks.Id(id, null)}\">{inner}</section>");
        }
        return body.ToString();
    }
}
