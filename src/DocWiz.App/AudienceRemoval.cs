using DocWiz.Core.Loading;
using DocWiz.Core.Models;

namespace DocWiz.App;

/// <summary>Deleting an audience in Project settings removes its tag from every chapter's
/// frontmatter, so no chapter is left pointing at an audience that no longer exists. Chapter
/// bodies are never rewritten: `::: audience` blocks naming it stay for the author to resolve
/// (Check lists them), and a view filtered by it blocks the delete until it picks another
/// audience (see the settings endpoint).</summary>
public static class AudienceRemoval
{
    /// <summary>Strips <paramref name="removedIds"/> from each affected chapter's `audiences`
    /// list (unknown frontmatter keys and the body are preserved). Returns the number of
    /// chapters rewritten.</summary>
    public static int StripFromChapters(DocProject project, IReadOnlyCollection<string> removedIds)
    {
        var changed = 0;
        foreach (var chapter in project.Chapters.Where(c => c.Audiences.Any(removedIds.Contains)))
        {
            var (yaml, rawBody) = FrontmatterParser.Split(File.ReadAllText(chapter.FilePath));
            var fm = ChapterYaml.Parse(yaml);
            fm["audiences"] = ChapterYaml.AudienceList(fm).Where(a => !removedIds.Contains(a)).ToList();
            ProjectWrites.Write(project.Root, chapter.FilePath, ChapterYaml.Compose(fm, rawBody.TrimStart('\n')));
            changed++;
        }
        return changed;
    }
}
