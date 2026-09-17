/* Block adjacency helpers (Task 10): insert an empty paragraph adjacent to a block
   node — figure, table, or container — and put the cursor in it. Block atoms and
   block+ containers all suffer "can't place a cursor next to/between them."

   Exposed as adjacencyButtons() so every buildTools() can append the same two
   buttons without duplicating the logic. */

import { TextSelection } from 'prosemirror-state';

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
 * Build the two adjacency buttons (¶↑ / ¶↓) using a caller-supplied button
 * factory so each node view can use its own button style.
 *
 * @param {function(label:string, tip:string, onclick:function): HTMLElement} mk
 *   The caller's button factory.
 * @param {EditorView} view
 * @param {function(): number|null} getPos
 * @param {Node} node  ProseMirror node
 * @returns {HTMLElement[]} Array of two button elements [above, below].
 */
export function adjacencyButtons(mk, view, getPos, node) {
  return [
    mk('¶↑', 'Add a paragraph above this block', () => insertAdjacentParagraph(view, getPos, node, 'before')),
    mk('¶↓', 'Add a paragraph below this block', () => insertAdjacentParagraph(view, getPos, node, 'after')),
  ];
}
