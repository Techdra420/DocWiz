using DocWiz.Core.Models;

namespace DocWiz.Core.Loading;

/// <summary>
/// Creates a new, valid DocWiz project skeleton (`docwiz new`). Also the base the
/// Stage 3 editor's "new project" flow will reuse. The generated project must always
/// pass `docwiz check` with zero errors.
/// </summary>
public static class ProjectScaffolder
{
    public static void CreateNew(string root)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new DocWizLoadException($"Folder '{root}' already exists and is not empty.");

        var name = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        Directory.CreateDirectory(Path.Combine(root, ProjectLayout.ContentFolder, "010-overview"));
        Directory.CreateDirectory(Path.Combine(root, ProjectLayout.ViewsFolder));
        Directory.CreateDirectory(Path.Combine(root, ProjectLayout.AssetsFolder));
        Directory.CreateDirectory(Path.Combine(root, ProjectLayout.AttachmentsFolder));

        File.WriteAllText(Path.Combine(root, ProjectLayout.ManifestFileName),
            $"""
            project: {ChapterTemplates.YamlQuote(name)}
            code: PROJECT-001
            customer: Customer Name
            version: 0.1.0
            language: en

            audiences:
              - id: user
                title: User
              - id: support
                title: Support
              - id: admin
                title: Administrator

            external:
              # Paths to self-contained HTML documentation folders (each must have an
              # index.html or handover.html entry page). Every non-null entry is embedded with its own tab.
              api_reference: null
              release_notes: null
              integration_guide: null
            """);

        File.WriteAllText(Path.Combine(root, ProjectLayout.ContentFolder, "010-overview", "010-introduction.md"),
            ChapterComposer.Compose(
                ChapterComposer.Frontmatter("introduction", "Introduction", "overview",
                    ["user", "support", "admin"]),
                """
                # Introduction

                ## What this system does {#what-it-does}

                Describe the system in two or three sentences. Plain language — this
                section is transcluded into the user training.

                ::: audience admin
                Technical depth goes into audience blocks like this one — visible in the
                manual (tagged) and in the administrator view, hidden from users.
                :::

                ## Scope of this documentation {#scope}

                What is covered, what is delivered separately. Link other chapters with
                stable IDs, e.g. `[see Introduction](dw:introduction#what-it-does)`.
                """));

        File.WriteAllText(Path.Combine(root, ProjectLayout.ViewsFolder, "user-training.yaml"),
            """
            id: user-training
            title: User Training
            audience: user
            mode: slides
            slides:
              - include: introduction#what-it-does
              - markdown: |
                  ## Questions?
                  Contact your support team.
            """);
    }
}
