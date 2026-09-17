using DocWiz.Build.Pdf;
using DocWiz.Core.Loading; // ProjectLoader
using DocWiz.Core.Tests; // TestRepo
using Xunit;

namespace DocWiz.Core.Tests.Pdf;

public class PdfExporterTests
{
    [Fact]
    public void Export_HtmlFormat_WritesSelfContainedFiles_WithoutEdge()
    {
        var root = TestRepo.DemoProjectPath;
        var outDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dw-html-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var first = ProjectLoader.Load(root).Chapters[0].Id;
            var selection = new ExportSelection
            {
                Manual = true,
                ChapterIds = new System.Collections.Generic.List<string> { first },
                Format = ExportFormat.Html,
            };

            var result = PdfExporter.Export(root, selection, outDir, edgeExe: null);

            Assert.Equal(2, result.Items.Count);                 // manual + subset
            Assert.All(result.Items, i => Assert.True(i.Success, i.Error));
            Assert.All(result.Items, i => Assert.True(System.IO.File.Exists(i.Path)));
            Assert.EndsWith("-Manual.html", result.Items[0].Path);
            Assert.EndsWith("-Selection.html", result.Items[1].Path);
            Assert.Contains("<style>", System.IO.File.ReadAllText(result.Items[1].Path!));
        }
        finally { try { System.IO.Directory.Delete(outDir, recursive: true); } catch { } }
    }

    [Fact]
    public void ManualFileName_IsProjectNameSafePlusManual()
    {
        var (project, _) = TestRepo.LoadDemoProject();
        var name = PdfExporter.ManualFileName(project);
        Assert.EndsWith("-Manual.pdf", name);
        Assert.DoesNotContain(" ", name);
    }

    [Fact]
    public void ViewFileName_IsProjectPlusViewTitleSafe()
    {
        var (project, _) = TestRepo.LoadDemoProject();
        var view = project.Views[0];
        var name = PdfExporter.ViewFileName(project, view);
        Assert.EndsWith(".pdf", name);
        Assert.DoesNotContain(" ", name);
    }

    [Fact]
    public void Guard_RejectsOutputDirInsideHandoverOutput()
    {
        var handover = @"C:\proj\output\handover";
        Assert.Throws<System.ArgumentException>(() =>
            PdfExporter.EnsureSafeOutputDir(@"C:\proj\output\handover\_site\pdfs", handover));
        PdfExporter.EnsureSafeOutputDir(@"C:\proj\output\PDF_Export", handover); // sibling allowed, no throw
    }

    [Fact(Skip = "Edge-gated; run locally")]
    public void Smoke_ExportsManualPdfForDemoCell()
    {
        var root = TestRepo.DemoProjectPath;
        var outDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dw-pdf-smoke-" + System.Guid.NewGuid().ToString("N"));
        var edge = DocWiz.App.BrowserLauncher.FindEdge()!;
        var result = PdfExporter.Export(root, new ExportSelection { Manual = true }, outDir, edge);
        Assert.True(result.Items[0].Success, result.Items[0].Error);
        Assert.True(new System.IO.FileInfo(result.Items[0].Path!).Length > 0);
    }
}
