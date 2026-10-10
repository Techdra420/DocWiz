using DocWiz.Core.Markdown;
using DocWiz.Core.Models;
using Markdig.Extensions.CustomContainers;
using Markdig.Syntax;

namespace DocWiz.Core.Validation;

/// <summary>Where one audience is used: chapters tagged with it in frontmatter, chapters
/// containing `::: audience` blocks that name it, and views filtered by it. Feeds the editor's
/// delete-audience confirmation, so the author sees the impact before anything changes.
/// Needs a parsed project (<see cref="DocWizMarkdown.ParseAll"/>).</summary>
public sealed record AudienceUsage(
    IReadOnlyList<Chapter> TaggedChapters,
    IReadOnlyList<(Chapter Chapter, int Count)> BlockChapters,
    IReadOnlyList<ViewDefinition> Views)
{
    public static AudienceUsage Find(DocProject project, string audienceId)
    {
        var tagged = project.Chapters.Where(c => c.Audiences.Contains(audienceId)).ToList();
        var blocks = project.Chapters
            .Select(c => (Chapter: c, Count: c.Parsed?.Descendants<CustomContainer>()
                .Count(cc => AudienceFilter.IsAudienceContainer(cc) && AudienceFilter.AudienceIds(cc).Contains(audienceId)) ?? 0))
            .Where(x => x.Count > 0)
            .ToList();
        var views = project.Views.Where(v => v.Audience == audienceId).ToList();
        return new AudienceUsage(tagged, blocks, views);
    }
}
