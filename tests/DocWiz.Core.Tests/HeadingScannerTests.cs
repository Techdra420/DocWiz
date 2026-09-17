using DocWiz.Core.Markdown;
using Xunit;

namespace DocWiz.Core.Tests;

public class HeadingScannerTests
{
    private const string Body = """
        # Title

        ## Anchored {#mine}

        text

        ```
        # not a heading (code fence)
        ```

        ### Sub heading & special: chars
        """;

    [Fact]
    public void Scan_FindsHeadings_SkipsFences_ReportsAnchors()
    {
        var heads = HeadingScanner.Scan(Body);

        Assert.Equal(3, heads.Count);
        Assert.Equal(["Title", "Anchored", "Sub heading & special: chars"],
            heads.Select(h => h.Text).ToArray());
        Assert.Equal([1, 2, 3], heads.Select(h => h.Level).ToArray());
        Assert.Equal("mine", heads[1].Anchor);
        Assert.Null(heads[2].Anchor);
        // Line points at the heading line in the body (0-based).
        Assert.Equal("## Anchored {#mine}", Body.Replace("\r\n", "\n").Split('\n')[heads[1].Line]);
    }

    [Fact]
    public void Slug_NormalizesAndNeverEmpty()
    {
        Assert.Equal("daily-startup-checks", HeadingScanner.Slug("Daily Startup & Checks!"));
        Assert.Equal("section", HeadingScanner.Slug("???"));
    }

    [Fact]
    public void UniqueAnchor_AppendsCounter()
    {
        var taken = new HashSet<string> { "x", "x-2" };
        Assert.Equal("x-3", HeadingScanner.UniqueAnchor("x", taken));
        Assert.Equal("y", HeadingScanner.UniqueAnchor("y", taken));
    }
}
