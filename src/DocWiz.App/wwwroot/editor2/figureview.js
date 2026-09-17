/* Figure NodeView (M3 + feedback round 1): the pipeline\'s exact figure markup from
   structured meta. Click the image to select it — its tools (size presets,
   alignment, caption) appear in the FIXED context bar under the banner; only the
   resize handle lives on the image itself (drag snaps to the FIGURE_WIDTHS scale).
   Every change is ONE transaction on attrs.meta → native undo/redo. */

import { NodeSelection } from 'prosemirror-state';
import { FIGURE_WIDTHS, FIGURE_SNAPS, SNAP_TOLERANCE, MIN_IMAGE_WIDTH, HANDLE_OFFSET } from './constants.js';
import { toSurfaceSrc } from './toolhelpers.js';
import { buildStrip, buildMenu, openContextMenu } from './contexttools.js';

export class FigureView {
  constructor(node, view, getPos, ui) {
    this.node = node;
    this.view = view;
    this.getPos = getPos;
    this.ui = ui;
    this.selected = false;

    this.dom = document.createElement('figure');
    this.dom.className = 'dw-figure dw-image-figure';
    this.dom.addEventListener('contextmenu', (e) => this.onContextMenu(e));
    this.render();
  }

  meta() { return this.node.attrs.meta; }

  patch(changes) {
    const pos = this.getPos();
    if (pos == null) return;
    const meta = { ...this.meta(), ...changes };
    this.view.dispatch(this.view.state.tr.setNodeMarkup(pos, null, { ...this.node.attrs, meta }));
    this.view.focus();
  }

