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
