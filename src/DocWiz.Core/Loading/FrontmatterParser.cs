namespace DocWiz.Core.Loading;

/// <summary>Splits a Markdown file into its YAML frontmatter block and body.</summary>
public static class FrontmatterParser
{
    /// <summary>
    /// Returns (yaml, body). yaml is null when the file has no leading --- fence.
    /// The fence lines themselves are not included in either part.
    /// </summary>
    public static (string? Yaml, string Body) Split(string text)
    {
        var normalized = text.Replace("\r\n", "\n");
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
            return (null, text);

        var end = normalized.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (end < 0)
            return (null, text);

        var yaml = normalized[4..end];
        var bodyStart = normalized.IndexOf('\n', end + 1);
        var body = bodyStart < 0 ? "" : normalized[(bodyStart + 1)..];
        return (yaml, body);
    }
}
