using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>
/// ADR-008 parity gate (Stage 6 step 5): an audience block's tag reads "Only for User, Support"
/// — written by the pipeline (<c>AudienceTag.Label</c>) for the manual and the editor's first
/// paint, and redrawn by the editor's node view (<c>audienceTagLabel</c> in views.js) when the
/// author changes the readers in place. Both must use the same words and separator.
/// </summary>
public class AudienceTagParityTests
{
    [Fact]
    public void Pipeline_And_NodeView_WriteTheSameTag()
    {
        var cs = TestRepo.Read("src", "DocWiz.Core", "Markdown", "AudienceContainerRenderer.cs");
        Assert.Contains("public const string Prefix = \"Only for \";", cs);
        Assert.Contains("Prefix + string.Join(\", \",", cs);

        var js = TestRepo.Read("src", "DocWiz.App", "wwwroot", "editor2", "views.js");
        Assert.Contains("return 'Only for ' + titles.join(', ');", js);
    }

    [Fact]
    public void Label_UsesTitles_AndFallsBackToIds()
    {
        var audiences = new[] { new DocWiz.Core.Models.AudienceDef { Id = "user", Title = "User" } };
        Assert.Equal("Only for User, admin", DocWiz.Core.Markdown.AudienceTag.Label(["user", "admin"], audiences));
        Assert.Equal("Only for user", DocWiz.Core.Markdown.AudienceTag.Label(["user"], null));
    }
}
