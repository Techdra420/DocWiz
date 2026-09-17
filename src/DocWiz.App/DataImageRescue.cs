using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DocWiz.Core.Models;

namespace DocWiz.App;

/// <summary>
/// Rewrites inline base64 <c>data:</c> image URIs in saved markdown into real project assets.
///
/// A "Copy image" from Word puts the picture on the clipboard ONLY as a Windows bitmap — a
/// format no web clipboard API exposes (neither the synchronous paste event nor
/// <c>navigator.clipboard.read()</c> can see it). ProseMirror's paste fallback therefore
/// inlines it as a multi-kB <c>data:</c> URI. Rather than fight the clipboard client-side, we
/// decode any data: image at SAVE time — the one place the bytes are guaranteed to be present —
/// write it under <c>assets/pasted/</c> (named by content hash, so identical images dedupe and
/// re-saves are idempotent), and replace the URI with the canonical <c>../../assets/</c> link.
/// The saved markdown and the handover never carry a base64 blob, wherever it was pasted.
/// </summary>
public static partial class DataImageRescue
{
    // Matches a base64 data: image URI as it appears inside a markdown image or an <img src>.
    // Base64 has no ')' or '"', so the value naturally stops at the link/attribute delimiter.
    [GeneratedRegex(@"data:image/(?<ext>[A-Za-z0-9.+-]+);base64,(?<data>[A-Za-z0-9+/=]+)")]
    private static partial Regex DataUriRegex();

    public static string Rewrite(string body, string projectRoot)
    {
        if (string.IsNullOrEmpty(body) || !body.Contains("data:image/", StringComparison.OrdinalIgnoreCase))
            return body;

        var pastedDir = Path.Combine(projectRoot, ProjectLayout.AssetsFolder, "pasted");
        return DataUriRegex().Replace(body, m =>
        {
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(m.Groups["data"].Value);
            }
            catch (FormatException)
            {
                return m.Value; // not valid base64 — leave it untouched
            }

            var ext = NormalizeExt(m.Groups["ext"].Value);
            var hash = Convert.ToHexString(SHA256.HashData(bytes))[..12].ToLowerInvariant();
            var fileName = $"{hash}.{ext}";
            Directory.CreateDirectory(pastedDir);
            var dest = Path.Combine(pastedDir, fileName);
            if (!File.Exists(dest))
                File.WriteAllBytes(dest, bytes);
            return $"../../assets/pasted/{fileName}";
        });
    }

    private static string NormalizeExt(string ext) => ext.ToLowerInvariant() switch
    {
        "jpeg" => "jpg",
        "svg+xml" => "svg",
        var e => e,
    };
}
