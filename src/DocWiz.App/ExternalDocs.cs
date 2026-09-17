using DocWiz.Core.Loading;
using DocWiz.Core.Models;

namespace DocWiz.App;

/// <summary>Manage the project's external documentation packages — the handover's chrome
/// doc-tabs (e.g. a generated API site → "API Reference"). Writes docwiz.yaml through
/// <see cref="ManifestWriter"/> so unknown keys/comments survive. Tab titles derive from the
/// key via the ONE source the builder uses (<c>HandoverBuilder.ExternalTabTitle</c>, ADR-008).</summary>
public static class ExternalDocs
{
    public sealed record Package(string Key, string Title, string Source, string Path, bool Valid, long Bytes, string Entry);

    private static string ManifestPath(string root) => System.IO.Path.Combine(root, "docwiz.yaml");

    /// <summary>The on-disk copy folder slug for a *_documentation key: controls_documentation → controls.</summary>
    private static string Slug(string key) =>
        key.EndsWith("_documentation", StringComparison.OrdinalIgnoreCase)
            ? key[..^"_documentation".Length] : key;

    /// <summary>Reject keys that aren't a plain lowercase identifier — they become a folder name
    /// under external/, so a `..`/slash/backslash would escape the project (path traversal).</summary>
    private static void GuardKey(string key)
    {
        if (!ProjectLayout.IsValidExternalKey(key))
            throw new DocWizLoadException($"Invalid documentation-package key '{key}'.");
    }

    /// <summary>Add (or replace) a package. Validates the source has an entry page
    /// (<see cref="ProjectLayout.ExternalEntryCandidates"/>). copyIn → copy the folder into
    /// <c>external/&lt;slug&gt;/</c> (self-contained, default); else store the path as a
    /// reference. Writes only the one external key, preserving the rest of docwiz.yaml.</summary>
    public static void Add(string root, string key, string sourceFolder, bool copyIn)
    {
        GuardKey(key);
        if (!Directory.Exists(sourceFolder) || ProjectLayout.ResolveExternalEntry(sourceFolder) is null)
            throw new DocWizLoadException(
                $"The selected folder must contain an entry page ({string.Join(" or ", ProjectLayout.ExternalEntryCandidates)}) — got '{sourceFolder}'.");

        string stored;
        if (copyIn)
        {
            var rel = System.IO.Path.Combine("external", Slug(key));
            var dest = System.IO.Path.Combine(root, rel);
            if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
            CopyTree(sourceFolder, dest);
            stored = rel.Replace('\\', '/');
        }
        else stored = sourceFolder;

        File.WriteAllText(ManifestPath(root),
            ManifestWriter.SetExternal(File.ReadAllText(ManifestPath(root)), key, stored));
    }

    /// <summary>Remove a package: drop the external key, and delete the embedded copy if this was a
    /// copy-in under <c>external/</c> (a referenced external folder is never touched).</summary>
    public static void Remove(string root, string key)
    {
        GuardKey(key);
        var yaml = File.ReadAllText(ManifestPath(root));
        var embedded = System.IO.Path.Combine(root, "external", Slug(key));
        if (Directory.Exists(embedded)) Directory.Delete(embedded, recursive: true);
        File.WriteAllText(ManifestPath(root), ManifestWriter.RemoveExternal(yaml, key));
    }

    /// <summary>List configured packages with display title, source kind, resolved path, total
    /// bytes, and the resolved entry page (empty when none — the tab would open blank).</summary>
    public static IReadOnlyList<Package> List(string root)
    {
        var project = ProjectLoader.Load(root);
        var result = new List<Package>();
        foreach (var (key, path) in project.Manifest.ExternalDocs)
        {
            var full = System.IO.Path.GetFullPath(path, root);
            var entry = Directory.Exists(full) ? ProjectLayout.ResolveExternalEntry(full) : null;
            var bytes = Directory.Exists(full)
                ? Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length)
                : 0;
            var source = path.Replace('\\', '/').StartsWith("external/", StringComparison.OrdinalIgnoreCase)
                ? "embedded" : "referenced";
            result.Add(new Package(key, DocWiz.Build.HandoverBuilder.ExternalTabTitle(key), source, path,
                entry is not null, bytes, entry ?? ""));
        }
        return result;
    }

    private static void CopyTree(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(src, dest));
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(src, dest), overwrite: true);
    }
}
