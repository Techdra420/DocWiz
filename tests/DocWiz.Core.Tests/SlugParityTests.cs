using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>
/// ADR-008 parity gate (audit F13): chapter filenames and heading anchors use the ONE slug
/// algorithm — <c>HeadingScanner.Slug</c>. <c>ChapterFactory.Slugify</c> delegates to it (it no
/// longer carries its own regex with a different empty-input fallback), so a title and its derived
/// filename slug can't diverge. (Markdig's GitHub auto-identifier is an external third rule that
/// only applies to a heading with no explicit anchor; the editor persists explicit anchors.)
/// </summary>
public class SlugParityTests
{
    [Fact]
    public void ChapterFactorySlugify_DelegatesToHeadingScannerSlug()
    {
        var src = TestRepo.Read("src", "DocWiz.App", "ChapterFactory.cs");
        Assert.Contains("HeadingScanner.Slug(", src);
        Assert.DoesNotContain("NonSlugRegex", src);   // its own second slug regex is gone
    }
}
