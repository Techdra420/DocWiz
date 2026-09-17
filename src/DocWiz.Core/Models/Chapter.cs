namespace DocWiz.Core.Models;

/// <summary>One Markdown chapter under content/. Frontmatter fields plus location metadata.</summary>
public sealed class Chapter
{
    /// <summary>Stable ID (ADR-005) — immutable after creation; links and views reference it.</summary>
    public required string Id { get; init; }

    public required string Title { get; init; }
    public string Discipline { get; init; } = "";
    public List<string> Audiences { get; init; } = [];

    /// <summary>Absolute path of the source .md file.</summary>
    public required string FilePath { get; init; }

    /// <summary>Section folder slug with numeric prefix stripped, e.g. "overview".</summary>
    public required string SectionSlug { get; init; }

    /// <summary>Display title of the section, e.g. "Overview".</summary>
    public required string SectionTitle { get; init; }

    /// <summary>Sort key combining section and file numeric prefixes.</summary>
    public required string SortKey { get; init; }

    /// <summary>Markdown body (frontmatter removed).</summary>
    public required string Markdown { get; init; }

    /// <summary>Heading anchor IDs available in this chapter (filled after parsing).</summary>
    public List<string> Anchors { get; } = [];

    /// <summary>Parsed Markdown AST (set by DocWizMarkdown.ParseAll; one parse per build).</summary>
    public Markdig.Syntax.MarkdownDocument? Parsed { get; set; }
}

/// <summary>Frontmatter shape of a chapter file (YAML between leading --- fences).</summary>
public sealed class ChapterFrontmatter
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public string? Discipline { get; set; }
    public List<string> Audiences { get; set; } = [];
}
