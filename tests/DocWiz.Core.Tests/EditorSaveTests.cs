using DocWiz.App;
using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>
/// Byte-surgical save (ADR-007 / M2): copy segments are written verbatim; only text
/// segments are new bytes. Heading anchors are auto-ensured ONLY inside text segments —
/// an untouched block must never be rewritten, not even to add an anchor.
/// </summary>
public class EditorSaveTests
{
    [Fact]
    public void BuildNewBody_CopyOnly_IsByteIdentical_NoAnchorInjection()
    {
        // The H2 has no anchor on purpose: a no-edit save must not add one.
        var body = "# T\n\n## Unanchored heading\n\nText.\n";
        var result = EditorDoc.BuildNewBody(body,
            [new SaveSegmentDto(0, body.Length, null)]);
        Assert.Equal(body, result);
    }

    [Fact]
    public void BuildNewBody_TextSegment_HeadingGetsUniqueAnchor()
    {
        var body = "# T\n\n## Old {#old}\n\nText.\n";
        var oldHeading = body.IndexOf("## Old", StringComparison.Ordinal);
        var afterHeading = body.IndexOf("\n\nText", StringComparison.Ordinal);
        var result = EditorDoc.BuildNewBody(body,
        [
            new SaveSegmentDto(0, oldHeading, null),
            new SaveSegmentDto(null, null, "## Brand New Section"),
            new SaveSegmentDto(afterHeading, body.Length, null),
        ]);
        Assert.Equal("# T\n\n## Brand New Section {#brand-new-section}\n\nText.\n", result);
    }

    [Fact]
    public void BuildNewBody_AnchorCollision_GetsSuffix()
    {
        var body = "## Setup {#setup}\n\nText.\n";
        var result = EditorDoc.BuildNewBody(body,
        [
            new SaveSegmentDto(0, body.Length, null),
            new SaveSegmentDto(null, null, "\n## Setup\n"),
        ]);
        Assert.Contains("## Setup {#setup-2}", result);
        Assert.Contains("## Setup {#setup}", result); // original untouched
    }

    [Fact]
    public void BuildNewBody_HeadingInsideCodeFence_IsNotAnchored()
    {
        var body = "Text.\n";
        var result = EditorDoc.BuildNewBody(body,
        [
            new SaveSegmentDto(0, body.Length, null),
            new SaveSegmentDto(null, null, "\n```text\n## not a heading\n```\n"),
        ]);
        Assert.DoesNotContain("{#not-a-heading}", result);
    }

    [Fact]
    public void BuildNewBody_H1InTextSegment_IsNotAnchored()
    {
        // The H1 is the chapter itself (dw:chapter-id) — never anchored, like v1.
        var body = "Text.\n";
        var result = EditorDoc.BuildNewBody(body,
        [
            new SaveSegmentDto(null, null, "# Chapter Title\n\n"),
            new SaveSegmentDto(0, body.Length, null),
        ]);
        Assert.DoesNotContain("{#chapter-title}", result);
    }

    [Fact]
    public void BuildNewBody_TextSegment_GroupGetsAnchor()
    {
        var body = "Text.\n";
        var result = EditorDoc.BuildNewBody(body,
        [
            new SaveSegmentDto(0, body.Length, null),
            new SaveSegmentDto(null, null, "\n::: group My Group\nbody\n:::\n"),
        ]);
        Assert.Contains("::: group My Group {#my-group}", result);
    }

    [Fact]
    public void BuildNewBody_TextSegment_GroupWithDesc_IdInsertedIntoBraces()
    {
        var body = "Text.\n";
        var result = EditorDoc.BuildNewBody(body,
        [
            new SaveSegmentDto(0, body.Length, null),
            new SaveSegmentDto(null, null, "\n::: group My Group {desc=\"a note\"}\nbody\n:::\n"),
        ]);
        Assert.Contains("::: group My Group {#my-group desc=\"a note\"}", result);
    }

    [Fact]
    public void BuildNewBody_TextSegment_GroupWithExistingId_IsUnchanged()
    {
        var body = "Text.\n";
        var result = EditorDoc.BuildNewBody(body,
        [
            new SaveSegmentDto(0, body.Length, null),
            new SaveSegmentDto(null, null, "\n::: group My Group {#existing}\nbody\n:::\n"),
        ]);
        Assert.Contains("::: group My Group {#existing}", result);
        Assert.DoesNotContain("{#my-group}", result);
    }

    [Fact]
    public void BuildNewBody_TextSegment_GroupAnchorCollision_GetsSuffix()
    {
        // A {#my-group} already exists elsewhere in the body (in the scan), so the
        // stamped group id must collide-avoid to my-group-2.
        var body = "## Heading {#my-group}\n\nText.\n";
        var result = EditorDoc.BuildNewBody(body,
        [
            new SaveSegmentDto(0, body.Length, null),
            new SaveSegmentDto(null, null, "\n::: group My Group\nbody\n:::\n"),
        ]);
        Assert.Contains("::: group My Group {#my-group-2}", result);
        Assert.Contains("## Heading {#my-group}", result); // original untouched
    }

    [Fact]
    public void BodyHash_IsStableAndMatchesBuild()
    {
        var a = EditorDoc.ComputeBodyHash("# T\n\nText.\n");
        var b = EditorDoc.ComputeBodyHash("# T\n\nText.\n");
        var c = EditorDoc.ComputeBodyHash("# T\n\nText!\n");
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal(64, a.Length); // sha256 hex, lowercase
        Assert.Equal(a, a.ToLowerInvariant());
    }
}
