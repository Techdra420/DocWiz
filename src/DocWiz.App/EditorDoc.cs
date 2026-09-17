using System.Net;
using System.Security.Cryptography;
using System.Text;
using DocWiz.Core.Loading;
using DocWiz.Core.Markdown;
using DocWiz.Core.Models;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace DocWiz.App;

public sealed record EditorSegmentDto(
    string Kind, int Start, int End, string Src, string? BlockType, string? Html,
    object? Meta = null, string? Ref = null, string? Width = null, string? Align = null);

/// <summary>Structured table data for the grid editor (M3): source lines per row
/// enable W3 per-row emission; aligns/widths/id are AST-authoritative.</summary>
public sealed record TableMetaDto(
    string? AttrLine, string HeaderLine, string DelimiterLine, List<string> RowLines,
    List<string?> Aligns, string? Widths, string? Width, string? Align, string? ExplicitId, int Number,
    string? Caption = null);

/// <summary>Structured figure data for the image tools (M3).</summary>
public sealed record FigureMetaDto(
    string Src, string Alt, string? Width, string? Align, string? ExplicitId, int Number,
    bool ShowCaption = true);

public sealed record EditorDocDto(
    string File, string Id, string Title, string Discipline, List<string> Audiences,
    bool Numbering, int ChapterNumber, string BodyHash, List<EditorSegmentDto> Segments);

/// <summary>One save operation: either copy [CopyStart, CopyEnd) of the original body
/// verbatim, or insert <see cref="Text"/>.</summary>
public sealed record SaveSegmentDto(int? CopyStart, int? CopyEnd, string? Text);

public sealed record SaveEditorDocDto(
    string BaseBodyHash, string Title, string Discipline, List<string> Audiences,
    List<SaveSegmentDto> Segments);

/// <summary>
/// Payload builder for the live editing surface (ADR-007): the chapter body segmented
/// by the one pipeline, each block carrying its verbatim source span plus pipeline
/// HTML rendered for the editor server's URL space. The body hash anchors the M2
/// byte-surgical save against concurrent external edits.
/// </summary>
public static class EditorDoc
{
    /// <summary>The editor server's URL route for project assets (mirrors
    /// EditorServer's /assets/{**path} mapping and the editor schema's
    /// '/assets/' prefix handling in JS).</summary>
    private const string AssetRoute = "/assets/";

    public static EditorDocDto Build(DocProject project, string file)
    {
        var path = Path.GetFullPath(Path.Combine(project.Root, file));
        var chapter = project.Chapters.FirstOrDefault(c =>
            string.Equals(Path.GetFullPath(c.FilePath), path, StringComparison.OrdinalIgnoreCase))
            ?? throw new DocWizLoadException($"'{file}' is not a chapter of this project.");

        var (_, rawBody) = FrontmatterParser.Split(File.ReadAllText(path));
        var body = rawBody.TrimStart('\n');

        var segments = BuildSegments(project, body, audience: null, resolveEmbeds: false);

        return new EditorDocDto(
            file,
            chapter.Id,
            chapter.Title,
            chapter.Discipline,
            chapter.Audiences,
            project.Manifest.Numbering,
            ChapterNumber(project, path),
            ComputeBodyHash(body),
            segments);
    }

