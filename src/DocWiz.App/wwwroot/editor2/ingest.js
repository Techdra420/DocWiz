/* Builds the ProseMirror document from /api/editor-doc segments. Pipeline HTML is the
   transfer format (ADR-004: Markdig parsed it; nothing here parses markdown). Tables,
   figures, and unmodeled blocks become atoms carrying their HTML + verbatim source. */
import { DOMParser as PmDomParser } from 'prosemirror-model';

// Block types with a PM-native structure; everything else is a raw atom. Must stay a
// subset of BlockSegmenter.Classify's names — the CLI's L1 fidelity scan keeps the
// same set as `modeled` in src/DocWiz.Cli/Program.cs (plus figure/table, which are
// structured atoms here); change them together.
const NATIVE = new Set(['heading', 'paragraph', 'container', 'code', 'list', 'quote', 'hr']);

export function buildDoc(schema, dto) {
  const parser = PmDomParser.fromSchema(schema);
  const blocks = [];
  dto.segments.forEach((seg, segIndex) => {
    if (seg.kind !== 'block') return;
    for (const node of nodesFor(schema, parser, seg)) blocks.push(withSrcId(node, segIndex));
  });
  if (blocks.length === 0) blocks.push(schema.nodes.paragraph.create());
  return schema.nodes.doc.create(null, blocks);
}

// srcId ties a top-level node to its source segment; the save path emits clean
// nodes as verbatim copy spans of that segment.
function withSrcId(node, srcId) {
  return node.type.create({ ...node.attrs, srcId }, node.content, node.marks);
}

/* Build top-level block nodes from editor-fragment segments WITHOUT srcId — for the
   embed Detach (Task 9): the transcluded section's raw markdown comes back as editable
   segments, and the result replaces the embed node as fresh AUTHORED content (no source
   span to copy verbatim — it now serializes as real markdown, not a directive). Reuses
   nodesFor so the same pipeline-HTML → PM mapping applies (tables/figures/containers).
   Returns [] when the section was empty (the caller decides on a fallback). */
export function nodesFromSegments(schema, segments) {
  const parser = PmDomParser.fromSchema(schema);
  const blocks = [];
  (segments || []).forEach((seg) => {
    if (seg.kind !== 'block') return;
    for (const node of nodesFor(schema, parser, seg)) blocks.push(node);
  });
  return blocks;
}

// Exported for the L2 fidelity harness (re-ingesting a re-rendered block).
export function nodesFor(schema, parser, seg) {
  // A slide-zone embed (Task 9): the server resolved it for DISPLAY (seg.html) and sent
  // the bare "chapter#anchor" ref (seg.ref) — the serialization source of truth. Build
  // the dedicated embed atom; EmbedView renders the transclusion under a LINKED pill and
  // serialize.js re-emits the canonical directive from ref (byte-stable round-trip). The
  // client never parses the directive — ref is computed server-side (DocWizEmbed).
  if (seg.blockType === 'embed')
    return [schema.nodes.embed.create({ ref: seg.ref, html: seg.html, width: seg.width || null, align: seg.align || null })];
  if (seg.blockType === 'table' || seg.blockType === 'figure')
    return [schema.nodes[seg.blockType].create({ html: seg.html, src: seg.src, meta: seg.meta || null })];
  // Any prose block WRAPPING a figure/table (container, quote, list item with a
  // standalone image) can't round-trip in M2 — the nested atom would lose its
  // source. Show it verbatim as a locked raw block; Source (advanced) edits it.
  if (!NATIVE.has(seg.blockType) || /<figure/.test(seg.html))
    return [schema.nodes.raw.create({ html: seg.html, src: seg.src })];

  const content = fragmentFrom(seg.html);
  stripContainerChrome(content);
  const doc = parser.parse(wrap(content));
  const nodes = [];
  doc.content.forEach((n) => nodes.push(stripAutoAnchor(n, seg)));
  return nodes;
}

// The pipeline auto-assigns ids to EVERY heading (GitHub auto-identifiers); only an
// explicit {#…} in the source is a real anchor. Without this check the serializer
// would stamp auto ids into the file — including onto the H1, which must never
// carry one (the chapter itself is dw:chapter-id).
function stripAutoAnchor(node, seg) {
  if (node.type.name !== 'heading' || node.attrs.anchor == null) return node;
  if (seg.blockType === 'heading' && /\{#[A-Za-z0-9_-]+\}\s*$/.test(seg.src.trimEnd())) return node;
  return node.type.create({ ...node.attrs, anchor: null }, node.content, node.marks);
}

function fragmentFrom(html) {
  const t = document.createElement('template');
  t.innerHTML = html;
  return t.content;
}

function wrap(fragment) {
  const div = document.createElement('div');
  div.appendChild(fragment);
  return div;
}

/* Container chrome (callout tag pill, audience tag, details summary) is generated
   markup, not content — the NodeView re-renders it from attrs. Remove it before PM
   parses, stashing the label where the schema's getAttrs expects it. Handles nesting
   (a callout inside an audience block). */
function stripContainerChrome(root) {
  for (const tag of root.querySelectorAll('.dw-callout-tag')) {
    tag.closest('.dw-callout')?.setAttribute('data-label', tag.textContent || '');
    tag.remove();
  }
  for (const tag of root.querySelectorAll('.aud-tag')) tag.remove();
  for (const summary of root.querySelectorAll('details.dw-details > summary')) {
    summary.parentElement.setAttribute('data-label', summary.textContent || '');
    summary.remove();
  }
  // Task-list checkbox: the pipeline renders <li class="task-list-item"><input type=checkbox ...>.
  // Lift the checked state onto the li (data-checked, read by list_item parseDOM) and drop the
  // input so PM never sees stray content. Mirrors the chrome-strip above.
  for (const li of root.querySelectorAll('li.task-list-item')) {
    const cb = li.querySelector(':scope > input[type=checkbox], :scope > p > input[type=checkbox]');
    li.setAttribute('data-checked', cb && cb.hasAttribute('checked') ? 'true' : 'false');
    if (cb) cb.remove();
  }
}
