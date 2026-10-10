/* Table NodeView (M3 + feedback round 1): the pipeline\'s table figure, directly
   editable with a click-to-activate model.
   - Click the table → ProseMirror NodeSelection → selectNode(): cell grid lines
     appear (editing aid only — the handover look stays clean) and the table\'s
     tools mount in the FIXED context bar under the banner. Esc or clicking
     elsewhere deselects (deselectNode commits any open cell editor).
   - Cells are always resolved by row/column INDEX against the live DOM — never by
     element reference across a re-render (that was the disappearing-cell bug:
     a no-op commit early-returned without restoring the cell\'s HTML).
   - Display comes from pipeline HTML (pristine) or the cell html cache (edited);
     markdown is never parsed in the browser. Edits are single transactions on
     attrs.meta → native undo/redo; untouched rows keep their bytes (W3). */
import { DOMParser as PmDomParser, DOMSerializer, Fragment, Slice } from 'prosemirror-model';
import { EditorState, Plugin, PluginKey, NodeSelection } from 'prosemirror-state';
import { EditorView, Decoration, DecorationSet } from 'prosemirror-view';
import { keymap } from 'prosemirror-keymap';
import { history, undo, redo } from 'prosemirror-history';
import { baseKeymap, toggleMark } from 'prosemirror-commands';
import { serializeInlineContent } from './serialize.js';
import { routePastedImage, dataImageGuard } from './pasteimage.js';
import { toSurfaceSrc } from './toolhelpers.js';
import { buildStrip, buildMenu, openContextMenu } from './contexttools.js';
import { trackWidthDrag } from './boxresize.js';
import { CELL_IMAGE_WIDTHS, CELL_IMAGE_SNAPS, SNAP_TOLERANCE, MIN_IMAGE_WIDTH, MIN_COL_DRAG_PX, MIN_COL_PCT, LOCK_FLASH_MS, HANDLE_OFFSET } from './constants.js';

export function splitRowLine(line) {
  let s = line.trim();
  if (s.startsWith('|')) s = s.slice(1);
  if (s.endsWith('|') && !s.endsWith('\\|')) s = s.slice(0, -1);
  const cells = [];
  let cur = '';
  for (let i = 0; i < s.length; i++) {
    const ch = s[i];
    if (ch === '\\' && i + 1 < s.length) { cur += ch + s[i + 1]; i++; continue; }
    if (ch === '|') { cells.push(cur.trim()); cur = ''; continue; }
    cur += ch;
  }
  cells.push(cur.trim());
  return cells;
}

/* Editable model from server meta + the rendered table element (cell html cache).
   Shared with the gate scenario so tests exercise the same path as the UI. */
export function buildTableModel(meta, tableEl) {
  const htmlRows = tableEl ? Array.from(tableEl.querySelectorAll('tr')) : [];
  // Harvested cell HTML must never contain editor chrome (the injected width
  // grips) — a cached dead grip would shadow the live one after a re-render
  // (that was the resize-works-only-once bug).
  const cellHtml = (td) => {
    const tmp = td.cloneNode(true);
    for (const n of tmp.querySelectorAll('.e2-colgrip, .e2-fighandle, .ProseMirror-trailingBreak')) n.remove();
    return tmp.innerHTML;
  };
  const cellsOf = (line, tr) => {
    const mds = splitRowLine(line);
    const tds = tr ? Array.from(tr.children) : [];
    return mds.map((md, i) => ({ md, html: tds[i] ? cellHtml(tds[i]) : escapeHtml(md) }));
  };
  return {
    header: { src: meta.headerLine, cells: cellsOf(meta.headerLine, htmlRows[0]) },
    rows: meta.rowLines.map((line, i) => ({ src: line, cells: cellsOf(line, htmlRows[i + 1]) })),
    delimiter: { src: meta.delimiterLine },
    aligns: meta.aligns.slice(),
    widths: meta.widths,
    width: meta.width,        // overall table-box width (L202), e.g. "50%"; null = full column
    align: meta.align,        // box alignment (L205): "center" | "right"; null = left (default)
    caption: meta.caption ?? null,  // human-readable table title (Task 5); null = no caption
    id: meta.explicitId,
    attrSrc: meta.attrLine,
  };
}

// Box-placement tooltips, shared by the bar's Place control and the right-click menu so they
// can't drift (one copy of the copy).
const TABLE_PLACE_TIPS = {
  left: 'Place the table at the LEFT (default).',
  center: 'Centre the table in the page/zone (once it is narrower than the page).',
  right: 'Place the table at the RIGHT (once it is narrower than the page).',
};

export class TableView {
  constructor(node, view, getPos, schema, ui) {
    this.node = node;
    this.view = view;
    this.getPos = getPos;
    this.schema = schema;
    this.ui = ui;
    this.parser = PmDomParser.fromSchema(schema);
    this.serializer = DOMSerializer.fromSchema(schema);
    this.cellEditor = null; // { view, r, c, backupHtml }
    this.active = false;

    this.dom = document.createElement('figure');
    this.dom.className = 'dw-figure dw-table-figure';
    this.dom.addEventListener('mousedown', (e) => this.onMouseDown(e));
    this.dom.addEventListener('contextmenu', (e) => this.onContextMenu(e));
    this.render();
  }

  meta() { return this.node.attrs.meta; }

  /* ---------- activation (ProseMirror NodeSelection drives it) ---------- */

  selectNode() {
    this.active = true;
    this.dom.classList.add('e2-active');
    if (this.ui) this.ui.set(this.buildTools(), null, 'table');
    // Esc inside Focus mode exits the focus first; the main keymap\'s Esc (which
    // deselects) only sees the second press. Capture phase, table-scoped.
    this.escListener = (e) => {
      if (e.key === 'Escape' && this.focusMode && !this.cellEditor) {
        e.stopPropagation();
        e.preventDefault();
        this.setFocusMode(false);
      }
    };
    document.addEventListener('keydown', this.escListener, true);
  }

