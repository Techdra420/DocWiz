using DocWiz.Core.Markdown;
using Markdig.Syntax;
using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>
/// L1 of the fidelity harness (live-editor plan §4): the segmenter must partition a
/// chapter body into contiguous gap/block segments whose concatenation is the original
/// text, byte for byte — the foundation that makes no-edit saves diff-free by
/// construction.
/// </summary>
public class BlockSegmenterTests
{
    private static readonly Markdig.MarkdownPipeline Pipeline = DocWizMarkdown.CreatePipeline();

    private static IReadOnlyList<BodySegment> Segment(string body) =>
        BlockSegmenter.Segment(body, Pipeline);

    private static void AssertPartition(string body)
    {
        var segments = BlockSegmenter.Segment(body, Pipeline);
        if (body.Length == 0)
        {
            Assert.Empty(segments);
            return;
        }
        Assert.Equal(0, segments[0].Start);
        Assert.Equal(body.Length, segments[^1].End);
        for (var i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            Assert.True(s.End >= s.Start, $"segment {i} has negative length");
            Assert.Equal(body.Substring(s.Start, s.End - s.Start), s.Source);
            if (i > 0)
                Assert.Equal(segments[i - 1].End, s.Start);
        }
        Assert.Equal(body, string.Concat(segments.Select(s => s.Source)));
    }

    [Fact]
    public void Partition_SimpleProse_CoversEveryByte()
    {
        AssertPartition("# Title\n\nA paragraph.\n\n## Section {#sec}\n\nMore text.\n");
    }

    [Fact]
    public void Partition_NastyMix_CoversEveryByte()
    {
        // Container inside a list item, fence containing ::: lines, attribute line
        // above a table, double blank lines, trailing hard-break spaces.
        var body =
            "# Chapter\n" +
            "\n" +
            "\n" +
            "Intro with a hard break.  \n" +
            "Second line.\n" +
            "\n" +
            "::: warning Hot\nCareful.\n:::\n" +
            "\n" +
            "1. step one\n" +
            "   ::: note\n   nested-looking fence\n   :::\n" +
            "2. step two\n" +
            "\n" +
            "```text\n::: not-a-container\n```\n" +
            "\n" +
            "{#my-table}\n" +
            "| A | B |\n|:--|--:|\n| \\#1 | \\<x\\> |\n" +
            "\n" +
            "![](assets/pic.png)\n";
        AssertPartition(body);
    }

    [Fact]
    public void Partition_CrlfBody_CoversEveryByte()
    {
        AssertPartition("# Title\r\n\r\nParagraph one.\r\n\r\n| A |\r\n|---|\r\n| 1 |\r\n");
    }

    [Fact]
    public void Partition_BodyWithBrTags_CoversEveryByte()
    {
        // A <br> renders as a hard break via an IN-PLACE inline parser (no source
        // rewrite), so block spans still map 1:1 to the file bytes — the byte-surgical
        // save (ADR-007) must stay diff-free even with <br> in a paragraph AND a cell.
        var body =
            "First line<br>second line.\n" +
            "\n" +
            "| A | B |\n|---|---|\n| one<br>two | x<br/>y |\n" +
            "\n" +
            "Tail<br />end.\n";
        AssertPartition(body);
    }

    [Fact]
    public void Partition_EmptyAndWhitespaceOnly()
    {
        AssertPartition("");
        AssertPartition("\n\n\n");
    }

    [Fact]
    public void Blocks_AlternateWithGaps_AndClassify()
    {
        var segments = Segment("# Title\n\nText.\n\n| A |\n|---|\n| 1 |\n");
        var blocks = segments.Where(s => s.Kind == SegmentKind.Block).ToList();
        Assert.Equal(["heading", "paragraph", "table"], blocks.Select(b => b.BlockType!).ToArray());
        // Gaps are never adjacent to each other.
        for (var i = 1; i < segments.Count; i++)
            Assert.False(segments[i].Kind == SegmentKind.Gap && segments[i - 1].Kind == SegmentKind.Gap,
                "two adjacent gap segments");
    }

