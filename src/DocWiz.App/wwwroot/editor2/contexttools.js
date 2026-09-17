/* contexttools.js — the ONE definition of the picture / table / in-cell-image / linked-block
   context-bar tool strips (ADR-008, docwiz-design rule 21, divergence-audit F1).

   Before this module the four strips were hand-built per node-view in four separate
   buildTools()/showCellImageTools() functions, with five button idioms and drift on badge
   text, Rotate/Link presence, and the box-placement glyph. They now all render from ONE
   shared definition:

   - CONTROL  — one factory per control: the only place a control's DOM, glyph, states and
     tooltip live. A node-view supplies handlers + current state through `ctx`; it never
     hand-builds a button.
   - BADGES   — one node-kind → context-badge map (replaces the per-file 'Image'/'image' and
     'Linked block' literals + the main.js hardcode).
   - LAYOUT   — the ordered, separator-grouped capability list per kind. The per-context
     differences live here as data, never as divergent hand-built lists.
   - OMIT_REASONS — every capability a kind does NOT have carries a one-line reason, so a
     future maintainer can tell "deliberately omitted" from "forgot" (the F1 finding).
   - auditCapabilities() — the parity gate: each kind's capability set == the vocabulary
     minus its declared (reasoned) omissions. Exposed on window for the `capabilityparity`
     scenario gate; also logged at module load.

   All four node-views call buildStrip(kind, ctx); the only divergence allowed is a declared
   flag with a reason. */

import { icon, sep } from './icons.js';
import { makeIbButton, buildPxInput } from './toolhelpers.js';
import { adjacencyButtons, insertAdjacentParagraph } from './blockadjacency.js';

/* The node-kind → context badge map (one source; main.js renders it). */
export const BADGES = {
  image: { icon: 'image', label: 'IMAGE' },
  table: { icon: 'table', label: 'TABLE' },
  embed: { icon: 'link', label: 'LINKED' },
};

/* The full capability vocabulary. Every per-kind layout is a subset of this; every
   absence must be documented in OMIT_REASONS (enforced by auditCapabilities). */
export const ALL = [
  'size', 'px', 'place', 'cellAlign', 'rows', 'cols',
  'replace', 'link', 'rotate', 'editSource', 'changeSource', 'detach',
  'autowidths', 'focus', 'captionToggle', 'captionField', 'altField',
  'adjacency', 'delete',
];

/* Ordered, separator-grouped capability lists. Each inner array is one hairline-separated
   group; the order is the canonical left→right order from the design handoff. */
const LAYOUT = {
  image:     [['size', 'px'], ['place'], ['replace', 'link', 'rotate'], ['captionToggle'], ['captionField'], ['adjacency'], ['delete']],
  cellImage: [['size', 'px'], ['place'], ['replace', 'rotate'], ['altField'], ['delete']],
  table:     [['rows', 'cols'], ['cellAlign'], ['place'], ['autowidths', 'link', 'focus'], ['captionField'], ['adjacency'], ['delete']],
  box:       [['editSource', 'changeSource', 'detach'], ['place'], ['delete']],
};

/* One-line reason for every capability a kind deliberately lacks (the F1 "defensible yet
   undocumented" fix). auditCapabilities asserts this covers exactly the absent caps. */
