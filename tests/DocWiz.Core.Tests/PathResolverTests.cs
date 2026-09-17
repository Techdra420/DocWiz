using System;
using System.IO;
using DocWiz.App;
using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>Pins the editor file-API path-traversal guards (review I11): the resolvers
/// must reject anything outside their allowed subtree and accept only the in-project,
/// correctly-typed targets they are documented to allow.</summary>
public class PathResolverTests : IDisposable
{
    private readonly string _root;

    public PathResolverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "docwiz-pathtest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "content", "010-intro"));
        Directory.CreateDirectory(Path.Combine(_root, "views"));
        File.WriteAllText(Path.Combine(_root, "content", "010-intro", "010-overview.md"), "# hi");
        File.WriteAllText(Path.Combine(_root, "views", "operator.yaml"), "id: operator");
        File.WriteAllText(Path.Combine(_root, "docwiz.yaml"), "title: t");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Theory]
    [InlineData("../../etc/x.md")]
    [InlineData("..\\..\\x.md")]
    [InlineData("/etc/passwd")]
    [InlineData(@"C:\Windows\system32\x.md")]
    [InlineData("content/../../outside.md")]
    public void ResolveChapterPath_RejectsEscapes(string relative)
    {
        Assert.Null(EditorServer.ResolveChapterPath(_root, relative));
    }

    [Fact]
    public void ResolveChapterPath_AcceptsInProjectChapter()
    {
        var ok = EditorServer.ResolveChapterPath(_root, "content/010-intro/010-overview.md");
        Assert.NotNull(ok);
    }

    [Fact]
    public void ResolveChapterPath_RejectsInProjectNonMarkdown()
    {
        Assert.Null(EditorServer.ResolveChapterPath(_root, "docwiz.yaml"));
    }

    [Theory]
    [InlineData("../../etc/x.yaml")]
    [InlineData("..\\..\\x.yaml")]
    [InlineData("/etc/x.yaml")]
    [InlineData(@"C:\Windows\x.yaml")]
    [InlineData("views/../../outside.yaml")]
    [InlineData("content/010-intro/010-overview.md")]
    [InlineData("docwiz.yaml")]
    public void ResolveViewPath_RejectsNonView(string relative)
    {
        Assert.Null(EditorServer.ResolveViewPath(_root, relative, mustExist: false));
        Assert.Null(EditorServer.ResolveViewPath(_root, relative, mustExist: true));
    }

    [Fact]
    public void ResolveViewPath_AcceptsInProjectView()
    {
        Assert.NotNull(EditorServer.ResolveViewPath(_root, "views/operator.yaml", mustExist: true));
        Assert.NotNull(EditorServer.ResolveViewPath(_root, "views/new.yaml", mustExist: false));
    }

    [Theory]
    [InlineData("../../etc/x.yaml")]
    [InlineData("..\\..\\x.yaml")]
    [InlineData("/etc/x.yaml")]
    [InlineData(@"C:\Windows\x.yaml")]
    [InlineData("views/../../outside.yaml")]
    [InlineData("content/010-intro/010-overview.md")]
    public void ResolveYamlPath_RejectsEscapesAndNonYaml(string relative)
    {
        Assert.Null(EditorServer.ResolveYamlPath(_root, relative));
    }

    [Fact]
    public void ResolveYamlPath_AcceptsManifestAndInProjectView()
    {
        Assert.NotNull(EditorServer.ResolveYamlPath(_root, "docwiz.yaml"));
        Assert.NotNull(EditorServer.ResolveYamlPath(_root, "views/operator.yaml"));
    }
}
