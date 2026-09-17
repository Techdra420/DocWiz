using System.Text.Json;

namespace DocWiz.App;

public sealed record RecentProject(string Path, string Project, string Code, DateTime LastOpened, bool Pinned = false);

/// <summary>Recent-projects history for the welcome screen, persisted per user in
/// %APPDATA%\DocWiz\recent.json. Failures are swallowed — history is a convenience,
/// never a reason the editor cannot start.</summary>
public static class RecentProjects
{
    private const int MaxEntries = 10;

    /// <summary>Override the store path for testing. Set to a temp file path in tests;
    /// reset to null in test teardown. Never set in production code.</summary>
    internal static string? StorePathOverride { get; set; }

    private static string StorePath => StorePathOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DocWiz", "recent.json");

    public static List<RecentProject> Load()
    {
        try
        {
            if (!File.Exists(StorePath))
                return [];
            return JsonSerializer.Deserialize<List<RecentProject>>(File.ReadAllText(StorePath)) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Remove(string projectRoot)
    {
        try
        {
            var full = Path.GetFullPath(projectRoot);
            var list = Load()
                .Where(r => !string.Equals(r.Path, full, StringComparison.OrdinalIgnoreCase))
                .ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // best effort only
        }
    }

    public static void Add(string projectRoot, string project, string code)
    {
        try
        {
            var full = Path.GetFullPath(projectRoot);
            var existing = Load();

            // Preserve the pinned state from any existing entry for this path
            var existingEntry = existing.FirstOrDefault(r => string.Equals(r.Path, full, StringComparison.OrdinalIgnoreCase));
            var pinned = existingEntry?.Pinned ?? false;

            var list = existing
                .Where(r => !string.Equals(r.Path, full, StringComparison.OrdinalIgnoreCase))
                .ToList();
            list.Insert(0, new RecentProject(full, project, code, DateTime.Now, pinned));
            if (list.Count > MaxEntries)
                list = list.Take(MaxEntries).ToList();

            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // best effort only
        }
    }

    public static void TogglePin(string projectRoot)
    {
        try
        {
            var full = Path.GetFullPath(projectRoot);
            var list = Load();
            var index = list.FindIndex(r => string.Equals(r.Path, full, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return;
            list[index] = list[index] with { Pinned = !list[index].Pinned };
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // best effort only
        }
    }
}
