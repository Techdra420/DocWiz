using System.Reflection;

namespace DocWiz.Build;

/// <summary>Access to the embedded handover site templates (templates/site/ at repo
/// root). Shared by the builder and the editor's preview so both surfaces render with
/// the identical CSS/JS.</summary>
public static class SiteTemplates
{
    public static string Read(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("." + name, StringComparison.OrdinalIgnoreCase))
            ?? throw new DocWizBuildException($"Embedded site template '{name}' not found.");
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
