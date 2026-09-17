using DocWiz.Core.Models;

namespace DocWiz.Core.Loading;

public sealed record ProjectTemplateInfo(string Id, string Title, string Description);

/// <summary>
/// Project starting points offered by the welcome screen. Every template must produce a
/// project that passes `docwiz check` with zero errors. A richer, file-based
/// template library is discovered separately — this is the basic built-in set.
/// </summary>
public static class ProjectTemplates
{
    public static readonly IReadOnlyList<ProjectTemplateInfo> All =
    [
        new("blank", "Blank project",
            "Manifest, one introduction chapter, one user-training view. The minimal valid start."),
        new("software-handbook", "Software handbook",
            "Standard product documentation skeleton: overview, setup, operations, " +
            "troubleshooting, and integration chapters plus the three audience views."),
    ];

    private static readonly string[] AllAudiences = ["user", "support", "admin"];

    public static void Apply(string root, string templateId)
    {
        switch (templateId)
        {
            case "blank":
                ProjectScaffolder.CreateNew(root);
                break;
            case "software-handbook":
                CreateSoftwareHandbook(root);
                break;
            default:
                throw new DocWizLoadException($"Unknown project template '{templateId}'.");
        }
    }

    private static void CreateSoftwareHandbook(string root)
    {
        // Reuses the blank scaffold for the manifest + folder skeleton, then replaces
        // the starter content with the full handbook structure.
        ProjectScaffolder.CreateNew(root);
        File.Delete(Path.Combine(root, ProjectLayout.ContentFolder, "010-overview", "010-introduction.md"));
        File.Delete(Path.Combine(root, ProjectLayout.ViewsFolder, "user-training.yaml"));

        WriteChapter(root, "010-overview", "010-system-overview.md",
            "system-overview", "System Overview", "overview", AllAudiences);
        WriteChapter(root, "020-setup", "010-installation.md",
            "installation", "Installation & Configuration", "setup", ["support", "admin"]);
        WriteChapter(root, "030-operations", "010-daily-operations.md",
            "daily-operations", "Daily Operations", "operations", AllAudiences);
        WriteChapter(root, "040-troubleshooting", "010-troubleshooting.md",
            "troubleshooting", "Troubleshooting", "troubleshooting", ["support", "admin"]);
        WriteChapter(root, "050-reference", "010-integration-reference.md",
            "integration-reference", "Integration Reference", "integration", ["admin"]);

        WriteView(root, "user-training.yaml",
            """
            id: user-training
            title: User Training
            audience: user
            mode: slides
            slides:
              - include: system-overview#purpose
              - include: daily-operations#daily-tasks
              - markdown: |
                  ## Questions?
                  Contact your support team.
            """);
        WriteView(root, "support-quickref.yaml",
            """
            id: support-quickref
            title: Support Quick Reference
            audience: support
            mode: document
            slides:
              - include: troubleshooting#common-problems
              - include: troubleshooting#getting-help
              - include: installation#configuration
            """);
        WriteView(root, "admin-guide.yaml",
            """
            id: admin-guide
            title: Administrator Guide
            audience: admin
            mode: document
            slides:
              - include: system-overview
              - include: installation
              - include: daily-operations
              - include: troubleshooting
              - include: integration-reference
            """);
    }

    private static void WriteChapter(string root, string section, string fileName,
        string id, string title, string discipline, string[] audiences)
    {
        var dir = Path.Combine(root, ProjectLayout.ContentFolder, section);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName),
            ChapterTemplates.ChapterFile(id, title, discipline, audiences));
    }

    private static void WriteView(string root, string fileName, string yaml) =>
        File.WriteAllText(Path.Combine(root, ProjectLayout.ViewsFolder, fileName), yaml);
}
