/* DocWiz markdown serializer (ADR-007): turns ONE edited ProseMirror block back into
   markdown. Untouched blocks never come through here — the save path copies their
   original bytes. Canonical forms and the escaping policy are the normalization
   whitelist W2/W5 (docs/fidelity/NORMALIZATION.md); the L2 harness proves that
   Markdig renders the output identically to the original. */
import { CALLOUTS, DEFAULT_LABEL } from './schema.js';

export function serializeBlock(node) {
  switch (node.type.name) {
    case 'paragraph': return inline(node);
    case 'heading': {
      const text = '#'.repeat(node.attrs.level) + ' ' + inline(node);
      return node.attrs.anchor ? text + ' {#' + node.attrs.anchor + '}' : text;
    }
    case 'bullet_list': return list(node, (_i, item) => '- ' + (
      item.attrs.checked === true ? '[x] ' : item.attrs.checked === false ? '[ ] ' : ''));
    case 'ordered_list': return list(node, (i) => (node.attrs.start + i) + '. ');
    case 'blockquote': return prefixLines(blocks(node), '> ', '>');
    case 'code_block': return codeBlock(node);
    case 'horizontal_rule': return '---';
    case 'container': return container(node);
    case 'figure': return serializeFigure(node);
    case 'table': return serializeTable(node);
    case 'raw': {
      if (!node.attrs.src) throw new Error('block has no source text — edit it via Source (advanced)');
      return node.attrs.src;
    }
    case 'embed': return embedDirective(node.attrs.ref, node.attrs.width, node.attrs.align);
    default:
      throw new Error('cannot serialize node type ' + node.type.name);
  }
}

/* Exported for the roaming table-cell editor: inline content of one paragraph. */
export function serializeInlineContent(paragraphNode) {
  return inline(paragraphNode);
}

/* The canonical '::: embed dw:<ref> :::' directive for a live-linked block. Byte-exact
   mirror of the ONE wire form — DocWizEmbed.DirectiveFor ("::: embed dw:{ref}\n:::", NO
   trailing newline) and app.js embedDirective. ref is the bare "chapter#anchor"; a
   stray leading 'dw:' is tolerated. A clean embed therefore re-serializes to the exact
   directive it loaded from (byte-stable round-trip). An optional width (L205, linked-box
   resize) appends a `width=…` token; absent width emits the bare directive (so a full-size
   embed stays byte-identical). Mirrors DocWizEmbed.DirectiveFor(ref, width). */
function embedDirective(ref, width, align) {
  if (!ref) throw new Error('linked block has no reference');
  const base = '::: embed dw:' + String(ref).replace(/^dw:/, '');
  const w = width ? ' width=' + width : '';
  const a = align === 'center' || align === 'right' ? ' align=' + align : '';
  return base + w + a + '\n:::';
}

/* ---------- figures (W2/M3): canonical line from structured meta ---------- */

function serializeFigure(node) {
  const m = node.attrs.meta;
  if (!m) {
    if (!node.attrs.src) throw new Error('figure has no source text — edit it via Source (advanced)');
    return node.attrs.src;
  }
  const alt = (m.alt || '').replace(/[\\[\]]/g, (c) => '\\' + c);
  const dest = m.src || '';
  const destOut = /[\s()]/.test(dest) ? '<' + dest + '>' : dest;
  const attrs = [];
  if (m.explicitId) attrs.push('#' + m.explicitId);
  if (m.width) attrs.push('width=' + m.width);
  if (m.align) attrs.push('.img-' + m.align);
  if (m.showCaption === false) attrs.push('.no-caption');
  return '![' + alt + '](' + destOut + ')' + (attrs.length ? '{' + attrs.join(' ') + '}' : '');
}

/* ---------- tables (W3): per-row emission — untouched rows keep their bytes ---------- */

function serializeTable(node) {
  const m = node.attrs.meta;
  if (!m) {
    if (!node.attrs.src) throw new Error('table has no source text — edit it via Source (advanced)');
    return node.attrs.src;
  }
  if (!m.model) {
    // Untouched table: original lines verbatim.
    return [m.attrLine, m.headerLine, m.delimiterLine, ...m.rowLines]
      .filter((l) => l !== null && l !== undefined)
      .join('\n');
  }
  const model = m.model;
  const lines = [];
  const attrLine = model.attrSrc !== null && model.attrSrc !== undefined
    ? model.attrSrc
    : buildAttrLine(model.id, model.widths, model.width, model.align, model.caption);
  if (attrLine) lines.push(attrLine);
  lines.push(model.header.src !== null && model.header.src !== undefined
    ? model.header.src
    : canonicalRow(model.header.cells));
  lines.push(model.delimiter.src !== null && model.delimiter.src !== undefined
    ? model.delimiter.src
    : canonicalDelimiter(model.aligns, model.header.cells.length));
  for (const row of model.rows)
    lines.push(row.src !== null && row.src !== undefined ? row.src : canonicalRow(row.cells));
  return lines.join('\n');
}

