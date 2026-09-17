using DocWiz.Core.Loading;
using DocWiz.Core.Markdown;
using DocWiz.Core.Models;
using DocWiz.Core.Validation;
using Markdig.Extensions.CustomContainers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Xunit;

namespace DocWiz.Core.Tests;

public class GroupContainerTests
{
    private static MarkdownDocument Parse(string md) =>
        Markdig.Markdown.Parse(md, DocWizMarkdown.CreatePipeline());

    /// <summary>Fixture construction mirrors TransclusionResolverTests.MakeChapter.</summary>
    private static Chapter MakeChapter(string markdown)
    {
        var chapter = new Chapter
        {
            Id = "ch", Title = "Chapter Title", FilePath = "ch.md",
            SectionSlug = "s", SectionTitle = "S", SortKey = "0", Markdown = markdown,
        };
        chapter.Parsed = Markdig.Markdown.Parse(markdown, DocWizMarkdown.CreatePipeline());
        return chapter;
    }

    [Fact]
    public void Group_id_is_collected_as_an_anchor()
    {
        var md = "::: group ESTOP locations & reset {#estop-text desc=\"where they are\"}\n"
               + "Body paragraph.\n\n- one\n- two\n:::\n";
        var anchors = DocWizMarkdown.CollectAnchors(Parse(md));
        Assert.Contains("estop-text", anchors);
    }

    [Fact]
    public void Group_head_yields_name_and_description()
    {
        var g = Assert.Single(DocWizGroupExtension.Scan(
            "::: group ESTOP locations & reset {#estop-text desc=\"where they are\"}\nBody.\n:::"));
        Assert.Equal("estop-text", g.Id);
        Assert.Equal("ESTOP locations & reset", g.Name);
    }

    [Fact]
    public void ParseHead_bare_name_has_no_id()
    {
        var (name, id, desc) = DocWizGroupExtension.ParseHead("Just a name", null, null);
        Assert.Equal("Just a name", name);
        Assert.Null(id);
        Assert.Null(desc);
    }

    [Fact]
    public void ParseHead_keeps_a_preparsed_attribute_id()
    {
        // The live Markdig path: generic-attributes already stripped {…} and set the id.
        var (name, id, _) = DocWizGroupExtension.ParseHead("ESTOP locations & reset", "estop-text", null);
        Assert.Equal("ESTOP locations & reset", name);
        Assert.Equal("estop-text", id);
    }

    [Fact]
    public void Group_renders_plain_div_with_id_and_data_attrs_no_label()
    {
        var md = "::: group ESTOP locations {#estop-text desc=\"where they are\"}\nBody paragraph.\n:::\n";
        var pipeline = DocWizMarkdown.CreatePipeline();
        var html = DocWizMarkdown.RenderHtml(Parse(md), pipeline);
        Assert.Contains("<div class=\"dw-group\" id=\"estop-text\"", html);
        Assert.Contains("data-dw-name=\"ESTOP locations\"", html);
        Assert.Contains("data-dw-desc=\"where they are\"", html);
        Assert.DoesNotContain("dw-callout", html);
        Assert.Contains("Body paragraph.", html);
    }

    [Fact]
    public void Embed_of_a_group_resolves_to_inner_content_without_the_fence()
    {
        var md = "## Section {#sec}\n\n::: group ESTOP {#estop-text}\nThe cell has 6 buttons.\n\n- one\n- two\n:::\n";
        var chapter = MakeChapter(md);
        var section = TransclusionResolver.Resolve(chapter, "estop-text");
        Assert.Contains("The cell has 6 buttons.", section.Markdown);
        Assert.Contains("- one", section.Markdown);
        Assert.DoesNotContain("::: group", section.Markdown);
        Assert.DoesNotContain(":::", section.Markdown);
    }

    // Fixture pattern mirrors WidthsValidationTests: write real files, ProjectLoader.Load,
    // ParseAll, then Validate — required so chapter.Parsed is populated.
    private readonly string _validatorRoot =
        Path.Combine(Path.GetTempPath(), "docwiz-grp-" + Guid.NewGuid().ToString("N"));

    private List<ValidationIssue> CheckValidator(string chapterBody)
    {
        Directory.CreateDirectory(Path.Combine(_validatorRoot, "content", "010-overview"));
        File.WriteAllText(Path.Combine(_validatorRoot, "docwiz.yaml"),
            """
            project: T
            code: T-1
            audiences:
              - id: operator
                title: Operator
            """);
        File.WriteAllText(Path.Combine(_validatorRoot, "content", "010-overview", "010-c.md"),
            """
            ---
            id: c1
            title: Intro
            audiences: [operator]
            ---
            """ + "\n## S {#s}\n\n" + chapterBody);
        var project = ProjectLoader.Load(_validatorRoot);
        DocWizMarkdown.ParseAll(project, DocWizMarkdown.CreatePipeline());
        return ProjectValidator.Validate(project);
    }

    [Fact]
    public void Group_without_id_is_warned()
    {
        var issues = CheckValidator("::: group No anchor here\nBody.\n:::\n");
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Warning
            && i.Message.Contains("group", StringComparison.OrdinalIgnoreCase));
    }

    // The ONE canonical group-head wire form. serialize.js container() (Task 7) MUST emit
    // exactly this string for a group node — this test is the ADR-008 parity contract.
    [Theory]
    [InlineData("ESTOP locations & reset", "estop-text", "where they are")]
    [InlineData("Plain name", "plain", "")]
    [InlineData("", "noname", "")]
    public void Group_canonical_head_round_trips(string name, string id, string desc)
    {
        var head = "group" + (name.Length > 0 ? " " + name : "")
                 + " {#" + id + (desc.Length > 0 ? " desc=\"" + desc + "\"" : "") + "}";
        var md = "::: " + head + "\nBody.\n:::\n";
        var cc = Assert.Single(Parse(md).Descendants<CustomContainer>());
        var attrs = cc.GetAttributes();
        Assert.Equal(id, attrs.Id);
        var gotName = attrs.Properties?.FirstOrDefault(p => p.Key == DocWizGroupExtension.NameProp).Value ?? "";
        var gotDesc = attrs.Properties?.FirstOrDefault(p => p.Key == DocWizGroupExtension.DescProp).Value ?? "";
        Assert.Equal(name, gotName);
        Assert.Equal(desc, gotDesc);
    }
}
