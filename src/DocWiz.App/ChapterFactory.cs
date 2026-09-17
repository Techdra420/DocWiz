using System.Text.RegularExpressions;
using DocWiz.Core.Loading;
using DocWiz.Core.Markdown;
using DocWiz.Core.Models;

namespace DocWiz.App;

/// <summary>Creates new chapter files from per-discipline templates (Stage 3 roadmap:
/// templates guide the author through the expected structure).</summary>
public static partial class ChapterFactory
{
    /// <summary>Returns the absolute path of the created file.</summary>
    public static string Create(string projectRoot, NewChapterDto dto)
    {
        var contentDir = Path.Combine(projectRoot, ProjectLayout.ContentFolder);
        Directory.CreateDirectory(contentDir);

        var sectionDir = ResolveSectionDir(contentDir, dto.Section);
        Directory.CreateDirectory(sectionDir);

        var fileName = $"{NextPrefix(Directory.GetFiles(sectionDir, "*.md"))}-{Slugify(dto.Slug)}.md";
        var path = Path.Combine(sectionDir, fileName);

        var audiences = dto.Audiences.Count > 0 ? dto.Audiences : ["user", "support", "admin"];
        File.WriteAllText(path,
            ChapterTemplates.ChapterFile(dto.Id, dto.Title, dto.Discipline, audiences));
        return path;
    }

    /// <summary>Writes an imported chapter: same naming/frontmatter rules as Create,
    /// but with the converted body instead of a discipline skeleton (the body
    /// already starts with its H1). Returns the absolute path.</summary>
    public static string CreateImported(string projectRoot, string section, string id, string title,
        string discipline, IEnumerable<string> audiences, string body)
    {
        var contentDir = Path.Combine(projectRoot, ProjectLayout.ContentFolder);
        Directory.CreateDirectory(contentDir);
        var sectionDir = ResolveSectionDir(contentDir, section);
        Directory.CreateDirectory(sectionDir);

        var fileName = $"{NextPrefix(Directory.GetFiles(sectionDir, "*.md"))}-{Slugify(title)}.md";
        var path = Path.Combine(sectionDir, fileName);
        File.WriteAllText(path,
            ChapterComposer.Compose(
                ChapterComposer.Frontmatter(id, title, discipline, audiences), body));
        return path;
    }

    private static string ResolveSectionDir(string contentDir, string section)
    {
        var existing = Path.Combine(contentDir, section);
        if (Directory.Exists(existing))
            return existing;
        // "080-safety" already carries an order prefix — honor it as-is; otherwise
        // assign the next free prefix per the CONTENT_MODEL ordering convention.
        if (PrefixedNameRegex().IsMatch(section))
            return existing;
        return Path.Combine(contentDir, $"{NextPrefix(Directory.GetDirectories(contentDir))}-{Slugify(section)}");
    }

    [GeneratedRegex(@"^\d+-")]
    private static partial Regex PrefixedNameRegex();

    /// <summary>Next numeric prefix in steps of 10 (CONTENT_MODEL ordering convention).</summary>
    private static string NextPrefix(string[] paths)
    {
        var max = paths
            .Select(p => Path.GetFileNameWithoutExtension(p).Split('-')[0])
            .Where(p => p.All(char.IsDigit) && p.Length > 0)
            .Select(int.Parse)
            .DefaultIfEmpty(0)
            .Max();
        return (max + 10).ToString("D3");
    }

    // One slug algorithm (ADR-008, audit F13): filenames use the SAME normalization as heading
    // anchors (HeadingScanner.Slug — never-empty, "section" fallback), so a title and its derived
    // filename slug can't disagree (e.g. on empty / symbol-only input).
    internal static string Slugify(string text) => HeadingScanner.Slug(text);
}
