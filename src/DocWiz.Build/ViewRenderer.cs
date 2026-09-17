using System.Net;
using System.Text;
using DocWiz.Core.Markdown;
using DocWiz.Core.Models;
using Markdig;
using Markdig.Extensions.CustomContainers;

namespace DocWiz.Build;

/// <summary>
/// Renders audience views (views/*.yaml) into HTML: slides mode (the training deck) and
/// document mode (the filtered quick-reference page). Each slide/section is transcluded
/// from the source chapters, audience-filtered, and re-parsed through the one shared
/// pipeline — views never contain copied content.
/// </summary>
public static class ViewRenderer
{
    /// <summary>Renders slides HTML for a view. Each slide is rendered through the one
    /// shared pipeline; bare include slides get the included section's title as their
    /// heading when no explicit Title is set.</summary>
    /// <param name="edit">When true, renders the live AUTHORING surface (Slides v2): every
    /// region the layout defines is emitted as a clickable <c>.slide-region</c> zone even
    /// when empty (so a per-zone editor can mount and the author can type into a new slide),
    /// and the title/subtitle render as editable elements (<c>data-slide-title</c> /
    /// <c>data-slide-sub</c>) carrying the slide's RAW title/subtitle (never the auto-derived
    /// include heading, which the author must not silently inherit as explicit text). The
    /// present/handover deck (edit: false) is unchanged: empty regions are omitted and only a
    /// non-empty heading/subtitle render.</param>
    public static string RenderSlides(DocProject project, ViewDefinition view, MarkdownPipeline pipeline,
        bool edit = false)
    {
        var total = view.Slides.Count + 1; // + auto title slide
        var sb = new StringBuilder();

        sb.AppendLine("    <div class=\"slide active\" data-layout=\"title-slide\">");
        sb.AppendLine($"      <h1>{Escape(view.Title)}</h1>");
        sb.AppendLine($"      <p class=\"slide-sub\">{Escape(project.Manifest.Project)} · {Escape(project.Manifest.Code)}</p>");
        sb.AppendLine("      <p>This presentation is generated from the project documentation — it is always as current as the manual.</p>");
        sb.AppendLine($"      <div class=\"src\">view: {Escape(view.Id)} · 1 of {total}</div>");
        sb.AppendLine("    </div>");

        for (var i = 0; i < view.Slides.Count; i++)
        {
            var slide = view.Slides[i];

            // Whole-chapter link expands to one slide per H2/H3 (always current with the manual).
            if (!string.IsNullOrWhiteSpace(slide.Chapter))
            {
                AppendChapterSlides(project, view, slide.Chapter!, pipeline, sb, total, i);
                continue;
            }

            var layout = slide.EffectiveLayout;
            // data-src-anchor: the source heading a single-source slide points at — inert in
            // Spec 1, the attach key for Spec 2's per-section sticky overrides.
            var srcAnchor = SingleSourceRef(slide, pipeline);
            var anchorAttr = srcAnchor is null ? "" : $" data-src-anchor=\"{Escape(srcAnchor)}\"";
            // data-slide-index (edit only): the explicit slides[] slot the zone-mounter
            // (DocWizSlideEdit.mountAll) reads. Chapter-expansion slides carry none (they
            // derive from a chapter → read-only in the editor). Inert in read/handover.
            var indexAttr = edit ? $" data-slide-index=\"{i}\"" : "";

            sb.AppendLine($"    <div class=\"slide\" data-layout=\"{Escape(layout)}\"{anchorAttr}{indexAttr}>");

            if (edit)
            {
                EmitEditableTitle(sb, project, view, slide, pipeline, indent: "      ", titleTag: "h1");
            }
            else
            {
                var heading = EffectiveHeading(project, view, slide, pipeline);
                if (!string.IsNullOrWhiteSpace(heading))
                    sb.AppendLine($"      <h1>{Escape(heading)}</h1>");
                if (!string.IsNullOrWhiteSpace(slide.Subtitle))
                    sb.AppendLine($"      <p class=\"slide-sub\">{Escape(slide.Subtitle)}</p>");
            }

            RenderRegions(project, view, slide, pipeline, sb,
                wrapRegions: true, emitSrcLines: false, indent: "      ", edit: edit);

            sb.AppendLine($"      <div class=\"src\">{i + 2} of {total}</div>");
            sb.AppendLine("    </div>");
        }

        return sb.ToString();
    }

