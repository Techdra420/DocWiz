using DocWiz.Core.Models;
using Markdig;

namespace DocWiz.Build.Pdf;

/// <summary>Assembles a self-contained print HTML document for headless Edge to print
/// natively (no Paged.js — it didn't scale, 2026-06-29). CSS is inlined from SiteTemplates
/// (the one source); asset/attachment URLs are absolutized to the project root so images
/// resolve in place with NO per-item copy. Footers + bookmarks come from Page.printToPDF.</summary>
public sealed class PrintHtmlBuilder
{
    private readonly string _rootUri; // file:///<projectRoot>/

    public PrintHtmlBuilder(string projectRoot) =>
        _rootUri = new Uri(Path.GetFullPath(projectRoot) + Path.DirectorySeparatorChar).AbsoluteUri;

    public string BuildManual(DocProject project, MarkdownPipeline pipeline,
                              IReadOnlyCollection<string>? chapterIds = null)
    {
        var body = ChapterDocumentAssembler.AssembleBody(project, pipeline, chapterIds);

        var metaLines = new List<string>
        {
            Field("Code", project.Manifest.Code),
            Field("Customer", project.Manifest.Customer),
            Field("Version", project.Manifest.Version),
        };
        if (chapterIds is not null) metaLines.Insert(0, "Selected chapters");
        var coverMeta = string.Join("<br>", metaLines.Where(s => s.Length > 0));

        return Render("manual.print.html", new()
        {
            ["TITLE"] = project.Manifest.Project,
            ["PROJECT"] = Esc(project.Manifest.Project),
            ["LANG"] = project.Manifest.Language,
            ["COVER_META"] = coverMeta,
            ["CONTENT"] = Absolutize(body),
            ["PAGEDJS"] = "",
        });
    }

    public string BuildView(DocProject project, ViewDefinition view, MarkdownPipeline pipeline)
    {
        if (view.Mode == "slides")
        {
            // Slides reuse the on-screen layout engine: slides.js runs paginate() in print mode
            // (body.dw-pdf) then signals window.__slidesReady; the driver waits for that.
            var rendered = Absolutize(ViewRenderer.RenderSlides(project, view, pipeline, standalone: true));
            return Render("slides.print.html", new()
            {
                ["TITLE"] = view.Title,
                ["LANG"] = project.Manifest.Language,
                ["CONTENT"] = rendered,
                ["PAGEDJS"] = "<script>" + SiteTemplates.Read("slides.js") + "</script>",
            });
        }

        var docHtml = Absolutize(ViewRenderer.RenderDocument(project, view, pipeline, standalone: true));
        return Render("document.print.html", new()
        {
            ["TITLE"] = view.Title,
            ["LANG"] = project.Manifest.Language,
            ["CONTENT"] = $"<h1>{Esc(view.Title)}</h1>{docHtml}",
            ["PAGEDJS"] = "",
        });
    }

    /// <summary>Point relative asset/attachment URLs at the real project folder (absolute
    /// file://) so images resolve without copying the assets into a temp dir per item.</summary>
    internal string Absolutize(string html) => html
        .Replace("\"" + ProjectLayout.AssetsFolder + "/", "\"" + _rootUri + ProjectLayout.AssetsFolder + "/")
        .Replace("\"" + ProjectLayout.AttachmentsFolder + "/", "\"" + _rootUri + ProjectLayout.AttachmentsFolder + "/");

    private string Render(string template, Dictionary<string, string> tokens)
    {
        var html = SiteTemplates.Read(template);
        foreach (var (k, v) in tokens)
            html = html.Replace("{{" + k + "}}", v);
        // Inline the CSS (one source = SiteTemplates) so a headless file:// page needs no fetches.
        html = html.Replace("{{HEAD}}",
            "<style>\n" + Css("tokens.css") + "\n" + Css("site.css") + "\n" + Css("print.css") + "\n</style>");
        return html;
    }

    private static string Css(string file) => SiteTemplates.Read(file);
    private static string Field(string name, string val) => string.IsNullOrWhiteSpace(val) ? "" : $"{name}: {Esc(val)}";
    private static string Esc(string s) => System.Net.WebUtility.HtmlEncode(s);
}