  deselectNode() {
    // deselectNode runs inside ProseMirror\'s update cycle — never dispatch
    // synchronously from here. Close the editor now, apply the commit after.
    const pending = this.takeCellCommit();
    this.active = false;
    this.setFocusMode(false);
    this.dom.classList.remove('e2-active');
    if (this.ui) this.ui.set(null);
    if (this.escListener) {
      document.removeEventListener('keydown', this.escListener, true);
      this.escListener = null;
    }
    if (pending) setTimeout(() => this.applyCellCommit(pending), 0);
  }

  setFocusMode(on) {
    this.focusMode = !!on;
    this.dom.classList.toggle('e2-focus', this.focusMode);
    // Inject or remove the dimmed backdrop (z-index 4, sits between toolbar and the focused table).
    const existing = document.getElementById('e2-focus-backdrop');
    if (this.focusMode && !existing) {
      const bd = document.createElement('div');
      bd.id = 'e2-focus-backdrop';
      bd.className = 'e2-focus-backdrop';
      document.body.appendChild(bd);
    } else if (!this.focusMode && existing) {
      existing.remove();
    }
  }

  onMouseDown(e) {
    const cell = e.target.closest('td, th');
    if (!cell || !this.dom.contains(cell)) return;
    if (e.target.closest('.e2-colgrip')) return; // width drag handles itself
    if (!this.active) {
      // Cold click: let ProseMirror select the node first, then open the SAME
      // cell — a single click goes straight into editing (stakeholder feedback).
      const { r, c } = this.indexOfCell(cell);
      setTimeout(() => { if (this.active) this.openCellEditorAt(r, c); }, 0);
      return;
    }
    if (this.cellEditor && this.cellAt(this.cellEditor.r, this.cellEditor.c) === cell) return;
    e.preventDefault();
    e.stopPropagation();
    const { r, c } = this.indexOfCell(cell);
    this.commitCellEditor();           // may re-render the table
    this.openCellEditorAt(r, c);       // re-resolves the cell in the LIVE dom
  }

  /* ---------- model ---------- */

  ensureModel(meta) {
    return meta.model || buildTableModel(meta, this.dom.querySelector('table'));
  }

  patchModel(mutate) {
    const pos = this.getPos();
    if (pos == null) return;
    const meta = this.meta();
    const model = structuredClone(this.ensureModel(meta));
    mutate(model);
    this.view.dispatch(this.view.state.tr.setNodeMarkup(pos, null, {
      ...this.node.attrs,
      meta: { ...meta, model },
    }));
  }

  /* ---------- rendering ---------- */

  render() {
    this.closeCellEditor();
    const meta = this.meta();
    this.dom.innerHTML = '';
    this.dom.classList.toggle('e2-active', this.active);
    const id = meta.model ? meta.model.id : meta.explicitId;
    if (id) this.dom.id = id;
    else this.dom.removeAttribute('id');

    // Overall table-box width (L202): size the figure narrower than the column (height
    // follows content). dw-table-sized lets CSS drop the default full-column stretch so
    // the inline width wins — the edit DOM matches the handover's .dw-table-figure.
    const boxWidth = (meta.model ? meta.model.width : meta.width) || null;
    this.dom.classList.toggle('dw-table-sized', !!boxWidth);
    this.dom.style.width = boxWidth || '';
    // Box alignment (L205): centre/right within the column/zone (left = default, no class).
    const boxAlign = (meta.model ? meta.model.align : meta.align) || null;
    this.dom.classList.toggle('dw-box-center', boxAlign === 'center');
    this.dom.classList.toggle('dw-box-right', boxAlign === 'right');

    const model = meta.model;
    if (!model) {
      const t = document.createElement('template');
      t.innerHTML = this.node.attrs.html;
      const original = t.content.querySelector('table');
      this.dom.appendChild(original ? document.importNode(original, true) : document.createElement('table'));
    } else {
      const table = document.createElement('table');
      if (model.widths) {
        const colgroup = document.createElement('colgroup');
        for (const w of model.widths.split(/\s+/).filter(Boolean)) {
          const col = document.createElement('col');
          col.style.width = w;
          colgroup.appendChild(col);
        }
        table.appendChild(colgroup);
      }
      const thead = document.createElement('thead');
      thead.appendChild(this.renderRow(model.header.cells, 'th', model.aligns));
      table.appendChild(thead);
      const tbody = document.createElement('tbody');
      model.rows.forEach((row) => tbody.appendChild(this.renderRow(row.cells, 'td', model.aligns)));
      table.appendChild(tbody);
      this.dom.appendChild(table);
    }

    const captionEl = document.createElement('figcaption');
    const captionText = (meta.model ? meta.model.caption : meta.caption) || null;
    captionEl.textContent = 'Table ' + meta.number + (captionText ? ' — ' + captionText : '');
    this.dom.appendChild(captionEl);

    // Width grips on header cells (visible only while active, via CSS).
    for (const th of this.dom.querySelectorAll('thead th')) {
      const grip = document.createElement('span');
      grip.className = 'e2-colgrip';
      grip.contentEditable = 'false';
      grip.setAttribute('data-tip', 'Drag to set column widths (saved as percentages). Use Auto widths to remove them.');
      grip.onmousedown = (e) => this.dragWidth(e, Array.from(th.parentElement.children).indexOf(th));
      th.appendChild(grip);
    }

    // Box-width handle (L202): drag the table's right edge to size the whole table as a
    // % of the page/zone width (height follows content). Visible only while active (CSS).
    const boxGrip = document.createElement('span');
    boxGrip.className = 'e2-boxhandle';
    boxGrip.contentEditable = 'false';
    boxGrip.setAttribute('data-tip', 'Drag to set the table width (saved as % of the page). Drag fully to the right to reset to full width.');
    boxGrip.onmousedown = (e) => this.dragBoxWidth(e);
    this.dom.appendChild(boxGrip);
  }

  renderRow(cells, tag, aligns) {
    const tr = document.createElement('tr');
    cells.forEach((cell, i) => {
      const el = document.createElement(tag);
      el.innerHTML = cell.html;
      const a = aligns[i];
      if (a) el.style.textAlign = a;
      tr.appendChild(el);
    });
    return tr;
  }

