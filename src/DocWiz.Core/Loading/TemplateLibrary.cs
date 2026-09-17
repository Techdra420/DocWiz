using DocWiz.Core.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DocWiz.Core.Loading;

/// <summary>
/// One entry in the on-disk template library. <see cref="Id"/> is the folder name (the
/// handle used to instantiate). A non-null <see cref="Error"/> marks a template that
/// exists on disk but fails to load — surfaced disabled rather than silently dropped, so
/// a broken master is visible to whoever maintains it.
/// </summary>
public sealed record TemplateInfo(string Id, string Title, string Description, string Path, string? Error);

/// <summary>
/// File-based project templates: real DocWiz project folders living under a templates
/// directory, discovered at startup. Colleagues author/maintain them as Markdown (the
/// whole point of DocWiz) and users spin a new project off one without a recompile.
///
/// Platform-neutral and testable: it operates on an explicit directory passed in — the
/// resolution of *which* directory (exe folder vs %APPDATA%) lives in the App layer.
/// </summary>
public static class TemplateLibrary
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>Thin metadata wrapper a template folder may carry alongside its
    /// <c>docwiz.yaml</c>. All fields optional — absent ones fall back to the manifest.</summary>
    private sealed class TemplateMeta
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public int Order { get; set; }
    }

    /// <summary>Lists every subfolder of <paramref name="templatesDir"/> that contains a
    /// <c>docwiz.yaml</c>, newest-author-intent first (by optional <c>order</c>, then title).
    /// A missing or empty directory yields an empty list. Templates that fail to load are
    /// included with a non-null <see cref="TemplateInfo.Error"/>.</summary>
    public static IReadOnlyList<TemplateInfo> Discover(string templatesDir)
    {
        if (string.IsNullOrWhiteSpace(templatesDir) || !Directory.Exists(templatesDir))
            return [];

        var entries = new List<(TemplateInfo Info, int Order)>();
        foreach (var dir in Directory.EnumerateDirectories(templatesDir))
        {
            if (!File.Exists(Path.Combine(dir, ProjectLayout.ManifestFileName)))
                continue;

            var id = Path.GetFileName(dir);
            var meta = ReadMeta(dir);

            string title = meta?.Name ?? "";
            string description = meta?.Description ?? "";
            string? error = null;
            try
            {
                var project = ProjectLoader.Load(dir);
                if (string.IsNullOrWhiteSpace(title))
                    title = project.Manifest.Project;
                if (string.IsNullOrWhiteSpace(description))
                    description = $"Project template ({project.Chapters.Count} chapters, {project.Views.Count} views).";
            }
            catch (DocWizLoadException ex)
            {
                error = ex.Message;
            }

            if (string.IsNullOrWhiteSpace(title))
                title = id;

            entries.Add((new TemplateInfo(id, title, description, dir, error), meta?.Order ?? 0));
        }

        return entries
            .OrderBy(e => e.Order)
            .ThenBy(e => e.Info.Title, StringComparer.OrdinalIgnoreCase)
            .Select(e => e.Info)
            .ToList();
    }

    private static TemplateMeta? ReadMeta(string dir)
    {
        var metaPath = Path.Combine(dir, "template.yaml");
        if (!File.Exists(metaPath))
            return null;
        try
        {
            return Yaml.Deserialize<TemplateMeta>(File.ReadAllText(metaPath));
        }
        catch
        {
            // A malformed template.yaml is non-fatal: fall back to the manifest.
            return null;
        }
    }

    /// <summary>Recursively copies a project folder, excluding a top-level <c>output/</c>
    /// (the git-ignored build artifact). Refuses an existing, non-empty destination —
    /// mirrors the guard in <see cref="ProjectScaffolder.CreateNew"/> so neither path can
    /// clobber real work.</summary>
    public static void CopyProject(string source, string dest)
    {
        source = Path.GetFullPath(source);
        dest = Path.GetFullPath(dest);

        if (!Directory.Exists(source))
            throw new DocWizLoadException($"Source project '{source}' does not exist.");
        if (Directory.Exists(dest) && Directory.EnumerateFileSystemEntries(dest).Any())
            throw new DocWizLoadException($"Folder '{dest}' already exists and is not empty.");

        var outputDir = Path.GetFullPath(Path.Combine(source, "output"));
        Directory.CreateDirectory(dest);
        foreach (var srcPath in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var full = Path.GetFullPath(srcPath);
            // Skip the build output tree (top-level output/ and anything under it).
            if (full.StartsWith(outputDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(full, outputDir, StringComparison.OrdinalIgnoreCase))
                continue;

            var rel = Path.GetRelativePath(source, full);
            var target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(full, target, overwrite: false);
        }
    }

    /// <summary>Writes a <c>template.yaml</c> wrapper into a template folder. Title and
    /// description are user-typed, so they are YAML-quoted (a name like "Cell: v2" would
    /// otherwise break the file).</summary>
    public static void WriteTemplateMeta(string templateDir, string name, string description)
    {
        File.WriteAllText(Path.Combine(templateDir, "template.yaml"),
            $"""
            name: {ChapterTemplates.YamlQuote(name)}
            description: {ChapterTemplates.YamlQuote(description)}
            order: 0
            """);
    }
}
