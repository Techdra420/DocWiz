using DocWiz.App;
using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>
/// The editor UI ships as embedded resources. Default MSBuild resource naming mangles
/// dashes in DIRECTORY names (vendor/prosemirror/prosemirror-model → prosemirror_model),
/// which would 404 every vendored ProseMirror module — so embedding uses explicit
/// path-preserving logical names and the lookup is an exact match.
/// </summary>
public class EmbeddedUiTests
{
    [Theory]
    [InlineData("index.html")]
    [InlineData("app.js")]
    [InlineData("vendor/prosemirror/prosemirror-inputrules/index.js")]
    [InlineData("vendor/prosemirror/prosemirror-model/index.js")]
    [InlineData("vendor/prosemirror/prosemirror-view/index.js")]
    [InlineData("vendor/prosemirror/w3c-keyname/index.js")]
    [InlineData("vendor/prosemirror/prosemirror-model/LICENSE")]
    public void Find_ResolvesEmbeddedUiFiles(string path)
    {
        using var stream = EmbeddedUi.Find(path);
        Assert.NotNull(stream);
        Assert.True(stream!.Length > 0, $"{path} resolved but is empty");
    }

    [Theory]
    [InlineData("../DocWiz.App.dll")]
    [InlineData("does/not/exist.js")]
    public void Find_ReturnsNullForUnknownPaths(string path)
    {
        Assert.Null(EmbeddedUi.Find(path));
    }

    /// <summary>
    /// Drift guard: wwwroot/probe.js and templates/site/probe.js must be byte-identical.
    /// These are the two copies of the visual probe (editor and handover). When you edit
    /// one, copy it to the other — this test catches the omission.
    /// </summary>
    [Fact]
    public void ProbeJs_EditorAndSiteTemplateAreBytesIdentical()
    {
        var root = FindRepoRoot();
        var editorCopy = Path.Combine(root, "src", "DocWiz.App", "wwwroot", "probe.js");
        var siteCopy = Path.Combine(root, "templates", "site", "probe.js");

        Assert.True(File.Exists(editorCopy), $"Editor probe not found: {editorCopy}");
        Assert.True(File.Exists(siteCopy), $"Site template probe not found: {siteCopy}");

        var editorBytes = File.ReadAllBytes(editorCopy);
        var siteBytes = File.ReadAllBytes(siteCopy);

        Assert.True(editorBytes.SequenceEqual(siteBytes),
            $"probe.js differs between editor ({editorCopy}) and site template ({siteCopy}). " +
            "Copy the updated version to both locations.");
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "DocWiz.sln")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return dir!;
    }
}
