using System.IO.Compression;

namespace DocWiz.Build;

/// <summary>Zips a built handover folder into a single deliverable file.</summary>
public static class Packager
{
    /// <summary>Zips <paramref name="sourceDir"/> to <paramref name="zipPath"/> (overwriting).</summary>
    public static string Zip(string sourceDir, string zipPath)
    {
        if (File.Exists(zipPath))
            File.Delete(zipPath);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);
        // Fastest, not Optimal: a handover is mostly already-compressed PNG/PDF assets, so Optimal
        // spends minutes re-compressing for almost no size gain. Fastest zips in seconds.
        ZipFile.CreateFromDirectory(sourceDir, zipPath, CompressionLevel.Fastest, includeBaseDirectory: false);
        return zipPath;
    }

    /// <summary>"Stockroom Demo" + "0.1.0" → "Stockroom-Demo-v0.1.0.zip".</summary>
    public static string ZipName(string code, string version) =>
        $"{FileNameSafe(code)}-v{version}.zip";

    /// <summary>Single source for "string → safe, dashed file-name stem" (ADR-008): the
    /// zip name and the handover entry-page name both derive from it. Splits on invalid
    /// file-name characters AND whitespace (Path.GetInvalidFileNameChars does not include
    /// space), so "Stockroom Inventory System" → "Stockroom-Inventory-System". Falls back to
    /// "handover" when nothing usable remains.</summary>
    public static string FileNameSafe(string s)
    {
        var bad = Path.GetInvalidFileNameChars().Append(' ').Append('\t').ToArray();
        var safe = string.Join("-", s.Split(bad, StringSplitOptions.RemoveEmptyEntries));
        return safe.Length == 0 ? "handover" : safe;
    }
}
