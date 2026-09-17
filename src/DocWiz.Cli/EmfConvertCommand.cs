using DocWiz.App;
using DocWiz.Core.Models;

namespace DocWiz.Cli;

/// <summary>`docwiz convert-emf &lt;project&gt;` — one-shot EMF/WMF → PNG over an existing
/// project (originals moved to _emf-originals/). Thin console wrapper over the shared
/// <see cref="EmfRetrofit.ConvertProject"/>, injecting the Windows <see cref="EmfConverter"/>.</summary>
public static class EmfConvertCommand
{
    public static int Run(string projectPath)
    {
        projectPath = Path.GetFullPath(projectPath);
        if (!File.Exists(Path.Combine(projectPath, ProjectLayout.ManifestFileName)))
        {
            Console.Error.WriteLine($"ERROR '{projectPath}' is not a DocWiz project ({ProjectLayout.ManifestFileName} not found).");
            return 1;
        }

        var r = EmfRetrofit.ConvertProject(projectPath, EmfConverter.Convert);
        Console.WriteLine($"Converted {r.Converted} EMF/WMF -> PNG; rewrote {r.LinksRewritten} link(s).");
        foreach (var f in r.FailedFiles)
            Console.WriteLine($"  FAILED   {f}");
        if (r.Failed > 0)
            Console.WriteLine($"{r.Failed} file(s) could not be converted and were left in place.");
        else if (r.Converted > 0)
            Console.WriteLine("Originals moved to _emf-originals/ (kept, not shipped).");
        return 0;
    }
}
