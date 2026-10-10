using System.Text.RegularExpressions;
using DocWiz.Core.Loading;
using DocWiz.Core.Markdown;
using DocWiz.Core.Models;

namespace DocWiz.App;

/// <summary>Creates new chapter files from per-discipline templates (Stage 3 roadmap:
/// templates guide the author through the expected structure). Names on disk follow the
/// Stage 6 rule (<see cref="ContentNames"/>): the file is named after the chapter title and a
/// new section's folder after the section name.</summary>
public static partial class ChapterFactory
{
    /// <summary>Returns the absolute path of the created file. <paramref name="projectAudiences"/>
    /// (the manifest's audience ids) are the default when the request names none.</summary>
    public static string Create(string projectRoot, NewChapterDto dto, IEnumerable<string> projectAudiences)
    {
        var contentDir = Path.Combine(projectRoot, ProjectLayout.ContentFolder);
        Directory.CreateDirectory(contentDir);

        var sectionDir = ResolveSectionDir(contentDir, dto.Section);
        Directory.CreateDirectory(sectionDir);
        var path = NewChapterPath(sectionDir, dto.Title);

        // No explicit choice → every audience the project defines (never a hardcoded set the
        // project may not have, which would fail Check on every new chapter).
        var audiences = dto.Audiences.Count > 0 ? dto.Audiences : projectAudiences.ToList();
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

        var path = NewChapterPath(sectionDir, title);
        File.WriteAllText(path,
            ChapterComposer.Compose(
                ChapterComposer.Frontmatter(id, title, discipline, audiences), body));
        return path;
    }

    /// <summary>A free id for a new chapter, derived from its title (ids are invisible to
    /// authors and never change — ADR-005): the slug, then slug-2, slug-3…</summary>
    public static string UniqueId(DocProject project, string title)
    {
        var baseId = Slugify(title);
        var id = baseId;
        for (var n = 2; project.FindChapter(id) is not null; n++)
            id = $"{baseId}-{n}";
        return id;
    }

    /// <summary>"{next prefix}-{title}.md" in the section, never overwriting a file.</summary>
    private static string NewChapterPath(string sectionDir, string title)
    {
        var stem = $"{ContentOrganizer.NextPrefix(Directory.GetFiles(sectionDir, "*.md"))}-{ContentNames.FileSafe(title)}";
        var path = Path.Combine(sectionDir, stem + ".md");
        for (var n = 2; File.Exists(path); n++)
            path = Path.Combine(sectionDir, $"{stem} ({n}).md");
        return path;
    }

    /// <summary>The section folder for a request: an existing folder name (from the section
    /// picker), a section that already shows the typed name, or a new folder
    /// "{next prefix}-{name}". "080-safety" (an explicit order prefix) is honored as-is.</summary>
    private static string ResolveSectionDir(string contentDir, string section)
    {
        if (section.IndexOfAny(['/', '\\']) >= 0 || section.Trim() is "" or "." or "..")
            throw new DocWizLoadException("Not a section name.");
        var existing = Path.Combine(contentDir, section);
        if (Directory.Exists(existing))
            return existing;

        var shown = Directory.GetDirectories(contentDir).FirstOrDefault(d => string.Equals(
            ContentNames.SectionTitle(Path.GetFileName(d)), section.Trim(), StringComparison.OrdinalIgnoreCase));
        if (shown is not null)
            return shown;

        var (prefix, rest) = ProjectLoader.SplitNumericPrefix(section.Trim());
        if (ContentNames.SectionNameProblem(rest) is { } problem)
            throw new DocWizLoadException(problem);
        return PrefixedNameRegex().IsMatch(section.Trim()) && prefix.Length > 0
            ? Path.Combine(contentDir, section.Trim())
            : Path.Combine(contentDir, $"{ContentOrganizer.NextPrefix(Directory.GetDirectories(contentDir))}-{section.Trim()}");
    }

    [GeneratedRegex(@"^\d+-")]
    private static partial Regex PrefixedNameRegex();

    // One slug algorithm (ADR-008, audit F13): chapter ids use the SAME normalization as heading
    // anchors (HeadingScanner.Slug — never-empty, "section" fallback). File and folder names
    // follow the display name instead (ContentNames, Stage 6).
    internal static string Slugify(string text) => HeadingScanner.Slug(text);
}
