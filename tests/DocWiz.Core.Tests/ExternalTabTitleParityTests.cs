using System.Linq;
using System.Text.RegularExpressions;
using DocWiz.Build;
using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>
/// The editor's add-package picker (app.js PKG_TYPES) hardcodes a display label per external
/// doc-package key. The shipped handover tab title is produced by HandoverBuilder.ExternalTabTitle
/// — the ONE source (ADR-008, ExternalDocs.cs). This pins each picker label to equal what the
/// handover actually renders so the two can't silently drift (review finding I1).
/// </summary>
public class ExternalTabTitleParityTests
{
    [Fact]
    public void PickerLabelsMatchExternalTabTitle()
    {
        var appJs = TestRepo.Read("src", "DocWiz.App", "wwwroot", "app.js");

        var block = Regex.Match(appJs, @"PKG_TYPES\s*=\s*\[(.*?)\];", RegexOptions.Singleline).Groups[1].Value;
        var pairs = Regex.Matches(block, @"\[\s*'([^']+)'\s*,\s*'([^']+)'\s*\]")
            .Select(m => (Key: m.Groups[1].Value, Label: m.Groups[2].Value))
            .ToList();

        Assert.NotEmpty(pairs); // guard: the regex actually found the JS list

        foreach (var (key, label) in pairs)
            Assert.Equal(HandoverBuilder.ExternalTabTitle(key), label);
    }
}
