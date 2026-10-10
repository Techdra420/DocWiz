using DocWiz.Core.Models;
using Markdig;
using Markdig.Extensions.CustomContainers;
using Markdig.Renderers;
using Markdig.Renderers.Html;

namespace DocWiz.Core.Markdown;

/// <summary>Registers DocWiz HTML rendering overrides on top of the stock extensions.
/// <paramref name="audiences"/> (the project's, when known) lets audience blocks name their
/// readers by title; without it they fall back to the ids.</summary>
public sealed class DocWizRenderingExtension(IReadOnlyList<AudienceDef>? audiences = null) : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        // Parsing side is fully covered by the stock CustomContainers extension.
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        if (renderer is HtmlRenderer html)
            html.ObjectRenderers.ReplaceOrAdd<HtmlCustomContainerRenderer>(new DocWizContainerRenderer(audiences));
    }
}

/// <summary>The tag an audience block carries in the full manual — "Only for User, Support".
/// The editor's node view builds the same words (views.js audienceTagLabel; pinned by
/// AudienceTagParityTests).</summary>
public static class AudienceTag
{
    public const string Prefix = "Only for ";

    public static string Label(IEnumerable<string> ids, IReadOnlyList<AudienceDef>? audiences) =>
        Prefix + string.Join(", ", ids.Select(id =>
            audiences?.FirstOrDefault(a => a.Id == id)?.Title is { Length: > 0 } title ? title : id));
}

/// <summary>
/// Renders the DocWiz `:::` element library (docs/CONTENT_MODEL.md "Content elements"):
/// - `audience &lt;ids&gt;` — audience-tagged block (views filter on data-aud)
/// - `note` / `tip` / `warning` / `danger` — callouts; optional argument overrides the label
/// - `steps` — big-numbered procedure styling for the ordered list inside
/// - `cards` — the bullet list inside becomes a navigation card grid
/// - `buttons` — links inside render as a button row
/// - `details` — collapsible section; the argument is the summary text
/// - `center` / `right` — text alignment for the content inside
/// Unknown names render as a plain div (the validator warns about them).
/// </summary>
public sealed class DocWizContainerRenderer(IReadOnlyList<AudienceDef>? audiences = null) : HtmlObjectRenderer<CustomContainer>
{
    /// <summary>Container names accepted by the pipeline; shared with the validator.</summary>
    public static readonly IReadOnlySet<string> KnownContainers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "audience", "note", "tip", "warning", "danger", "steps", "cards", "buttons", "details",
        "center", "right", "embed", "group",
    };

    protected override void Write(HtmlRenderer renderer, CustomContainer obj)
    {
        var info = obj.Info ?? "";
        var arguments = (obj.Arguments ?? "").Trim();

        switch (info.ToLowerInvariant())
        {
            case "audience":
                var ids = AudienceFilter.AudienceIds(obj);
                renderer.Write("<div class=\"aud\" data-aud=\"");
                renderer.WriteEscape(string.Join(" ", ids));
                renderer.Write("\"><span class=\"aud-tag\">");
                renderer.WriteEscape(AudienceTag.Label(ids, audiences));
                renderer.WriteLine("</span>");
                renderer.WriteChildren(obj);
                renderer.WriteLine("</div>");
                break;

            case "note":
            case "tip":
            case "warning":
            case "danger":
                var kind = info.ToLowerInvariant();
                renderer.Write("<div class=\"dw-callout dw-");
                renderer.Write(kind);
                renderer.Write("\"><span class=\"dw-callout-tag\">");
                renderer.WriteEscape(arguments.Length > 0 ? arguments : DefaultLabel(kind));
                renderer.WriteLine("</span>");
                renderer.WriteChildren(obj);
                renderer.WriteLine("</div>");
                break;

            case "steps":
                renderer.WriteLine("<div class=\"dw-steps\">");
                renderer.WriteChildren(obj);
                renderer.WriteLine("</div>");
                break;

            case "cards":
                renderer.WriteLine("<div class=\"dw-cards\">");
                renderer.WriteChildren(obj);
                renderer.WriteLine("</div>");
                break;

            case "buttons":
                renderer.WriteLine("<div class=\"dw-buttons\">");
                renderer.WriteChildren(obj);
                renderer.WriteLine("</div>");
                break;

            case "center":
            case "right":
                renderer.Write("<div class=\"dw-align-");
                renderer.Write(info.ToLowerInvariant());
                renderer.WriteLine("\">");
                renderer.WriteChildren(obj);
                renderer.WriteLine("</div>");
                break;

            case "details":
                renderer.Write("<details class=\"dw-details\"><summary>");
                renderer.WriteEscape(arguments.Length > 0 ? arguments : "Details");
                renderer.WriteLine("</summary>");
                renderer.WriteChildren(obj);
                renderer.WriteLine("</details>");
                break;

            case DocWizGroupExtension.ContainerName:
            {
                var attrs = obj.GetAttributes();
                var name = attrs.Properties?.FirstOrDefault(p => p.Key == DocWizGroupExtension.NameProp).Value;
                var desc = attrs.Properties?.FirstOrDefault(p => p.Key == DocWizGroupExtension.DescProp).Value;
                renderer.Write("<div class=\"dw-group\"");
                if (attrs.Id is { } gid) { renderer.Write(" id=\""); renderer.WriteEscape(gid); renderer.Write("\""); }
                if (!string.IsNullOrEmpty(name)) { renderer.Write(" data-dw-name=\""); renderer.WriteEscape(name); renderer.Write("\""); }
                if (!string.IsNullOrEmpty(desc)) { renderer.Write(" data-dw-desc=\""); renderer.WriteEscape(desc); renderer.Write("\""); }
                renderer.WriteLine(">");
                renderer.WriteChildren(obj);
                renderer.WriteLine("</div>");
                break;
            }

            case DocWizEmbed.Name:
                // An embed should have been replaced by DocWizEmbed.Resolve before render.
                // Reaching the renderer means it was not resolved (e.g. an embed left in a
                // chapter's own AST); render a visible placeholder rather than leaking the
                // raw `embed`/ref text — mirrors the missing-image placeholder convention.
                renderer.Write("<span class=\"dw-embed-unresolved\">unresolved embed: ");
                renderer.WriteEscape(DocWizEmbed.NormalizeRef(arguments));
                renderer.WriteLine("</span>");
                break;

            default:
                renderer.WriteLine("<div>");
                renderer.WriteChildren(obj);
                renderer.WriteLine("</div>");
                break;
        }
    }

    private static string DefaultLabel(string kind) => kind switch
    {
        "tip" => "Tip",
        "warning" => "Warning",
        "danger" => "Danger",
        _ => "Note",
    };
}