const OMIT_REASONS = {
  image: {
    cellAlign: 'no cells — alignment is whole-image placement (place)',
    rows: 'an image is not a table', cols: 'an image is not a table',
    autowidths: 'an image is not a table', focus: 'an image is not a table',
    editSource: 'not a linked block', changeSource: 'not a linked block', detach: 'not a linked block',
    altField: 'the caption field doubles as the image description (alt)',
  },
  cellImage: {
    link: 'an inline image has no stable #anchor to copy',
    captionToggle: 'a cell image has no figure caption', captionField: 'a cell image has no figure caption (alt only)',
    adjacency: 'an inline image is not a block — no block-adjacency',
    cellAlign: 'aligns itself within the cell via place, not a column',
    rows: 'an image is not a table', cols: 'an image is not a table',
    autowidths: 'an image is not a table', focus: 'an image is not a table',
    editSource: 'not a linked block', changeSource: 'not a linked block', detach: 'not a linked block',
  },
  table: {
    size: 'a table is not a raster image', px: 'a table is sized by box-width drag, not a px field',
    replace: 'a table has no single source file to replace', rotate: 'a table is not a raster image',
    captionToggle: 'a table caption is the field itself (always offered)',
    altField: 'a table uses a caption, not alt text',
    editSource: 'not a linked block', changeSource: 'not a linked block', detach: 'not a linked block',
  },
  box: {
    size: "the linked content is owned by the source chapter", px: 'the linked content is owned by the source chapter',
    cellAlign: 'not a table', rows: 'not a table', cols: 'not a table',
    autowidths: 'not a table', focus: 'not a table',
    replace: 'use Change source to re-point the link', rotate: 'the linked content is owned by the source chapter',
    link: 'it already IS a link',
    captionToggle: 'the caption is owned by the source chapter', captionField: 'the caption is owned by the source chapter',
    altField: 'the description is owned by the source chapter',
    adjacency: 'a linked block lives on a slide zone, not in chapter flow',
  },
};

/* ── shared button builder: one borderless icon .ib button ───────────────────────────── */
function ibIcon(iconName, tip, onClick, opts = {}) {
  const b = document.createElement('button');
  b.type = 'button';
  b.className = 'ib' + (opts.danger ? ' e2-ib-danger' : '') + (opts.active ? ' active' : '');
  b.appendChild(icon(iconName));
  b.setAttribute('data-tip', tip);
  b.onmousedown = (e) => e.preventDefault();
  b.onclick = (e) => { e.preventDefault(); onClick(e); };
  return b;
}

/* Wrap one capability's element(s) in a group span carrying its data-cap (for the audit and
   for 2px-gap visual grouping). */
function cap(name, ...els) {
  const g = document.createElement('span');
  g.className = 'e2-grp';
  g.dataset.cap = name;
  for (const e of els) g.appendChild(e);
  return g;
}

/* A flex text input (caption / alt) — the only slack-absorbing item in the row. */
function fieldInput(value, placeholder, tip, onCommit) {
  const input = document.createElement('input');
  input.type = 'text';
  input.className = 'e2-figcap';
  input.placeholder = placeholder;
  input.value = value || '';
  input.setAttribute('data-tip', tip);
  input.onchange = () => onCommit(input.value);
  input.onkeydown = (e) => { if (e.key === 'Enter') { e.preventDefault(); input.blur(); } };
  input.onmousedown = (e) => e.stopPropagation(); // click-into-field must not deselect the node
  return input;
}

