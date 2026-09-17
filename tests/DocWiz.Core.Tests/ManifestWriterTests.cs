using DocWiz.Core.Loading;
using Xunit;

namespace DocWiz.Core.Tests;

public class ManifestWriterTests
{
    private const string Sample =
        "project: 10042_Stockroom\n" +
        "code: 10042 Stockroom\n" +
        "version: 0.1.0\n" +
        "\n" +
        "external:\n" +
        "  # api_reference is where the generated API site goes.\n" +
        "  api_reference: external/api\n" +
        "  release_notes: null\n";

    [Fact]
    public void SetExternal_AddsNewKey_PreservesCommentsAndOtherKeys()
    {
        var outp = ManifestWriter.SetExternal(Sample, "integration_guide", "external/integration");
        Assert.Contains("integration_guide: external/integration", outp);
        Assert.Contains("api_reference: external/api", outp);
        Assert.Contains("# api_reference is where the generated API site goes.", outp);
        Assert.Contains("project: 10042_Stockroom", outp);
    }

    [Fact]
    public void SetExternal_UpdatesExistingKey_InPlace()
    {
        var outp = ManifestWriter.SetExternal(Sample, "api_reference", "external/docs");
        Assert.Contains("api_reference: external/docs", outp);
        Assert.DoesNotContain("external/api\n", outp);
    }

    [Fact]
    public void RemoveExternal_DropsTheKeyOnly()
    {
        var outp = ManifestWriter.RemoveExternal(Sample, "api_reference");
        Assert.DoesNotContain("api_reference: external/api", outp); // key line gone
        Assert.Contains("release_notes: null", outp);
        Assert.Contains("external:", outp);
    }

    [Fact]
    public void SetExternal_NoExternalBlock_CreatesOne()
    {
        var outp = ManifestWriter.SetExternal("project: X\ncode: Y\n", "api_reference", "external/api");
        Assert.Contains("external:", outp);
        Assert.Contains("  api_reference: external/api", outp);
        Assert.Contains("project: X", outp);
    }

    [Fact]
    public void SetScalar_ReplacesValue_KeepsComments()
    {
        var outp = ManifestWriter.SetScalar(Sample, "version", "1.2.3");
        Assert.Contains("version: 1.2.3", outp);
        Assert.Contains("# api_reference is where the generated API site goes.", outp);
    }

    [Fact]
    public void SetScalar_MissingKey_InsertsIt()
    {
        var outp = ManifestWriter.SetScalar("project: X\nexternal:\n  a: b\n", "customer", "ACME");
        Assert.Contains("customer: ACME", outp);
        Assert.Contains("project: X", outp);
    }

    [Fact]
    public void SetScalar_PreservesCrlfLineEndings()
    {
        var crlf = "project: X\r\ncode: Y\r\n";
        var outp = ManifestWriter.SetScalar(crlf, "version", "1.0.0");
        Assert.Contains("\r\n", outp);
        Assert.DoesNotContain("project: X\n", outp.Replace("\r\n", "")); // no stray LF-only lines
    }

    [Fact]
    public void SetExternal_InsertsAdjacentToSiblings_NotAfterTrailingBlank()
    {
        var yaml = "project: X\nexternal:\n  a: b\n\n";   // trailing blank after the block
        var outp = ManifestWriter.SetExternal(yaml, "c", "d");
        Assert.Contains("  a: b\n  c: d", outp);          // new key sits with its sibling
    }

    [Fact]
    public void SetAudiences_ReplacesTheBlock_PreservesAfter()
    {
        var yaml = "project: X\naudiences:\n  - id: user\n    title: User\nversion: 1.0.0\n";
        var outp = ManifestWriter.SetAudiences(yaml, new[] { ("user", "User"), ("support", "Support") });
        Assert.Contains("- id: user", outp);
        Assert.Contains("- id: support", outp);
        Assert.Contains("title: Support", outp);
        Assert.Contains("version: 1.0.0", outp);
    }
}