    /// <summary>Emits the editable title (+subtitle) field for the live authoring surface,
    /// shared by slides (h1) and document (h2) edit modes so the editable-title contract has
    /// ONE source (ADR-008). The author edits the RAW title/subtitle in place (contenteditable
    /// wired in slideedit.js → written back to slide.Title/Subtitle). Always rendered so a
    /// new/empty entry (notably section-header, which has no regions) is editable.
    ///
    /// WYSIWYG-by-construction (the edit-vs-ship gate): when there is no explicit title but the
    /// surface SHOWS an auto-derived heading (a lone-embed entry inherits the included section's
    /// title — EffectiveHeading), DISPLAY that derived heading here too, marked data-derived.
    /// Otherwise edit would show a blank title where the surface shows a heading — the body would
    /// float up and the geometry would diverge ("what I see is NOT what I ship"). data-derived
    /// lets slideedit.js show it as placeholder-style text WITHOUT writing it into the working
    /// model, so an untouched derived heading is never frozen as an explicit title (it keeps
    /// auto-deriving); only an actual edit promotes it. A field is .is-empty (placeholder) only
    /// when it is BOTH explicitly empty AND not derived. The read/present branch omits an empty
    /// title/subtitle, so the SHIPPED geometry is unchanged.</summary>
    private static void EmitEditableTitle(StringBuilder sb, DocProject project, ViewDefinition view,
        SlideDef slide, MarkdownPipeline pipeline, string indent, string titleTag)
    {
        var explicitTitle = slide.Title;
        var derivedHeading = string.IsNullOrWhiteSpace(explicitTitle)
            ? EffectiveHeading(project, view, slide, pipeline)
            : null;
        var titleText = !string.IsNullOrWhiteSpace(explicitTitle) ? explicitTitle : derivedHeading;
        var titleEmpty = string.IsNullOrWhiteSpace(titleText);
        var derivedAttr = !string.IsNullOrWhiteSpace(derivedHeading) ? " data-derived" : "";
        var subEmpty = string.IsNullOrWhiteSpace(slide.Subtitle);
        sb.AppendLine($"{indent}<{titleTag} data-slide-title data-placeholder=\"Add a title…\"{derivedAttr}{(titleEmpty ? " class=\"is-empty\"" : "")}>{Escape(titleText)}</{titleTag}>");
        sb.AppendLine($"{indent}<p class=\"slide-sub{(subEmpty ? " is-empty" : "")}\" data-slide-sub data-placeholder=\"Add a subtitle…\">{Escape(slide.Subtitle)}</p>");
    }

    /// <summary>Expands a `- chapter:` directive into one title-content slide per H2/H3 heading:
    /// the heading is the slide title and the slide body is that section's OWN markdown, sliced
    /// between consecutive headings of ANY level (so sub-headings become their own slides — NOT
    /// <see cref="TransclusionResolver"/>, which bounds to the next same-or-higher level and would
    /// pull an H2's H3 children into the H2 slide). Each slide carries
    /// data-src-anchor="chapter#anchor"; content is audience-filtered like all transclusion.
    /// Preamble under the chapter H1 (before the first H2) is intentionally dropped.</summary>
    private static void AppendChapterSlides(DocProject project, ViewDefinition view, string chapterId,
        MarkdownPipeline pipeline, StringBuilder sb, int total, int index)
    {
        var chapter = project.FindChapter(chapterId)
            ?? throw new DocWizBuildException($"View '{view.Id}': chapter '{chapterId}' not found — run 'docwiz check'.");

        var body = chapter.Markdown;
        var lines = body.Replace("\r\n", "\n").Split('\n');
        var headings = HeadingScanner.Scan(body);
        for (var k = 0; k < headings.Count; k++)
        {
            var h = headings[k];
            if (h.Level is not (2 or 3))
                continue;
            var startLine = h.Line;
            var endLine = k + 1 < headings.Count ? headings[k + 1].Line : lines.Length;
            var sectionMd = string.Join("\n", lines[startLine..endLine]).Trim();
            var anchor = !string.IsNullOrWhiteSpace(h.Anchor) ? h.Anchor! : HeadingScanner.Slug(h.Text);

            sb.AppendLine($"    <div class=\"slide\" data-layout=\"{SlideLayout.TitleContent}\" data-src-anchor=\"{Escape(chapterId)}#{Escape(anchor)}\">");
            sb.AppendLine($"      <h1>{Escape(h.Text)}</h1>");
            sb.AppendLine("      <div class=\"slide-region\" data-region=\"body\">");
            sb.AppendLine(RenderRegionContent(project, view, StripLeadingHeading(sectionMd), pipeline));
            sb.AppendLine("      </div>");
            sb.AppendLine($"      <div class=\"src\">{index + 2} of {total}</div>");
            sb.AppendLine("    </div>");
        }
    }

    /// <summary>Drops the first markdown heading line from a sliced section (its text is the
    /// slide title; we don't repeat it inside the body).</summary>
    private static string StripLeadingHeading(string sectionMd)
    {
        var nl = sectionMd.IndexOf('\n');
        return nl < 0 ? "" : sectionMd[(nl + 1)..].TrimStart('\n');
    }

    /// <summary>The single source ref ("chapter#anchor") a slide points at, or null when it is
    /// custom/multi-source — used to stamp data-src-anchor (the Spec 2 override key).</summary>
    private static string? SingleSourceRef(SlideDef slide, MarkdownPipeline pipeline)
    {
        if (!string.IsNullOrWhiteSpace(slide.Include))
            return DocWizEmbed.NormalizeRef(slide.Include!);
        var regions = slide.EffectiveRegions();
        if (regions.Count == 1)
        {
            var refs = DocWizEmbed.FindRefs(regions.Values.First(), pipeline);
            if (refs.Count == 1)
                return refs[0];
        }
        return null;
    }

    /// <summary>linkSources: false for the standalone single-file export — the manual's
    /// pages don't exist next to that file, so source references render as plain text.</summary>
    public static string RenderDocument(DocProject project, ViewDefinition view, MarkdownPipeline pipeline,
        bool linkSources = true, bool edit = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"      <h1>{Escape(view.Title)}</h1>");
        sb.AppendLine($"      <p class=\"lead\">Filtered view for the {Escape(view.Audience)} audience — " +
                      "generated from the same source chapters as the manual.</p>");

        for (var i = 0; i < view.Slides.Count; i++)
        {
            var slide = view.Slides[i];

            // sec-N ids let the editor's WYSIWYG stage scroll to a selected section. data-slide-index
            // (edit only) is the explicit slides[] slot DocWizSlideEdit.mountAll reads.
            var indexAttr = edit ? $" data-slide-index=\"{i}\"" : "";
            sb.AppendLine($"      <section class=\"view-sec\" id=\"sec-{i}\"{indexAttr}>");

            if (edit)
            {
                // EDIT: editable section title + editable zones (embeds resolve read-only), so the
                // view's own glue + titles are authorable inline — the same card/zone shape the
                // slides surface mounts. No srclines (the live embed IS the content).
                EmitEditableTitle(sb, project, view, slide, pipeline, indent: "        ", titleTag: "h2");
                RenderRegions(project, view, slide, pipeline, sb,
                    wrapRegions: true, emitSrcLines: false, indent: "        ", edit: true);
            }
            else
            {
                var heading = EffectiveHeading(project, view, slide, pipeline);
                if (!string.IsNullOrWhiteSpace(heading))
                    sb.AppendLine($"        <h2>{Escape(heading)}</h2>");
                if (!string.IsNullOrWhiteSpace(slide.Subtitle))
                    sb.AppendLine($"        <p class=\"slide-sub\">{Escape(slide.Subtitle)}</p>");

                RenderRegions(project, view, slide, pipeline, sb,
                    wrapRegions: false, emitSrcLines: linkSources, indent: "        ");
            }

            sb.AppendLine("      </section>");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Returns the effective heading text for a slide:
    /// 1. slide.Title if non-empty.
    /// 2. For layout title-content whose body region is a LONE embed (the markdown is just
    ///    that one embed directive), the title of the transcluded section — preserving the
    ///    v1 bare-include auto-heading.
    /// 3. Otherwise null/empty — no heading rendered.
    /// </summary>
    private static string? EffectiveHeading(DocProject project, ViewDefinition view, SlideDef slide,
        MarkdownPipeline pipeline)
    {
        if (!string.IsNullOrWhiteSpace(slide.Title))
            return slide.Title;

        if (slide.EffectiveLayout == SlideLayout.TitleContent)
        {
            var regions = slide.EffectiveRegions();
            if (regions.TryGetValue(SlideLayout.BodyRegion, out var body) && LoneEmbedRef(body, pipeline) is { } reference)
                return ResolveInclude(project, view, reference).Title;
        }

        return null;
    }

    /// <summary>If the region markdown is nothing but a single embed directive, returns its
    /// normalized "chapter#anchor" ref; otherwise null. Parse-based via the shared
    /// <see cref="DocWizEmbed.FindRefs"/> helper (one definition of "is an embed"): the
    /// document must hold exactly one block and that block must be the embed.</summary>
    private static string? LoneEmbedRef(string regionMarkdown, MarkdownPipeline pipeline)
    {
        if (string.IsNullOrWhiteSpace(regionMarkdown))
            return null;
        var doc = Markdig.Markdown.Parse(regionMarkdown, pipeline);
        var refs = DocWizEmbed.FindRefs(doc);
        if (refs.Count == 1 && doc.Count == 1 && doc[0] is CustomContainer)
            return refs[0];
        return null;
    }

    /// <summary>
    /// Renders all named regions for a slide into sb.
    /// wrapRegions: true → wrap each region in &lt;div class="slide-region" data-region="..."&gt;
    /// emitSrcLines: true → append a &lt;p class="srcline"&gt; link per embed ref in the region
    /// (document mode's "open in manual" attribution).
    /// </summary>
    private static void RenderRegions(DocProject project, ViewDefinition view, SlideDef slide,
        MarkdownPipeline pipeline, StringBuilder sb, bool wrapRegions, bool emitSrcLines, string indent,
        bool edit = false)
    {
        var layout = slide.EffectiveLayout;
        var regions = slide.EffectiveRegions();

        foreach (var name in SlideLayout.RegionsFor(layout))
        {
            var hasContent = regions.TryGetValue(name, out var markdown);
            // EDIT mode emits a zone for EVERY region the layout defines, even when empty,
            // so a per-zone editor mounts and the author can click in and type (fixes the
            // "new slides are blank pages I cannot edit" report). PRESENT/handover (edit:
            // false) keeps the old behaviour: empty regions are omitted.
            if (!hasContent && !edit)
                continue;

            if (wrapRegions)
            {
                sb.AppendLine($"{indent}<div class=\"slide-region\" data-region=\"{Escape(name)}\">");
                sb.AppendLine(RenderRegionContent(project, view, markdown ?? "", pipeline));
                sb.AppendLine($"{indent}</div>");
            }
            else
            {
                sb.AppendLine(RenderRegionContent(project, view, markdown ?? "", pipeline));
                if (emitSrcLines)
                    foreach (var reference in DocWizEmbed.FindRefs(markdown ?? "", pipeline))
                        sb.AppendLine($"{indent}<p class=\"srcline\"><a href=\"{SourceHref(reference)}\">open in manual: {Escape(reference)} →</a></p>");
            }
        }
    }

    /// <summary>Renders one region's markdown through the one shared pipeline, BLOCK BY
    /// BLOCK, so the handover deck groups an embed exactly like the live edit surface does
    /// (WYSIWYG by construction). A top-level block that is a LONE embed directive is
    /// rendered RESOLVED and wrapped in a single <c>&lt;div class="dw-embed"&gt;</c> — the
    /// same container <see cref="DocWiz.App.EditorDoc"/> emits for the zone editor's embed
    /// node — so a multi-block transclusion becomes one grouped block in BOTH surfaces (in a
    /// flex/grid region it then lays out as one contained box on both sides, never
    /// flex-row-spread on ship but stacked-in-a-box in edit). Every other block renders
    /// inline as before (its own inline embeds resolved, audience-filtered, link-rewritten).
    /// "Is a lone embed" is the shared parse-based test (<see cref="LoneEmbedRef"/> →
    /// <see cref="DocWizEmbed.FindRefs"/>), never a hand-rolled regex.</summary>
    private static string RenderRegionContent(DocProject project, ViewDefinition view, string regionMarkdown,
        MarkdownPipeline pipeline)
    {
        var doc = Markdig.Markdown.Parse(regionMarkdown ?? "", pipeline);
        var sb = new StringBuilder();
        foreach (var block in doc.ToList())
        {
            if (LoneBlockEmbed(block) is { } embed)
                sb.Append(RenderEmbedGrouped(project, view, embed.Reference, embed.Width, embed.Align, pipeline));
            else
                sb.Append(RenderBlockInline(project, view, block, pipeline));
        }
        return sb.ToString();
    }

    /// <summary>The normalized "chapter#anchor" ref + optional layout width (L205) if
    /// <paramref name="block"/> is itself an embed directive container, else null — the
    /// per-block counterpart to <see cref="LoneEmbedRef"/> the editor uses to decide which
    /// blocks become the dedicated embed node.</summary>
    private static (string Reference, string? Width, string? Align)? LoneBlockEmbed(Markdig.Syntax.Block block)
    {
        if (block is CustomContainer container
            && string.Equals(container.Info, DocWizEmbed.Name, StringComparison.OrdinalIgnoreCase))
            return (DocWizEmbed.NormalizeRef(container.Arguments ?? ""),
                    DocWizEmbed.ParseWidth(container.Arguments), DocWizEmbed.ParseAlign(container.Arguments));
        return null;
    }

    /// <summary>Renders one lone embed RESOLVED — expanded, audience-filtered,
    /// link-rewritten through the one pipeline — wrapped in the SAME single
    /// <c>.dw-embed</c> container the editor's embed node uses, so edit DOM == ship DOM.
    /// <paramref name="width"/>/<paramref name="align"/> (L205) size and place the box.</summary>
    private static string RenderEmbedGrouped(DocProject project, ViewDefinition view, string reference,
        string? width, string? align, MarkdownPipeline pipeline)
    {
        // Re-parse the canonical directive so resolution acts on a clean rooted document
        // (the block we matched still belongs to the region doc — a block has one parent).
        var doc = Markdig.Markdown.Parse(DocWizEmbed.DirectiveFor(reference), pipeline);
        DocWizEmbed.Resolve(doc, project, pipeline);
        AudienceFilter.Apply(doc, view.Audience);
        LinkRewriter.Rewrite(doc, project, id => $"{id}.html");
        return DocWizEmbed.WrapResolved(DocWizMarkdown.RenderHtml(doc, pipeline), width, align);
    }

    /// <summary>Renders a single non-embed block inline through the one pipeline: any inline
    /// embed it holds is resolved, then audience-filtered and link-rewritten (a freshly
    /// rooted one-block document so the shared mutating passes act on it safely).</summary>
    private static string RenderBlockInline(DocProject project, ViewDefinition view, Markdig.Syntax.Block block,
        MarkdownPipeline pipeline)
    {
        var doc = new Markdig.Syntax.MarkdownDocument();
        block.Parent?.Remove(block);
        doc.Add(block);
        DocWizEmbed.Resolve(doc, project, pipeline);
        AudienceFilter.Apply(doc, view.Audience);
        LinkRewriter.Rewrite(doc, project, id => $"{id}.html");
        return DocWizMarkdown.RenderHtml(doc, pipeline);
    }

    /// <summary>
    /// Resolves an include ref ("chapterId#anchor" or "chapterId") to a TranscludedSection.
    /// Throws DocWizBuildException when the chapter is not found (anchor errors are caught
    /// by ProjectValidator before builds run).
    /// </summary>
    private static TranscludedSection ResolveInclude(DocProject project, ViewDefinition view, string includeRef)
    {
        var (chapterId, anchor) = LinkRewriter.SplitRef(includeRef);
        var chapter = project.FindChapter(chapterId)
            ?? throw new DocWizBuildException($"View '{view.Id}': include '{includeRef}' not found — run 'docwiz check'.");
        return TransclusionResolver.Resolve(chapter, anchor);
    }

    private static string SourceHref(string sourceRef)
    {
        var (chapterId, anchor) = LinkRewriter.SplitRef(sourceRef);
        return $"{chapterId}.html" + (anchor is null ? "" : $"#{anchor}");
    }

    private static string Escape(string? text) => WebUtility.HtmlEncode(text ?? "");
}