  /* ---------- cell addressing: indices against the live DOM, always ---------- */

  cellAt(r, c) {
    const table = this.dom.querySelector('table');
    if (!table) return null;
    const tr = r < 0
      ? table.querySelector('thead tr')
      : table.querySelectorAll('tbody tr')[r];
    return tr ? tr.children[c] || null : null;
  }

  indexOfCell(cell) {
    const tr = cell.parentElement;
    const c = Array.from(tr.children).indexOf(cell);
    const inHead = tr.parentElement.tagName === 'THEAD';
    const table = this.dom.querySelector('table');
    const r = inHead ? -1 : Array.from(table.querySelectorAll('tbody tr')).indexOf(tr);
    return { r, c };
  }

  /* ---------- contextual tools (mounted in the fixed bar by selectNode) ---------- */

  /* ---- shared table operations: ONE implementation each, called by BOTH the bar (with the
     open cell editor's position) and the right-click menu (with the clicked cell's position),
     so the two surfaces can't drift (ADR-008). `at` is a body-row / column index; row ops use
     -1 for the header row, null for "no reference cell". ---- */

  insertRowAt(at, where) {
    this.commitCellEditor();
    this.patchModel((m) => {
      const cols = m.header.cells.length;
      const row = { src: null, cells: Array.from({ length: cols }, () => ({ md: '', html: '' })) };
      // null → append at the end (the bar's default); header (-1) → top of the body;
      // otherwise insert above (at) or below (at + 1) the reference row.
      const idx = at == null ? m.rows.length : at < 0 ? 0 : where === 'above' ? at : at + 1;
      m.rows.splice(idx, 0, row);
    });
    this.view.focus();
  }

  deleteRowAt(at) {
    this.commitCellEditor();
    if (at == null || at < 0) {
      if (this.ui) this.ui.flash('Click a cell in the row first — Remove row deletes that row (the header row stays).');
      return;
    }
    if (this.ensureModel(this.meta()).rows.length <= 1) {
      if (this.ui) this.ui.flash('A table needs at least one row — delete the whole table instead.');
      return;
    }
    this.patchModel((m) => { if (m.rows.length > 1) m.rows.splice(at, 1); });
    this.view.focus();
  }

  insertColAt(at, offset) {
    this.commitCellEditor();
    this.patchModel((m) => {
      const idx = (at == null ? m.header.cells.length - 1 : at) + offset;
      m.header.cells.splice(idx, 0, { md: '', html: '' });
      m.header.src = null;
      m.rows.forEach((r) => { r.cells.splice(idx, 0, { md: '', html: '' }); r.src = null; });
      m.aligns.splice(idx, 0, null);
      m.delimiter.src = null;
      m.widths = null;
      m.attrSrc = null;
    });
    this.view.focus();
  }

  deleteColAt(at) {
    this.commitCellEditor();
    if (at == null) {
      if (this.ui) this.ui.flash('Click a cell in the column first — Remove column deletes that column.');
      return;
    }
    if (this.ensureModel(this.meta()).header.cells.length <= 1) {
      if (this.ui) this.ui.flash('A table needs at least one column — delete the whole table instead.');
      return;
    }
    this.patchModel((m) => {
      if (m.header.cells.length <= 1) return;
      m.header.cells.splice(at, 1);
      m.header.src = null;
      m.rows.forEach((r) => { r.cells.splice(at, 1); r.src = null; });
      m.aligns.splice(at, 1);
      m.delimiter.src = null;
      m.widths = null;
      m.attrSrc = null;
    });
    this.view.focus();
  }

  setColAlign(at, value) {
    this.commitCellEditor();
    if (at == null) {
      if (this.ui) this.ui.flash('Click a cell first — alignment applies to that cell\'s whole column.');
      return;
    }
    this.patchModel((m) => { m.aligns[at] = value; m.delimiter.src = null; });
    this.view.focus();
  }

  setBoxAlign(val) {
    this.commitCellEditor();
    this.patchModel((m) => { m.align = val; m.attrSrc = null; });
    this.view.focus();
  }

  clearWidths() {
    this.commitCellEditor();
    this.patchModel((m) => { m.widths = null; m.attrSrc = null; });
  }

  copyLink() {
    const m = this.meta();
    const anchor = (m.model ? m.model.id : m.explicitId) || 'table-' + m.number;
    const ref = 'dw:' + ((this.ui && this.ui.chapterId) || '') + '#' + anchor;
    if (navigator.clipboard) navigator.clipboard.writeText(ref);
    if (this.ui) this.ui.flash('Copied ' + ref + ' — paste it into any text or chapter-link dialog.');
  }

  toggleFocus() { this.setFocusMode(!this.focusMode); }

