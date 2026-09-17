using DocWiz.Core.Markdown;
using Xunit;

namespace DocWiz.Core.Tests;

public class ElementTests
{
    private static string Render(string markdown)
    {
        var pipeline = DocWizMarkdown.CreatePipeline();
        var doc = Markdig.Markdown.Parse(markdown, pipeline);
        return DocWizMarkdown.RenderHtml(doc, pipeline);
    }

    [Fact]
    public void TaskList_RendersGitHubMarkup()
    {
        // The editor's ingest (data-checked strip) and site.css key off these exact classes —
        // pin the contract so a Markdig bump can't silently break task lists (Phase 3).
        var html = Render("- [ ] open\n- [x] done\n");
        Assert.Contains("<ul class=\"contains-task-list\">", html);
        Assert.Contains("<li class=\"task-list-item\"><input disabled=\"disabled\" type=\"checkbox\" /> open</li>", html);
        Assert.Contains("<input disabled=\"disabled\" type=\"checkbox\" checked=\"checked\" /> done", html);
    }

    [Theory]
    [InlineData("note", "Note")]
    [InlineData("tip", "Tip")]
    [InlineData("warning", "Warning")]
    [InlineData("danger", "Danger")]
    public void Callout_RendersWithDefaultLabel(string kind, string label)
    {
        var html = Render($"::: {kind}\nBody text.\n:::\n");
        Assert.Contains($"dw-callout dw-{kind}", html);
        Assert.Contains($"<span class=\"dw-callout-tag\">{label}</span>", html);
        Assert.Contains("Body text.", html);
    }

    [Fact]
    public void Callout_CustomLabelFromArguments()
    {
        var html = Render("::: warning Hot surface\nDo not touch.\n:::\n");
        Assert.Contains("<span class=\"dw-callout-tag\">Hot surface</span>", html);
    }

    [Fact]
    public void Steps_WrapsOrderedList()
    {
        var html = Render("::: steps\n1. First.\n2. Second.\n:::\n");
        Assert.Contains("<div class=\"dw-steps\">", html);
        Assert.Contains("<ol>", html);
    }

    [Fact]
    public void Cards_WrapsList_LinksIntact()
    {
        var html = Render("::: cards\n- [Title](dw:ch) — short text\n:::\n");
        Assert.Contains("<div class=\"dw-cards\">", html);
        Assert.Contains("href=\"dw:ch\"", html); // dw: rewritten later by LinkRewriter
    }

    [Fact]
    public void Buttons_And_Details_Render()
    {
        var html = Render("::: buttons\n[Go](dw:ch)\n:::\n\n::: details Spare parts\nList here.\n:::\n");
        Assert.Contains("<div class=\"dw-buttons\">", html);
        Assert.Contains("<details class=\"dw-details\"><summary>Spare parts</summary>", html);
        Assert.Contains("List here.", html);
    }

    [Fact]
    public void AlignmentContainers_Render()
    {
        var html = Render("::: center\nMiddle.\n:::\n\n::: right\nEdge.\n:::\n");
        Assert.Contains("<div class=\"dw-align-center\">", html);
        Assert.Contains("<div class=\"dw-align-right\">", html);
    }

    [Fact]
    public void ImageAlignmentClass_RendersOnImg()
    {
        var html = Render("![Layout](assets/x.png){width=640 .img-center}\n");
        Assert.Contains("width=\"640\"", html);
        Assert.Contains("class=\"img-center\"", html);
    }

    [Fact]
    public void AudienceContainer_StillRendersDataAud()
    {
        var html = Render("::: audience operator\nHi.\n:::\n");
        Assert.Contains("data-aud=\"operator\"", html);
    }

    [Fact]
    public void UnknownContainer_RendersPlainDiv()
    {
        var html = Render("::: bogus\nText.\n:::\n");
        Assert.DoesNotContain("dw-callout", html);
        Assert.Contains("Text.", html);
    }

    [Theory]
    [InlineData("| H | x |\n|---|---|\n| one<br>two | b |\n")]
    [InlineData("| H | x |\n|---|---|\n| one<br/>two | b |\n")]
    [InlineData("| H | x |\n|---|---|\n| one<br />two | b |\n")]
    public void TableCell_With_BrTag_Renders_LineBreak(string markdown)
    {
        // A literal <br> in a cell must become a line break, with the text BEFORE it
        // preserved (the pipe-table cell rebuild used to drop content before a LineBreak).
        var html = Render(markdown).Replace("<br />", "<br>").Replace("\n", "");
        Assert.Contains("<td>one<br>two</td>", html);
    }

    [Fact]
    public void Paragraph_With_BrTag_Renders_LineBreak()
    {
        // <br> is DocWiz's canonical hard break everywhere — in body text as well as in
        // cells (DocWizLineBreak.cs). Only <br> is special; other angle text stays literal
        // (see AngleText_StillRendersLiterally_NotAsBr).
        var html = Render("one<br>two").Replace("<br />", "<br>");
        Assert.Contains("one<br>", html);
        Assert.Contains("two", html);
    }

    [Fact]
    public void AngleText_StillRendersLiterally_NotAsBr()
    {
        // The <br> exception must not reopen raw HTML: "<24V>" and "<script>" stay literal.
        var html = Render("Voltage <24V> and <script>x</script> and <break> here.");
        Assert.Contains("&lt;24V&gt;", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&lt;break&gt;", html);
        Assert.DoesNotContain("<script>", html);
    }
}
