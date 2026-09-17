using DocWiz.Build;
using DocWiz.Cli;
using DocWiz.Core.Loading;
using DocWiz.Core.Markdown;
using DocWiz.Core.Models;
using DocWiz.Core.Slides;
using DocWiz.Core.Validation;

// Per-Monitor-V2 DPI awareness for the WHOLE process, set before any window (splash, native
// folder picker, or the WebView2 app window) is created. With no awareness declared, the
// borderless app window's taskbar-aware maximize math (AppWindow.WndProc / WM_GETMINMAXINFO,
// which assumes physical per-monitor pixels) can resolve to a size larger than the work area
// on a scaled or multi-monitor desktop — the "maximized window does not fit my screen" report,
// which surfaces when the window is moved between differently-scaled screens. PerMonitorV2 makes
// GetMonitorInfo return true per-monitor pixels and lets WinForms rescale across a DPI boundary.
// Harmless for the headless terminal verbs (they never create a window).
Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

// Hidden helper: show the native folder dialog and print the path. Runs as its own
// short-lived process (see FolderPick) so the dialog gets foreground rights.
if (args is ["__pickfolder", ..])
    return FolderPick.RunHelper(args);

ConsoleInterop.TryAttachParentConsole();

// The stakeholder's editor lives on this port; the fidelity runners deliberately
// use other ports and refuse to kill anything listening here.
const int DefaultEditorPort = 5210;

// Double-clicking the exe (no arguments) is the non-programmer entry point:
// behave exactly like `docwiz edit` — welcome screen, browser, no terminal.
if (args.Length == 0)
    args = ["edit"];

if (args is ["edit", .. var editRest])
{
    string? projectPath = null;
    var port = DefaultEditorPort;
    var openBrowser = true;
    for (var i = 0; i < editRest.Length; i++)
    {
        if (editRest[i] == "--port" && i + 1 < editRest.Length && int.TryParse(editRest[i + 1], out var parsed))
        {
            port = parsed;
            i++;
        }
        else if (editRest[i] == "--no-browser")
        {
            openBrowser = false;
        }
        else if (!editRest[i].StartsWith('-'))
        {
            projectPath ??= editRest[i];
        }
    }

    // No path → the welcome screen handles open/new/recents fully in the browser.
    // Explicit path (a terminal flow): empty folder → scaffold; anything else
    // that is not a project → console error.
    if (projectPath is not null && !File.Exists(Path.Combine(projectPath, ProjectLayout.ManifestFileName)))
    {
        var isEmptyFolder = Directory.Exists(projectPath)
                            && !Directory.EnumerateFileSystemEntries(projectPath).Any();
        if (isEmptyFolder)
        {
            ProjectScaffolder.CreateNew(projectPath);
            Console.WriteLine($"'{projectPath}' was empty — created a new DocWiz project in it.");
        }
        else
        {
            Console.Error.WriteLine($"ERROR '{projectPath}' is not a DocWiz project (docwiz.yaml not found).");
            return 1;
        }
    }

    // Instant feedback for the double-click flow: a splash appears at once and is
    // dismissed when the editor window is actually up (see Splash / onUiLaunched). Only
    // for the browser/app-window flow — never under --no-browser (tests/headless).
    // DOCWIZ_NO_SPLASH=1 disables it (escape hatch / isolation when diagnosing launch issues).
    var splashEnabled = openBrowser
        && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCWIZ_NO_SPLASH"));
    var splash = splashEnabled ? Splash.Show() : null;
    try
    {
        return await DocWiz.App.EditorServer.RunAsync(
            projectPath, port, openBrowser, FolderPick.PickViaHelper,
            splash is null ? null : splash.Close,
            AppWindow.TryLaunch,
            EmfConverter.Convert,
            ImageRotator.Rotate);
    }
    finally
    {
        splash?.Close(); // safety net: never let the splash linger on any exit path
    }
}

