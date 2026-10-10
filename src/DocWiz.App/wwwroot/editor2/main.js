/* Live editor surface entry point (ADR-007) — THE chapter editor since the M5 flip
   (2026-06-12). The surface is the handover page itself: prose edits in place,
   tables/figures/containers are structured node views, and saves are byte-surgical —
   clean blocks save as verbatim copy spans of their original bytes; only edited
   blocks re-serialize (serialize.js). app.js drives this via window.DocWizEditor2
   (set on module load; module scripts run deferred). */
import { EditorState, TextSelection, NodeSelection, Selection, Plugin } from 'prosemirror-state';
import { EditorView } from 'prosemirror-view';
import { keymap } from 'prosemirror-keymap';
import { history, undo, redo } from 'prosemirror-history';
import { baseKeymap, toggleMark, setBlockType, wrapIn, lift } from 'prosemirror-commands';
import { findWrapping } from 'prosemirror-transform';
import { inputRules, wrappingInputRule, textblockTypeInputRule, InputRule } from 'prosemirror-inputrules';
import { splitListItem, liftListItem, sinkListItem, wrapInList } from 'prosemirror-schema-list';
import { dropCursor } from 'prosemirror-dropcursor';
import { gapCursor, GapCursor } from 'prosemirror-gapcursor';
import { createSchema } from './schema.js';
import { FIGURE_WIDTHS, CELL_IMAGE_WIDTHS, CTX_FLASH_MS } from './constants.js';
import { buildDoc } from './ingest.js';
import { nodeViews } from './views.js';
import { serializeBlock } from './serialize.js';
import { icon, sep } from './icons.js';
import { BADGES, menuItem, openContextMenu } from './contexttools.js';
import { routePastedImage, dataImageGuard } from './pasteimage.js';

const schema = createSchema();

// One open chapter at a time.
let current = null; // { file, dto, originals: Map(srcId → node), view, opts, sourceArea }

/* The toolbar/commands act on ONE view at a time. For the chapter editor that is
   always current.view; for the slide canvas (slideedit.js) it is whichever per-zone
   EditorView has focus. setActiveView is called on focus/selection in a zone (and by
   open() for the chapter). getActiveView falls back to the open chapter so nothing
   regresses when no zone is focused. The chapter LIFECYCLE (open/save/rebuild/
   byte-surgical payload) stays on current — zones never use that path (Task 7c owns
   per-zone save). */
let activeView = null;
function getActiveView() { return activeView || (current && current.view) || null; }
/* Switching the active view (e.g. clicking a DIFFERENT slide zone) must clear the
   PREVIOUS view's contextual toolbar. Each .slide-region is a separate EditorView and the
   contextual TABLE/IMAGE bar is ONE shared singleton (contextBar), driven by the focused
   node view's selectNode/deselectNode. PM views do NOT drop their selection on blur, so a
   zone that still holds a NodeSelection never fires deselectNode — its stale tools (and the
   hidden main track) persist over the newly focused zone (BUG 2/3). When we move to a
   genuinely different view, step the OUTGOING view out of any NodeSelection: that fires its
   node view's deselectNode → contextBar.set(null) → the main track returns. setActiveView(null)
   (unmount / clicking off all zones) likewise clears a lingering node selection. The chapter
   editor is a single view, so newView === activeView and nothing changes there.

   setActiveView is called synchronously from INSIDE the INCOMING zone's dispatchTransaction
   (slideedit.js), so dispatching on the OUTGOING zone here would mutate a DIFFERENT EditorView
   mid-dispatch (re-entrancy). Defer that outgoing-zone deselect to a macrotask so the current
   dispatch finishes first; re-check the view is still alive and still in a NodeSelection then. */
function setActiveView(view) {
  const prev = activeView;
  activeView = view;
  if (prev && prev !== view) {
    setTimeout(() => {
      if (!prev.isDestroyed && prev.state.selection instanceof NodeSelection)
        stepOutOfNodeSelection(prev, /* keepFocus */ true);
    }, 0);
  }
}

/* Esc steps OUT of a selected table/figure/container/embed: replace the NodeSelection
   with a text caret next to the node so the node view's deselectNode runs (commits any
   open cell editor, hides its tools). The node's view first deselects, then focus
   returns to the surface. Returns true when it acted (a NodeSelection was present).

   Robust placement is the crux of the slide-zone Esc fix (BUG 6): a freshly inserted
   table is frequently the LAST block of a short zone, so a forward-biased
   TextSelection.near(end, 1) finds no text position after it and leaves the NodeSelection
   in place — Esc appeared to do nothing. We compute a caret BEFORE the node (bias -1) and
   fall back to AFTER it (bias 1), so a node at either edge of the doc always steps out.
   Works for ANY view (chapter or a focused slide zone) — the caller passes the active
   view, mirroring how the toolbar follows getActiveView(). */
function stepOutOfNodeSelection(view, keepFocus) {
  if (!view) return false;
  const { state } = view;
  if (!(state.selection instanceof NodeSelection)) return false;
  // Prefer a caret just BEFORE the node (Selection.near searches outward from there and
  // never lands back ON the node); fall back to just AFTER it for a node at doc start.
  const before = Selection.near(state.doc.resolve(state.selection.from), -1);
  let target = before instanceof NodeSelection
    ? Selection.near(state.doc.resolve(state.selection.to), 1)
    : before;
  // Atom-only zone (e.g. a two-column region that is JUST a linked embed, or a media zone
  // holding one figure): there is NO text caret to step to, so both Selection.near calls
  // return the SAME node selection and Esc appeared to do nothing (the linked-block tools
  // stayed up). Fall back to a gap cursor beside the node — a non-node selection, so the
  // node view's deselectNode runs and its context tools clear.
  if (target instanceof NodeSelection) {
    const $to = state.doc.resolve(state.selection.to);
    const $from = state.doc.resolve(state.selection.from);
    if (GapCursor.valid($to)) target = new GapCursor($to);
    else if (GapCursor.valid($from)) target = new GapCursor($from);
  }
  view.dispatch(state.tr.setSelection(target));
  // keepFocus: the active-view switch deselects the OUTGOING zone but must NOT pull focus
  // back to it — the caret belongs to the newly focused zone. The Esc path (keepFocus
  // falsy) keeps refocusing the surface as before.
  if (!keepFocus) view.focus();
  return true;
}

/* Figure/table captions renumber live: whenever the document changes, recount in
   document order and patch metas (M4-4). Display-only — the save path ignores
   number when deciding whether a block is clean. */
function renumberPlugin() {
  return new Plugin({
    appendTransaction(transactions, _oldState, newState) {
      if (!transactions.some((tr) => tr.docChanged)) return null;
      let tr = null;
      let figures = 0;
      let tables = 0;
      newState.doc.descendants((node, pos) => {
        if (node.type.name !== 'figure' && node.type.name !== 'table') return node.isBlock;
        const n = node.type.name === 'figure' ? ++figures : ++tables;
        if (node.attrs.meta && node.attrs.meta.number !== n) {
          tr = tr || newState.tr;
          tr.setNodeMarkup(pos, null, { ...node.attrs, meta: { ...node.attrs.meta, number: n } });
        }
        return false;
      });
      return tr;
    },
  });
}

/* Inline autoformat: typing the closing delimiter (double-star, star, tildes,
   backtick) converts to the mark and the literal characters disappear — one
   Ctrl+Z restores the typed text. */
function markInputRule(regexp, markType) {
  return new InputRule(regexp, (state, match, start, end) => {
    const tr = state.tr;
    tr.replaceWith(start, end, schema.text(match[1]));
    tr.addMark(start, start + match[1].length, markType.create());
    tr.removeStoredMark(markType); // typing continues unformatted after it
    return tr;
  });
}

/* Markdown typing shortcuts (stakeholder-approved "autoformat", 2026-06-12).
   These create MODEL nodes directly — no markdown is parsed in the browser
   (ADR-004 holds). Conservative triggers: ordered lists only on "1. " so part
   numbers like "2026." never convert; main surface only, never table cells. */
