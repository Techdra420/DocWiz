/* Live per-zone editing on the slide canvas (Slides v2, ADR-007, Task 7b).

   The slide canvas (app.js #vEditSurface.dw-live-surface) is rendered in the PARENT
   document as the RenderSlides fragment: a .slides-edit-list of .slide[data-layout]
   cards, each holding .slide-region[data-region] zones (plus the auto title slide,
   which has no zones). This module mounts ONE ProseMirror EditorView per zone IN THAT
   SAME document and lets the SHARED chapter toolbar (main.js) act on whichever zone has
   focus — the stakeholder's "toolbar follows the active zone" design.

   ADR-004 holds: zones ingest pipeline HTML from /api/editor-fragment (the same engine
   as the chapter editor's /api/editor-doc); the browser never parses markdown. Saving
   zone content back to the view is Task 7c — 7b is editing only (a module dirty flag).

   app.js (a classic script) drives this via window.DocWizSlideEdit. We import the PM
   primitives + ingest/views directly (ES modules are singletons), and pull the shared
   schema/plugins/toolbar/active-view helpers from window.DocWizEditor2 (set by main.js
   on load) so zones share the chapter editor's exact model and commands. */
import { EditorState } from 'prosemirror-state';
import { EditorView } from 'prosemirror-view';
import { buildDoc } from './ingest.js';
import { nodeViews } from './views.js';
import { blocks } from './serialize.js';
import { slashMenuPlugin } from './slashmenu.js';
import { routePastedImage } from './pasteimage.js';

// Mounted zone views for the currently open deck. Cleared by unmountAll() before any
// deliberate surface re-render so editors never leak or double-mount. Each zone records
// the working-model slot it maps to (slideIndex into the deck's slides[] — i.e. DOM
// slide index minus the auto title slide — and its region name) plus a per-zone dirty
// flag, so collectRegions() (Task 7c) re-serializes ONLY the zones the author actually
// edited and copies every untouched region's original markdown verbatim.
let zones = [];     // [{ view, regionEl, slideIndex, regionName, dirty }]
let toolbarEl = null;
let dirty = false;  // any zone edited since mount (drives the view's dirty indicator)
let opts = {};      // { slides, audience, onZoneDirty }

function ed() { return window.DocWizEditor2; }

/* Pasted/dropped image files route through app.js's project-assets dialog and land in
   the focused zone — the active view is this zone (set on focus/selection), so
   DocWizEditor2.insert targets it (same flow as the chapter editor's pasteImageFiles). */
function pasteImageFiles(view, data) {
  return routePastedImage(data, (file) => {
    const bridge = window.DocWizBridge;
    if (!bridge || !bridge.pasteImage) return;
    ed().setActiveView(view);
    bridge.pasteImage(file, (res) => ed().insert({ type: 'image', src: res.src, alt: res.alt }));
  });
}

/* Build one zone editor on a .slide-region element from its region markdown. The
   fragment endpoint returns { segments } in the SAME shape as /api/editor-doc, so
   buildDoc + nodeViews + plugins() are reused verbatim. slideIndex/regionName tie this
   editor to its slides[slideIndex].regions[regionName] slot for collectRegions(). */
