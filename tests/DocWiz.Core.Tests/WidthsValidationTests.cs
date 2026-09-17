using DocWiz.Core.Loading;
using DocWiz.Core.Markdown;
using DocWiz.Core.Validation;
using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>{widths="…"} table attribute (live-editor M3): the validator warns when
/// the token count does not match the column count or a token is not a CSS length.</summary>
public class WidthsValidationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "docwiz-w-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private List<ValidationIssue> Check(string chapterBody)
    {
        Directory.CreateDirectory(Path.Combine(_root, "content", "010-overview"));
        File.WriteAllText(Path.Combine(_root, "docwiz.yaml"),
            """
            project: T
            code: T-1
            audiences:
              - id: operator
                title: Operator
            """);
        File.WriteAllText(Path.Combine(_root, "content", "010-overview", "010-c.md"),
            """
            ---
            id: c1
            title: Intro
            audiences: [operator]
            ---
            """ + "\n# Intro\n\n" + chapterBody);
        var project = ProjectLoader.Load(_root);
        DocWizMarkdown.ParseAll(project, DocWizMarkdown.CreatePipeline());
        return ProjectValidator.Validate(project);
    }

    [Fact]
    public void MatchingWidths_NoWarning()
    {
        var issues = Check("{widths=\"30% 70%\"}\n| A | B |\n|---|---|\n| 1 | 2 |\n");
        Assert.DoesNotContain(issues, i => i.Message.Contains("widths"));
    }

    [Fact]
    public void CountMismatch_Warns()
    {
        var issues = Check("{widths=\"30% 30% 40%\"}\n| A | B |\n|---|---|\n| 1 | 2 |\n");
        var w = Assert.Single(issues, i => i.Message.Contains("widths"));
        Assert.Equal(IssueSeverity.Warning, w.Severity);
        Assert.Contains("3", w.Message);
        Assert.Contains("2 column", w.Message);
    }

    [Fact]
    public void InvalidToken_Warns()
    {
        var issues = Check("{widths=\"30% wide\"}\n| A | B |\n|---|---|\n| 1 | 2 |\n");
        Assert.Contains(issues, i => i.Message.Contains("widths") && i.Message.Contains("wide"));
    }
}