    [Fact]
    public void ImageOnlyParagraph_ClassifiesAsFigure()
    {
        var segments = Segment("Text before.\n\n![Layout](assets/x.png){width=640 .img-center}\n");
        Assert.Contains(segments, s => s.BlockType == "figure");
    }

    [Fact]
    public void AttributeLine_AboveTable_IsGluedIntoTheTableBlock()
    {
        // Editing a table's widths/id must rewrite the attribute line with it, so the
        // line belongs to the table's segment, never to a gap.
        var body = "Intro.\n\n{#my-table}\n| A | B |\n|---|---|\n| 1 | 2 |\n";
        var segments = Segment(body);
        var table = segments.Single(s => s.BlockType == "table");
        Assert.StartsWith("{#my-table}", table.Source);
        AssertPartition(body);
    }

    [Fact]
    public void ContainerBlock_SpansFenceToFence()
    {
        var body = "Before.\n\n::: audience operator maintenance\nOnly for them.\n:::\n\nAfter.\n";
        var segments = Segment(body);
        var container = segments.Single(s => s.BlockType == "container");
        Assert.StartsWith("::: audience", container.Source);
        Assert.EndsWith(":::", container.Source.TrimEnd());
    }

    // ---- BlockSourceSpan: the single-block span helper TransclusionResolver uses to
    // slice an element's verbatim source. Must apply the same whole-line snap and
    // leading-attribute-line glue as the segmenter, and honour the synthetic-block
    // contract. ----

    [Fact]
    public void BlockSourceSpan_SyntheticBlock_ReturnsZeroZero()
    {
        // A block with no source position (Span.Start < 0) — e.g. one inserted into the
        // AST rather than parsed — has no slice; the contract returns (0, 0).
        var synthetic = new ParagraphBlock(); // default Span is (0, -1) → Start defaults
        synthetic.Span = new Markdig.Syntax.SourceSpan(-1, -1);
        Assert.Equal((0, 0), BlockSegmenter.BlockSourceSpan("anything", synthetic));
    }

    [Fact]
    public void BlockSourceSpan_TableWithLeadingAttributeLine_IncludesThatLine()
    {
        // The {#id widths=…} line above a table is glued into the table's span so the
        // sliced fragment re-parses with its columns — the same glue the segmenter applies.
        var body = "Intro.\n\n{#my-table widths=\"40% 60%\"}\n| A | B |\n|---|---|\n| 1 | 2 |\n";
        var doc = Markdig.Markdown.Parse(body, Pipeline);
        var table = doc.Descendants<Markdig.Extensions.Tables.Table>().Single();

        var (start, end) = BlockSegmenter.BlockSourceSpan(body, table);
        var slice = body[start..end];
        Assert.StartsWith("{#my-table widths=\"40% 60%\"}", slice);
        Assert.Contains("| 1 | 2 |", slice);
        // Matches what the segmenter glues into the table block.
        var tableSegment = Segment(body).Single(s => s.BlockType == "table");
        Assert.Equal(tableSegment.Source, slice);
    }

    [Fact]
    public void BlockSourceSpan_FigureWithAttributeSuffix_KeepsTheSuffix()
    {
        // A standalone image's trailing {#id width=…} is on the same line, so the snap
        // keeps it; the slice round-trips to the same figure.
        var body = "# C\n\n![Hero](assets/hero.png){#hero width=480}\n";
        var doc = Markdig.Markdown.Parse(body, Pipeline);
        var figure = doc.Descendants<ParagraphBlock>()
            .Single(p => DocWizFiguresExtension.ImageOf(p) is not null);

        var (start, end) = BlockSegmenter.BlockSourceSpan(body, figure);
        Assert.Equal("![Hero](assets/hero.png){#hero width=480}", body[start..end]);
    }

