/* Node views for the live surface. They emit the exact markup the pipeline emits
   (.dw-callout/.aud/.dw-details classes + chrome), so the @scope'd site.css renders
   the surface like the built page. Chrome is contentEditable=false and lives outside
   contentDOM; ProseMirror only manages the content wrapper. Clicking the chrome pill
   selects the container — its tools appear in the fixed context bar (M4). */
import { NodeSelection, TextSelection } from 'prosemirror-state';
import { CALLOUTS, DEFAULT_LABEL } from './schema.js';
import { TableView } from './tableview.js';
import { FigureView } from './figureview.js';
import { TaskItemView } from './taskitemview.js';
import { nodesFromSegments } from './ingest.js';
import { sep } from './icons.js';
import { trackWidthDrag } from './boxresize.js';
import { adjacencyButtons } from './blockadjacency.js';
import { buildStrip, menuItem, openContextMenu } from './contexttools.js';

// Callout labels (note/tip/warning/danger) come from DEFAULT_LABEL — ONE source (ADR-008),
// so the context-bar pill matches the callout chrome tag and can't drift to the raw type.
const CONTAINER_LABEL = {
  ...DEFAULT_LABEL,
  audience: 'Audience block', details: 'Collapsible', steps: 'Procedure steps',
  cards: 'Navigation cards', buttons: 'Button row', center: 'Centered', right: 'Right-aligned',
  group: 'Group',
};

// The audience block's tag — the same words the pipeline writes (AudienceTag.Label in
// AudienceContainerRenderer.cs; pinned by AudienceTagParityTests): titles, ids as fallback.
export function audienceTagLabel(args, audiences) {
  const titles = args.split(/\s+/).filter(Boolean).map((id) => {
    const a = (audiences || []).find((x) => x.id === id);
    return a && a.title ? a.title : id;
  });
  return 'Only for ' + titles.join(', ');
}

class ContainerView {
  constructor(node, view, getPos, ui) {
    this.node = node;
    this.view = view;
    this.getPos = getPos;
    this.ui = ui;
    this.selected = false;
    const { type, args, label } = node.attrs;

    const pill = (el) => {
      el.contentEditable = 'false';
      el.style.cursor = 'pointer';
      el.setAttribute('data-tip', 'Click to select this block — its tools appear under the banner (change type, label, audiences, unwrap, delete).');
      // preventDefault on mousedown (no caret placement), select on CLICK — the
      // separated phases avoid fighting ProseMirror's own mousedown pipeline,
      // which made pill selection feel like it needed several clicks.
      el.addEventListener('mousedown', (e) => { e.preventDefault(); e.stopPropagation(); });
      el.addEventListener('click', (e) => {
        e.preventDefault();
        e.stopPropagation();
        const pos = this.getPos();
        if (pos == null) return;
        this.view.dispatch(this.view.state.tr.setSelection(NodeSelection.create(this.view.state.doc, pos)));
        this.view.focus();
      });
      return el;
    };

    if (type === 'details') {
      this.dom = element('details', 'dw-details');
      const summary = document.createElement('summary');
      summary.textContent = label || 'Details';
      pill(summary);
      this.dom.appendChild(summary);
      this.dom.open = false;
      this.contentDOM = this.dom.appendChild(document.createElement('div'));
      return;
    }

    if (type === 'audience') {
      this.dom = element('div', 'aud');
      this.dom.setAttribute('data-aud', args);
      const tag = element('span', 'aud-tag');
      tag.textContent = audienceTagLabel(args, ui && ui.audiences);
      this.dom.appendChild(pill(tag));
      this.contentDOM = this.dom.appendChild(document.createElement('div'));
      return;
    }

    if (CALLOUTS.includes(type)) {
      this.dom = element('div', 'dw-callout dw-' + type);
      const tag = element('span', 'dw-callout-tag');
      tag.textContent = label || DEFAULT_LABEL[type];
      this.dom.appendChild(pill(tag));
      this.contentDOM = this.dom.appendChild(document.createElement('div'));
      return;
    }

    if (type === 'group') {
      this.dom = element('div', 'dw-group e2-group');
      if (node.attrs.anchor) this.dom.id = node.attrs.anchor;
      const banner = element('span', 'e2-group-banner');
      banner.textContent = label || 'Group';
      if (node.attrs.desc) { const d = element('span', 'e2-group-desc'); d.textContent = node.attrs.desc; banner.appendChild(d); }
      this.dom.appendChild(pill(banner));
      this.contentDOM = this.dom.appendChild(document.createElement('div'));
      this.dom.addEventListener('contextmenu', (e) => this.onGroupContextMenu(e));
      return;
    }

    // steps / cards / buttons / center / right: pure wrappers — a slim grab handle
    // on the left edge stands in for the missing chrome.
    // center/right omit the pill: the visual alignment is self-explanatory and the
    // badge was flagged as clutter (stakeholder feedback 2026-06-13).
    const cls = type === 'center' || type === 'right' ? 'dw-align-' + type : 'dw-' + type;
    this.dom = element('div', cls);
    if (type !== 'center' && type !== 'right') {
      const grab = element('span', 'e2-blockpill');
      grab.textContent = CONTAINER_LABEL[type] || type;
      this.dom.appendChild(pill(grab));
    }
    this.contentDOM = this.dom.appendChild(document.createElement('div'));
  }

