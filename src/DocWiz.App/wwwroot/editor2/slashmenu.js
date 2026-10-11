/* Slash (/) insert menu for the slide-zone surface (Slides v2, ADR-007, Task 8).

   Typing '/' at the start of an empty textblock (or right after whitespace) opens a
   filtered popup of insert actions; typing filters the list, ArrowUp/Down move the
   highlight, Enter/click selects, Esc closes. Selecting deletes the typed '/query'
   range and performs the insert on the ACTIVE view (DocWizEditor2.getActiveView()).

   SCOPE: this plugin is added ONLY to the per-zone EditorViews (slideedit.js
   plugins().concat([slashMenuPlugin(...)])), never to the chapter editor's plugins() —
   so the chapter surface is unaffected (the fixed Insert bar stays its insertion UX).

   The menu items reuse the EXISTING editor machinery — no markdown is parsed in the
   browser (ADR-004 holds):
   - Heading 2/3, Bulleted/Numbered list, Quote → ProseMirror commands run through
     DocWizEditor2.runActiveCmd (the same path the toolbar buttons use).
   - Image → DocWizBridge.image() (the project-assets dialog → DocWizEditor2.insert).
   - Linked element → DocWizBridge.pickSection() (the dw: section picker) → build the
     '::: embed dw:ref :::' directive → POST /api/editor-fragment to resolve it →
     DocWizEditor2.insert({ type:'embed', html, ref }) (the dedicated embed node, Task 9:
     rendered transcluded in place under a LINKED pill, round-trips to the directive). */
import { Plugin, PluginKey } from 'prosemirror-state';
import { setBlockType, wrapIn } from 'prosemirror-commands';
import { wrapInList } from 'prosemirror-schema-list';
import { icon } from './icons.js';
import { INSERTS } from './inserts.js';

export const slashMenuKey = new PluginKey('dwSlashMenu');

function ed() { return window.DocWizEditor2; }
function bridge() { return window.DocWizBridge || {}; }

/* The menu items. `match` is the lowercase haystack used for filtering; `run` performs
   the insert (the plugin has already removed the '/query' text before calling it).
   `opts` carries per-zone context (audience) for the embed resolve. */
function items(opts) {
  const E = ed();
  const schema = E.schema;
  return [
    {
      key: 'h2', title: 'Heading 2', desc: 'Section heading', icon: 'H2',
      match: 'heading 2 h2 title subtitle',
      run: () => E.runActiveCmd(setBlockType(schema.nodes.heading, { level: 2 })),
    },
    {
      key: 'h3', title: 'Heading 3', desc: 'Sub-section heading', icon: 'H3',
      match: 'heading 3 h3 title subtitle',
      run: () => E.runActiveCmd(setBlockType(schema.nodes.heading, { level: 3 })),
    },
    {
      key: 'ul', title: 'Bulleted list', desc: 'A list of points', svg: 'bulletList',
      match: 'bulleted list bullets unordered points',
      run: () => E.runActiveCmd(wrapInList(schema.nodes.bullet_list)),
    },
    {
      key: 'ol', title: 'Numbered list', desc: 'A numbered list', svg: 'orderedList',
      match: 'numbered list ordered steps',
      run: () => E.runActiveCmd(wrapInList(schema.nodes.ordered_list)),
    },
    {
      key: 'quote', title: 'Quote', desc: 'A quote block', svg: 'quote',
      match: 'quote blockquote citation',
      run: () => E.runActiveCmd(wrapIn(schema.nodes.blockquote)),
    },
    // The inserts' words and icons come from the shared vocabulary (inserts.js, ADR-008).
    {
      key: 'image', title: INSERTS.picture.title, desc: INSERTS.picture.desc, svg: INSERTS.picture.icon,
      match: 'image picture photo figure asset screenshot',
      run: () => { if (bridge().image) bridge().image(); },
    },
    {
      key: 'embed', title: INSERTS.linked.title, desc: INSERTS.linked.desc, svg: INSERTS.linked.icon,
      match: 'linked element embed transclude section chapter reference link',
      run: () => insertLinkedElement(opts),
    },
  ];
}

/* Linked element: reuse the dw: section picker, resolve the directive through the one
   pipeline, and insert the resolved raw atom. All async — happens AFTER the '/query'
   text was already removed by the caller. The ONE way to insert a linked section: the
   toolbar's Insert ▾ › Linked section… calls it too (main.js), so both land it the same
   way — on an empty line in place of it, otherwise after the block, never over text. */
export function insertLinkedElement(opts) {
  const b = bridge();
  if (!b.pickSection) return;
  b.pickSection((ref) => {
    if (!ref) return;
    const directive = b.embedDirective(ref); // the one classic-script embed-directive producer (app.js)
    fetch('/api/editor-fragment', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ markdown: directive, audience: (opts && opts.audience) || null }),
    })
      .then((r) => { if (!r.ok) return r.text().then((t) => { throw new Error(t || r.statusText); }); return r.json(); })
      .then((dto) => {
        const seg = (dto.segments || []).find((s) => s.kind === 'block') || (dto.segments || [])[0];
        if (!seg) throw new Error('the linked section produced no content');
        // The fragment segment carries the bare "chapter#anchor" ref (computed
        // server-side via DocWizEmbed) — fall back to the picked ref so the client
        // never has to parse the directive.
        ed().insert({ type: 'embed', html: seg.html, ref: seg.ref || String(ref).replace(/^dw:/, '') });
      })
      .catch((e) => {
        if (b.toast) b.toast('Could not insert the linked element: ' + e.message, 'err');
        else window.alert('Could not insert the linked element: ' + e.message);
      });
  });
}

