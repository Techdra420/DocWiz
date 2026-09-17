using DocWiz.App;
using DocWiz.Core.Models;
using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>The platform-neutral half of "rotate image 90°": asset resolution, the
/// keep-the-original-once backup, and the path-traversal guard. The actual GDI+ pixel
/// rotation (DocWiz.Cli.ImageRotator) is Windows-only and verified end-to-end via the
/// editor; here the rotation is a fake delegate so the orchestration is testable anywhere.</summary>
public class ImageRotationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "docwiz-rotate-" + Guid.NewGuid().ToString("N"));

    public ImageRotationTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string WriteAsset(string rel, byte[] bytes)
    {
        var full = Path.Combine(_root, ProjectLayout.AssetsFolder, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
        return full;
    }

    private string BackupPath(string rel) =>
        Path.Combine(_root, "_rotated-originals", ProjectLayout.AssetsFolder, rel);

    [Fact]
    public void RotateAsset_CallsDelegate_OnTheResolvedAssetFile()
    {
        var full = WriteAsset("diagrams/x.png", [1, 2, 3]);
        string? seen = null;
        var ok = ImageRotation.RotateAsset(_root, "../../assets/diagrams/x.png", clockwise: true,
            (path, cw) => { seen = path; return cw; });

        Assert.True(ok);
        Assert.Equal(Path.GetFullPath(full), Path.GetFullPath(seen!));
    }

    [Theory]
    [InlineData("../../assets/diagrams/x.png")]
    [InlineData("/assets/diagrams/x.png")]
    [InlineData("assets/diagrams/x.png")]
    public void RotateAsset_ResolvesEveryFigureUrlForm(string src)
    {
        WriteAsset("diagrams/x.png", [1, 2, 3]);
        var called = false;
        var ok = ImageRotation.RotateAsset(_root, src, clockwise: true, (_, _) => { called = true; return true; });
        Assert.True(ok);
        Assert.True(called);
    }

    [Fact]
    public void RotateAsset_KeepsThePristineOriginal_OnceOnly()
    {
        WriteAsset("x.png", [1, 2, 3]);
        // The fake "rotation" overwrites the live file, so a re-copied backup would change.
        RotateImage90 mutate = (path, _) => { File.WriteAllBytes(path, [9, 9, 9]); return true; };

        ImageRotation.RotateAsset(_root, "assets/x.png", true, mutate);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(BackupPath("x.png")));

        // Second rotation must not re-back-up the (now rotated) live file.
        ImageRotation.RotateAsset(_root, "assets/x.png", true, mutate);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(BackupPath("x.png")));
    }

    [Fact]
    public void RotateAsset_RejectsPathTraversal_WithoutCallingDelegate()
    {
        File.WriteAllText(Path.Combine(_root, "secret.txt"), "nope");
        var called = false;
        var ok = ImageRotation.RotateAsset(_root, "assets/../secret.txt", true, (_, _) => { called = true; return true; });
        Assert.False(ok);
        Assert.False(called);
    }

    [Fact]
    public void RotateAsset_ReturnsFalse_ForMissingAsset()
    {
        var ok = ImageRotation.RotateAsset(_root, "assets/nope.png", true, (_, _) => true);
        Assert.False(ok);
    }
}
