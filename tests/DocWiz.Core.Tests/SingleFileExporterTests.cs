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
    public void ExportChapters_NullFilter_IncludesEveryChapter()
    {
        var (project, pipeline) = TestRepo.LoadDemoProject();
        var html = SingleFileExporter.ExportChapters(project, pipeline, null);
        foreach (var ch in project.Chapters)
            Assert.Contains($"id=\"{ch.Id}\"", html);
    }
}
