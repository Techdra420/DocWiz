using DocWiz.App;
using DocWiz.Core.Loading;
using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>
/// M3: table/figure segments carry structured meta so the grid editor and the image
/// tools manipulate REAL fields (rows, aligns, widths, alt, width) — never markdown
/// text in the browser. Row source lines enable W3 per-row emission.
/// </summary>
public class EditorDocMetaTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "docwiz-meta-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private EditorDocDto Build(string body)
    {
        Directory.CreateDirectory(Path.Combine(_root, "content", "010-overview"));
        File.WriteAllText(Path.Combine(_root, "docwiz.yaml"),
            """
            project: T
            code: T-1
            audiences:
              - id: operator
                title: Operator
            """);
        File.WriteAllText(Path.Combine(_root, "content", "010-overview", "010-c.md"),
            """
            ---
            id: c1
            title: Intro
            audiences: [operator]
            ---
            """ + "\n# Intro\n\n" + body);
        var project = ProjectLoader.Load(_root);
        return EditorDoc.Build(project, "content/010-overview/010-c.md");
    }

    [Fact]
    public void TableMeta_CarriesRowsAlignsWidthsAndId()
    {
        var dto = Build(
            "{#sizes widths=\"30% 70%\"}\n" +
            "| Name | Value |\n" +
            "|:-----|------:|\n" +
            "| a | 1 |\n" +
            "| b | 2 |\n");

        var seg = dto.Segments.Single(s => s.BlockType == "table");
        var meta = Assert.IsType<TableMetaDto>(seg.Meta);
        Assert.Equal("{#sizes widths=\"30% 70%\"}", meta.AttrLine);
        Assert.Equal("| Name | Value |", meta.HeaderLine);
        Assert.Equal("|:-----|------:|", meta.DelimiterLine);
        Assert.Equal(["| a | 1 |", "| b | 2 |"], meta.RowLines);
        Assert.Equal(["left", "right"], meta.Aligns);
        Assert.Equal("30% 70%", meta.Widths);
        Assert.Equal("sizes", meta.ExplicitId);
        Assert.Equal(1, meta.Number);
    }

    [Fact]
    public void TableMeta_NumbersCountPerChapter()
    {
        var dto = Build(
            "| A |\n|---|\n| 1 |\n" +
            "\n" +
            "text\n" +
            "\n" +
            "| B |\n|---|\n| 2 |\n");
        var tables = dto.Segments.Where(s => s.BlockType == "table").ToList();
        Assert.Equal(1, ((TableMetaDto)tables[0].Meta!).Number);
        Assert.Equal(2, ((TableMetaDto)tables[1].Meta!).Number);
        Assert.Null(((TableMetaDto)tables[0].Meta!).AttrLine);
    }

    [Fact]
    public void FigureMeta_CarriesSrcAltWidthAlign()
    {
        var dto = Build("![Cell layout](../../assets/x.png){width=640 .img-center}\n");

        var seg = dto.Segments.Single(s => s.BlockType == "figure");
        var meta = Assert.IsType<FigureMetaDto>(seg.Meta);
        Assert.Equal("../../assets/x.png", meta.Src);
        Assert.Equal("Cell layout", meta.Alt);
        Assert.Equal("640", meta.Width);
        Assert.Equal("center", meta.Align);
        Assert.Equal(1, meta.Number);
    }

    [Fact]
    public void FigureMeta_RecognisesLeftAlign()
    {
        // {.img-left} is an EXPLICIT left alignment (the default no-class figure is
        // centered by the flex column, so "left" needs its own class to escape that).
        var dto = Build("![Layout](../../assets/x.png){.img-left}\n");

        var seg = dto.Segments.Single(s => s.BlockType == "figure");
        var meta = Assert.IsType<FigureMetaDto>(seg.Meta);
        Assert.Equal("left", meta.Align);
    }
}
