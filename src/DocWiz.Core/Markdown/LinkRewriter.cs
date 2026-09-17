using DocWiz.Core.Models;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace DocWiz.Core.Markdown;

/// <summary>
/// Rewrites a parsed chapter's links for the handover output:
/// - dw:chapter-id#anchor → chapter page URL (ADR-005 stable-ID links)
/// - paths into assets/ and attachments/ → flat output folders
/// - images whose source file is missing → visible placeholder box (never a broken img)
/// Mutates the AST in place; run after validation, before rendering.
/// </summary>
public static class LinkRewriter
{
    public const string ImgPlaceholderClass = "img-placeholder";
    public const string MissingImageText = "image not available yet: ";

    /// <summary>The bare missing-image span. The editor's schema re-creates this text
    /// in JS (wwwroot/editor2/constants.js) — class and text must match byte-for-byte
    /// or the cell faithfulness gate locks every cell holding a placeholder.</summary>
    public static string MissingImagePlaceholder(string assetName) =>
        $"<span class=\"{ImgPlaceholderClass}\">{MissingImageText}{assetName}</span>";

    public static void Rewrite(
        MarkdownDocument doc,
        DocProject project,
        Func<string, string> chapterHref,
        Func<string, string?, string>? dwResolver = null,
        Func<string, string>? fragmentResolver = null)
    {
        foreach (var link in doc.Descendants().OfType<LinkInline>().ToList())
        {
            var url = link.Url ?? "";

            if (url.StartsWith("dw:", StringComparison.OrdinalIgnoreCase))
            {
                var (chapterId, anchor) = SplitRef(url[3..]);
                if (project.FindChapter(chapterId) is not null)
                    link.Url = dwResolver is not null
                        ? dwResolver(chapterId, anchor)
                        : chapterHref(chapterId) + (anchor is null ? "" : $"#{anchor}");
                // Unresolvable dw: links are reported by the validator; leave them untouched here.
                continue;
            }

            if (fragmentResolver is not null && url.StartsWith('#') && url.Length > 1)
            {
                link.Url = fragmentResolver(url[1..]);
                continue;
            }

            if (TryGetLocalFile(url, ProjectLayout.AssetsFolder, out var assetName))
            {
                if (link.IsImage && !File.Exists(Path.Combine(project.AssetsDir, assetName)))
                {
                    link.InsertBefore(new HtmlInline(MissingImagePlaceholder(assetName)));
                    link.Remove();
                }
                else
                {
                    // One source for the emitted asset URL prefix (ADR-008, audit F4):
                    // SingleFileExporter's inliner regex derives its prefix from the same
                    // ProjectLayout.AssetsFolder, so they cannot drift.
                    link.Url = $"{ProjectLayout.AssetsFolder}/{assetName}";
                }
                continue;
            }

            if (TryGetLocalFile(url, ProjectLayout.AttachmentsFolder, out var attachmentName))
                link.Url = $"{ProjectLayout.AttachmentsFolder}/{attachmentName}";
        }
    }

    /// <summary>"cell-overview#daily-startup" → ("cell-overview", "daily-startup").</summary>
    public static (string ChapterId, string? Anchor) SplitRef(string reference)
    {
        var hash = reference.IndexOf('#');
        return hash < 0 ? (reference, null) : (reference[..hash], reference[(hash + 1)..]);
    }

    /// <summary>Matches relative URLs into the given project folder and returns the path
    /// *under* that folder (subfolders included), e.g. "../../assets/renders/cell.png" →
    /// "renders/cell.png"; external URLs never match. Public: shared with the validator
    /// and the editor-doc URL rewrite so all agree on nested paths.</summary>
    public static bool TryGetLocalFile(string url, string folder, out string fileName)
    {
        fileName = "";
        if (url.Contains("://", StringComparison.Ordinal) || url.StartsWith('#'))
            return false;

        var normalized = url.Replace('\\', '/');
        var marker = folder + "/";
        var idx = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return false;
        // Only treat it as a project folder reference when at a path-segment boundary.
        if (idx > 0 && normalized[idx - 1] != '/')
            return false;

        fileName = Uri.UnescapeDataString(normalized[(idx + marker.Length)..]);
        return fileName.Length > 0;
    }
}
