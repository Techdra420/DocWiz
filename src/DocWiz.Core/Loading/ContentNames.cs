using System.Text;
using System.Text.RegularExpressions;

namespace DocWiz.Core.Loading;

/// <summary>
/// Display names ↔ names on disk (Stage 6 decision, 2026-10-10): the folder name IS the
/// section name — <c>020-Instalace a síť</c> shows as "Instalace a síť" — and chapter files
/// follow their title. The ONE place these rules live (ADR-008): the loader, the editor's
/// rename/create endpoints, and the importer all go through here. Links never depend on
/// these names; they use chapter ids (ADR-005).
/// </summary>
public static partial class ContentNames
{
    /// <summary>Characters Windows refuses in file and folder names.</summary>
    private static readonly char[] Forbidden = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    /// <summary>How the forbidden characters are listed in messages.</summary>
    public const string ForbiddenList = "\\ / : * ? \" < > |";

    /// <summary>Keeps content paths well clear of the Windows path-length limit.</summary>
    public const int MaxNameLength = 80;

    /// <summary>The display title of a section folder: the text after the order prefix,
    /// verbatim. Legacy slug folders (<c>020-setup</c>, <c>050-api-reference</c>) keep their
    /// derived titles ("Setup", "API Reference").</summary>
    public static string SectionTitle(string folderName)
    {
        var (_, rest) = ProjectLoader.SplitNumericPrefix(folderName);
        return LegacySlugRegex().IsMatch(rest) ? ProjectLoader.ToDisplayTitle(rest) : rest;
    }

    /// <summary>Why <paramref name="name"/> can't be a section (folder) name — or null when it
    /// can. The name is shown exactly as typed, so characters Windows forbids are refused
    /// rather than silently changed.</summary>
    public static string? SectionNameProblem(string name)
    {
        var n = name.Trim();
        if (n.Length == 0)
            return "Give the section a name.";
        if (n.IndexOfAny(Forbidden) >= 0 || n.Any(char.IsControl))
            return $"Section names can't contain {ForbiddenList} — Windows doesn't allow them in folder names.";
        if (n.EndsWith('.'))
            return "Section names can't end with a dot.";
        if (n.Length > MaxNameLength)
            return $"Keep section names under {MaxNameLength} characters.";
        return null;
    }

    /// <summary>A file-name-safe form of a chapter title: forbidden characters become spaces,
    /// whitespace collapses, trailing dots and spaces go, length is capped. Never empty. The
    /// title itself (frontmatter + H1) keeps every character.</summary>
    public static string FileSafe(string title)
    {
        var sb = new StringBuilder(title.Length);
        foreach (var ch in title)
            sb.Append(Array.IndexOf(Forbidden, ch) >= 0 || char.IsControl(ch) ? ' ' : ch);
        var s = WhitespaceRegex().Replace(sb.ToString(), " ").Trim().TrimEnd('.', ' ');
        if (s.Length > MaxNameLength)
            s = s[..MaxNameLength].TrimEnd('.', ' ');
        return s.Length == 0 ? "chapter" : s;
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex LegacySlugRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
