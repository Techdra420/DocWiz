using System.Text.RegularExpressions;
using DocWiz.Core.Markdown;
using DocWiz.Core.Models;

namespace DocWiz.Core.Loading;

/// <summary>File-system reorganization of a project's content/ tree. Moving a chapter
/// or a section only renumbers the name prefixes (010-, 020-, …) — stable ids
/// (ADR-005) make this safe, because links and view includes reference ids, never
/// file paths.</summary>
public static class ContentOrganizer
{
    /// <summary>Moves a chapter one position up (-1) or down (+1) within its section.
    /// All files in the section are renumbered to the 010/020 convention in the new
    /// order. Returns the chapter's absolute path after the move (unchanged when the
    /// chapter is already at the edge).</summary>
    public static string MoveChapter(string chapterPath, int direction)
    {
        var full = Path.GetFullPath(chapterPath);
        var dir = Path.GetDirectoryName(full)
            ?? throw new DocWizLoadException($"'{chapterPath}' has no parent folder.");
        var ordered = Directory.GetFiles(dir, "*.md").OrderBy(f => f, StringComparer.Ordinal).ToList();
        var index = ordered.FindIndex(f =>
            string.Equals(Path.GetFullPath(f), full, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            throw new DocWizLoadException($"'{chapterPath}' is not a chapter file in '{dir}'.");

        var target = index + direction;
        if (target < 0 || target >= ordered.Count)
            return full;

        (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
        return Renumber(ordered, isDirectory: false)[target];
    }

    /// <summary>Moves a section folder one position up (-1) or down (+1) among the
    /// content/ folders, renumbering all of them. Returns the section's folder NAME
    /// after the move.</summary>
    public static string MoveSection(string root, string sectionFolder, int direction)
    {
        var contentDir = Path.Combine(Path.GetFullPath(root), ProjectLayout.ContentFolder);
        var full = Path.GetFullPath(Path.Combine(contentDir, sectionFolder));
        var ordered = Directory.GetDirectories(contentDir).OrderBy(d => d, StringComparer.Ordinal).ToList();
        var index = ordered.FindIndex(d =>
            string.Equals(Path.GetFullPath(d), full, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            throw new DocWizLoadException($"'{sectionFolder}' is not a section folder of this project.");

        var target = index + direction;
        if (target < 0 || target >= ordered.Count)
            return Path.GetFileName(full);

        (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
        return Path.GetFileName(Renumber(ordered, isDirectory: true)[target]);
    }

    /// <summary>Next order prefix in steps of 10 after the highest existing one ("010",
    /// "020", …) — for a new section folder or chapter file.</summary>
    public static string NextPrefix(IEnumerable<string> paths)
    {
        var max = paths
            .Select(p => ProjectLoader.SplitNumericPrefix(Path.GetFileNameWithoutExtension(p)).Order)
            .Where(o => o.Length > 0)
            .Select(int.Parse)
            .DefaultIfEmpty(0)
            .Max();
        return (max + 10).ToString("D3");
    }

    /// <summary>Creates an empty section "{next prefix}-{name}" and returns its folder name.
    /// Refused when the name can't be a folder name or another section already shows it.</summary>
    public static string CreateSection(string root, string name)
    {
        var contentDir = Path.Combine(Path.GetFullPath(root), ProjectLayout.ContentFolder);
        Directory.CreateDirectory(contentDir);
        var clean = CheckSectionName(contentDir, name, except: null);
        var folder = $"{NextPrefix(Directory.GetDirectories(contentDir))}-{clean}";
        Directory.CreateDirectory(Path.Combine(contentDir, folder));
        return folder;
    }

    /// <summary>Renames a section: its folder becomes "{same prefix}-{name}" — the folder
    /// name IS the section name. Returns the new folder name.</summary>
    public static string RenameSection(string root, string sectionFolder, string name)
    {
        var contentDir = Path.Combine(Path.GetFullPath(root), ProjectLayout.ContentFolder);
        var full = SectionDir(contentDir, sectionFolder);
        var clean = CheckSectionName(contentDir, name, except: full);
        var (prefix, _) = ProjectLoader.SplitNumericPrefix(Path.GetFileName(full));
        var target = prefix.Length > 0 ? $"{prefix}-{clean}" : clean;
        MoveEntry(full, Path.Combine(contentDir, target), isDirectory: true);
        return target;
    }

    /// <summary>Deletes an empty section folder. A section that still holds chapters (or any
    /// other file) is refused — the author moves or deletes those first.</summary>
    public static void DeleteSection(string root, string sectionFolder)
    {
        var contentDir = Path.Combine(Path.GetFullPath(root), ProjectLayout.ContentFolder);
        var full = SectionDir(contentDir, sectionFolder);
        var chapters = Directory.GetFiles(full, "*.md").Length;
        if (chapters > 0)
            throw new DocWizLoadException(
                $"“{ContentNames.SectionTitle(Path.GetFileName(full))}” still has {chapters} chapter{(chapters == 1 ? "" : "s")}. Move or delete {(chapters == 1 ? "it" : "them")} first.");
        if (Directory.EnumerateFileSystemEntries(full).Any())
            throw new DocWizLoadException("This section's folder still contains other files. Remove them in Explorer first.");
        Directory.Delete(full);
    }

    /// <summary>Moves a chapter into another section (or to another position in its own) at
    /// <paramref name="index"/> (clamped), renumbering the target section. Returns the
    /// chapter's new absolute path.</summary>
    public static string MoveChapterTo(string chapterPath, string targetSectionDir, int index)
    {
        var full = Path.GetFullPath(chapterPath);
        var target = Path.GetFullPath(targetSectionDir);
        var others = Directory.GetFiles(target, "*.md").OrderBy(f => f, StringComparer.Ordinal)
            .Where(f => !string.Equals(Path.GetFullPath(f), full, StringComparison.OrdinalIgnoreCase))
            .ToList();
        index = Math.Clamp(index, 0, others.Count);

        var moving = full;
        if (!string.Equals(Path.GetDirectoryName(full), target, StringComparison.OrdinalIgnoreCase))
        {
            // Land under a parking name that can't collide; the renumber below names it.
            var (_, rest) = ProjectLoader.SplitNumericPrefix(Path.GetFileNameWithoutExtension(full));
            moving = Path.Combine(target, $"999999-{rest}.md");
            File.Move(full, moving);
        }
        others.Insert(index, moving);
        return Renumber(others, isDirectory: false)[index];
    }

    /// <summary>Chapter files follow their title: "{same prefix}-{title, file-safe}.md".
    /// Another file already using that name gets " (2)", " (3)"… Returns the new path.</summary>
    public static string RenameChapterFile(string chapterPath, string title)
    {
        var full = Path.GetFullPath(chapterPath);
        var dir = Path.GetDirectoryName(full)!;
        var (prefix, _) = ProjectLoader.SplitNumericPrefix(Path.GetFileNameWithoutExtension(full));
        var stem = (prefix.Length > 0 ? prefix + "-" : "") + ContentNames.FileSafe(title);
        var candidate = Path.Combine(dir, stem + ".md");
        for (var n = 2; File.Exists(candidate) && !string.Equals(candidate, full, StringComparison.OrdinalIgnoreCase); n++)
            candidate = Path.Combine(dir, $"{stem} ({n}).md");
        MoveEntry(full, candidate, isDirectory: false);
        return candidate;
    }

    private static string SectionDir(string contentDir, string sectionFolder)
    {
        if (sectionFolder.Length == 0 || sectionFolder.IndexOfAny(['/', '\\']) >= 0 || sectionFolder.Contains(".."))
            throw new DocWizLoadException("Not a section folder name.");
        var full = Path.Combine(contentDir, sectionFolder);
        if (!Directory.Exists(full))
            throw new DocWizLoadException($"No section folder '{sectionFolder}' in this project.");
        return full;
    }

    /// <summary>Validates a section name and refuses one another section already shows
    /// (Windows folder names are case-insensitive). Returns the trimmed name.</summary>
    private static string CheckSectionName(string contentDir, string name, string? except)
    {
        if (ContentNames.SectionNameProblem(name) is { } problem)
            throw new DocWizLoadException(problem);
        var clean = name.Trim();
        var taken = Directory.GetDirectories(contentDir).Any(d =>
            !string.Equals(d, except, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(ContentNames.SectionTitle(Path.GetFileName(d)), clean, StringComparison.OrdinalIgnoreCase));
        if (taken)
            throw new DocWizLoadException($"There's already a section called “{clean}”.");
        return clean;
    }

    /// <summary>Rename a file or folder; a case-only rename (Windows treats both names as the
    /// same entry) goes through a temporary name.</summary>
    private static void MoveEntry(string from, string to, bool isDirectory)
    {
        if (string.Equals(from, to, StringComparison.Ordinal))
            return;
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            var tmp = to + ".__dwcase";
            if (isDirectory) { Directory.Move(from, tmp); Directory.Move(tmp, to); }
            else { File.Move(from, tmp); File.Move(tmp, to); }
            return;
        }
        if (isDirectory) Directory.Move(from, to);
        else File.Move(from, to);
    }

    /// <summary>Human-readable list of places that reference the chapter id. Used to
    /// refuse deleting a chapter that would leave dangling dw: links or view includes.</summary>
    public static List<string> FindReferences(DocProject project, string chapterId)
    {
        var references = new List<string>();
        var linkPattern = new Regex($@"dw:{Regex.Escape(chapterId)}(?![\w-])", RegexOptions.IgnoreCase);

        foreach (var chapter in project.Chapters.Where(c => c.Id != chapterId))
        {
            if (linkPattern.IsMatch(chapter.Markdown))
                references.Add($"chapter \"{chapter.Title}\" links to it");
        }

        // A slide references the chapter via the legacy single-include field OR an inline
        // ::: embed in any region's markdown (Structured Slides v2). Count each slide once.
        var pipeline = DocWizMarkdown.CreatePipeline();
        foreach (var view in project.Views)
        {
            var count = view.Slides.Count(s => SlideReferences(s, chapterId, pipeline));
            if (count > 0)
                references.Add($"view \"{view.Title}\" includes it in {count} slide{(count == 1 ? "" : "s")}");
        }

        return references;
    }

    /// <summary>Does the slide reference <paramref name="chapterId"/> — via the legacy
    /// single-include field or an inline embed in any region's markdown?</summary>
    private static bool SlideReferences(SlideDef slide, string chapterId, Markdig.MarkdownPipeline pipeline)
    {
        if (RefMatches(slide.Include, chapterId))
            return true;
        if (slide.Regions is null)
            return false;
        return slide.Regions.Values.Any(markdown =>
            DocWizEmbed.FindRefs(markdown ?? "", pipeline).Any(r => RefMatches(r, chapterId)));
    }

    private static bool RefMatches(string? reference, string chapterId) =>
        reference is not null &&
        (reference == chapterId || reference.StartsWith(chapterId + "#", StringComparison.Ordinal));

    /// <summary>Renames every entry to "{010,020,…}-{slug}" matching its position in
    /// the given order. Two-phase (via temp names) so swaps never collide with a name
    /// that still exists. Returns the final absolute paths in order.</summary>
    private static List<string> Renumber(List<string> orderedPaths, bool isDirectory)
    {
        const string tmpSuffix = ".__dwmove";
        var finals = new List<string>(orderedPaths.Count);
        for (var i = 0; i < orderedPaths.Count; i++)
        {
            var parent = Path.GetDirectoryName(orderedPaths[i])!;
            var name = isDirectory
                ? Path.GetFileName(orderedPaths[i])
                : Path.GetFileNameWithoutExtension(orderedPaths[i]);
            var (_, slug) = ProjectLoader.SplitNumericPrefix(name);
            var ext = isDirectory ? "" : Path.GetExtension(orderedPaths[i]);
            finals.Add(Path.Combine(parent, $"{(i + 1) * 10:D3}-{slug}{ext}"));
        }

        for (var i = 0; i < orderedPaths.Count; i++)
        {
            if (orderedPaths[i] == finals[i])
                continue;
            if (isDirectory)
                Directory.Move(orderedPaths[i], finals[i] + tmpSuffix);
            else
                File.Move(orderedPaths[i], finals[i] + tmpSuffix);
        }

        for (var i = 0; i < orderedPaths.Count; i++)
        {
            if (orderedPaths[i] == finals[i])
                continue;
            if (isDirectory)
                Directory.Move(finals[i] + tmpSuffix, finals[i]);
            else
                File.Move(finals[i] + tmpSuffix, finals[i]);
        }

        return finals;
    }
}