  setCaption(raw) {
    const val = raw.replace(/"/g, '”').replace(/[\r\n]+/g, ' ').trim() || null;
    this.commitCellEditor();
    this.patchModel((m) => { m.caption = val; m.attrSrc = null; });
  }

  deleteTable() {
    this.closeCellEditor();
    const pos = this.getPos();
    if (pos == null) return;
    this.view.dispatch(this.view.state.tr.delete(pos, pos + this.node.nodeSize));
    this.view.focus();
  }

  // The tool strip is built from the ONE shared context-tools definition (contexttools.js,
  // ADR-008 / rule 21); this method only supplies the table handlers + current state. The
  // TABLE badge is rendered by contextBar.set() from the shared BADGES map. Row/col/align
  // handlers read the OPEN cell editor's position at click time.
  buildTools() {
    const meta = this.meta();
    const widths = meta.model ? meta.model.widths : meta.widths;
    const row = () => (this.cellEditor ? this.cellEditor.r : null);
    const col = () => (this.cellEditor ? this.cellEditor.c : null);
    return buildStrip('table', {
      view: this.view, getPos: this.getPos, node: this.node,
      onRowAdd: () => this.insertRowAt(row(), 'below'),
      onRowDel: () => this.deleteRowAt(row()),
      onColAddLeft: () => this.insertColAt(col(), 0),
      onColAddRight: () => this.insertColAt(col(), 1),
      onColDel: () => this.deleteColAt(col()),
      onCellAlign: (value) => this.setColAlign(col(), value),
      placeKind: 'box',
      align: meta.model ? meta.model.align : meta.align,
      placeTips: TABLE_PLACE_TIPS,
      onPlace: (val) => this.setBoxAlign(val),
      autoActive: !widths,
      onAutoWidths: () => this.clearWidths(),
      linkTip: 'Copy this table’s link — paste it into any text or into a view.',
      onLink: () => this.copyLink(),
      focusActive: !!this.focusMode,
      onFocus: () => this.toggleFocus(),
      captionValue: (meta.model ? meta.model.caption : meta.caption) ?? '',
      captionPlaceholder: 'Table caption',
      captionTip: 'The caption shown under the table (e.g. "Table 1 — Safety symbols").',
      onCaption: (raw) => this.setCaption(raw),
      onDelete: () => this.deleteTable(),
      deleteTip: 'Delete this table INCLUDING its content (Ctrl+Z brings it back).',
      hint: 'Click a cell to edit it · <kbd>Esc</kbd> steps out',
      // ⋯ opens the right-click menu, aimed at the open cell (or the table's end) like the bar.
      menuCtx: () => this.menuCtx(row(), col()),
    });
  }

  // Right-click menu ctx: the SAME handlers as the bar, but row/col/align target the
  // RIGHT-CLICKED cell (r, c) instead of the open cell editor — and it offers the richer
  // positional ops the compact bar can't (insert row above, insert column left/right).
  menuCtx(r, c) {
    const meta = this.meta();
    const widths = meta.model ? meta.model.widths : meta.widths;
    return {
      view: this.view, getPos: this.getPos, node: this.node,
      onRowInsertAbove: () => this.insertRowAt(r, 'above'),
      onRowInsertBelow: () => this.insertRowAt(r, 'below'),
      onRowDel: () => this.deleteRowAt(r),
      onColAddLeft: () => this.insertColAt(c, 0),
      onColAddRight: () => this.insertColAt(c, 1),
      onColDel: () => this.deleteColAt(c),
      onCellAlign: (value) => this.setColAlign(c, value),
      placeKind: 'box',
      align: meta.model ? meta.model.align : meta.align,
      placeTips: TABLE_PLACE_TIPS,
      onPlace: (val) => this.setBoxAlign(val),
      autoActive: !widths,
      onAutoWidths: () => this.clearWidths(),
      onLink: () => this.copyLink(),
      focusActive: !!this.focusMode,
      onFocus: () => this.toggleFocus(),
      onDelete: () => this.deleteTable(),
    };
  }

  // Right-click anywhere on the table: open the shared context menu at the cursor, targeting
  // the clicked cell. Selecting the table first mounts the bar too (consistent state).
  onContextMenu(e) {
    const cell = e.target.closest('td, th');
    if (!cell || !this.dom.contains(cell)) return;
    e.preventDefault();
    const { r, c } = this.indexOfCell(cell);
    const pos = this.getPos();
    if (pos != null && !this.active) {
      this.view.dispatch(this.view.state.tr.setSelection(NodeSelection.create(this.view.state.doc, pos)));
    }
    openContextMenu(buildMenu('table', this.menuCtx(r, c)), e.clientX, e.clientY);
  }

  dragWidth(e, index) {
    window.__dwDragStart = { index, at: Date.now() }; // scenario probe
    e.preventDefault();
    e.stopPropagation();
    this.commitCellEditor(); // may re-render; resolve everything fresh below
    const table = this.dom.querySelector('table');
    const th = table ? table.querySelectorAll('thead th')[index] : null;
    if (!table || !th) return;
    const startX = e.clientX;
    const startWidths = Array.from(table.querySelectorAll('thead th'))
      .map((h) => h.getBoundingClientRect().width);
    const col = table.querySelectorAll('colgroup col')[index] || null;
    // Force fixed layout for the duration of the drag so the live `col`/`th` width is
    // AUTHORITATIVE (under auto layout <col> widths are advisory and the column would only
    // snap on mouse-release). Once widths commit, the colgroup keeps it fixed via CSS.
    // Force fixed layout INLINE for the drag (an inline style beats any CSS-scope/:has/specificity
    // question — a class-based switch silently failed in the editor's @scoped CSS, so a <col>/th
    // width stayed advisory and the column only moved on mouse-release). Restored in up().
    const prevLayout = table.style.tableLayout;
    table.style.tableLayout = 'fixed';
    const target = (dx) => Math.max(MIN_COL_DRAG_PX, Math.round(startWidths[index] + dx));
    // Set the width synchronously on each mousemove. Drive the colgroup col when it exists
    // (once widths are set the engine ignores a narrower inline th). A bare style.width write
    // does not force a sync reflow (we read no layout here), so the browser coalesces paints
    // per frame on its own — no rAF needed (and rAF does not fire under headless virtual time).
    const move = (ev) => {
      const w = target(ev.clientX - startX) + 'px';
      if (col) col.style.width = w;
      else th.style.width = w;
    };
    const up = (ev) => {
      document.removeEventListener('mousemove', move);
      document.removeEventListener('mouseup', up);
      table.style.tableLayout = prevLayout; // back to CSS-driven layout; patchModel re-renders next
      // Percentages from drag MATH, not from a post-layout re-measure (the engine
      // may refuse the new width visually; the intent must still be saved).
      const sizes = startWidths.slice();
      sizes[index] = target(ev.clientX - startX);
      const total = sizes.reduce((a, b) => a + b, 0);
      if (!total) return;
      const widths = sizes.map((w) => Math.max(MIN_COL_PCT, Math.round((w / total) * 100)) + '%').join(' ');
      window.__dwDragDebug = { index, startWidths, dx: ev.clientX - startX, widths }; // scenario probe
      this.patchModel((m) => { m.widths = widths; m.attrSrc = null; });
      this.view.focus();
    };
    document.addEventListener('mousemove', move);
    document.addEventListener('mouseup', up);
  }

  /* Box-width drag (L202): size the whole table as a % of the available width (the page
     column or a slide zone). Height follows content — width only. Dragging (nearly) to
     the right edge resets to full width (m.width = null). Percentages come from drag math
     against the container width, not a post-layout re-measure. */
  dragBoxWidth(e) {
    e.preventDefault();
    e.stopPropagation();
    this.commitCellEditor();
    // VISUAL widths (getBoundingClientRect) for the figure AND its container, so the ratio
    // is zoom-independent (the slide surface is CSS-zoomed; see boxresize.js). Read
    // startVisual BEFORE adding .dw-table-sized — that class drops min-width:100%, so
    // measuring after it would catch a collapsed fit-content width (the "jumps left" bug).
    const container = this.dom.parentElement;
    const avail = container ? container.getBoundingClientRect().width : this.dom.getBoundingClientRect().width;
    const startVisual = this.dom.getBoundingClientRect().width;
    this.dom.classList.add('dw-table-sized');
    trackWidthDrag(this.dom, e.clientX, startVisual, avail, (width) => {
      this.patchModel((m) => { m.width = width; m.attrSrc = null; });
      this.view.focus();
    }, '__dwBoxDrag');
  }

  /* ---------- roaming cell editor ---------- */

  /* The cell\'s PM-model round-trip, rendered to HTML by the schema\'s own toDOM —
     never the live view DOM, which carries editing chrome (contenteditable,
     separator imgs, trailing breaks) that poisoned every HTML comparison. */
  modelHtml(doc) {
    const parts = [];
    doc.content.forEach((n) => {
      if (n.type.name !== 'paragraph') return;
      const div = document.createElement('div');
      div.appendChild(this.serializer.serializeFragment(n.content));
      parts.push(div.innerHTML);
    });
    return parts.join(' ');
  }

  openCellEditorAt(r, c) {
    const cell = this.cellAt(r, c);
    if (!cell) return;
    // Strip ALL editor chrome before capturing the backup (same rule as the
    // buildTableModel harvest): chrome is never content, and a stray grip in the
    // backup would resurface as a dead handle on restore.
    for (const n of cell.querySelectorAll('.e2-colgrip, .e2-fighandle')) n.remove();
    const backupHtml = cell.innerHTML;

    const wrap = document.createElement('div');
    const p = document.createElement('p');
    p.innerHTML = backupHtml;
    wrap.appendChild(p);
    const doc = this.parser.parse(wrap);

    // Faithfulness gate — BEFORE mounting anything, so a locked cell is never
    // touched: if the schema cannot model this cell 1:1, the first keystroke
    // would degrade it. Model-level compare (parse → toDOM), not view DOM.
    const initial = this.modelHtml(doc);
    if (!htmlEquivalent(initial, backupHtml)) {
      window.__dwLockDebug = { initial, backup: backupHtml }; // scenario probe (cellopen)
      // Self-diagnosing lock: log the exact compared strings so a screenshot or
      // console dump can be root-caused without reproducing the project locally.
      console.warn('[DocWiz] cell lock at row ' + r + ', col ' + c
        + '\n- cell HTML:        ' + backupHtml
        + '\n- model round-trip: ' + initial);
      cell.classList.add('e2-locked');
      setTimeout(() => cell.classList.remove('e2-locked'), LOCK_FLASH_MS);
      if (this.ui && this.ui.flash)
        this.ui.flash('This cell contains content the in-place cell editor cannot keep 1:1 (e.g. a sized image) — edit it via Source (advanced).');
      return;
    }

    cell.innerHTML = '';
    cell.classList.add('e2-cell-editing');
    const self = this;
    const holder = { view: null, r, c, backupHtml, dirty: false };
    const mini = new EditorView(cell, {
      state: EditorState.create({
        schema: this.schema,
        doc,
        plugins: [
          history(),
          keymap({
            Enter: () => { self.commitCellEditor(); self.view.focus(); return true; },
            // Shift+Enter inserts a hard_break in the cell; it serializes to <br>
            // (serializeInlineContent) and the pipeline renders it as a break in the cell.
            'Shift-Enter': (state, dispatch) => {
              if (dispatch)
                dispatch(state.tr.replaceSelectionWith(self.schema.nodes.hard_break.create()).scrollIntoView());
              return true;
            },
            Escape: () => { self.closeCellEditor(); self.view.focus(); return true; },
            Tab: () => self.moveCell(1),
            'Shift-Tab': () => self.moveCell(-1),
            'Mod-z': undo,
            'Mod-y': redo,
            'Mod-b': toggleMark(this.schema.marks.strong),
            'Mod-i': toggleMark(this.schema.marks.em),
          }),
          keymap(baseKeymap),
          cellImageResizePlugin(),
          dataImageGuard(), // a Word-bitmap paste inlines a data: URI here — convert it to an asset
        ],
      }),
      dispatchTransaction(tr) {
        mini.updateState(mini.state.apply(tr));
        if (tr.docChanged) holder.dirty = true;
      },
      // A pasted/dropped image (incl. a Word "Copy" that arrives as a data: URI in the
      // clipboard HTML) routes through the project-assets dialog and lands as an asset
      // link — never a base64 blob inlined into the cell markdown. Other pasted content is
      // inserted INLINE (block breaks → hard_breaks) so a cell stays a single line/paragraph:
      // the default paste would drop a <p> in as a new paragraph (empty line in the editor +
      // a space-joined leading whitespace on save).
      handlePaste: (_v, event) => self.cellPaste(mini, event.clipboardData),
      handleDrop: (_v, event) => self.cellPaste(mini, event.dataTransfer),
    });
    holder.view = mini;
    this.cellEditor = holder;
    window.__dwActiveCell = { view: mini, table: this }; // image inserts route here

    // Clicking a picture inside the cell shows its size tools in the context bar.
    // Resolve the image\'s document position once and key the tools on it (not the
    // element, which a patch re-renders) — see showCellImageTools.
    mini.dom.addEventListener('click', (e) => {
      const img = e.target.closest('img');
      if (!img) return;
      const pos = mini.posAtDOM(img, 0);
      if (pos < 0) return;
      mini.dispatch(mini.state.tr.setMeta(cellImgResizeKey, { pos, width: null })); // show the handle
      this.showCellImageTools(mini, pos);
    });

    mini.focus();
  }

  commitCellEditor() {
    const pending = this.takeCellCommit();
    if (pending) this.applyCellCommit(pending);
  }

  /* Tab / Shift+Tab between cells: commit the current cell, then open the next/prev.
     Header row is r = -1; body rows are 0-based. Wraps across rows; off the end is a no-op.
     Geometry is read BEFORE commitCellEditor() because that call closes the editor (clearing
     this.cellEditor) and may re-render the table. */
  moveCell(dir) {
    const ce = this.cellEditor;
    if (!ce) return false;
    const { r, c } = ce;
    const model = this.ensureModel(this.meta());
    const cols = model.header.cells.length;
    const lastRow = model.rows.length - 1;
    this.commitCellEditor();
    let nr = r, nc = c + dir;
    if (nc >= cols) { nc = 0; nr = (r === -1) ? 0 : r + 1; }
    else if (nc < 0) { nc = cols - 1; nr = (r === 0) ? -1 : r - 1; }
    if (nr > lastRow) return true;                  // past the last body row: no-op, stay closed
    if (nr < -1) return true;                       // before the very first cell (header col 0, Shift+Tab): stop
    this.openCellEditorAt(nr, nc);
    return true;
  }

  /* Computes the commit, closes the editor and RESTORES the cell display (the fix
     for the disappearing-cell bug: a no-op must leave the backup in place).
     Returns the pending model change, or null when nothing changed. */
  takeCellCommit() {
    const ce = this.cellEditor;
    if (!ce) return null;
    // A mere VISIT never commits: only real typing (docChanged transactions)
    // counts. This is the hard guarantee against visit-degrades-content bugs.
    if (!ce.dirty) {
      this.closeCellEditor();
      return null;
    }
    const parts = [];
    ce.view.state.doc.content.forEach((n) => {
      if (n.type.name === 'paragraph') parts.push(serializeInlineContent(n));
    });
    const md = parts.join(' ').replace(/(?<!\\)\|/g, '\\|');
    const html = this.modelHtml(ce.view.state.doc);
    const { r, c, backupHtml } = ce;
    this.closeCellEditor();
    if (htmlEquivalentExact(html, backupHtml)) return null; // ws-sensitive: a whitespace-only cleanup still commits
    return { r, c, md, html };
  }

  /* Paste/drop into a cell: an image routes to assets (cellPasteImage); anything else is
     inserted INLINE — clipboard HTML is parsed and flattened to inline content with
     hard_breaks between former blocks, plain text splits on newlines into hard_breaks. This
     keeps the cell a single paragraph (the default paste dropped a <p> in as a new paragraph,
     which showed an empty line in the editor and serialized as a space-joined leading
     whitespace on the next line). Returns true when handled (stops the default paste). */
  cellPaste(mini, data) {
    if (data && this.cellPasteImage(mini, data)) return true; // an image was routed to assets
    if (!data) return false;
    const html = data.getData('text/html');
    const text = data.getData('text/plain');
    let nodes = null;
    if (html) {
      const tmp = document.createElement('div');
      tmp.innerHTML = html;
      nodes = flattenToInline(this.parser.parse(tmp), this.schema);
    } else if (text) {
      nodes = [];
      text.split(/\r\n|\r|\n/).forEach((line, i) => {
        if (i > 0) nodes.push(this.schema.nodes.hard_break.create());
        if (line) nodes.push(this.schema.text(line));
      });
      while (nodes.length && nodes[nodes.length - 1].type.name === 'hard_break') nodes.pop();
    }
    if (!nodes || !nodes.length) return false; // nothing usable → let the default paste run
    mini.dispatch(mini.state.tr.replaceSelection(new Slice(Fragment.fromArray(nodes), 0, 0)).scrollIntoView());
    mini.focus();
    return true;
  }

  /* Route a pasted/dropped image in this cell through the project-assets dialog (saved
     under assets/, with the folder prompt) and insert it inline — instead of letting
     ProseMirror ingest a data: URI (Word "Copy" puts the image in the clipboard HTML as
     a base64 data URI). Returns true when it handled an image, stopping the default. */
  cellPasteImage(mini, data) {
    return routePastedImage(data, (file) => {
      const bridge = window.DocWizBridge;
      if (!bridge || !bridge.pasteImage) return;
      bridge.pasteImage(file, (res) => {
        const src = toSurfaceSrc(res.src);
        const img = this.schema.nodes.image.create({ src, alt: res.alt || '', width: null, align: null });
        mini.dispatch(mini.state.tr.replaceSelectionWith(img, false).scrollIntoView());
        mini.focus();
      });
    });
  }

  /* Tools for a picture inside the focused cell. Built from the ONE shared context-tools
     definition (contexttools.js, kind 'cellImage') — the F1 follow-up that this design pass
     finally lands: figure and in-cell image now share the same builder, glyphs and states,
     differing only by declared capability flags.
     Capability decisions vs the standalone figure (ADR-008 — declared in contexttools.js
     OMIT_REASONS, deliberate, not drift): no Copy-Link (an inline image has no stable
     #anchor), no Caption toggle/field (a cell image has no figure caption — alt only), no
     Add-paragraph (an inline image is not a block). Rotate and Delete ARE provided: Rotate
     rotates the image FILE server-side exactly like a figure (not an in-cell HTML edit, so
     the faithfulness gate is irrelevant); Delete removes the inline image.
     Shares the figure's IMAGE context badge (kind 'image').
     Keyed on the image\'s DOCUMENT POSITION, never a DOM element: a patch re-renders the
     <img>, so a stale element reference would resolve to -1 and crash nodeAt. The position
     is invariant across attr-only edits (an inline image is size 1 whatever its attrs). */
  showCellImageTools(mini, pos) {
    if (!this.ui || pos == null || pos < 0) return;
    const node = mini.state.doc.nodeAt(pos);
    if (!node || node.type.name !== 'image') return;
    const patchImg = (changes) => {
      const fresh = mini.state.doc.nodeAt(pos);
      if (fresh && fresh.type.name === 'image')
        mini.dispatch(mini.state.tr.setNodeMarkup(pos, null, { ...fresh.attrs, ...changes }));
      this.showCellImageTools(mini, pos); // pos is stable — refresh active states
    };
    const tools = buildStrip('cellImage', {
      sizeScale: CELL_IMAGE_WIDTHS,
      width: node.attrs.width,
      onSize: (w) => patchImg({ width: w }),
      onPx: (w) => patchImg({ width: w }),
      // Box-placement glyphs (shared with the figure/table/embed); a cell image defaults to
      // LEFT (null), unlike the centered standalone figure — hence placeKind 'box'.
      placeKind: 'box',
      align: node.attrs.align,
      placeTips: {
        left: 'Align this picture to the left of the cell (default).',
        center: 'Center this picture in the cell.',
        right: 'Align this picture to the right of the cell.',
      },
      onPlace: (val) => patchImg({ align: val }),
      onReplace: () => {
        const bridge = window.DocWizBridge;
        if (!bridge || !bridge.pickAsset) return;
        // pickAsset returns the file-relative form; the editor displays via its /assets/
        // route (same mapping FigureView uses). Save re-emits relative.
        bridge.pickAsset((res) => patchImg({ src: toSurfaceSrc(res.src), alt: node.attrs.alt || res.alt, missing: false }));
      },
      // Rotate the asset FILE server-side (GDI+) — not a document edit (the cell's <img src>
      // is unchanged), so no dirty/commit. The mini editor's <img> is cache-busted for instant
      // feedback; assets are no-store, so the committed cell refetches the rotated bytes.
      onRotate: () => {
        const fresh = mini.state.doc.nodeAt(pos);
        const src = fresh && fresh.type.name === 'image' ? fresh.attrs.src : null;
        if (!src) return;
        fetch('/api/rotate-image', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ src, clockwise: true }),
        })
          .then((r) => { if (!r.ok) return r.text().then((t) => { throw new Error(t || r.statusText); }); })
          .then(() => {
            const dom = mini.nodeDOM(pos);
            if (dom && dom.tagName === 'IMG') dom.src = dom.src.replace(/[?].*$/, '') + '?r=' + Date.now();
            if (this.ui && this.ui.flash) this.ui.flash('Rotated 90° — the picture file was updated.');
          })
          .catch((err) => { if (this.ui && this.ui.flash) this.ui.flash('Could not rotate this picture: ' + err.message); });
      },
      altValue: node.attrs.alt,
      onAlt: (v) => patchImg({ alt: v }),
      onDelete: () => {
        const fresh = mini.state.doc.nodeAt(pos);
        if (fresh && fresh.type.name === 'image') mini.dispatch(mini.state.tr.delete(pos, pos + fresh.nodeSize));
        if (this.ui) this.ui.set(null);   // the image is gone — clear its tools
        mini.focus();
      },
      deleteTip: 'Delete this picture from the cell (Ctrl+Z restores it).',
      hint: 'Picture in this cell · commit the cell to apply',
    });
    this.ui.set(tools, null, 'image');
  }

  applyCellCommit({ r, c, md, html }) {
    this.patchModel((m) => {
      const target = r < 0 ? m.header : m.rows[r];
      if (!target || !target.cells[c]) return;
      target.cells[c] = { md, html };
      target.src = null;
    });
  }

  closeCellEditor() {
    const ce = this.cellEditor;
    if (!ce) return;
    this.cellEditor = null;
    if (window.__dwActiveCell && window.__dwActiveCell.view === ce.view) window.__dwActiveCell = null;
    const cell = this.cellAt(ce.r, ce.c);
    ce.view.destroy();
    if (cell) {
      cell.classList.remove('e2-cell-editing');
      cell.innerHTML = ce.backupHtml; // commits re-render from the model right after
    }
  }

  /* ---------- ProseMirror NodeView contract ---------- */

  update(node) {
    if (node.type !== this.node.type) return false;
    const changed = node.attrs.meta !== this.node.attrs.meta || node.attrs.html !== this.node.attrs.html;
    this.node = node;
    if (changed) this.render();
    return true;
  }

  ignoreMutation() { return true; }

  stopEvent(event) {
    const t = event.target;
    if (!t || !t.closest) return false;
    if (t.closest('.e2-cell-editing, .e2-colgrip')) return true; // ours entirely
    if (this.active && /^mouse/.test(event.type) && t.closest('td, th')) return true; // cell activation is ours
    return false; // inactive clicks reach ProseMirror → NodeSelection → selectNode
  }

  destroy() {
    this.closeCellEditor();
    if (this.active && this.ui) this.ui.set(null);
  }
}