function autoformatRules() {
  return inputRules({
    rules: [
      textblockTypeInputRule(/^(#{1,4})\s$/, schema.nodes.heading, (m) => ({ level: m[1].length })),
      wrappingInputRule(/^\s*[-*]\s$/, schema.nodes.bullet_list),
      wrappingInputRule(/^1\.\s$/, schema.nodes.ordered_list),
      wrappingInputRule(/^>\s$/, schema.nodes.blockquote),
      markInputRule(/\*\*([^*\s][^*]*)\*\*$/, schema.marks.strong),
      markInputRule(/(?<!\*)\*([^*\s][^*]*)\*$/, schema.marks.em),
      markInputRule(/~~([^~\s][^~]*)~~$/, schema.marks.strike),
      markInputRule(/`([^`\s][^`]*)`$/, schema.marks.code),
    ],
  });
}

/* Task 11: pressing Enter inside an EMPTY trailing paragraph that is the LAST
   child of a container (callout/audience/details/…) exits the container by
   deleting that empty trailing para and inserting a new empty paragraph AFTER
   the container — the same behaviour Word/Notion give for pressing Enter at the
   end of a block quote or callout.

   Runs AHEAD of baseKeymap (separate keymap plugin earlier in the array) so
   normal Enter elsewhere still works unchanged. Falls through (returns false)
   whenever the cursor is not in that exact situation. */
function exitContainerOnEnter(state, dispatch) {
  const { $from, empty } = state.selection;
  if (!empty) return false;
  if ($from.parent.type.name !== 'paragraph') return false;
  if ($from.parent.content.size !== 0) return false; // paragraph must be empty
  const depth = $from.depth;
  if (depth < 2) return false; // must be inside at least a container
  const container = $from.node(depth - 1);
  if (container.type.name !== 'container') return false;
  // Must be the LAST child of the container (trailing empty line).
  if ($from.index(depth - 1) !== container.childCount - 1) return false;
  if (dispatch) {
    const after = $from.after(depth - 1); // position after the container node
    const para = state.schema.nodes.paragraph.create();
    // Delete the empty trailing para from the container, then insert a new
    // paragraph right after the container.
    let tr = state.tr.delete($from.before(depth), $from.after(depth));
    const insertAt = tr.mapping.map(after);
    tr = tr.insert(insertAt, para)
           .setSelection(TextSelection.create(tr.doc, insertAt + 1));
    dispatch(tr.scrollIntoView());
  }
  return true;
}

function plugins() {
  return [
    renumberPlugin(),
    autoformatRules(),
    history(),
    // Task 11: container-exit keymap runs BEFORE baseKeymap so it intercepts Enter
    // in an empty trailing container paragraph; falls through otherwise.
    keymap({ Enter: exitContainerOnEnter }),
    keymap({
      'Mod-z': undo,
      'Mod-y': redo,
      'Shift-Mod-z': redo,
      'Mod-b': toggleMark(schema.marks.strong),
      'Mod-i': toggleMark(schema.marks.em),
      Enter: splitListItem(schema.nodes.list_item),
      Tab: sinkListItem(schema.nodes.list_item),
      'Shift-Tab': liftListItem(schema.nodes.list_item),
      // Esc steps OUT: a selected table/figure deselects (its view\'s
      // deselectNode commits any open cell editor and hides its tools). Acts on the
      // view the keymap fires in — the chapter view or a focused slide zone — and
      // places the caret robustly even when the node is the last block (BUG 6).
      Escape: (state, dispatch, view) => {
        if (!(state.selection instanceof NodeSelection)) return false;
        if (dispatch) stepOutOfNodeSelection(view);
        return true;
      },
    }),
    keymap(baseKeymap),
    dropCursor(),
    gapCursor(),
    dataImageGuard(), // rescue a Word-bitmap paste (inlined as a data: URI) into an asset
  ];
}

/* Toolbar controller (Phase 4 — replace model):
   One 48px shell (.e2-maintoolbar). Two internal tracks:
   - .e2-tb-main  : the main formatting groups + the right-pinned view switch
   - .e2-tb-ctx   : contextual format bar (IMAGE / TABLE) — badge + tools + Esc hint
   When nothing is selected → main track visible, shell has no .context class.
   When a figure/table is selected → node view calls set(toolsEl, label, 'image'|'table')
   which swaps to the ctx track (3px accent inset line via .context).
   For link/characters contextual tools → the legacy .e2-contextbar sub-row is used
   (kind is undefined / 'link' / 'characters') so those existing selectors are stable.
   set(null) → restores main track. */
const contextBar = {
  el: null,         // .e2-contextbar  (link / characters / flash sub-row)
  toolbarEl: null,  // .e2-maintoolbar shell (the 48px bar)
  _kind: null,      // 'image' | 'table' | null
  set(toolsEl, label, kind) {
    // The MAIN formatting toolbar stays visible for EVERY context (stakeholder, slides
    // dogfooding): selecting/editing a table or image must NOT take away bold/italic/
    // lists — those commands route to the open table cell (runEitherCmd → cellView), so
    // the author can style text INSIDE a table. Contextual tools (image/table structure,
    // link, characters) render in the sub-row BELOW the main toolbar — never replacing it.
    // (Earlier this swapped the main track out for an IMAGE/TABLE ctx track; that hid the
    // formatting buttons while a table was active. Same behaviour now in chapter + slides.)
    this._kind = BADGES[kind] ? kind : null;
    if (this.toolbarEl) {
      const main = this.toolbarEl.querySelector('.e2-tb-main');
      const ctx = this.toolbarEl.querySelector('.e2-tb-ctx');
      if (main) main.hidden = false;
      if (ctx) { ctx.innerHTML = ''; ctx.hidden = true; }
      this.toolbarEl.classList.remove('context');
    }
    if (!this.el) return;
    this.el.innerHTML = '';
    // The sub-row keeps its RESERVED height even when empty (e2-ctx-empty just hides its
    // border/content) so showing the contextual tools never changes the toolbar band's
    // height — selecting a table/link/image no longer shoves the content (and the title)
    // down or behind the bar. (This replaced the overflow-anchor:none reflow hack; a
    // constant-height header neither covers nor shifts.)
    if (!toolsEl) { this.el.classList.add('e2-ctx-empty'); return; }
    this.el.classList.remove('e2-ctx-empty');
    // Node kinds with a context badge (image/table/embed) get an icon badge from the ONE
    // shared BADGES map (contexttools.js); link/characters/containers get a text label.
    const badgeSpec = BADGES[kind];
    if (badgeSpec) {
      const badge = document.createElement('span');
      badge.className = 'e2-ctx-badge';
      badge.appendChild(icon(badgeSpec.icon));
      badge.appendChild(document.createTextNode(badgeSpec.label));
      this.el.appendChild(badge);
    } else {
      const tag = document.createElement('span');
      tag.className = 'e2-ctx-label';
      tag.textContent = label || '';
      this.el.appendChild(tag);
    }
    this.el.appendChild(toolsEl);
  },
  // Transient notice (e.g. a locked cell) — shown even when no tools are mounted.
  flash(message) {
    // Always use the sub-row for flashes (never interrupts the format bar).
    if (!this.el) return;
    this.el.classList.remove('e2-ctx-empty');
    let note = this.el.querySelector('.e2-ctx-flash');
    if (!note) {
      note = document.createElement('span');
      note.className = 'e2-ctx-flash';
      this.el.appendChild(note);
    }
    note.textContent = message;
    clearTimeout(note._t);
    note._t = setTimeout(() => {
      note.remove();
      if (this.el.childElementCount === 0) this.el.classList.add('e2-ctx-empty');
    }, CTX_FLASH_MS);
  },
};

async function open(file, opts) {
  const res = await fetch('/api/editor-doc?file=' + encodeURIComponent(file));
  if (!res.ok) throw new Error((await res.text()) || res.statusText);
  const dto = await res.json();

  close();
  const mount = document.getElementById('editor2');
  mount.innerHTML = '';

  // Toolbar + context bar share one sticky wrapper so the contextual tools stay
  // visible while scrolled deep inside a long table.
  const top = document.createElement('div');
  top.className = 'e2-top';
  // The main toolbar shell is built before the article but after we have the banner
  // so that buildMainToolbar can place the view-switch buttons which reference IDs.
  // contextBar.toolbarEl is set when buildMainToolbar() returns.
  contextBar.el = document.createElement('div');
  contextBar.el.className = 'e2-contextbar e2-ctx-empty';
  // Esc must work from the bar\'s own inputs too (label/caption fields), and also
  // from inside the main toolbar\'s contextual track (caption input on picture bar).
  const escFromBar = (event) => {
    if (event.key !== 'Escape') return;
    const view = getActiveView();
    if (!view) return;
    event.preventDefault();
    if (!stepOutOfNodeSelection(view)) view.focus();
  };
  contextBar.el.addEventListener('keydown', escFromBar);
  top.appendChild(contextBar.el);
  mount.appendChild(top);

  const article = document.createElement('article');
  if (dto.numbering && dto.chapterNumber > 0) {
    article.setAttribute('data-num', '');
    article.style.setProperty('--dwch', "'" + dto.chapterNumber + "'");
  }
  mount.appendChild(article);

  current = { file, dto, originals: new Map(), opts: opts || {}, view: null, sourceArea: null };
  contextBar.audiences = (opts && opts.audiences) || [];
  contextBar.chapterId = dto.id; // node views build dw: links from it
  const doc = buildDoc(schema, dto);
  rememberOriginals(doc);

  current.view = new EditorView(article, {
    state: EditorState.create({ schema, doc, plugins: plugins() }),
    nodeViews: nodeViews(schema, contextBar),
    dispatchTransaction(tr) {
      const view = current.view;
      view.updateState(view.state.apply(tr));
      // The chapter editor is the active view whenever it is being driven.
      setActiveView(view);
      if (tr.docChanged && current.opts.onDirty) current.opts.onDirty();
      updateAnchorInfo();
      updateToolbarState();
    },
    handlePaste: (_v, event) => {
      // Pasting a copied dw: reference (the heading "copy link" button) opens the
      // link dialog with the target\'s natural name prefilled — paste-and-go.
      const text = event.clipboardData && event.clipboardData.getData('text/plain');
      if (text && /^dw:[A-Za-z0-9_-]+(#[A-Za-z0-9_-]+)?$/.test(text.trim())) {
        const bridge = window.DocWizBridge;
        if (bridge && bridge.dwPaste) {
          bridge.dwPaste(text.trim(), (res) => insert({ type: 'dwlink', href: res.href, text: res.text }));
          return true;
        }
      }
      return pasteImageFiles(event.clipboardData);
    },
    handleDrop: (_v, event) => pasteImageFiles(event.dataTransfer),
    handleDOMEvents: {
      // Clicking a link shows its tools in the context bar instead of navigating
      // the editor away (dw: → Open chapter; files → Open in a new tab).
      click: (_v, event) => {
        const link = event.target.closest && event.target.closest('a[href]');
        if (!link || link.closest('.e2-cell-editing') || !current || !current.view.dom.contains(link)) return false;
        linkClickTools(link, event);
        return true;
      },
      // Right-click on selected text: offer "Group selection…" via the shared menu.
      // Figures and tables already have their own right-click menus (figureview/tableview)
      // so skip when the target is inside one of those; an empty selection has nothing to group.
      contextmenu: (view, event) => {
        if (view.state.selection.empty) return false;
        if (event.target.closest && event.target.closest('.dw-figure, .dw-table-figure, .e2-group')) return false;
        event.preventDefault();
        const menu = document.createElement('div');
        menu.className = 'e2-menu';
        menu.appendChild(menuItem(null, 'Group selection…', () => createGroup()));
        openContextMenu(menu, event.clientX, event.clientY);
        return true;
      },
    },
  });

  setActiveView(current.view); // chapter editor is the active view on open

  const tb = buildMainToolbar();
  contextBar.toolbarEl = tb;
  top.insertBefore(tb, contextBar.el);
  contextBar.set(null);
  // The escFromBar listener also needs to cover the toolbar shell (the caption
  // input in the Image format bar lives inside it).
  tb.addEventListener('keydown', escFromBar);

  // Dev layout probe (#…&e2probe in the URL): reports the figure\'s ancestor chain —
  // overflow/contain/rects — for breakout-clipping diagnostics in the REAL page.
  if (location.hash.includes('e2probe')) {
    setTimeout(() => {
      const fig = document.querySelector('#editor2 .dw-table-figure');
      const chain = [];
      let el = fig && fig.parentElement;
      while (el && el !== document.body) {
        const cs = getComputedStyle(el);
        const r = el.getBoundingClientRect();
        chain.push({
          el: el.tagName + (el.id ? '#' + el.id : '') + (el.className && el.className.split ? '.' + el.className.split(' ')[0] : ''),
          overflowX: cs.overflowX, contain: cs.contain, display: cs.display,
          left: Math.round(r.left), right: Math.round(r.right), width: Math.round(r.width),
        });
        el = el.parentElement;
      }
      // Which stylesheet rule sets overflow on the PM root?
      const pm = fig && fig.parentElement;
      const culprits = [];
      if (pm) {
        for (const sheet of document.styleSheets) {
          let rules;
          try { rules = sheet.cssRules; } catch { continue; }
          const walk = (list) => {
            for (const r of list) {
              if (r.cssRules) walk(r.cssRules);
              if (r.style && (r.style.overflowX || r.style.overflow)) {
                try {
                  if (r.selectorText && pm.matches(r.selectorText))
                    culprits.push({ sel: r.selectorText, overflow: r.style.overflow, overflowX: r.style.overflowX, from: sheet.href || 'inline-style-tag' });
                } catch { /* unsupported selector */ }
              }
            }
          };
          walk(rules);
        }
      }
      const pre = document.createElement('pre');
      pre.id = 'out';
      pre.textContent = JSON.stringify({
        fig: fig ? fig.getBoundingClientRect().toJSON() : null,
        pmInline: pm ? pm.style.cssText : null,
        culprits,
        chain,
      });
      document.body.appendChild(pre);
      document.title = 'PROBE-DONE';
    }, 1200);
  }
  return dto;
}

function rememberOriginals(doc) {
  current.originals.clear();
  doc.content.forEach((node) => {
    const srcId = node.attrs.srcId;
    if (srcId !== null && srcId !== undefined && !current.originals.has(srcId))
      current.originals.set(srcId, node);
  });
}

/* ---------- byte-surgical save payload ---------- */

/* Clean = byte-copyable. meta.number is display-only (live renumbering shifts it
   when blocks are inserted above) and must not flip untouched blocks to dirty. */
function nodeClean(original, node) {
  if (original.eq(node)) return true;
  if (original.type !== node.type || !original.content.eq(node.content)) return false;
  if (!original.sameMarkup(node)) {
    const strip = (attrs) => JSON.stringify({ ...attrs, meta: attrs.meta ? { ...attrs.meta, number: 0 } : null });
    return strip(original.attrs) === strip(node.attrs);
  }
  return true;
}

function nextBlockIndexAfter(segments, j) {
  for (let k = j + 1; k < segments.length; k++)
    if (segments[k].kind === 'block') return k;
  return -1;
}

function firstBlockIndex(segments) {
  return segments.findIndex((s) => s.kind === 'block');
}

export function buildSavePayload() {
  const segs = current.dto.segments;
  const doc = current.view.state.doc;
  const ops = [];
  const usedSrc = new Set();
  let prevSrcId = null;
  let first = true;

  doc.content.forEach((node) => {
    const srcId = node.attrs.srcId;
    const original = srcId !== null && srcId !== undefined ? current.originals.get(srcId) : undefined;
    const clean = original !== undefined && !usedSrc.has(srcId) && nodeClean(original, node);

    // Separator before this block: the original gap when the two blocks were
    // adjacent in the original order (preserves double blank lines etc.),
    // canonical "\n\n" otherwise.
    if (first) {
      if (srcId === firstBlockIndex(segs))
        for (let k = 0; k < srcId; k++) ops.push({ copyStart: segs[k].start, copyEnd: segs[k].end });
    } else if (
      prevSrcId !== null && srcId !== null && srcId !== undefined &&
      nextBlockIndexAfter(segs, prevSrcId) === srcId
    ) {
      for (let k = prevSrcId + 1; k < srcId; k++)
        ops.push({ copyStart: segs[k].start, copyEnd: segs[k].end });
    } else {
      ops.push({ text: '\n\n' });
    }

    if (clean) {
      const seg = segs[srcId];
      ops.push({ copyStart: seg.start, copyEnd: seg.end });
      usedSrc.add(srcId);
    } else {
      ops.push({ text: serializeBlock(node) });
    }

    prevSrcId = srcId === undefined ? null : srcId;
    first = false;
  });

  // Trailing gap (usually the final newline) when the last block is the original last.
  if (prevSrcId !== null && nextBlockIndexAfter(segs, prevSrcId) === -1) {
    for (let k = prevSrcId + 1; k < segs.length; k++)
      ops.push({ copyStart: segs[k].start, copyEnd: segs[k].end });
  }
  return ops;
}

function resolveOps(ops) {
  const body = current.dto.segments.map((s) => s.src).join('');
  const offsets = [];
  let pos = 0;
  for (const s of current.dto.segments) {
    offsets.push(pos);
    pos += s.src.length;
  }
  // segment starts in dto are offsets into the ORIGINAL body string — identical to
  // the rebuilt concat because segments partition it (harness L1 invariant).
  return ops.map((op) => (op.text !== undefined ? op.text : body.slice(op.copyStart, op.copyEnd))).join('');
}

/* ---------- insertion (M4): live blocks, never markdown text ---------- */

function placeholderPara(text) {
  return schema.nodes.paragraph.create(null, schema.text(text));
}

function dwLinkText(text, href) {
  return schema.text(text, [schema.marks.link.create({ href })]);
}

function buildElementNode(kind) {
  const c = (attrs, content) => schema.nodes.container.create({ args: '', label: null, ...attrs }, content);
  const li = (content) => schema.nodes.list_item.create(null, content);
  switch (kind) {
    case 'note': case 'tip': case 'warning': case 'danger':
      return c({ type: kind }, placeholderPara(
        kind === 'warning' ? 'Describe the hazard and how to avoid it.'
          : kind === 'danger' ? 'Describe the danger. State what must never be done.'
          : kind === 'tip' ? 'A faster or safer way to do it.' : 'Good to know.'));
    case 'steps':
      return c({ type: 'steps' }, schema.nodes.ordered_list.create({ tight: true }, [
        li(placeholderPara('First step.')), li(placeholderPara('Second step.')), li(placeholderPara('Third step.')),
      ]));
    case 'cards':
      return c({ type: 'cards' }, schema.nodes.bullet_list.create({ tight: true }, [
        li(schema.nodes.paragraph.create(null, [dwLinkText('Chapter title', 'dw:chapter-id'), schema.text(' — one-line description')])),
        li(schema.nodes.paragraph.create(null, [dwLinkText('Another chapter', 'dw:chapter-id'), schema.text(' — one-line description')])),
      ]));
    case 'buttons':
      return c({ type: 'buttons' }, schema.nodes.paragraph.create(null, dwLinkText('Open the manual', 'dw:chapter-id')));
    case 'details':
      return c({ type: 'details', label: 'More information' }, placeholderPara('Hidden until the reader clicks.'));
    case 'center': case 'right':
      return c({ type: kind }, placeholderPara(kind === 'center' ? 'Centered content.' : 'Right-aligned content.'));
    default:
      throw new Error('unknown element kind ' + kind);
  }
}

/* The top-level block holding a collapsed text selection, when it is an EMPTY paragraph
   — the block a fresh insert should REPLACE rather than push below (empty-block hygiene,
   Task 9: inserting an embed into an empty zone must not strand a leading blank paragraph).
   Returns null for a node selection, a non-empty block, or anything not a top-level
   paragraph. */
function emptyTopParagraphRange(state) {
  if (state.selection instanceof NodeSelection) return null;
  const $from = state.selection.$from;
  if ($from.depth !== 1) return null; // only a bare top-level block, never inside a list/quote/container
  const node = $from.parent;
  if (node.type.name !== 'paragraph' || node.content.size !== 0) return null;
  return { from: $from.before(1), to: $from.after(1) };
}

function insertBlockAfterSelection(node) {
  const view = getActiveView();
  if (!view) return;
  const { state } = view;

  // Replace a lone empty paragraph in place (e.g. inserting into an empty zone) so no
  // blank paragraph is stranded above the new block.
  const empty = emptyTopParagraphRange(state);
  if (empty) {
    let tr = state.tr.replaceWith(empty.from, empty.to, node);
    const sel = node.isAtom && node.type.spec.selectable
      ? NodeSelection.create(tr.doc, empty.from)
      : TextSelection.near(tr.doc.resolve(Math.min(empty.from + 1, tr.doc.content.size)), 1);
    tr = tr.setSelection(sel).scrollIntoView();
    view.dispatch(tr);
    view.focus();
    return;
  }

  let pos;
  try {
    pos = state.selection instanceof NodeSelection ? state.selection.to : state.selection.$to.after(1);
  } catch {
    pos = state.doc.content.size;
  }
  let tr = state.tr.insert(pos, node);
  tr = tr.setSelection(TextSelection.near(tr.doc.resolve(Math.min(pos + 1, tr.doc.content.size)), 1)).scrollIntoView();
  view.dispatch(tr);
  view.focus();
}

function insertInline(nodes) {
  // Inline content (links) is representable inside a table cell — with a cell
  // editor open it goes there, like the image insert does.
  const mini = cellView();
  if (mini) {
    mini.dispatch(mini.state.tr.replaceSelectionWith(Array.isArray(nodes) ? nodes[0] : nodes, false).scrollIntoView());
    mini.focus();
    return;
  }
  const view = getActiveView();
  if (!view) return;
  const { state } = view;
  if (state.selection instanceof NodeSelection) {
    insertBlockAfterSelection(schema.nodes.paragraph.create(null, nodes));
    return;
  }
  view.dispatch(state.tr.replaceSelectionWith(Array.isArray(nodes) ? nodes[0] : nodes, false).scrollIntoView());
  view.focus();
}

/* Public insert API — app.js dialogs call this when the live surface is active.
   spec.type: element|image|audience|dwlink|attachment|wrap */
function insert(spec) {
  if (!getActiveView()) throw new Error('no editor open in the live surface');
  switch (spec.type) {
    case 'element':
      insertBlockAfterSelection(buildElementNode(spec.kind));
      return;
    case 'audience': {
      const args = (spec.audiences || []).join(' ');
      insertBlockAfterSelection(schema.nodes.container.create(
        { type: 'audience', args, label: null },
        placeholderPara('Write the content for this audience here.')));
      return;
    }
    case 'image': {
      // With a table cell open, the picture goes INLINE into that cell
      // (stakeholder: images in tables with the same controls).
      const cell = window.__dwActiveCell;
      if (cell && cell.view) {
        const inlineImg = schema.nodes.image.create({
          src: spec.src, alt: spec.alt || '', width: spec.width || null, align: spec.align || null,
        });
        cell.view.dispatch(cell.view.state.tr.replaceSelectionWith(inlineImg, false).scrollIntoView());
        cell.view.focus();
        return;
      }
      const figNode = schema.nodes.figure.create({
        html: '', src: '',
        // Task 12: figures inside callouts default showCaption=false (no caption clutter).
        meta: { src: spec.src, alt: spec.alt || '', width: spec.width || null, align: spec.align || null, explicitId: null, number: 0, showCaption: false },
      });
      // Task 12: if the cursor is inside a container (callout/audience/details/…),
      // insert the figure as a block child of the container rather than at doc level.
      // The container schema allows `block+` children (including figure), so this is
      // always valid. The figure gets full FigureView tools because it IS a figure block.
      const view = getActiveView();
      if (view) {
        const { $from } = view.state.selection;
        for (let d = $from.depth; d >= 1; d--) {
          if ($from.node(d).type.name === 'container') {
            // Insert the figure after the paragraph where the cursor lives, keeping
            // it as a block child of the container (block+ allows figure children).
            const childDepth = d + 1; // depth of the paragraph inside the container
            const afterPara = $from.after(childDepth > $from.depth ? $from.depth : childDepth);
            const insertAt = Math.min(afterPara, view.state.doc.content.size);
            let tr = view.state.tr.insert(insertAt, figNode);
            tr = tr.setSelection(NodeSelection.create(tr.doc, insertAt)).scrollIntoView();
            view.dispatch(tr);
            view.focus();
            return;
          }
        }
      }
      insertBlockAfterSelection(figNode);
      return;
    }
    case 'dwlink':
      insertInline(dwLinkText(spec.text || spec.href, spec.href));
      return;
    case 'attachment':
      insertInline(schema.text(spec.text, [schema.marks.link.create({ href: spec.href })]));
      return;
    case 'embed': {
      // A linked element (Slides v2, Task 9): the server resolved the directive for
      // DISPLAY (spec.html, one <div class="dw-embed"> wrapper) and sent the bare
      // "chapter#anchor" ref (spec.ref) — the round-trip source of truth. Insert the
      // dedicated embed atom (the SAME node ingest.js builds for a server-resolved
      // embed): EmbedView renders the transclusion under a LINKED pill and serialize.js
      // re-emits the canonical '::: embed dw:ref :::' directive from ref.
      if (!spec.ref) throw new Error('embed insert needs a chapter#anchor reference');
      insertBlockAfterSelection(schema.nodes.embed.create({ ref: spec.ref, html: spec.html || '' }));
      return;
    }
    case 'wrap': {
      const view = getActiveView();
      if (!view) return;
      const { $from, $to } = view.state.selection;
      const range = $from.blockRange($to);
      const wrapping = range && findWrapping(range, schema.nodes.container, { type: spec.kind, args: '', label: null });
      if (!wrapping) throw new Error('this selection cannot be wrapped');
      view.dispatch(view.state.tr.wrap(range, wrapping).scrollIntoView());
      view.focus();
      return;
    }
    default:
      throw new Error('unknown insert type ' + spec.type);
  }
}

/* Relink (Slides v2, Task 9): turn the SELECTED custom block into a live-linked embed.
   The target range is the NodeSelection'd node (e.g. a callout selected via its pill) or,
   for a text selection, the enclosing top-level block. Picks a chapter section, resolves
   the directive through the one pipeline (/api/editor-fragment — the browser never parses
   markdown), and replaces the target block with an embed node (resolved render + LINKED
   pill). The replaced content is discarded — the author is consciously swapping their
   block for a live link, the inverse of Detach. */
function relinkTargetRange(view) {
  const { state } = view;
  const sel = state.selection;
  if (sel instanceof NodeSelection) return { from: sel.from, to: sel.to };
  const $from = sel.$from;
  if ($from.depth < 1) return null;
  return { from: $from.before(1), to: $from.after(1) };
}

function relinkSelection() {
  const view = getActiveView();
  if (!view) return;
  if (cellView()) { contextBar.flash('Finish the cell first — Enter commits it, Esc cancels.'); return; }
  const range = relinkTargetRange(view);
  if (!range) { contextBar.flash('Click into a block first, then Link it to a chapter section.'); return; }
  const b = window.DocWizBridge;
  if (!b || !b.pickSection) return;
  b.pickSection((ref) => {
    if (!ref) return;
    const directive = b.embedDirective(ref); // the one classic-script embed-directive producer (app.js)
    fetch('/api/editor-fragment', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ markdown: directive, audience: contextBar.audience || null }),
    })
      .then((r) => { if (!r.ok) return r.text().then((t) => { throw new Error(t || r.statusText); }); return r.json(); })
      .then((dto) => {
        const seg = (dto.segments || []).find((s) => s.kind === 'block') || (dto.segments || [])[0];
        if (!seg) throw new Error('the linked section produced no content');
        const node = schema.nodes.embed.create({ ref: seg.ref || String(ref).replace(/^dw:/, ''), html: seg.html || '' });
        // Re-resolve the range against the CURRENT doc (the picker dialog is async).
        const cur = relinkTargetRange(view) || range;
        const to = Math.min(cur.to, view.state.doc.content.size);
        let tr = view.state.tr.replaceWith(cur.from, to, node);
        tr = tr.setSelection(NodeSelection.create(tr.doc, cur.from)).scrollIntoView();
        view.dispatch(tr);
        view.focus();
      })
      .catch((e) => {
        if (b.toast) b.toast('Could not link this block: ' + e.message, 'err');
        else window.alert('Could not link this block: ' + e.message);
      });
  });
}

