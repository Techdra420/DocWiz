using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using DocWiz.App;

namespace DocWiz.Cli;

/// <summary>The only place DocWiz uses System.Drawing (GDI+). Rasterizes a Windows metafile
/// to PNG. Windows-only — injected as an <see cref="EmfToPng"/> so DocWiz.App/Core stay
/// platform-neutral. Defaults (Scale/MinSide/MaxSide/white bg) tuned against real files; see
/// (originals are kept in _emf-originals/, never shipped).</summary>
public static class EmfConverter
{
    private const double Scale = 2.0;   // crispness for small icons
    private const int MinSide = 64;     // floor: import drops size attrs, so intrinsic px = display px
    private const int MaxSide = 2000;   // cap: keep 10 MB diagrams from huge PNGs

    /// <summary>Matches the <see cref="EmfToPng"/> delegate. Returns false (never throws) on a
    /// corrupt/unsupported metafile; logs the reason (guardrail: do not silently swallow) and
    /// the caller records the file as failed so the batch continues.</summary>
    public static bool Convert(string srcPath, string pngPath)
    {
        try
        {
            using var img = Image.FromFile(srcPath); // disposed before return → caller can move the source
            var (w, h) = TargetSize(img.Width, img.Height);
            using var bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.White); // Word EMFs assume a white page; transparent loses fills / haloes edges
                g.DrawImage(img, 0, 0, w, h);
            }
            bmp.Save(pngPath, ImageFormat.Png);
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"EMF/WMF convert failed for {srcPath}: {ex.Message}");
            try { if (File.Exists(pngPath)) File.Delete(pngPath); } catch { /* no half-written PNG */ }
            return false;
        }
    }

    /// <summary>native × Scale, then clamp the longest side into [MinSide, MaxSide].</summary>
    private static (int Width, int Height) TargetSize(int nativeW, int nativeH)
    {
        if (nativeW <= 0 || nativeH <= 0)
            return (MinSide, MinSide);
        double w = nativeW * Scale, h = nativeH * Scale;
        var longest = Math.Max(w, h);
        var factor = longest > MaxSide ? MaxSide / longest
                   : longest < MinSide ? MinSide / longest
                   : 1.0;
        return (Math.Max(1, (int)Math.Round(w * factor)), Math.Max(1, (int)Math.Round(h * factor)));
    }
}
