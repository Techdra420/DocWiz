using DocWiz.Core.Loading;
using DocWiz.Core.Markdown;
using DocWiz.Core.Validation;
using Xunit;

namespace DocWiz.Core.Tests;

public class NumberingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "docwiz-num-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private void Scaffold(bool numbering, string chapterBody)
    {
        Directory.CreateDirectory(Path.Combine(_root, "content", "010-overview"));
        File.WriteAllText(Path.Combine(_root, "docwiz.yaml"),
            $"""
            project: T
            code: T-1
            numbering: {(numbering ? "true" : "false")}
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
            """ + "\n" + chapterBody);
    }

    private List<ValidationIssue> Check()
    {
        var project = ProjectLoader.Load(_root);
        DocWizMarkdown.ParseAll(project, DocWizMarkdown.CreatePipeline());
        return ProjectValidator.Validate(project);
    }

    [Fact]
    public void TypedHeadingNumbers_WarnWhenNumberingIsOn()
    {
        // Migrated-from-Word content: the typed prefixes would double-number.
        Scaffold(numbering: true, "# 1 Intro\n\n## 1.1 Modification\n\n## Clean heading\n");
        var issues = Check();

        var warnings = issues.Where(i => i.Message.Contains("typed number")).ToList();
        Assert.Equal(2, warnings.Count);
        Assert.All(warnings, w => Assert.Equal(IssueSeverity.Warning, w.Severity));
    }

    [Fact]
    public void TypedHeadingNumbers_AllowedWhenNumberingIsOff()
    {
        Scaffold(numbering: false, "# 1 Intro\n\n## 1.1 Modification\n");
        Assert.DoesNotContain(Check(), i => i.Message.Contains("typed number"));
    }

    [Fact]
    public void NumbersInsideText_DoNotWarn()
    {
        // Only a leading "N" / "N.N " prefix is suspicious — versions, counts, and
        // technical headings starting with a product name must stay silent.
        Scaffold(numbering: true, "# Intro\n\n## Step into 2025\n\n## X11-2000 Server\n");
        Assert.DoesNotContain(Check(), i => i.Message.Contains("typed number"));
    }
}
