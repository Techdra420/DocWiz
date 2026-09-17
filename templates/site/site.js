/* localStorage throws in a sandboxed iframe — e.g. a single-file HTML export previewed in
   Teams/SharePoint (about:srcdoc without allow-same-origin). Guard every access so the page
   degrades to session-only preferences instead of crashing. */
var dwLS = {
  get: function (k) { try { return localStorage.getItem(k); } catch (e) { return null; } },
  set: function (k, v) { try { localStorage.setItem(k, v); } catch (e) { /* sandboxed: ignore */ } }
};

(function () {
  var btn = document.getElementById('themeBtn');
  if (!btn) return;
  function sync() {
    btn.textContent = document.documentElement.getAttribute('data-theme') === 'dark' ? 'Light' : 'Dark';
  }
  btn.addEventListener('click', function () {
    var next = document.documentElement.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
    document.documentElement.setAttribute('data-theme', next);
    dwLS.set('dw-theme', next);
    sync();
  });
  sync();
})();

/* Focus view — fullscreen lightbox for handover pictures and tables.
   Standalone IIFE (the theme IIFE above bails early without #themeBtn).
   Scoped to main>article and out of editing zones, so the shared
   /app/site.js served in the editor is unaffected. file://-safe:
   pure DOM + inline SVG, no fetch. */
