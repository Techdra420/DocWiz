using DocWiz.Core.Markdown;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DocWiz.Core.Models;

/// <summary>The ONE serializer for writing a view's YAML (views/*.yaml) — underscored
/// keys + OmitNull (so e.g. <c>mode: document</c> is never dropped, but null/blank slide
/// fields are). Shared by the editor's view endpoints (<c>EditorServer</c>) and the CLI's
/// <c>draft-view</c> verb so editor-written and CLI-written view YAML can never drift.</summary>
public static class ViewYaml
{
    public static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();
}

/// <summary>One audience view from views/*.yaml (rendered by the Stage 2 views engine;
/// already loaded and validated in Stage 1 so `docwiz check` catches dangling includes).</summary>
public sealed class ViewDefinition
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Audience { get; set; } = "";

    /// <summary>"slides" or "document".</summary>
    public string Mode { get; set; } = "document";

    public List<SlideDef> Slides { get; set; } = [];

    /// <summary>Absolute path of the source .yaml file (for diagnostics). Not serialized.</summary>
    [YamlIgnore]
    public string FilePath { get; set; } = "";
}

public sealed class SlideDef
{
    /// <summary>Layout name (see <see cref="SlideLayout"/>). Null = legacy/default title-content.</summary>
    public string? Layout { get; set; }

    /// <summary>Optional explicit slide title override.</summary>
    public string? Title { get; set; }

    /// <summary>Optional subtitle (rendered only where the layout has a slot).</summary>
    public string? Subtitle { get; set; }

    /// <summary>Named regions → markdown (Structured Slides v2). The markdown may mix
    /// authored content and inline <c>::: embed dw:chapter#anchor :::</c> live-linked
    /// blocks. Null when using the legacy single-content form.</summary>
    public Dictionary<string, string>? Regions { get; set; }

    /// <summary>Legacy: transclusion ref when the slide is a single include (no Regions).</summary>
    public string? Include { get; set; }

    /// <summary>Legacy: inline glue content when the slide is a single markdown (no Regions).</summary>
    public string? Markdown { get; set; }

    /// <summary>Whole-chapter link: expands at render time into one slide per H2/H3 heading of
    /// the chapter (always current). Mutually exclusive with Layout/Regions/Include/Markdown.</summary>
    public string? Chapter { get; set; }

    /// <summary>Canonical layout: explicit Layout, else title-content (covers legacy slides).</summary>
    [YamlIgnore]
    public string EffectiveLayout => Layout ?? SlideLayout.TitleContent;

    /// <summary>Canonical regions (region name → markdown): explicit Regions as-is; else a
    /// legacy <c>Include</c> migrated to a body embed directive; else legacy <c>Markdown</c>
    /// as the body; else empty.</summary>
    public IReadOnlyDictionary<string, string> EffectiveRegions()
    {
        if (Regions is { Count: > 0 })
            return Regions;
        if (!string.IsNullOrWhiteSpace(Include))
            return new Dictionary<string, string> { [SlideLayout.BodyRegion] = DocWizEmbed.DirectiveFor(Include) };
        if (!string.IsNullOrWhiteSpace(Markdown))
            return new Dictionary<string, string> { [SlideLayout.BodyRegion] = Markdown };
        return new Dictionary<string, string>();
    }
}