function mountZone(regionEl, markdown, slideIndex, regionName) {
  const E = ed();
  const schema = E.schema;
  return fetch('/api/editor-fragment', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ markdown: markdown || '', audience: opts.audience || null }),
  })
    .then((r) => { if (!r.ok) return r.text().then((t) => { throw new Error(t || r.statusText); }); return r.json(); })
    .then((dto) => {
      const doc = buildDoc(schema, dto);
      // The .slide-region currently holds the server's rendered HTML (7a read-only
      // display). Clear it so ProseMirror owns the element.
      regionEl.innerHTML = '';
      regionEl.classList.add('dw-zone');
      // The zone record is created up front so dispatchTransaction can flag THIS zone
      // dirty (collectRegions re-serializes dirty zones only — untouched zones keep
      // their original region bytes; serialize.js is a canonical re-serializer, not a
      // byte-preserving one, so re-emitting unchanged content would churn the YAML).
      const zone = { view: null, regionEl, slideIndex, regionName, dirty: false };
      const view = new EditorView(regionEl, {
        // The slash (/) insert menu FIRST, then the shared chapter plugins — scoped to
        // ZONE editors only (Task 8). Order matters: ProseMirror runs handleKeyDown in
        // plugin order, so the menu must precede the base keymaps to intercept
        // Enter/Arrows/Esc WHILE OPEN (otherwise Enter splits the block before the menu
        // sees it). When the menu is closed its handlers return false immediately, so the
        // base keymaps behave normally. The chapter editor never gets this plugin (its
        // plugins() is unchanged) — it keeps the fixed Insert bar and shows no slash menu.
        // opts.audience lets the menu's "Linked element" resolve embeds through the same
        // view audience the zone was mounted with.
        state: EditorState.create({
          schema, doc,
          plugins: [slashMenuPlugin({ audience: opts.audience || null })].concat(E.plugins()),
        }),
        nodeViews: nodeViews(schema, E.contextBar),
        dispatchTransaction(tr) {
          view.updateState(view.state.apply(tr));
          // This zone is the active view whenever it is driven (selection/typing).
          E.setActiveView(view);
          if (tr.docChanged) {
            zone.dirty = true;
            dirty = true;
            if (opts.onZoneDirty) opts.onZoneDirty();
          }
          E.notifyToolbar();
        },
        handleDOMEvents: {
          focus: () => { E.setActiveView(view); E.notifyToolbar(); return false; },
        },
        handlePaste: (_v, event) => pasteImageFiles(view, event.clipboardData),
        handleDrop: (_v, event) => pasteImageFiles(view, event.dataTransfer),
      });
      zone.view = view;
      zones.push(zone);
      return view;
    })
    .catch((e) => {
      // A dangling embed (missing chapter/anchor) or fragment error — leave the 7a
      // rendered HTML in place and show the message inline rather than blanking the zone.
      regionEl.classList.add('dw-zone-error');
      const note = document.createElement('p');
      note.className = 'dw-zone-errnote';
      note.textContent = 'Could not open this region for editing: ' + e.message;
      regionEl.appendChild(note);
    });
}

/* Make a slide's title/subtitle editable in place (Slides v2 — new/blank slides must be
   authorable, notably section-header which has no regions). These are plain single-line
   contenteditable elements (NOT ProseMirror zones — a slide title/subtitle is a scalar
   string, not block markdown), bound straight to the working-model slide.title/subtitle so
   collectRegions/slidePayload saves them via the existing payload. The shared toolbar does
   not act on them (they are not the active ProseMirror view); typing just edits text. */
function mountTitleField(el, slideIndex, key) {
  el.setAttribute('contenteditable', 'true');
  // A DERIVED heading (data-derived, set by ViewRenderer for a lone-embed title-content slide
  // with no explicit title) is shown so edit matches the deck (WYSIWYG), but it is NOT the
  // author's explicit title — it must stay out of the working model so it keeps auto-deriving.
  // We leave slide[key] untouched on mount; only an actual edit (input) promotes it to explicit
  // and drops the derived flag. The displayed text is real (not a CSS placeholder), so it
  // occupies the same space the deck's <h1> does — the divergence the gate caught.
  const derived = el.hasAttribute('data-derived');
  // Enter would insert a <br>/newline into a scalar title — block it (a title is one line).
  el.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') { e.preventDefault(); el.blur(); }
  });
  el.addEventListener('input', () => {
    const slide = (opts.slides || [])[slideIndex];
    if (!slide) return;
    const text = el.textContent.trim();
    slide[key] = text || null;
    // The author has taken ownership of a previously-derived heading: it is now explicit.
    el.removeAttribute('data-derived');
    el.classList.toggle('is-empty', !text);
    dirty = true;
    if (opts.onZoneDirty) opts.onZoneDirty();
  });
  return derived;
}