(function () {
  'use strict';

  var article = document.querySelector('main > article');
  if (!article) return;

  var figures = Array.prototype.slice.call(
    article.querySelectorAll('figure.dw-image-figure, figure.dw-table-figure')
  ).filter(function (f) { return !f.closest('[contenteditable], .ProseMirror'); });
  if (!figures.length) return;

  var EXPAND_SVG =
    '<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">' +
    '<path d="M9 4H4v5M15 4h5v5M9 20H4v-5M15 20h5v-5" fill="none" ' +
    'stroke="currentColor" stroke-width="2" stroke-linecap="round" ' +
    'stroke-linejoin="round"/></svg>';
  var CLOSE_SVG =
    '<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">' +
    '<path d="M6 6l12 12M18 6L6 18" fill="none" stroke="currentColor" ' +
    'stroke-width="2" stroke-linecap="round"/></svg>';

  var overlay, host, caption, closeBtn, opener = null;

  function isContent(t) {
    return t.closest('.dw-focus-img, .dw-focus-tablewrap, .dw-focus-cap, .dw-focus-close');
  }

  function lockScroll() {
    var sbw = window.innerWidth - document.documentElement.clientWidth;
    if (sbw > 0) document.body.style.paddingRight = sbw + 'px';
    document.body.style.overflow = 'hidden';
  }
  function unlockScroll() {
    document.body.style.overflow = '';
    document.body.style.paddingRight = '';
  }

  function captionText(figure, img) {
    var cap = figure.querySelector(':scope > figcaption');
    if (cap) return cap.textContent.trim();
    if (img && img.alt) return img.alt.trim();
    return '';
  }

  function mountImage(srcImg) {
    var img = document.createElement('img');
    img.className = 'dw-focus-img';
    img.src = srcImg.currentSrc || srcImg.getAttribute('src');
    img.alt = srcImg.alt || '';
    host.appendChild(img);

    var MIN = 1, MAX = 6, STEP = 1.6;
    var scale = 1, tx = 0, ty = 0;
    var pts = new Map();
    var startDist = 0, startScale = 1, anchorX = 0, anchorY = 0, startTx = 0, startTy = 0;
    var moved = false;

    function apply() {
      img.style.transform = 'translate(' + tx + 'px,' + ty + 'px) scale(' + scale + ')';
      img.style.cursor = scale > MIN ? (pts.size ? 'grabbing' : 'grab') : 'zoom-in';
    }
    function clamp(s) { return Math.max(MIN, Math.min(MAX, s)); }

    // Zoom toward a viewport point so the pixel under it stays put.
    function zoomAt(target, px, py) {
      var ns = clamp(target);
      if (ns === scale) return;
      var r = img.getBoundingClientRect();
      var cx = r.left + r.width / 2, cy = r.top + r.height / 2;
      var k = ns / scale;
      tx += (px - cx) * (1 - k);
      ty += (py - cy) * (1 - k);
      scale = ns;
      if (scale === MIN) { tx = 0; ty = 0; }
      apply();
    }

    function mid() {
      var xs = 0, ys = 0; pts.forEach(function (p) { xs += p.x; ys += p.y; });
      return { x: xs / pts.size, y: ys / pts.size };
    }
    function dist() {
      var a = []; pts.forEach(function (p) { a.push(p); });
      return Math.hypot(a[0].x - a[1].x, a[0].y - a[1].y);
    }

    img.addEventListener('wheel', function (e) {
      e.preventDefault();
      zoomAt(scale * (e.deltaY < 0 ? 1.12 : 1 / 1.12), e.clientX, e.clientY);
    }, { passive: false });

    img.addEventListener('pointerdown', function (e) {
      img.setPointerCapture(e.pointerId);
      pts.set(e.pointerId, { x: e.clientX, y: e.clientY });
      moved = false;
      startTx = tx; startTy = ty;
      if (pts.size === 2) {
        startDist = dist(); startScale = scale;
        var m = mid(); anchorX = m.x; anchorY = m.y;
      } else {
        anchorX = e.clientX; anchorY = e.clientY;
      }
      apply();
    });

    img.addEventListener('pointermove', function (e) {
      if (!pts.has(e.pointerId)) return;
      pts.set(e.pointerId, { x: e.clientX, y: e.clientY });
      if (pts.size === 2) {
        scale = clamp(startScale * (dist() / startDist));
        var m = mid();
        tx = startTx + (m.x - anchorX);
        ty = startTy + (m.y - anchorY);
        if (scale <= MIN) { tx = 0; ty = 0; }
        moved = true;
        apply();
      } else if (pts.size === 1) {
        if (Math.abs(e.clientX - anchorX) > 3 || Math.abs(e.clientY - anchorY) > 3) moved = true;
        if (scale > MIN) {
          tx = startTx + (e.clientX - anchorX);
          ty = startTy + (e.clientY - anchorY);
          apply();
        }
      }
    });

    function up(e) {
      if (!pts.has(e.pointerId)) return;
      var wasSingle = pts.size === 1;
      pts.delete(e.pointerId);
      if (wasSingle && !moved) {
        if (scale >= MAX) zoomAt(MIN, e.clientX, e.clientY);
        else zoomAt(scale * STEP, e.clientX, e.clientY);
      } else if (pts.size === 1) {
        // Pinch ended with one finger still down: re-seed the pan anchor to that
        // finger so continued dragging doesn't jump from the old pinch midpoint.
        var rem = null; pts.forEach(function (p) { rem = p; });
        anchorX = rem.x; anchorY = rem.y; startTx = tx; startTy = ty; moved = true;
      }
      apply();
    }
    img.addEventListener('pointerup', up);
    img.addEventListener('pointercancel', up);

    apply();
  }

  function mountTable(srcTable) {
    var wrap = document.createElement('div');
    wrap.className = 'dw-focus-tablewrap';
    var t = srcTable.cloneNode(true);
    t.removeAttribute('id');
    var cg = t.querySelector('colgroup');
    if (cg) cg.parentNode.removeChild(cg); // natural content widths in focus view
    wrap.appendChild(t);
    host.appendChild(wrap);
  }

  function onKey(e) {
    if (e.key === 'Escape') { close(); return; }
    if (e.key === 'Tab') { e.preventDefault(); closeBtn.focus(); } // single-control trap
  }

  function open(figure) {
    opener = figure.querySelector('.dw-focus-btn');
    host.innerHTML = '';
    // Branch on the figure CLASS, never querySelector('img'): a TABLE figure can
    // contain an <img> in a cell, which would otherwise mis-route to image mode.
    var img = null;
    if (figure.classList.contains('dw-image-figure')) {
      overlay.classList.remove('is-table');
      img = figure.querySelector('img');
      if (img) mountImage(img);
    } else {
      overlay.classList.add('is-table');
      var table = figure.querySelector('table');
      if (table) mountTable(table);
    }
    var text = captionText(figure, img);
    caption.textContent = text;
    caption.hidden = !text;
    lockScroll();
    overlay.hidden = false;
    document.addEventListener('keydown', onKey);
    closeBtn.focus();
  }

  function close() {
    overlay.hidden = true;
    host.innerHTML = '';
    caption.textContent = '';
    document.removeEventListener('keydown', onKey);
    unlockScroll();
    if (opener) opener.focus();
    opener = null;
  }

  function buildOverlay() {
    overlay = document.createElement('div');
    overlay.className = 'dw-focus-overlay';
    overlay.setAttribute('role', 'dialog');
    overlay.setAttribute('aria-modal', 'true');
    overlay.setAttribute('aria-label', 'Focused view');
    overlay.hidden = true;

    host = document.createElement('div');
    host.className = 'dw-focus-host';
    caption = document.createElement('figcaption');
    caption.className = 'dw-focus-cap';
    closeBtn = document.createElement('button');
    closeBtn.className = 'dw-focus-close';
    closeBtn.type = 'button';
    closeBtn.setAttribute('aria-label', 'Close focus view');
    closeBtn.innerHTML = CLOSE_SVG;

    overlay.appendChild(host);
    overlay.appendChild(caption);
    overlay.appendChild(closeBtn);
    document.body.appendChild(overlay);

    closeBtn.addEventListener('click', close);

    // Scrim-close guard: close only when BOTH pointerdown and pointerup land on
    // the dim area (not the image/table/caption/close) — so a pan that releases
    // over the scrim does not dismiss the view.
    var downScrim = false;
    overlay.addEventListener('pointerdown', function (e) { downScrim = !isContent(e.target); });
    overlay.addEventListener('pointerup', function (e) {
      if (downScrim && !isContent(e.target)) close();
      downScrim = false;
    });
  }

  figures.forEach(function (fig) {
    fig.classList.add('dw-focusable');
    var btn = document.createElement('button');
    btn.className = 'dw-focus-btn';
    btn.type = 'button';
    btn.setAttribute('aria-label', 'View in focus');
    btn.innerHTML = EXPAND_SVG;
    btn.addEventListener('click', function () { open(fig); });
    fig.appendChild(btn);
  });

  buildOverlay();
})();

