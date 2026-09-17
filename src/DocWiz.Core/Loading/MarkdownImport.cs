using System.Text;
using System.Text.RegularExpressions;
using DocWiz.Core.Markdown;

namespace DocWiz.Core.Loading;

/// <summary>One chapter produced by an import. MediaFiles are the file names
/// (as found under the pandoc extract folder's media/) that the caller must copy
/// into assets/&lt;Slug&gt;/ — the body already links them there.</summary>
public sealed record ImportedChapter(string Slug, string Title, string Body, List<string> MediaFiles);

public sealed record ImportResult(List<ImportedChapter> Chapters, List<string> Report);

/// <summary>
/// Turns pandoc's raw docx→GFM output into DocWiz chapters: splits at H1
/// boundaries, strips typed numbering prefixes (Word migration leftovers that
/// would double-number — same rule the validator warns about), adds stable
/// heading anchors, converts NOTE/WARNING/… paragraphs into callout elements,
/// reroutes extracted media into assets/, and reports everything that needs a
/// human (HTML tables, Word vector graphics) instead of faking a conversion.
/// Pure text transforms — pandoc itself is orchestrated by the CLI.
/// </summary>
public static partial class MarkdownImport
{
    public static ImportResult Convert(string pandocMarkdown, string fallbackTitle)
    {
        var report = new List<string>();
        var text = pandocMarkdown.Replace("\r\n", "\n");
        var lines = text.Split('\n');
        var headings = HeadingScanner.Scan(text);

        var h1Lines = headings.Where(h => h.Level == 1).Select(h => h.Line).ToList();
        var chapters = new List<ImportedChapter>();

        if (h1Lines.Count == 0)
        {
            // The fallback is a FILE name ("10042 Installation Manual") — leading digits
            // there are project codes, not heading numbering; don't strip them.
            chapters.Add(BuildChapter(fallbackTitle.Trim(), lines, 0, lines.Length, hasH1Line: false, report));
            return new ImportResult(chapters, report);
        }

        if (lines.Take(h1Lines[0]).Any(l => l.Trim().Length > 0))
        {
            // Word title pages and tables of contents convert into a preamble before
            // the first real heading. Kept (never silently dropped), but quarantined.
            report.Add("front-matter: content before the first Heading 1 (title page / table of " +
                       "contents?) was kept as its own chapter — review and usually delete it.");
            chapters.Add(BuildChapter("Front matter (review)", lines, 0, h1Lines[0], hasH1Line: false, report));
        }

        for (var i = 0; i < h1Lines.Count; i++)
        {
            var start = h1Lines[i];
            var end = i + 1 < h1Lines.Count ? h1Lines[i + 1] : lines.Length;
            var title = CleanTitle(HeadingTextOf(lines[start]));
            chapters.Add(BuildChapter(title, lines, start + 1, end, hasH1Line: true, report));
        }

        return new ImportResult(chapters, report);
    }

    private static ImportedChapter BuildChapter(
        string title, string[] lines, int start, int end, bool hasH1Line, List<string> report)
    {
        var slug = HeadingScanner.Slug(title);
        var media = new List<string>();
        var takenAnchors = new HashSet<string>(StringComparer.Ordinal);
        var body = new StringBuilder();
        body.Append("# ").Append(title).Append('\n').Append('\n');

        var inFence = false;
        var i = start;
        while (i < end)
        {
            var line = lines[i];

            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                body.Append(line).Append('\n');
                i++;
                continue;
            }
            if (inFence)
            {
                body.Append(line).Append('\n');
                i++;
                continue;
            }

            var headingMatch = HeadingLine().Match(line);
            if (headingMatch.Success)
            {
                body.Append(RewriteHeading(headingMatch, takenAnchors)).Append('\n');
                i++;
                continue;
            }

            if (line.TrimStart().StartsWith("<table", StringComparison.OrdinalIgnoreCase))
            {
                // Pandoc emits raw HTML for tables with merged cells (alarm lists,
                // technical data). The pipeline strips HTML, so the content would be
                // lost — convert: spanned cells repeat into the rows/columns they
                // covered. Only truly unparseable tables stay raw and get flagged.
                var blockLines = new List<string>();
                while (i < end)
                {
                    blockLines.Add(lines[i]);
                    var done = lines[i].Contains("</table>", StringComparison.OrdinalIgnoreCase);
                    i++;
                    if (done)
                        break;
                }
                var converted = TryConvertHtmlTable(string.Join("\n", blockLines));
                if (converted is not null)
                {
                    body.Append(converted).Append('\n');
                    report.Add($"{slug}: an HTML table (merged cells) was auto-converted to a simple " +
                               "table — spanned values repeat per row; verify the layout.");
                }
                else
                {
                    foreach (var raw in blockLines)
                        body.Append(raw).Append('\n');
                    report.Add($"{slug}: a table was too complex to convert and came through as raw " +
                               "HTML — DocWiz strips HTML, so rebuild it as a simple table.");
                }
                continue;
            }

            var callout = CalloutLabel().Match(line);
            if (callout.Success)
            {
                // The whole paragraph (up to the next blank line) becomes the callout.
                var kind = callout.Groups["kind"].Value.ToUpperInvariant() switch
                {
                    "WARNING" or "CAUTION" => "warning",
                    "DANGER" => "danger",
                    "TIP" => "tip",
                    _ => "note",
                };
                body.Append("::: ").Append(kind).Append('\n');
                body.Append(ProcessInline(line[callout.Length..].TrimStart(), slug, media, report)).Append('\n');
                i++;
                while (i < end && lines[i].Trim().Length > 0)
                {
                    body.Append(ProcessInline(lines[i], slug, media, report)).Append('\n');
                    i++;
                }
                body.Append(":::").Append('\n');
                continue;
            }

            FlagUnconvertibles(line, slug, report);
            body.Append(ProcessInline(line, slug, media, report)).Append('\n');
            i++;
        }

