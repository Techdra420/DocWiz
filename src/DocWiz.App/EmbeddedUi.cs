using System.Reflection;

namespace DocWiz.App;

/// <summary>
/// Exact-match lookup of embedded editor UI files. The csproj assigns each wwwroot file
/// a logical name equal to its real relative path, because MSBuild's default resource
/// naming mangles dashes in directory names (vendor/prosemirror/prosemirror-model →
/// prosemirror_model), which would 404 the vendored ProseMirror modules.
/// </summary>
public static class EmbeddedUi
{
    private static readonly Assembly Assembly = typeof(EmbeddedUi).Assembly;

    private static readonly Dictionary<string, string> Names = Assembly
        .GetManifestResourceNames()
        .ToDictionary(n => n.Replace('\\', '/'), n => n, StringComparer.OrdinalIgnoreCase);

    /// <summary>Opens an embedded wwwroot file by relative path, or null when absent.
    /// Pure dictionary lookup — no path arithmetic, so traversal cannot escape.</summary>
    public static Stream? Find(string path)
    {
        var key = "wwwroot/" + path.Replace('\\', '/');
        return Names.TryGetValue(key, out var name) ? Assembly.GetManifestResourceStream(name) : null;
    }
}
