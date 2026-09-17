using System.IO;
using DocWiz.App;
using Xunit;

namespace DocWiz.Core.Tests;

public class EmfRetrofitTests
{
    [Fact]
    public void RewriteEmfLinks_SwapsExtension_OnlyForApprovedImageLinks()
    {
        var md =
            "# C\n\n" +
            "![](../../assets/s/image1.emf)\n" +              // approved → .png
            "![alt](../../assets/s/photo.png)\n" +           // not emf → unchanged
            "![](../../assets/s/skip.wmf)\n" +               // emf but NOT approved → unchanged
            "see `image1.emf` in prose and image1.emf bare\n"; // not image links → untouched

        // Approve only image1.emf (by filename) to prove per-target gating.
        var result = DocWiz.App.EmfRetrofit.RewriteEmfLinks(
            md, url => System.IO.Path.GetFileName(url).Equals("image1.emf", System.StringComparison.OrdinalIgnoreCase));

        Assert.Contains("![](../../assets/s/image1.png)", result);
        Assert.Contains("![alt](../../assets/s/photo.png)", result);
        Assert.Contains("![](../../assets/s/skip.wmf)", result); // not approved → kept
        Assert.Contains("`image1.emf`", result);                 // code span untouched
        Assert.Contains("image1.emf bare", result);              // prose untouched
    }
}

public class EmfRetrofitConvertTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "docwiz-emf-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private void Asset(string relUnderAssets, byte[] bytes)
    {
        var p = Path.Combine(_root, "assets", relUnderAssets.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, bytes);
    }

    private void Chapter(string relUnderContent, string body)
    {
        var p = Path.Combine(_root, "content", relUnderContent.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, body);
    }

    // Stub: writes a 4-byte "png" and reports success; the failing stub returns false.
    private static bool OkConverter(string src, string png) { File.WriteAllBytes(png, [0x89, 0x50, 0x4E, 0x47]); return true; }

    [Fact]
    public void CountConvertible_CountsEmfAndWmf()
    {
        Asset("s/a.emf", [1]); Asset("s/b.wmf", [1]); Asset("s/c.png", [1]);
        Assert.Equal(2, DocWiz.App.EmfRetrofit.CountConvertible(_root));
    }

    [Fact]
    public void ConvertProject_Converts_MovesOriginal_RewritesLink()
    {
        Asset("s/img1.emf", [1, 2, 3]);
        Chapter("010-sec/010-ch.md", "# Ch\n\n![](../../assets/s/img1.emf)\n");

        var r = DocWiz.App.EmfRetrofit.ConvertProject(_root, OkConverter);

        Assert.Equal(1, r.Converted);
        Assert.Equal(0, r.Failed);
        Assert.Equal(1, r.LinksRewritten);
        Assert.True(File.Exists(Path.Combine(_root, "assets", "s", "img1.png")));
        Assert.False(File.Exists(Path.Combine(_root, "assets", "s", "img1.emf")));      // gone from assets
        Assert.True(File.Exists(Path.Combine(_root, "_emf-originals", "assets", "s", "img1.emf"))); // moved, not deleted
        var body = File.ReadAllText(Path.Combine(_root, "content", "010-sec", "010-ch.md"));
        Assert.Contains("![](../../assets/s/img1.png)", body);
        Assert.DoesNotContain(".emf", body);

        // Idempotent: a second run finds nothing.
        var r2 = DocWiz.App.EmfRetrofit.ConvertProject(_root, OkConverter);
        Assert.Equal(0, r2.Converted);
        Assert.Equal(0, r2.LinksRewritten);
    }

    [Fact]
    public void ConvertProject_OneFileThrows_BatchContinues_RecordsFailure()
    {
        Asset("s/good.emf", [1]);
        Asset("s/bad.emf", [2]);
        Chapter("010-sec/010-ch.md",
            "# Ch\n\n![](../../assets/s/good.emf)\n\n![](../../assets/s/bad.emf)\n");

        // Converter throws for bad.emf (a locked file), succeeds for good.emf. The throw must
        // not abort the batch — good.emf still converts and its link is still rewritten.
        var r = DocWiz.App.EmfRetrofit.ConvertProject(_root, (src, png) =>
        {
            if (src.EndsWith("bad.emf", StringComparison.OrdinalIgnoreCase))
                throw new IOException("locked");
            File.WriteAllBytes(png, [0x89, 0x50, 0x4E, 0x47]);
            return true;
        });

        Assert.Equal(1, r.Converted);
        Assert.Equal(1, r.Failed);
        Assert.Equal(1, r.LinksRewritten);
        Assert.True(File.Exists(Path.Combine(_root, "assets", "s", "good.png")));
        Assert.True(File.Exists(Path.Combine(_root, "assets", "s", "bad.emf"))); // left in place
        var body = File.ReadAllText(Path.Combine(_root, "content", "010-sec", "010-ch.md"));
        Assert.Contains("assets/s/good.png", body);
        Assert.Contains("assets/s/bad.emf", body); // not rewritten — its conversion threw
    }

    [Fact]
    public void ConvertProject_FailedConversion_KeepsOriginalAndLink()
    {
        Asset("s/bad.emf", [9]);
        Chapter("010-sec/010-ch.md", "# Ch\n\n![](../../assets/s/bad.emf)\n");

        var r = DocWiz.App.EmfRetrofit.ConvertProject(_root, (src, png) => false);

        Assert.Equal(0, r.Converted);
        Assert.Equal(1, r.Failed);
        Assert.Single(r.FailedFiles);
        Assert.True(File.Exists(Path.Combine(_root, "assets", "s", "bad.emf"))); // left in place
        var body = File.ReadAllText(Path.Combine(_root, "content", "010-sec", "010-ch.md"));
        Assert.Contains("![](../../assets/s/bad.emf)", body); // link unchanged
    }
}
