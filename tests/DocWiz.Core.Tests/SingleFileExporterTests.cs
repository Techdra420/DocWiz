using System.Linq;
using DocWiz.Build;
using Xunit;

namespace DocWiz.Core.Tests;

public class SingleFileExporterTests
{
    [Fact]
    public void ExportChapters_Filter_ProducesSelfContainedSubset()
    {
        var (project, pipeline) = TestRepo.LoadDemoProject();
        var first = project.Chapters[0];
        var other = project.Chapters[1];

        var html = SingleFileExporter.ExportChapters(project, pipeline, new[] { first.Id });

        Assert.Contains("<style>", html);                  // CSS inlined, not linked
        Assert.DoesNotContain("_dw/site.css", html);       // no external stylesheet link left
        Assert.Contains($"id=\"{first.Id}\"", html);       // selected chapter present
        Assert.DoesNotContain($"id=\"{other.Id}\"", html); // unselected chapter absent
    }

    [Fact]
    public void ExportView_Slides_InlinesAssetsAndResolvesEveryPlaceholder()
    {
        // Regression: slides.html links its assets as {{BASE}}_dw/…; the exporter never resolved
        // {{BASE}}, so nothing matched the inliner and the deck opened unstyled.
        var (project, pipeline) = TestRepo.LoadDemoProject();
        var view = project.Views.Single(v => v.Id == "user-training");
        Assert.Equal("slides", view.Mode);

        var html = SingleFileExporter.ExportView(project, view, pipeline);

        Assert.DoesNotContain("{{", html);                   // every template token resolved
        Assert.DoesNotContain("_dw/", html);                 // no external asset reference left
        Assert.Contains("<style>" + SiteTemplates.Read("tokens.css") + "</style>", html);
        Assert.Contains("<style>" + SiteTemplates.Read("site.css") + "</style>", html);
        Assert.Contains("<script>" + SiteTemplates.Read("site.js") + "</script>", html);
        Assert.Contains("<script>" + SiteTemplates.Read("slides.js") + "</script>", html);
    }

    [Fact]
    public void ExportChapters_NullFilter_IncludesEveryChapter()
    {
        var (project, pipeline) = TestRepo.LoadDemoProject();
        var html = SingleFileExporter.ExportChapters(project, pipeline, null);
        foreach (var ch in project.Chapters)
            Assert.Contains($"id=\"{ch.Id}\"", html);
    }
}
