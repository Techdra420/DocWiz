using System.Text.RegularExpressions;
using DocWiz.Core.Markdown;
using DocWiz.Core.Models;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace DocWiz.Build;

/// <summary>
/// Links in a standalone document — a PDF or a single-file HTML export, which has no chapter
/// pages and no attachments folder next to it. One rule for every such output (ADR-008): a
/// link to a chapter or section the document contains jumps to it in the document; a link to
/// one it leaves out, or to a project file, becomes plain text (a file gets a "see attachment"
/// note). Only #anchors and web links survive: a relative URL would resolve against wherever
/// the document is opened from — for a PDF, the temp folder it was printed in.
/// An instance holds the targets one document contains, added before any link is rewritten
/// (a link may point forward).
/// </summary>
public sealed class StandaloneLinks
{
    /// <summary>Class of the note that replaces a link to a project file (styled in site.css).</summary>
    public const string AttachmentNoteClass = "pdf-attachment-note";

    private readonly Dictionary<string, string> _targets = new(StringComparer.Ordinal);

    /// <summary>The in-document id of a chapter (anchor null) or of an anchor inside it. The
    /// chapter id namespaces the anchor: anchors are only unique within a chapter (ADR-005).</summary>
    public static string Id(string chapterId, string? anchor) =>
        anchor is null ? chapterId : $"{chapterId}__{anchor}";

    /// <summary>Gives every id="x" in HTML rendered from <paramref name="chapterId"/> its
    /// in-document id, <see cref="Id"/>(chapterId, x).</summary>
    public static string NamespaceIds(string html, string chapterId) =>
        Regex.Replace(html, "id=\"([^\"]+)\"", m => $"id=\"{Id(chapterId, m.Groups[1].Value)}\"");

    /// <summary>Records that the document shows <paramref name="chapterId"/> (anchor null) or
    /// chapterId#anchor at <paramref name="elementId"/>. The first record of a target wins.</summary>
    public void Add(string chapterId, string? anchor, string elementId) =>
        _targets.TryAdd(Key(chapterId, anchor), elementId);

    /// <summary>Records anchors of <paramref name="chapterId"/> rendered through <see cref="NamespaceIds"/>.</summary>
    public void AddAnchors(string chapterId, IEnumerable<string> anchors)
    {
        foreach (var anchor in anchors)
            Add(chapterId, anchor, Id(chapterId, anchor));
    }

    /// <summary>Rewrites the links of a fragment of this document. <paramref name="chapterId"/>
    /// is the chapter the fragment comes from, for its #anchor links; null for view-authored
    /// text, whose #anchors are left as they are.</summary>
    public void Rewrite(MarkdownDocument doc, DocProject project, string? chapterId)
    {
        LinkRewriter.Rewrite(doc, project,
            chapterHref: _ => "", // not called: the dwResolver decides every dw: link
            dwResolver: Href,
            fragmentResolver: chapterId is null ? null : anchor => Href(chapterId, anchor));

        foreach (var link in doc.Descendants<LinkInline>().Where(l => !l.IsImage).ToList())
        {
            var url = link.Url ?? "";
            if (url.StartsWith('#') || IsWebLink(url))
                continue;
            if (LinkRewriter.TryGetLocalFile(url, ProjectLayout.AttachmentsFolder, out var file))
                LinkRewriter.Unlink(link, $" (see attachment: {Path.GetFileName(file)})", AttachmentNoteClass);
            else
                LinkRewriter.Unlink(link);
        }
    }

    private string? Href(string chapterId, string? anchor) =>
        _targets.TryGetValue(Key(chapterId, anchor), out var id) ? "#" + id : null;

    private static string Key(string chapterId, string? anchor) =>
        anchor is null ? chapterId : $"{chapterId}#{anchor}";

    /// <summary>An absolute URL that works from any machine: not a local file, and not a dw:
    /// reference LinkRewriter could not resolve.</summary>
    private static bool IsWebLink(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is not ("file" or "dw");
}