  selectNode() {
    this.selected = true;
    this.dom.classList.add('e2-active');
    if (this.ui) this.ui.set(this.buildTools(), CONTAINER_LABEL[this.node.attrs.type] || this.node.attrs.type);
  }

  deselectNode() {
    this.selected = false;
    this.dom.classList.remove('e2-active');
    if (this.ui) this.ui.set(null);
  }

  patchAttrs(changes) {
    const pos = this.getPos();
    if (pos == null) return;
    this.view.dispatch(this.view.state.tr.setNodeMarkup(pos, null, { ...this.node.attrs, ...changes }));
    this.view.focus();
  }

  buildTools() {
    const { type, args, label, desc } = this.node.attrs;
    // ContainerView (callout/audience/details/center-right) is NOT one of the four shared
    // picture/box/table strips, but it lives in the same context bar, so its buttons use the
    // same borderless .ib family (the redesign retired the bordered .e2-tools button idiom).
    const tools = document.createElement('div');
    tools.className = 'e2-tb-tools';
    const add = (text, tip, fn, active) => {
      const b = document.createElement('button');
      b.type = 'button';
      b.className = 'ib' + (active ? ' active' : '');
      b.textContent = text;
      b.setAttribute('data-tip', tip);
      b.onmousedown = (e) => e.preventDefault();
      b.onclick = (e) => { e.preventDefault(); fn(); };
      tools.appendChild(b);
      return b;
    };

    if (CALLOUTS.includes(type)) {
      for (const kind of CALLOUTS)
        add(DEFAULT_LABEL[kind], 'Turn this callout into a ' + kind + '.', () => this.patchAttrs({ type: kind }), type === kind);
      tools.appendChild(sep());
      const labelInput = document.createElement('input');
      labelInput.className = 'e2-figcap';
      labelInput.placeholder = DEFAULT_LABEL[type] + ' (default label)';
      labelInput.value = label || '';
      labelInput.setAttribute('data-tip', 'Custom label on the callout, e.g. Hot surface. Empty = the default.');
      labelInput.onchange = () => this.patchAttrs({ label: labelInput.value.trim() || null });
      labelInput.onkeydown = (e) => { if (e.key === 'Enter') { e.preventDefault(); labelInput.blur(); } };
      tools.appendChild(labelInput);
    } else if (type === 'audience') {
      // The shared Readers picker (app.js): change who, or "Everyone" to remove the tag.
      const who = add(audienceTagLabel(args, this.ui && this.ui.audiences).replace(/^Only for /, 'Readers: '),
        'Who reads this block. Everyone removes the tag and keeps the content.', () => {
          const b = window.DocWizBridge;
          if (b && b.blockReaders) b.blockReaders(who);
        });
    } else if (type === 'details') {
      const labelInput = document.createElement('input');
      labelInput.className = 'e2-figcap';
      labelInput.placeholder = 'Details (summary text)';
      labelInput.value = label || '';
      labelInput.setAttribute('data-tip', 'The clickable summary line of the collapsible section.');
      labelInput.onchange = () => this.patchAttrs({ label: labelInput.value.trim() || null });
      labelInput.onkeydown = (e) => { if (e.key === 'Enter') { e.preventDefault(); labelInput.blur(); } };
      tools.appendChild(labelInput);
    } else if (type === 'group') {
      const nameInput = document.createElement('input');
      nameInput.className = 'e2-figcap';
      nameInput.placeholder = 'Group name (shown in the embed picker)';
      nameInput.value = label || '';
      nameInput.setAttribute('data-tip', 'A friendly name for this group — what you pick it by when embedding it on a slide.');
      nameInput.onchange = () => this.patchAttrs({ label: nameInput.value.trim() || null });
      nameInput.onkeydown = (e) => { if (e.key === 'Enter') { e.preventDefault(); nameInput.blur(); } };
      tools.appendChild(nameInput);
      const descInput = document.createElement('input');
      descInput.className = 'e2-figcap';
      descInput.placeholder = 'Description (a note to you — never shown to readers)';
      descInput.value = desc || '';
      descInput.setAttribute('data-tip', 'An author note about what this group is for. Authoring-only; it is not shown in the handover.');
      descInput.onchange = () => this.patchAttrs({ desc: descInput.value.trim() || null });
      descInput.onkeydown = (e) => { if (e.key === 'Enter') { e.preventDefault(); descInput.blur(); } };
      tools.appendChild(descInput);
      if (this.node.attrs.anchor) {
        tools.appendChild(sep());
        add('Copy link', 'Copy this group’s dw: link to paste into a slide or chapter link.', () => {
          const ref = 'dw:' + ((this.ui && this.ui.chapterId) || '') + '#' + this.node.attrs.anchor;
          if (navigator.clipboard) navigator.clipboard.writeText(ref);
          if (this.ui && this.ui.flash) this.ui.flash('Copied ' + ref);
        });
      }
    } else if (type === 'center' || type === 'right') {
      add('↔ Center', 'Center the content.', () => this.patchAttrs({ type: 'center' }), type === 'center');
      add('⇥ Right', 'Right-align the content.', () => this.patchAttrs({ type: 'right' }), type === 'right');
    }

    tools.appendChild(sep());
    // Link… (Task 9): swap this whole block for a live-linked chapter section. Only
    // offered on the slide canvas, where relinkSelection is wired (the chapter editor
    // has no embeds). The block is already NodeSelected here, so relink targets it.
    const E = window.DocWizEditor2;
    if (E && typeof E.relinkSelection === 'function' && this.ui && this.ui.audience !== undefined) {
      add('Link…', 'Replace this block with a live link to a chapter section (it stays in sync).', () => E.relinkSelection());
    }
    add('Unwrap', 'Remove the block wrapper but keep its content in place.', () => {
      const pos = this.getPos();
      if (pos == null) return;
      const node = this.node;
      this.view.dispatch(this.view.state.tr.replaceWith(pos, pos + node.nodeSize, node.content));
      this.view.focus();
    });
    // ── Add paragraph above / below (Task 10) ────────────────────────────────
    // `add` already appends to tools; adjacencyButtons calls mk() which returns+appends.
    adjacencyButtons(add, this.view, this.getPos, this.node);
    add('Delete', 'Delete this block INCLUDING its content.', () => {
      const pos = this.getPos();
      if (pos == null) return;
      this.view.dispatch(this.view.state.tr.delete(pos, pos + this.node.nodeSize));
      this.view.focus();
    }).classList.add('e2-ib-danger');
    const hint = document.createElement('span');
    hint.className = 'e2-ctx-hint';
    hint.textContent = 'Esc deselects';
    tools.appendChild(hint);
    return tools;
  }

