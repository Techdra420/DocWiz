namespace DocWiz.Core.Models;

/// <summary>Deserialized docwiz.yaml. Field names follow docs/CONTENT_MODEL.md.</summary>
public sealed class ProjectManifest
{
    public string Project { get; set; } = "";
    public string Code { get; set; } = "";
    public string Customer { get; set; } = "";
    public string Version { get; set; } = "0.0.0";
    public string Language { get; set; } = "en";

    /// <summary>Word-style automatic numbering in the manual (sections 1, chapters 1.1,
    /// headings 1.1.1 / 1.1.1.1). Views and slides are never numbered. Default on;
    /// set `numbering: false` in docwiz.yaml to disable.</summary>
    public bool Numbering { get; set; } = true;

    public List<AudienceDef> Audiences { get; set; } = [];

    /// <summary>External documentation packages: key (e.g. api_reference,
    /// release_notes) → path to a self-contained HTML docs folder, or null.
    /// Each non-null entry is copied into the handover and gets a chrome tab.</summary>
    public Dictionary<string, string?> External { get; set; } = [];

    /// <summary>The configured external packages (non-empty paths only), in manifest order.</summary>
    public IEnumerable<(string Key, string Path)> ExternalDocs =>
        External.Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => (kv.Key, kv.Value!));
}

public sealed class AudienceDef
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
}
