namespace DocWiz.Core.Markdown;

/// <summary>
/// A chapter's title IS its H1 (Stage 6 decision): the heading on the page and the
/// frontmatter <c>title</c> are one concept. The editor's save derives the title from the
/// H1; renaming a chapter rewrites the H1. Text-level (via <see cref="HeadingScanner"/>, code
/// fences skipped) so the rest of the body keeps its exact bytes.
/// </summary>
public static class ChapterTitle
{
    /// <summary>The plain text of the body's first H1, or null when it has none. Inline
    /// markdown (emphasis, code, links) is reduced to its text.</summary>
    public static string? FromBody(string body)
    {
        var h1 = HeadingScanner.Scan(body).FirstOrDefault(h => h.Level == 1);
        if (h1 is null)
            return null;
        // Parsed as a heading, so a title like "1. Intro" isn't read as a list item.
        var text = Markdig.Markdown.ToPlainText("# " + h1.Text, DocWizMarkdown.CreatePipeline()).Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>The body with its first H1's text replaced by <paramref name="title"/> (an
    /// explicit <c>{#anchor}</c> on it is kept). A body without an H1 is returned unchanged.
    /// Newlines are normalized to LF — the editor's save writes LF throughout.</summary>
    public static string SetH1(string body, string title)
    {
        var h1 = HeadingScanner.Scan(body).FirstOrDefault(h => h.Level == 1);
        if (h1 is null)
            return body;
        var lines = body.Replace("\r\n", "\n").Split('\n');
        lines[h1.Line] = "# " + title + (h1.Anchor is null ? "" : " {#" + h1.Anchor + "}");
        return string.Join("\n", lines);
    }
}