function createGroup() {
  const view = getActiveView();
  if (!view) return;
  if (cellView()) { contextBar.flash('Finish the cell first — Enter commits it, Esc cancels.'); return; }
  const { $from, $to } = view.state.selection;
  const range = $from.blockRange($to);
  if (!range) { contextBar.flash('Select the text to group first.'); return; }
  if (!wrapIn(schema.nodes.container, { type: 'group' })(view.state, view.dispatch)) {
    contextBar.flash('Could not group this selection — pick whole paragraphs or bullets.');
    return;
  }
  // wrapIn left a text selection inside the new container; NodeSelect the container itself
  // so its context tools appear and the naming dialog (next task) can patch its attrs.
  const sel = view.state.selection;
  let depth = sel.$from.depth;
  while (depth > 0 && sel.$from.node(depth).type !== schema.nodes.container) depth--;
  if (depth > 0) {
    const pos = sel.$from.before(depth);
    view.dispatch(view.state.tr.setSelection(NodeSelection.create(view.state.doc, pos)).scrollIntoView());
  }
  view.focus();
  if (window.DocWizBridge && window.DocWizBridge.group) window.DocWizBridge.group();
}

/* The MAIN toolbar (M5): always visible above the surface — block type, marks,
   lists, table, the insert set, alignment. Contextual tools (active table/image/
   container/link) appear in the context bar BELOW it (stakeholder\'s layout rule).
   Buttons run ProseMirror commands on the live selection; active states track it. */
