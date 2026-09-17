using DocWiz.App;
using DocWiz.Core.Loading;
using DocWiz.Core.Markdown;
using DocWiz.Core.Models;
using DocWiz.Build;
using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>The one rule for an external documentation package's offline entry page
/// (ADR-008): prefer the navigable frame <c>handover.html</c> over the
/// generic single-page <c>index.html</c>. Validator, builder, and editor all defer to this.</summary>
public class ExternalEntryResolveTests
{
    private static string FolderWith(params string[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "dw-entry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var f in files) File.WriteAllText(Path.Combine(dir, f), "<html></html>");
        return dir;
    }

    [Fact]
    public void PrefersHandoverOverIndex()
    {
        var dir = FolderWith("index.html", "handover.html");
        Assert.Equal("handover.html", ProjectLayout.ResolveExternalEntry(dir));
    }

    [Fact]
    public void FallsBackToIndex()
    {
        var dir = FolderWith("index.html");
        Assert.Equal("index.html", ProjectLayout.ResolveExternalEntry(dir));
    }

    [Fact]
    public void NullWhenNeitherPresent()
    {
        var dir = FolderWith("readme.txt");
        Assert.Null(ProjectLayout.ResolveExternalEntry(dir));
    }
}

/// <summary>The editor accepts a handover.html-only package and surfaces the resolved entry.</summary>
public class ExternalDocsEntryTests
{
    private static string TempProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "dw-extentry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "docwiz.yaml"), "project: T\ncode: T\nversion: 0.0.0\n");
        return root;
    }

    private static string FolderWith(params string[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "dw-src-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var f in files) File.WriteAllText(Path.Combine(dir, f), "<html></html>");
        return dir;
    }

    [Fact]
    public void Add_AcceptsHandoverOnlyFolder()
    {
        var root = TempProject(); var src = FolderWith("handover.html");
        ExternalDocs.Add(root, "api_reference", src, copyIn: true);
        var p = Assert.Single(ExternalDocs.List(root));
        Assert.True(p.Valid);
        Assert.Equal("handover.html", p.Entry);
    }

    [Fact]
    public void List_PrefersHandoverEntry()
    {
        var root = TempProject(); var src = FolderWith("index.html", "handover.html");
        ExternalDocs.Add(root, "api_reference", src, copyIn: true);
        var p = Assert.Single(ExternalDocs.List(root));
        Assert.Equal("handover.html", p.Entry);
    }
}

/// <summary>The external doc tab now points at a DocWiz wrapper page (not straight into the
/// embedded doc), and the wrapper hosts the resolved entry in an iframe with a back link home —
/// so touch users have a way back (the embedded doc is copied as-is, ADR-006, with no link home).</summary>
public class ExternalTabHrefTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dw-exttab-" + Guid.NewGuid().ToString("N"));
    private readonly string _outDir;

    public ExternalTabHrefTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "content", "010-cell"));
        var ext = Path.Combine(_root, "external", "api");
        Directory.CreateDirectory(ext);
        File.WriteAllText(Path.Combine(ext, "index.html"), "<html></html>");
        File.WriteAllText(Path.Combine(ext, "handover.html"), "<html></html>");
        File.WriteAllText(Path.Combine(_root, "docwiz.yaml"),
            """
            project: T
            code: T
            audiences:
              - id: operator
                title: Operator
            external:
              api_reference: external/api
            """);
        File.WriteAllText(Path.Combine(_root, "content", "010-cell", "010-cell.md"),
            """
            ---
            id: cell
            title: Cell
            audiences: [operator]
            ---

            # Cell

            Body.
            """);

        var project = ProjectLoader.Load(_root);
        var pipeline = DocWizMarkdown.CreatePipeline();
        DocWizMarkdown.ParseAll(project, pipeline);
        _outDir = Path.Combine(_root, "output", "handover");
        HandoverBuilder.Build(project, pipeline, _outDir);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ChromeTabPointsAtWrapperNotEmbeddedDoc()
    {
        var html = File.ReadAllText(Path.Combine(_outDir, "_site", "cell.html"));
        Assert.Contains("external-api_reference.html", html);
        Assert.DoesNotContain("external/api_reference/handover.html", html);
    }

    [Fact]
    public void WrapperHostsEntryInIframeWithBackLink()
    {
        var wrapper = Path.Combine(_outDir, "_site", "external-api_reference.html");
        Assert.True(File.Exists(wrapper), "wrapper page external-api_reference.html was not generated");
        var html = File.ReadAllText(wrapper);
        // iframe hosts the resolved entry (handover.html preferred over index.html)
        Assert.Contains("src=\"external/api_reference/handover.html\"", html);
        Assert.DoesNotContain("external/api_reference/index.html", html);
        // back link to the root entry page (../<EntryFileName> — project "T" -> T.html)
        Assert.Contains("href=\"../T.html\"", html);
        Assert.Contains("← Documentation", html);
    }
}
