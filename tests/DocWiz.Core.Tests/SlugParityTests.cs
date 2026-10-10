using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>
/// ADR-008 parity gate (audit F13, updated for Stage 6): chapter ids and heading anchors use the
/// ONE slug algorithm — <c>HeadingScanner.Slug</c>; <c>ChapterFactory.Slugify</c> delegates to
/// it. Names on disk follow the display name instead (the folder name IS the section name, a
/// chapter file follows its title) through the ONE file-name rule, <c>ContentNames.FileSafe</c>.
/// Neither may grow a second private regex. (Markdig's GitHub auto-identifier is an external
/// third rule that only applies to a heading with no explicit anchor; the editor persists
/// explicit anchors.)
/// </summary>
public class SlugParityTests
{
    [Fact]
    public void ChapterFactory_UsesTheSharedSlugAndFileNameRules()
    {
        var src = TestRepo.Read("src", "DocWiz.App", "ChapterFactory.cs");
        Assert.Contains("HeadingScanner.Slug(", src);
        Assert.Contains("ContentNames.FileSafe(", src);
        Assert.DoesNotContain("NonSlugRegex", src);   // its own second slug regex is gone
    }
}