function runCmd(cmd) {
  const view = getActiveView();
  if (!view) return;
  cmd(view.state, view.dispatch, view);
  view.focus();
}

/* The roaming cell editor wins: while a cell is open, toolbar commands a table
   cell can represent (marks, symbols, inline links, undo/redo) act on the CELL;
   block-level tools flash instead of silently hitting the page underneath — the
   page selection is the table itself while its cell is edited, so acting there
   replaced the whole table (the toolbarcell gate guards this). */
function cellView() {
  const cell = window.__dwActiveCell;
  return cell && cell.view ? cell.view : null;
}

// Marks, undo, redo: run on the cell editor when one is open, the page otherwise.
function runEitherCmd(cmd) {
  const mini = cellView();
  if (mini) {
    cmd(mini.state, mini.dispatch, mini);
    mini.focus();
    return;
  }
  runCmd(cmd);
}

// Block-level tools cannot apply inside a cell — say so instead of doing nothing.
function blockedInCell() {
  if (!cellView()) return false;
  contextBar.flash('Finish the cell first — Enter commits it, Esc cancels.');
  return true;
}

/* A block-level command (list / quote / heading / align) has no TEXT target when a whole
   NODE is selected — a linked block, a figure, or the table itself — instead of a text
   range. Running it would wrap/transform that display block AND scrollIntoView, which
   scrolls the deck (the "click anything in the toolbar and the view shifts up" report when a
   linked block was selected). Block it with a hint. An open cell is the editing case
   (commands route into the cell via runEitherCmd), so it is exempt. */
function blockedOnNode() {
  const view = getActiveView();
  if (!view || cellView()) return false;
  if (!(view.state.selection instanceof NodeSelection)) return false;
  contextBar.flash('That formats text — click into some text first (a linked block / figure / table is selected).');
  return true;
}

function markActive(type) {
  const view = cellView() || getActiveView();
  if (!view) return false;
  const { from, $from, to, empty } = view.state.selection;
  if (empty) return !!type.isInSet(view.state.storedMarks || $from.marks());
  return view.state.doc.rangeHasMark(from, to, type);
}

function blockIs(name, attrs) {
  const view = getActiveView();
  if (!view) return false;
  const { $from } = view.state.selection;
  for (let d = $from.depth; d >= 1; d--) {
    const n = $from.node(d);
    if (n.type.name === name && (!attrs || Object.keys(attrs).every((k) => n.attrs[k] === attrs[k]))) return true;
  }
  return false;
}

// Bullet/numbered buttons toggle: wrap when outside a list, lift when inside.
function smartList(listType) {
  return (state, dispatch, view) => {
    if (blockIs(listType.name)) return liftListItem(schema.nodes.list_item)(state, dispatch, view);
    return wrapInList(listType)(state, dispatch, view);
  };
}

// True when the selection's enclosing list item is a task item (checked !== null) — Phase 3.
function listIsTask() {
  const view = getActiveView();
  if (!view) return false;
  const { $from } = view.state.selection;
  for (let d = $from.depth; d >= 1; d--) {
    const n = $from.node(d);
    if (n.type.name === 'list_item') return n.attrs.checked !== null;
  }
  return false;
}