/* ── the control vocabulary: one factory per capability ──────────────────────────────── */
const CONTROL = {
  size(ctx) {
    const set = document.createElement('span');
    set.className = 'e2-grp e2-sizeset';
    for (const [label, px] of [...ctx.sizeScale, ['1:1', '']]) {
      const w = String(px);
      const b = makeIbButton(label, w === '' ? 'Original size (1:1).' : label + ' — ' + w + ' px wide.', () => ctx.onSize(w || null));
      if ((ctx.width || '') === w) b.classList.add('active');
      set.appendChild(b);
    }
    return set;
  },
  px(ctx) {
    const input = buildPxInput(ctx.width, ctx.onPx);
    return input;
  },
  // Block-on-page placement (box glyphs) — image align, table place, box place all share it.
  // Image default is CENTER (no class); table/box default is LEFT (null). placeKind selects.
  place(ctx) {
    const isImage = ctx.placeKind === 'image';
    const a = ctx.align || null;
    const active = {
      left: isImage ? a === 'left' : (a !== 'center' && a !== 'right'),
      center: isImage ? (a === 'center' || !a) : a === 'center',
      right: a === 'right',
    };
    const leftVal = isImage ? 'left' : null;
    const t = ctx.placeTips;
    return cap('place',
      ibIcon('boxLeft', t.left, () => ctx.onPlace(leftVal), { active: active.left }),
      ibIcon('boxCenter', t.center, () => ctx.onPlace('center'), { active: active.center }),
      ibIcon('boxRight', t.right, () => ctx.onPlace('right'), { active: active.right }));
  },
  // Text-line alignment of the active cell's whole column (table cells only).
  cellAlign(ctx) {
    return cap('cellAlign',
      ibIcon('alignLeft', 'Align the selected cell’s column left (default).', () => ctx.onCellAlign(null)),
      ibIcon('alignCenter', 'Center the selected cell’s column.', () => ctx.onCellAlign('center')),
      ibIcon('alignRight', 'Right-align the selected cell’s column.', () => ctx.onCellAlign('right')));
  },
  rows(ctx) {
    return cap('rows',
      ibIcon('rowAdd', 'Insert a row below the selected cell (or at the end).', ctx.onRowAdd),
      ibIcon('rowDel', 'Delete the row of the selected cell.', ctx.onRowDel));
  },
  cols(ctx) {
    return cap('cols',
      ibIcon('colAddL', 'Insert a column left of the selected cell.', ctx.onColAddLeft),
      ibIcon('colAddR', 'Insert a column right of the selected cell (or at the end).', ctx.onColAddRight),
      ibIcon('colDel', 'Delete the column of the selected cell.', ctx.onColDel));
  },
  replace(ctx) {
    const b = ibIcon('replace', 'Replace this picture — pick another from the project’s assets.', ctx.onReplace);
    b.dataset.cap = 'replace';
    return b;
  },
  link(ctx) {
    const b = ibIcon('link', ctx.linkTip || 'Copy this object’s link — paste it into any text or a view.', ctx.onLink);
    b.dataset.cap = 'link';
    return b;
  },
  rotate(ctx) {
    const b = ibIcon('rotate', 'Rotate this picture 90° clockwise (rotates the image file; the original is kept — click four times to return to the start).', ctx.onRotate);
    b.dataset.cap = 'rotate';
    return b;
  },
  editSource(ctx) {
    const b = ibIcon('pencil', 'Edit source — open the linked chapter to edit the original; changes flow back here automatically.', ctx.onEditSource);
    b.dataset.cap = 'editSource';
    return b;
  },
  changeSource(ctx) {
    const b = ibIcon('swap', 'Change source — point this linked block at a different chapter or section (it stays in sync with the new one).', ctx.onChangeSource);
    b.dataset.cap = 'changeSource';
    return b;
  },
  detach(ctx) {
    const b = ibIcon('unlink', 'Detach — turn this into your own editable copy. Future chapter edits will no longer update it.', ctx.onDetach);
    b.dataset.cap = 'detach';
    return b;
  },
  autowidths(ctx) {
    const b = ibIcon('autowidths', 'Auto column widths — remove fixed widths so the handover lays the table out automatically.', ctx.onAutoWidths, { active: !!ctx.autoActive });
    b.dataset.cap = 'autowidths';
    return b;
  },
  focus(ctx) {
    const b = ibIcon('tabfocus', 'Focus this table — expand it to the whole pane for a big editing session (edit-time only; the page is unchanged). Esc returns.', ctx.onFocus, { active: !!ctx.focusActive });
    b.dataset.cap = 'focus';
    return b;
  },
  captionToggle(ctx) {
    const b = ibIcon('caption', 'Show or hide the caption (the description is always kept for accessibility).', ctx.onCaptionToggle, { active: ctx.showCaption !== false });
    b.dataset.cap = 'captionToggle';
    return b;
  },
  captionField(ctx) {
    const input = fieldInput(ctx.captionValue, ctx.captionPlaceholder || 'Caption — shown under the figure',
      ctx.captionTip || 'The caption shown under this object.', ctx.onCaption);
    input.dataset.cap = 'captionField';
    return input;
  },
  altField(ctx) {
    const input = fieldInput(ctx.altValue, 'Description (alt) — not shown as a caption in cells',
      'Image description for screen readers (alt text) — not shown as a caption inside table cells.', ctx.onAlt);
    input.dataset.cap = 'altField';
    return input;
  },
  adjacency(ctx) {
    return cap('adjacency', ...adjacencyButtons(makeIbButton, ctx.view, ctx.getPos, ctx.node));
  },
  delete(ctx) {
    const b = ibIcon('trash', ctx.deleteTip || 'Delete this object (Ctrl+Z brings it back).', ctx.onDelete, { danger: true });
    b.dataset.cap = 'delete';
    return b;
  },
};