    /// <summary>
    /// Segments and renders a markdown body into editor-surface DTOs through the ONE
    /// pipeline (ADR-004) — the shared engine behind both the chapter editor
    /// (<see cref="Build"/>) and the slide-zone fragment endpoint (POST
    /// /api/editor-fragment). Source spans are verbatim; HTML is pipeline-rendered for
    /// the editor server's URL space; table/figure blocks carry their structured meta.
    /// <para>
    /// <paramref name="resolveEmbeds"/> (slide zones): a lone <c>::: embed dw:ref :::</c>
    /// block is rendered RESOLVED (so it displays faithfully) but emitted with
    /// blockType "embed", the resolved HTML, and the bare "chapter#anchor" ref, so
    /// ingest.js builds the dedicated <c>embed</c> node (Task 9) that displays the
    /// transclusion and round-trips back to the directive from its ref.
    /// <paramref name="audience"/> filters the embedded content to the view's
    /// audience, matching <see cref="DocWiz.Build.ViewRenderer"/>. Chapters pass
    /// resolveEmbeds=false / audience=null — their rendering is unchanged.
    /// </para>
    /// </summary>
    public static List<EditorSegmentDto> BuildSegments(
        DocProject project, string body, string? audience, bool resolveEmbeds)
    {
        var pipeline = DocWizMarkdown.CreatePipeline();
        var rendered = BlockSegmenter.SegmentAndRender(body, pipeline, doc => RewriteForSurface(doc, project));

        var segments = new List<EditorSegmentDto>(rendered.Count);
        int tableNo = 0, figureNo = 0;
        foreach (var r in rendered)
        {
            // A lone embed directive: render it resolved for display (seg.Html) but emit
            // blockType "embed" plus the bare "chapter#anchor" ref (Ref) — the
            // serialization source of truth. ingest.js builds the dedicated embed node
            // from {ref, html}; serialize.js re-emits the canonical directive from ref, so
            // a clean embed round-trips byte-stably. The ref is found through DocWizEmbed
            // (parse-based, the one definition of "what is an embed") — the client never
            // parses the directive (Task 9).
            if (resolveEmbeds && r.Segment.Kind == SegmentKind.Block
                && r.Segment.BlockType == "container"
                && IsLoneEmbed(r.Segment.Source, pipeline))
            {
                // First(): IsLoneEmbed already proved exactly one ref; First() keeps a
                // clean 400 (not a 500) if that invariant is ever loosened.
                var embedRef = DocWizEmbed.FindRefs(r.Segment.Source, pipeline).First();
                var embedDoc = Markdig.Markdown.Parse(r.Segment.Source, pipeline);
                var embedWidth = DocWizEmbed.WidthOf(embedDoc);
                var embedAlign = DocWizEmbed.AlignOf(embedDoc);
                segments.Add(new EditorSegmentDto(
                    "block", r.Segment.Start, r.Segment.End, r.Segment.Source,
                    "embed", RenderEmbedResolved(project, r.Segment.Source, audience, pipeline, embedWidth, embedAlign),
                    Meta: null, Ref: embedRef, Width: embedWidth, Align: embedAlign));
                continue;
            }

            object? meta = r.Segment.BlockType switch
            {
                "table" => TableMeta(r.Segment.Source, pipeline, ++tableNo),
                "figure" => FigureMeta(r.Segment.Source, pipeline, ++figureNo),
                _ => null,
            };
            segments.Add(new EditorSegmentDto(
                r.Segment.Kind == SegmentKind.Block ? "block" : "gap",
                r.Segment.Start, r.Segment.End, r.Segment.Source, r.Segment.BlockType, r.Html, meta));
        }
        return segments;
    }

    /// <summary>True when the source parses to exactly one block and that block is an
    /// embed directive — the same test ViewRenderer uses for its auto-heading, so "is a
    /// lone embed" is defined once (never a hand-rolled regex).</summary>
    private static bool IsLoneEmbed(string source, Markdig.MarkdownPipeline pipeline)
    {
        var doc = Markdig.Markdown.Parse(source, pipeline);
        return doc.Count == 1 && doc[0] is Markdig.Extensions.CustomContainers.CustomContainer
            && DocWizEmbed.FindRefs(doc).Count == 1;
    }

    /// <summary>Renders a region's lone embed directive RESOLVED — embeds expanded, then
    /// audience-filtered and link-rewritten for the editor surface — exactly like
    /// <see cref="DocWiz.Build.ViewRenderer"/> renders a region, so the zone shows what
    /// ships. Asset/attachment URLs go through the same surface rewrite as the rest.
    /// <para>
    /// The resolved section is spliced as multiple document-root siblings (e.g. an h2 +
    /// paragraph + list), so the result is wrapped in a single <c>.dw-embed</c> element:
    /// the embed node-view (<c>EmbedView</c>) renders this wrapper as display-only
    /// content under its chrome pill, while the round-trip source is the node's ref
    /// (the canonical directive). The wrapper is display-only.
    /// </para></summary>
    private static string RenderEmbedResolved(
        DocProject project, string source, string? audience, Markdig.MarkdownPipeline pipeline, string? width, string? align)
    {
        var doc = Markdig.Markdown.Parse(source, pipeline);
        DocWizEmbed.Resolve(doc, project, pipeline);
        if (!string.IsNullOrEmpty(audience))
            AudienceFilter.Apply(doc, audience);
        RewriteForSurface(doc, project);
        return DocWizEmbed.WrapResolved(DocWizMarkdown.RenderHtml(doc, pipeline), width, align);
    }

