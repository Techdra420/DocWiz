/* DocWiz visual self-check probe (dev/verification only — not part of the product UI).

   Purpose: give the AGENT measurable facts about the live slide surface instead of making
   it (or the human) eyeball a screenshot. The bugs that cost this branch the most time —
   cards overlap/clip, text unreadable on a dark slide, edit surface not matching read —
   are all GEOMETRIC or COLOR facts. This reads them straight off the rendered DOM with
   getBoundingClientRect + getComputedStyle, so a headless Edge run yields pass/fail JSON
   instead of a picture someone has to interpret.

   Dependency-free classic script (no import map, no PM). Loads on every page but does
   nothing unless the URL carries ?probe=1, so it has zero effect on the real editor or
   the shipped handover.

   === Modes ===
   SLIDE mode   — for the slide view / view editor surface. Checks per-slide region
                  overlap, clipping, and contrast. Activated when a .dw-live-surface
                  containing .slide elements is present.
   CONTENT mode — for the chapter editor live surface and handover <article> pages.
                  Checks contrast, vertical clipping, and horizontal overflow of content
                  blocks (table, figure, callout, pre, img, …).

   Run headlessly (slide example):
     msedge --headless=new --virtual-time-budget=9000 --dump-dom \
       "http://127.0.0.1:PORT/?probe=1#view=user-training.yaml"

   Run headlessly (chapter editor example):
     msedge --headless=new --virtual-time-budget=20000 --dump-dom \
       "http://127.0.0.1:PORT/?probe=1#chapter=content/030-operations/020-reports-overview.md"

   Run headlessly (handover page example):
     msedge --headless=new --virtual-time-budget=9000 --dump-dom \
       "file:///path/to/handover/chapter-id.html?probe=1"

   window.DocWizProbe.run(surfaceSelector?)        — slide mode
   window.DocWizProbe.runContent(containerEl)      — content mode
   window.DocWizProbe.compareEditVsShip(...)       — WYSIWYG fidelity gate */