/* Build a context-bar tool strip for `kind` from the shared LAYOUT, wiring each control to
   the node-view's handlers/state in `ctx`. The ONLY entry point — node-views never assemble
   their own button list. */
export function buildStrip(kind, ctx) {
  const tools = document.createElement('div');
  tools.className = 'e2-tb-tools';
  tools.dataset.kind = kind;
  const groups = LAYOUT[kind];
  if (!groups) throw new Error('contexttools: unknown kind ' + kind);
  groups.forEach((grp, gi) => {
    if (gi > 0) tools.appendChild(sep());
    for (const name of grp) {
      const factory = CONTROL[name];
      if (!factory) throw new Error('contexttools: no control factory for ' + name);
      tools.appendChild(factory(ctx));
    }
  });
  if (ctx.hint) {
    const h = document.createElement('span');
    h.className = 'e2-tb-hint';
    h.innerHTML = ctx.hint;
    tools.appendChild(h);
  }
  return tools;
}

/* ──────────────────────────────────────────────────────────────────────────────────────
   Right-click context menus (Phase 2). The menu is a SECOND presentation of the SAME
   capability model: every menu item runs the node-view's own `ctx` handler (so the bar and
   the menu can't drift on behaviour), the inline Size/Align/Place sets reuse the bar's CONTROL
   factories, and the icon set + state language are shared. MENU_LAYOUT declares which of the
   kind's capabilities appear, in order; auditCapabilities checks every menu item is a real
   capability of that kind. The menu is intentionally richer where it is positional (insert row
   above/below, insert column left/right) — those map to extra ctx handlers the bar omits.
   ────────────────────────────────────────────────────────────────────────────────────── */

const MENU_LAYOUT = {
  image: ['size', 'place', 'sep', 'replace', 'link', 'rotate', 'sep', 'captionToggle', 'sep', 'adjacency', 'sep', 'delete'],
  table: ['rows', 'cols', 'sep', 'cellAlign', 'place', 'autowidths', 'sep', 'link', 'focus', 'sep', 'adjacency', 'sep', 'delete'],
};

function menuLabel(text) {
  const el = document.createElement('div');
  el.className = 'mlabel';
  el.textContent = text;
  return el;
}
function menuSep() {
  const el = document.createElement('div');
  el.className = 'msep';
  return el;
}
export function menuItem(iconName, label, onClick, opts = {}) {
  const el = document.createElement('div');
  el.className = 'mi' + (opts.danger ? ' danger' : '') + (opts.disabled ? ' disabled' : '');
  if (iconName) el.appendChild(icon(iconName));
  const lbl = document.createElement('span');
  lbl.className = 'mlbl';
  lbl.textContent = label;
  el.appendChild(lbl);
  if (opts.key) {
    const k = document.createElement('span');
    k.className = 'mkey';
    k.textContent = opts.key;
    el.appendChild(k);
  }
  if (!opts.disabled) {
    el.addEventListener('mousedown', (e) => e.preventDefault()); // keep the node selected
    el.addEventListener('click', (e) => { e.preventDefault(); onClick(); });
  }
  return el;
}
function menuCheck(iconName, label, on, onClick) {
  const el = menuItem(iconName, label, onClick);
  if (on) {
    el.classList.add('on');
    const chk = icon('check');
    chk.classList.add('mcheck');
    el.appendChild(chk);
  }
  return el;
}
function menuInlineSet(label, controlEl) {
  const row = document.createElement('div');
  row.className = 'mrow';
  const lbl = document.createElement('span');
  lbl.className = 'mlbl';
  lbl.textContent = label;
  row.appendChild(lbl);
  row.appendChild(controlEl);
  return row;
}
function menuSubmenu(iconName, label, subItems) {
  const el = document.createElement('div');
  el.className = 'mi has-sub';
  el.appendChild(icon(iconName));
  const lbl = document.createElement('span');
  lbl.className = 'mlbl';
  lbl.textContent = label;
  el.appendChild(lbl);
  const chev = icon('chevronRight');
  chev.classList.add('mchev');
  el.appendChild(chev);
  const sub = document.createElement('div');
  sub.className = 'submenu';
  for (const it of subItems) sub.appendChild(it);
  el.appendChild(sub);
  el.addEventListener('mouseenter', () => {
    el.classList.add('open');
    const r = sub.getBoundingClientRect();
    if (r.right > window.innerWidth - 8) el.classList.add('flip'); // flip near the right edge
  });
  el.addEventListener('mouseleave', () => el.classList.remove('open', 'flip'));
  return el;
}