    [Fact]
    public void BlockSourceSpan_DoesNotGlueAnAttributeLineThatBelongsToThePrecedingBlock()
    {
        // The leading-attribute-line glue must be floored at the END of the preceding block.
        // Here the paragraph's LAST source line is "{.note}" (an attribute-shaped line that
        // is part of the paragraph) and only a SINGLE newline separates it from the block
        // below. Without a floor the glue would step back over that one newline, see the
        // attribute-shaped line, and pull the PARAGRAPH's last line into the next block's
        // span — crossing the block boundary. The floor (previous block's end) prevents it,
        // so the sliced span starts at the block's own first line.
        var body = "Para one.\n{.note}\n```\ncode\n```\n";
        var doc = Markdig.Markdown.Parse(body, Pipeline);
        var fence = doc.Descendants<Markdig.Syntax.FencedCodeBlock>().Single();

        var (start, end) = BlockSegmenter.BlockSourceSpan(body, fence);
        var slice = body[start..end];

        Assert.DoesNotContain("{.note}", slice);
        Assert.DoesNotContain("Para one.", slice);
        Assert.StartsWith("```", slice);
        // And the span agrees with the segmenter's own block (the same floor applies there,
        // so BlockSourceSpan and the segmenter never disagree about where a block starts).
        var fenceSegment = Segment(body).Single(s => s.BlockType == "code");
        Assert.Equal(fenceSegment.Source, slice);
    }

    // ---- splice: the write side of fidelity-by-construction ----

    [Fact]
    public void Splice_CopyEverything_IsIdentity()
    {
        var body = "# T\n\nPara.\n\n| A |\n|---|\n| 1 |\n";
        var ops = Segment(body).Select(s => (SpliceOp)new SpliceOp.Copy(s.Start, s.End));
        Assert.Equal(body, BlockSegmenter.Splice(body, ops));
    }

    [Fact]
    public void Splice_ReplaceOneBlock_LeavesOriginalBytesElsewhere()
    {
        var body = "# T\n\nOld paragraph with \\#escape kept elsewhere.\n\nLast.\n";
        var segments = Segment(body);
        var ops = new List<SpliceOp>();
        foreach (var s in segments)
        {
            if (s.BlockType == "paragraph" && s.Source.Contains("Old paragraph"))
                ops.Add(new SpliceOp.Text("New paragraph."));
            else
                ops.Add(new SpliceOp.Copy(s.Start, s.End));
        }
        var result = BlockSegmenter.Splice(body, ops);
        Assert.Equal("# T\n\nNew paragraph.\n\nLast.\n", result);
    }

    [Fact]
    public void Splice_MoveBlocks_PreservesOriginalBytes()
    {
        var body = "First para.\n\nSecond para.\n";
        var segments = Segment(body);
        var blocks = segments.Where(s => s.Kind == SegmentKind.Block).ToList();
        // The gap BETWEEN the two paragraphs (not the trailing-newline gap).
        var gap = segments.Single(s => s.Kind == SegmentKind.Gap && s.Start == blocks[0].End);
        var ops = new SpliceOp[]
        {
            new SpliceOp.Copy(blocks[1].Start, blocks[1].End),
            new SpliceOp.Copy(gap.Start, gap.End),
            new SpliceOp.Copy(blocks[0].Start, blocks[0].End),
            new SpliceOp.Text("\n"),
        };
        Assert.Equal("Second para.\n\nFirst para.\n", BlockSegmenter.Splice(body, ops));
    }