/* Should a just-typed '/' open the menu? Reads a transaction's RESULTING doc +
   collapsed selection: the '/' sits at `cursor-1`. It must be inside a plain textblock
   (paragraph/heading, not code) and be the first char of the block OR directly preceded
   by whitespace — the Notion rule. Returns the anchor position of the '/' (cursor-1)
   when it qualifies, else -1. Doc-state driven (not input interception) so it behaves
   identically for real typing and scripted edits, and never double-inserts the slash. */
function slashAnchorFromTr(tr) {
  const sel = tr.selection;
  if (!sel.empty) return -1;
  const at = sel.from;
  const $pos = tr.doc.resolve(at);
  const parent = $pos.parent;
  if (!parent.isTextblock || parent.type.spec.code) return -1;
  const offset = $pos.parentOffset;
  if (offset < 1) return -1;
  const slashCh = parent.textBetween(offset - 1, offset, undefined, '￼');
  if (slashCh !== '/') return -1;
  if (offset === 1) return at - 1; // '/' is the first char of the block
  const before = parent.textBetween(offset - 2, offset - 1, undefined, '￼');
  return /\s/.test(before) ? at - 1 : -1;
}

/* The popup element. Mirrors showTableGridPicker/the block dropdown in main.js:
   fixed position near the caret, clamped to the viewport, tokens-only classes in
   app.css (.dw-slash-*). The plugin owns its lifecycle (open/filter/move/close). */
class SlashPopup {
  constructor(view, opts) {
    this.view = view;
    this.opts = opts;
    this.items = items(opts);
    this.filtered = this.items.slice();
    this.hi = 0;
    this.el = document.createElement('div');
    this.el.className = 'dw-slash-menu';
    this.el.setAttribute('role', 'listbox');
    this.el.setAttribute('aria-label', 'Insert');
    this.empty = document.createElement('div');
    this.empty.className = 'dw-slash-empty';
    this.empty.textContent = 'No matches';
    document.body.appendChild(this.el);
  }

  render() {
    this.el.innerHTML = '';
    if (this.filtered.length === 0) {
      this.el.appendChild(this.empty);
      return;
    }
    if (this.hi >= this.filtered.length) this.hi = this.filtered.length - 1;
    if (this.hi < 0) this.hi = 0;
    this.filtered.forEach((it, i) => {
      const row = document.createElement('div');
      row.className = 'dw-slash-opt' + (i === this.hi ? ' dw-slash-hi' : '');
      row.setAttribute('role', 'option');
      row.setAttribute('aria-selected', i === this.hi ? 'true' : 'false');
      row.setAttribute('data-tip', it.desc);
      const ic = document.createElement('span');
      ic.className = 'dw-slash-ic';
      // The editor's one icon set (icons.js); H2 / H3 stay as type labels.
      if (it.svg) ic.appendChild(icon(it.svg)); else ic.textContent = it.icon;
      const txt = document.createElement('span');
      txt.className = 'dw-slash-text';
      const name = document.createElement('span');
      name.className = 'dw-slash-name';
      name.textContent = it.title;
      const desc = document.createElement('span');
      desc.className = 'dw-slash-desc';
      desc.textContent = it.desc;
      txt.appendChild(name);
      txt.appendChild(desc);
      row.appendChild(ic);
      row.appendChild(txt);
      row.addEventListener('mousedown', (e) => { e.preventDefault(); });
      row.addEventListener('mouseenter', () => { this.hi = i; this.paintHi(); });
      row.addEventListener('click', (e) => { e.preventDefault(); this.choose(i); });
      this.el.appendChild(row);
    });
  }

  paintHi() {
    Array.prototype.forEach.call(this.el.querySelectorAll('.dw-slash-opt'), (row, i) => {
      row.classList.toggle('dw-slash-hi', i === this.hi);
      row.setAttribute('aria-selected', i === this.hi ? 'true' : 'false');
    });
  }

  filter(query) {
    const q = (query || '').toLowerCase().trim();
    this.filtered = q
      ? this.items.filter((it) => (it.title.toLowerCase() + ' ' + it.match).includes(q))
      : this.items.slice();
    this.hi = 0;
    this.render();
  }

