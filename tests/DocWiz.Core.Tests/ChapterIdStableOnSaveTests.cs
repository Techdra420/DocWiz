using DocWiz.App;
using DocWiz.Core.Loading;
using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>
/// ADR-005: a chapter's stable <c>id</c> is immutable. The editor save path
/// (EditorServer chapter PUT) updates title/discipline/audiences and never the id;
/// this pins that invariant on the actual building blocks the save uses
/// (<see cref="ChapterYaml.Parse"/> → mutate → <see cref="ChapterYaml.Compose"/> →
/// re-split/parse) so a future "re-derive id from title" regression fails here.
/// </summary>
public class ChapterIdStableOnSaveTests
{
    [Fact]
    public void Save_WithChangedTitle_LeavesIdByteIdentical()
    {
        const string originalId = "chapter-fixed-id-42";

        // Same sequence the chapter PUT runs (EditorServer.cs save path).
        var fm = ChapterYaml.Parse($"id: {originalId}\ntitle: Old Title\ndiscipline: setup\n");
        fm["title"] = "A Completely Different Title";          // the only legitimate change
        fm["discipline"] = "operations";
        var composed = ChapterYaml.Compose(fm, "# Heading\n\nBody.\n");

        // Re-parse the saved bytes the way a reload would.
        var (yaml, _) = FrontmatterParser.Split(composed);
        var reloaded = ChapterYaml.Parse(yaml);

        Assert.Equal(originalId, reloaded["id"]);              // ADR-005: id never re-derived
        Assert.Equal("A Completely Different Title", reloaded["title"]);
    }
}
