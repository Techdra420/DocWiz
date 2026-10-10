using Markdig.Extensions.CustomContainers;
using Markdig.Syntax;

namespace DocWiz.Core.Markdown;

/// <summary>
/// Applies audience filtering to a parsed fragment for view rendering:
/// `::: audience` containers matching the view's audience are unwrapped into the normal
/// flow (the tag box is manual-only noise inside a filtered view); non-matching
/// containers are removed entirely. Mutates the document — only use on freshly parsed
/// fragments, never on a chapter's stored AST.
/// </summary>
public static class AudienceFilter
{
    /// <summary>Whether a container is a `::: audience …` block.</summary>
    public static bool IsAudienceContainer(CustomContainer container) =>
        string.Equals(container.Info, "audience", StringComparison.OrdinalIgnoreCase);

    /// <summary>The audience ids a `::: audience a b` block names — the ONE reading of its
    /// arguments (ADR-008), shared by this filter, the validator, and the editor's usage count.</summary>
    public static string[] AudienceIds(CustomContainer container) =>
        (container.Arguments ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static void Apply(MarkdownDocument doc, string audience)
    {
        foreach (var container in doc.Descendants<CustomContainer>().ToList())
        {
            if (!IsAudienceContainer(container))
                continue;
            if (!IsRooted(container, doc))
                continue; // ancestor was already removed (nested containers)

            var audiences = AudienceIds(container);
            var parent = container.Parent!;
            var index = parent.IndexOf(container);
            parent.RemoveAt(index);

            if (!audiences.Contains(audience, StringComparer.Ordinal))
                continue;

            var children = container.ToList();
            container.Clear();
            for (var i = 0; i < children.Count; i++)
                parent.Insert(index + i, children[i]);
        }
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
