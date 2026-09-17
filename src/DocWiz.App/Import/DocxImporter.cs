using System.Collections.Concurrent;
using System.Diagnostics;
using DocWiz.Core.Loading;
using DocWiz.Core.Markdown;
using DocWiz.Core.Models;
using DocWiz.Core.Validation;

namespace DocWiz.App.Import;

/// <summary>One detected section in an import preview — a chapter-to-be, with light stats
/// so the author can judge what to keep. <see cref="LooksLikeFrontMatter"/> marks the
/// title-page/TOC chapter the converter quarantines (usually skipped).</summary>
public sealed record ImportSection(
    string Slug, string Title, int Paragraphs, int Tables, int Images, bool LooksLikeFrontMatter);

/// <summary>Result of the preview phase. <see cref="Token"/> identifies the staged
/// conversion for a later <see cref="DocxImporter.Commit"/>; null when nothing was staged
/// (Pandoc missing or conversion failed — see <see cref="Error"/>).</summary>
public sealed record ImportPreview(
    bool PandocFound, string? Token, IReadOnlyList<ImportSection> Sections,
    IReadOnlyList<string> Report, string? Error);

public sealed record ImportedChapterInfo(string Id, string Title, string File);

public sealed record ImportIssue(string Severity, string Source, string Message);

/// <summary>Result of the commit phase: what was written, what to review, and the
/// post-import validation issues. <see cref="Error"/> non-null means nothing was written.</summary>
public sealed record ImportOutcome(
    IReadOnlyList<ImportedChapterInfo> ChaptersCreated, IReadOnlyList<string> Report,
    IReadOnlyList<ImportIssue> Issues, string? Error);

/// <summary>Frontmatter defaults applied to every imported chapter. Null
/// <see cref="Audiences"/> = all of the project's audiences (the safe default — empty
/// audiences would hide the chapter from every view). Discipline is a pure authoring label
/// (inert in the build), so "" (None) is the harmless default.</summary>
public sealed record ImportDefaults(string Discipline, IReadOnlyList<string>? Audiences);

/// <summary>
/// Two-phase Word import. Pandoc does the raw docx→gfm work (external, authoring-time only),
/// <see cref="MarkdownImport"/> makes the result valid chapters + a review report, and this
/// orchestrates the rest. <b>Preview</b> converts into a temp staging dir and returns the
/// detected sections WITHOUT touching the project; <b>Commit</b> writes only the chosen
/// sections. The split is what lets the editor cherry-pick before anything lands (and is the
/// "what would I get" half a future revision-reconcile feature would reuse).
/// </summary>
public static class DocxImporter
{
    private sealed record Staged(string Dir, ImportResult Result, DateTime Created);

    // Single-session localhost editor, so a process-wide registry is fine. Keyed by the
    // token handed to the client; evicted on commit/cancel and by a stale-sweep.
    private static readonly ConcurrentDictionary<string, Staged> Registry = new();
    private static readonly TimeSpan StagingTtl = TimeSpan.FromHours(2);

    private const string PandocMissing =
        "pandoc.exe not found. The published DocWiz bundles it; in a dev checkout, fetch the " +
        "portable zip into tools\\pandoc\\ (see tools/README.md) or put pandoc on PATH.";

