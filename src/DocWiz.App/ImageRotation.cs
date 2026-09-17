using DocWiz.Core.Models;

namespace DocWiz.App;

/// <summary>Rotate the raster image at <paramref name="path"/> 90° in place, preserving its
/// format. Clockwise when <paramref name="clockwise"/> is true, else counter-clockwise.
/// Returns false (never throws) on a corrupt/locked/unsupported file. Implemented by
/// DocWiz.Cli.ImageRotator.Rotate and injected, so DocWiz.App/Core stay platform-neutral
/// (the GDI+ dependency lives only in the Windows CLI — mirrors <see cref="EmfToPng"/>).</summary>
public delegate bool RotateImage90(string path, bool clockwise);

/// <summary>Per-image 90° rotation for the editor's figure tools: resolve a project asset,
/// keep the pristine original once, then rotate the file in place via the injected GDI+
/// delegate. The Markdown is untouched (same <c>![alt](src)</c>) — rotation is a pixel
/// edit of the asset, so it needs no save and round-trips trivially in the handover.</summary>
public static class ImageRotation
{
    private const string OriginalsFolder = "_rotated-originals";

    /// <summary>Rotate the asset referenced by <paramref name="srcUrl"/> (a figure's image URL,
    /// e.g. <c>../../assets/diagrams/x.png</c> or <c>/assets/diagrams/x.png</c>) 90° in place.
    /// Backs the pristine original up to <c>_rotated-originals/</c> on the FIRST rotation only
    /// (projects aren't git repos; a lossy re-encode of a JPEG should stay recoverable).
    /// Rejects anything that does not resolve to an existing file under <c>assets/</c>
    /// (path-traversal guard). Returns false without calling the delegate when the guard fails.</summary>
    public static bool RotateAsset(string projectRoot, string srcUrl, bool clockwise, RotateImage90 rotate)
    {
        var assetsRoot = Path.GetFullPath(Path.Combine(projectRoot, ProjectLayout.AssetsFolder));
        var rel = AssetRelative(srcUrl);
        if (rel is null)
            return false;

        var full = Path.GetFullPath(Path.Combine(assetsRoot, rel));
        if (!full.StartsWith(assetsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(full))
            return false;

        BackupOnce(projectRoot, full);
        return rotate(full, clockwise);
    }

    /// <summary>The portion of a figure image URL below <c>assets/</c>, or null if the URL
    /// is not an assets reference. Strips any number of leading <c>../</c> and an optional
    /// leading slash; tolerates both forward and back slashes.</summary>
    private static string? AssetRelative(string srcUrl)
    {
        if (string.IsNullOrWhiteSpace(srcUrl))
            return null;
        var normalized = srcUrl.Replace('\\', '/');
        var marker = normalized.IndexOf("assets/", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
            return null;
        var rel = normalized[(marker + "assets/".Length)..].Trim();
        return rel.Length == 0 ? null : rel;
    }

    /// <summary>Copy (not move) the pristine asset to <c>_rotated-originals/&lt;project-relative
    /// path&gt;</c> the first time it is rotated; later rotations leave that backup untouched so
    /// it always holds the un-rotated source.</summary>
    private static void BackupOnce(string projectRoot, string assetFull)
    {
        var dest = Path.Combine(projectRoot, OriginalsFolder, Path.GetRelativePath(projectRoot, assetFull));
        if (File.Exists(dest))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(assetFull, dest);
    }
}