// Set `checked` on every item of the selection's enclosing bullet_list (value=false makes a
// task list, value=null reverts to a plain bullet list).
function setListChecked(value) {
  return (state, dispatch) => {
    const { $from } = state.selection;
    let listPos = null, listNode = null;
    for (let d = $from.depth; d >= 1; d--) {
      if ($from.node(d).type.name === 'bullet_list') { listNode = $from.node(d); listPos = $from.before(d); break; }
    }
    if (listNode == null) return false;
    if (dispatch) {
      let tr = state.tr;
      listNode.forEach((item, off) => {
        tr = tr.setNodeMarkup(listPos + 1 + off, undefined, { ...item.attrs, checked: value });
      });
      dispatch(tr.scrollIntoView());
    }
    return true;
  };
}

// Checklist button: already a task list -> un-list; already a bullet list -> mark items checked;
// otherwise wrap into a bullet list, then mark its items checked (two-step is fine — one undo each).
function smartTaskList(state, dispatch, view) {
  if (listIsTask()) return liftListItem(schema.nodes.list_item)(state, dispatch, view);
  if (blockIs('bullet_list')) return setListChecked(false)(state, dispatch);
  const wrapped = wrapInList(schema.nodes.bullet_list)(state, dispatch, view);
  if (wrapped && dispatch && view) setListChecked(false)(view.state, view.dispatch);
  return wrapped;
}

function newTableNode(rows, cols) {
  rows = (rows > 0 ? rows : 2);
  cols = (cols > 0 ? cols : 2);
  const cell = () => ({ md: '', html: '' });
  const headerCells = [];
  for (let c = 0; c < cols; c++) headerCells.push({ md: 'Column ' + (c + 1), html: 'Column ' + (c + 1) });
  const bodyRows = [];
  for (let r = 0; r < rows - 1; r++) {
    const cells = [];
    for (let c = 0; c < cols; c++) cells.push(cell());
    bodyRows.push({ src: null, cells });
  }
  const aligns = [];
  for (let c = 0; c < cols; c++) aligns.push(null);
  return schema.nodes.table.create({
    html: '', src: '',
    meta: {
      attrLine: null, headerLine: null, delimiterLine: null, rowLines: [],
      aligns: aligns.slice(), widths: null, explicitId: null, number: 0,
      model: {
        header: { src: null, cells: headerCells },
        rows: bodyRows,
        delimiter: { src: null },
        aligns: aligns.slice(),
        widths: null,
        id: null,
        attrSrc: null,
      },
    },
  });
}

/* Grid picker popup for "Insert table" — Word/Docs style 8x10 cell grid.
   Returns an Element that is appended to document.body and removes itself
   on click, Esc, or outside-click. */
function showTableGridPicker(anchorEl, onPick) {
  const ROWS = 8;
  const COLS = 10;

  // Remove any stale picker
  const old = document.getElementById('dw-tbl-picker');
  if (old) old.remove();

  const popup = document.createElement('div');
  popup.id = 'dw-tbl-picker';
  popup.className = 'dw-tbl-picker';
  popup.setAttribute('role', 'dialog');
  popup.setAttribute('aria-label', 'Choose table size');

  const grid = document.createElement('div');
  grid.className = 'dw-tbl-picker-grid';

  const label = document.createElement('div');
  label.className = 'dw-tbl-picker-label';
  label.textContent = '1 x 1';

  let hoverR = 0;
  let hoverC = 0;

  const cells = [];

  function highlight(r, c) {
    hoverR = r; hoverC = c;
    label.textContent = r + ' x ' + c;
    for (let i = 0; i < ROWS; i++) {
      for (let j = 0; j < COLS; j++) {
        cells[i][j].classList.toggle('dw-tbl-picker-cell--on', i < r && j < c);
      }
    }
  }

  function close() {
    popup.remove();
    document.removeEventListener('mousedown', outsideClick, true);
    document.removeEventListener('keydown', escKey, true);
  }

  function outsideClick(e) {
    if (!popup.contains(e.target) && e.target !== anchorEl) close();
  }

  function escKey(e) {
    if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); close(); }
  }

  for (let i = 0; i < ROWS; i++) {
    cells.push([]);
    for (let j = 0; j < COLS; j++) {
      const c = document.createElement('div');
      c.className = 'dw-tbl-picker-cell';
      c.addEventListener('mouseenter', () => highlight(i + 1, j + 1));
      c.addEventListener('mousedown', (e) => {
        e.preventDefault();
        const r = i + 1, col = j + 1;
        close();
        onPick(r, col);
      });
      grid.appendChild(c);
      cells[i].push(c);
    }
  }

  popup.appendChild(grid);
  popup.appendChild(label);
  popup.addEventListener('mouseleave', () => highlight(0, 0));

  // Position anchored below the button
  document.body.appendChild(popup);
  const rect = anchorEl.getBoundingClientRect();
  popup.style.left = Math.round(rect.left) + 'px';
  popup.style.top = Math.round(rect.bottom + 4) + 'px';

  // Clamp so it never falls off the right edge
  const pw = popup.offsetWidth;
  const vw = window.innerWidth;
  if (rect.left + pw > vw - 8) {
    popup.style.left = Math.max(8, vw - pw - 8) + 'px';
  }

  document.addEventListener('mousedown', outsideClick, true);
  document.addEventListener('keydown', escKey, true);
  return popup;
}

let toolbarButtons = [];

/* withViewSwitch=false (the slide canvas) omits the chapter-only Edit/Source/Read
   view-switch — a deck has no single chapter to switch modes on. Default true keeps
   the chapter toolbar identical. opts.linkTool (slide canvas only) adds the "Link…"
   relink button — turn the selected block into a live-linked embed (Task 9). */
