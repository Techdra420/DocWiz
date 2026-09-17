using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>
/// ADR-008 parity gate (audit F4): the asset-URL prefix has ONE source — <c>ProjectLayout.AssetsFolder</c>.
/// <c>LinkRewriter</c> emits <c>{AssetsFolder}/&lt;name&gt;</c> and <c>SingleFileExporter</c>'s inliner
/// derives its match regex from the same constant, so the single-file export can't silently stop
/// inlining every image if the folder name ever changes. On the editor side the file-relative →
/// <c>/assets/</c> surface mapping is one shared helper (<c>toolhelpers.js toSurfaceSrc</c>), not a
/// per-file regex copy.
/// </summary>
public class AssetPathParityTests
{
    [Fact]
    public void SingleFileExporter_DerivesItsImageRegexFromTheAssetsFolderConstant()
    {
        var src = TestRepo.Read("src", "DocWiz.Build", "SingleFileExporter.cs");
        Assert.Contains("ProjectLayout.AssetsFolder", src);
        Assert.DoesNotContain("src=\\\"assets", src);   // no hardcoded "assets/" prefix in the regex
    }

    [Fact]
    public void LinkRewriter_EmitsAssetUrlsViaTheConstant()
    {
        var src = TestRepo.Read("src", "DocWiz.Core", "Markdown", "LinkRewriter.cs");
        Assert.Contains("{ProjectLayout.AssetsFolder}/", src);
        Assert.DoesNotContain("$\"assets/", src);       // no hardcoded prefix
    }

    [Fact]
    public void EditorImageSites_UseTheOneSurfaceSrcHelper()
    {
        // figureview.js and tableview.js display images via the shared toSurfaceSrc, not their own
        // copy of the file-relative -> /assets/ regex.
        foreach (var file in new[] { "figureview.js", "tableview.js" })
        {
            var js = TestRepo.Read("src", "DocWiz.App", "wwwroot", "editor2", file);
            Assert.Contains("toSurfaceSrc", js);
            Assert.DoesNotContain(")+assets", js);       // the inline /^(\.\.\/)+assets\// regex is gone
        }
        Assert.Contains("export function toSurfaceSrc",
            TestRepo.Read("src", "DocWiz.App", "wwwroot", "editor2", "toolhelpers.js"));
    }
}