return args switch
{
    ["new", var path] => NewProject(path, null),
    ["new", var path, "--template", var tpl] => NewProject(path, tpl),
    ["check", var path] => Check(path),
    ["build", var path] => BuildHandover(path, null, zip: false),
    ["build", var path, "--zip"] => BuildHandover(path, null, zip: true),
    ["build", var path, "-o", var outDir] => BuildHandover(path, outDir, zip: false),
    ["build", var path, "-o", var outDir, "--zip"] => BuildHandover(path, outDir, zip: true),
    ["export-view", var path, var viewId] => ExportView(path, viewId, null),
    ["export-view", var path, var viewId, "-o", var outFile] => ExportView(path, viewId, outFile),
    ["export-pdf", var path] => ExportPdf(path, null, all: false),
    ["export-pdf", var path, "--all"] => ExportPdf(path, null, all: true),
    ["export-pdf", var path, "-o", var outDir] => ExportPdf(path, outDir, all: false),
    ["export-pdf", var path, "-o", var outDir, "--all"] => ExportPdf(path, outDir, all: true),
    ["draft-view", var path, var chapter] => DraftView(path, chapter, null),
    ["draft-view", var path, var chapter, "-o", var outFile] => DraftView(path, chapter, outFile),
    ["import-docx", var file, var path, "--section", var section]
        => DocWiz.Cli.DocxImport.Run(file, path, section, null),
    ["import-docx", var file, var path, "--section", var section, "--discipline", var disc]
        => DocWiz.Cli.DocxImport.Run(file, path, section, disc),
    ["convert-emf", var path] => EmfConvertCommand.Run(path),
    // Hidden dev verb (live-editor fidelity harness, L1): read-only scan proving that
    // block segmentation partitions every chapter loss-free and that a no-edit save
    // would be diff-free. Not in Usage() on purpose.
    ["fidelity", var path] => Fidelity(path),
    _ => Usage(),
};

static int Usage()
{
    Console.WriteLine(
        $"""
        DocWiz — audience-aware documentation for delivered systems

        Usage:
          docwiz                                        Double-click flow: folder picker,
                                                        then the editor in your browser
          docwiz new <folder> [--template <id>]         Scaffold a new DocWiz project
                                                        (--template blank|software-handbook)
          docwiz check <project-folder>                 Validate a DocWiz project
          docwiz build <project-folder> [-o <out-dir>] [--zip]
                                                        Generate the handover site
                                                        (default: <project>/output/handover;
                                                        --zip also packages it as one file)
          docwiz export-view <project-folder> <view-id> [-o <file.html>]
                                                        Export one view as a single
                                                        self-contained HTML file
          docwiz export-pdf <project-folder> [-o <dir>] [--all]
                                                        Export the manual (and all views
                                                        with --all) as PDF (default dir:
                                                        <project>/output/PDF_Export)
          docwiz draft-view <project-folder> <chapter-id> [-o <file.yaml>]
                                                        Draft a starting slides deck from a
                                                        chapter (linked ::: embed ::: blocks
                                                        you then trim live); writes
                                                        views/<id>.yaml unless -o is given
          docwiz import-docx <file.docx> <project-folder> --section <folder> [--discipline <d>]
                                                        EXPERIMENTAL: convert a Word
                                                        document into chapters (needs
                                                        pandoc — see tools/README.md)
          docwiz convert-emf <project-folder>           Convert Word vector images (EMF/WMF)
                                                        in a project to PNG (originals kept in
                                                        _emf-originals/)
          docwiz edit <project-folder> [--port <n>]     Open the editor app in the browser
                                                        (default port: {DefaultEditorPort})
        """);
    return 2;
}

