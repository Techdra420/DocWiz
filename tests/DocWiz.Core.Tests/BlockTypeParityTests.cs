using System.Text.RegularExpressions;
using DocWiz.Core.Markdown;
using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>
/// ADR-008 parity gate (audit F3): the editor's PM-native block-type set (<c>NATIVE</c> in
/// <c>wwwroot/editor2/ingest.js</c>) and the CLI L1 fidelity scan's modeled set are derived
/// from the ONE C# source (<see cref="BlockSegmenter.EditorNativeBlockTypes"/> /
/// <see cref="BlockSegmenter.ModeledBlockTypes"/>). A "change them together" comment used to
/// be the only guard; this test fails on drift instead.
/// </summary>
public class BlockTypeParityTests
{
    [Fact]
    public void IngestNativeSet_MatchesEditorNativeBlockTypes()
    {
        var ingest = TestRepo.Read("src", "DocWiz.App", "wwwroot", "editor2", "ingest.js");
        var m = Regex.Match(ingest, @"const\s+NATIVE\s*=\s*new\s+Set\(\[(?<items>[^\]]*)\]\)");
        Assert.True(m.Success, "Could not find `const NATIVE = new Set([...])` in ingest.js");

        var jsNative = m.Groups["items"].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.Trim('\'', '"')).Where(s => s.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            BlockSegmenter.EditorNativeBlockTypes.OrderBy(x => x, StringComparer.Ordinal),
            jsNative.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void ModeledBlockTypes_IsEditorNativePlusFigureAndTable()
    {
        var expected = BlockSegmenter.EditorNativeBlockTypes
            .Append("figure").Append("table")
            .OrderBy(x => x, StringComparer.Ordinal);

        Assert.Equal(expected, BlockSegmenter.ModeledBlockTypes.OrderBy(x => x, StringComparer.Ordinal));
    }
}
