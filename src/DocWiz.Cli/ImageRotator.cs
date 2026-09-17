using System.Drawing;
using System.Drawing.Imaging;
using DocWiz.App;

namespace DocWiz.Cli;

/// <summary>The second (and only other) place DocWiz uses System.Drawing (GDI+): rotates a
/// raster image 90° in place, preserving its on-disk format. Windows-only — injected as a
/// <see cref="RotateImage90"/> so DocWiz.App/Core stay platform-neutral (mirrors
/// <see cref="EmfConverter"/>).</summary>
public static class ImageRotator
{
    /// <summary>Matches the <see cref="RotateImage90"/> delegate. Reads the file fully into
    /// memory first so the source is never locked while we overwrite it, rotates 90°
    /// (clockwise or counter-clockwise), and writes back in the original format. Returns
    /// false (never throws) on a corrupt/locked/unsupported file; logs the reason (guardrail:
    /// do not silently swallow).</summary>
    public static bool Rotate(string path, bool clockwise)
    {
        try
        {
            var format = FormatFor(path);
            // Decode from a copy of the bytes, not Image.FromFile, so the file is not locked
            // when we overwrite it (Image.FromFile holds the handle until disposed).
            var bytes = File.ReadAllBytes(path);
            using var input = new MemoryStream(bytes);
            using var img = Image.FromStream(input);
            img.RotateFlip(clockwise ? RotateFlipType.Rotate90FlipNone : RotateFlipType.Rotate270FlipNone);
            using var output = new MemoryStream();
            img.Save(output, format);
            File.WriteAllBytes(path, output.ToArray());
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Image rotate failed for {path}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Preserve the on-disk encoding by extension; PNG for anything else (the editor
    /// only ever rotates the raster images it can display, and PNG is the lossless default).</summary>
    private static ImageFormat FormatFor(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => ImageFormat.Jpeg,
            ".gif" => ImageFormat.Gif,
            ".bmp" => ImageFormat.Bmp,
            _ => ImageFormat.Png,
        };
}