  onGroupContextMenu(e) {
    e.preventDefault();
    e.stopPropagation();
    const menu = element('div', 'e2-menu');
    menu.appendChild(menuItem(null, 'Edit name & description…', () => {
      const b = window.DocWizBridge;
      if (b && b.groupEdit) b.groupEdit({ label: this.node.attrs.label, desc: this.node.attrs.desc },
        (name, desc) => this.patchAttrs({ label: name || null, desc: desc || null }));
    }));
    menu.appendChild(menuItem(null, 'Ungroup (keep content)', () => this.ungroupSelf()));
    menu.appendChild(menuItem('trash', 'Delete group and contents', () => this.deleteSelf(), { danger: true }));
    openContextMenu(menu, e.clientX, e.clientY);
  }

  ungroupSelf() {
    const pos = this.getPos();
    if (pos == null) return;
    this.view.dispatch(this.view.state.tr.replaceWith(pos, pos + this.node.nodeSize, this.node.content));
    this.view.focus();
  }

  deleteSelf() {
    const pos = this.getPos();
    if (pos == null) return;
    this.view.dispatch(this.view.state.tr.delete(pos, pos + this.node.nodeSize));
    this.view.focus();
  }

  update(node) {
    if (node.type !== this.node.type) return false;
    // Chrome (classes, pill text) derives from attrs — recreate on any attr change;
    // ProseMirror restores the selection, so the tools refresh too.
    if (node.attrs.type !== this.node.attrs.type
      || node.attrs.label !== this.node.attrs.label
      || node.attrs.desc !== this.node.attrs.desc
      || node.attrs.args !== this.node.attrs.args) return false;
    this.node = node;
    return true;
  }