/* Mount the shared chapter toolbar ONCE, across the whole editor above the rail and the
   canvas (as the chapter toolbar sits under its document bar), so it acts on the focused zone
   and has the width to stay one row. Re-created on each (re)mount so its button list binds to
   the live zones. */
function mountToolbar(surfaceEl) {
  const E = ed();
  unmountToolbar();
  // linkTool: the slide canvas gets the "Link…" relink button (turn the selected block
  // into a live-linked embed — Task 9); the chapter toolbar never shows it.
  toolbarEl = E.buildMainToolbar({ linkTool: true });
  toolbarEl.classList.add('e2-slide-toolbar');
  // The relink + embed-insert resolve embeds with the deck's view audience (so the
  // resolved render matches what ships); expose it on the shared contextBar.
  E.contextBar.audience = opts.audience || null;
  // The contextBar (link/character/figure/table tools) shares the chapter editor's
  // sub-row machinery; point it at a FRESH row under the slide toolbar (never move the
  // chapter editor's own element) so those tools render when a zone figure/table/link
  // is selected.
  const ctx = document.createElement('div');
  ctx.className = 'e2-contextbar e2-ctx-empty';
  E.contextBar.el = ctx;
  E.contextBar.toolbarEl = toolbarEl;
  const wrap = document.createElement('div');
  wrap.className = 'e2-top e2-slide-top';
  wrap.appendChild(toolbarEl);
  wrap.appendChild(ctx);
  const layout = surfaceEl.closest('.vedit-layout') || surfaceEl;
  layout.parentNode.insertBefore(wrap, layout);
  E.contextBar.set(null);
}

function unmountToolbar() {
  if (toolbarEl && toolbarEl.parentNode) {
    const wrap = toolbarEl.closest('.e2-slide-top') || toolbarEl;
    wrap.remove();
  }
  toolbarEl = null;
  // The shared contextBar pointed at our (now detached) sub-row/toolbar; null them so
  // they don't dangle at removed DOM after leaving the slide view (M-3 hygiene). The
  // chapter editor re-establishes its own contextBar.el/.toolbarEl on next open().
  const E = ed();
  if (E && E.contextBar) {
    E.contextBar.el = null;
    E.contextBar.toolbarEl = null;
    // Clear the slide-only relink audience so the container Link… tool (gated on
    // audience !== undefined) never shows in the chapter editor.
    delete E.contextBar.audience;
  }
}

/* Pair each .slide-region with its working-model markdown and mount an editor.
   slides[] is the deck's working-model slides in render order (NOT including the auto
   title slide); the rendered .slide elements are [title, slides[0], slides[1], …], so
   region markdown for a .slide-region is slides[domSlideIndex-1].regions[name]. */
export function mountAll(o) {
  opts = o || {};
  const surfaceEl = opts.surface;
  if (!surfaceEl || !ed()) return Promise.resolve();
  unmountAll();
  dirty = false;

  // cardSelector: '.slide' for the slides surface, '.view-sec' for the document surface.
  // Each editable card carries an explicit data-slide-index (set by RenderSlides/RenderDocument
  // edit mode) = its slides[] slot. Cards WITHOUT it — the auto title slide, the document lead,
  // chapter-expansion slides — are read-only and skipped.
  const cardSelector = opts.cardSelector || '.slide';
  const cardEls = Array.prototype.slice.call(surfaceEl.querySelectorAll(cardSelector));
  const slides = opts.slides || [];
  const tasks = [];
  cardEls.forEach((cardEl) => {
    const idxAttr = cardEl.getAttribute('data-slide-index');
    if (idxAttr === null) return;
    const slideIndex = parseInt(idxAttr, 10);
    const slide = slides[slideIndex];
    if (!slide) return;
    // Editable title/subtitle on the card (every layout, incl. section-header which has
    // no regions) so a brand-new entry is authorable rather than a dead blank page.
    const titleEl = cardEl.querySelector('[data-slide-title]');
    if (titleEl) mountTitleField(titleEl, slideIndex, 'title');
    const subEl = cardEl.querySelector('[data-slide-sub]');
    if (subEl) mountTitleField(subEl, slideIndex, 'subtitle');
    const regionEls = Array.prototype.slice.call(cardEl.querySelectorAll('.slide-region[data-region]'));
    regionEls.forEach((regionEl) => {
      const name = regionEl.getAttribute('data-region');
      const md = slide.regions ? slide.regions[name] : '';
      tasks.push(mountZone(regionEl, md, slideIndex, name));
    });
  });

  mountToolbar(surfaceEl);
  return Promise.all(tasks);
}