// L1 fidelity scan (read-only): for every chapter — segmentation partition invariant,
// copy-splice identity, raw-block inventory, and a report of what today's save path
// (FrontmatterParser → ChapterYaml.Compose) would normalize (EOL, frontmatter style).
static int Fidelity(string path)
{
    try
    {
        var project = ProjectLoader.Load(path);
        var pipeline = DocWizMarkdown.CreatePipeline();
        // ADR-008 single source: the editor (ingest.js NATIVE) and this scan both derive
        // their block-type set from BlockSegmenter; BlockTypeParityTests pins ingest.js to it.
        var modeled = BlockSegmenter.ModeledBlockTypes;

        int partitionFail = 0, spliceFail = 0, rawTotal = 0, crlfFiles = 0, restyled = 0, bodyDiff = 0;
        var rawKinds = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var chapter in project.Chapters)
        {
            var original = File.ReadAllText(chapter.FilePath);
            var (yaml, rawBody) = FrontmatterParser.Split(original);
            // The editor edits the body exactly as /api/chapter serves it.
            var body = rawBody.TrimStart('\n');
            var rel = Path.GetRelativePath(project.Root, chapter.FilePath).Replace('\\', '/');
            var flags = new List<string>();

            var segments = BlockSegmenter.Segment(body, pipeline);
            var partitionOk = string.Concat(segments.Select(s => s.Source)) == body
                && segments.All(s => body.Substring(s.Start, s.End - s.Start) == s.Source)
                && (segments.Count == 0 || (segments[0].Start == 0 && segments[^1].End == body.Length));
            if (!partitionOk)
            {
                partitionFail++;
                flags.Add("PARTITION BROKEN");
            }

            var spliced = BlockSegmenter.Splice(body,
                segments.Select(s => (SpliceOp)new SpliceOp.Copy(s.Start, s.End)));
            if (spliced != body)
            {
                spliceFail++;
                flags.Add("SPLICE NOT IDENTITY");
            }

            var raw = segments.Where(s => s.Kind == SegmentKind.Block && !modeled.Contains(s.BlockType!)).ToList();
            foreach (var r in raw)
            {
                rawTotal++;
                rawKinds[r.BlockType!] = rawKinds.GetValueOrDefault(r.BlockType!) + 1;
                flags.Add($"raw:{r.BlockType}@{r.Start}");
            }

            if (original.Contains("\r\n", StringComparison.Ordinal))
            {
                crlfFiles++;
                flags.Add("CRLF (normalizes on first save)");
            }

            // What would a no-edit save write today?
            var composed = DocWiz.App.ChapterYaml.Compose(DocWiz.App.ChapterYaml.Parse(yaml), body);
            var normalizedOriginal = original.Replace("\r\n", "\n");
            if (composed != original)
            {
                if (composed == normalizedOriginal)
                {
                    // EOL alone — already counted via the CRLF flag.
                }
                else if (composed.EndsWith(body.Replace("\r\n", "\n").TrimEnd('\n') + "\n", StringComparison.Ordinal))
                {
                    restyled++;
                    flags.Add("frontmatter restyles on save");
                }
                else
                {
                    bodyDiff++;
                    flags.Add("BODY DIFF ON SAVE (investigate)");
                }
            }

            var blocks = segments.Count(s => s.Kind == SegmentKind.Block);
            Console.WriteLine($"  {rel}: {blocks} blocks" + (flags.Count > 0 ? " — " + string.Join("; ", flags) : " — clean"));
        }

        Console.WriteLine();
        Console.WriteLine($"{project.Manifest.Code}: {project.Chapters.Count} chapters scanned.");
        Console.WriteLine($"  partition broken: {partitionFail}   splice not identity: {spliceFail}   body-diff on save: {bodyDiff}");
        Console.WriteLine($"  raw (unmodeled) blocks: {rawTotal}" +
            (rawKinds.Count > 0 ? " (" + string.Join(", ", rawKinds.Select(kv => $"{kv.Key}×{kv.Value}")) + ")" : ""));
        Console.WriteLine($"  CRLF files: {crlfFiles}   frontmatter restyle on save: {restyled} (pre-existing YamlDotNet behavior)");
        return partitionFail + spliceFail + bodyDiff > 0 ? 1 : 0;
    }
    catch (DocWizLoadException ex)
    {
        Console.Error.WriteLine("ERROR " + ex.Message);
        return 1;
    }
}