        _ = hasH1Line; // the H1 is always re-emitted from the cleaned title
        return new ImportedChapter(slug, title, body.ToString().TrimEnd('\n') + "\n", media);
    }

    /// <summary>Heading line → typed number stripped, stable anchor added (H2+),
    /// mirroring the editor's auto-anchor behavior so sections are immediately
    /// linkable from views.</summary>
    private static string RewriteHeading(Match m, HashSet<string> taken)
    {
        var hashes = m.Groups[1].Value;
        var clean = CleanTitle(m.Groups[2].Value);
        if (hashes.Length == 1)
            return $"{hashes} {clean}";
        var anchor = HeadingScanner.UniqueAnchor(HeadingScanner.Slug(clean), taken);
        taken.Add(anchor);
        return $"{hashes} {clean} {{#{anchor}}}";
    }

    /// <summary>Strips typed numbering ("1.2 Maintenance" → "Maintenance"), inline
    /// HTML pandoc emits for Word highlights (&lt;span class="mark"&gt;…&lt;/span&gt;), and
    /// any anchor carried over; numbering is automatic in DocWiz.</summary>
    internal static string CleanTitle(string headingText)
    {
        var t = HtmlTag().Replace(headingText, "");
        t = ExistingAnchor().Replace(t, "").Trim();
        t = TypedNumberPrefix().Replace(t, "").Trim();
        return t.Length == 0 ? "Untitled section" : t;
    }

    /// <summary>Rewrites pandoc-extracted media links (…/media/imageN.png) to
    /// ../../assets/&lt;slug&gt;/imageN.png, drops pandoc size attributes (inches/EMU
    /// are meaningless here — sizing is set in the editor), and records the file.</summary>
    private static string ProcessInline(string line, string slug, List<string> media, List<string> report)
    {
        // Pandoc emits raw <img> HTML whenever the docx image carries sizing it cannot
        // express in GFM — which is every image placed in Word. Convert to Markdown
        // first, then the media rewrite below applies to both forms.
        line = ImgTag().Replace(line, m => $"![{m.Groups["alt"].Value}]({m.Groups["url"].Value})");
        // Word highlight/underline runs arrive as <span class="mark">/<u> wrappers; the
        // pipeline disables raw HTML, so they would show as literal tag text. Unwrap.
        line = SpanTag().Replace(line, "");
        return ImageLink().Replace(line, m =>
        {
            var alt = m.Groups["alt"].Value;
            var url = m.Groups["url"].Value;
            var mediaIdx = url.LastIndexOf("media/", StringComparison.OrdinalIgnoreCase);
            if (mediaIdx < 0)
                return $"![{alt}]({url})"; // foreign link: keep, but without pandoc attrs
            var fileName = url[(mediaIdx + "media/".Length)..];
            if (!media.Contains(fileName))
                media.Add(fileName);
            if (fileName.EndsWith(".emf", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".wmf", StringComparison.OrdinalIgnoreCase))
                report.Add($"{slug}: image '{fileName}' is a Word vector graphic (EMF/WMF) — " +
                           "browsers cannot display it; export it as PNG and replace the link.");
            return $"![{alt}](../../assets/{slug}/{fileName})";
        });
    }

    private static void FlagUnconvertibles(string line, string slug, List<string> report)
    {
        if (line.Contains("<table", StringComparison.OrdinalIgnoreCase))
            report.Add($"{slug}: a table was too complex for Markdown (merged cells?) and came " +
                       "through as raw HTML — DocWiz strips HTML, so rebuild it as a simple table.");
    }

    /// <summary>HTML table → pipe table. Rowspan/colspan cells repeat their value into
    /// every covered position (faithful, if visually flatter). Returns null when the
    /// block has no parseable rows or contains a nested table.</summary>
    internal static string? TryConvertHtmlTable(string html)
    {
        // A nested table would interleave its rows with the outer one — bail out.
        if (CountOf(html, "<table") > 1)
            return null;

        var rows = new List<List<(string Text, int RowSpan, int ColSpan)>>();
        foreach (Match tr in TableRow().Matches(html))
        {
            var cells = new List<(string, int, int)>();
            foreach (Match cell in TableCell().Matches(tr.Groups["row"].Value))
            {
                var attrs = cell.Groups["attrs"].Value;
                var content = cell.Groups["content"].Value;
                content = content.Replace("</p>", " ").Replace("<br />", " ").Replace("<br/>", " ").Replace("<br>", " ");
                content = HtmlTag().Replace(content, "");
                content = System.Net.WebUtility.HtmlDecode(content);
                content = Whitespace().Replace(content, " ").Trim().Replace("|", "\\|");
                cells.Add((content, SpanOf(attrs, "rowspan"), SpanOf(attrs, "colspan")));
            }
            if (cells.Count > 0)
                rows.Add(cells);
        }
        if (rows.Count == 0)
            return null;

        // Resolve the span grid: place cells left-to-right, skipping positions still
        // covered by a rowspan from above; spanned values repeat into covered cells.
        var grid = new List<List<string?>>();
        for (var r = 0; r < rows.Count; r++)
        {
            while (grid.Count <= r)
                grid.Add([]);
            var col = 0;
            foreach (var (text, rowSpan, colSpan) in rows[r])
            {
                while (col < grid[r].Count && grid[r][col] is not null)
                    col++;
                for (var dr = 0; dr < rowSpan; dr++)
                {
                    while (grid.Count <= r + dr)
                        grid.Add([]);
                    for (var dc = 0; dc < colSpan; dc++)
                    {
                        var target = grid[r + dr];
                        while (target.Count <= col + dc)
                            target.Add(null);
                        target[col + dc] = text;
                    }
                }
                col += colSpan;
            }
        }

        var width = grid.Max(r => r.Count);
        if (width == 0)
            return null;
        var sb = new StringBuilder();
        for (var r = 0; r < grid.Count; r++)
        {
            sb.Append('|');
            for (var c = 0; c < width; c++)
                sb.Append(' ').Append(c < grid[r].Count ? grid[r][c] ?? "" : "").Append(" |");
            sb.Append('\n');
            if (r == 0)
            {
                sb.Append('|');
                for (var c = 0; c < width; c++)
                    sb.Append("---|");
                sb.Append('\n');
            }
        }
        return sb.ToString();
    }

    private static int SpanOf(string attrs, string name)
    {
        var m = Regex.Match(attrs, name + @"\s*=\s*""?(\d+)""?", RegexOptions.IgnoreCase);
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) && n > 0 ? n : 1;
    }

    private static int CountOf(string text, string token)
    {
        var count = 0;
        for (var idx = text.IndexOf(token, StringComparison.OrdinalIgnoreCase);
             idx >= 0;
             idx = text.IndexOf(token, idx + token.Length, StringComparison.OrdinalIgnoreCase))
            count++;
        return count;
    }

    private static string HeadingTextOf(string line) =>
        HeadingLine().Match(line) is { Success: true } m ? m.Groups[2].Value : line;

    [GeneratedRegex(@"^(#{1,6})\s+(.*)$")]
    private static partial Regex HeadingLine();

    /// <summary>Same idea as ProjectValidator.ManualNumberPrefix, anchored for stripping.</summary>
    [GeneratedRegex(@"^\d+(\.\d+)*[\s.]\s*")]
    private static partial Regex TypedNumberPrefix();

    [GeneratedRegex(@"\s*\{#[^}]+\}\s*$")]
    private static partial Regex ExistingAnchor();

    [GeneratedRegex(@"^(?<kind>NOTE|WARNING|CAUTION|DANGER|TIP)\b[:.]?\s*|^\*\*(?<kind>NOTE|WARNING|CAUTION|DANGER|TIP)\b[:.]?\*\*[:.]?\s*", RegexOptions.IgnoreCase)]
    private static partial Regex CalloutLabel();

    [GeneratedRegex(@"!\[(?<alt>[^\]]*)\]\((?<url>[^)\s]+)(?:\s+""[^""]*"")?\)(\{[^}]*\})?")]
    private static partial Regex ImageLink();

    [GeneratedRegex(@"<img\b[^>]*?src=""(?<url>[^""]+)""[^>]*?(?:alt=""(?<alt>[^""]*)""[^>]*?)?/?>", RegexOptions.IgnoreCase)]
    private static partial Regex ImgTag();

    [GeneratedRegex(@"</?(?:span|mark|u)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex SpanTag();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"<tr\b[^>]*>(?<row>.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TableRow();

    [GeneratedRegex(@"<(th|td)\b(?<attrs>[^>]*)>(?<content>.*?)</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TableCell();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
