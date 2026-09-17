using System.Text.RegularExpressions;
using DocWiz.Core.Models;
using Markdig;
using Markdig.Extensions.CustomContainers;
using Markdig.Syntax;

namespace DocWiz.Core.Markdown;

/// <summary>
/// The `::: embed dw:chapter#anchor :::` directive — inline transclusion of a chapter
/// element into view region markdown (Structured Slides v2). The directive is a no-body
/// custom container parsed by the stock CustomContainers extension; this project-aware
/// pass replaces each one, in place, with the referenced section re-parsed through the
/// one shared pipeline. The container itself never reaches the HTML renderer.
///
/// Canonical stored form is the TWO-LINE container:
/// <code>
/// ::: embed dw:ch#sec
/// :::
/// </code>
/// The one-line form (`::: embed dw:ch#sec :::`) is NOT used: the stock parser swallows
/// the trailing `:::` into the container arguments and leaves the block open, consuming
/// the following content. The two-line form parses cleanly (Info=="embed", no body) and
/// terminates correctly. This is the form the editor serializer and the YAML fixtures
/// must produce.
///
/// Shaped to mirror <see cref="AudienceFilter.Apply"/> / <see cref="LinkRewriter.Rewrite"/>:
/// a static pass over a freshly parsed fragment. Only call on fragments, never on a
/// chapter's stored AST (it mutates the document).
///
/// Single-pass by design (like <see cref="AudienceFilter"/>): an embed nested INSIDE a
/// transcluded section is not recursively resolved and renders as the visible
/// "unresolved embed" placeholder. Nested-embed recursion is deliberately not supported,
/// so this caps recursion at depth 1 with no loop risk; revisit if nested embeds become a
/// requirement.
/// </summary>
public static class DocWizEmbed
{
    /// <summary>The directive name (custom container info string).</summary>
    public const string Name = "embed";

    /// <summary>The canonical two-line embed directive markdown for a bare
    /// "chapter#anchor" reference (no <c>dw:</c> prefix). The single source of truth for
    /// producing an embed string — used by the legacy-include migration
    /// (<see cref="ViewDefinition.EffectiveRegions"/>), the fixtures, the editor
    /// serializer, and tests. The one-line form is deliberately NOT produced
    /// (the stock parser swallows the trailing <c>:::</c> — see the type remarks).</summary>
    public static string DirectiveFor(string reference) => DirectiveFor(reference, null, null);
    public static string DirectiveFor(string reference, string? width) => DirectiveFor(reference, width, null);

    /// <summary>The canonical two-line embed directive, optionally carrying layout tokens:
    /// <c>width=</c> (L205 linked-box resize) and <c>align=center|right</c> (L205 box
    /// alignment; left is the default = no token). Null/empty/invalid values are dropped, so
    /// a plain embed stays byte-identical. The single source of truth for the directive.</summary>
    public static string DirectiveFor(string reference, string? width, string? align)
    {
        var w = NormalizeWidth(width);
        var a = NormalizeAlign(align);
        var tail = (w is null ? "" : " width=" + w) + (a is null ? "" : " align=" + a);
        return $"::: embed dw:{reference}{tail}\n:::";
    }

    /// <summary>The ONE wrapper both surfaces use for a RESOLVED embed: the editor's embed
    /// node-view (<see cref="DocWiz.App.EditorDoc"/>) and the handover renderer
    /// (<see cref="DocWiz.Build.ViewRenderer"/>) wrap a resolved transclusion in this single
    /// <c>&lt;div class="dw-embed"&gt;</c> so the edit DOM equals the ship DOM (a multi-block
    /// transclusion becomes one grouped box on both sides). Defined once here so the two
    /// surfaces can never drift. An optional <paramref name="width"/> (L205) sizes the whole
    /// linked box (content REFLOWS, not zoomed) and <paramref name="align"/> (center|right)
    /// places it within the column/zone — applied identically on both surfaces.</summary>
    public static string WrapResolved(string innerHtml, string? width = null, string? align = null)
    {
        var w = NormalizeWidth(width);
        var style = w is null ? "" : " style=\"width:" + w + ";max-width:100%\"";
        var cls = NormalizeAlign(align) switch { "center" => " dw-box-center", "right" => " dw-box-right", _ => "" };
        return "<div class=\"dw-embed" + cls + "\"" + style + ">" + innerHtml + "</div>";
    }