static int NewProject(string path, string? templateId)
{
    try
    {
        // `ProjectTemplates` is the CLI scaffolding map (blank/software-handbook, code-defined),
        // distinct from the file-based editor template library. Emitting `software-handbook` here
        // is how its structure gets a foothold on disk (e.g. into the Templates\ folder).
        if (templateId is null)
            ProjectScaffolder.CreateNew(path);
        else
            ProjectTemplates.Apply(path, templateId);
        Console.WriteLine($"New DocWiz project created at {Path.GetFullPath(path)}");
        Console.WriteLine("Next: edit docwiz.yaml (project/code/customer), write chapters under content/,");
        Console.WriteLine($"then run: docwiz build {path}");
        return 0;
    }
    catch (DocWizLoadException ex)
    {
        Console.Error.WriteLine("ERROR " + ex.Message);
        return 1;
    }
}

static (DocProject Project, Markdig.MarkdownPipeline Pipeline, List<ValidationIssue> Issues) LoadAndValidate(string path)
{
    var project = ProjectLoader.Load(path);
    var pipeline = DocWizMarkdown.CreatePipeline();
    DocWizMarkdown.ParseAll(project, pipeline);
    return (project, pipeline, ProjectValidator.Validate(project));
}

static int Check(string path)
{
    try
    {
        var (project, _, issues) = LoadAndValidate(path);
        foreach (var issue in issues.OrderByDescending(i => i.Severity))
            Console.WriteLine("  " + issue);

        var errors = issues.Count(i => i.Severity == IssueSeverity.Error);
        var warnings = issues.Count - errors;
        Console.WriteLine($"{project.Manifest.Code}: {project.Chapters.Count} chapters, " +
                          $"{project.Views.Count} views — {errors} error(s), {warnings} warning(s).");
        return errors > 0 ? 1 : 0;
    }
    catch (DocWizLoadException ex)
    {
        Console.Error.WriteLine("ERROR " + ex.Message);
        return 1;
    }
}

static int BuildHandover(string path, string? outDirArg, bool zip)
{
    try
    {
        // The default output lives INSIDE the project (<project>/output/handover —
        // same place the editor builds to). A bare relative default would resolve
        // against the shell's working directory and silently scatter handovers.
        var outDir = outDirArg ?? Path.Combine(Path.GetFullPath(path), "output", "handover");
        var (project, pipeline, issues) = LoadAndValidate(path);
        foreach (var issue in issues.OrderByDescending(i => i.Severity))
            Console.WriteLine("  " + issue);

        if (issues.Any(i => i.Severity == IssueSeverity.Error))
        {
            Console.Error.WriteLine("Build aborted: fix the errors above (see 'docwiz check').");
            return 1;
        }

        var result = HandoverBuilder.Build(project, pipeline, outDir);
        Console.WriteLine($"Handover written to {result}");
        Console.WriteLine($"Open {Path.Combine(result, HandoverBuilder.EntryFileName(project.Manifest))} in a browser.");

        if (zip)
        {
            var zipName = Packager.ZipName(project.Manifest.Code, project.Manifest.Version);
            var zipPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outDir))!, zipName);
            Packager.Zip(result, zipPath);
            Console.WriteLine($"Package written to {zipPath}");
        }
        return 0;
    }
    catch (Exception ex) when (ex is DocWizLoadException or DocWizBuildException)
    {
        Console.Error.WriteLine("ERROR " + ex.Message);
        return 1;
    }
}

static int ExportView(string path, string viewId, string? outFile)
{
    try
    {
        var (project, pipeline, issues) = LoadAndValidate(path);
        if (issues.Any(i => i.Severity == IssueSeverity.Error))
        {
            foreach (var issue in issues.Where(i => i.Severity == IssueSeverity.Error))
                Console.WriteLine("  " + issue);
            Console.Error.WriteLine("Export aborted: fix the errors above (see 'docwiz check').");
            return 1;
        }

        var view = project.Views.FirstOrDefault(v => v.Id == viewId);
        if (view is null)
        {
            Console.Error.WriteLine($"ERROR no view with id '{viewId}'. Available: " +
                                    string.Join(", ", project.Views.Select(v => v.Id)));
            return 1;
        }

        var html = SingleFileExporter.ExportView(project, view, pipeline);
        var target = outFile ?? Path.Combine(Path.GetFullPath(path), "output", $"{viewId}.html");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        File.WriteAllText(target, html);
        Console.WriteLine($"View '{viewId}' exported to {Path.GetFullPath(target)}");
        return 0;
    }
    catch (Exception ex) when (ex is DocWizLoadException or DocWizBuildException)
    {
        Console.Error.WriteLine("ERROR " + ex.Message);
        return 1;
    }
}