function escapeHtml(text) {
  const d = document.createElement('span');
  d.textContent = text;
  return d.innerHTML;
}

const cellImgResizeKey = new PluginKey('dwCellImgResize');

/* Resize handle for a picture inside the cell editor. The handle is a widget
   (PM-managed → never culled); live width is an inline-style node decoration set by
   non-docChanged meta transactions (a drag in progress never marks the cell dirty);
   the committed width is one setNodeMarkup on mouseup. Free drag with magnetic
   assist to the cell scale, mirroring the standalone figure handle. */
function cellImageResizePlugin() {
  return new Plugin({
    key: cellImgResizeKey,
    state: {
      init: () => ({ pos: null, width: null }),
      apply(tr, value) {
        const meta = tr.getMeta(cellImgResizeKey);
        if (meta !== undefined) return meta;
        if (value.pos == null) return value;
        return { pos: tr.mapping.map(value.pos, -1), width: value.width };
      },
    },
    props: {
      decorations(state) {
        const st = cellImgResizeKey.getState(state);
        if (!st || st.pos == null) return null;
        const node = state.doc.nodeAt(st.pos);
        if (!node || node.type.name !== 'image') return null;
        const decos = [];
        if (st.width != null)
          decos.push(Decoration.node(st.pos, st.pos + 1, { style: 'width:' + st.width + 'px' }));
        decos.push(Decoration.widget(st.pos + 1, (view) => cellImgHandle(view, st.pos), {
          side: 1, key: 'dw-cellimg-handle-' + st.pos, // stable per image → DOM reused mid-drag
        }));
        return DecorationSet.create(state.doc, decos);
      },
    },
  });
}

