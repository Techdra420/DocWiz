using DocWiz.App;
using DocWiz.Core.Loading;
using Xunit;

namespace DocWiz.Core.Tests;

public class ExternalDocsTests
{
    private static string TempProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "dw-extdocs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "docwiz.yaml"), "project: T\ncode: T\nversion: 0.0.0\n");
        return root;
    }

    private static string SourceWithIndex()
    {
        var src = Path.Combine(Path.GetTempPath(), "dw-src-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "index.html"), "<html></html>");
        return src;
    }

    [Fact]
    public void Add_CopyIn_CopiesFolderAndWritesManifest()
    {
        var root = TempProject(); var src = SourceWithIndex();
        ExternalDocs.Add(root, "api_reference", src, copyIn: true);
        Assert.True(File.Exists(Path.Combine(root, "external", "api_reference", "index.html")));
        Assert.Contains("api_reference: external/api_reference",
            File.ReadAllText(Path.Combine(root, "docwiz.yaml")));
    }

    [Fact]
    public void Add_Reference_StoresPathWithoutCopying()
    {
        var root = TempProject(); var src = SourceWithIndex();
        ExternalDocs.Add(root, "api_reference", src, copyIn: false);
        Assert.False(Directory.Exists(Path.Combine(root, "external", "api_reference")));
        Assert.Contains("api_reference: " + src.Replace('\\', '/'),
            File.ReadAllText(Path.Combine(root, "docwiz.yaml")).Replace('\\', '/'));
    }

    [Fact]
    public void Add_MissingIndexHtml_Throws()
    {
        var root = TempProject();
        var src = Path.Combine(Path.GetTempPath(), "dw-noidx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(src);
        Assert.Throws<DocWizLoadException>(() => ExternalDocs.Add(root, "api_reference", src, copyIn: true));
    }

    [Fact]
    public void Add_RejectsTraversalKey()
    {
        var root = TempProject(); var src = SourceWithIndex();
        Assert.Throws<DocWizLoadException>(() => ExternalDocs.Add(root, "..\\..\\evil", src, copyIn: true));
        Assert.Throws<DocWizLoadException>(() => ExternalDocs.Remove(root, "../evil"));
    }

    [Fact]
    public void List_ReportsTitleStatusAndSource()
    {
        var root = TempProject(); var src = SourceWithIndex();
        ExternalDocs.Add(root, "api_reference", src, copyIn: true);
        var pkgs = ExternalDocs.List(root);
        var p = Assert.Single(pkgs);
        Assert.Equal("API Reference", p.Title);
        Assert.True(p.Valid);
        Assert.Equal("embedded", p.Source);
        Assert.True(p.Bytes > 0);
    }

    [Fact]
    public void Remove_DropsEntryAndEmbeddedFolder()
    {
        var root = TempProject(); var src = SourceWithIndex();
        ExternalDocs.Add(root, "api_reference", src, copyIn: true);
        ExternalDocs.Remove(root, "api_reference");
        Assert.False(Directory.Exists(Path.Combine(root, "external", "api_reference")));
        Assert.DoesNotContain("api_reference: external/api_reference",
            File.ReadAllText(Path.Combine(root, "docwiz.yaml")));
    }
}
