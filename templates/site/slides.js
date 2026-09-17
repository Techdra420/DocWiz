(function () {
  var slides = document.querySelectorAll('.slide');
  if (slides.length === 0) return;
  var surface = document.getElementById('slidesSurface');
  var stage = document.getElementById('slideStage');
  var frame = document.getElementById('slideFrame');
  var prevBtn = document.getElementById('prevBtn');
  var nextBtn = document.getElementById('nextBtn');
  var ctr = document.getElementById('slideCtr');
  var idx = 0;

  /* ---- Auto-pagination (Spec 1) -------------------------------------------------------
     A .slide is a fixed-height box (overflow:hidden) inside the 1920×1080 frame, so content
     taller than the box would be CLIPPED. paginate() splits such a slide's single content
     region across generated "(cont.)" slides at block boundaries — lists between <li>, tables
     between rows (header repeated), other blocks moved whole; a single block taller than a
     whole slide is scaled to fit. Font size never shrinks → readable. Measured in the fixed
     1920-coordinate space (frame is pre-scale), so it's deterministic and identical on every
     screen and on both surfaces (handover + editor stage). Runs once before first show()/fit(). */
  var CLIP_EPS = 1;

  function overflowPx(slide) { return slide.scrollHeight - slide.clientHeight; }

  // The element whose DIRECT children are the splittable blocks: descend through a single
  // non-splittable wrapper (e.g. a transclusion's .dw-embed box) until a node with >1 child,
  // a splittable leaf (table/list), or a leaf. Without this a big-media slide whose region
  // holds ONE .dw-embed would be treated as one atomic block and scaled (squashed) — the
  // very thing we're removing — instead of split between the blocks inside the embed.
  function splitHostOf(region) {
    var host = region;
    while (host.children.length === 1) {
      var only = host.children[0];
      if (only.children.length === 0) break;            // leaf (<p>, <img>, …)
      if (/^(TABLE|UL|OL|FIGURE)$/.test(only.tagName)) break;  // splittable/atomic block — handled as oversize
      host = only;                                      // descend through wrapper (.dw-embed, …)
    }
    return host;
  }

  // Build an empty continuation slide mirroring the slide chrome (h1 + .slide-sub) and the
  // wrapper chain region → … → host, so moved blocks land in the same structure. Returns the
  // continuation and its matching host. "(cont.)" is appended to the visible title only.
  function makeContinuation(slide, region, host) {
    var cont = document.createElement('div');
    cont.className = 'slide';
    if (slide.getAttribute('data-layout')) cont.setAttribute('data-layout', slide.getAttribute('data-layout'));
    if (slide.getAttribute('data-src-anchor')) cont.setAttribute('data-src-anchor', slide.getAttribute('data-src-anchor'));
    var h1 = slide.querySelector(':scope > h1');
    if (h1) {
      var h = h1.cloneNode(true);
      if (!/\(cont\.\)\s*$/.test(h.textContent)) h.textContent = h.textContent.replace(/\s+$/, '') + ' (cont.)';
      cont.appendChild(h);
    }
    var sub = slide.querySelector(':scope > .slide-sub');
    if (sub) cont.appendChild(sub.cloneNode(true));

    var contRegion = region.cloneNode(false); // shallow: keeps class + data-region
    cont.appendChild(contRegion);
    var contHost = contRegion, node = region;
    while (node !== host) {
      node = node.children[0];                // the single wrapper we descended through
      var w = node.cloneNode(false);          // shallow clone (.dw-embed shell)
      contHost.appendChild(w);
      contHost = w;
    }
    return { cont: cont, contHost: contHost };
  }

  // Split the single oversize block in `host` so the slide fits: tables between tbody rows
  // (thead repeated on the continuation), lists between items, else scale an atomic block.
  function splitOversizeBlock(slide, host, contHost) {
    var block = host.children[0];
    if (!block) return;
    // A <figure> is a UNIT (image|table + figcaption): never move its caption to its own slide.
    if (block.tagName === 'FIGURE') {
      var innerTable = block.querySelector('table');
      if (innerTable) {
        // Split the figure's table by rows into a cloned figure>table (thead repeated). The
        // ORIGINAL figure keeps its caption; the continuation gets the overflow rows.
        var fig2 = block.cloneNode(false);
        var t2 = innerTable.cloneNode(false);
        var th = innerTable.querySelector('thead');
        if (th) t2.appendChild(th.cloneNode(true));
        var tb = innerTable.querySelector('tbody') || innerTable;
        var tb2 = innerTable.querySelector('tbody') ? tb.cloneNode(false) : t2;
        if (tb2 !== t2) t2.appendChild(tb2);
        fig2.appendChild(t2);
        contHost.insertBefore(fig2, contHost.firstChild);
        while (overflowPx(slide) > CLIP_EPS && tb.rows && tb.rows.length > 1) {
          tb2.insertBefore(tb.rows[tb.rows.length - 1], tb2.firstChild);
        }
        return;
      }
      // Image figure (img + caption): atomic — keep together, scale to fit (scaleToFit fallback).
      block.style.maxHeight = (1080 - 192) + 'px';
      var fimg = block.querySelector('img');
      if (fimg) { fimg.style.maxHeight = '100%'; fimg.style.objectFit = 'contain'; }
      return;
    }
    if (block.tagName === 'TABLE') {
      var tbody = block.querySelector('tbody') || block;
      var contTable = block.cloneNode(false);
      var thead = block.querySelector('thead');
      if (thead) contTable.appendChild(thead.cloneNode(true)); // repeat header
      var contBody = block.querySelector('tbody') ? tbody.cloneNode(false) : contTable;
      if (contBody !== contTable) contTable.appendChild(contBody);
      contHost.insertBefore(contTable, contHost.firstChild);
      while (overflowPx(slide) > CLIP_EPS && tbody.rows && tbody.rows.length > 1) {
        contBody.insertBefore(tbody.rows[tbody.rows.length - 1], contBody.firstChild);
      }
      return;
    }
    if (block.tagName === 'UL' || block.tagName === 'OL') {
      var contList = block.cloneNode(false);
      contHost.insertBefore(contList, contHost.firstChild);
      while (overflowPx(slide) > CLIP_EPS && block.children.length > 1) {
        contList.insertBefore(block.children[block.children.length - 1], contList.firstChild);
      }
      return;
    }
    // Atomic (image/figure) that alone exceeds a slide: scale to fit — it cannot be split.
    block.style.maxHeight = (1080 - 192) + 'px';
    var img = block.tagName === 'IMG' ? block : block.querySelector('img');
    if (img) { img.style.maxHeight = '100%'; img.style.objectFit = 'contain'; }
  }

  // Last resort for content that cannot be split smaller (a single table row / figure taller
  // than a whole slide): shrink the region with `zoom` (Chromium/Edge — the handover target,
  // same mechanism the editor already uses) so the slide never clips. This is the spec's
  // explicitly-allowed shrink for genuinely unsplittable oversize content; the common case
  // (split onto more slides) never reaches here, so readable slides stay full size.
  function scaleToFit(slide) {
    var region = slide.querySelector(':scope > .slide-region');
    if (!region) return;
    // Measure-and-shrink: step the region's zoom down until the slide actually fits (a single
    // computed factor under-corrects when h1/.src also contribute to the overflow). Bounded by
    // a 0.2 floor + iteration guard so it always terminates.
    var z = parseFloat(region.style.zoom) || 1;
    var guard = 0;
    while (overflowPx(slide) > CLIP_EPS && z > 0.2 && guard++ < 40) {
      z *= 0.95;
      region.style.zoom = z;
    }
  }

  function paginateSlide(slide) {
    var wasActive = slide.classList.contains('active');
    slide.classList.add('active'); // display:block so layout/scrollHeight are real
    try {
      if (overflowPx(slide) <= CLIP_EPS) return;
      var regions = slide.querySelectorAll(':scope > .slide-region');
      if (regions.length !== 1) return; // only single-region slides paginate (two-column deferred)
      var region = regions[0];
      var host = splitHostOf(region);

      var made = makeContinuation(slide, region, host);
      slide.parentNode.insertBefore(made.cont, slide.nextSibling);

      // Move whole trailing blocks until the slide fits or only one block remains in host.
      while (overflowPx(slide) > CLIP_EPS && host.children.length > 1) {
        made.contHost.insertBefore(host.children[host.children.length - 1], made.contHost.firstChild);
      }
      // One remaining block still overflows → split inside it.
      if (overflowPx(slide) > CLIP_EPS && host.children.length === 1) {
        splitOversizeBlock(slide, host, made.contHost);
      }
      if (made.contHost.children.length === 0) {
        made.cont.remove();
        scaleToFit(slide); // a single unsplittable oversize block — scale it (spec's allowed shrink)
        return;
      }
      if (overflowPx(slide) > CLIP_EPS) scaleToFit(slide); // an oversize remainder we can't split smaller
      paginateSlide(made.cont); // the continuation may itself overflow
    } finally {
      if (!wasActive) slide.classList.remove('active');
    }
  }

  function paginate() {
    Array.prototype.slice.call(frame.querySelectorAll('.slide')).forEach(paginateSlide);
  }

  /* The frame is a fixed 1920×1080 design surface, scaled to fit the viewport —
     slide typography is authored at TV size and stays proportional everywhere. */
  function fit() {
    var bar = document.querySelector('.slide-bar');
    var hint = document.querySelector('.hint');
    var pad = 48;
    var availW = surface.clientWidth - pad;
    var availH = surface.clientHeight - bar.offsetHeight - hint.offsetHeight - pad - 24;
    var s = Math.min(availW / 1920, availH / 1080);
    frame.style.transform = 'scale(' + s + ')';
    stage.style.width = (1920 * s) + 'px';
    stage.style.height = (1080 * s) + 'px';
  }

  function show(i) {
    idx = Math.max(0, Math.min(slides.length - 1, i));
    slides.forEach(function (s, n) { s.classList.toggle('active', n === idx); });
    ctr.textContent = (idx + 1) + ' / ' + slides.length;
    prevBtn.disabled = idx === 0;
    nextBtn.disabled = idx === slides.length - 1;
    if (location.hash !== '#' + (idx + 1)) history.replaceState(null, '', '#' + (idx + 1));
  }

  // PDF print mode (body.dw-pdf): no interactive chrome exists. Paginate exactly like the
  // on-screen deck (same engine, ADR-008), leave every slide displayed (print CSS stacks them
  // as 1920×1080 pages), then signal the PDF driver once images have loaded. No fit/show/nav.
  if (document.body.classList.contains('dw-pdf')) {
    var imgs = Array.prototype.slice.call(document.images);
    Promise.all(imgs.map(function (im) {
      return im.complete ? null : new Promise(function (res) {
        im.addEventListener('load', res); im.addEventListener('error', res);
      });
    })).then(function () {
      // Paginate AFTER images have loaded so table/figure heights are real. Paginating first
      // measures a table of unloaded cell images as short → it doesn't split → then the images
      // appear and the slide clips (the wide-icon-table bug).
      paginate();
      // Mark every slide .active so site.css's .slide.active[data-layout] grids apply (two-column,
      // big-media, etc.); the print CSS then stacks them as pages. Without .active the layouts fall
      // back to a plain block — the image goes full-width and the other column is clipped.
      Array.prototype.forEach.call(document.querySelectorAll('.slide'), function (s) { s.classList.add('active'); });
      window.__slidesReady = true;
    });
    return;
  }

  prevBtn.addEventListener('click', function () { show(idx - 1); });
  nextBtn.addEventListener('click', function () { show(idx + 1); });
  document.getElementById('fsBtn').addEventListener('click', function () {
    if (document.fullscreenElement) document.exitFullscreen();
    else surface.requestFullscreen(); // the dark surface fills the screen, frame rescales
  });
  document.addEventListener('keydown', function (e) {
    if (e.key === 'ArrowRight' || e.key === 'PageDown' || e.key === ' ') { show(idx + 1); e.preventDefault(); }
    if (e.key === 'ArrowLeft' || e.key === 'PageUp') { show(idx - 1); e.preventDefault(); }
    if (e.key === 'f' || e.key === 'F') document.getElementById('fsBtn').click();
  });
  window.addEventListener('resize', fit);
  window.addEventListener('load', fit);
  document.addEventListener('fullscreenchange', fit);
  // The script can run before the (i)frame has its final size — e.g. the editor's
  // stage iframe on first open. A ResizeObserver re-fits the moment the surface
  // actually gets its box, covering initial layout and any later parent reflow.
  if (window.ResizeObserver) new ResizeObserver(fit).observe(surface);
  // External hash navigation: browser back/forward, and the editor's WYSIWYG
  // stage which drives the deck by setting the hash.
  window.addEventListener('hashchange', function () {
    var n = parseInt(location.hash.slice(1), 10);
    if (!isNaN(n) && n - 1 !== idx) show(n - 1);
  });

  paginate();
  slides = document.querySelectorAll('.slide'); // re-query: pagination added (cont.) slides
  var fromHash = parseInt(location.hash.slice(1), 10);
  show(isNaN(fromHash) ? 0 : fromHash - 1);
  fit();
})();
