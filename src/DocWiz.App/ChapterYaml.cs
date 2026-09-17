using YamlDotNet.Serialization;
using DocWiz.Core.Loading;

namespace DocWiz.App;

/// <summary>
/// Frontmatter round-tripping for the editor: parse to a key-ordered dictionary, update
/// the known fields, re-serialize. Unknown keys survive a save (comments do not — an
/// accepted editor limitation).
/// </summary>
public static class ChapterYaml
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder().Build();

    public static Dictionary<string, object?> Parse(string? yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
            return [];
        return Deserializer.Deserialize<Dictionary<string, object?>>(yaml) ?? [];
    }

    public static List<string> AudienceList(Dictionary<string, object?> fm) =>
        fm.GetValueOrDefault("audiences") is IEnumerable<object> list
            ? list.Select(x => x.ToString() ?? "").Where(s => s.Length > 0).ToList()
            : [];

    /// <summary>The editor's save path. Delegates to the one chapter-file composer
    /// (<see cref="ChapterComposer.Compose"/>) so every creation path and every save emit
    /// identical bytes (ADR-008).</summary>
    public static string Compose(Dictionary<string, object?> fm, string body) =>
        ChapterComposer.Compose(fm, body);
}
