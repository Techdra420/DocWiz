/* list_item node view (Phase 3). A plain item (checked === null) renders as the default
   <li> (dom === contentDOM). A task item renders <li class="task-list-item"> with a real
   <input type=checkbox> (contenteditable=false) before an inner content holder; clicking the
   box toggles the `checked` attr via setNodeMarkup (a node-attr change — byte-surgical save).
   toDOM can't place a sibling before the content hole, which is why this is a node view. */
export class TaskItemView {
  constructor(node, view, getPos) {
    this.view = view;
    this.getPos = getPos;
    this.node = node;
    this.dom = document.createElement('li');
    if (node.attrs.checked === null) {
      this.contentDOM = this.dom;            // plain list item — identical to the default toDOM
      return;
    }
    this.dom.className = 'task-list-item';
    this.box = document.createElement('input');
    this.box.type = 'checkbox';
    this.box.contentEditable = 'false';
    this.box.checked = node.attrs.checked === true;
    this.box.addEventListener('mousedown', (e) => e.preventDefault()); // don't blur the editor selection
    // NOTE: do NOT preventDefault() this click. A checkbox click's "canceled activation steps"
    // revert .checked to its pre-click value AFTER listeners run — so preventDefault would undo
    // the visual tick even though update() set it (the "cannot mark as done" bug). We let the
    // click through and drive the model; update() then reconciles the box to the attr.
    this.box.addEventListener('click', () => {
      const pos = this.getPos();
      if (pos == null) return;
      const cur = this.view.state.doc.nodeAt(pos);
      if (!cur) return;
      this.view.dispatch(this.view.state.tr.setNodeMarkup(pos, undefined, { ...cur.attrs, checked: !(cur.attrs.checked === true) }));
    });
    this.contentDOM = document.createElement('div');
    this.contentDOM.className = 'tli-body';
    this.dom.appendChild(this.box);
    this.dom.appendChild(this.contentDOM);
  }

  update(node) {
    if (node.type !== this.node.type) return false;
    // Structure differs between plain and task items — force re-create when null-ness flips.
    if ((node.attrs.checked === null) !== (this.node.attrs.checked === null)) return false;
    this.node = node;
    if (this.box) this.box.checked = node.attrs.checked === true;
    return true;
  }

  // Clicks on the checkbox are ours, not PM's; everything else is normal editing.
  stopEvent(e) { return this.box ? e.target === this.box : false; }
  ignoreMutation(m) { return this.box ? this.box.contains(m.target) : false; }
}