    /// <summary>Convert a .docx into a staged set of sections — no project writes. The
    /// returned <see cref="ImportPreview.Token"/> feeds <see cref="Commit"/>.</summary>
    public static ImportPreview Preview(Stream docx, string fileName)
    {
        SweepStale();

        var pandoc = FindPandoc();
        if (pandoc is null)
            return new ImportPreview(false, null, [], [], PandocMissing);

        var stagingDir = Path.Combine(Path.GetTempPath(), "docwiz-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDir);
        try
        {
            var docxPath = Path.Combine(stagingDir, "source.docx");
            using (var fs = File.Create(docxPath))
                docx.CopyTo(fs);

            var (ok, stderr) = RunPandoc(pandoc, stagingDir, docxPath);
            if (!ok)
            {
                TryDelete(stagingDir);
                return new ImportPreview(true, null, [], [], $"Pandoc could not read this file: {stderr}");
            }

            var markdown = File.ReadAllText(Path.Combine(stagingDir, "out.md"));
            var result = MarkdownImport.Convert(markdown, Path.GetFileNameWithoutExtension(fileName));

            var token = Guid.NewGuid().ToString("N");
            Registry[token] = new Staged(stagingDir, result, DateTime.UtcNow);

            var sections = result.Chapters.Select(ToSection).ToList();
            return new ImportPreview(true, token, sections, result.Report, null);
        }
        catch
        {
            TryDelete(stagingDir);
            throw;
        }
    }

    /// <summary>Drop a staged import without committing (dialog cancelled).</summary>
    public static void Cancel(string token)
    {
        if (Registry.TryRemove(token, out var staged))
            TryDelete(staged.Dir);
    }

    /// <summary>Write the selected sections of a staged import into the project. Consumes
    /// the staging (one commit per preview).</summary>
    public static ImportOutcome Commit(
        string token, string projectRoot, IReadOnlyList<string> selectedSlugs,
        string section, ImportDefaults defaults, EmfToPng? convert = null)
    {
        if (!Registry.TryRemove(token, out var staged))
            return new ImportOutcome([], [], [], "This import has expired — start the import again.");
        try
        {
            return CommitStaged(staged.Result, staged.Dir, projectRoot, selectedSlugs, section, defaults, convert);
        }
        finally
        {
            TryDelete(staged.Dir);
        }
    }

    /// <summary>The commit core, separated from token/staging plumbing so it is testable
    /// without Pandoc: feed it a <see cref="MarkdownImport.Convert"/> result directly.</summary>
    internal static ImportOutcome CommitStaged(
        ImportResult result, string stagingDir, string projectRoot,
        IReadOnlyList<string> selectedSlugs, string section, ImportDefaults defaults,
        EmfToPng? convert = null)
    {
        DocProject project;
        try
        {
            project = ProjectLoader.Load(projectRoot);
        }
        catch (DocWizLoadException ex)
        {
            return new ImportOutcome([], [], [], ex.Message);
        }

        var audiences = defaults.Audiences is { Count: > 0 } a
            ? a
            : project.Manifest.Audiences.Select(au => au.Id).ToList();

        var selected = new HashSet<string>(selectedSlugs, StringComparer.Ordinal);
        var takenIds = project.Chapters.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var mediaDir = Path.Combine(stagingDir, "media");
        var created = new List<ImportedChapterInfo>();
        var report = new List<string>();
        var convertedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // "image1.emf"

        foreach (var chapter in result.Chapters)
        {
            if (!selected.Contains(chapter.Slug))
                continue;

            var id = HeadingScanner.UniqueAnchor(chapter.Slug, takenIds);
            takenIds.Add(id);

            // Convert this chapter's EMF/WMF media first (so the body we write links the PNG).
            var renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // emf name -> png name
            if (convert is not null)
            {
                foreach (var media in chapter.MediaFiles)
                {
                    if (!EmfRetrofit.HasMetafileExtension(media))
                        continue;
                    var src = Path.Combine(mediaDir, media);
                    if (!File.Exists(src))
                        continue;
                    var pngName = Path.ChangeExtension(media, ".png");
                    if (convert(src, Path.Combine(mediaDir, pngName)))
                    {
                        renamed[media] = pngName;
                        convertedNames.Add(media);
                    }
                }
            }

            var body = renamed.Count == 0
                ? chapter.Body
                : EmfRetrofit.RewriteEmfLinks(chapter.Body, url => renamed.ContainsKey(Path.GetFileName(url)));

            var path = ChapterFactory.CreateImported(
                projectRoot, section, id, chapter.Title, defaults.Discipline, audiences, body);
            created.Add(new ImportedChapterInfo(id, chapter.Title, RelPath(projectRoot, path)));

            var assetDir = Path.Combine(projectRoot, ProjectLayout.AssetsFolder, chapter.Slug);
            foreach (var media in chapter.MediaFiles)
            {
                // Copy the converted PNG for metafiles that converted; otherwise the file as-is.
                var name = renamed.TryGetValue(media, out var png) ? png : media;
                var src = Path.Combine(mediaDir, name);
                if (!File.Exists(src))
                {
                    report.Add($"{chapter.Slug}: media '{media}' was referenced but not extracted.");
                    continue;
                }
                Directory.CreateDirectory(assetDir);
                File.Copy(src, Path.Combine(assetDir, name), overwrite: true);
            }
        }

        // Carry over review items for imported sections — but drop the stale "vector graphic"
        // advisory for files we converted (it was generated at Preview, before conversion).
        report.AddRange(result.Report.Where(line =>
            ReportAppliesTo(line, selected, result) && !IsConvertedAdvisory(line, convertedNames)));
        if (convertedNames.Count > 0)
            report.Add($"converted {convertedNames.Count} EMF/WMF image(s) to PNG.");

        // Validate the project as imported — same gate as `docwiz check`.
        var reloaded = ProjectLoader.Load(projectRoot);
        DocWizMarkdown.ParseAll(reloaded, DocWizMarkdown.CreatePipeline());
        var issues = ProjectValidator.Validate(reloaded)
            .OrderByDescending(i => i.Severity)
            .Select(i => new ImportIssue(i.Severity.ToString(), i.Source, i.Message))
            .ToList();

        return new ImportOutcome(created, report, issues, null);
    }

    /// <summary>Bundled-first Pandoc discovery: beside the published exe, then PATH, then the
    /// dev tools/pandoc walk. Bundled-first pins behaviour (a different Pandoc on PATH could
    /// change conversion output). NOTE: the bundled copy sits beside the REAL exe, so resolve
    /// via Environment.ProcessPath — under single-file publish AppContext.BaseDirectory is the
    /// temp extraction dir, not dist\ (same gotcha as TemplatesLocation/AppWindow).</summary>
    public static string? FindPandoc()
    {
        var exeDir = Environment.ProcessPath is { } exe ? Path.GetDirectoryName(exe) : null;
        if (!string.IsNullOrEmpty(exeDir))
        {
            var bundled = Path.Combine(exeDir, "pandoc", "pandoc.exe");
            if (File.Exists(bundled))
                return bundled;
        }

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;
            try
            {
                var candidate = Path.Combine(dir.Trim(), "pandoc.exe");
                if (File.Exists(candidate))
                    return candidate;
            }
            catch
            {
                // unparseable PATH entry — ignore
            }
        }

        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = start; dir is not null; dir = Path.GetDirectoryName(dir))
            {
                var toolsDir = Path.Combine(dir, "tools", "pandoc");
                if (!Directory.Exists(toolsDir))
                    continue;
                var found = Directory.GetFiles(toolsDir, "pandoc.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (found is not null)
                    return found;
            }
        }
        return null;
    }

    private static (bool Ok, string Stderr) RunPandoc(string pandoc, string workDir, string docxPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = pandoc,
            WorkingDirectory = workDir,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // --wrap=none keeps paragraphs on single lines (the transforms are line-based);
        // --extract-media=. puts images under <workDir>\media\.
        foreach (var a in new[] { "-f", "docx", "-t", "gfm", "--wrap=none", "--extract-media=.", "-o", "out.md", docxPath })
            psi.ArgumentList.Add(a);
        using var proc = Process.Start(psi)!;
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        return (proc.ExitCode == 0, stderr.Trim());
    }

    private static ImportSection ToSection(ImportedChapter ch)
    {
        var lines = ch.Body.Replace("\r\n", "\n").Split('\n');
        var tables = lines.Count(l => l.TrimStart().StartsWith("|---", StringComparison.Ordinal));
        var paragraphs = lines.Count(l =>
        {
            var t = l.Trim();
            return t.Length > 0
                && !t.StartsWith('#') && !t.StartsWith('|')
                && !t.StartsWith(":::", StringComparison.Ordinal) && !t.StartsWith("![", StringComparison.Ordinal);
        });
        return new ImportSection(ch.Slug, ch.Title, paragraphs, tables, ch.MediaFiles.Count,
            ch.Title == "Front matter (review)");
    }

    private static bool ReportAppliesTo(string line, HashSet<string> selectedSlugs, ImportResult result)
    {
        // The global front-matter advisory has no slug prefix; keep it only if that chapter
        // was imported. Everything else is "<slug>: …".
        if (line.StartsWith("front-matter:", StringComparison.Ordinal))
            return result.Chapters.Any(c => c.Title == "Front matter (review)" && selectedSlugs.Contains(c.Slug));
        var colon = line.IndexOf(':');
        return colon <= 0 || selectedSlugs.Contains(line[..colon]);
    }

    /// <summary>True if <paramref name="line"/> is the Preview EMF/WMF advisory
    /// (MarkdownImport) for a file we converted — matched by the quoted '&lt;file&gt;' it names.
    /// Couples to that wording by design (see the spec's report-handling note).</summary>
    private static bool IsConvertedAdvisory(string line, HashSet<string> convertedNames)
    {
        if (!line.Contains("is a Word vector graphic", StringComparison.Ordinal))
            return false;
        return convertedNames.Any(name => line.Contains($"'{name}'", StringComparison.OrdinalIgnoreCase));
    }

    private static string RelPath(string root, string full) =>
        Path.GetRelativePath(root, full).Replace('\\', '/');

    private static void SweepStale()
    {
        var cutoff = DateTime.UtcNow - StagingTtl;
        foreach (var kv in Registry)
            if (kv.Value.Created < cutoff && Registry.TryRemove(kv.Key, out var s))
                TryDelete(s.Dir);
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch { /* temp dir, best effort */ }
    }
}
