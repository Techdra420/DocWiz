using DocWiz.Core.Markdown;
using Markdig;
using Markdig.Syntax;
using Xunit;

namespace DocWiz.Core.Tests.Pdf;

public class LinkRewriterSingleDocTests
{
    private static MarkdownDocument Parse(string md, MarkdownPipeline p) => Markdig.Markdown.Parse(md, p);

    [Fact]
    public void SingleDoc_DwLink_BecomesNamespacedInDocAnchor()
    {
        var (project, pipeline) = TestRepo.LoadDemoProject();
        var firstChapterId = project.Chapters[0].Id;
        var doc = Parse($"See [there](dw:{firstChapterId}#intro).", pipeline);

        LinkRewriter.Rewrite(doc, project, id => $"{id}.html",
            dwResolver: (cid, anchor) => "#" + (anchor is null ? cid : $"{cid}__{anchor}"),
            fragmentResolver: anchor => $"#current__{anchor}");

        var link = System.Linq.Enumerable.First(doc.Descendants<Markdig.Syntax.Inlines.LinkInline>());
        Assert.Equal($"#{firstChapterId}__intro", link.Url);
    }

    [Fact]
    public void SingleDoc_PlainFragmentLink_UsesFragmentResolver()
    {
        var (project, pipeline) = TestRepo.LoadDemoProject();
        var doc = Parse("Jump [down](#details).", pipeline);

        LinkRewriter.Rewrite(doc, project, id => $"{id}.html",
            dwResolver: (cid, a) => "#x",
            fragmentResolver: anchor => $"#chap__{anchor}");

        var link = System.Linq.Enumerable.First(doc.Descendants<Markdig.Syntax.Inlines.LinkInline>());
        Assert.Equal("#chap__details", link.Url);
    }

    [Fact]
    public void ResolverReturningNull_LeavesTheTextAndKeepsTheId()
    {
        var (project, pipeline) = TestRepo.LoadDemoProject();
        var cid = project.Chapters[0].Id;
        var doc = Parse($"See [the intro](dw:{cid}#intro){{#keep}} now.", pipeline);

        LinkRewriter.Rewrite(doc, project, id => $"{id}.html", dwResolver: (_, _) => null);

        var html = DocWizMarkdown.RenderHtml(doc, pipeline);
        Assert.DoesNotContain("<a", html);
        Assert.Contains("<span id=\"keep\">the intro</span> now.", html);
    }

    [Fact]
    public void Default_NoResolvers_KeepsHandoverBehavior()
    {
        var (project, pipeline) = TestRepo.LoadDemoProject();
        var cid = project.Chapters[0].Id;
        var doc = Parse($"See [there](dw:{cid}#intro).", pipeline);

        LinkRewriter.Rewrite(doc, project, id => $"{id}.html");

        var link = System.Linq.Enumerable.First(doc.Descendants<Markdig.Syntax.Inlines.LinkInline>());
        Assert.Equal($"{cid}.html#intro", link.Url);
    }
}
