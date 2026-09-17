using System.Text.RegularExpressions;
using DocWiz.App;
using DocWiz.Core.Loading;
using DocWiz.Core.Markdown;
using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>
/// ADR-008 parity gate (audit F5): figure/table numbering has multiple authorities that MUST
/// agree — the anchor-id counter (<c>DocWizFigures.AssignIds</c>), the caption renderers, and
/// the editor's per-segment meta counter (<c>EditorDoc.Build</c>). They currently agree only
/// because each independently repeats the same "skip images inside table cells" rule; this
/// golden test fails if any one drifts. The client <c>renumberPlugin</c> renders
/// <c>meta.Number</c> verbatim, so pinning the meta keeps the live surface honest too.
/// </summary>
public class NumberingParityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "docwiz-numparity-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void FigureAndTableNumbers_AgreeAcrossIds_Captions_AndEditorMeta()
    {
        // Two standalone figures and three tables, interleaved; the middle table holds an
        // INLINE image in a cell, which must NOT be numbered as a figure by ANY authority.
        const string body =
            "# Intro\n\n" +
            "![First figure](../../assets/a.png)\n\n" +
            "| H1 |\n|----|\n| t1 |\n\n" +
            "Some prose.\n\n" +
            "![Second figure](../../assets/b.png)\n\n" +
            "| Has a cell image |\n|------------------|\n| ![inline](../../assets/c.png) |\n\n" +
            "| H3 |\n|----|\n| t3 |\n";

        // Authority A + B: anchor ids and rendered captions, from one pipeline pass.
        var pipeline = DocWizMarkdown.CreatePipeline();
        var doc = Markdig.Markdown.Parse(body, pipeline);
        var html = DocWizMarkdown.RenderHtml(doc, pipeline);

        var figureIds = Nums(html, @"id=""figure-(\d+)""");
        var figureCaptions = Nums(html, @"<figcaption>Figure (\d+)");
        var tableIds = Nums(html, @"id=""table-(\d+)""");
        var tableCaptions = Nums(html, @"<figcaption>Table (\d+)");

        // Authority C: the editor's per-segment meta numbers.
        var dto = BuildEditorDoc(body);
        var figureMeta = dto.Segments.Where(s => s.BlockType == "figure")
            .Select(s => ((FigureMetaDto)s.Meta!).Number).ToList();
        var tableMeta = dto.Segments.Where(s => s.BlockType == "table")
            .Select(s => ((TableMetaDto)s.Meta!).Number).ToList();

        // Exactly two figures (the cell image is not one) and three tables — and every
        // authority produces the same sequence.
        Assert.Equal(new[] { 1, 2 }, figureIds);
        Assert.Equal(figureIds, figureCaptions);
        Assert.Equal(figureIds, figureMeta);

        Assert.Equal(new[] { 1, 2, 3 }, tableIds);
        Assert.Equal(tableIds, tableCaptions);
        Assert.Equal(tableIds, tableMeta);
    }

    private EditorDocDto BuildEditorDoc(string bodyWithH1)
    {
        Directory.CreateDirectory(Path.Combine(_root, "content", "010-overview"));
        File.WriteAllText(Path.Combine(_root, "docwiz.yaml"),
            "project: T\ncode: T-1\naudiences:\n  - id: operator\n    title: Operator\n");
        File.WriteAllText(Path.Combine(_root, "content", "010-overview", "010-c.md"),
            "---\nid: c1\ntitle: Intro\naudiences: [operator]\n---\n\n" + bodyWithH1);
        var project = ProjectLoader.Load(_root);
        return EditorDoc.Build(project, "content/010-overview/010-c.md");
    }

    private static List<int> Nums(string html, string pattern) =>
        Regex.Matches(html, pattern).Select(m => int.Parse(m.Groups[1].Value)).ToList();
}
