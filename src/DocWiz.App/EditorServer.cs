using System.Diagnostics;
using System.Reflection;
using DocWiz.App.Import;
using DocWiz.Build;
using DocWiz.Build.Pdf;
using DocWiz.Core.Loading;
using DocWiz.Core.Markdown;
using DocWiz.Core.Models;
using DocWiz.Core.Slides;
using DocWiz.Core.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DocWiz.App;

public sealed record NewChapterDto(string Section, string Slug, string Id, string Title, string Discipline, List<string> Audiences);
public sealed record MoveChapterDto(string File, int Direction);
public sealed record MoveSectionDto(string Folder, int Direction);
public sealed record TextDto(string Text);
public sealed record PreviewDto(string Markdown);
public sealed record OpenProjectDto(string? Path);
public sealed record NewProjectDto(string Template, string? Path);
public sealed record SaveAsTemplateDto(string Name, string? Description);
public sealed record ImportCommitDto(string Token, List<string>? Selected, string Section, string? Discipline, List<string>? Audiences);
public sealed record ImportTokenDto(string Token);
public sealed record AddExternalDto(string Key, string Folder, bool CopyIn);
public sealed record ProjectSettingsDto(string? Project, string? Code, string? Customer, string? Version, string? Language, bool Numbering, List<AudienceDto>? Audiences);
public sealed record AudienceDto(string Id, string Title);
public sealed record ChapterFrontmatterDto(string File, string Discipline, List<string> Audiences);
public sealed record ViewSlideDto(string? Layout, string? Include, string? Title, string? Subtitle, string? Markdown,
    Dictionary<string, string>? Regions);
public sealed record ViewDto(string Id, string Title, string Audience, string Mode, List<ViewSlideDto> Slides);
public sealed record NewViewDto(string Id, string Title, string Audience, string Mode);
public sealed record DraftViewDto(string Chapter, string? Audience);
public sealed record EnsureAnchorDto(string Chapter, int Index);
public sealed record EditorFragmentDto(string Markdown, string? Audience);
public sealed record EmbedDetachDto(string Ref);
public sealed record RotateImageDto(string Src, bool Clockwise);
public sealed record PinDto(string Path);
public sealed record SettingsDto(string WindowMode);
public sealed record ExportPdfDto(string? Path, bool Manual, List<string>? ViewIds, List<string>? ChapterIds, string? Format);
public sealed record OpenPathDto(string? Path);

/// <summary>
/// The local editor app (`docwiz edit` / double-click): a Kestrel server bound to
/// localhost. Starts with or without a project — without one, the UI shows the welcome
/// screen (recent projects, open, new-from-template) and the project is mounted at
/// runtime. The project is reloaded from disk per request — files are small and authors
/// may also edit them externally.
/// </summary>
public static class EditorServer
{
    private sealed class ProjectHolder
    {
        public string? Root;
    }

    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    /// <summary>Where Build writes the handover, relative to the project root —
    /// the same default as `docwiz build` (git-ignored).</summary>
    private static readonly string HandoverOutDir = Path.Combine("output", "handover");

    private static readonly IDeserializer YamlIn = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    // The ONE view-YAML serializer (shared with the CLI's draft-view verb) — OmitNull only
    // (not OmitDefaults) so 'mode: document' is never dropped. See DocWiz.Core ViewYaml.
    private static readonly ISerializer YamlOut = ViewYaml.Serializer;