    private static readonly System.Text.RegularExpressions.Regex AttrLinePattern =
        new(@"^\{[^{}\r\n]+\}[ \t]*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex ExplicitIdPattern =
        new(@"\{[^{}]*#([A-Za-z0-9_-]+)", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Structured table data from the segment's own source (parsed standalone
    /// through the one pipeline — never parsed in the browser).</summary>
    internal static TableMetaDto TableMeta(string src, Markdig.MarkdownPipeline pipeline, int number)
    {
        var lines = src.Replace("\r\n", "\n").Split('\n');
        var idx = 0;
        string? attrLine = null;
        if (lines.Length > 0 && AttrLinePattern.IsMatch(lines[0]))
        {
            attrLine = lines[0];
            idx = 1;
        }
        var headerLine = lines[idx];
        var delimiterLine = lines[idx + 1];
        var rowLines = lines.Skip(idx + 2).Where(l => l.Trim().Length > 0).ToList();

        var doc = Markdig.Markdown.Parse(src, pipeline);
        var table = doc.Descendants().OfType<Markdig.Extensions.Tables.Table>().First();
        var columns = table.Count > 0 && table[0] is Markdig.Extensions.Tables.TableRow headerRow
            ? headerRow.Count
            : table.ColumnDefinitions.Count;
        var aligns = Enumerable.Range(0, columns)
            .Select(i => i < table.ColumnDefinitions.Count
                ? table.ColumnDefinitions[i].Alignment switch
                {
                    Markdig.Extensions.Tables.TableColumnAlign.Left => "left",
                    Markdig.Extensions.Tables.TableColumnAlign.Center => "center",
                    Markdig.Extensions.Tables.TableColumnAlign.Right => "right",
                    _ => (string?)null,
                }
                : null)
            .ToList();

        var props = table.TryGetAttributes()?.Properties;
        var widthsIndex = props?.FindIndex(p => p.Key == "widths") ?? -1;
        var widths = widthsIndex >= 0 ? props![widthsIndex].Value : null;
        var widthIndex = props?.FindIndex(p => p.Key == "width") ?? -1;   // overall table-box width (L202)
        var width = widthIndex >= 0 ? props![widthIndex].Value : null;
        var alignIndex = props?.FindIndex(p => p.Key == "align") ?? -1;   // box alignment (L205): center|right
        var align = alignIndex >= 0 ? props![alignIndex].Value : null;
        var captionIndex = props?.FindIndex(p => p.Key == "caption") ?? -1; // human-readable table title (Task 5)
        var caption = captionIndex >= 0 ? props![captionIndex].Value : null;
        var explicitId = attrLine is not null && ExplicitIdPattern.Match(attrLine) is { Success: true } m
            ? m.Groups[1].Value
            : null;

        return new TableMetaDto(attrLine, headerLine, delimiterLine, rowLines, aligns, widths, width, align, explicitId, number, caption);
    }

    /// <summary>Structured figure data from the segment's own source.</summary>
    internal static FigureMetaDto FigureMeta(string src, Markdig.MarkdownPipeline pipeline, int number)
    {
        var doc = Markdig.Markdown.Parse(src, pipeline);
        var paragraph = doc.Descendants().OfType<ParagraphBlock>().First();
        var image = DocWizFiguresExtension.ImageOf(paragraph)
            ?? throw new InvalidOperationException("figure segment without an image");
        var attrs = image.TryGetAttributes();
        var props = attrs?.Properties;
        var widthIndex = props?.FindIndex(p => p.Key == "width") ?? -1;
        var align = attrs?.Classes?.Contains("img-center") == true ? "center"
            : attrs?.Classes?.Contains("img-right") == true ? "right"
            : attrs?.Classes?.Contains("img-left") == true ? "left"
            : null;

        // .no-caption: showCaption = false when the class is present; the class is NOT
        // sent to the client (the renderer strips it so it never appears on <img>).
        var showCaption = attrs?.Classes?.Contains("no-caption") != true;

        return new FigureMetaDto(
            image.Url ?? "",
            DocWizFiguresExtension.AltTextOf(image),
            widthIndex >= 0 ? props![widthIndex].Value : null,
            align,
            attrs?.Id,
            number,
            showCaption);
    }

    public static string ComputeBodyHash(string body) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    /// <summary>
    /// Builds the new chapter body from save segments: copies are verbatim original
    /// bytes; text segments are new. Heading anchors (H2+) are ensured ONLY inside
    /// text segments — untouched blocks are never rewritten, not even to add an
    /// anchor (W4 in docs/fidelity/NORMALIZATION.md).
    /// </summary>
    public static string BuildNewBody(string body, List<SaveSegmentDto> segments)
    {
        var sb = new StringBuilder(body.Length + 256);
        var textRanges = new List<(int Start, int End)>();
        foreach (var seg in segments)
        {
            if (seg.Text is not null)
            {
                textRanges.Add((sb.Length, sb.Length + seg.Text.Length));
                sb.Append(seg.Text);
            }
            else if (seg is { CopyStart: { } start, CopyEnd: { } end })
            {
                if (start < 0 || end < start || end > body.Length)
                    throw new ArgumentOutOfRangeException(nameof(segments),
                        $"Copy span [{start},{end}) is outside the body (length {body.Length}).");
                sb.Append(body, start, end - start);
            }
            else
            {
                throw new ArgumentException("Each save segment needs either copy bounds or text.", nameof(segments));
            }
        }
        return EnsureAnchorsInRanges(sb.ToString(), textRanges);
    }

    /// <summary>Adds a unique {#slug} to every anchor-less H2+ heading AND anchor-less
    /// ':::&#160;group' head whose line lies inside one of the given char ranges (code
    /// fences skipped). Taken-set rule: any {#x} occurrence (heading or group attrs)
    /// reserves x. Already-anchored heads are left untouched (ADR-005 immutability).</summary>
    private static string EnsureAnchorsInRanges(string text, List<(int Start, int End)> ranges)
    {
        if (ranges.Count == 0)
            return text;

        var lines = text.Split('\n');
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
            foreach (System.Text.RegularExpressions.Match tm in System.Text.RegularExpressions.Regex.Matches(line, @"\{#([A-Za-z0-9_-]+)(?=[}\s])"))
                taken.Add(tm.Groups[1].Value);

        var offset = 0;
        var inFence = false;
        var changed = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineStart = offset;
            offset += line.Length + 1; // +1 for the split '\n'

            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence)
                continue;
            var lineEnd = lineStart + line.Length;
            var inRange = ranges.Any(r => lineStart < r.End && lineEnd > r.Start);
            if (!inRange)
                continue;

            // H2+ heading without an explicit {#id}: append one.
            var heading = System.Text.RegularExpressions.Regex.Match(line, @"^(#{2,6})\s+(.*\S)\s*$");
            if (heading.Success)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(heading.Groups[2].Value, @"\{#[^}]+\}\s*$"))
                    continue;
                var hAnchor = HeadingScanner.UniqueAnchor(HeadingScanner.Slug(heading.Groups[2].Value), taken);
                taken.Add(hAnchor);
                lines[i] = line.TrimEnd() + " {#" + hAnchor + "}";
                changed = true;
                continue;
            }

