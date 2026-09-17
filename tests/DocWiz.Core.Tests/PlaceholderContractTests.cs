using System.Text.RegularExpressions;
using DocWiz.Core.Markdown;
using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>
/// ADR-008 parity gate (audit F7): the missing-image placeholder class + text are ONE
/// contract across C# (<see cref="LinkRewriter"/>) and JS (<c>editor2/constants.js</c>, which
/// <c>schema.js</c> imports). They must match byte-for-byte or the cell faithfulness gate
/// locks every cell holding a placeholder.
/// </summary>
public class PlaceholderContractTests
{
    [Fact]
    public void ConstantsJs_PlaceholderClassAndText_MatchLinkRewriter()
    {
        var constants = TestRepo.Read("src", "DocWiz.App", "wwwroot", "editor2", "constants.js");
        Assert.Equal(LinkRewriter.ImgPlaceholderClass, JsConst(constants, "IMG_PLACEHOLDER_CLASS"));
        Assert.Equal(LinkRewriter.MissingImageText, JsConst(constants, "MISSING_IMAGE_TEXT"));
    }

    [Fact]
    public void SchemaJs_UsesThePlaceholderConstants_NotHardcodedLiterals()
    {
        var schema = TestRepo.Read("src", "DocWiz.App", "wwwroot", "editor2", "schema.js");
        Assert.Contains("IMG_PLACEHOLDER_CLASS", schema);
        Assert.Contains("from './constants.js'", schema);
        // The hardcoded literals must be gone: a typo in a literal would silently break the
        // byte-for-byte contract without any test catching it.
        Assert.DoesNotContain("'" + LinkRewriter.ImgPlaceholderClass + "'", schema);
        Assert.DoesNotContain("'" + LinkRewriter.MissingImageText + "'", schema);
    }

    private static string JsConst(string source, string name)
    {
        var m = Regex.Match(source, name + @"\s*=\s*'(?<v>[^']*)'");
        Assert.True(m.Success, $"Could not find `{name} = '...'` in constants.js");
        return m.Groups["v"].Value;
    }
}