    /// <summary>folderPicker: shows a native folder dialog (separate process) and returns
    /// the chosen path, or null if cancelled. onUiLaunched: invoked once the editor window
    /// is actually on screen (or we gave up waiting) — the CLI uses it to dismiss the
    /// startup splash. launchNativeWindow(url, onPainted, onClosed): opens the editor in a
    /// native DocWiz-owned WebView2 window and returns a closer (call it to close the window),
    /// or null when WebView2 is unavailable so we fall back to the Edge app-window. All injected
    /// by the CLI so DocWiz.App stays platform-neutral.</summary>
    public static async Task<int> RunAsync(string? projectRoot, int port, bool openBrowser,
        Func<string?, string?>? folderPicker = null, Action? onUiLaunched = null,
        Func<string, Action, Action, Action?>? launchNativeWindow = null,
        EmfToPng? emfToPng = null,
        RotateImage90? rotateImage = null)
    {
        var holder = new ProjectHolder();
        if (projectRoot is not null && Mount(holder, projectRoot) is { } mountError)
        {
            Console.Error.WriteLine("ERROR " + mountError);
            return 1;
        }

        var url = $"http://127.0.0.1:{port}";
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(url);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        var app = builder.Build();

        IResult? NoProject() => holder.Root is null
            ? Results.Json(new { error = "no project open" }, statusCode: 409)
            : null;

        // PDF export runs in the background (~30-60s) so the request returns immediately
        // with a job id; the client polls /api/export-pdf/status until done/error.
        var pdfJobs = new PdfJobRegistry();

        // ---- UI shell + embedded static files ----
        // no-store: the UI is embedded in the exe, so a browser-cached old app.js/index.html
        // from a previous launch must never mix with a newer one (caused "buttons did nothing").
        app.MapGet("/", (HttpResponse res) => { NoStore(res); return EmbeddedFile("index.html"); });
        // The design tokens live once, in the site templates (DocWiz.Build) — the editor
        // links the same file instead of duplicating the palette.
        app.MapGet("/app/tokens.css", (HttpResponse res) =>
        {
            NoStore(res);
            return Results.Content(SiteTemplates.Read("tokens.css"), "text/css");
        });
        // The live surface IS the handover page: same site.css, wrapped in @scope so
        // article styling applies inside .dw-live-surface without fighting the editor
        // chrome (html/body/header rules become inert inside a scope). Chromium-only
        // is already the product's reality (file:// handover, import maps).
        app.MapGet("/app/site-scoped.css", (HttpResponse res) =>
        {
            NoStore(res);
            return Results.Content(
                "@scope (.dw-live-surface) {\n" + SiteTemplates.Read("site.css") + "\n}", "text/css");
        });
        // The light/dark toggle is the handover's site.js, served here so the editor and
        // handover share ONE toggle implementation (Rule 21). It is a site template, not a
        // wwwroot file, so the /app/{**path} catch-all below would not find it.
        app.MapGet("/app/site.js", (HttpResponse res) =>
        {
            NoStore(res);
            return Results.Content(SiteTemplates.Read("site.js"), "text/javascript");
        });
        app.MapGet("/app/{**path}", (string path, HttpResponse res) => { NoStore(res); return EmbeddedFile(path); });

        // ---- project files (dynamic: the mounted project can change at runtime).
        // All no-store: these files change under the browser's feet (rebuilds, image
        // replacement) — a cached handover page made "Build did nothing" a reported
        // stakeholder bug (2026-06-12). Localhost serving; caching buys nothing. ----
        app.MapGet("/assets/{**path}", (string path, HttpResponse res) => { NoStore(res); return ProjectFile(holder, ProjectLayout.AssetsFolder, path); });
        app.MapGet("/attachments/{**path}", (string path, HttpResponse res) => { NoStore(res); return ProjectFile(holder, ProjectLayout.AttachmentsFolder, path); });
        app.MapGet("/preview-root/assets/{**path}", (string path, HttpResponse res) => { NoStore(res); return ProjectFile(holder, ProjectLayout.AssetsFolder, path); });
        app.MapGet("/preview-root/attachments/{**path}", (string path, HttpResponse res) => { NoStore(res); return ProjectFile(holder, ProjectLayout.AttachmentsFolder, path); });
        // The built handover root holds only folders + the one project-named entry html
        // (HandoverBuilder.EntryFileName). Resolve it from the actual artifact so this stable
        // /handover URL keeps working whatever the project is named; 404 before the first build.
        app.MapGet("/handover", () =>
        {
            if (holder.Root is null) return Results.NotFound();
            var dir = Path.Combine(holder.Root, HandoverOutDir);
            var entry = Directory.Exists(dir)
                ? Directory.GetFiles(dir, "*.html").Select(Path.GetFileName).FirstOrDefault()
                : null;
            return entry is null
                ? Results.NotFound("Build the handover first.")
                : Results.Redirect($"/handover/{Uri.EscapeDataString(entry)}");
        });
        app.MapGet("/handover/{**path}", (string path, HttpResponse res) =>
        {
            NoStore(res);
            return ProjectFile(holder, HandoverOutDir, path);
        });

        // ---- build identity: which binary is actually serving this session ----
        // Shown in the header so "am I running the new build" is answerable from
        // a screenshot (two stale-binary support loops motivated this).
        app.MapGet("/api/version", () =>
        {
            var exe = Environment.ProcessPath;
            var built = exe is not null && File.Exists(exe)
                ? File.GetLastWriteTime(exe).ToString("yyyy-MM-dd HH:mm")
                : "unknown";
            return Results.Json(new { built, exe });
        });

        // ---- welcome screen ----
        app.MapGet("/api/welcome", () => Results.Json(new
        {
            recent = RecentProjects.Load().Select(r => new
            {
                r.Path,
                r.Project,
                r.Code,
                lastOpened = r.LastOpened.ToString("yyyy-MM-dd HH:mm"),
                exists = File.Exists(Path.Combine(r.Path, ProjectLayout.ManifestFileName)),
                pinned = r.Pinned,
            }),
            templates = TemplateLibrary.Discover(TemplatesLocation.Resolve()),
        }));

        // Removes one entry from the recents list (the project folder is untouched).
        app.MapDelete("/api/recent", (string path) =>
        {
            RecentProjects.Remove(path);
            return Results.Ok();
        });

        // Toggles the pinned flag for a recent-project entry.
        app.MapPost("/api/recent/pin", async (HttpRequest req) =>
        {
            var dto = await req.ReadFromJsonAsync<PinDto>();
            if (string.IsNullOrWhiteSpace(dto?.Path)) return Results.BadRequest("path required");
            RecentProjects.TogglePin(dto.Path);
            return Results.Ok();
        });

        app.MapPost("/api/open", async (HttpRequest request) =>
        {
            var dto = await request.ReadFromJsonAsync<OpenProjectDto>();
            // No path → ask via the native folder picker (separate process).
            var path = string.IsNullOrWhiteSpace(dto?.Path)
                ? folderPicker?.Invoke(RecentProjects.Load().FirstOrDefault()?.Path)
                : dto!.Path;
            if (string.IsNullOrWhiteSpace(path))
                return Results.Json(new { ok = false, cancelled = true });
            var error = Mount(holder, path);
            return Results.Json(new { ok = error is null, error });
        });

        app.MapPost("/api/new", async (HttpRequest request) =>
        {
            var dto = await request.ReadFromJsonAsync<NewProjectDto>();
            if (dto is null || string.IsNullOrWhiteSpace(dto.Template))
                return Results.BadRequest("template is required.");

            var path = string.IsNullOrWhiteSpace(dto.Path)
                ? folderPicker?.Invoke(RecentProjects.Load().FirstOrDefault()?.Path)
                : dto.Path;
            if (string.IsNullOrWhiteSpace(path))
                return Results.Json(new { ok = false, cancelled = true });

            try
            {
                // `blank` stays the code-scaffolded "New project" path; everything else
                // is a file template copied out of the on-disk library (master untouched).
                if (string.Equals(dto.Template, "blank", StringComparison.OrdinalIgnoreCase))
                {
                    ProjectScaffolder.CreateNew(path);
                }
                else
                {
                    if (dto.Template.Contains('/') || dto.Template.Contains('\\') || dto.Template.Contains(".."))
                        return Results.Json(new { ok = false, error = $"Invalid template id '{dto.Template}'." });
                    var templatePath = Path.Combine(TemplatesLocation.Resolve(), dto.Template);
                    if (!File.Exists(Path.Combine(templatePath, ProjectLayout.ManifestFileName)))
                        return Results.Json(new { ok = false, error = $"Template '{dto.Template}' was not found." });
                    TemplateLibrary.CopyProject(templatePath, path);
                }
            }
            catch (DocWizLoadException ex)
            {
                return Results.Json(new { ok = false, error = ex.Message });
            }
            var error = Mount(holder, path);
            return Results.Json(new { ok = error is null, error });
        });

        // ---- Save As: copy the current project to a chosen folder and switch to the copy.
        app.MapPost("/api/save-as", async (HttpRequest request) =>
        {
            if (NoProject() is { } guard)
                return guard;
            var dto = await request.ReadFromJsonAsync<OpenProjectDto>();
            var dest = string.IsNullOrWhiteSpace(dto?.Path)
                ? folderPicker?.Invoke(holder.Root)
                : dto!.Path;
            if (string.IsNullOrWhiteSpace(dest))
                return Results.Json(new { ok = false, cancelled = true });
            try
            {
                TemplateLibrary.CopyProject(holder.Root!, dest);
            }
            catch (DocWizLoadException ex)
            {
                return Results.Json(new { ok = false, error = ex.Message });
            }
            var error = Mount(holder, dest);
            return Results.Json(new { ok = error is null, error });
        });

        // ---- Save as template: copy the current project into the library + meta wrapper.
        // Stays on the current project (the copy is the master, not what you're editing).
        app.MapPost("/api/save-as-template", async (HttpRequest request) =>
        {
            if (NoProject() is { } guard)
                return guard;
            var dto = await request.ReadFromJsonAsync<SaveAsTemplateDto>();
            if (dto is null || string.IsNullOrWhiteSpace(dto.Name))
                return Results.BadRequest("name is required.");
            var dest = Path.Combine(TemplatesLocation.Resolve(), HeadingScanner.Slug(dto.Name));
            try
            {
                TemplateLibrary.CopyProject(holder.Root!, dest);
                TemplateLibrary.WriteTemplateMeta(dest, dto.Name, dto.Description ?? "");
            }
            catch (DocWizLoadException ex)
            {
                return Results.Json(new { ok = false, error = ex.Message });
            }
            return Results.Json(new { ok = true, id = Path.GetFileName(dest) });
        });

        // ---- project API ----
        app.MapGet("/api/project", () =>
        {
            if (NoProject() is { } guard)
                return guard;
            var (project, _) = LoadParsed(holder.Root!);
            var templatesDir = Path.GetFullPath(TemplatesLocation.Resolve());
            var isTemplate = Path.GetFullPath(project.Root)
                .StartsWith(templatesDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            return Results.Json(new
            {
                project = project.Manifest.Project,
                code = project.Manifest.Code,
                root = project.Root,
                emfCount = EmfRetrofit.CountConvertible(project.Root),
                isTemplate,
                numbering = project.Manifest.Numbering,
                audiences = project.Manifest.Audiences.Select(a => new { a.Id, a.Title }),
                sections = project.Chapters
                    .GroupBy(c => Path.GetFileName(Path.GetDirectoryName(c.FilePath))!)
                    .Select(g => new
                    {
                        folder = g.Key,
                        title = g.First().SectionTitle,
                        chapters = g.Select(c => new
                        {
                            c.Id,
                            c.Title,
                            c.Discipline,
                            c.Audiences,
                            file = RelPath(project.Root, c.FilePath),
                            anchors = c.Anchors,
                            groups = DocWizGroupExtension.Scan(c.Markdown).Select(g => new { g.Id, g.Name }),
                            // H2/H3 outline for the tree's subchapter display
                            headings = HeadingScanner.Scan(c.Markdown)
                                .Where(h => h.Level is 2 or 3)
                                .Select(h => new { h.Level, h.Text, h.Anchor }),
                        }),
                    }),
                views = project.Views.Select(v => new
                {
                    v.Id, v.Title, v.Audience, v.Mode,
                    file = RelPath(project.Root, v.FilePath),
                }),
            });
        });

        app.MapGet("/api/chapter", (string file) =>
        {
            if (NoProject() is { } guard)
                return guard;
            var path = ResolveChapterPath(holder.Root!, file);
            if (path is null)
                return Results.BadRequest("Not a chapter file inside this project.");
            var (yaml, body) = FrontmatterParser.Split(File.ReadAllText(path));
            var fm = ChapterYaml.Parse(yaml);
            return Results.Json(new
            {
                file,
                id = fm.GetValueOrDefault("id") as string ?? "",
                title = fm.GetValueOrDefault("title") as string ?? "",
                discipline = fm.GetValueOrDefault("discipline") as string ?? "",
                audiences = ChapterYaml.AudienceList(fm),
                markdown = body.TrimStart('\n'),
            });
        });

        // (Chapter saves go through PUT /api/editor-doc — the byte-surgical path.
        // The Toast-era whole-markdown PUT /api/chapter was removed in the cleanup
        // pass, 2026-06-12.)

        app.MapPost("/api/chapter", async (HttpRequest request) =>
        {
            if (NoProject() is { } guard)
                return guard;
            var dto = await request.ReadFromJsonAsync<NewChapterDto>();
            if (dto is null || dto.Slug.Length == 0 || dto.Id.Length == 0)
                return Results.BadRequest("section, slug, id, and title are required.");
            var (project, _) = LoadParsed(holder.Root!);
            if (project.FindChapter(dto.Id) is not null)
                return Results.BadRequest($"Chapter id '{dto.Id}' already exists.");

            var path = ChapterFactory.Create(holder.Root!, dto);
            return Results.Json(new { file = RelPath(holder.Root!, path) });
        });

        // Reordering renames filename/folder prefixes only — ids are stable (ADR-005),
        // so dw: links and view includes survive every move.
        app.MapPost("/api/chapter-move", async (HttpRequest request) =>
        {
            if (NoProject() is { } guard)
                return guard;
            var dto = await request.ReadFromJsonAsync<MoveChapterDto>();
            if (dto is null || (dto.Direction != -1 && dto.Direction != 1))
                return Results.BadRequest("file and direction (-1 or 1) are required.");
            var path = ResolveChapterPath(holder.Root!, dto.File);
            if (path is null)
                return Results.BadRequest("Not a chapter file inside this project.");
            var newPath = ContentOrganizer.MoveChapter(path, dto.Direction);
            return Results.Json(new { file = RelPath(holder.Root!, newPath) });
        });

        app.MapPost("/api/section-move", async (HttpRequest request) =>
        {
            if (NoProject() is { } guard)
                return guard;
            var dto = await request.ReadFromJsonAsync<MoveSectionDto>();
            if (dto is null || (dto.Direction != -1 && dto.Direction != 1))
                return Results.BadRequest("folder and direction (-1 or 1) are required.");
            if (dto.Folder.Contains('/') || dto.Folder.Contains('\\') || dto.Folder.Contains(".."))
                return Results.BadRequest("Not a section folder name.");
            if (!Directory.Exists(Path.Combine(holder.Root!, ProjectLayout.ContentFolder, dto.Folder)))
                return Results.BadRequest($"No section folder '{dto.Folder}' in this project.");
            var newName = ContentOrganizer.MoveSection(holder.Root!, dto.Folder, dto.Direction);
            return Results.Json(new { folder = newName });
        });

        // Deleting is refused while anything still references the chapter — the
        // validator would flag the dangling links anyway; failing early is kinder.
        app.MapDelete("/api/chapter", (string file) =>
        {
            if (NoProject() is { } guard)
                return guard;
            var path = ResolveChapterPath(holder.Root!, file);
            if (path is null)
                return Results.BadRequest("Not a chapter file inside this project.");
            var (project, _) = LoadParsed(holder.Root!);
            var chapter = project.Chapters.FirstOrDefault(c =>
                string.Equals(Path.GetFullPath(c.FilePath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
            if (chapter is null)
                return Results.BadRequest("Chapter not found in the loaded project.");
            var references = ContentOrganizer.FindReferences(project, chapter.Id);
            if (references.Count > 0)
                return Results.Text(
                    $"Cannot delete: {string.Join("; ", references)}. Remove those references first.",
                    "text/plain", statusCode: 409);
            File.Delete(path);
            return Results.Ok();
        });

        app.MapGet("/api/yaml", (string path) =>
        {
            if (NoProject() is { } guard)
                return guard;
            var full = ResolveYamlPath(holder.Root!, path);
            return full is null
                ? Results.BadRequest("Only docwiz.yaml and views/*.yaml are editable here.")
                : Results.Json(new TextDto(File.ReadAllText(full)));
        });

        app.MapPut("/api/yaml", async (string path, HttpRequest request) =>
        {
            if (NoProject() is { } guard)
                return guard;
            var full = ResolveYamlPath(holder.Root!, path);
            if (full is null)
                return Results.BadRequest("Only docwiz.yaml and views/*.yaml are editable here.");
            TextDto? dto;
            try
            {
                dto = await request.ReadFromJsonAsync<TextDto>();
            }
            catch (System.Text.Json.JsonException)
            {
                return Results.BadRequest("Malformed request body.");
            }
            if (dto?.Text is null)
                return Results.BadRequest("Missing body.");

            // Validate BEFORE writing — a broken docwiz.yaml would make every
            // project-loading endpoint fail and effectively brick the session.
            try
            {
                var isManifest = string.Equals(Path.GetFileName(full), ProjectLayout.ManifestFileName, StringComparison.OrdinalIgnoreCase);
                if (isManifest)
                {
                    var manifest = YamlIn.Deserialize<ProjectManifest>(dto.Text);
                    if (manifest is null || manifest.Audiences.Count == 0)
                        return Results.BadRequest("Not saved: the manifest needs at least one audience.");
                }
                else if (YamlIn.Deserialize<ViewDefinition>(dto.Text) is null)
                {
                    return Results.BadRequest("Not saved: the file would be empty.");
                }
            }
            catch (Exception ex)
            {
                return Results.BadRequest("Not saved — invalid YAML: " + ex.Message);
            }

            File.WriteAllText(full, dto.Text);
            return Results.Ok();
        });

        // ---- visual view manager (views/*.yaml edited structurally, not as raw text) ----
        app.MapGet("/api/view", (string file) =>
        {
            if (NoProject() is { } guard)
                return guard;
            var full = ResolveViewPath(holder.Root!, file, mustExist: true);
            if (full is null)
                return Results.BadRequest("Not a view file inside this project.");
            var view = YamlIn.Deserialize<ViewDefinition>(File.ReadAllText(full)) ?? new ViewDefinition();
            return Results.Json(new
            {
                file,
                view.Id,
                view.Title,
                view.Audience,
                view.Mode,
                slides = view.Slides.Select(s => new
                {
                    s.Layout, s.Title, s.Subtitle, s.Include, s.Markdown,
                    regions = s.Regions,
                }),
            });
        });

        app.MapPut("/api/view", async (string file, HttpRequest request) =>
        {
            if (NoProject() is { } guard)
                return guard;
            var full = ResolveViewPath(holder.Root!, file, mustExist: true);
            if (full is null)
                return Results.BadRequest("Not a view file inside this project.");
            var dto = await request.ReadFromJsonAsync<ViewDto>();
            if (dto is null)
                return Results.BadRequest("Missing body.");
            // Decode any inline base64 data: image a slide-zone paste inlined (see the
            // chapter save) into an asset before the view yaml is written.
            var yaml = DataImageRescue.Rewrite(YamlOut.Serialize(ToView(dto)), holder.Root!);
            File.WriteAllText(full, yaml);
            return Results.Ok();
        });

        app.MapPost("/api/view", async (HttpRequest request) =>
        {
            if (NoProject() is { } guard)
                return guard;
            var dto = await request.ReadFromJsonAsync<NewViewDto>();
            if (dto is null || string.IsNullOrWhiteSpace(dto.Id) || string.IsNullOrWhiteSpace(dto.Title))
                return Results.BadRequest("id and title are required.");

            var viewsDir = Path.Combine(holder.Root!, ProjectLayout.ViewsFolder);
            Directory.CreateDirectory(viewsDir);
            var full = Path.Combine(viewsDir, dto.Id + ".yaml");
            if (File.Exists(full))
                return Results.BadRequest($"A view file '{dto.Id}.yaml' already exists.");

            var view = ToView(new ViewDto(dto.Id, dto.Title, dto.Audience, dto.Mode, []));
            File.WriteAllText(full, YamlOut.Serialize(view));
            return Results.Json(new { file = RelPath(holder.Root!, full) });
        });

        // Draft a starting slides deck from a chapter (Slides v2): linked
        // ::: embed ::: blocks the author then trims live. Reuses the pure DeckDrafter
        // and the same YamlOut as the other view endpoints; guards the path and refuses
        // to clobber an existing view file (DeckDrafter already picks a collision-free id,
        // so this only trips if that exact file appeared since the id was chosen).
        app.MapPost("/api/draft-view", async (HttpRequest request) =>
        {
            if (NoProject() is { } guard)
                return guard;
            var dto = await request.ReadFromJsonAsync<DraftViewDto>();
            if (dto is null || string.IsNullOrWhiteSpace(dto.Chapter))
                return Results.BadRequest("chapter is required.");

            var (project, _) = LoadParsed(holder.Root!);
            var chapter = project.FindChapter(dto.Chapter);
            if (chapter is null)
                return Results.BadRequest($"No chapter with id '{dto.Chapter}'.");

            var view = DeckDrafter.Draft(project, chapter, string.IsNullOrWhiteSpace(dto.Audience) ? null : dto.Audience);

            var viewsDir = Path.Combine(holder.Root!, ProjectLayout.ViewsFolder);
            Directory.CreateDirectory(viewsDir);
            var full = Path.Combine(viewsDir, view.Id + ".yaml");
            if (File.Exists(full))
                return Results.BadRequest($"A view file '{view.Id}.yaml' already exists.");

            File.WriteAllText(full, YamlOut.Serialize(view));
            return Results.Json(new { file = RelPath(holder.Root!, full) });
        });

        app.MapDelete("/api/view", (string file) =>
        {
            if (NoProject() is { } guard)
                return guard;
            var full = ResolveViewPath(holder.Root!, file, mustExist: true);
            if (full is null)
                return Results.BadRequest("Not a view file inside this project.");
            File.Delete(full);
            return Results.Ok();
        });

        // Renders a DRAFT view (unsaved editor state) for the WYSIWYG stage. Same
        // renderer as the build — what the stage shows is what ships. Assets resolve
        // via <base href="/preview-root/">; slides mode includes the real slides.js so
        // the stage can drive navigation by hash.
        //
        // ?edit=1 (slides mode) returns the authoring surface as a FRAGMENT (not a full
        // document): just the <div class="slides-edit-list"> with the RenderSlides output
        // inside it (Slides v2, ADR-007, Task 7). The client injects that fragment into a
        // .dw-live-surface container in the PARENT document — where site.css already applies
        // via the /app/site-scoped.css @scope wrapper, exactly like the chapter editor — so
        // per-zone editors (7b) mount in one document, not a child iframe. No tokens.css /
        // site.css wrapper here (the parent has them), no slides.js, and no inline
        // all-active script: site.css's .slide{display:none} keeps slides hidden, so the
        // client marks each .slide .active in JS to trigger the .slide.active[data-layout]
        // layout rules. The edit surface renders each slide as the SAME 1920×1080 frame as the
        // present deck, scaled to the column with CSS `zoom` (NOT transform: scale — zoom scales
        // the hit-test/coordinate space natively in Edge so ProseMirror carets stay correct),
        // so EDIT is pixel-identical to Read. That re-framing is editor chrome in app.css
        // (.dw-slide-edit); app.js sets --dw-edit-zoom from the live column width. edit: true
        // also makes every layout region (even empty) an editable zone and the title/subtitle
        // editable, so new/blank slides are authorable. The present/scaled branch is unchanged.
        app.MapPost("/api/preview-view", async (HttpRequest request) =>
        {
            if (NoProject() is { } guard)
                return guard;
            var dto = await request.ReadFromJsonAsync<ViewDto>();
            if (dto is null)
                return Results.BadRequest("Missing body.");
            var editSurface = request.Query["edit"] == "1";

            var (project, pipeline) = LoadParsed(holder.Root!);
            var view = ToView(dto);
            string html;
            if (view.Mode == "slides" && editSurface)
            {
                // FRAGMENT only — injected into a .dw-live-surface container in the parent
                // document (site.css applies there via the @scope wrapper). The client adds
                // .active to each .slide (injected innerHTML never runs <script>).
                html =
                    "<div class=\"slides-edit-list\" id=\"slidesEditList\">" +
                    ViewRenderer.RenderSlides(project, view, pipeline, edit: true) +
                    "</div>";
            }
            else if (editSurface)
            {
                // Document mode, edit surface: a FRAGMENT (like slides) wrapped in the same
                // <article> the read page uses, injected into the parent-document .dw-live-surface
                // so DocWizSlideEdit can mount zone editors on its .view-sec cards. linkSources
                // is irrelevant in edit mode (RenderDocument omits srclines when edit:true).
                html =
                    "<article class=\"dw-doc-edit-list\" id=\"docEditList\">" +
                    ViewRenderer.RenderDocument(project, view, pipeline, linkSources: false, edit: true) +
                    "</article>";
            }
            else
            {
                html = RenderViewPage(project, view, pipeline);
            }
            return Results.Content(html, "text/html");
        });

        // Read/present a SAVED view by id — the same slides deck or document as the non-edit
        // branch of /api/preview-view, but addressable by URL so the editor's "Read / present"
        // button can open it in the DEFAULT BROWSER (the native shell routes _blank → browser;
        // in browser mode it's a tab). The client saves the view first, so the saved deck is
        // what renders (mirrors the chapter Read button).
        app.MapGet("/preview-root/view", (string id, HttpResponse res) =>
        {
            NoStore(res);
            if (NoProject() is { } guard)
                return guard;
            var (project, pipeline) = LoadParsed(holder.Root!);
            var view = project.Views.FirstOrDefault(v => v.Id == id);
            if (view is null)
                return Results.NotFound($"No view '{id}' in this project.");
            return Results.Content(RenderViewPage(project, view, pipeline), "text/html");
        });

        // Export one view as a self-contained single HTML file (download).
        app.MapGet("/api/export-view", (string file) =>
        {
            if (NoProject() is { } guard)
                return guard;
            var full = ResolveViewPath(holder.Root!, file, mustExist: true);
            if (full is null)
                return Results.BadRequest("Not a view file inside this project.");
            var (project, pipeline) = LoadParsed(holder.Root!);
            var view = project.Views.FirstOrDefault(v =>
                string.Equals(Path.GetFullPath(v.FilePath), full, StringComparison.OrdinalIgnoreCase));
            if (view is null)
                return Results.NotFound();
            var bytes = System.Text.Encoding.UTF8.GetBytes(SingleFileExporter.ExportView(project, view, pipeline));
            return Results.File(bytes, "text/html", $"{view.Id}.html");
        });

        // Build the handover and return it as a single zip (download).
        app.MapGet("/api/package", () =>
        {
            if (NoProject() is { } guard)
                return guard;
            var (project, pipeline) = LoadParsed(holder.Root!);
            if (ProjectValidator.Validate(project).Any(i => i.Severity == IssueSeverity.Error))
                return Results.BadRequest("Fix validation errors first (run Check).");
            var outDir = Path.Combine(holder.Root!, HandoverOutDir);
            HandoverBuilder.Build(project, pipeline, outDir);
            var zipName = Packager.ZipName(project.Manifest.Code, project.Manifest.Version);
            var zipPath = Path.Combine(holder.Root!, "output", zipName);
            Packager.Zip(outDir, zipPath);
            return Results.File(zipPath, "application/zip", zipName);
        });

        // Headings of a chapter (for the view section picker's anchor auto-creation).
        app.MapGet("/api/headings", (string chapter) =>
        {
            if (NoProject() is { } guard)
                return guard;
            var (project, _) = LoadParsed(holder.Root!);
            var ch = project.FindChapter(chapter);
            if (ch is null)
                return Results.NotFound();
            var (_, body) = FrontmatterParser.Split(File.ReadAllText(ch.FilePath));
            // Skip the H1 (the chapter title) — "(whole chapter)" already covers it.
            // Index stays from the full scan so ensure-anchor can still locate the line.
            return Results.Json(HeadingScanner.Scan(body)
                .Where(h => h.Level >= 2)
                .Select(h => new { h.Index, h.Level, h.Text, h.Anchor }));
        });

        // Ensure the chosen heading has an anchor, creating one if missing (mutates the
        // chapter file). Lets a view pull in a section the author never pre-anchored.
        app.MapPost("/api/ensure-anchor", async (HttpRequest request) =>
        {
            if (NoProject() is { } guard)
                return guard;
            var dto = await request.ReadFromJsonAsync<EnsureAnchorDto>();
            var (project, _) = LoadParsed(holder.Root!);
            var ch = dto is null ? null : project.FindChapter(dto.Chapter);
            if (dto is null || ch is null)
                return Results.BadRequest("Unknown chapter.");

            var (yaml, body) = FrontmatterParser.Split(File.ReadAllText(ch.FilePath));
            var heads = HeadingScanner.Scan(body);
            var target = heads.FirstOrDefault(h => h.Index == dto.Index);
            if (target is null)
                return Results.BadRequest("Heading not found.");
            if (target.Anchor is not null)
                return Results.Json(new { anchor = target.Anchor });

            var existing = heads.Where(h => h.Anchor is not null).Select(h => h.Anchor!).ToHashSet(StringComparer.Ordinal);
            var anchor = HeadingScanner.UniqueAnchor(HeadingScanner.Slug(target.Text), existing);

            var lines = body.Replace("\r\n", "\n").Split('\n').ToList();
            lines[target.Line] = lines[target.Line].TrimEnd() + " {#" + anchor + "}";
            File.WriteAllText(ch.FilePath, ChapterYaml.Compose(ChapterYaml.Parse(yaml), string.Join('\n', lines)));
            return Results.Json(new { anchor });
        });

        // Files are listed recursively (subfolders allowed for organisation); names are
        // relative forward-slash paths, e.g. "renders/cell.png".
        app.MapGet("/api/assets", () =>
        {
            if (NoProject() is { } guard)
                return guard;
            return Results.Json(ListProjectFiles(Path.Combine(holder.Root!, ProjectLayout.AssetsFolder)));
        });

        app.MapGet("/api/attachments", () =>
        {
            if (NoProject() is { } guard)
                return guard;
            return Results.Json(ListProjectFiles(Path.Combine(holder.Root!, ProjectLayout.AttachmentsFolder)));
        });

        app.MapPost("/api/attachment", async (string name, string? folder, HttpRequest request) =>
        {
            if (NoProject() is { } guard)
                return guard;
            var rel = await SaveUpload(Path.Combine(holder.Root!, ProjectLayout.AttachmentsFolder), folder, name, request);
            return rel is null
                ? Results.BadRequest("Invalid target folder.")
                : Results.Json(new { fileName = rel });
        });

        // Delete one attachment (Attachments manager). Traversal-guarded, attachments-only.
        app.MapDelete("/api/attachment", (string name) =>
        {
            if (NoProject() is { } guard)
                return guard;
            var dir = Path.Combine(holder.Root!, ProjectLayout.AttachmentsFolder);
            var full = Path.GetFullPath(Path.Combine(dir, name));
            if (!full.StartsWith(Path.GetFullPath(dir) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return Results.BadRequest("Invalid path.");
            if (File.Exists(full)) File.Delete(full);
            return Results.Ok();
        });

        // ---- documentation packages (external HTML doc tabs, e.g. the API Reference tab) ----
        app.MapGet("/api/external-docs", () =>
        {
            if (NoProject() is { } guard)
                return guard;
            return Results.Json(ExternalDocs.List(holder.Root!).Select(p => new
            {
                key = p.Key, title = p.Title, source = p.Source, path = p.Path, valid = p.Valid, bytes = p.Bytes, entry = p.Entry,
            }));
        });

        app.MapPost("/api/external-docs", async (HttpRequest req) =>
        {
            if (NoProject() is { } guard)
                return guard;
            var dto = await req.ReadFromJsonAsync<AddExternalDto>();
            if (dto is null || string.IsNullOrWhiteSpace(dto.Key) || string.IsNullOrWhiteSpace(dto.Folder))
                return Results.BadRequest("key and folder are required.");
            try { ExternalDocs.Add(holder.Root!, dto.Key, dto.Folder, dto.CopyIn); }
            catch (DocWizLoadException e) { return Results.BadRequest(e.Message); }
            return Results.Ok();
        });

        app.MapDelete("/api/external-docs", (string key) =>
        {
            if (NoProject() is { } guard)
                return guard;
            try { ExternalDocs.Remove(holder.Root!, key); }
            catch (DocWizLoadException e) { return Results.BadRequest(e.Message); }
            return Results.Ok();
        });

        // Native folder picker — reuses the injected delegate the welcome screen uses for Open project.
        app.MapGet("/api/pick-folder", () =>
            Results.Json(new { path = folderPicker?.Invoke(holder.Root) }));

        // ---- project settings (interactive docwiz.yaml editor; writes via ManifestWriter) ----
        app.MapGet("/api/settings-project", () =>
        {
            if (NoProject() is { } guard)
                return guard;
            var m = LoadParsed(holder.Root!).Project.Manifest;
            return Results.Json(new
            {
                project = m.Project, code = m.Code, customer = m.Customer, version = m.Version,
                language = m.Language, numbering = m.Numbering,
                audiences = m.Audiences.Select(a => new { id = a.Id, title = a.Title }),
            });
        });

        app.MapPut("/api/settings-project", async (HttpRequest req) =>
        {
            if (NoProject() is { } guard)
                return guard;
            var dto = await req.ReadFromJsonAsync<ProjectSettingsDto>();
            if (dto is null)
                return Results.BadRequest("Missing body.");
            // Quote free-text scalars that YAML would otherwise re-type (bool/null/number) or
            // mis-parse (special chars / leading indicators / surrounding space); leave plain
            // values (and version/numbering, handled separately) bare so the file style doesn't churn.
            static string Q(string? v)
            {
                if (string.IsNullOrEmpty(v)) return v ?? "";
                var needs =
                    v != v.Trim() ||                                   // leading/trailing space
                    "-?:,[]{}#&*!|>'\"%@`".IndexOf(v[0]) >= 0 ||        // YAML indicator at start
                    v.Contains(": ") || v.EndsWith(":") || v.Contains(" #") ||
                    System.Text.RegularExpressions.Regex.IsMatch(v, "^(true|false|yes|no|on|off|null|~)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
                    System.Text.RegularExpressions.Regex.IsMatch(v, @"^[+-]?(\d+\.?\d*|\.\d+)$");  // looks numeric
                return needs ? ChapterTemplates.YamlQuote(v) : v;
            }
            var path = Path.Combine(holder.Root!, "docwiz.yaml");
            var yaml = File.ReadAllText(path);
            yaml = ManifestWriter.SetScalar(yaml, "project", Q(dto.Project));
            yaml = ManifestWriter.SetScalar(yaml, "code", Q(dto.Code));
            yaml = ManifestWriter.SetScalar(yaml, "customer", Q(dto.Customer));
            yaml = ManifestWriter.SetScalar(yaml, "version", dto.Version ?? "0.0.0");
            yaml = ManifestWriter.SetScalar(yaml, "language", Q(dto.Language));
            yaml = ManifestWriter.SetScalar(yaml, "numbering", dto.Numbering ? "true" : "false");
            yaml = ManifestWriter.SetAudiences(yaml,
                (dto.Audiences ?? []).Select(a => (a.Id, (string)Q(a.Title))));
            File.WriteAllText(path, yaml);
            return Results.Ok();
        });

        app.MapPost("/api/asset", async (string name, string? folder, HttpRequest request) =>
        {
            if (NoProject() is { } guard)
                return guard;
            var rel = await SaveUpload(Path.Combine(holder.Root!, ProjectLayout.AssetsFolder), folder, name, request);
            return rel is null
                ? Results.BadRequest("Invalid target folder.")
                : Results.Json(new { url = $"../../assets/{rel}", fileName = rel });
        });

        // ---- Word import (two-phase: preview/stage, then commit selected) ----
        // Preview converts the uploaded .docx into a temp staging area and returns the
        // detected sections WITHOUT touching the project; the client cherry-picks, then
        // commit writes only the chosen sections. The .docx arrives as the raw request body
        // (browser <input type=file>), buffered so the synchronous importer can read it.
        app.MapPost("/api/import-docx/preview", async (string name, HttpRequest request) =>
        {
            if (NoProject() is { } guard)
                return guard;
            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer);
            buffer.Position = 0;
            var preview = DocxImporter.Preview(buffer, string.IsNullOrWhiteSpace(name) ? "document.docx" : name);
            return Results.Json(preview);
        });

        app.MapPost("/api/import-docx/commit", async (HttpRequest request) =>
        {
            if (NoProject() is { } guard)
                return guard;
            var dto = await request.ReadFromJsonAsync<ImportCommitDto>();
            if (dto is null || string.IsNullOrWhiteSpace(dto.Token) || string.IsNullOrWhiteSpace(dto.Section))
                return Results.BadRequest("token and section are required.");
            if (dto.Section.Contains('/') || dto.Section.Contains('\\') || dto.Section.Contains(".."))
                return Results.BadRequest("Invalid section name.");
            var defaults = new ImportDefaults(dto.Discipline ?? "", dto.Audiences);
            var outcome = DocxImporter.Commit(dto.Token, holder.Root!, dto.Selected ?? [], dto.Section, defaults, emfToPng);
            return Results.Json(outcome);
        });

        app.MapPost("/api/import-docx/cancel", async (HttpRequest request) =>
        {
            var dto = await request.ReadFromJsonAsync<ImportTokenDto>();
            if (!string.IsNullOrWhiteSpace(dto?.Token))
                DocxImporter.Cancel(dto.Token);
            return Results.Ok();
        });

        // One-shot: convert this project's EMF/WMF to PNG (editor "Convert EMF…" button).
        app.MapPost("/api/convert-emf", () =>
        {
            if (NoProject() is { } guard)
                return guard;
            if (emfToPng is null)
                return Results.BadRequest("EMF/WMF conversion is unavailable in this build.");
            var result = EmfRetrofit.ConvertProject(holder.Root!, emfToPng);
            return Results.Json(result);
        });

        // Rotate one figure's image 90° in place (editor image tools). A pixel edit of the
        // asset, not a Markdown change — the original is kept once in _rotated-originals/.
        app.MapPost("/api/rotate-image", async (HttpRequest request) =>
        {
            if (NoProject() is { } guard)
                return guard;
            if (rotateImage is null)
                return Results.BadRequest("Image rotation is unavailable in this build.");
            var dto = await request.ReadFromJsonAsync<RotateImageDto>();
            if (dto is null || string.IsNullOrWhiteSpace(dto.Src))
                return Results.BadRequest("src is required.");
            return ImageRotation.RotateAsset(holder.Root!, dto.Src, dto.Clockwise, rotateImage)
                ? Results.Json(new { ok = true })
                : Results.BadRequest("Could not rotate that image.");
        });

        // Frontmatter-only update (discipline + audiences) for the post-import walkthrough —
        // the stable id and the body are untouched (ADR-005), unknown keys preserved.
        app.MapPost("/api/chapter-frontmatter", async (HttpRequest request) =>
        {
            if (NoProject() is { } guard)
                return guard;
            var dto = await request.ReadFromJsonAsync<ChapterFrontmatterDto>();
            if (dto is null || string.IsNullOrWhiteSpace(dto.File))
                return Results.BadRequest("file is required.");
            var path = ResolveChapterPath(holder.Root!, dto.File);
            if (path is null)
                return Results.BadRequest("Not a chapter file inside this project.");
            var (yaml, rawBody) = FrontmatterParser.Split(File.ReadAllText(path));
            var fm = ChapterYaml.Parse(yaml);
            fm["discipline"] = dto.Discipline;
            fm["audiences"] = dto.Audiences;
            File.WriteAllText(path, ChapterYaml.Compose(fm, rawBody.TrimStart('\n')));
            return Results.Json(new { ok = true });
        });

        // The live editing surface's document (ADR-007): per-block pipeline HTML +
        // verbatim source spans + body hash (M2 save guard). Read-only consumer in M1.
        app.MapGet("/api/editor-doc", (string file, HttpResponse res) =>
        {
            if (NoProject() is { } guard)
                return guard;
            var path = ResolveChapterPath(holder.Root!, file);
            if (path is null)
                return Results.BadRequest("Not a chapter file inside this project.");
            NoStore(res);
            var project = ProjectLoader.Load(holder.Root!);
            return Results.Json(EditorDoc.Build(project, RelPath(holder.Root!, path)));
        });

        // Byte-surgical save (ADR-007): copy segments splice original bytes verbatim;
        // only edited blocks arrive as text. Heading anchors are ensured inside text
        // segments only; the stable id is never rewritten (ADR-005 — same key-by-key
        // frontmatter update as /api/chapter). Returns the fresh editor-doc so the
        // surface rebuilds from pipeline truth after every save.
        app.MapPut("/api/editor-doc", async (string file, HttpRequest request) =>
        {
            if (NoProject() is { } guard)
                return guard;
            var path = ResolveChapterPath(holder.Root!, file);
            if (path is null)
                return Results.BadRequest("Not a chapter file inside this project.");
            var dto = await request.ReadFromJsonAsync<SaveEditorDocDto>();
            if (dto is null || dto.Segments is null || dto.Segments.Count == 0)
                return Results.BadRequest("Missing save segments.");

            var (yaml, rawBody) = FrontmatterParser.Split(File.ReadAllText(path));
            var body = rawBody.TrimStart('\n');
            if (!string.Equals(EditorDoc.ComputeBodyHash(body), dto.BaseBodyHash, StringComparison.OrdinalIgnoreCase))
                return Results.Text(
                    "Not saved: the chapter changed on disk since it was opened here. Reload the chapter, then redo the edit.",
                    "text/plain", statusCode: 409);

            string newBody;
            try
            {
                newBody = EditorDoc.BuildNewBody(body, dto.Segments);
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
            {
                return Results.BadRequest(ex.Message);
            }

            // Decode any inline base64 data: image (a Word-bitmap paste ProseMirror inlined,
            // unreachable by the clipboard APIs) into a real asset under assets/pasted/ — so
            // the saved chapter never carries a multi-kB blob. Only changed bytes can contain
            // one, so this stays byte-stable for untouched content.
            newBody = DataImageRescue.Rewrite(newBody, holder.Root!);

            var fm = ChapterYaml.Parse(yaml);
            fm["title"] = dto.Title;
            fm["discipline"] = dto.Discipline;
            fm["audiences"] = dto.Audiences;
            File.WriteAllText(path, ChapterYaml.Compose(fm, newBody));

            var project = ProjectLoader.Load(holder.Root!);
            return Results.Json(EditorDoc.Build(project, RelPath(holder.Root!, path)));
        });

        // Slide-zone editing (Slides v2, ADR-007, Task 7b): turn one region's markdown
        // into editor-surface segments through the SAME engine as /api/editor-doc
        // (EditorDoc.BuildSegments → BlockSegmenter + the one pipeline). No frontmatter,
        // no bodyHash, no chapter lookup, no byte-surgical save — zones re-serialize fully
        // in 7c. Embeds render resolved but stay locked raw blocks that round-trip to the
        // directive (see EditorDoc.BuildSegments). Audience filters the embedded content.
        app.MapPost("/api/editor-fragment", async (HttpRequest request) =>
        {
            if (NoProject() is { } guard)
                return guard;
            var dto = await request.ReadFromJsonAsync<EditorFragmentDto>();
            if (dto is null)
                return Results.BadRequest("Missing body.");
            // Embeds transclude chapter sections, so the chapters must be parsed
            // (LoadParsed → DocWizMarkdown.ParseAll) — same as /api/preview-view.
            var (project, _) = LoadParsed(holder.Root!);
            try
            {
                var segments = EditorDoc.BuildSegments(project, dto.Markdown ?? "", dto.Audience, resolveEmbeds: true);
                return Results.Json(new { segments });
            }
            catch (Exception ex) when (ex is DocWizEmbed.EmbedException or TransclusionResolver.TransclusionException)
            {
                // A dangling embed (missing chapter/anchor) — the validator reports it too;
                // surface it so the zone can show the message instead of 500ing.
                return Results.BadRequest(ex.Message);
            }
        });

        // Embed Detach (Slides v2, ADR-007, Task 9): given a linked block's ref, return the
        // referenced section's RAW markdown as EDITABLE segments — the same shape as
        // /api/editor-fragment but with embeds NOT resolved and no audience filter, so the
        // editor can fork the transclusion into the author's own editable content (after
        // detach the region serializes as real markdown, not a '::: embed' directive).
        // "What the ref points at" is resolved through DocWizEmbed.ResolveReference, the
        // single definition shared with render-time expansion.
        app.MapPost("/api/embed-detach", async (HttpRequest request) =>
        {
            if (NoProject() is { } guard)
                return guard;
            var dto = await request.ReadFromJsonAsync<EmbedDetachDto>();
            if (dto is null || string.IsNullOrWhiteSpace(dto.Ref))
                return Results.BadRequest("Missing embed reference.");
            var (project, _) = LoadParsed(holder.Root!);
            try
            {
                var section = DocWizEmbed.ResolveReference(dto.Ref, project);
                var segments = EditorDoc.BuildSegments(project, section.Markdown, audience: null, resolveEmbeds: false);
                return Results.Json(new { segments });
            }
            catch (Exception ex) when (ex is DocWizEmbed.EmbedException or TransclusionResolver.TransclusionException)
            {
                return Results.BadRequest(ex.Message);
            }
        });

        // Dev support for the fidelity harness (L2): renders a markdown snippet through
        // the one pipeline. Read-only; used to prove serialize(parse(x)) ≡ x semantically.
        app.MapPost("/api/dev/render-block", async (HttpRequest request) =>
        {
            var dto = await request.ReadFromJsonAsync<PreviewDto>();
            var pipeline = DocWizMarkdown.CreatePipeline();
            var rendered = DocWiz.Core.Markdown.BlockSegmenter.SegmentAndRender(dto?.Markdown ?? "", pipeline);
            return Results.Json(new
            {
                html = string.Concat(rendered.Where(r => r.Html is not null).Select(r => r.Html)),
                blocks = rendered.Where(r => r.Segment.Kind == DocWiz.Core.Markdown.SegmentKind.Block)
                    .Select(r => new { type = r.Segment.BlockType, html = r.Html }),
            });
        });

        // Dev support for the WYSIWYG edit-vs-ship gate (editor2/wysiwyg.html): renders BOTH
        // surfaces of one view through the SAME ViewRenderer.RenderSlides — the ship deck
        // (edit:false) and the live authoring fragment (edit:true) — so the gate harness can
        // measure per-region geometry on each and prove edit DOM == ship DOM by construction.
        // Read-only; never part of the product UI (only the committed harness page fetches it).
        app.MapGet("/api/dev/wysiwyg", (string view) =>
        {
            if (NoProject() is { } guard)
                return guard;
            var full = ResolveViewPath(holder.Root!, view, mustExist: true);
            if (full is null)
                return Results.BadRequest("Not a view file inside this project.");
            var def = YamlIn.Deserialize<ViewDefinition>(File.ReadAllText(full)) ?? new ViewDefinition();
            var (project, pipeline) = LoadParsed(holder.Root!);
            return Results.Json(new
            {
                id = def.Id,
                title = def.Title,
                audience = def.Audience,
                mode = def.Mode,
                ship = ViewRenderer.RenderSlides(project, def, pipeline, edit: false),
                edit = ViewRenderer.RenderSlides(project, def, pipeline, edit: true),
            });
        });

        // Reading view (ADR-004 enforcement): pipeline-pure render of the SAVED
        // chapter, on demand — the live surface's verification surface. (The route
        // prefix dates from the old draft-preview pane it replaced at the M5 flip.)
        app.MapGet("/preview-root/chapter", (string file, HttpResponse res) =>
        {
            if (NoProject() is { } guard)
                return guard;
            NoStore(res);
            var path = ResolveChapterPath(holder.Root!, file);
            if (path is null)
                return Results.BadRequest("Not a chapter file inside this project.");
            // I8: LinkRewriter only needs FindChapter existence + numbering needs chapter
            // paths — neither needs every chapter's parsed AST, so skip ParseAll. The single
            // target chapter is parsed once below.
            var project = ProjectLoader.Load(holder.Root!);
            var pipeline = DocWizMarkdown.CreatePipeline();
            var (_, body) = FrontmatterParser.Split(File.ReadAllText(path));
            var doc = Markdig.Markdown.Parse(body, pipeline);
            LinkRewriter.Rewrite(doc, project, id => $"{id}.html");
            var articleAttrs = "";
            if (project.Manifest.Numbering)
            {
                var n = EditorDoc.ChapterNumber(project, path);
                if (n > 0)
                    articleAttrs = $" data-num style=\"--dwch:'{n}'\"";
            }
            var html =
                "<!DOCTYPE html><html><head><meta charset=\"UTF-8\">" + ThemeGuardScript +
                "<base href=\"/preview-root/\">" +
                $"<style>{SiteTemplates.Read("tokens.css")}</style><style>{SiteTemplates.Read("site.css")}</style>" +
                "<style>body{display:block;padding:24px 32px}</style></head><body><article" + articleAttrs + ">" +
                DocWizMarkdown.RenderHtml(doc, pipeline) +
                "</article></body></html>";
            return Results.Content(html, "text/html");
        });

        app.MapGet("/api/check", () =>
        {
            if (NoProject() is { } guard)
                return guard;
            var (project, _) = LoadParsed(holder.Root!);
            return Results.Json(ProjectValidator.Validate(project)
                .Select(i => new { severity = i.Severity.ToString(), i.Source, i.Message }));
        });

        app.MapPost("/api/build", () =>
        {
            if (NoProject() is { } guard)
                return guard;
            var (project, pipeline) = LoadParsed(holder.Root!);
            var issues = ProjectValidator.Validate(project);
            if (issues.Any(i => i.Severity == IssueSeverity.Error))
                return Results.Json(new { ok = false, issues = Shape(issues) });
            HandoverBuilder.Build(project, pipeline, Path.Combine(holder.Root!, HandoverOutDir));
            return Results.Json(new { ok = true, issues = Shape(issues), url = "/handover" });

            static object Shape(List<ValidationIssue> issues) =>
                issues.Select(i => new { severity = i.Severity.ToString(), i.Source, i.Message });
        });

        // Unmounts the current project — the UI reloads to the welcome screen.
        app.MapPost("/api/close-project", () =>
        {
            holder.Root = null;
            return Results.Ok();
        });

        // Local desktop app convenience — opens the mounted project in Explorer.
        app.MapPost("/api/open-folder", () =>
        {
            if (NoProject() is { } guard)
                return guard;
            var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            psi.ArgumentList.Add(holder.Root!);
            using (Process.Start(psi)) { }
            return Results.Ok();
        });

        // Start a background PDF export and return its job id immediately.
        app.MapPost("/api/export-pdf", (ExportPdfDto dto) =>
        {
            if (NoProject() is { } guard) return guard;
            var format = string.Equals(dto.Format, "html", StringComparison.OrdinalIgnoreCase)
                ? ExportFormat.Html : ExportFormat.Pdf;
            var edge = BrowserLauncher.FindEdge();
            if (format == ExportFormat.Pdf && edge is null)
                return Results.BadRequest("Microsoft Edge was not found; it is required for PDF export.");
            var outDir = string.IsNullOrWhiteSpace(dto.Path)
                ? Path.Combine(holder.Root!, "output", "Export")
                : dto.Path!;
            var selection = new ExportSelection
            {
                Manual = dto.Manual,
                ChapterIds = dto.ChapterIds ?? [],
                ViewIds = dto.ViewIds ?? [],
                Format = format,
            };
            var job = pdfJobs.Start(holder.Root!, selection, outDir, edge);
            return Results.Json(new { id = job.Id });
        });

        // Poll a PDF export job — running | done | error, plus per-item outcomes when done.
        app.MapGet("/api/export-pdf/status", (string id) =>
        {
            var job = pdfJobs.Get(id);
            if (job is null) return Results.NotFound();
            return Results.Json(new
            {
                status = job.Status,
                outDir = job.OutDir,
                error = job.Error,
                items = job.Result?.Items.Select(i => new { i.Label, i.Success, i.Error }),
            });
        });

        // Open any folder in Explorer (used to reveal the PDF export output dir).
        app.MapPost("/api/open-path", (OpenPathDto dto) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Path)) return Results.BadRequest("No such folder.");
            var full = Path.GetFullPath(dto.Path);
            if (!Directory.Exists(full)) return Results.BadRequest("No such folder.");
            var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            psi.ArgumentList.Add(full);
            using (Process.Start(psi)) { }
            return Results.Ok();
        });

        app.MapPost("/api/shutdown", () =>
        {
            // Delayed so the HTTP response reaches the browser before Kestrel stops.
            _ = Task.Run(async () =>
            {
                await Task.Delay(300);
                app.Lifetime.StopApplication();
            });
            return Results.Ok();
        });

        app.MapGet("/api/settings", () =>
            Results.Json(new { windowMode = AppSettings.Load().WindowMode }));

        app.MapPost("/api/settings", async (HttpRequest req) =>
        {
            var dto = await req.ReadFromJsonAsync<SettingsDto>();
            // Clamp to a known mode (native | app | browser); unknown/garbage → native.
            var mode = new AppSettings { WindowMode = dto?.WindowMode ?? "" }.NormalizedWindowMode;
            AppSettings.Save(new AppSettings { WindowMode = mode });
            return Results.Ok();
        });

        try
        {
            await app.StartAsync();
        }
        catch (IOException)
        {
            // Port already taken — almost certainly another DocWiz editor. Instead of
            // dying silently (WinExe has no console), take the user to the running one.
            Console.Error.WriteLine($"DocWiz is already running at {url} — opening it.");
            if (openBrowser)
                BrowserLauncher.Launch(url, AppSettings.Load().WindowMode);
            onUiLaunched?.Invoke(); // dismiss the startup splash
            return 0;
        }
        Console.WriteLine($"DocWiz editor running at {url}" +
                          (holder.Root is null ? "  (welcome screen)" : $"  (project: {holder.Root})"));
        Console.WriteLine("Stop it with the Quit button in the editor (or Ctrl+C here).");
        Action? closeNativeWindow = null;
        if (openBrowser)
        {
            var mode = AppSettings.Load().NormalizedWindowMode;
            if (mode == "native" && launchNativeWindow is not null)
            {
                // The native DocWiz window: painted → dismiss the splash; closed → quit the editor.
                closeNativeWindow = launchNativeWindow(
                    url,
                    () => onUiLaunched?.Invoke(),
                    () => app.Lifetime.StopApplication());
            }

            if (closeNativeWindow is null)
            {
                // Native not requested, or WebView2 unavailable → chromeless Edge app-window
                // (mapping "native" → "app"), or the plain default-browser tab.
                var win = BrowserLauncher.Launch(url, mode == "native" ? "app" : mode);
                if (win is not null)
                {
                    // Close the app window == quit the editor.
                    win.Exited += (_, _) => app.Lifetime.StopApplication();
                    if (win.HasExited) app.Lifetime.StopApplication(); // window closed before we subscribed
                    // Keep the splash up until the app window is actually painted, then dismiss.
                    _ = Task.Run(() => { WaitForWindow(win); onUiLaunched?.Invoke(); });
                }
                else
                {
                    onUiLaunched?.Invoke(); // default browser tab — nothing of ours to wait on
                }
            }
        }
        else
        {
            onUiLaunched?.Invoke(); // headless (--no-browser): release any waiter
        }
        await app.WaitForShutdownAsync();
        closeNativeWindow?.Invoke(); // host stopped (Quit/Ctrl+C) → close the native window so we exit cleanly
        return 0;
    }

    /// <summary>Blocks until the launched browser process has a top-level window (or a few
    /// seconds elapse), so the splash bridges the Edge cold-start gap without lingering.</summary>
    private static void WaitForWindow(System.Diagnostics.Process win)
    {
        try
        {
            for (var i = 0; i < 80 && !win.HasExited; i++) // ~8s at 100ms, then give up
            {
                win.Refresh();
                if (win.MainWindowHandle != IntPtr.Zero)
                    return;
                Thread.Sleep(100);
            }
        }
        catch
        {
            // Best-effort: on any failure just let the splash be dismissed.
        }
    }

    /// <summary>Validates and mounts a project folder; records it in the recent list.
    /// Returns an error message, or null on success.</summary>
    private static string? Mount(ProjectHolder holder, string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(Path.Combine(full, ProjectLayout.ManifestFileName)))
            return $"'{full}' is not a DocWiz project ({ProjectLayout.ManifestFileName} not found).";
        try
        {
            var project = ProjectLoader.Load(full);
            holder.Root = full;
            RecentProjects.Add(full, project.Manifest.Project, project.Manifest.Code);
            return null;
        }
        catch (DocWizLoadException ex)
        {
            return ex.Message;
        }
    }

    private static (DocProject Project, Markdig.MarkdownPipeline Pipeline) LoadParsed(string root)
    {
        var project = ProjectLoader.Load(root);
        var pipeline = DocWizMarkdown.CreatePipeline();
        DocWizMarkdown.ParseAll(project, pipeline);
        return (project, pipeline);
    }

    /// <summary>Serves a file from a subfolder of the mounted project (traversal-guarded).</summary>
    private static IResult ProjectFile(ProjectHolder holder, string subdir, string relPath)
    {
        if (holder.Root is null)
            return Results.NotFound();
        var baseDir = Path.GetFullPath(Path.Combine(holder.Root, subdir));
        var full = Path.GetFullPath(Path.Combine(baseDir, relPath));
        if (!full.StartsWith(baseDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(full))
            return Results.NotFound();
        if (!ContentTypes.TryGetContentType(full, out var contentType))
            contentType = "application/octet-stream";
        return Results.File(full, contentType);
    }

    private static string RelPath(string root, string full) =>
        Path.GetRelativePath(root, full).Replace('\\', '/');

    // No-flash theme guard injected into the server-built preview/present pages so they follow
    // the editor's saved theme (same origin → same localStorage['dw-theme']). Mirrors the
    // handover templates' inline guard; defined ONCE here rather than pasted into each page
    // (ADR-008 — the HTML-template copies in base/slides/index.html are a separate cascade).
    private const string ThemeGuardScript =
        "<script>(function(){var t=localStorage.getItem('dw-theme');" +
        "if(!t)t=window.matchMedia('(prefers-color-scheme: dark)').matches?'dark':'light';" +
        "document.documentElement.setAttribute('data-theme',t);})();</script>";

    /// <summary>Full standalone HTML for a view's READ/present surface — the slides deck or the
    /// filtered document, rendered through the same <see cref="ViewRenderer"/> as the handover
    /// (ADR-004, no second renderer). Shared by POST /api/preview-view (live payload) and
    /// GET /preview-root/view (saved view by id) so the in-app preview and the open-in-browser
    /// present deck can never drift.</summary>
    private static string RenderViewPage(DocProject project, ViewDefinition view, Markdig.MarkdownPipeline pipeline)
    {
        if (view.Mode == "slides")
            return
                "<!DOCTYPE html><html><head><meta charset=\"UTF-8\">" + ThemeGuardScript +
                "<base href=\"/preview-root/\">" +
                $"<style>{SiteTemplates.Read("tokens.css")}</style><style>{SiteTemplates.Read("site.css")}</style>" +
                "<style>body{background:var(--dw-chrome)} .slide-bar,.hint{display:none}</style>" +
                "</head><body>" +
                "<section class=\"slides-surface\" id=\"slidesSurface\">" +
                "<div class=\"slide-stage\" id=\"slideStage\"><div class=\"slide-frame\" id=\"slideFrame\">" +
                ViewRenderer.RenderSlides(project, view, pipeline) +
                "</div></div>" +
                "<div class=\"slide-bar\"><button id=\"prevBtn\" hidden></button><span class=\"ctr\" id=\"slideCtr\"></span>" +
                "<button id=\"nextBtn\" hidden></button><button id=\"fsBtn\" hidden></button></div><div class=\"hint\"></div>" +
                "</section>" +
                $"<script>{SiteTemplates.Read("slides.js")}</script></body></html>";

        return
            "<!DOCTYPE html><html><head><meta charset=\"UTF-8\">" + ThemeGuardScript +
            "<base href=\"/preview-root/\">" +
            $"<style>{SiteTemplates.Read("tokens.css")}</style><style>{SiteTemplates.Read("site.css")}</style>" +
            "<style>body{display:block;padding:24px 32px}</style></head><body><article>" +
            ViewRenderer.RenderDocument(project, view, pipeline, linkSources: false) +
            "</article></body></html>";
    }

    /// <summary>Chapter API only touches content/**/*.md inside the project.</summary>
    internal static string? ResolveChapterPath(string root, string relative)
    {
        var full = Path.GetFullPath(relative, root);
        var contentDir = Path.Combine(root, ProjectLayout.ContentFolder) + Path.DirectorySeparatorChar;
        return full.StartsWith(contentDir, StringComparison.OrdinalIgnoreCase)
               && full.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
               && File.Exists(full)
            ? full
            : null;
    }

    /// <summary>View API only touches views/*.yaml inside the project.</summary>
    internal static string? ResolveViewPath(string root, string relative, bool mustExist)
    {
        var full = Path.GetFullPath(relative, root);
        var viewsDir = Path.Combine(root, ProjectLayout.ViewsFolder) + Path.DirectorySeparatorChar;
        var ok = full.StartsWith(viewsDir, StringComparison.OrdinalIgnoreCase)
                 && full.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)
                 && (!mustExist || File.Exists(full));
        return ok ? full : null;
    }

    /// <summary>DTO → model, dropping blank slide fields so the YAML stays clean.</summary>
    private static ViewDefinition ToView(ViewDto dto) => new()
    {
        Id = dto.Id,
        Title = dto.Title,
        Audience = dto.Audience,
        Mode = dto.Mode,
        Slides = (dto.Slides ?? [])
            .Select(s => new SlideDef
            {
                Layout = Blank(s.Layout),
                Include = Blank(s.Include),
                Title = Blank(s.Title),
                Subtitle = Blank(s.Subtitle),
                Markdown = Blank(s.Markdown),
                Regions = NonBlankRegions(s.Regions),
            })
            .ToList(),
    };

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>Drops blank region values (and an all-blank dict) so the saved YAML stays
    /// clean — matching the Blank(...) discipline for the scalar slide fields.</summary>
    private static Dictionary<string, string>? NonBlankRegions(Dictionary<string, string>? regions)
    {
        if (regions is not { Count: > 0 })
            return null;
        var kept = regions.Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        return kept.Count > 0 ? kept : null;
    }

    /// <summary>YAML API only touches docwiz.yaml and views/*.yaml.</summary>
    internal static string? ResolveYamlPath(string root, string relative)
    {
        var full = Path.GetFullPath(relative, root);
        if (string.Equals(full, Path.Combine(root, ProjectLayout.ManifestFileName), StringComparison.OrdinalIgnoreCase))
            return full;
        var viewsDir = Path.Combine(root, ProjectLayout.ViewsFolder) + Path.DirectorySeparatorChar;
        return full.StartsWith(viewsDir, StringComparison.OrdinalIgnoreCase)
               && full.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)
               && File.Exists(full)
            ? full
            : null;
    }

    /// <summary>Recursive file + folder listing of a project subtree, relative
    /// forward-slash paths, README.md excluded.</summary>
    private static object ListProjectFiles(string dir)
    {
        if (!Directory.Exists(dir))
            return new { files = Array.Empty<string>(), folders = Array.Empty<string>() };

        var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(dir, f).Replace('\\', '/'))
            .Where(n => !string.Equals(Path.GetFileName(n), "README.md", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var folders = Directory.GetDirectories(dir, "*", SearchOption.AllDirectories)
            .Select(d => Path.GetRelativePath(dir, d).Replace('\\', '/'))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new { files, folders };
    }

    /// <summary>Saves an uploaded file into baseDir/folder (subfolder created, traversal
    /// guarded), returning the relative forward-slash path, or null if the folder escapes
    /// the project.</summary>
    private static async Task<string?> SaveUpload(string baseDir, string? folder, string name, HttpRequest request)
    {
        var targetDir = baseDir;
        if (!string.IsNullOrWhiteSpace(folder))
        {
            var candidate = Path.GetFullPath(Path.Combine(baseDir, folder.Replace('\\', '/')));
            var root = Path.GetFullPath(baseDir);
            if (candidate != root && !candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return null;
            targetDir = candidate;
        }
        Directory.CreateDirectory(targetDir);

        var fileName = UniqueName(targetDir, Path.GetFileName(name));
        await using (var output = File.Create(Path.Combine(targetDir, fileName)))
            await request.Body.CopyToAsync(output);
        return Path.GetRelativePath(baseDir, Path.Combine(targetDir, fileName)).Replace('\\', '/');
    }

    private static string UniqueName(string dir, string name)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        var candidate = name;
        for (var i = 1; File.Exists(Path.Combine(dir, candidate)); i++)
            candidate = $"{stem}-{i}{ext}";
        return candidate;
    }

    private static void NoStore(HttpResponse res) =>
        res.Headers.CacheControl = "no-store, no-cache, must-revalidate";

    private static IResult EmbeddedFile(string path)
    {
        var stream = EmbeddedUi.Find(path);
        if (stream is null)
            return Results.NotFound();

        var contentType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" => "text/html",
            ".css" => "text/css",
            ".js" => "text/javascript",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            _ => "application/octet-stream",
        };
        return Results.Stream(stream, contentType);
    }
}