function cellImgHandle(view, pos) {
  const handle = document.createElement('span');
  handle.className = 'e2-fighandle'; // reuse the figure handle\'s look
  handle.contentEditable = 'false';
  handle.setAttribute('data-tip', 'Drag to resize freely — or use S / M / L. Pulls to ' + CELL_IMAGE_SNAPS.join(' / ') + ' px.');
  // The picture resizes live under the cursor (the width decoration) — that is the
  // feedback; no readout chrome, which was cramped and odd-looking inside a cell.
  const place = () => {
    const img = view.nodeDOM(pos);
    if (!img || !img.offsetParent) return;
    handle.style.left = (img.offsetLeft + img.offsetWidth - HANDLE_OFFSET) + 'px';
    handle.style.top = (img.offsetTop + img.offsetHeight - HANDLE_OFFSET) + 'px';
  };
  setTimeout(place, 0);
  const snap = (w) => {
    for (const s of CELL_IMAGE_SNAPS) if (Math.abs(w - s) <= SNAP_TOLERANCE) return s;
    return w;
  };
  handle.onmousedown = (e) => {
    e.preventDefault();
    e.stopPropagation();
    const startX = e.clientX;
    const img0 = view.nodeDOM(pos);
    const startW = img0 ? img0.getBoundingClientRect().width : 0;
    const width = (ev) => snap(Math.max(MIN_IMAGE_WIDTH, Math.round(startW + (ev.clientX - startX))));
    const move = (ev) => {
      const w = width(ev);
      view.dispatch(view.state.tr.setMeta(cellImgResizeKey, { pos, width: w }));
      place();
      window.__dwCellResize = { pos, width: w }; // scenario probe
    };
    const up = (ev) => {
      document.removeEventListener('mousemove', move);
      document.removeEventListener('mouseup', up);
      const w = width(ev);
      const fresh = view.state.doc.nodeAt(pos);
      if (fresh && fresh.type.name === 'image')
        view.dispatch(view.state.tr.setNodeMarkup(pos, null, { ...fresh.attrs, width: String(w) }));
      view.dispatch(view.state.tr.setMeta(cellImgResizeKey, { pos, width: null }));
      view.focus();
    };
    document.addEventListener('mousemove', move);
    document.addEventListener('mouseup', up);
  };
  return handle;
}