(function () {
  'use strict';

  var TEXT_SEL = 'h1,h2,h3,h4,p,li,td,th,figcaption,blockquote,code,span,strong,em';
  var CONTENT_BLOCK_SEL = 'table,.dw-figure,.dw-table-figure,.dw-callout,.dw-embed,pre,figure,img';
  var CONTRAST_AA = 4.5;       // WCAG AA, normal text
  var CONTRAST_AA_LARGE = 3.0; // >=24px, or >=18.66px bold
  var OVERLAP_EPS = 1;         // sub-pixel rounding tolerance (px)
  var CLIP_EPS = 1;            // content taller than box by more than this = clipped

  function rectOf(el) {
    var r = el.getBoundingClientRect();
    return { x: Math.round(r.left), y: Math.round(r.top), w: Math.round(r.width), h: Math.round(r.height) };
  }

  // Two rects overlap if they intersect on both axes by more than the rounding epsilon.
  function overlaps(a, b) {
    var ix = Math.min(a.x + a.w, b.x + b.w) - Math.max(a.x, b.x);
    var iy = Math.min(a.y + a.h, b.y + b.h) - Math.max(a.y, b.y);
    return ix > OVERLAP_EPS && iy > OVERLAP_EPS ? { x: ix, y: iy } : null;
  }

  // sRGB channel -> linear, for relative luminance (WCAG 2.x).
  function lin(c) { c /= 255; return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); }
  function luminance(rgb) { return 0.2126 * lin(rgb[0]) + 0.7152 * lin(rgb[1]) + 0.0722 * lin(rgb[2]); }
  function contrast(fg, bg) {
    var l1 = luminance(fg), l2 = luminance(bg);
    var hi = Math.max(l1, l2), lo = Math.min(l1, l2);
    return (hi + 0.05) / (lo + 0.05);
  }

  // Parse computed rgb()/rgba(); returns [r,g,b,a] or null.
  function parseColor(s) {
    var m = /rgba?\(([^)]+)\)/.exec(s || '');
    if (!m) return null;
    var p = m[1].split(',').map(function (x) { return parseFloat(x); });
    return [p[0], p[1], p[2], p.length > 3 ? p[3] : 1];
  }

  // Effective background: walk ancestors until a non-transparent background-color. If any
  // ancestor paints a background-image we cannot reason about the pixel behind the text, so
  // we report that honestly rather than guess.
  function effectiveBg(el) {
    var node = el;
    while (node && node.nodeType === 1) {
      var cs = getComputedStyle(node);
      if (cs.backgroundImage && cs.backgroundImage !== 'none') return { image: true };
      var c = parseColor(cs.backgroundColor);
      if (c && c[3] > 0) return { rgb: [c[0], c[1], c[2]] };
      node = node.parentElement;
    }
    return { rgb: [255, 255, 255] }; // assume white page if nothing painted
  }

  function isLargeText(cs) {
    var px = parseFloat(cs.fontSize);
    var bold = parseInt(cs.fontWeight, 10) >= 700;
    return px >= 24 || (bold && px >= 18.66);
  }

  // A node is a "text leaf" if it has its own visible text not already counted by a child
  // element in TEXT_SEL (avoids double-counting a <p> and the <span> inside it).
  function ownText(el) {
    var t = '';
    for (var i = 0; i < el.childNodes.length; i++) {
      var n = el.childNodes[i];
      if (n.nodeType === 3) t += n.nodeValue;
    }
    return t.trim();
  }

  function checkContrast(containerEl) {
    var out = [];
    var els = containerEl.querySelectorAll(TEXT_SEL);
    for (var i = 0; i < els.length; i++) {
      var el = els[i];
      var txt = ownText(el);
      if (!txt) continue;
      var cs = getComputedStyle(el);
      if (cs.visibility === 'hidden' || cs.display === 'none' || parseFloat(cs.opacity) === 0) continue;
      var fg = parseColor(cs.color);
      if (!fg) continue;
      var bg = effectiveBg(el);
      var sample = txt.length > 32 ? txt.slice(0, 32) + '…' : txt;
      if (bg.image) { out.push({ text: sample, ratio: null, note: 'over background-image — contrast not computable' }); continue; }
      var ratio = contrast(fg, bg.rgb);
      var min = isLargeText(cs) ? CONTRAST_AA_LARGE : CONTRAST_AA;
      if (ratio < min) {
        out.push({
          text: sample,
          ratio: Math.round(ratio * 100) / 100,
          min: min,
          fg: 'rgb(' + fg.slice(0, 3).join(',') + ')',
          bg: 'rgb(' + bg.rgb.join(',') + ')',
          fontPx: Math.round(parseFloat(cs.fontSize)),
        });
      }
    }
    return out;
  }

  // ===========================================================================================
  // CONTENT MODE
  // Checks content blocks (tables, figures, callouts, pre, img) for:
  //   - vertical clipping (scrollHeight > clientHeight + CLIP_EPS)
  //   - horizontal overflow (scrollWidth > clientWidth + CLIP_EPS)
  //   - contrast failures (reuses checkContrast, filters ratio !== null)
  // Returns { verdict, mode:'content', totals, clipped, overflowing, lowContrast, when }.
  // verdict PASS iff clipped.length + overflowing.length + lowContrast.length === 0.

  function snippetOf(el) {
    var t = (el.textContent || '').replace(/\s+/g, ' ').trim();
    return t.length > 48 ? t.slice(0, 48) + '…' : t;
  }

  function runContent(containerEl) {
    if (!containerEl) return { error: 'no content container found', mode: 'content' };

    var blocks = Array.prototype.slice.call(containerEl.querySelectorAll(CONTENT_BLOCK_SEL));

    var clipped = [];
    var overflowing = [];

    blocks.forEach(function (el) {
      var cs = getComputedStyle(el);
      if (cs.display === 'none' || cs.visibility === 'hidden') return;

      var tag = el.tagName.toLowerCase();
      var cls = el.className || '';
      var text = snippetOf(el);

      // Vertical clipping: rendered box shorter than scroll height.
      var clipBy = el.scrollHeight - el.clientHeight;
      if (clipBy > CLIP_EPS) {
        clipped.push({ tag: tag, cls: cls, by: clipBy, text: text });
      }

      // Horizontal overflow: content wider than the box.
      var overBy = el.scrollWidth - el.clientWidth;
      if (overBy > CLIP_EPS) {
        overflowing.push({ tag: tag, cls: cls, by: overBy, text: text });
      }
    });

    var lowContrast = checkContrast(containerEl).filter(function (c) { return c.ratio !== null; });

    // Pictures rendered NARROWER than their set width attribute: a table column squeezing an
    // in-cell image (max-width:100% shrinks it to the cell content box). The author asked for
    // N px; if it renders < N, the layout is overriding the intended size.
    var shrunkImgs = [];
    Array.prototype.slice.call(containerEl.querySelectorAll('img[width]')).forEach(function (img) {
      var set = parseInt(img.getAttribute('width'), 10);
      if (!set) return;
      var rendered = Math.round(img.getBoundingClientRect().width);
      if (rendered > 0 && rendered < set - 1) {
        shrunkImgs.push({ src: (img.getAttribute('src') || '').slice(-48), set: set, rendered: rendered });
      }
    });

    var verdict = (clipped.length + overflowing.length + lowContrast.length + shrunkImgs.length) === 0 ? 'PASS' : 'FAIL';
    return {
      verdict: verdict,
      mode: 'content',
      totals: {
        elements: blocks.length,
        clipped: clipped.length,
        overflowing: overflowing.length,
        lowContrast: lowContrast.length,
        shrunkImgs: shrunkImgs.length,
      },
      clipped: clipped,
      overflowing: overflowing,
      lowContrast: lowContrast,
      shrunkImgs: shrunkImgs,
      when: new Date().toISOString(),
    };
  }

  // ===========================================================================================
  // SLIDE MODE (unchanged)

  // Pick the live surface that actually holds slides. The page has several
  // .dw-live-surface containers (the chapter editor's #editor2 is first in DOM but empty
  // on the slide view), so a bare querySelector grabs the wrong one — prefer whichever
  // surface contains .slide elements, falling back to the slide canvas / first match.
  function pickSurface(sel) {
    if (sel && sel !== '.dw-live-surface') return document.querySelector(sel);
    var all = Array.prototype.slice.call(document.querySelectorAll('.dw-live-surface'));
    var withSlides = all.filter(function (s) { return s.querySelector('.slide'); });
    return withSlides[0] || document.getElementById('vEditSurface') || all[0] || null;
  }

  function run(surfaceSel) {
    var surface = pickSurface(surfaceSel);
    if (!surface) return { error: 'no .dw-live-surface with slides found' };

    var slideEls = Array.prototype.slice.call(surface.querySelectorAll('.slide'));
    var slides = [];
    var totals = { slides: slideEls.length, zones: 0, overlaps: 0, clipped: 0, lowContrast: 0, emptyZones: 0 };

    slideEls.forEach(function (slideEl, i) {
      var layout = slideEl.getAttribute('data-layout') || '(none)';
      var regionEls = Array.prototype.slice.call(slideEl.querySelectorAll('.slide-region[data-region]'));
      var regions = regionEls.map(function (rEl) {
        var rect = rectOf(rEl);
        var clipped = rEl.scrollHeight - rEl.clientHeight > CLIP_EPS;
        // Deep textContent (not ownText): zone text lives nested in .ProseMirror > p, so a
        // shallow direct-child check reports every text zone as empty (a probe bug found by
        // this very run). A zone is empty only if it has no text AND no embedded media.
        var empty = (rEl.textContent || '').trim() === '' && rEl.querySelectorAll('img,figure,table,svg').length === 0;
        if (clipped) totals.clipped++;
        if (empty) totals.emptyZones++;
        totals.zones++;
        return {
          region: rEl.getAttribute('data-region'),
          rect: rect,
          clipped: clipped,
          clipBy: clipped ? rEl.scrollHeight - rEl.clientHeight : 0,
          empty: empty,
        };
      });

      // Pairwise region overlap within this slide.
      var overlapPairs = [];
      for (var a = 0; a < regions.length; a++) {
        for (var b = a + 1; b < regions.length; b++) {
          var ov = overlaps(regions[a].rect, regions[b].rect);
          if (ov) { overlapPairs.push({ a: regions[a].region, b: regions[b].region, by: ov }); totals.overlaps++; }
        }
      }

      var lowContrast = checkContrast(slideEl).filter(function (c) { return c.ratio !== null; });
      totals.lowContrast += lowContrast.length;

      slides.push({
        index: i,
        layout: layout,
        title: (slideEl.querySelector('[data-slide-title], .slide-title, h1, h2') || {}).textContent || '',
        rect: rectOf(slideEl),
        regions: regions,
        overlaps: overlapPairs,
        lowContrast: lowContrast,
      });
    });

    totals.zonesMounted = surface.querySelectorAll('.dw-zone').length;
    var verdict = totals.overlaps + totals.clipped + totals.lowContrast === 0 ? 'PASS' : 'FAIL';
    return { verdict: verdict, totals: totals, slides: slides, when: new Date().toISOString() };
  }

  // ===========================================================================================
  // WYSIWYG edit-vs-ship gate (the guarantee). Captures each slide's per-region geometry
  // NORMALIZED to the 1920×1080 logical slide coordinate (so the ship deck's transform:scale and
  // the edit surface's CSS zoom divide out and the two are directly comparable) plus a structural
  // signature (the region's effective layout-box child tag sequence), on BOTH surfaces, then
  // diffs them per slide/region. A few logical px of tolerance absorbs rounding/font-hinting.
  //
  // window.DocWizProbe.compareEditVsShip(shipSel, editSel) — both selectors resolve to a container
  // holding .slide elements (the ship deck's .slide-frame and the edit .dw-slide-edit surface).
  // The committed harness editor2/wysiwyg.html drives this headlessly; see that file to re-run.

  var GEOM_EPS = 3; // logical px tolerance on a 1920-wide slide (rounding + hinting)

  // The elements that actually generate layout boxes under `el`: an element with
  // `display:contents` paints no box, so its CHILDREN are the real layout children (this is the
  // whole point of the convergence — the edit surface's .ProseMirror / .dw-embed-host are
  // display:contents, so a region's effective children become the same blocks ship lays out).
  function layoutChildren(el) {
    var out = [];
    for (var i = 0; i < el.children.length; i++) {
      var c = el.children[i];
      var cs = getComputedStyle(c);
      if (cs.display === 'none') continue;
      // An absolutely-positioned, zero-flow affordance (e.g. an empty title/subtitle hint, or
      // the LINKED pill) is not a flow box — exclude it: ship omits it entirely.
      if (cs.position === 'absolute' || cs.position === 'fixed') continue;
      // Descend through transparent wrappers: a display:contents element paints no box, and the
      // per-zone editor's .ProseMirror root is a real (clickable, editable) box but is a pure
      // editing wrapper that should compare as transparent — its children are the same blocks the
      // deck lays out directly in the region. (.ProseMirror is a real box for editability; the
      // gate looks THROUGH it so edit and ship still read structurally identical.)
      if (cs.display === 'contents' || c.classList.contains('ProseMirror')) { out = out.concat(layoutChildren(c)); continue; }
      out.push(c);
    }
    return out;
  }

  // A compact, surface-independent tag for one layout child: tagName + the content classes that
  // affect layout (the generated-content hooks), dropping edit-only chrome classes so the two
  // surfaces read the same. e.g. "DIV.dw-embed", "UL", "H2", "DIV.aud".
  var EDIT_ONLY_CLASS = /^(dw-zone|dw-zone-error|e2-[\w-]*|ProseMirror[\w-]*|is-empty|active)$/;
  function sig(el) {
    var keep = [];
    for (var i = 0; i < el.classList.length; i++) {
      var cl = el.classList[i];
      if (!EDIT_ONLY_CLASS.test(cl)) keep.push(cl);
    }
    keep.sort();
    return el.tagName + (keep.length ? '.' + keep.join('.') : '');
  }

  // Normalize a child rect to logical 1920-space relative to its slide's top-left, dividing out
  // the slide's render scale (rendered slide width / 1920 design width).
  function logicalRect(rect, slideRect) {
    var scale = slideRect.w / 1920 || 1;
    return {
      x: Math.round((rect.x - slideRect.x) / scale),
      y: Math.round((rect.y - slideRect.y) / scale),
      w: Math.round(rect.w / scale),
      h: Math.round(rect.h / scale),
    };
  }

  // Capture one surface's slides as [{ index, layout, head:{rect,sig}|null, sub:..., regions:[
  // { region, rect(logical), children:[sig...] } ] }]. head/sub are the slide's h1/.slide-sub
  // (ship omits an empty one; edit pulls an empty one out of flow so it isn't a flow box).
  function captureSurface(surfaceEl) {
    var slideEls = Array.prototype.slice.call(surfaceEl.querySelectorAll('.slide'));
    return slideEls.map(function (slideEl, i) {
      var srect = rectOf(slideEl);
      function headOf(sel) {
        var el = slideEl.querySelector(':scope > ' + sel);
        if (!el) return null;
        var cs = getComputedStyle(el);
        if (cs.display === 'none' || cs.position === 'absolute' || cs.position === 'fixed') return null;
        return { rect: logicalRect(rectOf(el), srect), sig: sig(el) };
      }
      var regionEls = Array.prototype.slice.call(slideEl.querySelectorAll('.slide-region[data-region]'));
      var regions = regionEls.map(function (rEl) {
        // Diagnostic: the first contained media box (figure/table/img). When a region's rect
        // diverges but its child structure matches, this localises WHERE the height comes from
        // (e.g. a table-figure breakout wrapping differently) — printed only for triage.
        var mediaEl = rEl.querySelector('figure,table,img');
        var tableEl = rEl.querySelector('table');
        var capEl = rEl.querySelector('figcaption');
        return {
          region: rEl.getAttribute('data-region'),
          rect: logicalRect(rectOf(rEl), srect),
          children: layoutChildren(rEl).map(sig),
          media: mediaEl ? { tag: sig(mediaEl), rect: logicalRect(rectOf(mediaEl), srect) } : null,
          table: tableEl ? { rect: logicalRect(rectOf(tableEl), srect),
            scrollW: Math.round(tableEl.scrollWidth), clientW: Math.round(tableEl.clientWidth),
            ofx: getComputedStyle(mediaEl || tableEl).overflowX } : null,
          cap: capEl ? logicalRect(rectOf(capEl), srect) : null,
        };
      });
      return {
        index: i,
        layout: slideEl.getAttribute('data-layout') || '(none)',
        head: headOf('h1'),
        sub: headOf('.slide-sub'),
        regions: regions,
      };
    });
  }

  function rectClose(a, b) {
    return Math.abs(a.x - b.x) <= GEOM_EPS && Math.abs(a.y - b.y) <= GEOM_EPS
      && Math.abs(a.w - b.w) <= GEOM_EPS && Math.abs(a.h - b.h) <= GEOM_EPS;
  }
  function rectStr(r) { return '[' + r.x + ',' + r.y + ' ' + r.w + '×' + r.h + ']'; }

  function diffHead(slideIdx, which, ship, edit, mismatches) {
    if (!ship && !edit) return;
    if (!ship || !edit) {
      mismatches.push({ slide: slideIdx, region: which, problem: 'present on only one surface',
        ship: ship ? ship.sig + ' ' + rectStr(ship.rect) : null,
        edit: edit ? edit.sig + ' ' + rectStr(edit.rect) : null });
      return;
    }
    if (!rectClose(ship.rect, edit.rect))
      mismatches.push({ slide: slideIdx, region: which, problem: 'rect differs',
        ship: rectStr(ship.rect), edit: rectStr(edit.rect) });
  }

  function compareEditVsShip(shipSel, editSel) {
    var shipSurface = typeof shipSel === 'string' ? document.querySelector(shipSel) : shipSel;
    var editSurface = typeof editSel === 'string' ? document.querySelector(editSel) : editSel;
    if (!shipSurface || !editSurface)
      return { verdict: 'ERR', error: 'ship or edit surface not found', shipSel: shipSel, editSel: editSel };

    var ship = captureSurface(shipSurface);
    var edit = captureSurface(editSurface);
    var mismatches = [];

    if (ship.length !== edit.length)
      mismatches.push({ problem: 'slide count differs', ship: ship.length, edit: edit.length });

    var n = Math.min(ship.length, edit.length);
    for (var i = 0; i < n; i++) {
      var s = ship[i], e = edit[i];
      if (s.layout !== e.layout)
        mismatches.push({ slide: i, problem: 'layout differs', ship: s.layout, edit: e.layout });
      diffHead(i, 'h1', s.head, e.head, mismatches);
      diffHead(i, '.slide-sub', s.sub, e.sub, mismatches);

      // Match regions by data-region name (order is identical, but name-match is robust).
      var eByName = {};
      e.regions.forEach(function (r) { eByName[r.region] = r; });
      s.regions.forEach(function (sr) {
        var er = eByName[sr.region];
        if (!er) {
          mismatches.push({ slide: i, region: sr.region, problem: 'region missing on edit surface' });
          return;
        }
        if (!rectClose(sr.rect, er.rect))
          mismatches.push({ slide: i, region: sr.region, problem: 'region rect differs',
            ship: rectStr(sr.rect), edit: rectStr(er.rect) });
        if (sr.children.join(',') !== er.children.join(','))
          mismatches.push({ slide: i, region: sr.region, problem: 'region child structure differs',
            ship: sr.children, edit: er.children });
      });
      // Regions present in edit but not ship (e.g. an edit-only empty zone ship omits).
      var sNames = {};
      s.regions.forEach(function (r) { sNames[r.region] = true; });
      e.regions.forEach(function (er) {
        if (!sNames[er.region])
          mismatches.push({ slide: i, region: er.region, problem: 'region only on edit surface (ship omits it)',
            edit: rectStr(er.rect) + ' ' + er.children.join(',') });
      });
    }

    return {
      verdict: mismatches.length === 0 ? 'PASS' : 'FAIL',
      slideCount: { ship: ship.length, edit: edit.length },
      mismatches: mismatches,
      ship: ship,
      edit: edit,
      when: new Date().toISOString(),
    };
  }

  // ===========================================================================================
  // SLIDES-FIT MODE — handover slides page (#slideFrame with .slide, NO .dw-live-surface).
  // Measures every slide's natural content height against its fixed canvas box: a slide is a
  // fixed-height box (overflow:hidden), so scrollHeight > clientHeight means content is CLIPPED
  // off the bottom. Inactive slides are display:none, so each is made .active to be measurable.
  // This is the per-slide check content-mode could not do (it only ever saw the active slide).
  function runSlidesFit() {
    var frame = document.getElementById('slideFrame');
    if (!frame) return { verdict: 'ERR', mode: 'slides-fit', error: 'no #slideFrame on page' };
    var slideEls = Array.prototype.slice.call(frame.querySelectorAll('.slide'));
    var clipped = [];
    slideEls.forEach(function (s, i) {
      var wasActive = s.classList.contains('active');
      s.classList.add('active'); // display:block so it lays out and reports scrollHeight
      var by = s.scrollHeight - s.clientHeight;
      if (by > CLIP_EPS) {
        clipped.push({
          index: i,
          by: by,
          anchor: s.getAttribute('data-src-anchor') || '',
          title: ((s.querySelector('h1') || {}).textContent || '').trim().slice(0, 48),
        });
      }
      if (!wasActive) s.classList.remove('active');
    });
    return {
      verdict: clipped.length === 0 ? 'PASS' : 'FAIL',
      mode: 'slides-fit',
      totals: { slides: slideEls.length, clipped: clipped.length },
      clipped: clipped,
      when: new Date().toISOString(),
    };
  }

  // ===========================================================================================
  // FOCUS-VIEW MODE — handover lightbox (?probe=1&focus=1). Asserts every eligible figure
  // has a focus button, then opens the first one and measures the open overlay: the close
  // button must NOT overlap the content, and an image must not overflow its host. Closes
  // again so the DOM is left clean.
  function runFocusView(containerEl) {
    var root = containerEl || document.querySelector('main article') || document.body;
    var figs = Array.prototype.slice.call(
      root.querySelectorAll('figure.dw-image-figure, figure.dw-table-figure'));
    var problems = [];

    var missingBtn = figs.filter(function (f) { return !f.querySelector('.dw-focus-btn'); }).length;
    if (missingBtn) problems.push({ kind: 'missing-button', count: missingBtn });

    var measured = null, notes = [];
    if (figs.length) {
      var btn = figs[0].querySelector('.dw-focus-btn');
      if (btn) {
        btn.click();
        var ov = document.querySelector('.dw-focus-overlay');
        if (!ov || ov.hidden) {
          problems.push({ kind: 'overlay-did-not-open' });
        } else {
          var isImage = !!ov.querySelector('.dw-focus-img');
          var content = ov.querySelector('.dw-focus-img') || ov.querySelector('.dw-focus-tablewrap');
          var closeB = ov.querySelector('.dw-focus-close');
          var hostEl = ov.querySelector('.dw-focus-host');
          var cr = rectOf(content);
          // The overlay is position:fixed inset:0 — it must NEVER scroll past the
          // viewport (a too-tall image or table panel doing so is the failure mode
          // a small fixture hides; large content must bound + scroll INSIDE the host).
          var oOver = ov.scrollHeight - ov.clientHeight;
          if (oOver > CLIP_EPS) problems.push({ kind: 'overlay-overflows-viewport', by: oOver });
          // A measurable image must sit fully within the viewport (contained, not cropped).
          if (isImage && cr.h > 1) {
            var vh = window.innerHeight;
            if (cr.y < -CLIP_EPS || cr.y + cr.h > vh + CLIP_EPS)
              problems.push({ kind: 'image-overflows-viewport', top: cr.y, bottom: cr.y + cr.h, vh: vh });
          }
          // Close button must not overlap real content. Guard on a measured (non-zero)
          // content rect — a freshly-cloned <img> may not be laid out at this synchronous
          // measure, in which case image geometry is screenshot-authoritative (a note,
          // not a pass/fail), and the deterministic checks are: every figure has a button,
          // the overlay opens, the table case measures, and close-overlap when measurable.
          if (cr.h > 1 && cr.w > 1) {
            var ovl = overlaps(rectOf(closeB), cr);
            if (ovl) problems.push({ kind: 'close-overlaps-content', by: ovl });
            if (isImage) {
              var overX = hostEl.scrollWidth - hostEl.clientWidth;
              if (overX > CLIP_EPS) problems.push({ kind: 'image-overflow', by: overX });
            }
          } else if (isImage) {
            notes.push({ kind: 'image-not-laid-out', detail: 'verify image geometry by screenshot' });
          } else {
            problems.push({ kind: 'table-not-measurable', rect: cr });
          }
          measured = { content: cr, close: rectOf(closeB), kind: isImage ? 'image' : 'table' };
        }
        var cb = ov && ov.querySelector('.dw-focus-close');
        if (cb) cb.click();
      }
    }

    return {
      verdict: problems.length === 0 ? 'PASS' : 'FAIL',
      mode: 'focus-view',
      totals: { figures: figs.length },
      problems: problems,
      notes: notes,
      measured: measured,
      when: new Date().toISOString(),
    };
  }

  window.DocWizProbe = { run: run, runContent: runContent, compareEditVsShip: compareEditVsShip, runSlidesFit: runSlidesFit, runFocusView: runFocusView };

  // ===========================================================================================
  // Headless auto-run (?probe=1)
  //
  // Mode selection:
  //   SLIDE mode   — a .dw-live-surface with .slide children is present (and stable).
  //   CONTENT mode — no slide surface found; look for content in this priority order:
  //                  1. #editor2.dw-live-surface  (chapter editor live surface)
  //                  2. main article              (handover base.html)
  //                  3. main                      (fallback)
  //                  4. body                      (last resort)
  //
  // The chapter editor mounts content asynchronously after the hash route fires; we poll
  // until the container has non-empty content or we give up. The handover's content is
  // static (already present on DOMContentLoaded); a short settle is enough.

  if (/[?&]probe=1\b/.test(location.search)) {
    var stableTicks = 0, lastCount = -1, waited = 0;
    var POLL_MS = 200;
    var MAX_WAIT_MS = 12000; // generous — chapter editor can be slow on first mount

    function pickContentContainer() {
      // Prefer the chapter editor surface (already scoped by @scope in the editor).
      var e2 = document.getElementById('editor2');
      if (e2 && (e2.textContent || '').trim().length > 0) return e2;
      // Handover and fallbacks.
      var art = document.querySelector('main article');
      if (art) return art;
      var main = document.querySelector('main');
      if (main) return main;
      return document.body;
    }

    function hasSlideSurface() {
      var all = Array.prototype.slice.call(document.querySelectorAll('.dw-live-surface'));
      return all.some(function (s) { return s.querySelector('.slide'); });
    }

    function contentReady() {
      // In slide mode: wait for slides to mount (same as before).
      if (hasSlideSurface()) return true;
      // In content mode for the chapter editor: wait for #editor2 to have visible text.
      var e2 = document.getElementById('editor2');
      if (e2) return (e2.textContent || '').trim().length > 20;
      // Handover / static: always ready.
      return true;
    }

    var poll = setInterval(function () {
      var slideMode = hasSlideSurface();

      // For slide mode, track slide count stability (existing logic).
      if (slideMode) {
        var surface = document.querySelector('.dw-live-surface');
        var count = surface ? surface.querySelectorAll('.slide').length : 0;
        var zones = surface ? surface.querySelectorAll('.dw-zone').length : 0;
        if (count > 0 && count === lastCount && (zones > 0 || waited > 6000)) stableTicks++;
        else stableTicks = 0;
        lastCount = count;
      } else {
        // Content mode: increment stableTicks when content is ready.
        if (contentReady()) stableTicks++;
        else stableTicks = 0;
      }

      waited += POLL_MS;

      if (stableTicks >= 3 || waited > MAX_WAIT_MS) {
        clearInterval(poll);

        var result;
        if (/[?&]focus=1\b/.test(location.search)) {
          // Focus-view lightbox check (must win over content mode).
          result = runFocusView(pickContentContainer());
        } else if (document.getElementById('slideFrame') && !slideMode) {
          // Handover slides page (fixed-canvas deck, no .dw-live-surface): per-slide fit.
          result = runSlidesFit();
        } else if (slideMode) {
          result = run('.dw-live-surface');
        } else {
          var container = pickContentContainer();
          result = runContent(container);
        }

        var pre = document.createElement('pre');
        pre.id = 'dwProbeOut';
        pre.textContent = JSON.stringify(result, null, 2);
        document.body.appendChild(pre);
        document.title = 'PROBE-' + (result.verdict || 'ERR') + ' DocWiz';
      }
    }, POLL_MS);
  }
})();