    [Fact]
    public void Splice_CopyOutOfBounds_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BlockSegmenter.Splice("abc", [new SpliceOp.Copy(1, 99)]));
    }

    // ---- per-block rendering: the editing surface shows pipeline output per block,
    // with figure/table numbering continuing across blocks like on the built page ----

    [Fact]
    public void SegmentAndRender_NumberingContinuesAcrossBlocks()
    {
        var body =
            "| A |\n|---|\n| 1 |\n" +
            "\n" +
            "Some text between.\n" +
            "\n" +
            "| B |\n|---|\n| 2 |\n" +
            "\n" +
            "![first](assets/a.png)\n" +
            "\n" +
            "![second](assets/b.png)\n";
        var rendered = BlockSegmenter.SegmentAndRender(body, Pipeline);
        var html = rendered.Where(r => r.Segment.Kind == SegmentKind.Block).Select(r => r.Html!).ToList();

        Assert.Contains("Table 1", html[0]);
        Assert.Contains("Table 2", html[2]);
        Assert.Contains("Figure 1 — first", html[3]);
        Assert.Contains("Figure 2 — second", html[4]);
    }

    [Fact]
    public void SegmentAndRender_AlignsWithSegment_GapsHaveNoHtml()
    {
        var body = "# T\n\nPara.\n\n::: note\nHi.\n:::\n";
        var rendered = BlockSegmenter.SegmentAndRender(body, Pipeline);

        Assert.Equal(Segment(body), rendered.Select(r => r.Segment).ToList());
        Assert.All(rendered, r => Assert.Equal(r.Segment.Kind == SegmentKind.Block, r.Html is not null));
        var container = rendered.Single(r => r.Segment.BlockType == "container");
        Assert.Contains("dw-callout dw-note", container.Html);
    }

    [Fact]
    public void SegmentAndRender_Classification_IsImmuneToMutation()
    {
        // The editor's URL rewrite replaces a missing image with a placeholder span
        // (removing the LinkInline). The block must STILL classify as figure — kinds
        // derive from the source, not from surface rewrites — or the placeholder
        // loses its pipeline markup in the editor.
        var body = "![gone](../../assets/missing.png)\n";
        var rendered = BlockSegmenter.SegmentAndRender(body, Pipeline, doc =>
        {
            foreach (var link in doc.Descendants().OfType<Markdig.Syntax.Inlines.LinkInline>().ToList())
            {
                link.InsertBefore(new Markdig.Syntax.Inlines.HtmlInline("<span class=\"img-placeholder\">x</span>"));
                link.Remove();
            }
        });
        var block = rendered.Single(r => r.Segment.Kind == SegmentKind.Block);
        Assert.Equal("figure", block.Segment.BlockType);
        Assert.Contains("img-placeholder", block.Html);
    }

    [Fact]
    public void SegmentAndRender_MutateHook_RunsBeforeRendering()
    {
        var body = "![pic](../../assets/p.png)\n\nSee [other](dw:other-chapter).\n";
        var rendered = BlockSegmenter.SegmentAndRender(body, Pipeline, doc =>
        {
            foreach (var link in doc.Descendants().OfType<Markdig.Syntax.Inlines.LinkInline>())
                if (link.IsImage)
                    link.Url = "/assets/p.png";
        });
        var html = string.Concat(rendered.Where(r => r.Html is not null).Select(r => r.Html));
        Assert.Contains("src=\"/assets/p.png\"", html);
        Assert.Contains("href=\"dw:other-chapter\"", html); // untouched by the hook
    }

    /// <summary>The repo's own sample project must segment loss-free — the in-repo
    /// slice of the corpus gate (the full corpus run happens via `docwiz fidelity`).</summary>
    [Fact]
    public void Corpus_DemoProject_PartitionsEveryChapterLossFree()
    {
        var root = FindRepoRoot();
        var content = Path.Combine(root, "samples", "stockroom-demo", "content");
        var chapters = Directory.GetFiles(content, "*.md", SearchOption.AllDirectories);
        Assert.NotEmpty(chapters);
        foreach (var file in chapters)
        {
            var text = File.ReadAllText(file);
            var (_, body) = DocWiz.Core.Loading.FrontmatterParser.Split(text);
            AssertPartition(body);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "DocWiz.sln")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return dir!;
    }
}