  destroy() {
    if (this.selected && this.ui) this.ui.set(null);
  }
}

/* Verbatim-HTML fallback for blocks without a structured model: raw blocks always,
   tables/figures only when the server sent no meta (e.g. the missing-image
   placeholder figure). Display-only; Source (advanced) edits them. */
class AtomHtmlView {
  constructor(node) {
    const t = document.createElement('template');
    t.innerHTML = (node.attrs.html || '').trim();
    this.dom = t.content.firstElementChild
      ? document.importNode(t.content.firstElementChild, true)
      : element('div', 'dw-raw-block');
    this.dom.setAttribute('data-dw-atom', node.type.name);
  }

  // The HTML inside an atom is display-only; PM must not interpret DOM changes there.
  ignoreMutation() { return true; }
}

/* EmbedView: a live-linked block (Slides v2, Task 9). Renders the RESOLVED transclusion
   (node.attrs.html, like AtomHtmlView) as display-only content — it is another chapter's
   content, not editable here — PLUS a chrome pill marking it LINKED. Clicking the pill
   selects the node (NodeSelection); its tools (Edit source ↗ / Detach) then appear in the
   shared context bar, mirroring ContainerView's select/buildTools pattern. The node's ref
   (chapter#anchor) is the round-trip source of truth; serialize.js re-emits the directive.

   Detach is a conscious fork: it replaces the embed with the section's ACTUAL editable
   markdown (fetched as editable segments), after which the content serializes as real
   markdown — no longer '::: embed'. */
