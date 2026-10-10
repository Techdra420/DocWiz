using System.Globalization;
using System.Text.RegularExpressions;
using DocWiz.Core.Loading;
using DocWiz.Core.Models;

namespace DocWiz.Core.History;

/// <summary>A point in one file's timeline. <see cref="Id"/> is the snapshot's file name.</summary>
public sealed record HistoryEntry(string Id, DateTime Utc, string Kind, DateTime? RestoredFromUtc, string FullPath);

/// <summary>A file in "Recently deleted": where it lived, when it was deleted, what it was.</summary>
public sealed record DeletedItem(string Stamp, DateTime Utc, string RelPath, string Kind, string Title);

/// <summary>A named snapshot of the whole project's text (manifest, chapters, views).</summary>
public sealed record NamedVersion(string Id, DateTime Utc, string Name);

/// <summary>
/// The editor's change history (Stage 6 step 3), kept in <c>.docwiz/history/</c> inside the
/// project. Stakeholder rules: kept until deleted, and every snapshot is a COMPLETE copy of one
/// file — never a list of changes — so deleting any snapshot, or the whole folder, breaks
/// nothing. Nothing outside the history screens reads it; the project's own files stay the
/// truth.
/// <para>Layout: <c>files/&lt;key&gt;/&lt;stamp&gt;__&lt;kind&gt;.ext</c> (one folder per file, keyed by
/// identity — a chapter by its id — so renames and moves keep one timeline),
/// <c>deleted/&lt;stamp&gt;/&lt;original relative path&gt;</c>, and
/// <c>versions/&lt;stamp&gt;__&lt;name&gt;/</c> (manifest, content/, views/).</para>
/// </summary>
public static partial class ProjectHistory
{
    public const string Original = "original";   // how a file looked before its first save with history
    public const string Save = "save";
    public const string Rename = "rename";
    public const string Restore = "restore";

    /// <summary>Saves closer together than this merge into one entry, so typing (and
    /// autosave) produces a readable timeline instead of hundreds of entries.</summary>
    public static readonly TimeSpan CoalesceWindow = TimeSpan.FromMinutes(10);

    private const string StampFormat = "yyyyMMdd'T'HHmmssfff'Z'";

    // ---------------------------------------------------------------- per-file snapshots