/* One renderer per capability — the menu's presentation of that capability, wired to the
   shared ctx handler. (Field capabilities px/captionField/altField have no menu form: they are
   inline editors, not menu commands — so they are absent from MENU_LAYOUT by construction.) */
const MENU_RENDER = {
  size: (kind, ctx) => menuInlineSet('Size', CONTROL.size(ctx)),
  place: (kind, ctx) => menuInlineSet(kind === 'table' ? 'Place table' : 'Align', CONTROL.place(ctx)),
  cellAlign: (kind, ctx) => menuInlineSet('Align cells', CONTROL.cellAlign(ctx)),
  replace: (kind, ctx) => menuItem('replace', 'Replace image…', ctx.onReplace),
  link: (kind, ctx) => menuItem('link', 'Copy link', ctx.onLink),
  rotate: (kind, ctx) => menuItem('rotate', 'Rotate 90°', ctx.onRotate),
  captionToggle: (kind, ctx) => menuCheck('caption', 'Show caption', ctx.showCaption !== false, ctx.onCaptionToggle),
  autowidths: (kind, ctx) => menuCheck('autowidths', 'Auto column widths', !!ctx.autoActive, ctx.onAutoWidths),
  focus: (kind, ctx) => menuCheck('tabfocus', 'Focus this table', !!ctx.focusActive, ctx.onFocus),
  rows: (kind, ctx) => menuSubmenu('rowAdd', 'Row', [
    menuItem('rowAbove', 'Insert row above', ctx.onRowInsertAbove),
    menuItem('rowAdd', 'Insert row below', ctx.onRowInsertBelow),
    menuSep(),
    menuItem('rowDel', 'Delete row', ctx.onRowDel, { danger: true }),
  ]),
  cols: (kind, ctx) => menuSubmenu('colAddR', 'Column', [
    menuItem('colAddL', 'Insert column left', ctx.onColAddLeft),
    menuItem('colAddR', 'Insert column right', ctx.onColAddRight),
    menuSep(),
    menuItem('colDel', 'Delete column', ctx.onColDel, { danger: true }),
  ]),
  adjacency: (kind, ctx) => [
    menuItem('paragraph', 'Insert paragraph above', () => insertAdjacentParagraph(ctx.view, ctx.getPos, ctx.node, 'before')),
    menuItem('paragraph', 'Insert paragraph below', () => insertAdjacentParagraph(ctx.view, ctx.getPos, ctx.node, 'after')),
  ],
  delete: (kind, ctx) => menuItem('trash', kind === 'table' ? 'Delete table' : 'Delete image', ctx.onDelete, { danger: true, key: 'Del' }),
};

/* Build the right-click menu for `kind` from MENU_LAYOUT, wiring each item to `ctx`. */
export function buildMenu(kind, ctx) {
  const menu = document.createElement('div');
  menu.className = 'e2-menu';
  menu.dataset.kind = kind;
  const badge = BADGES[kind];
  menu.appendChild(menuLabel(badge ? badge.label : kind));
  for (const entry of MENU_LAYOUT[kind] || []) {
    if (entry === 'sep') { menu.appendChild(menuSep()); continue; }
    const render = MENU_RENDER[entry];
    if (!render) continue;
    const out = render(kind, ctx);
    for (const el of Array.isArray(out) ? out : [out]) menu.appendChild(el);
  }
  return menu;
}

/* Open a menu element at viewport coords (x, y), clamped 8px inside the viewport. Closes on
   outside-mousedown / Esc / scroll(capture) / an item click. Only one menu open at a time. */
