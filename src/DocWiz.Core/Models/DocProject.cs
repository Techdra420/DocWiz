namespace DocWiz.Core.Models;

/// <summary>A fully loaded DocWiz project folder.</summary>
public sealed class DocProject
{
    public required string Root { get; init; }
    public required ProjectManifest Manifest { get; init; }

    /// <summary>All chapters in document order (section prefix, then file prefix).</summary>
    public required List<Chapter> Chapters { get; init; }

    public required List<ViewDefinition> Views { get; init; }

    public string AssetsDir => Path.Combine(Root, ProjectLayout.AssetsFolder);
    public string AttachmentsDir => Path.Combine(Root, ProjectLayout.AttachmentsFolder);

    // M11: FindChapter is called per dw:-link / include during render — O(links×chapters)
    // as a linear scan. Build an id→chapter index lazily on first call. Add only if the key
    // is absent so duplicate ids keep first-wins semantics (matching the old FirstOrDefault).
    private Dictionary<string, Chapter>? _byId;

    public Chapter? FindChapter(string id)
    {
        if (_byId is null)
        {
            var index = new Dictionary<string, Chapter>(Chapters.Count, StringComparer.Ordinal);
            foreach (var c in Chapters)
                if (!index.ContainsKey(c.Id))
                    index[c.Id] = c;
            _byId = index;
        }
        return _byId.TryGetValue(id, out var chapter) ? chapter : null;
    }
}