/* Flatten a parsed doc into a single run of inline nodes, with ONE hard_break between former
   textblocks (no double or trailing breaks). A table cell is one inline line; pasting block
   content (e.g. a <p> from the clipboard) otherwise created a second paragraph — an empty line
   in the editor and a space-joined leading whitespace on save. */
function flattenToInline(doc, schema) {
  const out = [];
  const lastIsBreak = () => out.length && out[out.length - 1].type.name === 'hard_break';
  let firstBlock = true;
  doc.descendants((node) => {
    if (!node.isTextblock) return true; // descend into containers (lists, quotes…)
    if (!firstBlock && !lastIsBreak()) out.push(schema.nodes.hard_break.create());
    firstBlock = false;
    node.content.forEach((n) => out.push(n));
    return false; // already took this textblock's inline content
  });
  while (lastIsBreak()) out.pop();
  return out;
}

/* Parsed fragment with everything that is NOT content removed: ProseMirror editing artifacts
   and DocWiz editor chrome (grips/handles — same set the buildTableModel harvest strips).
   DOM-level, so attribute order and formatting don't matter. */
function contentFragment(html) {
  const t = document.createElement('template');
  t.innerHTML = html || '';
  for (const n of t.content.querySelectorAll(
    'br.ProseMirror-trailingBreak, img.ProseMirror-separator, .e2-colgrip, .e2-fighandle')) n.remove();
  return t.content;
}