class EmbedView {
  constructor(node, view, getPos, ui) {
    this.node = node;
    this.view = view;
    this.getPos = getPos;
    this.ui = ui;
    this.selected = false;

    this.dom = element('div', 'dw-embed-host');
    this.dom.contentEditable = 'false';

    // The resolved transclusion: the server wraps it in a single .dw-embed element.
    this.body = this.renderBody(node);

    // Chrome pill (top-right) — same select-on-click discipline as ContainerView: a
    // preventDefault mousedown (no caret) then a click that sets a NodeSelection.
    // The pill is a child of the .dw-embed BODY (which app.css makes position:relative in
    // the editor) so it anchors to the embedded block's own top-right corner. It must NOT
    // live on the display:contents host — a contents element paints no box, so an absolute
    // child would anchor to the whole SLIDE and float in its corner (clipped) instead.
    const pill = element('span', 'dw-embed-pill');
    pill.setAttribute('data-tip', 'A linked block — it shows a chapter section live and stays in sync. Click to select it, then Edit source to change it, or Detach to turn it into your own editable content.');
    const dot = element('span', 'dw-embed-dot');
    pill.appendChild(dot);
    pill.appendChild(document.createTextNode('LINKED'));
    pill.addEventListener('mousedown', (e) => { e.preventDefault(); e.stopPropagation(); });
    pill.addEventListener('click', (e) => {
      e.preventDefault();
      e.stopPropagation();
      const pos = this.getPos();
      if (pos == null) return;
      this.view.dispatch(this.view.state.tr.setSelection(NodeSelection.create(this.view.state.doc, pos)));
      this.view.focus();
    });
    this.body.appendChild(pill);

    // Width handle (L205): drag the right edge to resize the linked box; the transcluded
    // content REFLOWS to the new width (it is NOT zoomed). Same right-edge handle and feel
    // as a table's box-width handle. Shown only while selected (CSS).
    const handle = element('span', 'e2-boxhandle');
    handle.contentEditable = 'false';
    handle.setAttribute('data-tip', 'Drag to resize the linked block — its content reflows to the new width. Drag fully to the right to reset to full width.');
    handle.addEventListener('mousedown', (e) => this.dragWidth(e));
    this.body.appendChild(handle);

    this.dom.appendChild(this.body);
    this.applyWidth();
    this.applyAlign();
  }

  /* Drive the box-alignment class from node.attrs.align (the serialize source of truth);
     left = no class (default). */
  applyAlign() {
    this.body.classList.toggle('dw-box-center', this.node.attrs.align === 'center');
    this.body.classList.toggle('dw-box-right', this.node.attrs.align === 'right');
  }

  renderBody(node) {
    const t = document.createElement('template');
    t.innerHTML = (node.attrs.html || '').trim();
    const body = t.content.firstElementChild
      ? document.importNode(t.content.firstElementChild, true)
      : element('div', 'dw-embed');
    body.contentEditable = 'false';
    return body;
  }

  /* Drive the linked box's width from node.attrs.width (the serialize source of truth),
     overriding whatever inline width the server put on .dw-embed — so the edit DOM and the
     node attribute can never drift. The content reflows to this width (no zoom). */
  applyWidth() {
    if (this.node.attrs.width) {
      this.body.style.width = this.node.attrs.width;
      this.body.style.maxWidth = '100%';
    } else {
      this.body.style.width = '';
      this.body.style.maxWidth = '';
    }
  }

  /* Width drag (L205): a right-edge handle sizes the linked box as a % of its column/zone;
     the content reflows (text rewraps, a linked table/image narrows). Dragging (nearly) to
     the right edge resets to full width. The current width is pinned on mousedown so the box
     never jumps. The committed width serializes as `width=…` on the directive. */
  dragWidth(e) {
    e.preventDefault();
    e.stopPropagation();
    const startVisual = this.body.getBoundingClientRect().width;
    // avail = the box's FULL visual width, measured directly (momentarily clear the width
    // and read the rect) — correct no matter how the display:contents host nests the box,
    // and VISUAL so the ratio is zoom-independent (the slide surface is CSS-zoomed; the
    // shared trackWidthDrag drives width as a %). The maxWidth:100% (set by applyWidth) is
    // restored, so the committed % box never exceeds its column.
    const prevW = this.body.style.width, prevMax = this.body.style.maxWidth;
    this.body.style.width = '';
    this.body.style.maxWidth = '';
    const avail = this.body.getBoundingClientRect().width;
    this.body.style.width = prevW;
    this.body.style.maxWidth = prevMax;
    trackWidthDrag(this.body, e.clientX, startVisual, avail, (width) => {
      const pos = this.getPos();
      if (pos != null)
        this.view.dispatch(this.view.state.tr.setNodeMarkup(pos, null, { ...this.node.attrs, width }));
      this.view.focus();
    }, '__dwEmbedWidth');
  }