function buildMainToolbar(opts) {
  const withViewSwitch = !opts || opts.withViewSwitch !== false;
  const withLinkTool = !!(opts && opts.linkTool);
  toolbarButtons = [];
  // The shell: one 48px bar that contains two tracks.
  // .e2-tb-main  — horizontally scrollable formatting groups + right-pinned view switch
  // .e2-tb-ctx   — contextual format bar (IMAGE / TABLE) — badge + node tools (hidden initially)
  const bar = document.createElement('div');
  bar.className = 'e2-maintoolbar';
  const bridge = () => window.DocWizBridge || {};

  // ── Build the main track ──────────────────────────────────────────────────
  const mainTrack = document.createElement('div');
  mainTrack.className = 'e2-tb-main';

  // Scrollable formatting groups
  const scroll = document.createElement('div');
  scroll.className = 'tb-scroll';
  mainTrack.appendChild(scroll);

  // Helper: add icon-first button into the scroll track.
  const add = (iconName, label, tip, fn, isActive) => {
    const b = document.createElement('button');
    b.type = 'button';
    b.className = 'ib';
    if (iconName) b.appendChild(icon(iconName));
    if (label) b.appendChild(document.createTextNode(label));
    b.setAttribute('data-tip', tip);
    b.onmousedown = (e) => e.preventDefault(); // keep the text selection
    b.onclick = (e) => { e.preventDefault(); fn(); };
    if (isActive) toolbarButtons.push({ el: b, isActive });
    scroll.appendChild(b);
    return b;
  };
  const addSep = () => scroll.appendChild(sep());

  // Block-type DROPDOWN (custom — native <select> is OS-rendered, cannot preview fonts)
  // Options: value → [label shown in trigger, data-style attr for font preview]
  const BLOCK_OPTS = [
    { v: 'p',  label: 'Normal text',           style: 'p'  },
    { v: '1',  label: 'Title (H1)',             style: 'h1' },
    { v: '2',  label: 'Heading 2',              style: 'h2' },
    { v: '3',  label: 'Heading 3',              style: 'h3' },
    { v: '4',  label: 'Heading 4',              style: 'h4' },
  ];

  // Build the widget — returns { wrap, setVal } where setVal(v) syncs trigger + options.
  const blockDropdown = (() => {
    const wrap = document.createElement('div');
    wrap.className = 'dw-sel-wrap e2-blockselect-wrap';

    const trigger = document.createElement('button');
    trigger.type = 'button';
    trigger.className = 'dw-sel-trigger';
    trigger.setAttribute('data-tip', 'What the current block is: normal text or a section heading (headings are numbered automatically).');
    trigger.setAttribute('aria-haspopup', 'listbox');
    trigger.setAttribute('aria-expanded', 'false');
    trigger.onmousedown = (e) => e.preventDefault();

    const trigLabel = document.createElement('span');
    trigger.appendChild(trigLabel);

    // Inline chevron SVG (down arrow) — use setAttribute for class on SVGElement
    const chevSvg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    chevSvg.setAttribute('viewBox', '0 0 10 6');
    chevSvg.setAttribute('width', '10');
    chevSvg.setAttribute('height', '6');
    chevSvg.setAttribute('aria-hidden', 'true');
    chevSvg.setAttribute('class', 'dw-sel-chev');
    const chevPath = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    chevPath.setAttribute('d', 'M1 1l4 4 4-4');
    chevPath.setAttribute('fill', 'none');
    chevPath.setAttribute('stroke', 'currentColor');
    chevPath.setAttribute('stroke-width', '1.7');
    chevPath.setAttribute('stroke-linecap', 'round');
    chevPath.setAttribute('stroke-linejoin', 'round');
    chevSvg.appendChild(chevPath);
    trigger.appendChild(chevSvg);

    wrap.appendChild(trigger);

    const popup = document.createElement('div');
    popup.className = 'dw-sel-popup';
    popup.setAttribute('role', 'listbox');
    popup.setAttribute('aria-label', 'Block type');
    popup.hidden = true;

    let currentVal = 'p';
    let hiIdx = 0;

    const optEls = BLOCK_OPTS.map((o, idx) => {
      const el = document.createElement('div');
      el.className = 'dw-sel-opt';
      el.setAttribute('role', 'option');
      el.setAttribute('data-style', o.style);
      el.setAttribute('data-v', o.v);
      el.setAttribute('aria-selected', 'false');
      el.textContent = o.label;
      el.onmousedown = (e) => e.preventDefault();
      el.onclick = () => {
        select(o.v);
        close();
      };
      el.onmouseenter = () => { setHi(idx); };
      popup.appendChild(el);
      return el;
    });

    function setHi(idx) {
      hiIdx = idx;
      optEls.forEach((el, i) => el.classList.toggle('dw-sel-hi', i === idx));
    }

    function setVal(v) {
      currentVal = v;
      const opt = BLOCK_OPTS.find((o) => o.v === v) || BLOCK_OPTS[0];
      trigLabel.textContent = opt.label;
      optEls.forEach((el) => {
        const sel = el.getAttribute('data-v') === v;
        el.setAttribute('aria-selected', sel ? 'true' : 'false');
      });
    }

    function select(v) {
      if (blockedInCell() || blockedOnNode()) { updateToolbarState(); return; }
      setVal(v);
      runCmd(v === 'p' ? setBlockType(schema.nodes.paragraph) : setBlockType(schema.nodes.heading, { level: Number(v) }));
    }

    function open() {
      // Position below trigger
      document.body.appendChild(popup);
      popup.hidden = false;
      trigger.setAttribute('aria-expanded', 'true');
      const rect = trigger.getBoundingClientRect();
      popup.style.left = Math.round(rect.left) + 'px';
      popup.style.top = Math.round(rect.bottom + 2) + 'px';
      popup.style.minWidth = Math.round(rect.width) + 'px';
      // Clamp right edge
      const pw = popup.offsetWidth;
      const vw = window.innerWidth;
      if (rect.left + pw > vw - 8) popup.style.left = Math.max(8, vw - pw - 8) + 'px';
      // Set hi to current
      const ci = BLOCK_OPTS.findIndex((o) => o.v === currentVal);
      setHi(ci >= 0 ? ci : 0);
      document.addEventListener('mousedown', outsideClick, true);
      document.addEventListener('keydown', popupKey, true);
    }

    function close() {
      popup.hidden = true;
      trigger.setAttribute('aria-expanded', 'false');
      document.removeEventListener('mousedown', outsideClick, true);
      document.removeEventListener('keydown', popupKey, true);
      if (document.body.contains(popup)) document.body.removeChild(popup);
      trigger.focus();
    }

    function outsideClick(e) {
      if (!popup.contains(e.target) && e.target !== trigger) close();
    }

    function popupKey(e) {
      if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); close(); return; }
      if (e.key === 'ArrowDown') {
        e.preventDefault();
        setHi((hiIdx + 1) % optEls.length);
        return;
      }
      if (e.key === 'ArrowUp') {
        e.preventDefault();
        setHi((hiIdx - 1 + optEls.length) % optEls.length);
        return;
      }
      if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        const opt = BLOCK_OPTS[hiIdx];
        if (opt) { select(opt.v); close(); }
        return;
      }
    }

    trigger.onclick = (e) => {
      e.preventDefault();
      if (!popup.hidden) { close(); } else { open(); }
    };

    // Keyboard: open on Down/Enter when trigger is focused and popup is closed
    trigger.addEventListener('keydown', (e) => {
      if ((e.key === 'ArrowDown' || e.key === 'Enter' || e.key === ' ') && popup.hidden) {
        e.preventDefault();
        open();
      }
    });

    // Initialise display
    setVal('p');

    return { wrap, setVal };
  })();

  toolbarButtons.push({
    el: blockDropdown.wrap,
    isActive: () => {
      const view = getActiveView();
      if (!view) { blockDropdown.setVal('p'); return false; }
      const { $from } = view.state.selection;
      for (let d = $from.depth; d >= 1; d--) {
        const n = $from.node(d);
        if (n.type.name === 'heading') { blockDropdown.setVal(String(n.attrs.level)); return false; }
      }
      blockDropdown.setVal('p');
      return false;
    },
  });

  // Convention (Word Quick Access Toolbar, Google Docs, CKEditor): undo/redo at far LEFT.
  add('undo', null, 'Undo (Ctrl+Z).', () => runEitherCmd(undo));
  add('redo', null, 'Redo (Ctrl+Y).', () => runEitherCmd(redo));
  addSep();
  scroll.appendChild(blockDropdown.wrap);
  addSep();
  add('bold', null, 'Bold (Ctrl+B).', () => runEitherCmd(toggleMark(schema.marks.strong)), () => markActive(schema.marks.strong));
  add('italic', null, 'Italic (Ctrl+I).', () => runEitherCmd(toggleMark(schema.marks.em)), () => markActive(schema.marks.em));
  add('strike', null, 'Strikethrough.', () => runEitherCmd(toggleMark(schema.marks.strike)), () => markActive(schema.marks.strike));
  add('code', null, 'Code — for IDs, paths, values (renders in mono).', () => runEitherCmd(toggleMark(schema.marks.code)), () => markActive(schema.marks.code));
  add('clearFormat', null, 'Remove bold/italic/code/links from the selected text.', () => {
    const view = cellView() || getActiveView();
    if (!view) return;
    const { from, to, empty } = view.state.selection;
    if (empty) return;
    let tr = view.state.tr;
    for (const m of Object.values(schema.marks)) tr = tr.removeMark(from, to, m);
    view.dispatch(tr);
    view.focus();
  });
  addSep();
  add('bulletList', null, 'Bulleted list (click again to un-list).', () => {
    if (!blockedInCell() && !blockedOnNode())
      runCmd(blockIs('bullet_list') && listIsTask() ? setListChecked(null) : smartList(schema.nodes.bullet_list));
  }, () => blockIs('bullet_list') && !listIsTask());
  add('orderedList', null, 'Numbered list (click again to un-list).', () => {
    if (!blockedInCell() && !blockedOnNode()) runCmd(smartList(schema.nodes.ordered_list));
  }, () => blockIs('ordered_list'));
  add('taskList', null, 'Checklist (task list) — click again to un-list. Click a box to mark a step done.', () => {
    if (!blockedInCell() && !blockedOnNode()) runCmd(smartTaskList);
  }, () => listIsTask());
  add('indent', null, 'Indent — nest this list item under the one above.', () => {
    if (blockedInCell() || blockedOnNode()) return;
    const v = getActiveView();
    if (v && sinkListItem(schema.nodes.list_item)(v.state)) runCmd(sinkListItem(schema.nodes.list_item));
    else contextBar.flash('Indent works inside a list — click into a list item first.');
  });
  add('outdent', null, 'Outdent — un-nest this list item.', () => {
    if (blockedInCell() || blockedOnNode()) return;
    const v = getActiveView();
    if (v && liftListItem(schema.nodes.list_item)(v.state)) runCmd(liftListItem(schema.nodes.list_item));
    else contextBar.flash('Outdent works inside a list — click into a list item first.');
  });
  add('quote', null, 'Quote block (click again to un-quote).', () => {
    if (!blockedInCell() && !blockedOnNode()) runCmd(blockIs('blockquote') ? lift : wrapIn(schema.nodes.blockquote));
  }, () => blockIs('blockquote'));
  add('alignLeft', null, 'Align left — the default; removes centering.', () => {
    if (!blockedInCell() && !blockedOnNode()) setAlign(null);
  }, () => !alignWrapperAt());
  add('alignCenter', null, 'Center the selected block (click again to remove).', () => {
    if (!blockedInCell() && !blockedOnNode()) setAlign('center');
  }, () => { const w = alignWrapperAt(); return !!w && w.node.attrs.type === 'center'; });
  add('alignRight', null, 'Right-align the selected block (click again to remove).', () => {
    if (!blockedInCell() && !blockedOnNode()) setAlign('right');
  }, () => { const w = alignWrapperAt(); return !!w && w.node.attrs.type === 'right'; });
  addSep();
  add('weblink', null, 'Insert a link to a web address (for chapters use the chapter-link tool).', () => bridge().weblink && bridge().weblink());
  add('chapterLink', null, 'Insert a link to a chapter or one of its sections (stable IDs).', () => bridge().dwlink && bridge().dwlink());
  add('doc', null, 'Insert a link to a PDF/document from the project\'s attachments.', () => bridge().attachment && bridge().attachment());
  addSep();
  add('image', null, 'Insert a picture from the project\'s assets (or upload one).', () => bridge().image && bridge().image());
  const tableBtn = add('table', null, 'Insert a table — choose size in the grid that opens.', () => {
    if (blockedInCell()) return;
    showTableGridPicker(tableBtn, (r, c) => {
      insertBlockAfterSelection(newTableNode(r, c));
    });
  });
  add('blocks', null, 'Insert a designed building block — callouts, steps, cards, buttons, collapsible.', () => {
    if (!blockedInCell() && bridge().element) bridge().element();
  });
  add('group', null, 'Group the selected blocks so you can embed them together on a slide (you give the group a name + optional note).', () => {
    if (!blockedInCell()) createGroup();
  });
  addSep();
  add(null, 'Ω', 'Insert a special character — °, ±, µ, Ø, arrows …', () => showSymbolPalette());
  add('hrule', null, 'Insert a divider line.', () => {
    if (!blockedInCell()) insertBlockAfterSelection(schema.nodes.horizontal_rule.create());
  });
  addSep();
  add('audience', null, 'Insert content only selected audiences will see.', () => {
    if (!blockedInCell() && bridge().audience) bridge().audience();
  });
  // Slide canvas only: turn the selected block into a live-linked chapter section.
  if (withLinkTool) {
    add('chapterLink', 'Link…', 'Replace the selected block with a live link to a chapter section (it stays in sync).', () => relinkSelection());
  }
  addSep();
  add('anchor', null, 'Copy the selected section\'s link (click into a heading first).', () => {
    // Chapter-only: a slide zone has no chapter anchor to copy.
    if (!current || !current.view || getActiveView() !== current.view) {
      contextBar.flash('Section links live in chapters — open a chapter to copy a heading link.');
      return;
    }
    const { $from } = current.view.state.selection;
    let heading = null;
    for (let d = $from.depth; d >= 1; d--)
      if ($from.node(d).type.name === 'heading') heading = $from.node(d);
    if (!heading) { contextBar.flash('Section links live on headings — click into a heading first.'); return; }
    if (heading.attrs.level === 1) { contextBar.flash('The chapter title needs no anchor — link the chapter itself as dw:' + current.dto.id + '.'); return; }
    if (!heading.attrs.anchor) { contextBar.flash('This heading gets its permanent anchor when you Save — save, then copy the link here.'); return; }
    const ref = 'dw:' + current.dto.id + '#' + heading.attrs.anchor;
    if (navigator.clipboard) navigator.clipboard.writeText(ref);
    contextBar.flash('Copied ' + ref + ' — paste it into any text or chapter-link dialog.');
  });
  const anchorInfo = document.createElement('span');
  anchorInfo.className = 'e2-ctx-hint';
  anchorInfo.id = 'e2AnchorInfo';
  scroll.appendChild(anchorInfo);

  // ── View switch (right-pinned) ─────────────────────────────────────────────
  // Three flat buttons: Edit (pencil, active by default) / Source (code) / Read (eye).
  // Separated from the scroll track by a .e2-sep. IDs kept for backward compat.
  // Chapter-only (withViewSwitch): the slide canvas has no single chapter to switch.
  if (withViewSwitch) {
    const vsep = document.createElement('span');
    vsep.className = 'e2-sep';
    mainTrack.appendChild(vsep);

    const vs = document.createElement('div');
    vs.className = 'e2-view-switch';

    const vsBtn = (id, iconName, label, tip) => {
      const b = document.createElement('button');
      b.type = 'button';
      b.id = id;
      b.className = 'ib';
      b.appendChild(icon(iconName));
      b.appendChild(document.createTextNode(label));
      b.setAttribute('data-tip', tip);
      b.onmousedown = (e) => e.preventDefault();
      vs.appendChild(b);
      return b;
    };

    const editBtn = vsBtn('e2EditBtn', 'pencil', 'Edit',
      'Edit this chapter in the live surface (what you see is what you ship).');
    editBtn.classList.add('active');

    const srcBtn = vsBtn('e2SourceBtn', 'code', 'Source',
      'Shows the exact markdown this chapter will save as. For fixing import artifacts the visual surface cannot express. Switching back to Edit saves what you changed here.');

    const readBtn = vsBtn('e2ReadingBtn', 'eye', 'Read',
      'Opens this chapter exactly as the built handover will render it (saves your changes first).');

    mainTrack.appendChild(vs);

    // Wire view-switch handlers after bar is in the DOM tree (open() calls us).
    const markMode = (mode) => {
      editBtn.classList.toggle('active', mode === 'edit');
      srcBtn.classList.toggle('active', mode === 'source');
      readBtn.classList.remove('active');
    };
    // Leaving Source keeps what was typed there: changed text goes through the normal save
    // (the server writes it and the surface rebuilds from it — see rebuild()); unchanged text
    // just closes. A failed save keeps Source open; the status line says why.
    const leaveSource = () => {
      const area = current && current.sourceArea;
      if (!area) { markMode('edit'); return; }
      if (area.value === area.dataset.initial) { toggleSource(false); markMode('edit'); return; }
      const save = window.DocWizBridge && window.DocWizBridge.saveChapter;
      if (!save) return;
      save().then(() => markMode('edit')).catch(() => {});
    };
    srcBtn.onclick = () => {
      if (!current) return;
      if (current.sourceArea) leaveSource();
      else { toggleSource(true); markMode('source'); }
    };
    readBtn.onclick = () => {
      const url = '/preview-root/chapter?file=' + encodeURIComponent(current.file);
      const save = window.DocWizBridge && window.DocWizBridge.saveChapter;
      (save ? save() : Promise.resolve()).then(() => {
        if (current && !current.sourceArea) markMode('edit'); // the save closed Source
        window.open(url, '_blank');
      });
    };
    editBtn.onclick = leaveSource;
  }

  bar.appendChild(mainTrack);

  // ── Contextual format bar (IMAGE / TABLE) — hidden until set() activates it ─
  const ctxTrack = document.createElement('div');
  ctxTrack.className = 'e2-tb-ctx';
  ctxTrack.hidden = true;
  bar.appendChild(ctxTrack);

  return bar;
}

