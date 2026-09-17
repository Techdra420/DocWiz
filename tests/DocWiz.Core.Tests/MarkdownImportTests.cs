using DocWiz.Core.Loading;
using Xunit;

namespace DocWiz.Core.Tests;

public class MarkdownImportTests
{
    [Fact]
    public void SplitsAtH1_OneChapterPerHeading()
    {
        var result = MarkdownImport.Convert(
            "# Introduction\n\nIntro text.\n\n# Mechanical Design\n\nFrame text.\n", "manual");

        Assert.Equal(2, result.Chapters.Count);
        Assert.Equal("Introduction", result.Chapters[0].Title);
        Assert.Equal("introduction", result.Chapters[0].Slug);
        Assert.Contains("Intro text.", result.Chapters[0].Body);
        Assert.Equal("Mechanical Design", result.Chapters[1].Title);
        Assert.Contains("Frame text.", result.Chapters[1].Body);
        Assert.Empty(result.Report);
    }

    [Fact]
    public void NoH1_FallsBackToSingleChapterWithFileName()
    {
        var result = MarkdownImport.Convert("## Frame\n\nText.\n", "10042 Installation Manual");

        var chapter = Assert.Single(result.Chapters);
        Assert.Equal("10042 Installation Manual", chapter.Title);
        Assert.StartsWith("# 10042 Installation Manual", chapter.Body);
    }

    [Fact]
    public void PreambleBeforeFirstH1_BecomesReviewChapter()
    {
        var result = MarkdownImport.Convert("Title page junk\n\n# Real Chapter\n\nText.\n", "manual");

        Assert.Equal(2, result.Chapters.Count);
        Assert.Equal("Front matter (review)", result.Chapters[0].Title);
        Assert.Contains("Title page junk", result.Chapters[0].Body);
        Assert.Contains(result.Report, r => r.Contains("front-matter"));
    }

    [Fact]
    public void TypedNumbers_StrippedFromTitlesAndHeadings()
    {
        var result = MarkdownImport.Convert(
            "# 1 Brief Introduction\n\n## 1.1 Modification\n\nText.\n\n## 1.2 Responsibilities\n\nMore.\n",
            "manual");

        var chapter = Assert.Single(result.Chapters);
        Assert.Equal("Brief Introduction", chapter.Title);
        Assert.Contains("## Modification {#modification}", chapter.Body);
        Assert.Contains("## Responsibilities {#responsibilities}", chapter.Body);
        Assert.DoesNotContain("1.1", chapter.Body);
    }

    [Fact]
    public void Headings_GetUniqueAnchors()
    {
        var result = MarkdownImport.Convert(
            "# C\n\n## Maintenance\n\na\n\n## Maintenance\n\nb\n", "manual");

        var body = result.Chapters[0].Body;
        Assert.Contains("## Maintenance {#maintenance}", body);
        Assert.Contains("## Maintenance {#maintenance-2}", body);
    }

    [Fact]
    public void CalloutParagraphs_BecomeElements()
    {
        var result = MarkdownImport.Convert(
            "# C\n\n**WARNING:** Never bypass the light curtain.\nRisk of injury.\n\nNormal text.\n" +
            "\nNOTE: Check backups weekly.\n",
            "manual");

        var body = result.Chapters[0].Body;
        Assert.Contains("::: warning\nNever bypass the light curtain.\nRisk of injury.\n:::", body);
        Assert.Contains("::: note\nCheck backups weekly.\n:::", body);
        Assert.Contains("Normal text.", body);
    }

    [Fact]
    public void MediaLinks_RewrittenToAssetsAndCollected()
    {
        var result = MarkdownImport.Convert(
            "# Cell Layout\n\n![Layout render](./media/image1.png){width=\"6.3in\"}\n", "manual");

        var chapter = Assert.Single(result.Chapters);
        Assert.Contains("![Layout render](../../assets/cell-layout/image1.png)", chapter.Body);
        Assert.DoesNotContain("width=", chapter.Body);
        Assert.Equal(["image1.png"], chapter.MediaFiles);
    }

