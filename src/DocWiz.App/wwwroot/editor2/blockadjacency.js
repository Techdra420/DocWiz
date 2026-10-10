/* Block adjacency helpers (Task 10): insert an empty paragraph adjacent to a block
   node — figure, table, or container — and put the cursor in it. Block atoms and
   block+ containers all suffer "can't place a cursor next to/between them."

   Exposed as adjacencyButtons() so every buildTools() can append the same two
   buttons without duplicating the logic; ADJACENT names them for the bars and the menus. */

import { TextSelection } from 'prosemirror-state';
import { icon } from './icons.js';

export const ADJACENT = {
  before: { label: 'Add a paragraph above', icon: 'paraAbove' },
  after: { label: 'Add a paragraph below', icon: 'paraBelow' },
};

/**
 * Insert an empty paragraph directly before or after the given block node and
 * move the cursor into it.
 * @param {EditorView} view
 * @param {function(): number|null} getPos
 * @param {Node} node  ProseMirror node
 * @param {'before'|'after'} where
 */
export function insertAdjacentParagraph(view, getPos, node, where) {
  const pos = getPos();
  if (pos == null) return;
  const at = where === 'before' ? pos : pos + node.nodeSize;
  const para = view.state.schema.nodes.paragraph.create();
  let tr = view.state.tr.insert(at, para);
  tr = tr.setSelection(TextSelection.create(tr.doc, at + 1));
  view.dispatch(tr.scrollIntoView());
  view.focus();
}

/**
 * Build the two adjacency buttons: borderless `.ib` icon buttons, the context bar's family.
 * mousedown is prevented so a click does not steal the selection.
 *
 * @param {EditorView} view
 * @param {function(): number|null} getPos
 * @param {Node} node  ProseMirror node
 * @returns {HTMLElement[]} Array of two button elements [above, below].
 */
export function adjacencyButtons(view, getPos, node) {
  return ['before', 'after'].map((where) => {
    const b = document.createElement('button');
    b.type = 'button';
    b.className = 'ib';
    b.appendChild(icon(ADJACENT[where].icon));
    b.setAttribute('data-tip', ADJACENT[where].label + ' this block.');
    b.onmousedown = (e) => e.preventDefault();
    b.onclick = (e) => { e.preventDefault(); insertAdjacentParagraph(view, getPos, node, where); };
    return b;
  });
}