    private static readonly Regex WidthToken =
        new(@"^([0-9]+(\.[0-9]+)?(%|px|em|rem|ch)?|auto)$", RegexOptions.Compiled);

    /// <summary>A linked-box width that is a single CSS length (e.g. "60%", "480px", "auto"),
    /// or null when it is absent or not a plain length. The one place the width-format rule
    /// lives, shared by the directive emitter and both wrappers.</summary>
    public static string? NormalizeWidth(string? width)
    {
        var w = width?.Trim();
        return string.IsNullOrEmpty(w) || !WidthToken.IsMatch(w) ? null : w;
    }

    /// <summary>A box alignment of "center" or "right" (left is the default = null). The one
    /// place the align vocabulary lives, shared by the directive emitter and the wrapper.</summary>
    public static string? NormalizeAlign(string? align)
    {
        var a = align?.Trim().ToLowerInvariant();
        return a is "center" or "right" ? a : null;
    }

    /// <summary>Reads an optional <c>key=value</c> layout token from an embed directive's
    /// arguments ("dw:ch#sec width=60% align=center") and returns the normalized value, or
    /// null. The ref is whitespace-free, so each layout token is a separate space-delimited
    /// token; a trailing <c>:::</c> (stray one-line form) is tolerated.</summary>
    private static string? ParseArg(string? arguments, string key, Func<string?, string?> normalize)
    {
        if (string.IsNullOrWhiteSpace(arguments)) return null;
        var prefix = key + "=";
        foreach (var token in arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = token.EndsWith(":::", StringComparison.Ordinal) ? token[..^3] : token;
            if (t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return normalize(t[prefix.Length..]);
        }
        return null;
    }

    /// <summary>The raw arguments of the FIRST embed container in <paramref name="doc"/> (the
    /// lone embed of a region), or null. Parse-based, mirroring <see cref="FindRefs"/> — the
    /// one place the "first embed" lookup lives for the layout-token readers below.</summary>
    private static string? FirstEmbedArgs(MarkdownDocument doc)
    {
        foreach (var container in doc.Descendants<CustomContainer>())
            if (string.Equals(container.Info, Name, StringComparison.OrdinalIgnoreCase))
                return container.Arguments;
        return null;
    }

    /// <summary>The <c>width=</c> layout token (a CSS length) of a directive's arguments, or null.</summary>
    public static string? ParseWidth(string? arguments) => ParseArg(arguments, "width", NormalizeWidth);

    /// <summary>The <c>align=</c> layout token ("center"/"right") of a directive's arguments, or null.</summary>
    public static string? ParseAlign(string? arguments) => ParseArg(arguments, "align", NormalizeAlign);

    /// <summary>The width / alignment of the FIRST embed container in <paramref name="doc"/>, or null.</summary>
    public static string? WidthOf(MarkdownDocument doc) => ParseWidth(FirstEmbedArgs(doc));
    public static string? AlignOf(MarkdownDocument doc) => ParseAlign(FirstEmbedArgs(doc));

    /// <summary>Every embed container's normalized "chapter#anchor" reference, in document
    /// order. Parse-based — identical to what <see cref="Resolve"/> acts on — so "what is
    /// an embed" is defined in exactly one place. The renderer's auto-heading and the
    /// validator both use this; never hand-roll a regex.</summary>
    public static IReadOnlyList<string> FindRefs(MarkdownDocument doc)
    {
        var refs = new List<string>();
        foreach (var container in doc.Descendants<CustomContainer>())
        {
            if (string.Equals(container.Info, Name, StringComparison.OrdinalIgnoreCase))
                refs.Add(NormalizeRef(container.Arguments ?? ""));
        }
        return refs;
    }

    /// <summary>Parses <paramref name="markdown"/> through <paramref name="pipeline"/>
    /// (the one shared pipeline) and returns its embed references in order.</summary>
    public static IReadOnlyList<string> FindRefs(string markdown, MarkdownPipeline pipeline) =>
        FindRefs(Markdig.Markdown.Parse(markdown, pipeline));

    /// <summary>An embed whose target chapter cannot be found — mirrors how
    /// <see cref="ViewRenderer"/>'s include resolution surfaces a missing chapter, and
    /// is the render-time counterpart to the dangling-link diagnostic the validator
    /// reports. (Missing anchors surface as <see cref="TransclusionResolver.TransclusionException"/>.)</summary>
    public sealed class EmbedException(string message) : Exception(message);

    /// <summary>
    /// Replaces every `::: embed &lt;ref&gt; :::` container in the document with the
    /// transcluded section's content (re-parsed through <paramref name="pipeline"/>).
    /// Throws <see cref="EmbedException"/> for a missing chapter and
    /// <see cref="TransclusionResolver.TransclusionException"/> for a missing anchor —
    /// both are caught by validation before a build runs.
    /// </summary>
    public static void Resolve(MarkdownDocument doc, DocProject project, MarkdownPipeline pipeline)
    {
        foreach (var container in doc.Descendants<CustomContainer>().ToList())
        {
            if (!string.Equals(container.Info, Name, StringComparison.OrdinalIgnoreCase))
                continue;
            // An ancestor embed was already resolved and removed this subtree.
            if (!IsRooted(container, doc))
                continue;

            var section = ResolveSection(container, project);
            var fragment = Markdig.Markdown.Parse(section.Markdown, pipeline);

            var parent = container.Parent!;
            var index = parent.IndexOf(container);
            parent.RemoveAt(index);

            // Move the parsed blocks (in order) into the container's slot. Detach each
            // from the fragment first — a block may only belong to one parent.
            var blocks = fragment.ToList();
            fragment.Clear();
            for (var i = 0; i < blocks.Count; i++)
                parent.Insert(index + i, blocks[i]);
        }
    }

    /// <summary>Resolves the container's ref ("dw:chapter#anchor", or the bare
    /// "chapter#anchor" form) to its transcluded section.</summary>
    private static TranscludedSection ResolveSection(CustomContainer container, DocProject project)
        => ResolveReference(container.Arguments ?? "", project);

    /// <summary>Resolves a reference string ("dw:chapter#anchor" / "chapter#anchor") to its
    /// transcluded section — the single definition of "what an embed points at", shared by
    /// <see cref="Resolve"/> (render-time expansion) and the editor's embed Detach, which
    /// forks the linked section into editable markdown. Throws the same
    /// <see cref="EmbedException"/> / <see cref="TransclusionResolver.TransclusionException"/>
    /// the validator reports for a missing chapter / anchor.</summary>
    public static TranscludedSection ResolveReference(string reference, DocProject project)
    {
        var normalized = NormalizeRef(reference);
        if (normalized.Length == 0)
            throw new EmbedException("embed directive is missing a reference (expected '::: embed dw:chapter#anchor :::').");

        var (chapterId, anchor) = LinkRewriter.SplitRef(normalized);
        var chapter = project.FindChapter(chapterId)
            ?? throw new EmbedException($"embed '{normalized}': no chapter with id '{chapterId}' — run 'docwiz check'.");
        return TransclusionResolver.Resolve(chapter, anchor);
    }

    /// <summary>"dw:ch#sec" / "ch#sec" → "ch#sec". Tolerates a trailing `:::` that the
    /// stock parser swallows into the arguments of the one-line form, and surrounding
    /// whitespace, so a stray one-line directive still resolves. Public so the builder's
    /// per-block embed grouping (<see cref="DocWiz.Build.ViewRenderer"/>) can read a matched
    /// embed container's normalized ref directly from its arguments — the one normalizer.</summary>
    public static string NormalizeRef(string arguments)
    {
        var reference = arguments.Trim();
        if (reference.EndsWith(":::", StringComparison.Ordinal))
            reference = reference[..^3].TrimEnd();
        // The arguments may carry trailing layout tokens (width=…, align=…, L205); the ref
        // is the first whitespace-delimited token (a ref is always "chapter#anchor", no spaces).
        var space = reference.IndexOfAny([' ', '\t']);
        if (space >= 0)
            reference = reference[..space];
        if (reference.StartsWith("dw:", StringComparison.OrdinalIgnoreCase))
            reference = reference[3..];
        return reference.Trim();
    }

    private static bool IsRooted(Block block, MarkdownDocument doc)
    {
        for (var current = block.Parent; current is not null; current = current.Parent)
        {
            if (current == doc)
                return true;
        }
        return false;
    }
}
