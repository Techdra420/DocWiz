namespace DocWiz.Core.Loading;

/// <summary>Structure-preserving edits to docwiz.yaml: targeted line/block changes that keep
/// unknown keys, key order, and comments intact (never a full re-serialize). Mirrors the
/// byte-surgical chapter-save philosophy — the editor's Settings and Documentation-packages
/// surfaces both write through here so hand-added keys/comments survive a save.</summary>
public static class ManifestWriter
{
    private static bool IsTopLevelKey(string line) =>
        line.Length > 0 && line[0] != ' ' && line[0] != '\t' && line[0] != '#' && line.Contains(':');

    /// <summary>The [start,end) line range of a top-level block (the `key:` line through the
    /// line before the next top-level key / EOF), or (-1,-1) if absent.</summary>
    private static (int start, int end) BlockRange(List<string> lines, string key)
    {
        var start = lines.FindIndex(l => l.StartsWith(key + ":", StringComparison.Ordinal) && IsTopLevelKey(l));
        if (start < 0) return (-1, -1);
        var end = start + 1;
        while (end < lines.Count && !IsTopLevelKey(lines[end])) end++;
        return (start, end);
    }

    private static string EmitValue(string? value) => value is null ? "null" : value;

    /// <summary>Add or update one key under the top-level `external:` mapping (creating the
    /// block if absent), preserving every other key, the block's comments, and the rest of the file.</summary>
    public static string SetExternal(string yaml, string key, string? value)
    {
        var lines = Normalize(yaml);
        var nl = Nl(yaml);
        var (start, end) = BlockRange(lines, "external");
        var entry = $"  {key}: {EmitValue(value)}";
        if (start < 0)
        {
            if (lines.Count > 0 && lines[^1].Trim().Length != 0) lines.Add("");
            lines.Add("external:");
            lines.Add(entry);
            return Join(lines, nl);
        }
        var existing = lines.FindIndex(start + 1, end - (start + 1),
            l => l.TrimStart().StartsWith(key + ":", StringComparison.Ordinal));
        if (existing >= 0) { lines[existing] = entry; return Join(lines, nl); }
        // Insert directly after the LAST non-blank line of the block, so the new key sits with
        // its siblings rather than after any trailing blank line / next-block gap.
        var insertAt = end;
        while (insertAt > start + 1 && lines[insertAt - 1].Trim().Length == 0) insertAt--;
        lines.Insert(insertAt, entry);
        return Join(lines, nl);
    }

    /// <summary>Remove one key from the top-level `external:` mapping (no-op if absent).</summary>
    public static string RemoveExternal(string yaml, string key)
    {
        var lines = Normalize(yaml);
        var (start, end) = BlockRange(lines, "external");
        if (start < 0) return yaml;
        var idx = lines.FindIndex(start + 1, end - (start + 1),
            l => l.TrimStart().StartsWith(key + ":", StringComparison.Ordinal));
        if (idx >= 0) lines.RemoveAt(idx);
        return Join(lines, Nl(yaml));
    }

    /// <summary>Replace a top-level scalar's value in place (inserting the key before the first
    /// block-valued key if absent). Values are plain tokens; callers quote where needed.</summary>
    public static string SetScalar(string yaml, string key, string value)
    {
        var lines = Normalize(yaml);
        var idx = lines.FindIndex(l => l.StartsWith(key + ":", StringComparison.Ordinal) && IsTopLevelKey(l));
        var entry = $"{key}: {value}";
        if (idx >= 0) lines[idx] = entry;
        else
        {
            var firstBlock = lines.FindIndex(l => IsTopLevelKey(l) && l.TrimEnd().EndsWith(":", StringComparison.Ordinal));
            if (firstBlock >= 0) lines.Insert(firstBlock, entry); else lines.Add(entry);
        }
        return Join(lines, Nl(yaml));
    }

    /// <summary>Replace the whole top-level `audiences:` sequence (id, title and an optional
    /// description each — values already quoted by the caller where needed), preserving
    /// everything before and after the block.</summary>
    public static string SetAudiences(string yaml, IEnumerable<(string Id, string Title, string? Description)> audiences)
    {
        var lines = Normalize(yaml);
        var block = new List<string> { "audiences:" };
        foreach (var (id, title, description) in audiences)
        {
            block.Add($"  - id: {id}");
            block.Add($"    title: {title}");
            if (!string.IsNullOrEmpty(description))
                block.Add($"    description: {description}");
        }
        var (start, end) = BlockRange(lines, "audiences");
        if (start < 0) { lines.AddRange(block); return Join(lines, Nl(yaml)); }
        lines.RemoveRange(start, end - start);
        lines.InsertRange(start, block);
        return Join(lines, Nl(yaml));
    }

    private static List<string> Normalize(string yaml) =>
        yaml.Replace("\r\n", "\n").Split('\n').ToList();

    /// <summary>The source's dominant newline, so a CRLF file stays CRLF on save (no churn).</summary>
    private static string Nl(string yaml) => yaml.Contains("\r\n") ? "\r\n" : "\n";

    private static string Join(List<string> lines, string nl) => string.Join(nl, lines);
}
