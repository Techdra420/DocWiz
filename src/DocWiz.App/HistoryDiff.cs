using DocWiz.Core.Loading;
using DocWiz.Core.Models;

namespace DocWiz.App;

/// <summary>One rendered block of a history comparison: unchanged, removed since the older
/// version, or added since.</summary>
public sealed record DiffBlockDto(string State, string Html);

/// <summary>
/// "Compare this version with now" (Stage 6 step 3). Both chapter bodies are segmented and
/// rendered by the ONE pipeline (EditorDoc.BuildSegments, ADR-004 — the comparison looks like
/// the page), then matched block by block on their source (longest common subsequence).
/// Blocks only in the older version are "del", blocks only in the current one "ins".
/// </summary>
public static class HistoryDiff
{
    public const string Same = "same";
    public const string Removed = "del";
    public const string Added = "ins";

    public static List<DiffBlockDto> Compare(DocProject project, string olderFile, string currentFile)
    {
        var older = Blocks(project, olderFile);
        var current = Blocks(project, currentFile);

        // LCS table over normalized block sources.
        var n = older.Count;
        var m = current.Count;
        var lcs = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
            for (var j = m - 1; j >= 0; j--)
                lcs[i, j] = older[i].Key == current[j].Key ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var result = new List<DiffBlockDto>(n + m);
        int a = 0, b = 0;
        while (a < n && b < m)
        {
            if (older[a].Key == current[b].Key) { result.Add(new(Same, current[b].Html)); a++; b++; }
            else if (lcs[a + 1, b] >= lcs[a, b + 1]) result.Add(new(Removed, older[a++].Html));
            else result.Add(new(Added, current[b++].Html));
        }
        while (a < n) result.Add(new(Removed, older[a++].Html));
        while (b < m) result.Add(new(Added, current[b++].Html));
        return result;
    }

    private static List<(string Key, string Html)> Blocks(DocProject project, string fileText)
    {
        var (_, rawBody) = FrontmatterParser.Split(fileText);
        return EditorDoc.BuildSegments(project, rawBody.TrimStart('\n'), audience: null, resolveEmbeds: false)
            .Where(s => s.Kind == "block")
            .Select(s => (s.Src.Replace("\r\n", "\n").Trim(), s.Html ?? ""))
            .ToList();
    }
}
