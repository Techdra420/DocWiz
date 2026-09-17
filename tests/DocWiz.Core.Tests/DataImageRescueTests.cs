using DocWiz.App;
using Xunit;

namespace DocWiz.Core.Tests;

public class DataImageRescueTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "docwiz-rescue-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // A valid 1x1 transparent PNG.
    private const string Png1x1 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M8AAAMBAQDX/ueWAAAAAElFTkSuQmCC";

    [Fact]
    public void Rewrite_InlineDataImageInTableCell_WritesAssetAndRewritesLink()
    {
        Directory.CreateDirectory(_root);
        var body = "# T\n\n| a | b |\n|---|---|\n| ![danger](data:image/png;base64," + Png1x1 + ") |  |\n";

        var result = DataImageRescue.Rewrite(body, _root);

        Assert.DoesNotContain("data:image", result);
        Assert.Contains("![danger](../../assets/pasted/", result); // alt text + canonical link survive
        var files = Directory.GetFiles(Path.Combine(_root, "assets", "pasted"), "*.png");
        Assert.Single(files); // the decoded bytes were written
    }

    [Fact]
    public void Rewrite_NoDataImage_ReturnsUnchanged()
    {
        var body = "# T\n\nplain text and ![x](../../assets/real.png)\n";
        Assert.Equal(body, DataImageRescue.Rewrite(body, _root));
    }

    [Fact]
    public void Rewrite_SameImageTwice_DedupesByContentHash()
    {
        Directory.CreateDirectory(_root);
        var uri = "data:image/png;base64," + Png1x1;
        var body = "![a](" + uri + ") and ![b](" + uri + ")";

        var result = DataImageRescue.Rewrite(body, _root);

        Assert.DoesNotContain("data:image", result);
        var files = Directory.GetFiles(Path.Combine(_root, "assets", "pasted"), "*.png");
        Assert.Single(files); // identical content → one file
    }

    [Fact]
    public void Rewrite_MalformedBase64_LeftUntouched()
    {
        var body = "![x](data:image/png;base64,not valid base64!!!)";
        // The regex stops at the space, so the captured payload is "not" — invalid base64 → left as-is.
        var result = DataImageRescue.Rewrite(body, _root);
        Assert.Contains("data:image/png;base64,not", result);
    }
}