/* ---- Content-only zoom (VISION L256) --------------------------------------------------
   Ctrl+wheel / Ctrl +/-/0 scale the document content (.dw-zoomable elements + the
   same-origin #vDocSurface iframe) — never the chrome. We set `zoom` INLINE (not via a CSS
   rule): the editor serves site.css wrapped in @scope (.dw-live-surface), so a rule here
   wouldn't apply there, and a body-level badge style would be inert. Persisted in
   localStorage; the editor's fixed port (5210) + WebView2 data folder keep the origin stable. */
(function () {
  var KEY = 'dw-content-zoom', MIN = 0.5, MAX = 3, STEP = 0.1;
  var z = parse(dwLS.get(KEY));

  function parse(v) { var n = parseFloat(v); return (isFinite(n) && n >= MIN && n <= MAX) ? n : 1; }
  function clamp(n) { return Math.min(MAX, Math.max(MIN, Math.round(n * 100) / 100)); }

  function targets() {
    var els = [].slice.call(document.querySelectorAll('.dw-zoomable'));
    var f = document.getElementById('vDocSurface');
    if (f) { try { var m = f.contentDocument && f.contentDocument.querySelector('main, body'); if (m) els.push(m); } catch (e) { /* cross-origin: skip */ } }
    return els;
  }
  function apply() {
    var v = z === 1 ? '' : String(z);
    targets().forEach(function (el) { el.style.zoom = v; });
    window.__dwContentZoom = z;
  }
  window.__dwApplyZoom = apply;

  var badge;
  function flash() {
    if (!badge) {
      badge = document.createElement('div'); badge.id = 'dwZoomBadge';
      badge.style.cssText = 'position:fixed;right:16px;bottom:16px;z-index:2147483647;' +
        'background:rgba(20,20,20,.85);color:#fff;font:600 13px/1 system-ui,sans-serif;' +
        'padding:6px 10px;border-radius:6px;pointer-events:none;opacity:0;transition:opacity .15s';
      document.body.appendChild(badge);
    }
    badge.textContent = Math.round(z * 100) + '%';
    badge.style.opacity = '1';
    clearTimeout(badge._t);
    badge._t = setTimeout(function () { badge.style.opacity = '0'; }, 900);
  }

  function setZoom(next, anchor) {
    var prev = z; z = clamp(next);
    apply(); dwLS.set(KEY, String(z)); flash();
    if (anchor && z !== prev) anchor(prev, z);
  }

  // Ctrl + wheel → zoom to the pointer. The scroller IS the zoomed element itself
  // (#editor2 / handover <main> are overflow:auto), so anchor on the .dw-zoomable under the
  // cursor. capture:true so we run before inner wheel handlers.
  function onWheel(e) {
    if (!e.ctrlKey) return;
    e.preventDefault();
    var el = e.target && e.target.closest ? e.target.closest('.dw-zoomable') : null;
    var factor = e.deltaY < 0 ? (1 + STEP) : (1 / (1 + STEP));
    setZoom(z * factor, el ? function (prev, now) {
      var r = now / prev, rc = el.getBoundingClientRect();
      var offY = e.clientY - rc.top, offX = e.clientX - rc.left;
      el.scrollTop = (el.scrollTop + offY) * r - offY;
      el.scrollLeft = (el.scrollLeft + offX) * r - offX;
    } : null);
  }

  // Ctrl + '='/'+' , '-' , '0' (reset). No pointer → no anchor; the scroller's top stays put.
  // Effective in the editor (WebView2 zoom disabled); best-effort in a plain-browser handover.
  function onKey(e) {
    if (!e.ctrlKey || e.shiftKey || e.altKey || e.metaKey) return;
    if (e.key === '=' || e.key === '+') { e.preventDefault(); setZoom(z + STEP); }
    else if (e.key === '-') { e.preventDefault(); setZoom(z - STEP); }
    else if (e.key === '0') { e.preventDefault(); setZoom(1); }
  }

  window.addEventListener('wheel', onWheel, { passive: false, capture: true });
  window.addEventListener('keydown', onKey);
  window.addEventListener('storage', function (e) { if (e.key === KEY) { z = parse(e.newValue); apply(); } });

  // The read-view iframe is same-origin but a separate document: its wheel/key events don't
  // reach this window. On load, apply the level AND attach the same handlers inside it.
  var frame = document.getElementById('vDocSurface');
  if (frame) frame.addEventListener('load', function () {
    apply();
    try {
      var w = frame.contentWindow;
      w.addEventListener('wheel', onWheel, { passive: false, capture: true });
      w.addEventListener('keydown', onKey);
    } catch (e) { /* cross-origin: skip */ }
  });

  apply(); // restore persisted level on load
})();
