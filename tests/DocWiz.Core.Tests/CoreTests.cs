using DocWiz.Core.Loading;
using DocWiz.Core.Markdown;
using DocWiz.Core.Models;
using DocWiz.Core.Validation;
using Markdig.Syntax;
using Xunit;

namespace DocWiz.Core.Tests;

public class FrontmatterParserTests
{
    [Fact]
    public void Split_ExtractsYamlAndBody()
    {
        var (yaml, body) = FrontmatterParser.Split("---\nid: x\ntitle: T\n---\n\n# Hello\n");
        Assert.Equal("id: x\ntitle: T", yaml);
        Assert.StartsWith("\n# Hello", body);
    }

    [Fact]
    public void Split_NoFrontmatter_ReturnsNullYaml()
    {
        var (yaml, body) = FrontmatterParser.Split("# Just a heading\n");
        Assert.Null(yaml);
        Assert.Equal("# Just a heading\n", body);
    }
}

public class MarkdownPipelineTests
{
    [Fact]
    public void AudienceContainer_RendersDataAudAttribute()
    {
        var pipeline = DocWizMarkdown.CreatePipeline();
        var doc = Markdig.Markdown.Parse("::: audience operator maintenance\nHi.\n:::\n", pipeline);
        var html = DocWizMarkdown.RenderHtml(doc, pipeline);

        Assert.Contains("data-aud=\"operator maintenance\"", html);
        Assert.Contains("Only for operator, maintenance", html); // no project: the ids
    }

    [Fact]
    public void AudienceContainer_NamesItsReadersByTitle_WhenTheProjectIsKnown()
    {
        var pipeline = DocWizMarkdown.CreatePipeline(
            [new AudienceDef { Id = "operator", Title = "Operátor" }, new AudienceDef { Id = "maintenance", Title = "Maintenance" }]);
        var doc = Markdig.Markdown.Parse("::: audience operator maintenance\nHi.\n:::\n", pipeline);
        var html = DocWizMarkdown.RenderHtml(doc, pipeline);

        Assert.Contains("data-aud=\"operator maintenance\"", html);
        Assert.Contains(">Only for Operátor, Maintenance</span>", html);
    }

    [Fact]
    public void AudienceFilter_DropsAStepWhoseOnlyContentIsForOtherReaders()
    {
        var pipeline = DocWizMarkdown.CreatePipeline();
        var md = "1. Scan the delivery note.\n2. ::: audience admin\n   Check the server log.\n   :::\n3. Confirm the shelf.\n";
        var doc = Markdig.Markdown.Parse(md, pipeline);
        AudienceFilter.Apply(doc, "user");
        var html = DocWizMarkdown.RenderHtml(doc, pipeline);

        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "<li>").Count);
        Assert.DoesNotContain("server log", html);

        // Inside ::: steps the outer fence is longer (the editor's serializer writes it so).
        var inSteps = Markdig.Markdown.Parse(":::: steps\n\n" + md + "\n::::\n", pipeline);
        Assert.Empty(ProjectValidatorProbe.UnknownContainers(inSteps));
        AudienceFilter.Apply(inSteps, "user");
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(DocWizMarkdown.RenderHtml(inSteps, pipeline), "<li>").Count);

        var adminDoc = Markdig.Markdown.Parse(md, pipeline);
        AudienceFilter.Apply(adminDoc, "admin");
        var adminHtml = DocWizMarkdown.RenderHtml(adminDoc, pipeline);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(adminHtml, "<li>").Count);
        Assert.Contains("server log", adminHtml);
    }

    [Fact]
    public void RawHtml_IsEscaped_NotPassedThrough()
    {
        var pipeline = DocWizMarkdown.CreatePipeline();
        var doc = Markdig.Markdown.Parse("Voltage <24V> and <script>alert(1)</script> here.", pipeline);
        var html = DocWizMarkdown.RenderHtml(doc, pipeline);

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;24V&gt;", html);
    }

    [Fact]
    public void Headings_GetAnchors_ExplicitAndAuto()
    {
        var pipeline = DocWizMarkdown.CreatePipeline();
        var doc = Markdig.Markdown.Parse("## Daily startup {#daily-startup}\n\n## Some Other Title\n", pipeline);
        var anchors = DocWizMarkdown.CollectAnchors(doc);

        Assert.Contains("daily-startup", anchors);
        Assert.Contains("some-other-title", anchors);
    }
}

