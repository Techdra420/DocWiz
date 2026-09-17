using DocWiz.Core.Loading;
using DocWiz.Core.Markdown;
using DocWiz.Core.Models;

namespace DocWiz.Build.Pdf;

public enum ExportFormat { Pdf, Html }

public sealed class ExportSelection
{
    public bool Manual { get; set; }
    public List<string> ChapterIds { get; set; } = [];
    public List<string> ViewIds { get; set; } = [];
    public ExportFormat Format { get; set; } = ExportFormat.Pdf;
}

public sealed record ExportItem(string Label, bool Success, string? Path, string? Error);
public sealed record ExportResult(List<ExportItem> Items);

public static class PdfExporter
{
    public static string ManualFileName(DocProject p, string ext = "pdf") =>
        $"{Packager.FileNameSafe(p.Manifest.Project)}-Manual.{ext}";
    public static string SelectionFileName(DocProject p, string ext = "pdf") =>
        $"{Packager.FileNameSafe(p.Manifest.Project)}-Selection.{ext}";
    public static string ViewFileName(DocProject p, ViewDefinition v, string ext = "pdf") =>
        $"{Packager.FileNameSafe(p.Manifest.Project)}-{Packager.FileNameSafe(v.Title)}.{ext}";

    public static void EnsureSafeOutputDir(string outDir, string handoverDir)
    {
        var outFull = Path.GetFullPath(outDir).TrimEnd('\\', '/');
        var hoFull = Path.GetFullPath(handoverDir).TrimEnd('\\', '/');
        if (outFull.Equals(hoFull, StringComparison.OrdinalIgnoreCase) ||
            outFull.StartsWith(hoFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Export dir must not be inside the handover output ('{handoverDir}'), which Build wipes.");
    }

    /// <param name="edgeExe">msedge.exe path (BrowserLauncher.FindEdge()); may be null when
    /// Format is Html, which needs no browser.</param>
    public static ExportResult Export(string projectRoot, ExportSelection selection, string outDir, string? edgeExe)
    {
        EnsureSafeOutputDir(outDir, Path.Combine(projectRoot, "output", "handover"));
        Directory.CreateDirectory(outDir);

        var project = ProjectLoader.Load(projectRoot);
        var pipeline = DocWizMarkdown.CreatePipeline();
        DocWizMarkdown.ParseAll(project, pipeline);

        return selection.Format == ExportFormat.Html
            ? ExportHtml(project, selection, outDir, pipeline)
            : ExportPdf(project, selection, outDir, pipeline, edgeExe);
    }

    private static ExportResult ExportPdf(DocProject project, ExportSelection selection, string outDir,
        Markdig.MarkdownPipeline pipeline, string? edgeExe)
    {
        if (string.IsNullOrEmpty(edgeExe) || !File.Exists(edgeExe))
            throw new InvalidOperationException("Microsoft Edge was not found; it is required for PDF export.");
        var driver = new EdgePdfDriver(edgeExe);

        // Documents wait for images to finish loading; slides wait for slides.js's print pass.
        const string docReady = "document.readyState === 'complete' && " +
            "Array.prototype.every.call(document.images, function (i) { return i.complete; })";
        const string slidesReady = "window.__slidesReady === true";
        var footerLeft = project.Manifest.Project;

        var items = new List<ExportItem>();
        if (selection.Manual)
            items.Add(RenderOne("Manual", outDir, ManualFileName(project), project, driver,
                b => b.BuildManual(project, pipeline), Budget(project.Chapters.Count * 4), docReady, footer: true, footerLeft));
        if (selection.ChapterIds.Count > 0)
            items.Add(RenderOne("Selected chapters", outDir, SelectionFileName(project), project, driver,
                b => b.BuildManual(project, pipeline, selection.ChapterIds),
                Budget(selection.ChapterIds.Count * 4), docReady, footer: true, footerLeft));
        foreach (var vid in selection.ViewIds)
        {
            var view = project.Views.FirstOrDefault(v => v.Id == vid);
            if (view is null) { items.Add(new ExportItem(vid, false, null, "View not found")); continue; }
            var v2 = view;
            var slides = v2.Mode == "slides";
            items.Add(RenderOne(v2.Title, outDir, ViewFileName(project, v2), project, driver,
                b => b.BuildView(project, v2, pipeline), Budget(v2.Slides.Count + 4),
                slides ? slidesReady : docReady, footer: !slides, footerLeft));
        }
        return new ExportResult(items);
    }

    private static ExportResult ExportHtml(DocProject project, ExportSelection selection, string outDir,
        Markdig.MarkdownPipeline pipeline)
    {
        var items = new List<ExportItem>();
        if (selection.Manual)
            items.Add(WriteHtml("Manual", outDir, ManualFileName(project, "html"),
                () => SingleFileExporter.ExportChapters(project, pipeline)));
        if (selection.ChapterIds.Count > 0)
            items.Add(WriteHtml("Selected chapters", outDir, SelectionFileName(project, "html"),
                () => SingleFileExporter.ExportChapters(project, pipeline, selection.ChapterIds)));
        foreach (var vid in selection.ViewIds)
        {
            var view = project.Views.FirstOrDefault(v => v.Id == vid);
            if (view is null) { items.Add(new ExportItem(vid, false, null, "View not found")); continue; }
            var v2 = view;
            items.Add(WriteHtml(v2.Title, outDir, ViewFileName(project, v2, "html"),
                () => SingleFileExporter.ExportView(project, v2, pipeline)));
        }
        return new ExportResult(items);
    }

    private static ExportItem WriteHtml(string label, string outDir, string fileName, Func<string> build)
    {
        try
        {
            var dest = Path.Combine(outDir, fileName);
            File.WriteAllText(dest, build());
            return new ExportItem(label, true, dest, null);
        }
        catch (Exception ex) { return new ExportItem(label, false, null, ex.Message); }
    }

    // Timeout for one item's native Edge print, scaled by size. 60s floor covers a cold
    // headless Edge start; native pagination is fast, so this is comfortably generous.
    private static int Budget(int units) => Math.Clamp(30_000 + units * 1_500, 60_000, 600_000);

    private static ExportItem RenderOne(string label, string outDir, string fileName, DocProject project,
        EdgePdfDriver driver, Func<PrintHtmlBuilder, string> build, int timeoutMs,
        string readyExpression, bool footer, string footerLeft)
    {
        var work = Path.Combine(Path.GetTempPath(), "dw-pdf-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(work);
            // CSS is inlined and assets are referenced in place (absolute file://) by the builder,
            // so the work dir only holds the print HTML + Edge profile — no per-item copying.
            var html = build(new PrintHtmlBuilder(project.Root));
            var htmlPath = Path.Combine(work, "print.html");
            File.WriteAllText(htmlPath, html);

            var pdfTmp = Path.Combine(work, "out.pdf");
            driver.PrintToPdf(htmlPath, pdfTmp, Path.Combine(work, "edge-profile"), timeoutMs,
                readyExpression, footer, footerLeft);
            var dest = Path.Combine(outDir, fileName);
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(pdfTmp, dest);
            return new ExportItem(label, true, dest, null);
        }
        catch (Exception ex)
        {
            return new ExportItem(label, false, null, ex.Message);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { /* best effort */ }
        }
    }
}
