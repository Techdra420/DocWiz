using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.CustomContainers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("DocWiz.Core.Tests")]

namespace DocWiz.Core.Markdown;

/// <summary>A scanned group head: friendly Name + stable Id (for the editor's section picker).</summary>
public sealed record ScannedGroup(string Name, string Id);

/// <summary>
/// The `::: group &lt;name&gt; {#id desc="…"}` container (docs/CONTENT_MODEL.md): a named,
/// optionally-described wrapper that is invisible to readers but addressable by {#id} for
/// embedding (the editor styles it; the handover does not). DocWiz owns head parsing so the
/// behaviour does not depend on whether Markdig's generic-attributes strips the {…} — the id
/// is read from HtmlAttributes when present, else parsed from Arguments, then written back so
/// the rest of the pipeline (CollectAnchors / validator / resolver / picker) treats it like
/// any other anchor. Name + description are stashed as properties for DocWizContainerRenderer.
/// </summary>
public sealed partial class DocWizGroupExtension : IMarkdownExtension
{
    /// <summary>The custom-container info string for this element type.</summary>
    public const string ContainerName = "group";

    /// <summary>HtmlAttributes property key for the group's human-readable name.</summary>
    public const string NameProp = "dw-group-name";

    /// <summary>HtmlAttributes property key for the group's optional description.</summary>
    public const string DescProp = "dw-group-desc";

    public void Setup(MarkdownPipelineBuilder pipeline) => pipeline.DocumentProcessed += Process;

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer) { }

    internal static void Process(MarkdownDocument document)
    {
        foreach (var cc in document.Descendants<CustomContainer>())
        {
            if (!string.Equals(cc.Info, ContainerName, StringComparison.OrdinalIgnoreCase))
                continue;

            var attrs = cc.GetAttributes();

            // Markdig's UseGenericAttributes extension MAY already have parsed the trailing
            // {#id desc="…"} into attrs.Id / attrs.Properties — or it may have left it
            // verbatim in cc.Arguments. ParseHead handles both.
            var existingDesc = attrs.Properties?.FirstOrDefault(p => p.Key == DescProp).Value;
            var (name, id, desc) = ParseHead(cc.Arguments ?? "", attrs.Id, existingDesc);

            if (id is not null)
                attrs.Id = id;

            // Store name and description as properties so DocWizContainerRenderer can read them.
            // Guard against double-adding on re-processing the same document.
            attrs.Properties ??= [];
            if (!attrs.Properties.Any(p => p.Key == NameProp))
            {
                attrs.Properties.Add(new KeyValuePair<string, string?>(NameProp, name));
                if (!string.IsNullOrEmpty(desc))
                    attrs.Properties.Add(new KeyValuePair<string, string?>(DescProp, desc));
            }
        }
    }

    /// <summary>Resolve (name, id, desc) regardless of whether Markdig already stripped the
    /// trailing {#id desc="…"} into <paramref name="attrId"/>/<paramref name="attrDesc"/> or
    /// left it verbatim inside <paramref name="arguments"/>.</summary>
    internal static (string Name, string? Id, string? Desc) ParseHead(
        string arguments, string? attrId, string? attrDesc)
    {
        var name = arguments.Trim();
        string? id = attrId;
        string? desc = attrDesc;

        var m = TrailingAttrs().Match(name);
        if (m.Success)
        {
            name = name[..m.Index].Trim();
            if (id is null && IdToken().Match(m.Groups[1].Value) is { Success: true } im)
                id = im.Groups[1].Value;
            if (string.IsNullOrEmpty(desc) && DescToken().Match(m.Groups[1].Value) is { Success: true } dm)
                desc = dm.Groups[1].Value;
        }

        return (name, id, desc);
    }

    /// <summary>Scan group heads from a chapter body (code fences skipped) for the editor's
    /// section picker. Returns one <see cref="ScannedGroup"/> per group head that has an id.</summary>
    public static List<ScannedGroup> Scan(string body)
    {
        var result = new List<ScannedGroup>();
        var inFence = false;
        foreach (var line in body.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence) continue;

            var m = HeadLine().Match(line);
            if (!m.Success) continue;

            var (name, id, _) = ParseHead(m.Groups[1].Value, null, null);
            if (id is not null && name.Length > 0)
                result.Add(new ScannedGroup(name, id));
        }
        return result;
    }

    // Matches a trailing generic-attributes block: `  {#id desc="…"}` at end of string.
    // [^}]* assumes no nested braces — a content-model guarantee.
    [GeneratedRegex(@"\s*\{([^}]*)\}\s*$")]
    private static partial Regex TrailingAttrs();

    // Matches the first `#slug` inside an attributes block.
    [GeneratedRegex(@"#([A-Za-z0-9_-]+)")]
    private static partial Regex IdToken();

    // Matches `desc="value"` inside an attributes block.
    [GeneratedRegex("desc=\"([^\"]*)\"")]
    private static partial Regex DescToken();

    // Matches a group head line: `::: group <rest>` (case-insensitive, 3+ colons).
    [GeneratedRegex(@"^:{3,}\s+group\s+(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex HeadLine();
}
