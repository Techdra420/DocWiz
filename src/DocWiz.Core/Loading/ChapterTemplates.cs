namespace DocWiz.Core.Loading;

/// <summary>Per-discipline chapter body skeletons. Used by the editor's new-chapter
/// flow and by project templates — the structure guides non-programmer authors.</summary>
public static class ChapterTemplates
{
    public static string Body(string discipline) => discipline switch
    {
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
    public static string ChapterFile(string id, string title, string discipline, IEnumerable<string> audiences) =>
        ChapterComposer.Compose(
            ChapterComposer.Frontmatter(id, title, discipline, audiences),
            $"# {title}\n\n{Body(discipline)}");

    /// <summary>Single-quoted YAML scalar (safe for any content; inner ' doubled).</summary>
    public static string YamlQuote(string value) => "'" + value.Replace("'", "''") + "'";
}
