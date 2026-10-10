/* Toolbar icons. doc / arrow-right are verbatim from the design hand-off package;
   the rest are drawn to the same
   spec — 16×16, fill none, stroke currentColor, stroke-width 1.5, square caps.
   Inline SVG only (offline rule); colors come from the button's currentColor. */

const S = '<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="square">';
const SR = '<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">';
const E = '</svg>';

export const ICONS = {
  // From the design hand-off package, verbatim:
  doc: S + '<path d="M4 1.5h5.5L13 5v9.5H4z"></path><path d="M9.5 1.5V5H13"></path>' + E,
  arrowRight: S + '<path d="M2 8h11M9 3.5L13.5 8 9 12.5"></path>' + E,

  // Drawn to the package's spec:
  paragraph: S + '<path d="M9.5 2.5v11M12.5 2.5v11M12.5 2.5H8a3 3 0 0 0 0 6h1.5"></path>' + E,
  bold: S + '<path d="M5 2.5h3.75a2.5 2.5 0 0 1 0 5H5zM5 7.5h4.5a2.75 2.75 0 0 1 0 5.5H5zM5 2.5V13"></path>' + E,
  italic: S + '<path d="M9.5 2.5h4M2.5 13.5h4M11.5 2.5l-3 11"></path>' + E,
  code: S + '<path d="M5 4.5L1.5 8 5 11.5M11 4.5L14.5 8 11 11.5"></path>' + E,
  bulletList: S + '<path d="M6.5 3.5H14M6.5 8H14M6.5 12.5H14"></path><rect x="2" y="2.75" width="1.5" height="1.5" fill="currentColor" stroke="none"></rect><rect x="2" y="7.25" width="1.5" height="1.5" fill="currentColor" stroke="none"></rect><rect x="2" y="11.75" width="1.5" height="1.5" fill="currentColor" stroke="none"></rect>' + E,
  orderedList: S + '<path d="M7 3.5H14M7 8H14M7 12.5H14"></path><path d="M2.5 2.5h1v3M2 5.5h2"></path><path d="M2 9.5h2v-2h-2M2 11.5h2" transform="translate(0,1)"></path>' + E,
  table: S + '<rect x="1.5" y="2.5" width="13" height="11"></rect><path d="M1.5 6h13M6 6v7.5M10.5 6v7.5"></path>' + E,
  blocks: S + '<rect x="1.5" y="1.5" width="5" height="5"></rect><rect x="9.5" y="1.5" width="5" height="5"></rect><rect x="1.5" y="9.5" width="5" height="5"></rect><path d="M12 9.5v5M9.5 12h5"></path>' + E,
  image: S + '<rect x="1.5" y="2.5" width="13" height="11"></rect><path d="M1.5 11l4-4 4.5 4.5M9.5 9.5L12 7l2.5 2.5"></path><circle cx="5.5" cy="5.5" r="0.9" fill="currentColor" stroke="none"></circle>' + E,
  chapterLink: S + '<path d="M12.5 2.5H4v11h9V7"></path><path d="M8 8l6-6M10.5 1.5h4v4"></path>' + E,
  audience: S + '<path d="M1.5 8s2.5-4.5 6.5-4.5S14.5 8 14.5 8 12 12.5 8 12.5 1.5 8 1.5 8z"></path><circle cx="8" cy="8" r="1.9"></circle>' + E,
  strike: S + '<path d="M3 4.5c0-1.5 1.8-2.5 5-2.5 2.5 0 4 .8 4.5 2M3.5 11.5c.5 1.6 2.2 2.5 4.5 2.5 3.2 0 5-1 5-2.8 0-1-.6-1.7-1.8-2.2"></path><path d="M1.5 8h13"></path>' + E,
  clearFormat: S + '<path d="M3 2.5h9M7.5 2.5l-2 8"></path><path d="M10.5 10.5l4 4M14.5 10.5l-4 4"></path><path d="M2.5 13.5h5"></path>' + E,
  weblink: S + '<path d="M6.5 9.5l3-3"></path><path d="M7 5.5l1.5-1.5a2.6 2.6 0 0 1 3.7 3.7L10.5 9.4"></path><path d="M9 10.5l-1.5 1.5a2.6 2.6 0 0 1-3.7-3.7L5.5 6.6"></path>' + E,
  quote: S + '<path d="M3 4.5h4v4H3.7L3 11V4.5zM9 4.5h4v4H9.7L9 11V4.5z"></path>' + E,
  hrule: S + '<path d="M2 8h12"></path><path d="M4 4h8M4 12h8" stroke-dasharray="1.5 2.5"></path>' + E,
  undo: S + '<path d="M5.5 3L2.5 6l3 3"></path><path d="M2.5 6h7a4 4 0 0 1 0 8H6"></path>' + E,
  redo: S + '<path d="M10.5 3l3 3-3 3"></path><path d="M13.5 6h-7a4 4 0 0 0 0 8H10"></path>' + E,
  anchor: S + '<circle cx="8" cy="3" r="1.4"></circle><path d="M8 4.4V14M4.5 7h7"></path><path d="M2.5 9c0 3.4 2.4 5.3 5.5 5.3S13.5 12.4 13.5 9"></path>' + E,
  alignLeft: S + '<path d="M2 3.5h12M2 8h7.5M2 12.5h12"></path>' + E,
  alignCenter: S + '<path d="M2 3.5h12M4.5 8h7M2 12.5h12"></path>' + E,
  alignRight: S + '<path d="M2 3.5h12M6.5 8H14M2 12.5h12"></path>' + E,

  // Phase 3 main-toolbar tools — S family (square caps), matching bulletList/orderedList/align.
  taskList: S + '<path d="M7.5 4H14M7.5 11.5H14"></path><rect x="1.5" y="2" width="4" height="4"></rect><path d="M2.25 4l1 1 1.75-2"></path><rect x="1.5" y="9.5" width="4" height="4"></rect>' + E,
  indent: S + '<path d="M6 3.5H14M6 8H14M6 12.5H14"></path><path d="M1.5 5.5L4 8l-2.5 2.5"></path>' + E,
  outdent: S + '<path d="M6 3.5H14M6 8H14M6 12.5H14"></path><path d="M4 5.5L1.5 8 4 10.5"></path>' + E,

  // Phase 4: contextual toolbar icons (round caps, same 16×16 spec)
  // Pencil — Edit source of a linked block
  pencil: SR + '<path d="M2.5 12.5l1-3L10 3a1.4 1.4 0 0 1 2 2l-6.5 6.5z"></path>' + E,
  // Trash — Delete in context bars
  trash: SR + '<path d="M2.5 4.5h11M6.5 4.5V3h3v1.5M3.5 4.5l.8 8.3a1 1 0 0 0 1 .9h5.4a1 1 0 0 0 1-.9l.8-8.3"></path>' + E,
  // Replace (folder-open) — Replace image
  replace: SR + '<path d="M2 5a.75.75 0 0 1 .75-.75H6l1.5 1.5H13.25a.75.75 0 0 1 .75.75v6.25a.75.75 0 0 1-.75.75H2.75A.75.75 0 0 1 2 13.25z"></path>' + E,
  // Link (chain) — Link button in context bars
  link: SR + '<path d="M6.5 9.5a2.5 2.5 0 0 0 3.5 0l2.5-2.5a2.5 2.5 0 0 0-3.5-3.5l-.7.7"></path><path d="M9.5 6.5a2.5 2.5 0 0 0-3.5 0L3.5 9a2.5 2.5 0 0 0 3.5 3.5l.7-.7"></path>' + E,
  // Plus — Add row/col
  plus: SR + '<path d="M8 3v10M3 8h10"></path>' + E,
  // Minus — Remove row/col
  minus: SR + '<path d="M3 8h10"></path>' + E,
  // Auto widths — table column widths reset
  autowidths: SR + '<path d="M2 4h12M2 12h12M5 4v8M11 4v8"></path>' + E,
  // Focus — expand table full-pane
  tabfocus: SR + '<path d="M3 6.5V4h2.5M13 6.5V4h-2.5M3 9.5V12h2.5M13 9.5V12h-2.5"></path>' + E,

  // Linked-block (embed) context tools:
  // Swap — Change source (re-point the link to a different section)
  swap: S + '<path d="M2.5 5.5h9.5M9.5 3L12 5.5 9.5 8"></path><path d="M13.5 10.5H4M6.5 8L4 10.5 6.5 13"></path>' + E,
  // Unlink (broken chain) — Detach into an editable copy
  unlink: SR + '<path d="M6 10l-1.6 1.6a2.5 2.5 0 0 1-3.5-3.5L2.5 6.5"></path><path d="M10 6l1.6-1.6a2.5 2.5 0 0 1 3.5 3.5L13.5 9.5"></path>' + E,
  // Rotate clockwise (Feather rotate-cw, scaled to the 16×16 spec) — turn a picture 90°
  rotate: SR + '<path d="M13.66 10a6 6 0 1 1-1.41-6.24L15.3 6.7"></path><path d="M15.3 2.7v4h-4"></path>' + E,
  // Box alignment — a hollow box + a guide bar marking the edge it aligns to (L / C / R)
  boxLeft: S + '<path d="M2 3v10"></path><rect x="4" y="4.5" width="6" height="7"></rect>' + E,
  boxCenter: S + '<path d="M2 3v10"></path><path d="M14 3v10"></path><rect x="5" y="4.5" width="6" height="7"></rect>' + E,
  boxRight: S + '<rect x="6" y="4.5" width="6" height="7"></rect><path d="M14 3v10"></path>' + E,

  // ── Table/picture context-bar redesign (Phase 1): the genuinely-missing glyphs.
  // Round-cap context set (SR), matching the plus/minus they replace and the design
  // hand-off spec for the toolbar context menus. place* are NOT
  // added on purpose — image-align + table-place + box-place reuse boxLeft/Center/Right.
  caption: SR + '<rect x="1.75" y="2.5" width="12.5" height="8" rx="1.5"></rect><path d="M3.5 13h9"></path>' + E,
  rowAdd: SR + '<rect x="2.25" y="2.25" width="11.5" height="6.5" rx="1"></rect><path d="M2.25 5.5h11.5"></path><path d="M8 10.25v3.5M6.25 12h3.5"></path>' + E,
  rowAbove: SR + '<path d="M8 2.25v3.5M6.25 4h3.5"></path><rect x="2.25" y="7.25" width="11.5" height="6.5" rx="1"></rect><path d="M2.25 10.5h11.5"></path>' + E,
  rowDel: SR + '<rect x="2.25" y="2.25" width="11.5" height="6.5" rx="1"></rect><path d="M2.25 5.5h11.5"></path><path d="M6.25 12h3.5"></path>' + E,
  colAddL: SR + '<rect x="5.75" y="2.25" width="8" height="11.5" rx="1"></rect><path d="M9.75 2.25v11.5"></path><path d="M2.75 6.25v3.5M1 8h3.5"></path>' + E,
  colAddR: SR + '<rect x="2.25" y="2.25" width="8" height="11.5" rx="1"></rect><path d="M6.25 2.25v11.5"></path><path d="M13.25 6.25v3.5M11.5 8h3.5"></path>' + E,
  colDel: SR + '<rect x="2.25" y="2.25" width="8" height="11.5" rx="1"></rect><path d="M6.25 2.25v11.5"></path><path d="M11.5 8h3.5"></path>' + E,

  // ── Phase 2 (right-click menus): checkmark for checkable items, chevron for submenus. ──
  check: SR + '<path d="M3 8.4l3.2 3.2L13 5"></path>' + E,
  chevronRight: SR + '<path d="M6.5 4L10 8l-3.5 4"></path>' + E,
  group: S + '<rect x="2.5" y="3.5" width="11" height="9" rx="1.5"></rect><path d="M5 6.5h6M5 9.5h4"></path>' + E,
};

/* Returns a detached element for a named icon (innerHTML on a span). */
export function icon(name) {
  const span = document.createElement('span');
  span.className = 'e2-icon';
  span.innerHTML = ICONS[name] || '';
  return span;
}

/* The vertical separator used between tool groups (toolbar, context bar). */
export function sep() {
  const s = document.createElement('span');
  s.className = 'e2-sep';
  return s;
}