  selectNode() {
    this.selected = true;
    this.dom.classList.add('e2-active');
    if (this.ui) this.ui.set(this.buildTools(), null, 'embed');
  }

  deselectNode() {
    this.selected = false;
    this.dom.classList.remove('e2-active');
    if (this.ui) this.ui.set(null);
  }

  // Built from the ONE shared context-tools definition (contexttools.js, kind 'box'); this
  // method supplies the linked-block handlers + current state. The LINKED badge is rendered by
  // contextBar.set() from the shared BADGES map; box-placement reuses the same glyphs as the
  // table/figure so they can't drift (ADR-008, audit F1).
  buildTools() {
    const ref = this.node.attrs.ref || '';
    const chapterId = ref.split('#')[0];
    return buildStrip('box', {
      view: this.view, getPos: this.getPos, node: this.node,
      onEditSource: () => {
        const bridge = window.DocWizBridge;
        if (chapterId && bridge && bridge.openChapterById) bridge.openChapterById(chapterId);
      },
      onChangeSource: () => this.changeSource(),
      onDetach: () => this.detach(),
      // Place the linked block left / centre / right (L205) — visible once it's narrower than
      // the page (drag the right edge to resize it first). Default is LEFT (null).
      placeKind: 'box',
      align: this.node.attrs.align,
      placeTips: {
        left: 'Place the linked block at the LEFT (default).',
        center: 'Centre the linked block in the page/zone (once it is narrower than the page).',
        right: 'Place the linked block at the RIGHT (once it is narrower than the page).',
      },
      onPlace: (val) => {
        const pos = this.getPos();
        if (pos == null) return;
        this.view.dispatch(this.view.state.tr.setNodeMarkup(pos, null, { ...this.node.attrs, align: val }));
        this.view.focus();
      },
      onDelete: () => {
        const pos = this.getPos();
        if (pos == null) return;
        this.view.dispatch(this.view.state.tr.delete(pos, pos + this.node.nodeSize));
        this.view.focus();
      },
      deleteTip: 'Remove this linked block from the slide.',
      hint: ref + ' · Esc deselects',
    });
  }