            // ':::[: …] group <name> [ {attrs} ]' head without a {#id}: stamp one.
            var group = System.Text.RegularExpressions.Regex.Match(line, @"^:{3,}\s+group\s+(.*\S)\s*$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (group.Success)
            {
                var rest = group.Groups[1].Value;                       // "<name>" or "<name> {attrs}"
                if (System.Text.RegularExpressions.Regex.IsMatch(rest, @"\{#[A-Za-z0-9_-]+"))
                    continue;                                           // already has an id
                // name = rest minus a trailing {...} block (e.g. {desc="…"})
                var brace = System.Text.RegularExpressions.Regex.Match(rest, @"\s*\{([^}]*)\}\s*$");
                var name = (brace.Success ? rest[..brace.Index] : rest).Trim();
                var gAnchor = HeadingScanner.UniqueAnchor(HeadingScanner.Slug(name), taken);
                taken.Add(gAnchor);
                if (brace.Success)
                {
                    // Insert "#id " at the start of the EXISTING trailing brace block:
                    // "::: group N {desc=\"d\"}" -> "::: group N {#id desc=\"d\"}".
                    var bracePos = line.LastIndexOf('{');               // the trailing brace block
                    var hasInner = !string.IsNullOrWhiteSpace(brace.Groups[1].Value);
                    lines[i] = line[..(bracePos + 1)] + "#" + gAnchor + (hasInner ? " " : "") + line[(bracePos + 1)..];
                }
                else
                {
                    lines[i] = line.TrimEnd() + " {#" + gAnchor + "}";
                }
                changed = true;
                continue;
            }
        }
        return changed ? string.Join('\n', lines) : text;
    }

    /// <summary>Flat 1-based chapter number in document order — must match
    /// HandoverBuilder's numbering, so the surface shows what ships.</summary>
    public static int ChapterNumber(DocProject project, string fullPath)
    {
        for (var i = 0; i < project.Chapters.Count; i++)
        {
            if (string.Equals(Path.GetFullPath(project.Chapters[i].FilePath), Path.GetFullPath(fullPath),
                    StringComparison.OrdinalIgnoreCase))
                return i + 1;
        }
        return 0;
    }

    /// <summary>Editor-surface URL rewrite: assets/attachments become the editor
    /// server's absolute routes, missing images become the pipeline's visible
    /// placeholder. dw: links are deliberately left untouched — inside the editor
    /// they are UI affordances (M4 popovers), not handover navigation, so
    /// <see cref="LinkRewriter.Rewrite"/>'s chapter hrefs would be wrong here.</summary>
    private static void RewriteForSurface(MarkdownDocument doc, DocProject project)
    {
        foreach (var link in doc.Descendants().OfType<LinkInline>().ToList())
        {
            var url = link.Url ?? "";
            if (url.StartsWith("dw:", StringComparison.OrdinalIgnoreCase))
                continue;

            if (LinkRewriter.TryGetLocalFile(url, ProjectLayout.AssetsFolder, out var assetName))
            {
                if (link.IsImage && !File.Exists(Path.Combine(project.AssetsDir, assetName)))
                {
                    link.InsertBefore(new HtmlInline(PlaceholderSpan(link, assetName)));
                    link.Remove();
                }
                else
                {
                    link.Url = AssetRoute + assetName;
                }
                continue;
            }

            if (LinkRewriter.TryGetLocalFile(url, ProjectLayout.AttachmentsFolder, out var attachmentName))
                link.Url = "/attachments/" + attachmentName;
        }
    }

    /// <summary>The editor's missing-image placeholder carries the original image
    /// data (src/alt/width/align) so the live surface can model it as a MISSING
    /// IMAGE — editable in place, serialized back to ![alt](src) — instead of
    /// locking every cell or paragraph that holds one. Images with attributes
    /// beyond the editor's model (width + img-center/img-right/img-left) emit the bare
    /// span: the cell editor then locks rather than silently dropping them.</summary>
    private static string PlaceholderSpan(LinkInline link, string assetName)
    {
        var attrs = link.TryGetAttributes();
        var classes = attrs?.Classes;
        var props = attrs?.Properties;
        var modelable = attrs is null ||
            (string.IsNullOrEmpty(attrs.Id)
             && (classes is null || classes.All(c => c is "img-center" or "img-right" or "img-left"))
             && (props is null || props.All(p => p.Key == "width")));
        if (!modelable)
            return LinkRewriter.MissingImagePlaceholder(assetName);

        var align = classes is null ? null
            : classes.Contains("img-center") ? "center"
            : classes.Contains("img-right") ? "right"
            : classes.Contains("img-left") ? "left" : null;
        var width = props?.FirstOrDefault(p => p.Key == "width").Value;
        var sb = new StringBuilder($"<span class=\"{LinkRewriter.ImgPlaceholderClass}\"");
        sb.Append($" data-src=\"{AssetRoute}").Append(WebUtility.HtmlEncode(assetName)).Append('"');
        sb.Append(" data-alt=\"").Append(WebUtility.HtmlEncode(DocWizFiguresExtension.AltTextOf(link))).Append('"');
        if (!string.IsNullOrEmpty(width))
            sb.Append(" data-width=\"").Append(WebUtility.HtmlEncode(width)).Append('"');
        if (align is not null)
            sb.Append(" data-align=\"").Append(align).Append('"');
        sb.Append('>').Append(LinkRewriter.MissingImageText).Append(assetName).Append("</span>");
        return sb.ToString();
    }
}
