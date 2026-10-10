namespace DocWiz.Core.Loading;

/// <summary>Per-discipline chapter body skeletons. Used by the editor's new-chapter
/// flow and by project templates — the structure guides non-programmer authors.</summary>
public static partial class ChapterTemplates
{
    private const string AdminNoteAudience = "admin";

    /// <summary>The skeleton for a discipline. Some skeletons carry a sample
    /// `::: audience admin` block; it is kept only when <paramref name="includeAdminNote"/> —
    /// i.e. the chapter is tagged for an `admin` audience — so a new chapter never names an
    /// audience the chapter (or project) doesn't have.</summary>
    public static string Body(string discipline, bool includeAdminNote = true)
    {
        var body = Skeleton(discipline);
        return includeAdminNote ? body : AdminNoteRegex().Replace(body, "");
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"(\r?\n)+::: audience admin\r?\n[\s\S]*?\r?\n:::")]
    private static partial System.Text.RegularExpressions.Regex AdminNoteRegex();

    private static string Skeleton(string discipline) => discipline switch
    {
        "blank" => "",
        "setup" =>
            """
            ## Requirements {#requirements}

            | Component | Minimum | Notes |
            |-----------|---------|-------|
            |           |         |       |

            ## Installation steps {#installation}

            1. Step one.
            2. Step two.

            ## Configuration {#configuration}

            | Setting | Default | Description |
            |---------|---------|-------------|
            |         |         |             |
            """,
        "operations" =>
            """
            ## Daily tasks {#daily-tasks}

            Describe the routine tasks in the order a user performs them.

            ## Procedures {#procedures}

            | Task | Where | Notes |
            |------|-------|-------|
            |      |       |       |
            """,
        "troubleshooting" =>
            """
            ## Common problems {#common-problems}

            | Symptom | Likely cause | Action |
            |---------|--------------|--------|
            |         |              |        |

            ## Getting help {#getting-help}

            Who to contact and what to include in a support request.

            ::: audience admin
            Log locations, diagnostic commands, escalation path.
            :::
            """,
        "reference" =>
            """
            ## Overview {#overview}

            What this reference covers.

            ## Details {#details}

            | Item | Type | Description |
            |------|------|-------------|
            |      |      |             |
            """,
        "integration" =>
            """
            ## Endpoints {#endpoints}

            | Endpoint | Method | Purpose |
            |----------|--------|---------|
            |          |        |         |

            ## Data exchange {#data-exchange}

            Import and export formats, schedules, and error handling.

            ::: audience admin
            Credentials, network requirements, and integration logs.
            :::
            """,
        _ =>
            """
            ## Purpose {#purpose}

            What this chapter covers and for whom.

            ::: audience admin
            Technical depth goes into audience blocks like this one.
            :::
            """,
    };

    /// <summary>Complete chapter file (frontmatter + H1 + body skeleton) in the canonical form
    /// (ADR-008): frontmatter via <see cref="ChapterComposer"/> (block-style YAML, minimal
    /// quoting handled by YamlDotNet), so a created chapter is byte-stable on its first editor
    /// save.</summary>
    public static string ChapterFile(string id, string title, string discipline, IEnumerable<string> audiences)
    {
        var list = audiences.ToList();
        return ChapterComposer.Compose(
            ChapterComposer.Frontmatter(id, title, discipline, list),
            $"# {title}\n\n{Body(discipline, list.Contains(AdminNoteAudience))}");
    }

    /// <summary>Single-quoted YAML scalar (safe for any content; inner ' doubled).</summary>
    public static string YamlQuote(string value) => "'" + value.Replace("'", "''") + "'";
}