// Exports the manual (always) — and every view with --all — as one PDF per item via the
// Edge-headless native-print exporter (Page.printToPDF over CDP). Default output dir lives
// inside the project (<project>/output/PDF_Export, git-ignored). Edge ships with Windows.
static int ExportPdf(string path, string? outDirArg, bool all)
{
    try
    {
        var root = Path.GetFullPath(path);
        var outDir = outDirArg ?? Path.Combine(root, "output", "PDF_Export");
        var selection = new DocWiz.Build.Pdf.ExportSelection { Manual = true };
        if (all)
        {
            var project = ProjectLoader.Load(root);
            selection.ViewIds.AddRange(project.Views.Select(v => v.Id));
        }

        var edge = DocWiz.App.BrowserLauncher.FindEdge();
        if (edge is null)
        {
            Console.Error.WriteLine("ERROR Microsoft Edge was not found; it is required for PDF export.");
            return 1;
        }

        var result = DocWiz.Build.Pdf.PdfExporter.Export(root, selection, outDir, edge);
        foreach (var item in result.Items)
            Console.WriteLine(item.Success ? $"  OK  {item.Label} -> {item.Path}" : $"  FAIL {item.Label}: {item.Error}");
        return result.Items.All(i => i.Success) ? 0 : 1;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("ERROR " + ex.Message);
        return 1;
    }
}

// Drafts a starting slides deck from a chapter and writes it as a view.
// Writes through the shared ViewYaml.Serializer (underscored naming + OmitNull) — the
// same serializer the editor's view endpoints use, so CLI- and editor-written view YAML
// cannot drift. Default target views/<id>.yaml is refused if it already exists (a drafted
// deck must not clobber hand-tuned work); pass -o to write anywhere. After writing it
// runs the existing Check so the author sees the draft is clean.
static int DraftView(string path, string chapterId, string? outFile)
{
    try
    {
        var project = ProjectLoader.Load(path);
        var pipeline = DocWizMarkdown.CreatePipeline();
        DocWizMarkdown.ParseAll(project, pipeline);

        var chapter = project.FindChapter(chapterId);
        if (chapter is null)
        {
            Console.Error.WriteLine($"ERROR no chapter with id '{chapterId}'. Available: " +
                                    string.Join(", ", project.Chapters.Select(c => c.Id)));
            return 1;
        }

        var view = DeckDrafter.Draft(project, chapter);

        string target;
        if (outFile is not null)
        {
            target = Path.GetFullPath(outFile);
        }
        else
        {
            var viewsDir = Path.Combine(Path.GetFullPath(path), ProjectLayout.ViewsFolder);
            target = Path.Combine(viewsDir, $"{view.Id}.yaml");
            if (File.Exists(target))
            {
                Console.Error.WriteLine(
                    $"ERROR '{target}' already exists — refusing to overwrite. " +
                    "Pass -o <file.yaml> to write elsewhere, or remove the file first.");
                return 1;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, ViewYaml.Serializer.Serialize(view));
        Console.WriteLine($"Drafted deck for chapter '{chapterId}' → {target}");
        Console.WriteLine("This is a starting point — open it in the editor and trim the linked blocks live.");
        Console.WriteLine();

        // Re-validate so the author sees the drafted view is clean (it is loaded fresh
        // from disk by Check, exercising the real load + validate path).
        return Check(path);
    }
    catch (DocWizLoadException ex)
    {
        Console.Error.WriteLine("ERROR " + ex.Message);
        return 1;
    }
}
