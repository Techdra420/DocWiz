using DocWiz.App;
using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>
/// The editor save path must write deterministic files: LF everywhere, exactly one
/// trailing newline, unknown frontmatter keys preserved (ADR-005 territory). Found by
/// the fidelity harness: AppendLine on Windows produced mixed CRLF/LF files.
/// </summary>
public class ChapterYamlTests
{
    [Fact]
    public void Compose_NeverEmitsCarriageReturns()
    {
        var fm = ChapterYaml.Parse("id: x\ntitle: T\n");
        var composed = ChapterYaml.Compose(fm, "# T\n\nBody line.\r\nCRLF line.\n");
        Assert.DoesNotContain('\r', composed);
    }

    [Fact]
    public void Compose_EndsWithExactlyOneNewline()
    {
        var fm = ChapterYaml.Parse("id: x\n");
        var composed = ChapterYaml.Compose(fm, "Body.\n\n\n");
        Assert.EndsWith("Body.\n", composed);
        Assert.False(composed.EndsWith("Body.\n\n", StringComparison.Ordinal));
    }

    [Fact]
    public void Compose_PreservesUnknownFrontmatterKeys()
    {
        var fm = ChapterYaml.Parse("id: x\ntitle: T\ncustom_key: keep-me\n");
        fm["title"] = "New";
        var composed = ChapterYaml.Compose(fm, "Body.\n");
        Assert.Contains("custom_key: keep-me", composed);
        Assert.Contains("title: New", composed);
        Assert.StartsWith("---\n", composed);
    }

    [Fact]
    public void Compose_RoundTripsThroughSplit()
    {
        var fm = ChapterYaml.Parse("id: x\ntitle: T\n");
        var composed = ChapterYaml.Compose(fm, "# T\n\nBody.\n");
        var (yaml, body) = DocWiz.Core.Loading.FrontmatterParser.Split(composed);
        Assert.NotNull(yaml);
        Assert.Equal("# T\n\nBody.\n", body.TrimStart('\n') );
        Assert.Equal("x", ChapterYaml.Parse(yaml)["id"]);
    }
}