export function openContextMenu(menu, x, y) {
  closeOpenMenu();
  menu.style.position = 'fixed';
  menu.style.left = '0px';
  menu.style.top = '0px';
  document.body.appendChild(menu);
  const r = menu.getBoundingClientRect();
  const left = Math.max(8, Math.min(x, window.innerWidth - r.width - 8));
  const top = Math.max(8, Math.min(y, window.innerHeight - r.height - 8));
  menu.style.left = left + 'px';
  menu.style.top = top + 'px';

  const close = () => {
    if (menu.parentNode) menu.parentNode.removeChild(menu);
    document.removeEventListener('mousedown', onDown, true);
    document.removeEventListener('keydown', onKey, true);
    document.removeEventListener('scroll', close, true);
    if (openMenuClose === close) openMenuClose = null;
  };
  const onDown = (e) => { if (!menu.contains(e.target)) close(); };
  const onKey = (e) => { if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); close(); } };
  // Defer the outside-mousedown listener so the very contextmenu that opened this menu
  // (and any synthetic mousedown around it) doesn't immediately close it.
  setTimeout(() => {
    document.addEventListener('mousedown', onDown, true);
    document.addEventListener('keydown', onKey, true);
    document.addEventListener('scroll', close, true);
  }, 0);
  // Close after any actionable item click — but NOT a submenu parent (it opens on hover).
  menu.addEventListener('click', (e) => {
    const item = e.target.closest('.mi, .ib');
    if (!item || item.classList.contains('has-sub') || item.classList.contains('disabled')) return;
    close();
  });
  openMenuClose = close;
  menu._dwClose = close;
  return close;
}

let openMenuClose = null;
function closeOpenMenu() { if (openMenuClose) openMenuClose(); }

/* The ADR-008 parity gate: for every kind, its capability set is exactly the vocabulary
   minus its declared (reasoned) omissions, every present cap is a real vocabulary cap with a
   factory, and no omission reason is stale. Returns { ok, problems }. */
export function auditCapabilities() {
  const problems = [];
  for (const kind of Object.keys(LAYOUT)) {
    const present = new Set(LAYOUT[kind].flat());
    for (const c of present) {
      if (!ALL.includes(c)) problems.push(kind + ": '" + c + "' is not in the capability vocabulary");
      if (!CONTROL[c]) problems.push(kind + ": '" + c + "' has no control factory");
    }
    const reasons = OMIT_REASONS[kind] || {};
    for (const c of ALL) {
      if (present.has(c)) {
        if (c in reasons) problems.push(kind + ": '" + c + "' is present but also carries an omit reason");
      } else if (!reasons[c]) {
        problems.push(kind + ": '" + c + "' is omitted without a documented reason");
      }
    }
    for (const c of Object.keys(reasons)) {
      if (!ALL.includes(c)) problems.push(kind + ": omit reason for unknown capability '" + c + "'");
    }
  }
  // Menu coverage: every right-click menu item must be a real capability of its kind with a
  // renderer — so the menu can never offer (or drift toward) something the bar's kind can't do.
  for (const kind of Object.keys(MENU_LAYOUT)) {
    const caps = new Set(LAYOUT[kind].flat());
    for (const entry of MENU_LAYOUT[kind]) {
      if (entry === 'sep') continue;
      if (!caps.has(entry)) problems.push("menu[" + kind + "]: '" + entry + "' is not a capability of " + kind);
      if (!MENU_RENDER[entry]) problems.push("menu[" + kind + "]: '" + entry + "' has no menu renderer");
    }
  }
  return { ok: problems.length === 0, problems };
}

/* Run the gate at module load: expose the result for the scenario gate; log (never throw —
   a registry slip must not brick the editor for an end user) if it fails. */
const __audit = auditCapabilities();
if (typeof window !== 'undefined') {
  window.DocWizContextTools = { BADGES, ALL, LAYOUT, MENU_LAYOUT, buildMenu, openContextMenu, auditCapabilities, audit: __audit };
  if (!__audit.ok) console.error('[DocWiz] context-tools capability parity FAILED:', __audit.problems);
}
