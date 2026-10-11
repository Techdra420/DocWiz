using System.Linq;
using System.Text.RegularExpressions;
using DocWiz.Build;
using DocWiz.Build.Pdf;
using DocWiz.Core.Loading;
using DocWiz.Core.Markdown;
using DocWiz.Core.Models;
using Markdig;
using Xunit;

namespace DocWiz.Core.Tests.Pdf;

/// <summary>Links in standalone outputs (PDF print HTML, single-file HTML). A PDF is printed
/// from a temp folder that is deleted afterwards, so a relative href there became a dead
/// file:///C:/Users/&lt;user&gt;/AppData/Local/Temp/dw-pdf-…/chapter.html link in the PDF. Every
/// link must be an in-document #anchor whose target exists, or a web link.</summary>
public class StandaloneLinksTests
{
    private static (DocProject, MarkdownPipeline) Load(string relativePath)
    {
        var project = ProjectLoader.Load(TestRepo.At(relativePath.Split('/')));
        var pipeline = DocWizMarkdown.CreatePipeline(project.Manifest.Audiences);
        DocWizMarkdown.ParseAll(project, pipeline);
        return (project, pipeline);
    }

    /// <summary>Fails on any &lt;a href&gt; that is not "#id" with that id in the document, or
    /// an http(s)/mailto link.</summary>
    private static void AssertPortableLinks(string html, string output)
    {
        var ids = Regex.Matches(html, "id=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToHashSet();
        foreach (var href in Regex.Matches(html, "<a\\s[^>]*href=\"([^\"]*)\"").Select(m => m.Groups[1].Value))
        {
            if (href.StartsWith('#'))
                Assert.True(ids.Contains(href[1..]), $"{output}: in-document link '{href}' has no target");
            else
                Assert.True(Regex.IsMatch(href, "^(https?|mailto):"), $"{output}: link '{href}' does not work outside the handover");
        }
    }

    [Theory]
    [InlineData("samples/stockroom-demo")]
    [InlineData("templates/projects/docwiz-guide")]
    public void EveryStandaloneOutput_LinksOnlyToItselfOrTheWeb(string projectPath)
    {
        var (project, pipeline) = Load(projectPath);
        var print = new PrintHtmlBuilder(project.Root);
        var someChapters = project.Chapters.Where((_, i) => i % 2 == 0).Select(c => c.Id).ToList();

        AssertPortableLinks(print.BuildManual(project, pipeline), "PDF manual");
        AssertPortableLinks(print.BuildManual(project, pipeline, someChapters), "PDF selected chapters");
        AssertPortableLinks(SingleFileExporter.ExportChapters(project, pipeline), "HTML manual");
        AssertPortableLinks(SingleFileExporter.ExportChapters(project, pipeline, someChapters), "HTML selected chapters");
        foreach (var view in project.Views)
        {
            AssertPortableLinks(print.BuildView(project, view, pipeline), $"PDF view {view.Id}");
            AssertPortableLinks(SingleFileExporter.ExportView(project, view, pipeline), $"HTML view {view.Id}");
        }
    }

    [Fact]
    public void DocumentView_LinkToAnEmbeddedSubsection_JumpsToItInTheDocument()
    {
        // admin-guide embeds the whole system-overview (which links to installation#network)
        // and the whole installation chapter (which holds the network heading).
        var (project, pipeline) = TestRepo.LoadDemoProject();
        var view = project.Views.Single(v => v.Id == "admin-guide");

        var html = new PrintHtmlBuilder(project.Root).BuildView(project, view, pipeline);

        Assert.Contains("href=\"#installation__network\"", html);
        Assert.Contains("id=\"installation__network\"", html);
    }

    [Fact]
    public void DocumentView_LinkToAnEmbeddedSection_JumpsToItsCard()
    {
        // support-quickref's third section embeds troubleshooting#common-problems (its heading
        // is the card's title), and daily-operations#daily-startup links to it.
        var (project, pipeline) = TestRepo.LoadDemoProject();
        var view = project.Views.Single(v => v.Id == "support-quickref");

        var html = new PrintHtmlBuilder(project.Root).BuildView(project, view, pipeline);

        Assert.Contains("href=\"#sec-2\"", html);
        Assert.Contains("id=\"sec-2\"", html);
    }

    [Fact]
    public void SelectedChapters_LinkToAChapterLeftOut_IsPlainText()
    {
        var (project, pipeline) = TestRepo.LoadDemoProject();

        var html = new PrintHtmlBuilder(project.Root).BuildManual(project, pipeline, new[] { "system-overview" });

        Assert.Contains("Network and addresses", html);
        Assert.DoesNotContain("installation__network", html);
    }

    [Fact]
    public void DocumentView_LinkToAnAttachment_BecomesANote()
    {
        var (project, pipeline) = TestRepo.LoadDemoProject();
        var view = project.Views.Single(v => v.Id == "admin-guide");

        var html = new PrintHtmlBuilder(project.Root).BuildView(project, view, pipeline);

        Assert.Contains("(see attachment: release-notes-placeholder.pdf)", html);
        Assert.DoesNotContain("href=\"file:", html);
    }

    [Fact]
    public void HandoverView_StillLinksToTheChapterPages()
    {
        var (project, pipeline) = TestRepo.LoadDemoProject();
        var view = project.Views.Single(v => v.Id == "admin-guide");

        var html = ViewRenderer.RenderDocument(project, view, pipeline);

        Assert.Contains("href=\"installation.html#network\"", html);
    }
}