/* Normalize whitespace the way ProseMirror does when it parses: collapse runs to a single
   space and drop the leading space right after a <br>. Used ONLY by the LOOSE comparison. */
function normalizeWs(frag) {
  const tw = document.createTreeWalker(frag, NodeFilter.SHOW_TEXT);
  const texts = [];
  while (tw.nextNode()) texts.push(tw.currentNode);
  for (const n of texts) n.nodeValue = n.nodeValue.replace(/\s+/g, ' ');
  for (const br of frag.querySelectorAll('br')) {
    const nx = br.nextSibling;
    if (nx && nx.nodeType === 3) nx.nodeValue = nx.nodeValue.replace(/^ /, '');
  }
  return frag;
}

/* DOM-level equality (attribute ORDER insensitive — string compares produced false locks on
   image cells where ProseMirror emits attributes in a different order).
   LOOSE: also whitespace-insensitive — used by the cell-open LOCK gate so insignificant
   whitespace the editor collapses (e.g. an author's "<br> M:") does not false-lock the cell. */
function htmlEquivalent(a, b) {
  return normalizeWs(contentFragment(a)).isEqualNode(normalizeWs(contentFragment(b)));
}

/* EXACT: attribute-order insensitive but whitespace SENSITIVE — used by the commit no-op check
   so an edit that only normalizes whitespace (e.g. removing the space after a <br>) is still
   saved and sticks, instead of being treated as a no-op and reverted to the backup. */
function htmlEquivalentExact(a, b) {
  return contentFragment(a).isEqualNode(contentFragment(b));
}