  // Rotate the underlying image file 90° clockwise (server-side GDI+). Not a document edit —
  // the Markdown ![alt](src) is unchanged — so there is no dirty state or save; on success we
  // just refresh this view's <img>. To undo, rotate three more times.
  rotate() {
    const src = this.meta().src || '';
    if (!src) return;
    fetch('/api/rotate-image', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ src, clockwise: true }),
    })
      .then((r) => { if (!r.ok) return r.text().then((t) => { throw new Error(t || r.statusText); }); })
      .then(() => {
        this.cacheBust = Date.now();
        this.render();
        if (this.ui && this.ui.flash) this.ui.flash('Rotated 90° — the picture file was updated.');
      })
      .catch((err) => {
        if (this.ui && this.ui.flash) this.ui.flash('Could not rotate this picture: ' + err.message);
      });
  }

  selectNode() {
    this.selected = true;
    this.dom.classList.add('e2-active');
    if (this.ui) this.ui.set(this.buildTools(), null, 'image');
  }

  deselectNode() {
    this.selected = false;
    this.dom.classList.remove('e2-active');
    if (this.ui) this.ui.set(null);
  }

  render() {
    const m = this.meta();
    this.dom.innerHTML = '';
    this.dom.classList.toggle('e2-active', this.selected);
    if (m.explicitId) this.dom.id = m.explicitId;

    const img = document.createElement('img');
    img.src = toSurfaceSrc(m.src);
    // After an in-place rotation the file changed but its URL did not; bust this view's
    // <img> so the already-loaded picture refetches the rotated bytes (assets are no-store).
    if (this.cacheBust) img.src += (img.src.includes('?') ? '&' : '?') + 'r=' + this.cacheBust;
    img.alt = m.alt || '';
    if (m.width) img.setAttribute('width', m.width);
    if (m.align === 'center') img.classList.add('img-center');
    if (m.align === 'right') img.classList.add('img-right');
    if (m.align === 'left') img.classList.add('img-left');
    this.dom.appendChild(img);

    this.resizeHandle(img);

    if (m.showCaption !== false) {
      const caption = document.createElement('figcaption');
      caption.textContent = 'Figure ' + m.number + (m.alt ? ' — ' + m.alt : '');
      this.dom.appendChild(caption);
    }
  }

  // The picture handlers + current state — ONE ctx shared by the bar (buildStrip) and the
  // right-click menu (buildMenu), so the two surfaces can't drift (ADR-008 / rule 21). The
  // IMAGE badge is rendered by contextBar.set() from the shared BADGES map.
  figureCtx() {
    const m = this.meta();
    return {
      view: this.view, getPos: this.getPos, node: this.node,
      // Size + exact px (FIGURE_WIDTHS scale; blank = original size).
      sizeScale: FIGURE_WIDTHS,
      width: m.width,
      onSize: (w) => this.patch({ width: w }),
      onPx: (w) => this.patch({ width: w }),
      // Place: a standalone image is CENTERED by default (the flex column's align-items), so
      // "center" owns the !align active state; left/right are explicit .img-left/.img-right.
      placeKind: 'image',
      align: m.align,
      placeTips: {
        left: 'Place the image at the left of the page.',
        center: 'Center the image on the page (default).',
        right: 'Place the image at the right of the page.',
      },
      onPlace: (a) => this.patch({ align: a }),
      onReplace: () => {
        const bridge = window.DocWizBridge;
        if (bridge && bridge.pickAsset)
          bridge.pickAsset((res) => this.patch({ src: res.src, alt: this.meta().alt || res.alt }));
      },
      linkTip: 'Copy this image’s link — paste it into any text or into a view.',
      onLink: () => {
        const meta = this.meta();
        const anchor = meta.explicitId || 'figure-' + meta.number;
        const ref = 'dw:' + ((this.ui && this.ui.chapterId) || '') + '#' + anchor;
        if (navigator.clipboard) navigator.clipboard.writeText(ref);
        if (this.ui && this.ui.flash) this.ui.flash('Copied ' + ref + ' — paste it into any text or chapter-link dialog.');
      },
      // Rotate the asset's PIXELS server-side (GDI+) so editor and handover match with no
      // CSS-transform tricks; the original is kept once in _rotated-originals/.
      onRotate: () => this.rotate(),
      showCaption: m.showCaption !== false,
      onCaptionToggle: () => this.patch({ showCaption: !(m.showCaption !== false) }),
      captionValue: m.alt,
      captionPlaceholder: 'Caption — shown under the figure',
      captionTip: 'The caption under the picture — also the image description.',
      onCaption: (v) => this.patch({ alt: v }),
      onDelete: () => {
        const pos = this.getPos();
        if (pos == null) return;
        this.view.dispatch(this.view.state.tr.delete(pos, pos + this.node.nodeSize));
        this.view.focus();
      },
      deleteTip: 'Delete this image and its caption (Ctrl+Z brings it back).',
      hint: 'Drag a corner handle to resize · <kbd>Esc</kbd> deselects',
    };
  }

  buildTools() { return buildStrip('image', this.figureCtx()); }

  // Right-click the figure: select it (so the bar mounts too), then open the shared context
  // menu at the cursor — the same handlers as the bar via the one figureCtx().
  onContextMenu(e) {
    e.preventDefault();
    const pos = this.getPos();
    if (pos != null && !this.selected) {
      this.view.dispatch(this.view.state.tr.setSelection(NodeSelection.create(this.view.state.doc, pos)));
    }
    openContextMenu(buildMenu('image', this.figureCtx()), e.clientX, e.clientY);
  }

  resizeHandle(img) {
    const handle = document.createElement('span');
    handle.className = 'e2-fighandle';
    handle.contentEditable = 'false';
    handle.setAttribute('data-tip', 'Drag to resize — snaps to ' + FIGURE_SNAPS.join(' / ') + ' px.');
    const readout = document.createElement('span');
    readout.className = 'e2-figreadout';

    // The handle sits ON the image\'s bottom-right corner (stakeholder feedback).
    const place = () => {
      handle.style.left = (img.offsetLeft + img.offsetWidth - HANDLE_OFFSET) + 'px';
      handle.style.top = (img.offsetTop + img.offsetHeight - HANDLE_OFFSET) + 'px';
    };
    if (img.complete) setTimeout(place, 0);
    img.addEventListener('load', place);

    handle.onmousedown = (e) => {
      e.preventDefault();
      e.stopPropagation();
      const startX = e.clientX;
      // startW in CONTENT px (the width attribute); scale = on-screen px per content px, so the
      // viewport-px drag delta maps correctly at any zoom (chapter content-zoom or slide-edit zoom).
      const startW = parseInt(img.getAttribute('width'), 10) || Math.round(img.getBoundingClientRect().width);
      const scale = (img.getBoundingClientRect().width / startW) || 1;
      this.dom.appendChild(readout);
      const snap = (w) => {
        for (const s of FIGURE_SNAPS) if (Math.abs(w - s) <= SNAP_TOLERANCE) return s;
        return w;
      };
      const move = (ev) => {
        const w = snap(Math.max(MIN_IMAGE_WIDTH, Math.round(startW + (ev.clientX - startX) / scale)));
        img.setAttribute('width', String(w));
        readout.textContent = w + ' px';
        place();
      };
      const up = (ev) => {
        document.removeEventListener('mousemove', move);
        document.removeEventListener('mouseup', up);
        readout.remove();
        const w = snap(Math.max(MIN_IMAGE_WIDTH, Math.round(startW + (ev.clientX - startX) / scale)));
        this.patch({ width: String(w) });
      };
      document.addEventListener('mousemove', move);
      document.addEventListener('mouseup', up);
    };
    this.dom.appendChild(handle);
  }

  update(node) {
    if (node.type !== this.node.type) return false;
    const changed = node.attrs.meta !== this.node.attrs.meta;
    this.node = node;
    if (changed) {
      this.render();
      if (this.selected && this.ui) this.ui.set(this.buildTools(), null, 'image'); // refresh active states
    }
    return true;
  }

  ignoreMutation() { return true; }
  stopEvent(event) { return !!(event.target.closest && event.target.closest('.e2-fighandle')); }
  destroy() { if (this.selected && this.ui) this.ui.set(null); }
}
