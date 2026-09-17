using DocWiz.App.Import;

namespace DocWiz.Cli;

/// <summary>
/// `docwiz import-docx` (experimental): converts a Word document into DocWiz chapters.
/// A thin console wrapper over the shared two-phase <see cref="DocxImporter"/> — the CLI
/// has no interactive cherry-pick, so it imports every detected section.
/// </summary>
public static class DocxImport
{
    public static int Run(string docxPath, string projectPath, string section, string? discipline)
    {
        docxPath = Path.GetFullPath(docxPath);
        projectPath = Path.GetFullPath(projectPath);
        if (!File.Exists(docxPath))
        {
            Console.Error.WriteLine($"ERROR file not found: {docxPath}");
            return 1;
        }

        Console.WriteLine($"Converting {Path.GetFileName(docxPath)} with pandoc…");
        ImportPreview preview;
        using (var fs = File.OpenRead(docxPath))
            preview = DocxImporter.Preview(fs, Path.GetFileName(docxPath));

        if (preview.Token is null)
        {
            Console.Error.WriteLine("ERROR " + (preview.Error ?? "conversion failed."));
            return 1;
        }

        // The CLI default discipline mirrors the old behaviour (section name); the editor
        // path defaults to None. Audiences = null → all of the project's audiences.
        var allSlugs = preview.Sections.Select(s => s.Slug).ToList();
        var defaults = new ImportDefaults(discipline ?? SectionSlug(section), Audiences: null);
        var outcome = DocxImporter.Commit(preview.Token, projectPath, allSlugs, section, defaults, EmfConverter.Convert);

        if (outcome.Error is not null)
        {
            Console.Error.WriteLine("ERROR " + outcome.Error);
            return 1;
        }

        foreach (var ch in outcome.ChaptersCreated)
            Console.WriteLine($"  chapter  {ch.File}");
        foreach (var line in outcome.Report)
            Console.WriteLine($"  REVIEW   {line}");
        foreach (var issue in outcome.Issues)
            Console.WriteLine($"  {issue.Severity.ToUpperInvariant(),-7} {issue.Source}: {issue.Message}");

        var hasErrors = outcome.Issues.Any(i => string.Equals(i.Severity, "Error", StringComparison.OrdinalIgnoreCase));
        Console.WriteLine(hasErrors
            ? "Import finished WITH ERRORS — fix them before building."
            : $"Import finished: {outcome.ChaptersCreated.Count} chapter(s), " +
              $"{outcome.Report.Count} item(s) to review. Open the editor to polish.");
        return 0;
    }

    /// <summary>"020-setup" → "setup" (the CLI's default discipline).</summary>
    private static string SectionSlug(string section)
    {
        var dash = section.IndexOf('-');
        return dash > 0 && section[..dash].All(char.IsDigit) ? section[(dash + 1)..] : section;
    }
}