  position(from) {
    let coords;
    try { coords = this.view.coordsAtPos(from); } catch { coords = null; }
    if (!coords) return;
    this.el.style.left = Math.round(coords.left) + 'px';
    this.el.style.top = Math.round(coords.bottom + 4) + 'px';
    // Clamp to the viewport (right + bottom), mirroring showTableGridPicker.
    const w = this.el.offsetWidth;
    const h = this.el.offsetHeight;
    const vw = window.innerWidth;
    const vh = window.innerHeight;
    if (coords.left + w > vw - 8) this.el.style.left = Math.max(8, vw - w - 8) + 'px';
    if (coords.bottom + 4 + h > vh - 8) {
      const above = coords.top - 4 - h;
      this.el.style.top = Math.max(8, above) + 'px';
    }
  }

  move(delta) {
    if (this.filtered.length === 0) return;
    this.hi = (this.hi + delta + this.filtered.length) % this.filtered.length;
    this.paintHi();
  }

  choose(index) {
    const it = this.filtered[index == null ? this.hi : index];
    if (!it) return;
    // Close FIRST (removes the '/query' text via the plugin), then run the action on
    // the active view. The action may open a dialog (image / linked element) — that is
    // fine, the typed text is already gone.
    closeMenu(this.view, true);
    try { it.run(); } catch (e) { /* surfaced by the action itself */ }
  }

  destroy() {
    if (this.el.parentNode) this.el.parentNode.removeChild(this.el);
  }
}

/* Dispatch a meta transaction that closes the menu; when removeQuery is true it also
   deletes the '/query' range first (selection insert point onwards is untouched). */
function closeMenu(view, removeQuery) {
  const st = slashMenuKey.getState(view.state);
  let tr = view.state.tr;
  if (removeQuery && st && st.active) {
    const to = view.state.selection.from;
    if (st.from <= to) tr = tr.delete(st.from, to);
  }
  tr = tr.setMeta(slashMenuKey, { type: 'close' });
  view.dispatch(tr);
  view.focus();
}

export function slashMenuPlugin(opts) {
  return new Plugin({
    key: slashMenuKey,
    state: {
      init() { return { active: false, from: null }; },
      apply(tr, prev) {
        const meta = tr.getMeta(slashMenuKey);
        if (meta && meta.type === 'close') return { active: false, from: null };
        if (!prev.active) {
          // Open when the user just typed a qualifying '/' (a doc edit ending at the
          // cursor). Detection reads the transaction's RESULTING doc + selection — no
          // input interception, so the slash is inserted exactly once and the same path
          // serves real and scripted typing alike.
          if (!tr.docChanged) return prev;
          const a = slashAnchorFromTr(tr);
          return a >= 0 ? { active: true, from: a } : prev;
        }
        // Active: track the '/' anchor across edits; close if it is gone or the cursor
        // moved before it.
        const from = tr.mapping.map(prev.from, -1);
        if (!tr.selection.empty || tr.selection.from < from) return { active: false, from: null };
        const ch = tr.doc.textBetween(from, Math.min(from + 1, tr.doc.content.size));
        if (ch !== '/') return { active: false, from: null };
        return { active: true, from };
      },
    },
    props: {
      handleKeyDown(view, event) {
        const st = slashMenuKey.getState(view.state);
        if (!st.active) return false;
        const popup = pluginViewFor(view);
        if (!popup) return false;
        if (event.key === 'ArrowDown') { event.preventDefault(); popup.move(1); return true; }
        if (event.key === 'ArrowUp') { event.preventDefault(); popup.move(-1); return true; }
        if (event.key === 'Enter') {
          event.preventDefault();
          if (popup.filtered.length === 0) { closeMenu(view, false); return true; }
          popup.choose(null);
          return true;
        }
        if (event.key === 'Escape') { event.preventDefault(); closeMenu(view, false); return true; }
        return false;
      },
    },
    view(editorView) {
      let popup = null;
      let onDocMouseDown = null;
      pluginViews.set(editorView, () => popup);

      const sync = () => {
        const st = slashMenuKey.getState(editorView.state);
        if (st.active && !popup) {
          popup = new SlashPopup(editorView, opts);
          popup.render();
          const query = editorView.state.doc.textBetween(st.from + 1, editorView.state.selection.from);
          popup.filter(query);
          popup.position(st.from);
          onDocMouseDown = (e) => {
            if (popup && !popup.el.contains(e.target)) closeMenu(editorView, false);
          };
          document.addEventListener('mousedown', onDocMouseDown, true);
        } else if (st.active && popup) {
          const query = editorView.state.doc.textBetween(st.from + 1, editorView.state.selection.from);
          popup.filter(query);
          popup.position(st.from);
        } else if (!st.active && popup) {
          teardown();
        }
      };

      const teardown = () => {
        if (onDocMouseDown) { document.removeEventListener('mousedown', onDocMouseDown, true); onDocMouseDown = null; }
        if (popup) { popup.destroy(); popup = null; }
      };

      sync();
      return {
        update() { sync(); },
        destroy() { teardown(); pluginViews.delete(editorView); },
      };
    },
  });
}

// Per-view bridge so handleKeyDown (a prop) can reach the live SlashPopup created in
// the plugin's view(). Keyed by EditorView; cleaned up on destroy.
const pluginViews = new Map();
function pluginViewFor(view) {
  const getter = pluginViews.get(view);
  return getter ? getter() : null;
}