/* Special characters for technical documentation — inserted as plain text. */
const SYMBOLS = ['°', '±', '×', 'Ø', 'µ', 'Δ', 'Ω', '≤', '≥', '≠', '≈', '‰', '→', '←', '↔', '½', '¼', '³', '²', '№', '§', '·'];

function showSymbolPalette() {
  const tools = document.createElement('div');
  tools.className = 'e2-tb-tools';
  for (const ch of SYMBOLS) {
    const b = document.createElement('button');
    b.type = 'button';
    b.className = 'ib';
    b.textContent = ch;
    b.setAttribute('data-tip', 'Insert ' + ch);
    b.onmousedown = (e) => e.preventDefault();
    b.onclick = (e) => {
      e.preventDefault();
      // Into the open cell editor when there is one; on the page, never over a
      // selected table/image — insertText would REPLACE the whole element.
      const active = getActiveView();
      const view = cellView() || active;
      if (!view) return;
      if (view === active && view.state.selection instanceof NodeSelection) {
        contextBar.flash('Click into text first — inserting here would replace the selected element.');
        return;
      }
      view.dispatch(view.state.tr.insertText(ch).scrollIntoView());
      view.focus();
    };
    tools.appendChild(b);
  }
  const done = document.createElement('button');
  done.type = 'button';
  done.className = 'ib';
  done.textContent = 'Close';
  done.onclick = () => contextBar.set(null);
  tools.appendChild(done);
  // Esc closes the palette and returns focus to the editor (same as Done).
  tools.addEventListener('keydown', (e) => {
    if (e.key !== 'Escape') return;
    e.preventDefault();
    e.stopPropagation();
    contextBar.set(null);
    const v = getActiveView();
    if (v) v.focus();
  });
  contextBar.set(tools, 'Characters');
}

function updateToolbarState() {
  if (!getActiveView()) return;
  for (const { el, isActive } of toolbarButtons) {
    try { el.classList.toggle('active', !!isActive()); } catch { /* selection edge */ }
  }
}

/* Word-style alignment: a PROPERTY of the selected block, not an insert. The
   markdown has exactly two alignment wrappers (::: center / ::: right); left is
   the default = no wrapper. So the buttons toggle/switch the wrapper in place —
   never nest a second one, never throw (stakeholder feedback 2026-06-12). */
function alignWrapperAt() {
  const view = getActiveView();
  if (!view) return null;
  const { $from } = view.state.selection;
  for (let d = $from.depth; d >= 1; d--) {
    const n = $from.node(d);
    if (n.type.name === 'container' && (n.attrs.type === 'center' || n.attrs.type === 'right'))
      return { node: n, pos: $from.before(d) };
  }
  return null;
}

function setAlign(kind) { // 'center' | 'right' | null (= left / default)
  const view = getActiveView();
  if (!view) return;
  const cur = alignWrapperAt();
  if (cur) {
    if (kind === null || cur.node.attrs.type === kind) {
      // back to default: drop the wrapper, keep its content in place
      view.dispatch(view.state.tr.replaceWith(cur.pos, cur.pos + cur.node.nodeSize, cur.node.content).scrollIntoView());
    } else {
      // center <-> right: retype the existing wrapper, do not nest
      view.dispatch(view.state.tr.setNodeMarkup(cur.pos, null, { ...cur.node.attrs, type: kind }).scrollIntoView());
    }
    view.focus();
    return;
  }
  if (kind === null) { view.focus(); return; } // already left-aligned
  const { $from, $to } = view.state.selection;
  const range = $from.blockRange($to);
  const wrapping = range && findWrapping(range, schema.nodes.container, { type: kind, args: '', label: null });
  if (!wrapping) {
    contextBar.flash('This cannot be aligned here — click into a text block first.');
    return;
  }
  view.dispatch(view.state.tr.wrap(range, wrapping).scrollIntoView());
  view.focus();
}

/* Cursor-in-heading: show the section\'s stable anchor with one-click copy. Chapter-
   only — a slide zone has no chapter anchor, so when a zone is the active view we just
   clear the field (the toolbar's anchor-copy button flashes a hint instead). */
function updateAnchorInfo() {
  const el = document.getElementById('e2AnchorInfo');
  if (!el) return;
  if (!current || !current.view || getActiveView() !== current.view) { el.textContent = ''; return; }
  const { $from } = current.view.state.selection;
  let heading = null;
  for (let d = $from.depth; d >= 1; d--)
    if ($from.node(d).type.name === 'heading') heading = $from.node(d);
  if (heading && heading.attrs.anchor) {
    const ref = 'dw:' + current.dto.id + '#' + heading.attrs.anchor;
    el.textContent = '';
    const code = document.createElement('code');
    code.textContent = '#' + heading.attrs.anchor;
    el.appendChild(code);
    const copy = document.createElement('button');
    copy.type = 'button';
    copy.textContent = '⧉ copy link';
    copy.setAttribute('data-tip', 'Copy ' + ref + ' — paste it anywhere a chapter link is asked for.');
    copy.onclick = () => navigator.clipboard && navigator.clipboard.writeText(ref);
    el.appendChild(copy);
  } else {
    el.textContent = '';
  }
}

/* Click tools for links on the surface (M4): dw: links get Open/Change/Remove;
   attachment/external links open in a new tab instead of navigating the editor. */
function linkClickTools(anchorEl, event) {
  const href = anchorEl.getAttribute('href') || '';
  const view = getActiveView();
  if (!view) return;
  const pos = view.posAtDOM(anchorEl, 0);
  const markType = schema.marks.link;

  const rangeOfLink = () => {
    const $pos = view.state.doc.resolve(pos);
    const parent = $pos.parent;
    let start = null;
    let end = null;
    let offset = $pos.start();
    parent.forEach((child) => {
      const has = child.marks.some((m) => m.type === markType && m.attrs.href === href);
      if (has && start === null) start = offset;
      if (has) end = offset + child.nodeSize;
      offset += child.nodeSize;
    });
    return start === null ? null : { start, end };
  };

  const tools = document.createElement('div');
  tools.className = 'e2-tb-tools';
  const add = (label, tip, fn) => {
    const b = document.createElement('button');
    b.type = 'button';
    b.className = 'ib';
    b.textContent = label;
    b.setAttribute('data-tip', tip);
    b.onmousedown = (e) => e.preventDefault();
    b.onclick = (e) => { e.preventDefault(); fn(); };
    tools.appendChild(b);
  };

  if (href.startsWith('dw:')) {
    const id = href.slice(3).split('#')[0];
    add('Open chapter →', 'Open the linked chapter in the editor.', () => {
      const bridge = window.DocWizBridge;
      if (bridge && bridge.openChapterById) bridge.openChapterById(id);
    });
  } else {
    add('Open ↗', 'Open the linked file in a new tab.', () => window.open(href, '_blank'));
  }
  add('Change…', 'Change where this link points, or its text.', () => {
    const range = rangeOfLink();
    if (!range) return;
    const text = view.state.doc.textBetween(range.start, range.end);
    const bridge = window.DocWizBridge;
    if (!bridge || !bridge.editLink) return;
    bridge.editLink(href, text, (res) => {
      const r = rangeOfLink(); // re-resolve — the doc may have moved meanwhile
      if (!r) return;
      view.dispatch(view.state.tr.replaceWith(r.start, r.end,
        schema.text(res.text, [schema.marks.link.create({ href: res.href })])));
      contextBar.set(null);
      view.focus();
    });
  });
  add('Remove link', 'Keep the text, remove the link from it.', () => {
    const range = rangeOfLink();
    if (!range) return;
    view.dispatch(view.state.tr.removeMark(range.start, range.end, markType));
    contextBar.set(null);
    view.focus();
  });
  const hint = document.createElement('span');
  hint.className = 'e2-ctx-hint';
  hint.textContent = href.startsWith('dw:') ? href + ' · edit the text in place' : href;
  tools.appendChild(hint);

  event.preventDefault();
  contextBar.set(tools, 'Link');
}

