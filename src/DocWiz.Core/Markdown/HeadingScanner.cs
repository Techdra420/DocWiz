using System.Text.RegularExpressions;

namespace DocWiz.Core.Markdown;

/// <summary>A heading found in a chapter body. Index counts headings in document order;
/// Line is the body line number (for in-place edits like anchor insertion).</summary>
public sealed record ScannedHeading(int Index, int Level, string Text, string? Anchor, int Line);

/// <summary>
/// Text-level heading scanning over a chapter body (code fences skipped). Used by the
/// editor's section picker and anchor auto-creation; text-based (not AST) on purpose so
/// Line/column positions map directly onto the file for in-place edits.
/// </summary>
public static partial class HeadingScanner
{
    public static List<ScannedHeading> Scan(string body)
    {
        var lines = body.Replace("\r\n", "\n").Split('\n');
        var result = new List<ScannedHeading>();
        var inFence = false;
        var index = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence)
                continue;
            var m = HeadingRegex().Match(line);
            if (!m.Success)
                continue;
            var raw = m.Groups[2].Value;
            var anchor = AnchorRegex().Match(raw);
            var text = AnchorRegex().Replace(raw, "").Trim();
            result.Add(new ScannedHeading(index++, m.Groups[1].Length, text,
                anchor.Success ? anchor.Groups[1].Value : null, i));
        }
        return result;
    }

    /// <summary>"Daily Startup & Checks" → "daily-startup-checks"; "Čištění skeneru" →
    /// "cisteni-skeneru" (accented Latin letters keep their base letter instead of vanishing);
    /// never empty.</summary>
    public static string Slug(string text)
    {
        var slug = NonSlugRegex().Replace(FoldToAscii(text).ToLowerInvariant(), "-").Trim('-');
        return slug.Length == 0 ? "section" : slug;
    }

    /// <summary>Letters that don't decompose into base + accent.</summary>
    private static readonly Dictionary<char, string> Unfoldable = new()
    {
        ['ß'] = "ss", ['ł'] = "l", ['Ł'] = "L", ['ø'] = "o", ['Ø'] = "O", ['æ'] = "ae", ['Æ'] = "AE",
        ['œ'] = "oe", ['Œ'] = "OE", ['đ'] = "d", ['Đ'] = "D", ['ð'] = "d", ['þ'] = "th",
    };

    private static string FoldToAscii(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var c in text.Normalize(System.Text.NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark)
                continue;
            if (Unfoldable.TryGetValue(c, out var s)) sb.Append(s);
            else sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>First free anchor name: base, base-2, base-3, …</summary>
    public static string UniqueAnchor(string baseSlug, ISet<string> taken)
    {
        if (!taken.Contains(baseSlug))
            return baseSlug;
        for (var i = 2; ; i++)
        {
            if (!taken.Contains($"{baseSlug}-{i}"))
                return $"{baseSlug}-{i}";
        }
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.*)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"\s*\{#([^}]+)\}\s*$")]
    private static partial Regex AnchorRegex();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugRegex();
}
