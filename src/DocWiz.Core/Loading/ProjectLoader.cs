using DocWiz.Core.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DocWiz.Core.Loading;

public sealed class DocWizLoadException(string message) : Exception(message);

/// <summary>Loads a DocWiz project folder into the in-memory model. Structural problems
/// (unreadable YAML, missing manifest) throw; content problems are left to the validator.</summary>
public static class ProjectLoader
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static DocProject Load(string root)
    {
        root = Path.GetFullPath(root);
        var manifestPath = Path.Combine(root, ProjectLayout.ManifestFileName);
        if (!File.Exists(manifestPath))
            throw new DocWizLoadException($"Not a DocWiz project: '{manifestPath}' not found.");

        ProjectManifest manifest;
        try
        {
            manifest = Yaml.Deserialize<ProjectManifest>(File.ReadAllText(manifestPath))
                       ?? new ProjectManifest();
        }
        catch (Exception ex)
        {
            throw new DocWizLoadException($"Cannot parse '{manifestPath}': {ex.Message}");
        }

        return new DocProject
        {
            Root = root,
            Manifest = manifest,
            Chapters = LoadChapters(Path.Combine(root, ProjectLayout.ContentFolder)),
            Views = LoadViews(Path.Combine(root, ProjectLayout.ViewsFolder)),
        };
    }

    private static List<Chapter> LoadChapters(string contentDir)
    {
        var chapters = new List<Chapter>();
        if (!Directory.Exists(contentDir))
            return chapters;

        foreach (var sectionDir in Directory.GetDirectories(contentDir).OrderBy(d => d, StringComparer.Ordinal))
        {
            var sectionName = Path.GetFileName(sectionDir);
            var (sectionOrder, sectionSlug) = SplitNumericPrefix(sectionName);
            var sectionTitle = ContentNames.SectionTitle(sectionName);

            foreach (var file in Directory.GetFiles(sectionDir, "*.md").OrderBy(f => f, StringComparer.Ordinal))
            {
                var (fileOrder, _) = SplitNumericPrefix(Path.GetFileNameWithoutExtension(file));
                var (yaml, body) = FrontmatterParser.Split(File.ReadAllText(file));

                ChapterFrontmatter fm;
                try
                {
                    fm = yaml is null
                        ? new ChapterFrontmatter()
                        : Yaml.Deserialize<ChapterFrontmatter>(yaml) ?? new ChapterFrontmatter();
                }
                catch (Exception ex)
                {
                    throw new DocWizLoadException($"Cannot parse frontmatter of '{file}': {ex.Message}");
                }

                chapters.Add(new Chapter
                {
                    // Validator reports missing id/title; placeholders keep the model loadable.
                    Id = fm.Id ?? "",
                    Title = fm.Title ?? Path.GetFileNameWithoutExtension(file),
                    Discipline = fm.Discipline ?? "",
                    Audiences = fm.Audiences,
                    FilePath = file,
                    SectionSlug = sectionSlug,
                    SectionTitle = sectionTitle,
                    SortKey = $"{sectionOrder}/{fileOrder}",
                    Markdown = body,
                });
            }
        }

        return chapters.OrderBy(c => c.SortKey, StringComparer.Ordinal).ToList();
    }

    private static List<ViewDefinition> LoadViews(string viewsDir)
    {
        var views = new List<ViewDefinition>();
        if (!Directory.Exists(viewsDir))
            return views;

        foreach (var file in Directory.GetFiles(viewsDir, "*.yaml").OrderBy(f => f, StringComparer.Ordinal))
        {
            try
            {
                var view = Yaml.Deserialize<ViewDefinition>(File.ReadAllText(file));
                if (view is null)
                    continue;
                view.FilePath = file;
                views.Add(view);
            }
            catch (Exception ex)
            {
                throw new DocWizLoadException($"Cannot parse view '{file}': {ex.Message}");
            }
        }

        return views;
    }

    /// <summary>"010-overview" → ("010", "overview"); "intro" → ("", "intro"). The one reading
    /// of an order prefix (loader, organizer, chapter factory).</summary>
    public static (string Order, string Slug) SplitNumericPrefix(string name)
    {
        var dash = name.IndexOf('-');
        if (dash > 0 && name[..dash].All(char.IsDigit))
            return (name[..dash], name[(dash + 1)..]);
        return ("", name);
    }

    private static readonly Dictionary<string, string> Acronyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["api"] = "API", ["pdf"] = "PDF", ["faq"] = "FAQ", ["ui"] = "UI",
        ["sql"] = "SQL", ["csv"] = "CSV", ["io"] = "I/O",
    };

    /// <summary>"api-reference" → "API Reference"; "overview" → "Overview".</summary>
    public static string ToDisplayTitle(string slug)
    {
        var words = slug.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => Acronyms.TryGetValue(w, out var a)
                ? a
                : char.ToUpperInvariant(w[0]) + w[1..]);
        return string.Join(" ", words);
    }
}