  /* Change source (Slides v2 dogfooding): re-point this linked block at a DIFFERENT
     chapter/section without detaching. Reuses the relink resolution path — pick a section
     (DocWizBridge.pickSection → bare chapter#anchor), build the canonical embed directive,
     resolve it through the one pipeline (/api/editor-fragment, the browser never parses
     markdown), then update THIS node's attrs (ref + html) in place via setNodeMarkup. The
     block stays an embed (it re-renders with the new content and serializes to the new
     '::: embed dw:newref :::'); a cancelled picker leaves it untouched. */
  changeSource() {
    const view = this.view;
    const bridge = window.DocWizBridge;
    if (!bridge || !bridge.pickSection) return;
    const ui = this.ui;
    bridge.pickSection((ref) => {
      if (!ref) return; // cancelled — leave the embed unchanged
      const directive = bridge.embedDirective(ref); // the one classic-script embed-directive producer (app.js)
      fetch('/api/editor-fragment', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ markdown: directive, audience: (ui && ui.audience) || null }),
      })
        .then((r) => { if (!r.ok) return r.text().then((t) => { throw new Error(t || r.statusText); }); return r.json(); })
        .then((dto) => {
          const seg = (dto.segments || []).find((s) => s.kind === 'block') || (dto.segments || [])[0];
          if (!seg) throw new Error('the linked section produced no content');
          // Re-resolve the node position against the CURRENT doc (the picker is async).
          const pos = this.getPos();
          if (pos == null) return;
          const newRef = seg.ref || String(ref).replace(/^dw:/, '');
          // Preserve the box's own attrs (width, srcId, meta) — changing the SOURCE keeps
          // the linked box's size (L205); only ref + resolved html change.
          let tr = view.state.tr.setNodeMarkup(pos, null, { ...this.node.attrs, ref: newRef, html: seg.html || '' });
          tr = tr.setSelection(NodeSelection.create(tr.doc, pos)).scrollIntoView();
          view.dispatch(tr);
          view.focus();
        })
        .catch((e) => {
          if (bridge.toast) bridge.toast('Could not change the linked source: ' + e.message, 'err');
          else window.alert('Could not change the linked source: ' + e.message);
        });
    });
  }

  /* Detach → fork: ask the server for the linked section's raw markdown as EDITABLE
     segments, build PM blocks from them (the same ingest mapping), and replace the embed
     node with that content. It is now authored content (no srcId, no ref), so it
     serializes as real markdown. An empty section falls back to an empty paragraph so the
     zone is never left with a stranded selection. */
  detach() {
    const ref = this.node.attrs.ref;
    const view = this.view;
    const schema = view.state.schema;
    fetch('/api/embed-detach', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ ref }),
    })
      .then((r) => { if (!r.ok) return r.text().then((t) => { throw new Error(t || r.statusText); }); return r.json(); })
      .then((dto) => {
        const pos = this.getPos();
        if (pos == null) return;
        let blocks = nodesFromSegments(schema, dto.segments);
        if (blocks.length === 0) blocks = [schema.nodes.paragraph.create()];
        const from = pos;
        const to = pos + this.node.nodeSize;
        let tr = view.state.tr.replaceWith(from, to, blocks);
        // Put the cursor inside the first detached block so the author can edit it.
        const sel = TextSelection.near(tr.doc.resolve(from + 1), 1);
        tr = tr.setSelection(sel).scrollIntoView();
        view.dispatch(tr);
        view.focus();
      })
      .catch((e) => {
        const bridge = window.DocWizBridge;
        if (bridge && bridge.toast) bridge.toast('Could not detach the linked block: ' + e.message, 'err');
        else window.alert('Could not detach the linked block: ' + e.message);
      });
  }

  // ref/html drive the chrome + body; recreate on either change so PM rebuilds the body
  // (and restores selection). width/align change IN PLACE — keep the view but re-apply the
  // layout, else a layout-only edit (the Left/Center/Right buttons) would update the
  // attribute but never the visible box ("the alignment buttons do nothing").
  update(node) {
    if (node.type !== this.node.type) return false;
    if (node.attrs.ref !== this.node.attrs.ref || node.attrs.html !== this.node.attrs.html) return false;
    this.node = node;
    this.applyWidth();
    this.applyAlign();
    return true;
  }

  // Display-only transclusion: PM must not interpret DOM changes inside it.
  ignoreMutation() { return true; }

  destroy() {
    if (this.selected && this.ui) this.ui.set(null);
  }
}

export function nodeViews(schema, ui) {
  return {
    container: (node, view, getPos) => new ContainerView(node, view, getPos, ui),
    // Structured views need meta; placeholders (missing image) stay verbatim HTML.
    figure: (node, view, getPos) =>
      node.attrs.meta && !/img-placeholder/.test(node.attrs.html || '')
        ? new FigureView(node, view, getPos, ui)
        : new AtomHtmlView(node),
    table: (node, view, getPos) =>
      node.attrs.meta ? new TableView(node, view, getPos, schema, ui) : new AtomHtmlView(node),
    raw: (node) => new AtomHtmlView(node),
    embed: (node, view, getPos) => new EmbedView(node, view, getPos, ui),
    list_item: (node, view, getPos) => new TaskItemView(node, view, getPos),
  };
}

function element(tag, className) {
  const el = document.createElement(tag);
  el.className = className;
  return el;
}