function buildAttrLine(id, widths, width, align, caption) {
  const parts = [];
  if (id) parts.push('#' + id);
  if (width) parts.push('width=' + width);   // overall table-box width (L202), e.g. width=50%
  if (align === 'center' || align === 'right') parts.push('align=' + align);  // box alignment (L205)
  if (widths) parts.push('widths="' + widths + '"');
  // Sanitize here too (not only at capture in tableview): a caption may reach this from the
  // editor model OR straight from the server's parse of an existing attr line. A straight `"`
  // would close the {caption="…"} value early (Markdig has no escape inside it), so map it to a
  // typographic ” and flatten newlines (the attr line is one line).
  if (caption) { const safe = caption.replace(/"/g, '”').replace(/[\r\n]+/g, ' '); parts.push('caption="' + safe + '"'); }
  return parts.length ? '{' + parts.join(' ') + '}' : null;
}

function canonicalRow(cells) {
  return '| ' + cells.map((c) => (c.md || '').trim()).join(' | ') + ' |';
}

function canonicalDelimiter(aligns, columns) {
  const marks = [];
  for (let i = 0; i < columns; i++) {
    const a = aligns[i] || null;
    marks.push(a === 'left' ? ':---' : a === 'center' ? ':---:' : a === 'right' ? '---:' : '---');
  }
  return '|' + marks.join('|') + '|';
}

export function blocks(parent, separator) {
  const out = [];
  parent.content.forEach((child) => {
    const text = serializeBlock(child);
    if (text.length > 0) out.push(text); // empty paragraphs are not representable
  });
  return out.join(separator === undefined ? '\n\n' : separator);
}

function container(node) {
  const { type, args, label, desc, anchor } = node.attrs;
  let head;
  if (type === 'audience') head = 'audience ' + args;
  else if (CALLOUTS.includes(type)) head = type + (label && label !== DEFAULT_LABEL[type] ? ' ' + label : '');
  else if (type === 'details') head = 'details' + (label && label !== 'Details' ? ' ' + label : '');
  else if (type === 'group') {
    // Canonical group head — pinned by GroupContainerTests.Group_canonical_head_round_trips (ADR-008).
    // group <name> {#id desc="…"}; braces omitted entirely until the id exists (stamped on first save).
    const attrs = (anchor ? '#' + anchor : '') + (desc ? (anchor ? ' ' : '') + 'desc="' + desc.replace(/"/g, '') + '"' : '');
    head = 'group' + (label ? ' ' + label : '') + (attrs ? ' {' + attrs + '}' : '');
  } else head = type;

  const body = blocks(node);
  // Nested containers: the outer fence must be longer than any inner one — including one
  // indented inside a list item (a tagged step inside ::: steps).
  let colons = 3;
  for (const m of body.matchAll(/^[ \t]*(:{3,})/gm)) colons = Math.max(colons, m[1].length + 1);
  const fence = ':'.repeat(colons);
  return fence + ' ' + head + '\n' + body + '\n' + fence;
}

function codeBlock(node) {
  const text = node.textContent;
  let ticks = 3;
  for (const m of text.matchAll(/`{3,}/g)) ticks = Math.max(ticks, m[0].length + 1);
  const fence = '`'.repeat(ticks);
  return fence + (node.attrs.info || '') + '\n' + text + (text.endsWith('\n') || text === '' ? '' : '\n') + fence;
}

function list(node, markerFor) {
  const tight = node.attrs.tight;
  const items = [];
  node.content.forEach((item, _offset, i) => {
    const marker = markerFor(i, item);
    const indent = ' '.repeat(marker.length);
    const content = listItemBlocks(item, tight);
    const lines = content.split('\n');
    const text = lines
      .map((line, li) => (li === 0 ? marker + line : line.length > 0 ? indent + line : line))
      .join('\n');
    items.push(text);
  });
  return items.join(tight ? '\n' : '\n\n');
}

// Inside a TIGHT list item a nested list must hug its paragraph (a blank line would
// loosen the whole outer list on re-parse). Consecutive paragraphs still need the
// blank line — that legitimately loosens the list, and the next ingest records it.
function listItemBlocks(item, tight) {
  const parts = [];
  item.content.forEach((child) => {
    const text = serializeBlock(child);
    if (text.length === 0) return;
    if (parts.length > 0) {
      const isList = child.type.name === 'bullet_list' || child.type.name === 'ordered_list';
      parts.push(tight && isList ? '\n' : '\n\n');
    }
    parts.push(text);
  });
  return parts.join('');
}

function prefixLines(text, prefix, emptyPrefix) {
  return text.split('\n').map((l) => (l.length > 0 ? prefix + l : emptyPrefix)).join('\n');
}

/* ---------- inline serialization ---------- */

// Outer-to-inner wrapping order; code is innermost because its content is literal.
const MARK_ORDER = ['link', 'strike', 'strong', 'em', 'code'];

function inline(parent) {
  const nodes = [];
  parent.content.forEach((n) => nodes.push(n));
  const ctx = { atLineStart: true };
  return inlineNodes(nodes, [], ctx);
}

function inlineNodes(nodes, active, ctx) {
  let out = '';
  let i = 0;
  while (i < nodes.length) {
    const node = nodes[i];
    const extra = node.marks.filter((m) => !active.some((a) => a.eq(m)));
    if (extra.length > 0) {
      const mark = pickOuter(extra);
      let j = i;
      while (j < nodes.length && nodes[j].marks.some((m) => m.eq(mark))) j++;
      out += wrapMark(mark, nodes.slice(i, j), active, ctx);
      i = j;
    } else {
      out += leaf(node, ctx);
      i++;
    }
  }
  return out;
}

function pickOuter(marks) {
  for (const name of MARK_ORDER) {
    const m = marks.find((x) => x.type.name === name);
    if (m) return m;
  }
  return marks[0];
}

function wrapMark(mark, nodes, active, ctx) {
  const name = mark.type.name;
  if (name === 'code') {
    // Literal content: fence with more backticks than any run inside; pad when the
    // content starts/ends with a backtick (CommonMark code-span rules).
    const text = nodes.map((n) => n.text || '').join('');
    let ticks = 1;
    for (const m of text.matchAll(/`+/g)) ticks = Math.max(ticks, m[0].length + 1);
    const fence = '`'.repeat(ticks);
    const pad = text.startsWith('`') || text.endsWith('`') || text === '' ? ' ' : '';
    ctx.atLineStart = false;
    return fence + pad + text + pad + fence;
  }

  const innerCtx = name === 'link' ? { atLineStart: false } : ctx;
  if (name === 'link') ctx.atLineStart = false;
  const inner = inlineNodes(nodes, active.concat(mark), innerCtx);

  if (name === 'strong') return '**' + inner + '**';
  if (name === 'em') return '*' + inner + '*';
  if (name === 'strike') return '~~' + inner + '~~';
  if (name === 'link') {
    const href = fileRelative(mark.attrs.href || '');
    const dest = /[\s()]/.test(href) ? '<' + href + '>' : href;
    return '[' + inner + '](' + dest + ')';
  }
  return inner;
}

// The editor serves project files on absolute routes; chapter files reference them
// relative to content/<section>/ (always two levels deep).
function fileRelative(href) {
  if (href.startsWith('/assets/')) return '../../assets/' + href.slice('/assets/'.length);
  if (href.startsWith('/attachments/')) return '../../attachments/' + href.slice('/attachments/'.length);
  return href;
}

function leaf(node, ctx) {
  if (node.type.name === 'hard_break') {
    ctx.atLineStart = true;
    // <br> is DocWiz's canonical hard break everywhere (DocWizLineBreak.cs renders it as
    // a break in body text AND inside a pipe-table cell, where a literal newline would end
    // the row). Round-trips 1:1; legacy two-space breaks normalize to <br> on a touched save.
    return '<br>';
  }
  if (node.type.name === 'image') {
    ctx.atLineStart = false;
    const alt = (node.attrs.alt || '').replace(/[\\[\]]/g, (c) => '\\' + c);
    const dest = fileRelative(node.attrs.src || '');
    const attrs = [];
    if (node.attrs.width) attrs.push('width=' + node.attrs.width);
    if (node.attrs.align) attrs.push('.img-' + node.attrs.align);
    return '![' + alt + '](' + (/[\s()]/.test(dest) ? '<' + dest + '>' : dest) + ')'
      + (attrs.length ? '{' + attrs.join(' ') + '}' : '');
  }
  const text = escapeText(node.text || '', ctx.atLineStart);
  if ((node.text || '').length > 0) ctx.atLineStart = false;
  return text;
}

/* Minimal-but-safe escaping under the DocWiz pipeline (DisableHtml, GenericAttributes,
   no autolink extension beyond CommonMark's <scheme:…>):
   - always: \ ` * _ [ ] { } and '<' (corpus convention from the Word import), '!'
     only when it precedes '[', '&' only when it starts an entity-looking sequence
   - additionally at line start: # > + - = ~ | and "1." / "1)" list markers */
function escapeText(text, atLineStart) {
  let out = text
    .replace(/[\\`*_[\]{}<]/g, (c) => '\\' + c)
    .replace(/!(?=\[)/g, '\\!')
    .replace(/&(?=[a-zA-Z][a-zA-Z0-9]{1,31};|#\d{1,7};|#[xX][0-9a-fA-F]{1,6};)/g, '\\&');
  if (atLineStart) {
    out = out.replace(/^([#>+\-=~|])/, '\\$1').replace(/^(\d{1,9})([.)])/, '$1\\$2');
  }
  return out;
}