/* Flush any open cell editor back into the table model BEFORE serialising.
   There is at most one active cell editor at a time (window.__dwActiveCell is a
   single global). commitCellEditor() is a safe no-op when nothing is dirty. */
function flushActiveCell() {
  if (window.__dwActiveCell && window.__dwActiveCell.table)
    window.__dwActiveCell.table.commitCellEditor();
}

async function save(frontmatter) {
  if (!current) throw new Error('no chapter open in the live editor');
  flushActiveCell();
  const ops = current.sourceArea
    ? [{ text: current.sourceArea.value }]
    : buildSavePayload();
  const res = await fetch('/api/editor-doc?file=' + encodeURIComponent(current.file), {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      baseBodyHash: current.dto.bodyHash,
      title: frontmatter.title,
      discipline: frontmatter.discipline,
      audiences: frontmatter.audiences,
      segments: ops,
    }),
  });
  if (!res.ok) throw new Error((await res.text()) || res.statusText);
  const dto = await res.json();
  rebuild(dto);
  return dto;
}

/* After every save the surface rebuilds from pipeline truth (fresh anchors, figure
   numbers, hash). Best-effort restore of scroll + selection position. */
function rebuild(dto) {
  const mount = document.getElementById('editor2');
  const scrollTop = mount.scrollTop;
  const selPos = current.view ? current.view.state.selection.from : 0;
  const wasSource = !!current.sourceArea;

  current.dto = dto;
  const doc = buildDoc(schema, dto);
  rememberOriginals(doc);
  current.view.updateState(EditorState.create({ schema, doc, plugins: plugins() }));
  if (wasSource) toggleSource(false);

  const max = current.view.state.doc.content.size;
  const sel = TextSelection.near(current.view.state.doc.resolve(Math.min(selPos, max)));
  current.view.dispatch(current.view.state.tr.setSelection(sel));
  mount.scrollTop = scrollTop;
}

/* ---------- Source (advanced): the exact text Save would write ---------- */

function toggleSource(show) {
  const mount = document.getElementById('editor2');
  const article = mount.querySelector('article');
  const open = show !== undefined ? show : !current.sourceArea;

  if (!open) {
    if (current.sourceArea) {
      current.sourceArea.remove();
      current.sourceArea = null;
    }
    article.hidden = false;
    return;
  }
  flushActiveCell();
  const area = document.createElement('textarea');
  area.className = 'e2-source';
  area.spellcheck = false;
  area.value = resolveOps(buildSavePayload());
  area.dataset.initial = area.value; // leaving Source applies the text only if it changed
  area.addEventListener('input', () => {
    if (current.opts.onDirty) current.opts.onDirty();
  });
  article.hidden = true;
  mount.appendChild(area);
  current.sourceArea = area;
}

function close() {
  if (current && current.view) {
    if (activeView === current.view) activeView = null;
    current.view.destroy();
  }
  current = null;
}

/* Pasted/dropped images route through the project-assets dialog (app.js) and land as a
   live figure — same rule as v1: cancel = no insert. routePastedImage catches a real image
   file or a data: URI <img> in the pasted HTML; a Word "Copy" that exposes only an OS bitmap
   (which no clipboard API can read) is caught later — dataImageGuard swaps the inlined data:
   URI for an asset in the editor, and the server-side DataImageRescue is the save-time
   backstop — so an image is never persisted as a base64 data: URI. */
function pasteImageFiles(data) {
  return routePastedImage(data, (file) => {
    const bridge = window.DocWizBridge;
    if (bridge && bridge.pasteImage)
      bridge.pasteImage(file, (res) => insert({ type: 'image', src: res.src, alt: res.alt }));
  });
}

/* Esc steps out reliably, even when DOM focus has drifted off the editor view
   (a caption-field blur, a closed dialog): the in-view keymap Esc only fires when
   the view is focused, so this document-level fallback deselects a selected
   table/figure/container otherwise. Registered ONCE (not per open) so handlers
   never stack. Gated so the finer Esc handlers win first: an open cell editor
   closes via its own keymap; a table in Focus mode exits focus first; only then
   does this step the page selection out. */
document.addEventListener('keydown', (event) => {
  if (event.key !== 'Escape') return;
  // Acts on whichever view the toolbar is following — the chapter editor OR a focused
  // slide zone (BUG 6: zones never set `current`, so the old `current.view`-only gate
  // never stepped a selected zone table out when DOM focus had drifted).
  const view = getActiveView();
  if (!view) return;
  if (window.__dwActiveCell) return; // the cell editor\'s own Esc closes it
  if (document.querySelector('.dw-table-figure.e2-focus')) return; // Focus mode\'s Esc exits it
  if (!(view.state.selection instanceof NodeSelection)) return;
  event.preventDefault();
  stepOutOfNodeSelection(view);
}, true);

/* Undo/redo follow the ACTIVE view, not raw DOM focus (L204). The toolbar Undo/Redo
   buttons run undo/redo on getActiveView() — the chapter editor, the focused slide zone,
   or an open table cell (runEitherCmd) — so they keep working after focus drifts onto a
   slide card, the filmstrip/panel, or a toolbar button. The per-view keymap's Mod-z only
   fires while that view holds DOM focus, so on the slide canvas Ctrl+Z silently did
   nothing the moment focus left the zone — "buttons work, shortcuts don't". This document
   handler fills exactly that gap, mirroring the Esc handler above: when focus has drifted
   off every editable, drive undo/redo through the same getActiveView()/runEitherCmd path
   the buttons use. When an editable IS focused we do nothing and let it own undo — a
   ProseMirror view (zone/chapter/cell) via its own Mod-z keymap, a plain contenteditable
   (slide title/subtitle) or a form field (YAML editor, search, dialog input) via native
   browser undo — so there is no double-undo (undo is not idempotent). */
document.addEventListener('keydown', (event) => {
  if (event.altKey || !(event.ctrlKey || event.metaKey)) return;
  const key = (event.key || '').toLowerCase();
  let cmd = null;
  if (key === 'z') cmd = event.shiftKey ? redo : undo;
  else if (key === 'y' && !event.shiftKey) cmd = redo;
  if (!cmd || !getActiveView()) return;
  // A focused editable owns its own undo (see comment above) — only step in once focus
  // has drifted off every editable, which is the case the per-view keymap cannot reach.
  const t = event.target;
  if (t && t.closest && (
      /^(input|textarea|select)$/i.test(t.tagName || '') ||
      t.closest('[contenteditable=""], [contenteditable="true"]'))) return;
  event.preventDefault();
  runEitherCmd(cmd);
}, true);

/* Refresh the shared toolbar against the active view. slideedit.js calls this when a
   zone gains focus or changes selection so the one toolbar tracks the focused zone. */
function notifyToolbar() {
  updateToolbarState();
  updateAnchorInfo();
}

window.DocWizEditor2 = {
  open,
  close,
  save,
  insert,
  // Image width presets (constants.js) — app.js dialogs build their size options
  // from here so the numbers exist in exactly one place.
  presets: { figure: FIGURE_WIDTHS, cellImage: CELL_IMAGE_WIDTHS },
  // Slide canvas hooks (Task 7b): slideedit.js mounts N per-zone EditorViews in this
  // same document and lets the shared toolbar follow whichever zone has focus.
  setActiveView,
  getActiveView,
  notifyToolbar,
  contextBar,
  plugins,
  buildMainToolbar,
  schema,
  // Run a ProseMirror command on the active view (the slash menu — slashmenu.js —
  // runs block-type/list/quote commands through here so they behave exactly like the
  // toolbar buttons, which also act on getActiveView()).
  runActiveCmd: runCmd,
  // Turn the selected block into a live-linked embed (Task 9 relink). Used by the slide
  // toolbar's Link… button and the container context-bar Link… tool (views.js).
  relinkSelection,
  // Wrap the selected blocks in a group container, NodeSelect it, and open the naming
  // dialog (Task 10). The right-click menu can also call DocWizEditor2.createGroup().
  createGroup,
  // Apply name/description changes to the currently NodeSelected group container
  // (Task 11). Called by the groupDialog in app.js after the user confirms.
  patchActiveGroup: (changes) => {
    if (!current || !current.view) return;
    const sel = current.view.state.selection;
    const node = sel.node;
    if (!node || node.type.name !== 'container' || node.attrs.type !== 'group') return;
    current.view.dispatch(current.view.state.tr.setNodeMarkup(sel.from, null, { ...node.attrs, ...changes }));
  },
  // Debug/scenario access to the live EditorView (used by the fidelity gate page
  // and handy on the support path); not part of the app.js contract.
  _view: () => (current ? current.view : null),
  // Scenario/debug: the active chapter's EditorView (same as _view for chapter edits).
  activeView: () => (current ? current.view : null),
  // Scenario/debug: flush any open cell editor then return the text save() would write.
  // Calling this exercises the same flush path as save() without doing a network round-trip.
  buildSaveText: () => { flushActiveCell(); return resolveOps(buildSavePayload()); },
  // Scenario/debug: open or close the Source (advanced) textarea. Mirrors the srcBtn click.
  toggleSource: (show) => toggleSource(show),
};
