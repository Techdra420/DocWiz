using System.Text.RegularExpressions;
using DocWiz.Core.Models;

namespace DocWiz.App;

/// <summary>Rasterize a Windows metafile (.emf/.wmf) at srcPath to a PNG at pngPath.
/// Returns false (never throws) when the file can't be converted. Implemented by
/// DocWiz.Cli.EmfConverter.Convert and injected; null on non-Windows / App-standalone.</summary>
public delegate bool EmfToPng(string srcPath, string pngPath);

/// <summary>Counts of a retrofit run. <see cref="FailedFiles"/> are project-relative paths
/// left in place (still .emf/.wmf) for the author to handle.</summary>
public sealed record EmfRetrofitResult(
    int Converted, int Failed, int LinksRewritten, IReadOnlyList<string> FailedFiles);

/// <summary>One-shot conversion of the EMF/WMF a project already contains, plus the shared
/// Markdown link-swap used by the importer. No GDI+ here — the actual rasterization is the
/// injected <see cref="EmfToPng"/> (Windows-only, lives in DocWiz.Cli).</summary>
public static partial class EmfRetrofit
{
    private const string OriginalsFolder = "_emf-originals";

    private static readonly EnumerationOptions Recursive = new() { RecurseSubdirectories = true };

    [GeneratedRegex(@"(?<prefix>!\[[^\]]*\]\()(?<url>[^)\s]+)\)")]
    private static partial Regex ImageLink();

    /// <summary>True for a path/URL ending in .emf or .wmf (the two Windows metafile formats).</summary>
    internal static bool HasMetafileExtension(string pathOrUrl) =>
        pathOrUrl.EndsWith(".emf", StringComparison.OrdinalIgnoreCase) ||
        pathOrUrl.EndsWith(".wmf", StringComparison.OrdinalIgnoreCase);

    /// <summary>Swap <c>.emf</c>/<c>.wmf</c> → <c>.png</c> in Markdown image-link URLs for
    /// which <paramref name="shouldConvert"/> is true. Scoped to image-link syntax, so a
    /// literal ".emf" in prose or a code span is untouched. Pure — used by both the retrofit
    /// and the importer (on the in-memory body before it is written).</summary>
    public static string RewriteEmfLinks(string markdown, Func<string, bool> shouldConvert) =>
        RewriteEmfLinks(markdown, shouldConvert, out _);

    internal static string RewriteEmfLinks(string markdown, Func<string, bool> shouldConvert, out int swaps)
    {
        var n = 0;
        var result = ImageLink().Replace(markdown, m =>
        {
            var url = m.Groups["url"].Value;
            if (!HasMetafileExtension(url) || !shouldConvert(url))
                return m.Value;
            n++;
            return m.Groups["prefix"].Value + Path.ChangeExtension(url, ".png") + ")";
        });
        swaps = n;
        return result;
    }

    /// <summary>How many .emf/.wmf live under the project's assets/ — gates the editor button.</summary>
    public static int CountConvertible(string projectRoot) => FindMetafiles(projectRoot).Count;

    /// <summary>Convert every assets/**/*.emf|*.wmf to PNG, move the originals to
    /// _emf-originals/ (mirroring their assets path — never deleted), and rewrite the
    /// matching image links in content/**/*.md. Idempotent. Views carry no image assets, so
    /// content/ is the complete link surface.</summary>
    public static EmfRetrofitResult ConvertProject(string projectRoot, EmfToPng convert)
    {
        var convertedAbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failed = new List<string>();

        foreach (var src in FindMetafiles(projectRoot))
        {
            try
            {
                if (convert(src, Path.ChangeExtension(src, ".png")))
                {
                    MoveToOriginals(projectRoot, src);
                    convertedAbs.Add(Path.GetFullPath(src));
                }
                else
                {
                    failed.Add(NormRel(projectRoot, src));
                }
            }
            catch (Exception)
            {
                // A locked or permission-denied file must not abort the batch (one bad file
                // would otherwise leave 161 links unrewritten). It is reported back in
                // FailedFiles — surfaced to the author, not silently swallowed — and left in place.
                failed.Add(NormRel(projectRoot, src));
            }
        }

        var linksRewritten = RewriteContentLinks(projectRoot, convertedAbs);
        return new EmfRetrofitResult(convertedAbs.Count, failed.Count, linksRewritten, failed);
    }

    private static List<string> FindMetafiles(string projectRoot)
    {
        var assets = Path.Combine(projectRoot, ProjectLayout.AssetsFolder);
        if (!Directory.Exists(assets))
            return [];
        return Directory.EnumerateFiles(assets, "*", Recursive)
            .Where(HasMetafileExtension)
            .ToList();
    }

    private static void MoveToOriginals(string projectRoot, string src)
    {
        var dest = Path.Combine(projectRoot, OriginalsFolder, Path.GetRelativePath(projectRoot, src));
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Move(src, dest, overwrite: true);
    }

    private static int RewriteContentLinks(string projectRoot, HashSet<string> convertedAbs)
    {
        var content = Path.Combine(projectRoot, ProjectLayout.ContentFolder);
        if (convertedAbs.Count == 0 || !Directory.Exists(content))
            return 0;

        var total = 0;
        foreach (var md in Directory.EnumerateFiles(content, "*.md", Recursive))
        {
            var dir = Path.GetDirectoryName(md)!;
            var text = File.ReadAllText(md);
            var rewritten = RewriteEmfLinks(
                text,
                url => convertedAbs.Contains(Path.GetFullPath(Path.Combine(dir, url))),
                out var swaps);
            if (swaps > 0)
            {
                File.WriteAllText(md, rewritten);
                total += swaps;
            }
        }
        return total;
    }

    private static string NormRel(string projectRoot, string abs) =>
        Path.GetRelativePath(projectRoot, abs).Replace('\\', '/');
}