    /// <summary>Writes <paramref name="text"/> to a project file and records it in the file's
    /// timeline. The first write of a file without history first keeps its previous content
    /// as <see cref="Original"/>. The file write always happens; a history problem never blocks
    /// a save — it is returned as a warning for the caller to log.</summary>
    public static string? Write(string root, string path, string text, string kind = Save, DateTime? now = null)
    {
        string? warning = null;
        string? dir = null;
        var t = now ?? DateTime.UtcNow;
        try
        {
            dir = FileDir(root, path, text);
            if (dir is not null && !Entries(dir).Any() && File.Exists(path))
            {
                var previous = File.ReadAllText(path);
                // Dated by the file's last change, but never after this save (copied files
                // and skewed clocks can carry future times).
                var lastChange = File.GetLastWriteTimeUtc(path);
                if (previous != text)
                    AddSnapshot(dir, previous, Original, lastChange < t ? lastChange : t.AddMilliseconds(-1), Path.GetExtension(path));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warning = $"history: could not keep the previous version of {path}: {ex.Message}";
            dir = null;
        }

        File.WriteAllText(path, text);

        if (dir is not null)
        {
            try { Record(dir, text, kind, t, Path.GetExtension(path)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warning = $"history: could not record the save of {path}: {ex.Message}";
            }
        }
        return warning;
    }

    /// <summary>The file's timeline, newest first (empty when it has none).</summary>
    public static IReadOnlyList<HistoryEntry> List(string root, string path)
    {
        var dir = FileDir(root, path, File.Exists(path) ? File.ReadAllText(path) : null);
        return dir is null ? [] : Entries(dir).ToList();
    }

    /// <summary>The full text of one entry.</summary>
    public static string Read(string root, string path, string entryId) =>
        File.ReadAllText(EntryPath(root, path, entryId));

    /// <summary>Puts an entry's content back into the file. The current content is recorded
    /// first, and the restore itself becomes a new entry — so a restore is always undoable.
    /// Returns the restored text.</summary>
    public static string RestoreEntry(string root, string path, string entryId, DateTime? now = null)
    {
        var entryPath = EntryPath(root, path, entryId);
        var text = File.ReadAllText(entryPath);
        var t = now ?? DateTime.UtcNow;
        var dir = Path.GetDirectoryName(entryPath)!;
        if (File.Exists(path))
            Record(dir, File.ReadAllText(path), Save, t.AddMilliseconds(-1), Path.GetExtension(path));
        var kind = $"{Restore}-{ParseName(Path.GetFileName(entryPath))?.Utc.ToString(StampFormat, CultureInfo.InvariantCulture)}";
        Write(root, path, text, kind, t);
        return text;
    }

    // ---------------------------------------------------------------- recently deleted

    /// <summary>Moves a project file into "Recently deleted" instead of erasing it. A deleted
    /// chapter keeps its timeline (keyed by id), so restoring it brings its history back.</summary>
    public static void MoveToDeleted(string root, string path, DateTime? now = null)
    {
        var rel = RelInside(root, path);
        var stamp = UniqueStamp(Path.Combine(HistoryRoot(root), "deleted"), now ?? DateTime.UtcNow, "");
        var dest = Path.Combine(HistoryRoot(root), "deleted", stamp, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Move(path, dest);
    }

    public static IReadOnlyList<DeletedItem> ListDeleted(string root)
    {
        var bin = Path.Combine(HistoryRoot(root), "deleted");
        if (!Directory.Exists(bin))
            return [];
        var items = new List<DeletedItem>();
        foreach (var stampDir in Directory.GetDirectories(bin))
        {
            var stamp = Path.GetFileName(stampDir);
            if (!TryParseStamp(stamp, out var utc))
                continue;
            foreach (var file in Directory.EnumerateFiles(stampDir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(stampDir, file).Replace('\\', '/');
                items.Add(new DeletedItem(stamp, utc, rel, KindOf(rel), TitleOf(file, rel)));
            }
        }
        return items.OrderByDescending(i => i.Utc).ToList();
    }

    /// <summary>Moves a deleted file back to where it was (its folder is recreated when
    /// needed; a name now taken gets " (restored)"). Returns its project-relative path.</summary>
    public static string RestoreDeleted(string root, string stamp, string relPath)
    {
        if (!TryParseStamp(stamp, out _))
            throw new DocWizLoadException("Not a deleted item.");
        var stampDir = Path.Combine(HistoryRoot(root), "deleted", stamp);
        var source = Path.GetFullPath(Path.Combine(stampDir, relPath));
        if (!source.StartsWith(Path.GetFullPath(stampDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(source))
            throw new DocWizLoadException("That deleted item is no longer in history.");

        var target = Path.Combine(Path.GetFullPath(root), relPath);
        if (File.Exists(target))
            target = Path.Combine(Path.GetDirectoryName(target)!,
                Path.GetFileNameWithoutExtension(target) + " (restored)" + Path.GetExtension(target));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(source, target);
        RemoveEmptyDirs(stampDir);
        return Path.GetRelativePath(root, target).Replace('\\', '/');
    }

    // ---------------------------------------------------------------- named versions

    /// <summary>Copies the project's text (manifest, content/, views/) into a named version.
    /// Images and attachments are not copied. Returns the version id.</summary>
    public static string MarkVersion(string root, string name, DateTime? now = null)
    {
        var versions = Path.Combine(HistoryRoot(root), "versions");
        var id = UniqueStamp(versions, now ?? DateTime.UtcNow, "__" + ContentNames.FileSafe(name.Trim()));
        var dest = Path.Combine(versions, id);
        Directory.CreateDirectory(dest);
        var manifest = Path.Combine(root, ProjectLayout.ManifestFileName);
        if (File.Exists(manifest))
            File.Copy(manifest, Path.Combine(dest, ProjectLayout.ManifestFileName));
        CopyTree(Path.Combine(root, ProjectLayout.ContentFolder), Path.Combine(dest, ProjectLayout.ContentFolder));
        CopyTree(Path.Combine(root, ProjectLayout.ViewsFolder), Path.Combine(dest, ProjectLayout.ViewsFolder));
        return id;
    }

    public static IReadOnlyList<NamedVersion> ListVersions(string root)
    {
        var versions = Path.Combine(HistoryRoot(root), "versions");
        if (!Directory.Exists(versions))
            return [];
        return Directory.GetDirectories(versions)
            .Select(d => Path.GetFileName(d))
            .Select(id => VersionNameRegex().Match(id))
            .Where(m => m.Success && TryParseStamp(m.Groups[1].Value, out _))
            .Select(m => { TryParseStamp(m.Groups[1].Value, out var utc); return new NamedVersion(m.Value, utc, m.Groups[2].Value); })
            .OrderByDescending(v => v.Utc)
            .ToList();
    }

    /// <summary>Restores the project's text to a named version. The current state is first
    /// kept as a version of its own ("Before restoring …"), so the restore can be undone. Each
    /// folder is swapped in one move, so a file locked by another program fails the restore
    /// before anything changed.</summary>
    public static string RestoreVersion(string root, string versionId, DateTime? now = null)
    {
        var source = VersionDir(root, versionId);
        var name = VersionNameRegex().Match(versionId).Groups[2].Value;
        var backup = MarkVersion(root, $"Before restoring {name}", now);

        foreach (var folder in new[] { ProjectLayout.ContentFolder, ProjectLayout.ViewsFolder })
        {
            var live = Path.Combine(root, folder);
            var incoming = live + ".__dwrestore";
            var outgoing = live + ".__dwold";
            if (Directory.Exists(incoming)) Directory.Delete(incoming, recursive: true);
            CopyTree(Path.Combine(source, folder), incoming);
            Directory.CreateDirectory(incoming);
            if (Directory.Exists(live)) Directory.Move(live, outgoing);
            Directory.Move(incoming, live);
            if (Directory.Exists(outgoing)) Directory.Delete(outgoing, recursive: true);
        }
        var manifest = Path.Combine(source, ProjectLayout.ManifestFileName);
        if (File.Exists(manifest))
            File.Copy(manifest, Path.Combine(root, ProjectLayout.ManifestFileName), overwrite: true);
        return backup;
    }

    public static void DeleteVersion(string root, string versionId) =>
        Directory.Delete(VersionDir(root, versionId), recursive: true);

    // ---------------------------------------------------------------- size and clean-up

    public sealed record Stats(long Bytes, int Snapshots, int Deleted, int Versions);

    public static Stats GetStats(string root)
    {
        var hist = HistoryRoot(root);
        if (!Directory.Exists(hist))
            return new Stats(0, 0, 0, 0);
        var bytes = Directory.EnumerateFiles(hist, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        var files = Path.Combine(hist, "files");
        var snapshots = Directory.Exists(files) ? Directory.EnumerateFiles(files, "*", SearchOption.AllDirectories).Count() : 0;
        return new Stats(bytes, snapshots, ListDeleted(root).Count, ListVersions(root).Count);
    }

    /// <summary>Removes snapshots and deleted files older than <paramref name="cutoffUtc"/>.
    /// Named versions are kept — they go only when deleted one by one. Returns how many
    /// entries were removed.</summary>
    public static int CleanUp(string root, DateTime cutoffUtc)
    {
        var removed = 0;
        var files = Path.Combine(HistoryRoot(root), "files");
        if (Directory.Exists(files))
        {
            foreach (var snap in Directory.EnumerateFiles(files, "*", SearchOption.AllDirectories).ToList())
            {
                if (ParseName(Path.GetFileName(snap)) is { } e && e.Utc < cutoffUtc)
                {
                    File.Delete(snap);
                    removed++;
                }
            }
            RemoveEmptyDirs(files);
        }
        var bin = Path.Combine(HistoryRoot(root), "deleted");
        if (Directory.Exists(bin))
        {
            foreach (var stampDir in Directory.GetDirectories(bin))
            {
                if (TryParseStamp(Path.GetFileName(stampDir), out var utc) && utc < cutoffUtc)
                {
                    removed += Directory.EnumerateFiles(stampDir, "*", SearchOption.AllDirectories).Count();
                    Directory.Delete(stampDir, recursive: true);
                }
            }
        }
        return removed;
    }

    public static string HistoryRoot(string root) => Path.Combine(Path.GetFullPath(root), ProjectLayout.HistoryFolder);

    // ---------------------------------------------------------------- internals

    /// <summary>The timeline folder of a project file, by identity: a chapter by its id (so it
    /// survives renames and moves), a view by its file name, the manifest as "project". Null
    /// for files without history.</summary>
    private static string? FileDir(string root, string path, string? text)
    {
        var rel = RelInside(root, path);
        string? key = null;
        if (rel.Equals(ProjectLayout.ManifestFileName, StringComparison.OrdinalIgnoreCase))
            key = "project";
        else if (rel.StartsWith(ProjectLayout.ViewsFolder + "/", StringComparison.OrdinalIgnoreCase) && rel.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
            key = "views/" + ContentNames.FileSafe(Path.GetFileNameWithoutExtension(rel));
        else if (rel.StartsWith(ProjectLayout.ContentFolder + "/", StringComparison.OrdinalIgnoreCase) && rel.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            key = "chapters/" + ContentNames.FileSafe(ChapterId(text) ?? Path.GetFileNameWithoutExtension(rel));
        if (key is null)
            return null;
        EnsureHistoryRoot(root);
        return Path.Combine(HistoryRoot(root), "files", key);
    }

    private static void EnsureHistoryRoot(string root)
    {
        var hist = HistoryRoot(root);
        if (Directory.Exists(hist))
            return;
        Directory.CreateDirectory(hist);
        // Self-ignoring: git skips the whole editor-data folder without touching the project's
        // own .gitignore.
        File.WriteAllText(Path.Combine(Path.GetFullPath(root), ProjectLayout.EditorDataFolder, ".gitignore"),
            "# DocWiz editor data (change history). Not part of the documentation.\n*\n");
    }

    private static void Record(string dir, string text, string kind, DateTime utc, string ext)
    {
        var latest = Entries(dir).FirstOrDefault();
        if (latest is not null && File.ReadAllText(latest.FullPath) == text)
            return; // nothing changed since the last entry
        if (kind == Save && latest is { Kind: Save } && utc - latest.Utc < CoalesceWindow && utc >= latest.Utc)
            File.Delete(latest.FullPath); // one entry per burst of saves
        AddSnapshot(dir, text, kind, utc, ext);
    }

    private static void AddSnapshot(string dir, string text, string kind, DateTime utc, string ext)
    {
        Directory.CreateDirectory(dir);
        var name = UniqueStamp(dir, utc, $"__{kind}{ext}");
        File.WriteAllText(Path.Combine(dir, name), text);
    }

    private static IEnumerable<HistoryEntry> Entries(string dir) =>
        !Directory.Exists(dir)
            ? []
            : Directory.GetFiles(dir)
                .Select(f => ParseName(Path.GetFileName(f)) is { } e ? e with { FullPath = f } : null)
                .Where(e => e is not null)
                .Select(e => e!)
                .OrderByDescending(e => e.Utc)
                .ThenByDescending(e => e.Id, StringComparer.Ordinal);

    private static HistoryEntry? ParseName(string fileName)
    {
        var m = EntryNameRegex().Match(fileName);
        if (!m.Success || !TryParseStamp(m.Groups[1].Value, out var utc))
            return null;
        var kind = m.Groups[2].Value;
        DateTime? from = null;
        if (kind.StartsWith(Restore + "-", StringComparison.Ordinal) && TryParseStamp(kind[(Restore.Length + 1)..], out var f))
        {
            from = f;
            kind = Restore;
        }
        return new HistoryEntry(fileName, utc, kind, from, "");
    }

    private static string EntryPath(string root, string path, string entryId)
    {
        if (entryId.IndexOfAny(['/', '\\']) >= 0 || ParseName(entryId) is null)
            throw new DocWizLoadException("Not a history entry.");
        var dir = FileDir(root, path, File.Exists(path) ? File.ReadAllText(path) : null)
            ?? throw new DocWizLoadException("This file has no history.");
        var full = Path.Combine(dir, entryId);
        if (!File.Exists(full))
            throw new DocWizLoadException("That history entry no longer exists.");
        return full;
    }

    private static string VersionDir(string root, string versionId)
    {
        if (versionId.IndexOfAny(['/', '\\']) >= 0 || versionId.Contains("..") || !VersionNameRegex().IsMatch(versionId))
            throw new DocWizLoadException("Not a named version.");
        var dir = Path.Combine(HistoryRoot(root), "versions", versionId);
        if (!Directory.Exists(dir))
            throw new DocWizLoadException("That version is no longer in history.");
        return dir;
    }

    /// <summary>A stamp-named entry that doesn't exist yet in <paramref name="dir"/> (same
    /// millisecond → the next one).</summary>
    private static string UniqueStamp(string dir, DateTime utc, string suffix)
    {
        for (var t = utc; ; t = t.AddMilliseconds(1))
        {
            var name = t.ToString(StampFormat, CultureInfo.InvariantCulture) + suffix;
            var full = Path.Combine(dir, name);
            if (!File.Exists(full) && !Directory.Exists(full))
                return name;
        }
    }

    private static bool TryParseStamp(string s, out DateTime utc) =>
        DateTime.TryParseExact(s, StampFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc);

    private static string RelInside(string root, string path)
    {
        var rootFull = Path.GetFullPath(root);
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new DocWizLoadException("Not a file inside this project.");
        return Path.GetRelativePath(rootFull, full).Replace('\\', '/');
    }

    private static string? ChapterId(string? text)
    {
        if (text is null)
            return null;
        var (yaml, _) = FrontmatterParser.Split(text);
        var m = yaml is null ? null : IdRegex().Match(yaml);
        return m is { Success: true } ? m.Groups[1].Value.Trim().Trim('\'', '"') : null;
    }

    private static string KindOf(string rel) =>
        rel.StartsWith(ProjectLayout.ContentFolder + "/", StringComparison.OrdinalIgnoreCase) ? "chapter"
        : rel.StartsWith(ProjectLayout.ViewsFolder + "/", StringComparison.OrdinalIgnoreCase) ? "view"
        : rel.StartsWith(ProjectLayout.AttachmentsFolder + "/", StringComparison.OrdinalIgnoreCase) ? "attachment"
        : rel.StartsWith(ProjectLayout.AssetsFolder + "/", StringComparison.OrdinalIgnoreCase) ? "image"
        : "file";

    private static string TitleOf(string file, string rel)
    {
        if (rel.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || rel.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
        {
            var text = File.ReadAllText(file);
            var (yaml, _) = FrontmatterParser.Split(text);
            var m = TitleRegex().Match(yaml ?? text);
            if (m.Success)
                return m.Groups[1].Value.Trim().Trim('\'', '"');
        }
        return Path.GetFileName(rel);
    }

    private static void CopyTree(string from, string to)
    {
        if (!Directory.Exists(from))
            return;
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
        foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir))); // keep empty sections
    }

    private static void RemoveEmptyDirs(string dir)
    {
        if (!Directory.Exists(dir))
            return;
        foreach (var sub in Directory.GetDirectories(dir))
            RemoveEmptyDirs(sub);
        if (!Directory.EnumerateFileSystemEntries(dir).Any())
            Directory.Delete(dir);
    }

    [GeneratedRegex(@"^(\d{8}T\d{9}Z)__([a-z]+(?:-\d{8}T\d{9}Z)?)\.[A-Za-z0-9]+$")]
    private static partial Regex EntryNameRegex();

    [GeneratedRegex(@"^(\d{8}T\d{9}Z)__(.+)$")]
    private static partial Regex VersionNameRegex();

    [GeneratedRegex(@"^id:\s*(.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex IdRegex();

    [GeneratedRegex(@"^title:\s*(.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex TitleRegex();
}
