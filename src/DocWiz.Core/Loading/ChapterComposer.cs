using System.Text;
using YamlDotNet.Serialization;

namespace DocWiz.Core.Loading;

/// <summary>
/// The ONE chapter-file composer (ADR-008). Frontmatter is serialized as canonical block-style
/// YAML (YamlDotNet, minimal quoting), LF throughout, and the body is appended with exactly one
/// trailing newline. Every chapter-creation path (<see cref="ChapterTemplates"/>,
/// <c>ChapterFactory</c>, <see cref="ProjectScaffolder"/>) AND the editor's save
/// (<c>DocWiz.App.ChapterYaml.Compose</c>, which delegates here) emit identical bytes, so a
/// freshly created chapter never restyles on its first save (the "frontmatter restyles on save"
/// the L1 fidelity scan used to count).
/// </summary>
public static class ChapterComposer
{
    private static readonly ISerializer Serializer = new SerializerBuilder().Build();

    /// <summary>Compose a chapter file from its frontmatter map and body.</summary>
    public static string Compose(IDictionary<string, object?> frontmatter, string body)
    {
        // LF throughout, deterministically: AppendLine / YamlDotNet would follow the platform
        // and churn diffs on Windows (mixed EOLs) — found by the fidelity harness (live-editor M0).
        var sb = new StringBuilder();
        sb.Append("---\n");
        sb.Append(Serializer.Serialize(frontmatter).Replace("\r\n", "\n"));
        sb.Append("---\n\n");
        sb.Append(body.Replace("\r\n", "\n").TrimEnd('\n'));
        sb.Append('\n');
        return sb.ToString();
    }

    /// <summary>The known chapter frontmatter fields in canonical order, ready for
    /// <see cref="Compose"/>: id/title/discipline as plain scalars, audiences a block sequence.</summary>
    public static Dictionary<string, object?> Frontmatter(
        string id, string title, string discipline, IEnumerable<string> audiences) =>
        new()
        {
            ["id"] = id,
            ["title"] = title,
            ["discipline"] = discipline,
            ["audiences"] = audiences.ToList(),
        };
}