/* Tear down every mounted zone editor (and the toolbar). app.js calls this before any
   deliberate surface re-render (add/delete slide, layout/mode change) so editors never
   double-mount; the active view falls back to the open chapter (none here) cleanly. */
export function unmountAll() {
  for (const z of zones) {
    try { z.view.destroy(); } catch { /* already gone */ }
  }
  zones = [];
  unmountToolbar();
  // Drop the active-view pointer if it was one of our zones (now destroyed).
  const E = ed();
  if (E && typeof E.getActiveView === 'function') E.setActiveView(null);
  dirty = false;
}

export function isMounted() { return zones.length > 0; }
export function isDirty() { return dirty; }

/* Clear the dirty bookkeeping after the view is saved (Task 7c review I1). The zone
   editors stay mounted (no re-render on save), so their per-zone dirty flags and the
   module flag would otherwise stay true forever and make isDirty() meaningless for
   Tasks 8/9. app.js calls this from saveView()'s success path, alongside
   state.dirty = false. A subsequent collectRegions() then correctly re-serializes only
   zones edited AFTER this save. */
export function markClean() {
  for (const z of zones) z.dirty = false;
  dirty = false;
}

/* Re-derive a zone's working-model slide index from its CURRENT DOM position rather than
   trusting the index cached at mount (Task 7c review I2). The surface renders slides as
   [auto title slide, slides[0], slides[1], …]; a zone lives inside the (i)-th rendered
   .slide, which maps to slides[i-1]. Today every deck mutation fully re-renders +
   remounts, so the cached index is always correct — but Task 8's mid-deck insert could
   leave mounted zones with stale cached indices, which would silently write an edit to
   the wrong slide (or hit the `if (!slide)` guard and vanish). Reading the live DOM
   position makes collect correct regardless of how the deck changed under the mounts.
   Falls back to the cached index if the zone is somehow detached from the surface. */
function liveSlideIndex(z) {
  const slideEl = z.regionEl && z.regionEl.closest('.slide');
  if (!slideEl || !slideEl.parentNode) return z.slideIndex;
  const slideEls = slideEl.parentNode.querySelectorAll('.slide');
  const domIndex = Array.prototype.indexOf.call(slideEls, slideEl);
  if (domIndex < 0) return z.slideIndex;
  return domIndex - 1; // subtract the auto title slide at DOM index 0
}

/* Apply each EDITED zone's content back into the slides working model in place (Task 7c
   save path). For every dirty zone, serialize its ProseMirror doc to markdown via
   blocks() — the canonical top-level-block serializer (a clean embed serializes back to
   the two-line '::: embed dw:ref :::' directive, byte-stable) — and write it into
   slides[slideIndex].regions[regionName]. The slot is re-derived from the zone's live DOM
   position (liveSlideIndex) so it stays correct even if the deck was mutated under the
   mounts (review I2). UNTOUCHED zones are left exactly as loaded: their original region
   string stays in the model verbatim, so re-serialization never churns content the author
   did not edit. app.js calls this from currentViewPayload() before the payload is built
   (slidePayload then prunes blank/invalid regions). Returns the same slides reference. */
export function collectRegions(slides) {
  const target = slides || opts.slides;
  if (!target) return target;
  for (const z of zones) {
    if (!z.dirty) continue;
    const slide = target[liveSlideIndex(z)];
    if (!slide) continue;
    if (!slide.regions) slide.regions = {};
    slide.regions[z.regionName] = blocks(z.view.state.doc);
  }
  return target;
}

// app.js (classic script) bridge.
window.DocWizSlideEdit = { mountAll, unmountAll, isMounted, isDirty, collectRegions, markClean };