internal static class ProjectValidatorProbe
{
    /// <summary>Container names the pipeline doesn't know — what Check warns about.</summary>
    public static List<string> UnknownContainers(Markdig.Syntax.MarkdownDocument doc) =>
        doc.Descendants<Markdig.Extensions.CustomContainers.CustomContainer>()
            .Select(c => c.Info ?? "")
            .Where(info => !DocWizContainerRenderer.KnownContainers.Contains(info))
            .ToList();
}

public class ProjectRoundtripTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "docwiz-test-" + Guid.NewGuid().ToString("N"));

    public ProjectRoundtripTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "content", "010-intro"));
        Directory.CreateDirectory(Path.Combine(_root, "views"));
        File.WriteAllText(Path.Combine(_root, "docwiz.yaml"),
            """
            project: Test Cell
            code: T-001
            audiences:
              - id: operator
                title: Operator
            """);
        File.WriteAllText(Path.Combine(_root, "content", "010-intro", "010-first.md"),
            """
            ---
            id: first
            title: First Chapter
            audiences: [operator]
            ---

            # First Chapter

            ## Startup {#startup}

            See [self](dw:first#startup) and [broken](dw:nope#x).
            """);
        File.WriteAllText(Path.Combine(_root, "views", "v.yaml"),
            """
            id: training
            title: Training
            audience: operator
            mode: slides
            slides:
              - include: first#startup
              - include: first#missing-anchor
            """);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void LoadParseValidate_FindsExpectedIssues()
    {
        var project = ProjectLoader.Load(_root);
        var pipeline = DocWizMarkdown.CreatePipeline();
        DocWizMarkdown.ParseAll(project, pipeline);
        var issues = ProjectValidator.Validate(project);

        Assert.Single(project.Chapters);
        Assert.Equal("first", project.Chapters[0].Id);
        Assert.Contains("startup", project.Chapters[0].Anchors);

        // The broken dw: link and the view's missing anchor must both be errors.
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Error && i.Message.Contains("'nope'"));
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Error && i.Message.Contains("missing-anchor"));
        // The valid self-link must not be flagged.
        Assert.DoesNotContain(issues, i => i.Message.Contains("dw:first#startup"));
    }
}

/// <summary>External-doc keys become folder + file names in the handover (external-&lt;key&gt;.html),
/// so a hand-edited manifest with a malformed key (path traversal / illegal chars) must be a build
/// error — not silently path-resolved. The editor guards keys on add; the validator closes the
/// build path.</summary>
public class ExternalKeyValidationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "docwiz-extkey-" + Guid.NewGuid().ToString("N"));

    public ExternalKeyValidationTests()
    {
        Directory.CreateDirectory(_root);
        // "Bad-Key" violates ^[a-z0-9_]+$ (uppercase + hyphen) but is a plain, loadable YAML key.
        File.WriteAllText(Path.Combine(_root, "docwiz.yaml"),
            """
            project: Test Cell
            code: T-001
            audiences:
              - id: operator
                title: Operator
            external:
              Bad-Key: external/x
            """);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void MalformedExternalKey_IsError()
    {
        var project = ProjectLoader.Load(_root);
        var pipeline = DocWizMarkdown.CreatePipeline();
        DocWizMarkdown.ParseAll(project, pipeline);
        var issues = ProjectValidator.Validate(project);

        Assert.Contains(issues, i => i.Severity == IssueSeverity.Error
            && i.Source == ProjectLayout.ManifestFileName
            && i.Message.Contains("Bad-Key"));
    }
}
