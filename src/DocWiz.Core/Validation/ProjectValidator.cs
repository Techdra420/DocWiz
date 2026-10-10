using System.Text.RegularExpressions;
using DocWiz.Core.Markdown;
using DocWiz.Core.Models;
using Markdig.Extensions.CustomContainers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace DocWiz.Core.Validation;

public enum IssueSeverity { Warning, Error }

public sealed record ValidationIssue(IssueSeverity Severity, string Source, string Message)
{
    public override string ToString() => $"{Severity.ToString().ToUpperInvariant(),-7} {Source}: {Message}";
}

/// <summary>Content-level checks behind `docwiz check`. Requires chapters to be parsed
/// (DocWizMarkdown.ParseAll) so link/anchor/container checks can walk the AST.</summary>
public static class ProjectValidator
{
    public static List<ValidationIssue> Validate(DocProject project)
    {
        var issues = new List<ValidationIssue>();
        var knownAudiences = project.Manifest.Audiences.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);

        ValidateManifest(project, knownAudiences, issues);
        ValidateChapters(project, knownAudiences, issues);
        ValidateViews(project, knownAudiences, issues);
        return issues;
    }

    private static void ValidateManifest(DocProject project, HashSet<string> knownAudiences, List<ValidationIssue> issues)
    {
        var src = ProjectLayout.ManifestFileName;
        if (string.IsNullOrWhiteSpace(project.Manifest.Project))
            issues.Add(new(IssueSeverity.Error, src, "'project' is required."));
        if (string.IsNullOrWhiteSpace(project.Manifest.Code))
            issues.Add(new(IssueSeverity.Error, src, "'code' is required."));
        if (project.Manifest.Audiences.Count == 0)
            issues.Add(new(IssueSeverity.Error, src, "at least one audience must be defined."));
        if (knownAudiences.Count != project.Manifest.Audiences.Count)
            issues.Add(new(IssueSeverity.Error, src, "duplicate audience ids."));

        foreach (var (key, path) in project.Manifest.ExternalDocs)
        {
            if (!ProjectLayout.IsValidExternalKey(key))
            {
                issues.Add(new(IssueSeverity.Error, src,
                    $"external key '{key}' must be a plain lowercase identifier (a-z, 0-9, _) — it becomes a folder and file name."));
                continue; // don't path-resolve a malformed key (path traversal)
            }
            var full = Path.GetFullPath(path, project.Root);
            if (!Directory.Exists(full))
                issues.Add(new(IssueSeverity.Warning, src, $"external.{key} points to a missing folder: {path}"));
            else if (ProjectLayout.ResolveExternalEntry(full) is null)
                issues.Add(new(IssueSeverity.Warning, src, $"external.{key} has no entry page ({string.Join(" or ", ProjectLayout.ExternalEntryCandidates)}) — the tab will open a blank page: {path}"));
        }
    }

    private static void ValidateChapters(DocProject project, HashSet<string> knownAudiences, List<ValidationIssue> issues)
    {
        var seenIds = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var chapter in project.Chapters)
        {
            var src = Path.GetRelativePath(project.Root, chapter.FilePath);

            if (string.IsNullOrWhiteSpace(chapter.Id))
                issues.Add(new(IssueSeverity.Error, src, "frontmatter 'id' is required (stable ID, ADR-005)."));
            else if (!seenIds.TryAdd(chapter.Id, src))
                issues.Add(new(IssueSeverity.Error, src, $"duplicate chapter id '{chapter.Id}' (also in {seenIds[chapter.Id]})."));

            if (chapter.Audiences.Count == 0)
                issues.Add(new(IssueSeverity.Warning, src, "frontmatter 'audiences' is empty — chapter will appear in no filtered view."));
            foreach (var a in chapter.Audiences.Where(a => !knownAudiences.Contains(a)))
                issues.Add(new(IssueSeverity.Error, src, $"unknown audience '{a}' in frontmatter."));

            if (chapter.Parsed is null)
            {
                issues.Add(new(IssueSeverity.Error, src, "chapter was not parsed — internal error."));
                continue;
            }

            ValidateContainers(chapter.Parsed, knownAudiences, src, issues);
            ValidateLinks(chapter.Parsed, project, src, issues);
            ValidateTableWidths(chapter.Parsed, src, issues);
            if (project.Manifest.Numbering)
                ValidateNoManualNumbers(chapter, src, issues);
        }
    }

    private static readonly Regex ManualNumberPrefix = new(@"^\d+(\.\d+)*[\s.]", RegexOptions.Compiled);

    /// <summary>With automatic numbering on, a typed "1.2 " prefix (common in content
    /// migrated from Word/PDF manuals) would double-number in the handover — and the
    /// typed numbers rot as soon as chapters are reordered.</summary>
    private static void ValidateNoManualNumbers(Chapter chapter, string src, List<ValidationIssue> issues)
    {
        foreach (var heading in HeadingScanner.Scan(chapter.Markdown))
        {
            if (ManualNumberPrefix.IsMatch(heading.Text))
                issues.Add(new(IssueSeverity.Warning, src,
                    $"heading \"{heading.Text}\" starts with a typed number — numbering is automatic " +
                    "(docwiz.yaml: numbering), so it would show twice in the handover. " +
                    "Remove the typed prefix, or set 'numbering: false'."));
        }
    }

    private static readonly Regex WidthTokenPattern = new(
        @"^([0-9]+(\.[0-9]+)?(%|px|em|rem|ch|fr)?|auto)$", RegexOptions.Compiled);

    /// <summary>{widths="30% 70%"} above a table sizes its columns (CONTENT_MODEL).
    /// Warn when the token count does not match the column count or a token is not
    /// a CSS length — the renderer drops invalid tokens silently.</summary>
    private static void ValidateTableWidths(MarkdownDocument doc, string src, List<ValidationIssue> issues)
    {
        foreach (var table in doc.Descendants<Markdig.Extensions.Tables.Table>())
        {
            var props = table.TryGetAttributes()?.Properties;
            var widthsIndex = props?.FindIndex(p => p.Key == "widths") ?? -1;
            var widths = widthsIndex >= 0 ? props![widthsIndex].Value : null;
            if (string.IsNullOrWhiteSpace(widths))
                continue;
            var tokens = widths.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            // Column count from the header row — Markdig's ColumnDefinitions can carry
            // a padding entry beyond the real columns.
            var columns = table.Count > 0 && table[0] is Markdig.Extensions.Tables.TableRow headerRow
                ? headerRow.Count
                : table.ColumnDefinitions.Count;
            if (tokens.Length != columns)
                issues.Add(new(IssueSeverity.Warning, src,
                    $"table widths attribute has {tokens.Length} value(s) for {columns} column(s) — " +
                    "give one width per column, e.g. {widths=\"30% 70%\"}."));
            foreach (var t in tokens.Where(t => !WidthTokenPattern.IsMatch(t)))
                issues.Add(new(IssueSeverity.Warning, src,
                    $"table widths value '{t}' is not a plain CSS length (use e.g. 30%, 240px or auto) — it will be ignored."));
        }
        ValidateTableWidth(doc, src, issues);
    }

    /// <summary>{width="50%"} above a table sizes the whole table box (CONTENT_MODEL) —
    /// a single CSS length, distinct from the per-column {widths="…"}. Warn (and the
    /// renderer ignores it) when it is not a plain length.</summary>
    private static void ValidateTableWidth(MarkdownDocument doc, string src, List<ValidationIssue> issues)
    {
        foreach (var table in doc.Descendants<Markdig.Extensions.Tables.Table>())
        {
            var props = table.TryGetAttributes()?.Properties;
            var widthIndex = props?.FindIndex(p => p.Key == "width") ?? -1;
            var width = widthIndex >= 0 ? props![widthIndex].Value : null;
            if (!string.IsNullOrWhiteSpace(width) && !WidthTokenPattern.IsMatch(width))
                issues.Add(new(IssueSeverity.Warning, src,
                    $"table width value '{width}' is not a single CSS length (use e.g. 50%, 480px or auto) — it will be ignored."));
        }
    }

    private static void ValidateContainers(MarkdownDocument doc, HashSet<string> knownAudiences, string src, List<ValidationIssue> issues)
    {
        foreach (var container in doc.Descendants<CustomContainer>())
        {
            if (!DocWizContainerRenderer.KnownContainers.Contains(container.Info ?? ""))
            {
                issues.Add(new(IssueSeverity.Warning, src,
                    $"unknown container ':::{container.Info}' — known elements: " +
                    string.Join(", ", DocWizContainerRenderer.KnownContainers) + " (ADR-004)."));
                continue;
            }

            if (string.Equals(container.Info, DocWizGroupExtension.ContainerName, StringComparison.OrdinalIgnoreCase)
                && container.TryGetAttributes()?.Id is null)
                issues.Add(new(IssueSeverity.Warning, src,
                    "a group has no {#id} — it can't be referenced or embedded."));

            if (!AudienceFilter.IsAudienceContainer(container))
                continue;

            var args = AudienceFilter.AudienceIds(container);
            if (args.Length == 0)
                issues.Add(new(IssueSeverity.Error, src, "'::: audience' without audience ids."));
            foreach (var a in args.Where(a => !knownAudiences.Contains(a)))
                issues.Add(new(IssueSeverity.Error, src, $"unknown audience '{a}' in '::: audience' container."));
        }
    }

    private static void ValidateLinks(MarkdownDocument doc, DocProject project, string src, List<ValidationIssue> issues)
    {
        foreach (var link in doc.Descendants().OfType<LinkInline>())
        {
            var url = link.Url ?? "";

            if (url.StartsWith("dw:", StringComparison.OrdinalIgnoreCase))
            {
                ValidateDwLink(url, project, src, issues);
                continue;
            }

            if (url.Contains("://", StringComparison.Ordinal) || url.StartsWith('#'))
                continue;

            if (link.IsImage)
            {
                // Resolve the path under assets/ (subfolders included), falling back to a
                // bare filename for images written without the assets/ prefix.
                var rel = LinkRewriter.TryGetLocalFile(url, ProjectLayout.AssetsFolder, out var assetRel)
                    ? assetRel
                    : Path.GetFileName(url);
                if (!File.Exists(Path.Combine(project.AssetsDir, rel)))
                    issues.Add(new(IssueSeverity.Warning, src, $"image '{rel}' not found in assets/ — placeholder will be rendered."));
            }
            else if (LinkRewriter.TryGetLocalFile(url, ProjectLayout.AttachmentsFolder, out var attachRel))
            {
                if (!File.Exists(Path.Combine(project.AttachmentsDir, attachRel)))
                    issues.Add(new(IssueSeverity.Warning, src, $"attachment '{attachRel}' not found in attachments/."));
            }
        }
    }

    /// <summary>Validates one <c>dw:chapter#anchor</c> link target — the single source for the
    /// dangling-link check (ADR-008), shared by chapter prose links (<see cref="ValidateLinks"/>)
    /// and inline links inside view region markdown (<see cref="ValidateRegionLinks"/>).</summary>
    private static void ValidateDwLink(string url, DocProject project, string src, List<ValidationIssue> issues)
    {
        var (chapterId, anchor) = LinkRewriter.SplitRef(url[3..]);
        var target = project.FindChapter(chapterId);
        if (target is null)
            issues.Add(new(IssueSeverity.Error, src, $"link '{url}': no chapter with id '{chapterId}'."));
        else if (anchor is not null && !target.Anchors.Contains(anchor))
            issues.Add(new(IssueSeverity.Error, src, $"link '{url}': chapter '{chapterId}' has no anchor '#{anchor}'."));
    }

    private static void ValidateViews(DocProject project, HashSet<string> knownAudiences, List<ValidationIssue> issues)
    {
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        // One pipeline (ADR-004) reused to parse region markdown for embed-ref discovery.
        var pipeline = DocWizMarkdown.CreatePipeline();

        foreach (var view in project.Views)
        {
            var src = Path.GetRelativePath(project.Root, view.FilePath);

            if (string.IsNullOrWhiteSpace(view.Id))
                issues.Add(new(IssueSeverity.Error, src, "'id' is required."));
            else if (!seenIds.Add(view.Id))
                issues.Add(new(IssueSeverity.Error, src, $"duplicate view id '{view.Id}'."));

            if (!knownAudiences.Contains(view.Audience))
                issues.Add(new(IssueSeverity.Error, src, $"unknown audience '{view.Audience}'."));
            if (view.Mode is not ("slides" or "document"))
                issues.Add(new(IssueSeverity.Error, src, $"mode must be 'slides' or 'document', got '{view.Mode}'."));

            for (var i = 0; i < view.Slides.Count; i++)
            {
                var slide = view.Slides[i];
                var where = $"{src} (slide {i + 1})";

                // Inline dw: links in any region/markdown body. The per-branch checks below only
                // cover ::: embed includes, so a plain [text](dw:chapter#anchor) link went
                // unvalidated and rendered broken in the handover. EffectiveRegions() covers
                // explicit-region and legacy markdown slides uniformly.
                ValidateRegionLinks(slide, project, where, pipeline, issues);

                // Whole-chapter link: expands at render time into one slide per H2/H3. Mutually
                // exclusive with the single-slide fields; the chapter id must resolve.
                if (!string.IsNullOrWhiteSpace(slide.Chapter))
                {
                    if (slide.Layout is not null || slide.Regions is { Count: > 0 }
                        || !string.IsNullOrWhiteSpace(slide.Include) || !string.IsNullOrWhiteSpace(slide.Markdown))
                        issues.Add(new(IssueSeverity.Error, where,
                            "a '- chapter:' slide cannot also set layout/regions/include/markdown."));
                    if (project.FindChapter(slide.Chapter!) is null)
                        issues.Add(new(IssueSeverity.Error, where,
                            $"chapter '{slide.Chapter}': no chapter with that id."));
                    continue;
                }

                // Legacy single-content slide (no explicit layout/regions): keep the old contract.
                if (slide.Layout is null && (slide.Regions is null || slide.Regions.Count == 0))
                {
                    var hasInclude = !string.IsNullOrWhiteSpace(slide.Include);
                    var hasMarkdown = !string.IsNullOrWhiteSpace(slide.Markdown);
                    if (hasInclude == hasMarkdown)
                    {
                        issues.Add(new(IssueSeverity.Error, where, "each slide needs exactly one of 'include' or 'markdown'."));
                        continue;
                    }
                    if (hasInclude)
                        ValidateInclude(slide.Include!, view, where, project, issues);
                    continue;
                }

                var layout = slide.EffectiveLayout;
                if (!SlideLayout.IsKnown(layout))
                {
                    issues.Add(new(IssueSeverity.Error, where, $"unknown layout '{layout}'."));
                    continue;
                }

                var valid = SlideLayout.RegionsFor(layout);
                if (slide.Regions is not null)
                {
                    foreach (var (name, markdown) in slide.Regions)
                    {
                        if (!valid.Contains(name))
                        {
                            issues.Add(new(IssueSeverity.Error, where, $"region '{name}' is not valid for layout '{layout}'."));
                            continue;
                        }
                        if (string.IsNullOrWhiteSpace(markdown))
                            continue; // empty-region warning is emitted below
                        // Validate each inline embed's target via the same chapter/anchor/audience
                        // checks the legacy include path uses.
                        foreach (var reference in DocWizEmbed.FindRefs(markdown, pipeline))
                            ValidateInclude(reference, view, $"{where} region '{name}'", project, issues);
                    }
                }

                foreach (var name in valid)
                    if (slide.Regions is null
                        || !slide.Regions.TryGetValue(name, out var markdown)
                        || string.IsNullOrWhiteSpace(markdown))
                        issues.Add(new(IssueSeverity.Warning, where, $"layout '{layout}' region '{name}' is empty."));
            }
        }
    }

    /// <summary>Validates inline <c>dw:</c> links inside a slide's region markdown (and legacy
    /// markdown body). The embed-include checks cover only <c>::: embed</c> directives, not plain
    /// <c>[text](dw:chapter#anchor)</c> links; <see cref="SlideDef.EffectiveRegions"/> gives the
    /// same coverage for explicit-region and legacy markdown slides.</summary>
    private static void ValidateRegionLinks(SlideDef slide, DocProject project, string where,
        Markdig.MarkdownPipeline pipeline, List<ValidationIssue> issues)
    {
        foreach (var (name, markdown) in slide.EffectiveRegions())
        {
            if (string.IsNullOrWhiteSpace(markdown))
                continue;
            var doc = Markdig.Markdown.Parse(markdown, pipeline);
            foreach (var link in doc.Descendants().OfType<LinkInline>())
            {
                var url = link.Url ?? "";
                if (url.StartsWith("dw:", StringComparison.OrdinalIgnoreCase))
                    ValidateDwLink(url, project, $"{where} region '{name}'", issues);
            }
        }
    }

    private static void ValidateInclude(string include, ViewDefinition view, string where,
        DocProject project, List<ValidationIssue> issues)
    {
        var (chapterId, anchor) = LinkRewriter.SplitRef(include);
        var target = project.FindChapter(chapterId);
        if (target is null)
        {
            issues.Add(new(IssueSeverity.Error, where, $"include '{include}': no chapter with id '{chapterId}'."));
            return;
        }
        if (anchor is not null && !target.Anchors.Contains(anchor))
            issues.Add(new(IssueSeverity.Error, where, $"include '{include}': chapter '{chapterId}' has no anchor '#{anchor}'."));
        if (!target.Audiences.Contains(view.Audience))
            issues.Add(new(IssueSeverity.Warning, where, $"include '{include}': chapter '{chapterId}' is not tagged for audience '{view.Audience}'."));
    }
}