    [Fact]
    public void HtmlImgTags_ConvertedToMarkdownImages()
    {
        // Pandoc emits raw <img> whenever the docx image carries sizing — i.e. always.
        var result = MarkdownImport.Convert(
            "# Frame\n\n<img src=\"media/image1.png\" style=\"width:2in\" alt=\"Cell layout\" />\n",
            "manual");

        var chapter = Assert.Single(result.Chapters);
        Assert.Contains("![Cell layout](../../assets/frame/image1.png)", chapter.Body);
        Assert.DoesNotContain("<img", chapter.Body);
        Assert.Equal(["image1.png"], chapter.MediaFiles);
    }

    [Fact]
    public void HighlightSpans_StrippedFromTitlesAndBody()
    {
        // Word highlights arrive as <span class="mark">…</span> — found in a real
        // migrated user guide. They must never reach titles, slugs, or rendered text.
        var result = MarkdownImport.Convert(
            "# <span class=\"mark\">Explain</span>\n\nSee the <span class=\"mark\">marked</span> value.\n",
            "manual");

        var chapter = Assert.Single(result.Chapters);
        Assert.Equal("Explain", chapter.Title);
        Assert.Equal("explain", chapter.Slug);
        Assert.Contains("See the marked value.", chapter.Body);
        Assert.DoesNotContain("<span", chapter.Body);
    }

    [Fact]
    public void EmptyHeading_GetsPlaceholderTitle()
    {
        var result = MarkdownImport.Convert("# \n\nText.\n", "manual");

        var chapter = Assert.Single(result.Chapters);
        Assert.Equal("Untitled section", chapter.Title);
    }

    [Fact]
    public void EmfImages_AreReported()
    {
        var result = MarkdownImport.Convert("# C\n\n![](./media/image2.emf)\n", "manual");

        Assert.Contains(result.Report, r => r.Contains("image2.emf") && r.Contains("PNG"));
    }

    [Fact]
    public void HtmlTable_WithRowspan_ConvertedByRepeatingValues()
    {
        // The migrated alarm-list pattern: ID + warning text span two rows, the last
        // column splits into root cause (R:) and measure (M:).
        var html = "<table>\n<thead>\n<tr>\n<th>ID</th>\n<th>Text</th>\n<th>Cause</th>\n</tr>\n</thead>\n" +
                   "<tbody>\n<tr>\n<th rowspan=\"2\">1</th>\n<td rowspan=\"2\">Warning - reserve</td>\n<td>R: -</td>\n</tr>\n" +
                   "<tr>\n<td>M: -</td>\n</tr>\n</tbody>\n</table>";
        var result = MarkdownImport.Convert("# Diagnostics\n\n" + html + "\n", "manual");

        var body = result.Chapters[0].Body;
        Assert.Contains("| ID | Text | Cause |", body);
        Assert.Contains("| 1 | Warning - reserve | R: - |", body);
        Assert.Contains("| 1 | Warning - reserve | M: - |", body);
        Assert.DoesNotContain("<table", body);
        Assert.Contains(result.Report, r => r.Contains("auto-converted"));
    }

    [Fact]
    public void HtmlTable_CellMarkupAndEntities_Cleaned()
    {
        var html = "<table><tr><th>K</th><th>V</th></tr>" +
                   "<tr><td><p>Multi</p><p>paragraph</p></td><td>A &amp; B | piped</td></tr></table>";
        var result = MarkdownImport.Convert("# C\n\n" + html + "\n", "manual");

        var body = result.Chapters[0].Body;
        Assert.Contains("| Multi paragraph | A & B \\| piped |", body);
    }

    [Fact]
    public void NestedHtmlTable_StaysRawAndFlagged()
    {
        var html = "<table><tr><td><table><tr><td>inner</td></tr></table></td></tr></table>";
        var result = MarkdownImport.Convert("# C\n\n" + html + "\n", "manual");

        Assert.Contains("<table>", result.Chapters[0].Body);
        Assert.Contains(result.Report, r => r.Contains("too complex"));
    }
}
