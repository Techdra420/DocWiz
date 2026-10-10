/* DocWiz Editor frontend. Talks to EditorServer's API; chapter editing is the live
   surface (editor2/ — ProseMirror over pipeline HTML, ADR-007). Toast UI was removed
   at the M5 flip (2026-06-12) after stakeholder sign-off. */
(function () {
  'use strict';

  // Timing (ms). TIP_HOVER_DELAY is stakeholder-ratified: tips popping at 400ms got
  // in the way of normal mousing — they are explanations for when you LINGER.
  var EDITOR2_RETRY_MS = 120;        // poll until the live-surface module is loaded
  var TOAST_HIDE_MS = 8000;          // error toast auto-dismiss
  var TIP_HOVER_DELAY = 1500;
  var TIP_FOCUS_DELAY = 150;
  var PREVIEW_DEBOUNCE = 400;        // canvas refresh after structural changes
  var PREVIEW_DEBOUNCE_TITLE = 800;  // title field re-renders the whole canvas

  // Bottom-right transient toast. kind ('error' default | 'success') colours the title
  // and left border (see .toast-* rules in app.css). Returns the element so callers can
  // make it clickable (e.g. open the export folder). Auto-dismisses after TOAST_HIDE_MS.
  function showToast(title, message, kind) {
    var toast = document.getElementById('errToast');
    if (!toast) {
      toast = document.createElement('div');
      toast.id = 'errToast';
      document.body.appendChild(toast);
    }
    toast.className = '';
    toast.classList.add('toast-' + (kind || 'error'));
    toast.onclick = null;
    toast.style.cursor = '';
    toast.innerHTML = '';
    var t = document.createElement('b'); t.textContent = title;
    var b = document.createElement('span'); b.textContent = message;
    toast.appendChild(t); toast.appendChild(b);
    toast.classList.add('show');
    clearTimeout(toast._t);
    toast._t = setTimeout(function () { toast.classList.remove('show'); }, TOAST_HIDE_MS);
    return toast;
  }

  // Errors must never be silent: any uncaught error shows a toast and marks the DOM
  // (data-js-error) so headless smoke tests can detect breakage.
  function reportError(message) {
    document.documentElement.setAttribute('data-js-error', String(message));
    showToast('Something went wrong',
      message + ' — try again; if it persists, restart the editor.', 'error');
  }
  window.addEventListener('error', function (e) { reportError(e.message || 'unknown error'); });
  window.addEventListener('unhandledrejection', function (e) {
    reportError((e.reason && e.reason.message) || 'request failed');
  });

  var state = { project: null, currentFile: null, currentYaml: null, currentView: null, viewSlides: null };

  // Unsaved changes are visible, not just tracked: every Save button grows a dot the
  // moment the content diverges from disk. A property setter catches all assignment
  // sites (editor change events, inserters, image tools) without threading a helper.
  (function () {
    var dirty = false;
    Object.defineProperty(state, 'dirty', {
      get: function () { return dirty; },
      set: function (v) {
        dirty = !!v;
        // Chapter bar gets a 'dirty' class that shows the amber save dot.
        var cb = document.querySelector('.chapterbar');
        if (cb) cb.classList.toggle('dirty', dirty);
        // Legacy class on yaml/view save buttons.
        ['vSaveBtn', 'yamlSaveBtn'].forEach(function (id) {
          var b = $(id);
          if (b) b.classList.toggle('dirty', dirty);
        });
      },
    });
  })();

  var $ = function (id) { return document.getElementById(id); };
  var api = {
    // Non-OK responses must surface the server's message, not a JSON parse error.
    json: function (url) {
      return fetch(url).then(function (r) {
        if (!r.ok) return r.text().then(function (t) { throw new Error(t || r.statusText); });
        return r.json();
      });
    },
    send: function (method, url, body) {
      return fetch(url, {
        method: method,
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      }).then(function (r) {
        if (!r.ok) return r.text().then(function (t) { throw new Error(t || r.statusText); });
        return r.headers.get('Content-Type') && r.headers.get('Content-Type').indexOf('json') >= 0 ? r.json() : null;
      });
    },
  };

  /* ---------- project tree ---------- */

  function loadProject() {
    return api.json('/api/project').then(function (p) {
      state.project = p;
      setProjectHeader(p);
      renderTree(p);
    });
  }

  // Populate the header project block with the loaded project data.
  function setProjectHeader(p) {
    $('projName').textContent = p.project;
    $('projCode').textContent = p.code;
    $('projBlock').hidden = false;
    $('navWrap').hidden = false;
    // The "Editing template" badge marks that every live save lands in the template
    // master — the footgun this whole workflow is designed around.
    $('tplBadge').hidden = !p.isTemplate;
    // Word vector images need a one-shot conversion before they render — show the action
    // (with the live count) only when the open project still has any.
    var emfBtn = $('convertEmfBtn');
    emfBtn.hidden = !(p.emfCount > 0);
    if (p.emfCount > 0)
      $('convertEmfLabel').textContent = 'Convert ' + p.emfCount + ' EMF/WMF…';
  }

  function renderTree(p) {
    var tree = $('tree');
    tree.innerHTML = '';
    // Word-style flat chapter numbers (1, 2, 3 … across the project); section
    // groups stay unnumbered — same scheme as the generated handover.
    var num = p.numbering;
    var chNo = 0;
    p.sections.forEach(function (s) {
      var grp = el('div', 'grp');
      grp.appendChild(el('span', 'tree-label', s.title));
      grp.appendChild(moveTools('section', function (dir) {
        moveItem('/api/section-move', { folder: s.folder, direction: dir });
      }));
      tree.appendChild(grp);
      s.chapters.forEach(function (c) {
        chNo++;
        var a = el('a');
        a.appendChild(el('span', 'tree-label',
          (num ? chNo + ' ' : '') + (c.title || c.file)));
        a.appendChild(moveTools('chapter', function (dir) {
          moveItem('/api/chapter-move', { file: c.file, direction: dir });
        }));
        a.href = '#';
        a.dataset.file = c.file;
        if (c.file === state.currentFile) a.classList.add('current');
        a.onclick = function (e) { e.preventDefault(); openChapter(c.file); };
        tree.appendChild(a);
        // Subchapter outline (H2s and H3s), expanded for the open chapter only —
        // numbers match the handover's in-page heading numbers (N.1, N.1.1 …).
        if (c.file === state.currentFile) {
          var n2 = 0, n3 = 0;
          (c.headings || []).forEach(function (h, hi) {
            var deep = h.level === 3;
            if (deep) { n3++; } else { n2++; n3 = 0; }
            var sub = el('a', deep ? 'tree-sub tree-sub3' : 'tree-sub');
            var numLabel = deep ? chNo + '.' + n2 + '.' + n3 + ' ' : chNo + '.' + n2 + ' ';
            // strip inline markdown markers (**bold** etc.) for display
            sub.appendChild(el('span', 'tree-label',
              (num ? numLabel : '') + h.text.replace(/[*_`]/g, '')));
            sub.href = '#';
            sub.setAttribute('data-tip', 'Jump to this section in the editor and the preview.');
            sub.onclick = function (e) { e.preventDefault(); scrollEditorToHeading(hi); };
            tree.appendChild(sub);
          });
        }
      });
    });
    tree.appendChild(el('div', 'grp', 'Views'));
    p.views.forEach(function (v) {
      var a = el('a', null, v.title + ' · ' + v.mode);
      a.href = '#';
      a.setAttribute('data-tip', 'Audience view for "' + v.audience + '" (' +
        (v.mode === 'slides' ? 'presentation' : 'filtered page') +
        '). Built from chapter sections — edit it visually here.');
      if (v.file === state.currentView) a.classList.add('current');
      a.onclick = function (e) { e.preventDefault(); openView(v.file); };
      tree.appendChild(a);
    });
    var addView = el('a', 'tree-add', '+ New view');
    addView.href = '#';
    addView.onclick = function (e) { e.preventDefault(); newView(); };
    tree.appendChild(addView);
    var draftView = el('a', 'tree-add', 'Draft from manual…');
    draftView.href = '#';
    draftView.setAttribute('data-tip',
      'Generate a starting slide deck from one chapter — a slide per section, each ' +
      'showing the live chapter content. A first draft to trim and rearrange, not a ' +
      'finished deck.');
    draftView.onclick = function (e) { e.preventDefault(); draftFromManual(); };
    tree.appendChild(draftView);

    // Handover artifacts — things bundled into the handover beyond chapters/views.
    tree.appendChild(el('div', 'grp', 'Handover artifacts'));
    var attachItem = el('a', null, 'Attachments');
    attachItem.href = '#';
    attachItem.setAttribute('data-tip', 'Files in attachments/ (PDFs, datasheets) bundled on the handover Attachments tab.');
    if (state.currentArtifact === 'attach') attachItem.classList.add('current');
    attachItem.onclick = function (e) { e.preventDefault(); openAttach(); };
    tree.appendChild(attachItem);
    var docsItem = el('a', null, 'Documentation packages');
    docsItem.href = '#';
    docsItem.setAttribute('data-tip', 'External HTML documentation bundled as handover tabs — e.g. a generated API site shown under the API Reference tab.');
    if (state.currentArtifact === 'docs') docsItem.classList.add('current');
    docsItem.onclick = function (e) { e.preventDefault(); openDocs(); };
    tree.appendChild(docsItem);

    tree.appendChild(el('div', 'grp', 'Project'));
    var m = el('a', null, 'Project settings');
    m.href = '#';
    m.setAttribute('data-tip', 'Project name, code, customer, version, language, audiences and handover output — plus app preferences. The interactive editor for docwiz.yaml (raw YAML lives under Advanced).');
    if (state.currentArtifact === 'settings') m.classList.add('current');
    m.onclick = function (e) { e.preventDefault(); openSettings(); };
    tree.appendChild(m);
  }

  // ▲▼ reorder handles, revealed on hover. Spans, not <button>s — a button inside
  // the chapter <a> would be invalid HTML and steal the row click.
  function moveTools(what, onMove) {
    var span = el('span', 'tree-tools');
    [['▲', -1, 'up'], ['▼', 1, 'down']].forEach(function (d) {
      var b = el('span', 'tbtn', d[0]);
      b.setAttribute('data-tip', 'Move this ' + what + ' one position ' + d[2] +
        '. Only the order number changes — links and views keep working (stable IDs).');
      b.onclick = function (e) {
        e.preventDefault();
        e.stopPropagation();
        onMove(d[1]);
      };
      span.appendChild(b);
    });
    return span;
  }

  // By POSITION, not text — heading text differs between surfaces (markdown
  // markers, "{#anchor}" suffixes), but the H2/H3 order is identical everywhere.
  // The outline lists levels 2 and 3 in document order, so index into the same
  // h2|h3 set in the live surface.
  function scrollEditorToHeading(index) {
    var heads = document.querySelectorAll('#editor2 article h2, #editor2 article h3');
    var target = heads[index];
    if (!target) return;
    // block:'start' lands the heading at the very top of #editor2 — i.e. behind the
    // sticky, opaque .e2-top toolbar, which covers it. Offset the scroll by the live
    // toolbar height (read fresh: it can wrap/grow) so the heading clears it.
    var scroller = document.getElementById('editor2');
    var bar = scroller && scroller.querySelector('.e2-top');
    if (scroller) scroller.style.scrollPaddingTop = (bar ? bar.offsetHeight : 0) + 'px';
    target.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  function moveItem(url, body) {
    // fmId is now a chip div; read the slug span for the chapter id.
    var fmIdEl = $('fmId');
    var openId = state.currentFile ? (fmIdEl && fmIdEl.querySelector('.cb-id-slug') ? fmIdEl.querySelector('.cb-id-slug').textContent : null) : null;
    api.send('POST', url, body)
      .then(loadProject)
      .then(function () {
        // Renumbering may have renamed the open chapter's file — re-resolve by id.
        if (!openId) return;
        (state.project.sections || []).forEach(function (s) {
          s.chapters.forEach(function (c) {
            if (c.id === openId) state.currentFile = c.file;
          });
        });
        renderTree(state.project);
        mountLiveEditor(); // reload — the open chapter's number may have changed
      })
      .catch(function (e) { reportError(e.message); });
  }

  function el(tag, cls, text) {
    var node = document.createElement(tag);
    if (cls) node.className = cls;
    if (text !== undefined) node.textContent = text;
    return node;
  }

  function esc(text) {
    var d = document.createElement('span');
    d.textContent = text == null ? '' : String(text);
    return d.innerHTML;
  }

  /* ---------- welcome screen ---------- */

  // Inline-SVG icon factory used in JS-rendered welcome rows.
  // Returns an SVG string for the given icon name.
  function svgIcon(name) {
    var icons = {
      pin: '<svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 3v7l3 3-4 1-4-1 3-3V3z"/><line x1="12" y1="14" x2="12" y2="21"/></svg>',
      unpin: '<svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 3v7l3 3-4 1-4-1 3-3V3z" stroke-dasharray="3 2" opacity=".6"/><line x1="12" y1="14" x2="12" y2="21"/></svg>',
      trash: '<svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 6h18M8 6V4h8v2M19 6l-1 14H6L5 6"/></svg>',
      grid: '<svg viewBox="0 0 24 24" width="17" height="17" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="4" y="4" width="16" height="16" rx="2"/><path d="M4 9h16M9 9v11"/></svg>',
      pencil: '<svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 20h9"/><path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z"/></svg>',
      copy: '<svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="9" y="9" width="11" height="11" rx="2"/><path d="M5 15H4a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1h10a1 1 0 0 1 1 1v1"/></svg>',
    };
    return icons[name] || '';
  }

  // Stable hash of a string → integer, used to pick avatar colour variant.
  function hashStr(s) {
    var h = 0;
    for (var i = 0; i < s.length; i++) { h = (Math.imul(31, h) + s.charCodeAt(i)) | 0; }
    return Math.abs(h);
  }

  // Pick avatar color variant by hashing the project path (stable across refreshes).
  var AVATAR_VARIANTS = ['accent', 'green', 'amber', ''];
  function avatarVariant(path) { return AVATAR_VARIANTS[hashStr(path || '') % AVATAR_VARIANTS.length]; }

  // Produce 1–2 alnum uppercase initials from the project code.
  function initials(code) {
    var alnum = (code || '').replace(/[^a-zA-Z0-9]/g, '');
    return alnum.slice(0, 2).toUpperCase() || '??';
  }

  // Relative human-readable time from a "yyyy-MM-dd HH:mm" server string.
  function relativeTime(isoish) {
    if (!isoish) return '';
    // Server returns "yyyy-MM-dd HH:mm" (local time) — parse without timezone shift.
    var parts = isoish.match(/^(\d{4})-(\d{2})-(\d{2}) (\d{2}):(\d{2})$/);
    if (!parts) return isoish;
    var then = new Date(
      parseInt(parts[1], 10), parseInt(parts[2], 10) - 1, parseInt(parts[3], 10),
      parseInt(parts[4], 10), parseInt(parts[5], 10)
    );
    var now = new Date();
    var todayDate = now.getFullYear() + '-' + String(now.getMonth() + 1).padStart(2, '0') + '-' + String(now.getDate()).padStart(2, '0');
    var thenDate = parts[1] + '-' + parts[2] + '-' + parts[3];
    if (thenDate === todayDate) {
      // Today → HH:mm
      return parts[4] + ':' + parts[5];
    }
    // Earlier → "Jun 10" style
    var MONTHS = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];
    return MONTHS[then.getMonth()] + ' ' + then.getDate();
  }

  // Is the given "yyyy-MM-dd HH:mm" from today?
  function isToday(isoish) {
    if (!isoish) return false;
    var parts = isoish.match(/^(\d{4})-(\d{2})-(\d{2}) /);
    if (!parts) return false;
    var now = new Date();
    return parseInt(parts[1], 10) === now.getFullYear() &&
           parseInt(parts[2], 10) === (now.getMonth() + 1) &&
           parseInt(parts[3], 10) === now.getDate();
  }

  function init() {
    // A success message queued before a reload (flashAfterReload).
    try {
      var flash = JSON.parse(sessionStorage.getItem('dwFlash') || 'null');
      sessionStorage.removeItem('dwFlash');
      if (flash) showToast(flash.title, flash.message, 'success');
    } catch (e) { /* storage off or malformed — nothing to show */ }
    // Build stamp in the header: makes "which binary am I running" answerable
    // from any screenshot (stale-exe sessions are indistinguishable otherwise).
    fetch('/api/version').then(function (r) { return r.json(); }).then(function (v) {
      var el = $('buildStamp');
      if (el && v.built) {
        el.textContent = v.built;
        var meta = $('buildMeta');
        if (meta) meta.hidden = false;
      }
    }).catch(quiet);
    fetch('/api/project').then(function (r) {
      if (r.status === 409) {
        showWelcome();
        return;
      }
      r.json().then(function (p) {
        state.project = p;
        setProjectHeader(p);
        renderTree(p);
        // Deep links: #chapter=<file>[&v2=1] / #view=<file> open directly (also used
        // by the headless screenshot tests). &v2=1 switches the live surface on.
        var h = location.hash;
        if (h.indexOf('#chapter=') === 0) {
          var rest = h.slice(9);
          var amp = rest.indexOf('&'); // old &v2=1 links still open fine — v2 IS the editor
          openChapter(decodeURIComponent(amp >= 0 ? rest.slice(0, amp) : rest));
        } else if (h.indexOf('#view=') === 0) {
          openView(decodeURIComponent(h.slice(6)));
        } else if (h === '#docs') {
          openDocs();
        } else if (h === '#attach') {
          openAttach();
        } else if (h === '#settings') {
          openSettings();
        }
        // Resume into the import dialog after "New project from Word…" scaffolded + reloaded.
        if (sessionStorage.getItem('dwImportOnLoad')) {
          sessionStorage.removeItem('dwImportOnLoad');
          importDocx();
        }
      });
    });
  }

  function showWelcome() {
    document.body.classList.add('welcome'); // enables the sanctioned hero wash
    $('welcomeScreen').hidden = false;
    $('editorShell').hidden = true;
    ['checkBtn', 'buildBtn', 'buildMeta', 'handoverLink', 'packageLink'].forEach(function (id) { $(id).hidden = true; });
    $('projBlock').hidden = true;
    $('navWrap').hidden = true;
    $('projName').textContent = '';
    $('projCode').textContent = '';

    api.json('/api/welcome').then(function (w) {
      // ---- Template cards ----
      // Each template is a real on-disk project folder (TemplateLibrary.Discover).
      // Whole-card click = Use (copies the master into a new folder you pick); the small
      // Edit affordance mounts the master itself for maintenance. Broken templates stay
      // visible but disabled, with the load error in the tooltip.
      var tpl = $('wTemplates');
      tpl.innerHTML = '';
      w.templates.forEach(function (t) {
        var wrap = el('div', 'tcard-wrap');
        var btn = el('button', 'tcard' + (t.error ? ' is-broken' : ''));
        if (t.error) {
          btn.disabled = true;
          btn.setAttribute('data-tip', 'This template can’t be opened — fix it via Edit. Reason: ' + t.error);
        } else {
          btn.setAttribute('data-tip', 'Start a new project from “' + t.title + '” — copies its full chapter and view structure into a folder you choose. The template itself is left untouched.');
          btn.onclick = function () { newProject(t.id); };
        }
        btn.innerHTML =
          '<div class="tcard-head">' +
            '<span class="tcard-ico">' + svgIcon('grid') + '</span>' +
            '<span class="tcard-name">' + esc(t.title) + '</span>' +
            (t.error ? '<span class="tcard-badge tcard-badge-broken">NEEDS FIXING</span>'
                     : '<span class="tcard-badge">BUILT-IN</span>') +
          '</div>' +
          '<div class="tcard-desc">' + esc(t.error || t.description) + '</div>';
        wrap.appendChild(btn);

        // Edit affordance — sibling button (never nested inside the card button).
        var editBtn = el('button', 'tcard-edit');
        editBtn.setAttribute('data-tip', 'Edit this template’s master content. Changes save straight into the template — use this only to maintain the template itself, not to start a project.');
        editBtn.innerHTML = svgIcon('pencil') + '<span>Edit</span>';
        editBtn.onclick = function (e) { e.stopPropagation(); editTemplate(t.path); };
        wrap.appendChild(editBtn);

        tpl.appendChild(wrap);
      });
      $('wHint').textContent = w.templates.length === 0
        ? 'No templates yet — open a project and choose “Save as template” to reuse its structure here.'
        : 'Click a template to start a new project from it, or Edit to maintain the template itself.';

      // ---- Recent projects ----
      var rec = $('wRecent');
      rec.innerHTML = '';

      if (w.recent.length === 0) {
        rec.appendChild(el('p', 'welcome-hint', 'No recent projects yet — open or create one to see it here.'));
        return;
      }

      // Sort: pinned first, then by lastOpened desc.
      var pinned = w.recent.filter(function (r) { return r.pinned; })
        .sort(function (a, b) { return b.lastOpened.localeCompare(a.lastOpened); });
      var unpinned = w.recent.filter(function (r) { return !r.pinned; });

      // Among unpinned: group into Today and Earlier, each sorted by lastOpened desc.
      var todayRows = unpinned.filter(function (r) { return isToday(r.lastOpened); })
        .sort(function (a, b) { return b.lastOpened.localeCompare(a.lastOpened); });
      var earlierRows = unpinned.filter(function (r) { return !isToday(r.lastOpened); })
        .sort(function (a, b) { return b.lastOpened.localeCompare(a.lastOpened); });

      function buildRow(r) {
        var variant = avatarVariant(r.path);
        var inits = initials(r.code);
        var timeStr = relativeTime(r.lastOpened);

        var row = el('div', 'prow' + (r.exists ? '' : ' missing'));
        if (r.exists) {
          row.setAttribute('tabindex', '0');
          row.setAttribute('data-tip', 'Open "' + esc(r.project || r.path) + '" — resumes your project in the editor.');
          row.onclick = function () { openProject(r.path); };
          row.onkeydown = function (e) { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); openProject(r.path); } };
        } else {
          row.setAttribute('title', 'Project folder not found');
        }

        // Avatar
        var ava = el('div', 'pava' + (variant ? ' ' + variant : ''));
        ava.textContent = inits;
        row.appendChild(ava);

        // Info block
        var info = el('div', 'pinfo');
        var nameEl = el('div', 'pname', r.project || r.path);
        info.appendChild(nameEl);
        var meta = el('div', 'pmeta');
        var codeEl = el('span', 'pcode');
        codeEl.textContent = r.code || '';
        meta.appendChild(codeEl);
        var pathEl = el('span', 'ppath');
        pathEl.textContent = r.path || '';
        meta.appendChild(pathEl);
        info.appendChild(meta);
        row.appendChild(info);

        // Pin icon (always visible for pinned rows)
        if (r.pinned) {
          var pinViz = el('span', 'ppin-icon');
          pinViz.innerHTML = svgIcon('pin');
          row.appendChild(pinViz);
        }

        // Time
        var timeEl = el('span', 'ptime');
        timeEl.textContent = timeStr;
        row.appendChild(timeEl);

        // Hover-reveal actions (pin toggle + delete)
        var actions = el('div', 'prow-actions');

        var pinBtn = el('button', 'prow-btn' + (r.pinned ? ' pin-active' : ''));
        pinBtn.setAttribute('data-tip', r.pinned
          ? 'Unpin this project — it will no longer stay at the top of the list.'
          : 'Pin this project to keep it at the top of the recent list.');
        pinBtn.innerHTML = r.pinned ? svgIcon('pin') : svgIcon('unpin');
        pinBtn.onclick = function (e) {
          e.stopPropagation();
          fetch('/api/recent/pin', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ path: r.path }),
          }).then(function () { showWelcome(); });
        };
        actions.appendChild(pinBtn);

        var delBtn = el('button', 'prow-btn del');
        delBtn.setAttribute('data-tip', 'Remove "' + esc(r.project || r.path) + '" from this list. The project folder itself is not touched.');
        delBtn.innerHTML = svgIcon('trash');
        delBtn.onclick = function (e) {
          e.stopPropagation();
          fetch('/api/recent?path=' + encodeURIComponent(r.path), { method: 'DELETE' })
            .then(function () { showWelcome(); });
        };
        actions.appendChild(delBtn);

        row.appendChild(actions);
        return row;
      }

      function appendGroup(label, rows) {
        if (rows.length === 0) return;
        var grpEl = el('div', 'daygroup', label);
        rec.appendChild(grpEl);
        rows.forEach(function (r) { rec.appendChild(buildRow(r)); });
      }

      // Pinned rows always first, no day-group label
      if (pinned.length > 0) {
        var pinnedGrp = el('div', 'daygroup', 'Pinned');
        rec.appendChild(pinnedGrp);
        pinned.forEach(function (r) { rec.appendChild(buildRow(r)); });
      }
      appendGroup('Today', todayRows);
      appendGroup('Earlier', earlierRows);

      // ---- Search filter ----
      var searchInput = $('wSearch');
      if (searchInput) {
        searchInput.oninput = function () {
          var q = searchInput.value.trim().toLowerCase();
          var allRows = rec.querySelectorAll('.prow');
          var allGroups = rec.querySelectorAll('.daygroup');
          allRows.forEach(function (row) {
            if (!q) { row.hidden = false; return; }
            var name = (row.querySelector('.pname') || {}).textContent || '';
            var code = (row.querySelector('.pcode') || {}).textContent || '';
            var path = (row.querySelector('.ppath') || {}).textContent || '';
            row.hidden = !(
              name.toLowerCase().indexOf(q) >= 0 ||
              code.toLowerCase().indexOf(q) >= 0 ||
              path.toLowerCase().indexOf(q) >= 0
            );
          });
          // Hide any day-group whose rows are all filtered out.
          allGroups.forEach(function (grp) {
            var next = grp.nextElementSibling;
            var anyVisible = false;
            while (next && !next.classList.contains('daygroup')) {
              if (!next.hidden) { anyVisible = true; break; }
              next = next.nextElementSibling;
            }
            grp.hidden = !anyVisible;
          });
        };
        // Clear search on welcome re-render.
        searchInput.value = '';
      }
    });

    api.json('/api/settings').then(function (s) {
      var box = $('wSettings');
      box.hidden = false;
      // The DocWiz-window option covers both the native shell and the legacy Edge app-window
      // (anything that is not "browser"); selecting it saves the native default.
      var mode = s.windowMode === 'browser' ? 'browser' : 'native';
      function paint() {
        $('wModeApp').setAttribute('aria-pressed', String(mode !== 'browser'));
        $('wModeBrowser').setAttribute('aria-pressed', String(mode === 'browser'));
      }
      function set(m) {
        if (m === mode) return;
        mode = m;
        paint();
        $('wModeNote').textContent = 'Applies next time you launch DocWiz.';
        api.send('POST', '/api/settings', { windowMode: m }).catch(quiet);
      }
      $('wModeApp').onclick = function () { set('native'); };
      $('wModeBrowser').onclick = function () { set('browser'); };
      paint();
    }).catch(quiet);
  }

  // path null → the server shows the native Windows folder picker (separate process).
  function openProject(path) {
    setBusy('Waiting for the folder picker…');
    api.send('POST', '/api/open', { path: path || null })
      .then(handleMountResult).catch(showWelcomeError);
  }

  function newProject(template) {
    setBusy('Waiting for the folder picker…');
    api.send('POST', '/api/new', { template: template, path: null })
      .then(handleMountResult).catch(showWelcomeError);
  }

  // Mount a template's master folder directly (maintenance). path comes from Discover,
  // so no folder picker — go straight to the project.
  function editTemplate(path) {
    setBusy('Opening the template…');
    api.send('POST', '/api/open', { path: path })
      .then(handleMountResult).catch(showWelcomeError);
  }

  function setBusy(msg) {
    var box = $('wError');
    box.classList.remove('welcome-error');
    box.textContent = msg;
    box.hidden = false;
  }

  function handleMountResult(res) {
    if (res.ok) { location.reload(); return; }
    if (res.cancelled) { $('wError').hidden = true; return; }
    showWelcomeError(new Error(res.error || 'Could not open the project.'));
  }

  function showWelcomeError(e) {
    var box = $('wError');
    box.classList.add('welcome-error');
    box.textContent = e.message;
    box.hidden = false;
  }

  /* ---------- discipline dropdown (custom — native <select> is OS-rendered) ----------
     Standard discipline values; unknown values (imported chapters) are added at open time.
     API: discDropdown.setVal(v) syncs trigger + hidden input.
          discDropdown.ensureVal(v) adds a dynamic option if v is not in the list.
          The hidden input #fmDiscipline.value is always the canonical value. */

  var DISC_OPTS = [
    '',
    'overview', 'setup', 'operations',
    'troubleshooting', 'reference', 'integration',
  ];

  var discDropdown = (function buildDisciplineDropdown() {
    var mountEl = $('fmDisciplineDropdown');
    var hiddenInput = $('fmDiscipline');
    if (!mountEl || !hiddenInput) return null;

    // Dynamic option list (starts as the standard set; extras pushed at runtime)
    var optList = DISC_OPTS.slice();
    var onChangeCb = null; // set by openChapter

    // DOM
    var trigger = document.createElement('button');
    trigger.type = 'button';
    trigger.className = 'dw-sel-trigger cb-disc-trigger';
    trigger.setAttribute('data-tip', 'Which kind of chapter this is (setup, operations, troubleshooting, …).');
    trigger.setAttribute('aria-haspopup', 'listbox');
    trigger.setAttribute('aria-expanded', 'false');

    var trigLabel = document.createElement('span');
    trigger.appendChild(trigLabel);

    // Inline chevron SVG — use setAttribute for class on SVGElement (className is read-only)
    var chevSvg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    chevSvg.setAttribute('viewBox', '0 0 10 6');
    chevSvg.setAttribute('width', '10');
    chevSvg.setAttribute('height', '6');
    chevSvg.setAttribute('aria-hidden', 'true');
    chevSvg.setAttribute('class', 'dw-sel-chev');
    var chevPath = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    chevPath.setAttribute('d', 'M1 1l4 4 4-4');
    chevPath.setAttribute('fill', 'none');
    chevPath.setAttribute('stroke', 'currentColor');
    chevPath.setAttribute('stroke-width', '1.7');
    chevPath.setAttribute('stroke-linecap', 'round');
    chevPath.setAttribute('stroke-linejoin', 'round');
    chevSvg.appendChild(chevPath);
    trigger.appendChild(chevSvg);

    mountEl.appendChild(trigger);

    var popup = document.createElement('div');
    popup.className = 'dw-sel-popup';
    popup.setAttribute('role', 'listbox');
    popup.setAttribute('aria-label', 'Discipline');
    popup.hidden = true;

    var optEls = []; // rebuilt on open
    var hiIdx = 0;

    function buildOptEls() {
      popup.innerHTML = '';
      optEls = optList.map(function (v, idx) {
        var optEl = document.createElement('div');
        optEl.className = 'dw-sel-opt';
        optEl.setAttribute('role', 'option');
        optEl.setAttribute('data-v', v);
        optEl.setAttribute('aria-selected', hiddenInput.value === v ? 'true' : 'false');
        optEl.textContent = v === '' ? '(none)' : v;
        optEl.onmousedown = function (e) { e.preventDefault(); };
        optEl.onclick = function () { select(v); closePopup(); };
        optEl.onmouseenter = function () { setHi(idx); };
        popup.appendChild(optEl);
        return optEl;
      });
    }

    function setHi(idx) {
      hiIdx = idx;
      optEls.forEach(function (el, i) { el.classList.toggle('dw-sel-hi', i === idx); });
    }

    function setVal(v) {
      hiddenInput.value = v;
      trigLabel.textContent = v === '' ? '(none)' : v;
      optEls.forEach(function (el) {
        el.setAttribute('aria-selected', el.getAttribute('data-v') === v ? 'true' : 'false');
      });
    }

    function ensureVal(v) {
      if (!v) return;
      var found = optList.some(function (o) { return o === v; });
      if (!found) optList.push(v);
    }

    function select(v) {
      setVal(v);
      if (onChangeCb) onChangeCb();
    }

    function openPopup() {
      buildOptEls();
      document.body.appendChild(popup);
      popup.hidden = false;
      trigger.setAttribute('aria-expanded', 'true');
      var rect = trigger.getBoundingClientRect();
      popup.style.left = Math.round(rect.left) + 'px';
      popup.style.top = Math.round(rect.bottom + 2) + 'px';
      popup.style.minWidth = Math.round(rect.width) + 'px';
      var pw = popup.offsetWidth;
      var vw = window.innerWidth;
      if (rect.left + pw > vw - 8) popup.style.left = Math.max(8, vw - pw - 8) + 'px';
      var ci = optList.indexOf(hiddenInput.value);
      setHi(ci >= 0 ? ci : 0);
      document.addEventListener('mousedown', outsideClick, true);
      document.addEventListener('keydown', popupKey, true);
    }

    function closePopup() {
      popup.hidden = true;
      trigger.setAttribute('aria-expanded', 'false');
      document.removeEventListener('mousedown', outsideClick, true);
      document.removeEventListener('keydown', popupKey, true);
      if (document.body.contains(popup)) document.body.removeChild(popup);
      trigger.focus();
    }

    function outsideClick(e) {
      if (!popup.contains(e.target) && e.target !== trigger) closePopup();
    }

    function popupKey(e) {
      if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); closePopup(); return; }
      if (e.key === 'ArrowDown') { e.preventDefault(); setHi((hiIdx + 1) % optEls.length); return; }
      if (e.key === 'ArrowUp') { e.preventDefault(); setHi((hiIdx - 1 + optEls.length) % optEls.length); return; }
      if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        if (optList[hiIdx] !== undefined) { select(optList[hiIdx]); closePopup(); }
        return;
      }
    }

    trigger.onclick = function (e) {
      e.preventDefault();
      if (!popup.hidden) { closePopup(); } else { openPopup(); }
    };

    trigger.addEventListener('keydown', function (e) {
      if ((e.key === 'ArrowDown' || e.key === 'Enter' || e.key === ' ') && popup.hidden) {
        e.preventDefault();
        openPopup();
      }
    });

    // Initialise display
    trigLabel.textContent = '(none)';

    return {
      setVal: setVal,
      ensureVal: ensureVal,
      setOnChange: function (cb) { onChangeCb = cb; },
    };
  })();

  /* ---------- chapter editing ---------- */

  function openChapter(file) {
    if (!confirmDiscard(openChapter, arguments)) return;
    api.json('/api/chapter?file=' + encodeURIComponent(file)).then(function (ch) {
      state.currentFile = file;
      state.currentYaml = null;
      state.currentView = null;
      show('chapterEditor');
      // fmId is now a read-only chip (div > .cb-id-slug span), not an input.
      var slugEl = $('fmId').querySelector('.cb-id-slug');
      if (slugEl) slugEl.textContent = ch.id;
      $('fmTitle').value = ch.title;
      // Disciplines outside the standard list (imported projects use section names)
      // get added dynamically so nothing is lost on save.
      if (discDropdown) {
        discDropdown.ensureVal(ch.discipline);
        discDropdown.setVal(ch.discipline || '');
        discDropdown.setOnChange(function () { state.dirty = true; });
      } else {
        // Fallback: if the custom dropdown failed to init (missing mount element),
        // write directly to the hidden input so chapterFormValues still works.
        $('fmDiscipline').value = ch.discipline || '';
      }
      // Wire dirty tracking for the metadata fields (audience toggles wire inside renderAudienceChecks).
      $('fmTitle').oninput = function () { state.dirty = true; };
      renderAudienceChecks(ch.audiences);
      state.dirty = false;
      setStatus('');
      renderTree(state.project);
      mountLiveEditor();
    });
  }

  /* ---------- the live editing surface (ADR-007; THE editor since the M5 flip).
     The module under /app/editor2/ exposes window.DocWizEditor2 once loaded
     (module scripts are deferred). */

  function mountLiveEditor() {
    if (!state.currentFile) return;
    if (window.DocWizEditor2) {
      window.DocWizEditor2
        .open(state.currentFile, {
          onDirty: function () { state.dirty = true; },
          audiences: state.project.audiences,
        })
        .catch(function (e) { reportError(e.message); });
    } else {
      setTimeout(mountLiveEditor, EDITOR2_RETRY_MS); // module still loading
    }
  }

  // Audience dot color map: maps audience id prefix to a token class.
  var AUD_DOT_MAP = { user: 'cb-dot-op', support: 'cb-dot-ma', admin: 'cb-dot-en' };

  function renderAudienceChecks(selected) {
    var container = $('fmAudiences');
    container.innerHTML = '';
    state.project.audiences.forEach(function (a) {
      var btn = el('button');
      btn.type = 'button';
      btn.className = 'cb-aud-btn';
      btn.dataset.aud = a.id;
      btn.dataset.on = selected.indexOf(a.id) >= 0 ? '1' : '0';

      // Color dot
      var dot = el('span');
      dot.className = 'cb-aud-dot ' + (AUD_DOT_MAP[a.id] || 'cb-dot-en');
      dot.setAttribute('aria-hidden', 'true');
      btn.appendChild(dot);

      // Label text
      btn.appendChild(document.createTextNode(a.title));

      btn.onclick = function () {
        var on = btn.dataset.on === '1';
        btn.dataset.on = on ? '0' : '1';
        state.dirty = true;
      };
      container.appendChild(btn);
    });
  }

  function chapterFormValues() {
    return {
      title: $('fmTitle').value,
      discipline: $('fmDiscipline').value,
      audiences: Array.prototype.slice
        .call($('fmAudiences').querySelectorAll('[data-on="1"]'))
        .map(function (btn) { return btn.dataset.aud; }),
    };
  }

  function saveChapter() {
    if (!state.currentFile) return Promise.resolve();
    // Byte-surgical save through the live surface (only edited blocks re-emit).
    return window.DocWizEditor2.save(chapterFormValues())
      .then(function () {
        state.dirty = false;
        setStatus('Saved ✓', 'ok');
        return loadProject();
      })
      .catch(function (e) {
        setStatus('Save failed: ' + e.message, 'err');
        throw e; // callers chaining Build/Export must not proceed on stale content
      });
  }

  function setStatus(text, cls) {
    var s = $('status');
    s.textContent = text;
    // Preserve the structural class ('cb-status') and only toggle ok/err modifiers.
    s.className = 'cb-status' + (cls ? ' ' + cls : '');
  }

  // Messages from the live surface (link, image, embed tools) go to the status line of the
  // editor that is on screen — the chapter bar's line is hidden while a view is edited.
  function editorStatus(text, cls) {
    if (!$('viewEditor').hidden) {
      $('vStatus').textContent = text;
      $('vStatus').className = cls || '';
    } else {
      setStatus(text, cls);
    }
  }

  // True when the slide-zone editors hold edits not yet saved (Task 7c). state.dirty is
  // the primary signal (zone edits flip it via onZoneDirty), but the slideedit module
  // owns its own per-zone dirty flags with a separate lifecycle; consulting both here
  // means the discard guard still fires if a future change to the zone wiring ever drops
  // the state.dirty flip. markClean() (called on save) clears it, so a saved deck is clean.
  function slideZonesDirty() {
    return !!(window.DocWizSlideEdit && window.DocWizSlideEdit.isDirty && window.DocWizSlideEdit.isDirty());
  }

  function settingsDirty() {
    var pane = $('setPane');
    return !!pane && pane.classList.contains('dirty');
  }

  // Unsaved-work guard for every navigation. Returns true when nothing is unsaved (chapter,
  // view, slide zones, Project settings). Otherwise it asks in a DocWiz dialog and returns
  // false; "Discard changes" then re-runs retry(...args) once with the guard satisfied.
  var discardConfirmed = false;
  function confirmDiscard(retry, args) {
    if (discardConfirmed) { discardConfirmed = false; return true; }
    if (!state.dirty && !slideZonesDirty() && !settingsDirty()) return true;
    confirmDialog('Discard unsaved changes?',
      settingsDirty() && !state.dirty
        ? 'Your Project settings changes haven’t been saved yet.'
        : 'Your latest changes haven’t been saved yet.',
      'Discard changes', function () {
        setDirty(false);
        state.dirty = false; // the next screen replaces the edited content
        if (window.DocWizSlideEdit && window.DocWizSlideEdit.markClean) window.DocWizSlideEdit.markClean();
        discardConfirmed = true;
        try { retry.apply(null, args || []); } finally { discardConfirmed = false; }
      }, true);
    return false;
  }

  /* ---------- insert helpers ---------- */

  // The paste/drop image flow of the live surface: upload into assets/<folder> under a
  // chosen file name. onCancel (optional) fires when the dialog is dismissed without
  // saving — the data:-image catcher uses it to remove the just-pasted image.
  function pasteImageDialog(blob, onDone, onCancel) {
    var defaultName = (blob && blob.name) || 'pasted.png';
    api.json('/api/assets').then(function (res) {
      var committed = false;
      openModal('Paste image into the project',
        '<p class="mhelp">The image is copied into the project’s assets folder. ' +
        'Pick a subfolder and a file name, or leave them to use the defaults.</p>' +
        '<div class="row"><label>Into folder</label><input id="mPasteFolder" list="mPasteFolders" ' +
        'placeholder="(root) — e.g. screenshots">' + folderDatalist('mPasteFolders', res.folders) + '</div>' +
        '<div class="row"><label>File name</label><input id="mPasteName" placeholder="' + esc(defaultName) + '"></div>' +
        '<div class="row"><label>Caption</label><input id="mPasteAlt" ' +
        'placeholder="describes the image — shown as its caption"></div>',
        function (root) {
          var folder = root.querySelector('#mPasteFolder').value.trim();
          state.lastPasteFolder = folder;
          var name = ensureImageExt(root.querySelector('#mPasteName').value.trim() || defaultName, defaultName);
          committed = true;
          fetch('/api/asset?name=' + encodeURIComponent(name) +
                (folder ? '&folder=' + encodeURIComponent(folder) : ''), { method: 'POST', body: blob })
            .then(function (r) { return r.json(); })
            .then(function (up) {
              // encodeURI: spaces in file names would break the Markdown link syntax.
              onDone({ src: encodeURI(up.url), alt: root.querySelector('#mPasteAlt').value || baseName(up.fileName) });
              setStatus('Image stored as assets/' + up.fileName + '.', 'ok');
            });
          return undefined; // close the dialog; upload completes async
        },
        function (root) {
          root.querySelector('#mPasteFolder').value = state.lastPasteFolder || '';
          // Dismissed without saving (Cancel / Esc / backdrop) → let the caller undo the paste.
          root.addEventListener('close', function handler() {
            root.removeEventListener('close', handler);
            if (!committed && onCancel) onCancel();
          });
        });
    });
  }

  // Keep a user-typed file name pointing at an image: if they drop the extension, reuse
  // the source's (e.g. "diagram" + "pasted.png" → "diagram.png").
  function ensureImageExt(name, fallback) {
    if (/\.[a-z0-9]+$/i.test(name)) return name;
    var m = fallback.match(/\.[a-z0-9]+$/i);
    return name + (m ? m[0] : '.png');
  }

  // Pasting a copied dw: reference on the live surface: confirm the display text,
  // prefilled with the target's natural name (chapter title / section name).
  function dwPasteDialog(href, done) {
    var ref = href.slice(3).split('#');
    var title = chapterTitleFor(ref[0]) || ref[0];
    var natural = ref[1] ? prettify(ref[1]) : title;
    openModal('Paste chapter link',
      '<p class="mhelp">This is a link to <b>' + esc(title) + (ref[1] ? ' › ' + esc(prettify(ref[1])) : '') + '</b>. ' +
      'Choose the text readers will see.</p>' +
      '<div class="row"><label>Link text</label><input id="mDwPasteText"></div>',
      function (root) {
        done({ href: href, text: root.querySelector('#mDwPasteText').value || natural });
        state.dirty = true;
      },
      function (root) { root.querySelector('#mDwPasteText').value = natural; });
  }

  // Generic web link (live-surface toolbar; dw:/attachment links have their own dialogs).
  function webLinkDialog() {
    openModal('Insert web link',
      '<p class="mhelp">A link to an external web address. For chapters use “chapter link”; ' +
      'for project documents use “attachment” — those keep working offline.</p>' +
      '<div class="row"><label>Address</label><input id="mWebUrl" placeholder="https://…"></div>' +
      '<div class="row"><label>Link text</label><input id="mWebText" placeholder="shown to the reader"></div>',
      function (root) {
        var url = root.querySelector('#mWebUrl').value.trim();
        var text = root.querySelector('#mWebText').value.trim() || url;
        if (!url) return modalWarn(root, 'Enter the address.');
        if (!/^[a-z]+:/i.test(url)) url = 'https://' + url;
        window.DocWizEditor2.insert({ type: 'attachment', href: url, text: text });
        state.dirty = true;
      });
  }

  // Edit an existing link of any type (live surface "Change…"): address + text,
  // prefilled. Bad chapter targets are caught by Check, like everywhere else.
  function editLinkDialog(href, text, done) {
    openModal('Change link',
      '<p class="mhelp">Change where this link points or the text readers see. ' +
      'Chapter links look like <code>dw:chapter-id</code>; project documents like ' +
      '<code>../../attachments/file.pdf</code>.</p>' +
      '<div class="row"><label>Address</label><input id="mEditUrl"></div>' +
      '<div class="row"><label>Link text</label><input id="mEditText"></div>',
      function (root) {
        var url = root.querySelector('#mEditUrl').value.trim();
        var t = root.querySelector('#mEditText').value.trim() || url;
        if (!url) return modalWarn(root, 'Enter the address.');
        done({ href: url, text: t });
        state.dirty = true;
      },
      function (root) {
        root.querySelector('#mEditUrl').value = href || '';
        root.querySelector('#mEditText').value = text || '';
      });
  }

  // Asset pick-only dialog (live surface: figure / table-cell "Replace…").
  function pickAssetDialog(onPick) {
    api.json('/api/assets').then(function (res) {
      var files = res.files || [];
      var fileList = null;
      openModal('Pick image',
        '<p class="mhelp">Pictures in the project’s assets folder — click one to select it. ' +
        'Uploading copies the file into the project; use the Folder box to organise it.</p>' +
        '<div id="mPickList" class="file-list"></div>' +
        '<div class="row"><label>Upload new</label><input type="file" id="mPickUpload" accept="image/*"></div>' +
        '<div class="row"><label>Into folder</label><input id="mPickFolder" list="mPickFolders" ' +
        'placeholder="(root) — e.g. renders">' + folderDatalist('mPickFolders', res.folders) + '</div>',
        function (root) {
          var name = fileList && fileList.get();
          if (!name) return modalWarn(root, 'Pick an image from the list first — or upload one.');
          onPick({ src: assetHref(name), alt: baseName(name).replace(/\.[^.]+$/, '') });
        },
        function (root) {
          var listEl = root.querySelector('#mPickList');
          if (files.length === 0)
            listEl.innerHTML = '<p class="mhelp warn">No images in this project yet — upload one below.</p>';
          fileList = createFileList(listEl, files, '/assets/', function () {});
          root.querySelector('#mPickUpload').onchange = function (e) {
            var file = e.target.files[0];
            if (!file) return;
            var folder = root.querySelector('#mPickFolder').value.trim();
            fetch('/api/asset?name=' + encodeURIComponent(file.name) +
                  (folder ? '&folder=' + encodeURIComponent(folder) : ''), { method: 'POST', body: file })
              .then(function (r) { return r.json(); })
              .then(function (up) {
                if (files.length === 0) listEl.innerHTML = '';
                files.push(up.fileName);
                fileList.add(up.fileName); // public add() selects it, so OK works immediately
              });
          };
        });
    });
  }

  /* Selectable file list used by the image and attachment dialogs: the project's
     files are the visible, primary choice — not a dropdown, not a browser picker. */
  function createFileList(listEl, files, thumbPrefix, onSelect) {
    var selected = null;
    function add(name, doSelect) {
      var row = el('button', 'file-row');
      row.type = 'button';
      if (thumbPrefix) {
        var img = el('img', 'file-thumb');
        // Encode each path segment so subfolder URLs (renders/cell.png) resolve.
        img.src = thumbPrefix + name.split('/').map(encodeURIComponent).join('/');
        img.alt = '';
        row.appendChild(img);
      }
      // Show subfolder prefix muted, filename emphasised.
      var slash = name.lastIndexOf('/');
      if (slash >= 0) {
        row.appendChild(el('span', 'file-dir', name.slice(0, slash + 1)));
        row.appendChild(el('span', null, name.slice(slash + 1)));
      } else {
        row.appendChild(el('span', null, name));
      }
      row.onclick = function () { select(name, row); };
      listEl.appendChild(row);
      if (doSelect) select(name, row);
      return row;
    }
    function select(name, row) {
      selected = name;
      Array.prototype.forEach.call(listEl.querySelectorAll('.file-row'), function (r) {
        r.classList.remove('selected');
      });
      if (row) row.classList.add('selected');
      onSelect(name);
    }
    files.forEach(function (f) { add(f, false); });
    if (files.length > 0) listEl.querySelector('.file-row').click();
    return {
      get: function () { return selected; },
      add: function (name) { add(name, true); },
    };
  }

  function slugify(text) {
    return text.trim().toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
  }

  function baseName(path) {
    var i = path.lastIndexOf('/');
    return i >= 0 ? path.slice(i + 1) : path;
  }

  // Chapter files live at content/<section>/<file>.md, so project assets are always
  // two levels up. encodeURI: spaces in file names would break the Markdown link.
  function assetHref(name) {
    return encodeURI('../../assets/' + name);
  }

  function folderDatalist(id, folders) {
    var opts = (folders || []).map(function (f) { return '<option value="' + esc(f) + '">'; }).join('');
    return '<datalist id="' + id + '">' + opts + '</datalist>';
  }

  function prettify(slug) {
    var t = slug.replace(/-/g, ' ');
    return t.charAt(0).toUpperCase() + t.slice(1);
  }

  // The chapter <optgroup> list (sections → chapters) shared by every dialog that
  // picks a chapter (link / linked-element / draft-deck). Returns the optgroup HTML
  // plus the flat chapter list collected in option order, so callers can look a
  // chapter up by id for auto-text / anchor rebuilds.
  function chapterOptgroups() {
    var chapters = [];
    var html = state.project.sections.map(function (s) {
      var opts = s.chapters.map(function (c) {
        chapters.push(c);
        return '<option value="' + esc(c.id) + '">' + esc(c.title || c.id) + '</option>';
      }).join('');
      return '<optgroup label="' + esc(s.title) + '">' + opts + '</optgroup>';
    }).join('');
    return { html: html, chapters: chapters };
  }

  // Rebuilds an anchor <select>'s options for the given chapter (the "(whole chapter)"
  // entry plus one option per section anchor) — the section dropdown shared by the
  // link and linked-element dialogs.
  function rebuildAnchorOptions(chapter, anchorSelEl) {
    var groups = (chapter && chapter.groups) || [];
    var groupIds = {};
    groups.forEach(function (g) { groupIds[g.id] = true; });
    var opts = '<option value="">(whole chapter)</option>';
    if (chapter) {
      opts += chapter.anchors.filter(function (a) { return !groupIds[a]; }).map(function (a) {
        return '<option value="' + esc(a) + '">' + esc(prettify(a)) + '</option>';
      }).join('');
      if (groups.length) {
        opts += '<optgroup label="Groups">' + groups.map(function (g) {
          return '<option value="' + esc(g.id) + '">' + esc(g.name || prettify(g.id)) + '</option>';
        }).join('') + '</optgroup>';
      }
    }
    anchorSelEl.innerHTML = opts;
  }

  // The ::: element library (rendered by the one pipeline — see CONTENT_MODEL.md).
  // preview: compact HTML rendered with .dw-ep-* classes (app.css) using --dw-* tokens.
  // These mirror the real .dw-callout/.dw-steps/etc. visuals without needing @scope.
  var elementLibrary = [
    { name: 'Warning', kind: 'warning',
      desc: 'Amber safety callout. The label can be customized: "::: warning Hot surface".',
      md: '\n::: warning\nDescribe the hazard and how to avoid it.\n:::\n',
      preview: '<div class="dw-ep-callout dw-ep-warning">' +
               '<span class="dw-ep-tag">Warning</span>' +
               'Describe the hazard.' +
               '</div>' },
    { name: 'Danger', kind: 'danger',
      desc: 'Red callout for serious hazards (injury, machine damage).',
      md: '\n::: danger\nDescribe the danger. State what must never be done.\n:::\n',
      preview: '<div class="dw-ep-callout dw-ep-danger">' +
               '<span class="dw-ep-tag">Danger</span>' +
               'Never do this.' +
               '</div>' },
    { name: 'Note', kind: 'note',
      desc: 'Neutral callout for things worth knowing.',
      md: '\n::: note\nGood to know.\n:::\n',
      preview: '<div class="dw-ep-callout">' +
               '<span class="dw-ep-tag">Note</span>' +
               'Good to know.' +
               '</div>' },
    { name: 'Tip', kind: 'tip',
      desc: 'Green callout for shortcuts and best practice.',
      md: '\n::: tip\nA faster or safer way to do it.\n:::\n',
      preview: '<div class="dw-ep-callout dw-ep-tip">' +
               '<span class="dw-ep-tag">Tip</span>' +
               'A faster way to do it.' +
               '</div>' },
    { name: 'Procedure steps', kind: 'steps',
      desc: 'Numbered steps with big markers — for step-by-step procedures.',
      md: '\n::: steps\n1. First step.\n2. Second step.\n3. Third step.\n:::\n',
      preview: '<div class="dw-ep-steps">' +
               '<ol><li>First step.</li><li>Second step.</li></ol>' +
               '</div>' },
    { name: 'Navigation cards', kind: 'cards',
      desc: 'A grid of link cards — build overview/landing pages. Use chapter links (dw:).',
      md: '\n::: cards\n- [Chapter title](dw:chapter-id) — one-line description\n- [Another chapter](dw:chapter-id) — one-line description\n:::\n',
      preview: '<div class="dw-ep-cards">' +
               '<div class="dw-ep-card">' +
               '<div class="dw-ep-card-title">Chapter title</div>' +
               '<div class="dw-ep-card-desc">One-line description</div>' +
               '</div>' +
               '<div class="dw-ep-card">' +
               '<div class="dw-ep-card-title">Another chapter</div>' +
               '<div class="dw-ep-card-desc">One-line description</div>' +
               '</div>' +
               '</div>' },
    { name: 'Button row', kind: 'buttons',
      desc: 'Links rendered as buttons — prominent calls to action.',
      md: '\n::: buttons\n[Open the manual](dw:chapter-id)\n:::\n',
      preview: '<div class="dw-ep-buttons">' +
               '<span class="dw-ep-btn">Open the manual</span>' +
               '</div>' },
    { name: 'Collapsible section', kind: 'details',
      desc: 'Folded content the reader expands — long troubleshooting, reference data. Title after "details".',
      md: '\n::: details More information\nHidden until the reader clicks.\n:::\n',
      preview: '<div class="dw-ep-details">' +
               '<div class="dw-ep-summary">More information</div>' +
               '</div>' },
    { name: 'Centered text', kind: 'center',
      desc: 'Center-aligns everything inside (text, tables, images).',
      md: '\n::: center\nCentered content.\n:::\n',
      preview: '<div class="dw-ep-center">Centered content</div>' },
    { name: 'Right-aligned text', kind: 'right',
      desc: 'Right-aligns everything inside — signatures, document numbers.',
      md: '\n::: right\nRight-aligned content.\n:::\n',
      preview: '<div class="dw-ep-right">Right-aligned content</div>' },
  ];

  // Size options come from the live surface's preset table (editor2/constants.js,
  // via DocWizEditor2.presets) — the px values exist in exactly one place. The
  // dialog lists them descending; Medium is the default.
  function imgSizeOptions() {
    var names = { XS: 'Icon', S: 'Small', M: 'Medium', L: 'Large' };
    var rows = window.DocWizEditor2.presets.figure.slice().reverse();
    return '<option value="">Original size</option>' + rows.map(function (p) {
      return '<option value="' + p[1] + '"' + (p[0] === 'M' ? ' selected' : '') + '>' +
        names[p[0]] + ' (' + p[1] + ' px wide)</option>';
    }).join('');
  }

  var inserters = {
    element: function () {
      // Each row: a compact live-rendered preview on the left, name + desc on the right.
      // The preview uses .dw-ep-* classes (app.css) which replicate the real block styles
      // via --dw-* tokens. The site-scoped .dw-callout etc. cannot be used here because
      // they are inside @scope (.dw-live-surface) — so we mirror them with preview classes.
      var rows = elementLibrary.map(function (e2, i) {
        return '<button type="button" class="elib-row" data-i="' + i + '" ' +
          'data-tip="Click to insert a ' + esc(e2.name) + ' block at the cursor.">' +
          '<div class="dw-elem-preview">' + e2.preview + '</div>' +
          '<div class="elib-meta">' +
          '<span class="elib-name">' + esc(e2.name) + '</span>' +
          '<span class="elib-desc">' + esc(e2.desc) + '</span>' +
          '</div></button>';
      }).join('');
      openModal('Insert element',
        '<p class="mhelp">Designed building blocks, rendered the same in the manual, views, and ' +
        'slides. Click one to insert it at the cursor, then replace the placeholder text.</p>' +
        '<div class="elib">' + rows + '</div>',
        function () {},
        function (root) {
          Array.prototype.forEach.call(root.querySelectorAll('.elib-row'), function (btn) {
            btn.onclick = function () {
              var entry = elementLibrary[parseInt(btn.dataset.i, 10)];
              window.DocWizEditor2.insert({ type: 'element', kind: entry.kind });
              state.dirty = true;
              $('modal').close();
              setStatus('Element inserted — replace the placeholder text.', 'ok');
            };
          });
        },
        { hideOk: true }); // a row click inserts and closes; an OK button would do nothing
    },

    image: function () {
      api.json('/api/assets').then(function (res) {
        var files = res.files || [];
        var fileList = null;
        openModal('Insert image',
          '<p class="mhelp">These are the pictures in the project’s assets folder (subfolders ' +
          'included) — click one to select it. Uploading copies the file into the project; use ' +
          'the Folder box to organise it. Tip: you can also paste a screenshot straight into the ' +
          'editor (Ctrl+V).</p>' +
          '<div id="mImgList" class="file-list"></div>' +
          '<div class="row"><label>Upload new</label><input type="file" id="mImgUpload" accept="image/*"></div>' +
          '<div class="row"><label>Into folder</label><input id="mImgFolder" list="mImgFolders" ' +
          'placeholder="(root) — e.g. renders">' + folderDatalist('mImgFolders', res.folders) + '</div>' +
          '<div class="row"><label>Size</label><select id="mImgSize">' +
          imgSizeOptions() +
          '</select></div>' +
          '<div class="row"><label>Alignment</label><select id="mImgAlign">' +
          '<option value="">Centered (default)</option>' +
          '<option value=".img-left">Left</option>' +
          '<option value=".img-right">Right</option>' +
          '</select></div>' +
          '<div class="row"><label>Caption</label><input id="mImgAlt" placeholder="filled automatically — edit if you like"></div>',
          function (root) {
            var name = fileList && fileList.get();
            if (!name) return modalWarn(root, 'Pick an image from the list first — or upload one.');
            var alt = root.querySelector('#mImgAlt').value || baseName(name);
            var width = root.querySelector('#mImgSize').value;
            var align = root.querySelector('#mImgAlign').value;
            window.DocWizEditor2.insert({
              type: 'image',
              src: assetHref(name),
              alt: alt,
              width: width || null,
              align: align ? align.replace('.img-', '') : null,
            });
            state.dirty = true;
            setStatus('Image inserted — stored in the project as assets/' + name + '.', 'ok');
          },
          function (root) {
            var altInput = root.querySelector('#mImgAlt');
            var altTouched = false;
            altInput.oninput = function () { altTouched = true; };
            function autoAlt(name) {
              if (!altTouched && name) altInput.value = baseName(name).replace(/\.[^.]+$/, '');
            }
            var listEl = root.querySelector('#mImgList');
            if (files.length === 0)
              listEl.innerHTML = '<p class="mhelp warn">No images in this project yet — upload one below.</p>';
            fileList = createFileList(listEl, files, '/assets/', autoAlt);
            root.querySelector('#mImgUpload').onchange = function (e) {
              var file = e.target.files[0];
              if (!file) return;
              var folder = root.querySelector('#mImgFolder').value.trim();
              fetch('/api/asset?name=' + encodeURIComponent(file.name) +
                    (folder ? '&folder=' + encodeURIComponent(folder) : ''), { method: 'POST', body: file })
                .then(function (r) { return r.json(); })
                .then(function (up) {
                  if (files.length === 0) { listEl.innerHTML = ''; }
                  files.push(up.fileName);
                  fileList.add(up.fileName);
                });
            };
          });
      });
    },

    audience: function () {
      var checks = state.project.audiences.map(function (a) {
        return '<label style="margin-right:10px"><input type="checkbox" value="' + esc(a.id) + '"> ' + esc(a.title) + '</label>';
      }).join('');
      openModal('Insert audience block',
        '<p class="mhelp">An audience block is a piece of the chapter that only some readers need. ' +
        'Check who it is for: the generated views of the <b>other</b> audiences will hide it. ' +
        'The full manual always shows it, marked with a small tag.</p>' +
        '<p class="mhelp">Typical uses: a user-only warning, support-only steps, ' +
        'admin-only technical details.</p>' +
        '<div class="row">' + checks + '</div>',
        function (root) {
          var ids = Array.prototype.slice.call(root.querySelectorAll('input:checked'))
            .map(function (cb) { return cb.value; });
          if (ids.length === 0) return modalWarn(root, 'Check at least one audience.');
          window.DocWizEditor2.insert({ type: 'audience', audiences: ids });
          state.dirty = true;
          setStatus('Audience block inserted — replace the placeholder text.', 'ok');
        });
    },

    dwlink: function () {
      var picker = chapterOptgroups();
      var chapters = picker.chapters;
      var groups = picker.html;
      openModal('Insert link to chapter',
        '<p class="mhelp">Links readers can click to jump to another chapter — or to one specific ' +
        'section of it. They use stable IDs, so they keep working even when titles change.</p>' +
        '<div class="row"><label>Chapter</label><select id="mChap">' + groups + '</select></div>' +
        '<div class="row"><label>Section</label><select id="mAnchor"><option value="">(whole chapter)</option></select></div>' +
        '<div class="row"><label>Link text</label><input id="mText" placeholder="filled automatically — edit if you like"></div>',
        function (root) {
          var id = root.querySelector('#mChap').value;
          var anchor = root.querySelector('#mAnchor').value;
          var text = root.querySelector('#mText').value || id;
          var href = 'dw:' + id + (anchor ? '#' + anchor : '');
          window.DocWizEditor2.insert({ type: 'dwlink', href: href, text: text });
          state.dirty = true;
        },
        function (root) {
          var sel = root.querySelector('#mChap');
          var anchorSel = root.querySelector('#mAnchor');
          var textInput = root.querySelector('#mText');
          var textTouched = false;
          textInput.oninput = function () { textTouched = true; };
          function autoText() {
            if (textTouched) return;
            var ch = chapters.filter(function (c) { return c.id === sel.value; })[0];
            textInput.value = anchorSel.value ? prettify(anchorSel.value) : (ch ? ch.title : '');
          }
          sel.onchange = function () {
            var ch = chapters.filter(function (c) { return c.id === sel.value; })[0];
            rebuildAnchorOptions(ch, anchorSel);
            autoText();
          };
          anchorSel.onchange = autoText;
          sel.onchange();
        });
    },

    // (No add-anchor dialog: anchors are automatic on save; the toolbar's anchor
    // button copies a heading's section link.)

    attachment: function () {
      api.json('/api/attachments').then(function (res) {
        var files = res.files || [];
        var fileList = null;
        openModal('Insert attachment link',
          '<p class="mhelp">These are the documents in the project’s attachments folder (subfolders ' +
          'included) — drawing PDFs, datasheets, reports. Click one to select it; the link opens the ' +
          'document in the reader’s browser. Uploading copies the file in; use the Folder box to ' +
          'organise it.</p>' +
          '<div id="mAttList" class="file-list"></div>' +
          '<div class="row"><label>Upload new</label><input type="file" id="mAttUpload"></div>' +
          '<div class="row"><label>Into folder</label><input id="mAttFolder" list="mAttFolders" ' +
          'placeholder="(root) — e.g. datasheets">' + folderDatalist('mAttFolders', res.folders) + '</div>' +
          '<div class="row"><label>Link text</label><input id="mAttText" placeholder="filled automatically — edit if you like"></div>',
          function (root) {
            var name = fileList && fileList.get();
            if (!name) return modalWarn(root, 'Pick an attachment from the list first — or upload one.');
            var text = root.querySelector('#mAttText').value || baseName(name);
            window.DocWizEditor2.insert({
              type: 'attachment',
              href: encodeURI('../../attachments/' + name),
              text: text,
            });
            state.dirty = true;
          },
          function (root) {
            var textInput = root.querySelector('#mAttText');
            var textTouched = false;
            textInput.oninput = function () { textTouched = true; };
            function autoText(name) {
              if (!textTouched && name) textInput.value = baseName(name).replace(/\.[^.]+$/, '');
            }
            var listEl = root.querySelector('#mAttList');
            if (files.length === 0)
              listEl.innerHTML = '<p class="mhelp warn">No attachments in this project yet — upload one below, or copy files into the attachments folder.</p>';
            fileList = createFileList(listEl, files, null, autoText);
            root.querySelector('#mAttUpload').onchange = function (e) {
              var file = e.target.files[0];
              if (!file) return;
              var folder = root.querySelector('#mAttFolder').value.trim();
              fetch('/api/attachment?name=' + encodeURIComponent(file.name) +
                    (folder ? '&folder=' + encodeURIComponent(folder) : ''), { method: 'POST', body: file })
                .then(function (r) { return r.json(); })
                .then(function (up) {
                  if (files.length === 0) { listEl.innerHTML = ''; }
                  files.push(up.fileName);
                  fileList.add(up.fileName);
                });
            };
          });
      });
    },
  };

  // Section picker that RETURNS a chapter#anchor ref (no insert of its own). Reuses
  // the same chapter→section structure as the dwlink dialog, but its OK calls back the
  // bare ref so the caller decides what to build from it — the slide-zone slash menu
  // (editor2/slashmenu.js) turns it into an embed directive (a Linked element). onPick
  // receives "chapter#anchor" (or just "chapter" for a whole-chapter embed), or is not
  // called on Cancel.
  function pickSectionDialog(onPick) {
    var picker = chapterOptgroups();
    var chapters = picker.chapters;
    var groups = picker.html;
    openModal('Insert a linked element',
      '<p class="mhelp">Pick a chapter — or one specific section of it — to show here on the slide. ' +
      'It stays linked: editing the chapter updates the slide. The slide shows the live content, not a copy.</p>' +
      '<div class="row"><label>Chapter</label><select id="mLeChap">' + groups + '</select></div>' +
      '<div class="row"><label>Section</label><select id="mLeAnchor"><option value="">(whole chapter)</option></select></div>',
      function (root) {
        var id = root.querySelector('#mLeChap').value;
        if (!id) return modalWarn(root, 'Pick a chapter first.');
        var anchor = root.querySelector('#mLeAnchor').value;
        onPick(id + (anchor ? '#' + anchor : ''));
      },
      function (root) {
        var sel = root.querySelector('#mLeChap');
        var anchorSel = root.querySelector('#mLeAnchor');
        sel.onchange = function () {
          var ch = chapters.filter(function (c) { return c.id === sel.value; })[0];
          rebuildAnchorOptions(ch, anchorSel);
        };
        sel.onchange();
      });
  }

  // Name/description dialog for group containers (Task 11 + right-click Edit).
  // When called with (null, onOk): create flow — blank fields, "Name this group" title,
  //   onOk(name, desc) is called with trimmed values.
  // When called with ({ label, desc }, onOk): edit flow — prefilled fields, "Edit group" title.
  // The bridge.group() wrapper keeps the original create behaviour (patches the active group
  // via patchActiveGroup); bridge.groupEdit is the raw (current, onOk) form for the context menu.
  function groupDialog(current, onOk) {
    var isEdit = current && (current.label || current.desc);
    var title = isEdit ? 'Edit group' : 'Name this group';
    openModal(title,
      '<p class="mhelp">Give the group a short name (you pick it by this name when embedding it on a slide) and, optionally, a note for yourself.</p>' +
      '<div class="row"><label>Name</label><input id="mGrpName" type="text" placeholder="e.g. ESTOP locations &amp; reset" value="' + esc(current && current.label || '') + '"></div>' +
      '<div class="row"><label>Description</label><input id="mGrpDesc" type="text" placeholder="Author note (optional)" value="' + esc(current && current.desc || '') + '"></div>',
      function (root) {
        var name = root.querySelector('#mGrpName').value.trim();
        var desc = root.querySelector('#mGrpDesc').value.trim();
        if (onOk) onOk(name, desc);
      },
      function (root) { setTimeout(function () { root.querySelector('#mGrpName').focus(); }, 0); });
  }

  // Bridge for the live surface (editor2): its toolbar and node-view tools call
  // the guided dialogs defined here — one dialog per insert type, defined once.
  window.DocWizBridge = {
    element: inserters.element,
    image: inserters.image,
    dwlink: inserters.dwlink,
    attachment: inserters.attachment,
    audience: inserters.audience,
    pasteImage: pasteImageDialog,
    pickAsset: pickAssetDialog,
    weblink: webLinkDialog,
    editLink: editLinkDialog,
    dwPaste: dwPasteDialog,
    // Slide-zone slash menu (Task 8): pick a section → bare chapter#anchor ref;
    // embedDirective turns it into the canonical '::: embed dw:ref :::' wire form
    // (the two producers agree, so a clean embed round-trips byte-stably).
    pickSection: pickSectionDialog,
    embedDirective: embedDirective,
    group: function () { groupDialog(null, function (name, desc) {
      if (window.DocWizEditor2 && window.DocWizEditor2.patchActiveGroup)
        window.DocWizEditor2.patchActiveGroup({ label: name || null, desc: desc || null });
    }); },
    groupEdit: groupDialog,   // (current, onOk) — used by the group node's right-click Edit
    toast: editorStatus,
    saveChapter: function () { return saveChapter(); }, // Reading view saves first
    openChapterById: function (id) {
      var file = chapterFileFor(id);
      if (file) openChapter(file);
      else editorStatus('No chapter with id "' + id + '" in this project.', 'err');
    },
  };

  /* ---------- yaml editing ---------- */

  function openYaml(path) {
    if (!confirmDiscard(openYaml, arguments)) return;
    api.json('/api/yaml?path=' + encodeURIComponent(path)).then(function (res) {
      state.currentYaml = path;
      state.currentFile = null;
      state.currentView = null;
      state.dirty = false;
      show('yamlEditor');
      $('yamlPath').textContent = path;
      $('yamlText').value = res.text;
      $('yamlStatus').textContent = '';
      renderTree(state.project);
    });
  }

  function saveYaml() {
    api.send('PUT', '/api/yaml?path=' + encodeURIComponent(state.currentYaml), { text: $('yamlText').value })
      .then(function () {
        $('yamlStatus').textContent = 'Saved ✓';
        $('yamlStatus').className = 'ok';
        return loadProject().then(runCheck);
      })
      .catch(function (e) {
        $('yamlStatus').textContent = e.message;
        $('yamlStatus').className = 'err';
        throw e;
      });
  }

  /* ---------- visual view manager ---------- */

  // Structured slide layouts (mirrors SlideLayout.cs — the single source of truth).
  // Region order here is render order. Pinned to the backend by SlideRegionParityTests.
  var SLIDE_REGIONS = {
    'title-content': ['body'],
    'two-column': ['left', 'right'],
    'big-media': ['media'],
    'section-header': [],
  };
  var DEFAULT_LAYOUT = 'title-content';

  // Author-facing metadata per layout: a short label, one-line help, and a tiny
  // inline-SVG wireframe thumbnail (token-coloured) used in the add-slide gallery
  // and the inspector's layout picker. No raw colours — strokes use currentColor.
  var LAYOUT_INFO = {
    'title-content': {
      label: 'Title + content',
      desc: 'A heading and one content area — the everyday slide.',
      thumb: '<rect x="6" y="6" width="60" height="9" rx="1.5"/>' +
             '<rect x="6" y="20" width="60" height="22" rx="1.5" fill="currentColor" opacity=".12" stroke="none"/>',
    },
    'two-column': {
      label: 'Two columns',
      desc: 'Side-by-side content — text next to a picture, before/after, do/don’t.',
      thumb: '<rect x="6" y="6" width="60" height="9" rx="1.5"/>' +
             '<rect x="6" y="20" width="28" height="22" rx="1.5" fill="currentColor" opacity=".12" stroke="none"/>' +
             '<rect x="38" y="20" width="28" height="22" rx="1.5" fill="currentColor" opacity=".12" stroke="none"/>',
    },
    'big-media': {
      label: 'Big media',
      desc: 'One large picture or screenshot filling the slide.',
      thumb: '<rect x="6" y="6" width="60" height="9" rx="1.5"/>' +
             '<rect x="6" y="20" width="60" height="22" rx="1.5" fill="currentColor" opacity=".18" stroke="none"/>' +
             '<circle cx="36" cy="31" r="5"/>',
    },
    'section-header': {
      label: 'Section header',
      desc: 'A divider slide — big title and subtitle only, no content.',
      thumb: '<rect x="14" y="17" width="44" height="8" rx="1.5"/>' +
             '<rect x="20" y="29" width="32" height="5" rx="1.5" fill="currentColor" opacity=".4" stroke="none"/>',
    },
  };
  var LAYOUT_ORDER = ['title-content', 'two-column', 'big-media', 'section-header'];

  function layoutThumbSvg(layout) {
    var info = LAYOUT_INFO[layout] || LAYOUT_INFO[DEFAULT_LAYOUT];
    return '<svg class="vlay-thumb" viewBox="0 0 72 48" fill="none" stroke="currentColor" ' +
      'stroke-width="1.6" aria-hidden="true">' + info.thumb + '</svg>';
  }

  function layoutLabel(layout) {
    var info = LAYOUT_INFO[layout];
    return info ? info.label : (layout || DEFAULT_LAYOUT);
  }

  /* ---------- slide organization panel (filmstrip) ----------
     A PowerPoint-style list of the deck's slides left of the canvas: navigate (click →
     scroll the canvas to that slide + highlight), reorder (▲▼), and delete (danger). It
     is rebuilt on every refreshEditSurface so it stays in lock-step with the working
     model and the canvas. Operates on state.viewSlides; reorder/delete collect zone
     edits BEFORE re-rendering (collectZoneEdits) so an in-flight edit is never lost. */

  // A short, human label for a slide item: its title, else the first linked region's
  // section name, else an excerpt of the first authored region, else the layout name.
  // Mirrors the v1 slideThumbLabel idea adapted to the markdown-string region model.
  function slideLabelFor(slide) {
    if (slide.title && slide.title.trim()) return slide.title.trim();
    var names = SLIDE_REGIONS[slide.layout] || [];
    for (var i = 0; i < names.length; i++) {
      var md = slide.regions[names[i]];
      if (typeof md !== 'string' || !md.trim()) continue;
      var embed = md.match(/:::\s*embed\s+dw:([^\s]+)/);
      if (embed) {
        var ref = embed[1];
        var bare = ref.split('#')[0];
        var title = chapterTitleFor(bare) || bare;
        return title + (ref.indexOf('#') >= 0 ? ' › ' + prettify(ref.split('#')[1]) : '');
      }
      var line = md.split('\n').filter(function (l) { return l.trim(); })[0] || '';
      line = line.replace(/^#+\s*/, '').replace(/[*_`>]/g, '').trim();
      if (line) return line.slice(0, 48);
    }
    return layoutLabel(slide.layout) + ' slide';
  }

  // Index (into state.viewSlides) of the slide currently highlighted in the panel and
  // scrolled to in the canvas. Kept in range across deletes/reorders.
  var activeSlideIndex = 0;

  // Rebuild the slide panel from state.viewSlides. Document mode hides the whole panel
  // (the document preview is a single filtered page, not a slide deck). Called from
  // refreshEditSurface after the canvas is (re)injected so the two stay in sync.
  function renderSlidePanel() {
    var panel = $('vPanel');
    var list = $('vPanelList');
    if (!panel || !list) return;
    var slidesMode = $('vMode').value === 'slides';
    panel.hidden = !slidesMode;
    // The "+ Add slide" belt and its slide-canvas hint mean nothing on a document page.
    var belt = document.querySelector('#viewEditor .toolbelt');
    if (belt) belt.hidden = !slidesMode;
    $('vReadBtn').setAttribute('data-tip', slidesMode
      ? 'Open the deck the way your audience sees it — full-screen-ready slides, one at a time.'
      : 'Open this document the way your audience sees it — one tidy page, exactly like the handover.');
    if (!slidesMode) return;

    var slides = state.viewSlides || [];
    if (activeSlideIndex >= slides.length) activeSlideIndex = Math.max(0, slides.length - 1);
    list.innerHTML = '';
    if (slides.length === 0) {
      list.appendChild(el('div', 'vpanel-empty', 'No slides yet — use “+ Add slide”.'));
      return;
    }
    slides.forEach(function (slide, i) {
      var item = el('div', 'vpanel-item' + (i === activeSlideIndex ? ' active' : ''));
      item.setAttribute('role', 'listitem');
      item.setAttribute('tabindex', '0');
      item.dataset.i = i;
      item.setAttribute('data-tip', 'Slide ' + (i + 1) + '. Click to jump to it on the canvas.');

      item.appendChild(el('span', 'vpanel-num', String(i + 1)));
      item.appendChild(el('span', 'vpanel-badge', layoutLabel(slide.layout)));
      item.appendChild(el('span', 'vpanel-label', slideLabelFor(slide)));

      var tools = el('div', 'vpanel-tools');
      var up = el('button', 'vpanel-tbtn', '▲');
      up.type = 'button';
      up.disabled = i === 0;
      up.setAttribute('data-tip', 'Move this slide one position up.');
      up.onclick = function (e) { e.stopPropagation(); moveSlide(i, -1); };
      tools.appendChild(up);
      var down = el('button', 'vpanel-tbtn', '▼');
      down.type = 'button';
      down.disabled = i === slides.length - 1;
      down.setAttribute('data-tip', 'Move this slide one position down.');
      down.onclick = function (e) { e.stopPropagation(); moveSlide(i, 1); };
      tools.appendChild(down);
      var del = el('button', 'vpanel-tbtn vpanel-tbtn-danger', 'Delete');
      del.type = 'button';
      del.setAttribute('data-tip', 'Delete this slide. You will be asked to confirm if it has content.');
      del.onclick = function (e) { e.stopPropagation(); deleteSlide(i); };
      tools.appendChild(del);
      item.appendChild(tools);

      item.onclick = function () { gotoSlide(i); };
      item.onkeydown = function (e) {
        if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); gotoSlide(i); }
      };
      list.appendChild(item);
    });
  }

  // The canvas renders slides as [auto title slide, slides[0], slides[1], …] in slides
  // mode, so working-model slide i is the (i+1)-th .slide element.
  function canvasSlideEl(i) {
    var surface = $('vEditSurface');
    if (!surface) return null;
    var all = surface.querySelectorAll('.slide');
    return all[i + 1] || null;
  }

  // Navigate: scroll the canvas to slide i and highlight its panel item.
  function gotoSlide(i) {
    activeSlideIndex = i;
    highlightPanel(i);
    var slideEl = canvasSlideEl(i);
    if (slideEl) slideEl.scrollIntoView({ behavior: 'smooth', block: 'center' });
  }

  function highlightPanel(i) {
    var list = $('vPanelList');
    if (!list) return;
    Array.prototype.forEach.call(list.querySelectorAll('.vpanel-item'), function (item) {
      item.classList.toggle('active', parseInt(item.dataset.i, 10) === i);
    });
  }

  // Keep the active highlight in sync as the author scrolls the canvas: the slide whose
  // frame is nearest the viewport centre becomes active. Throttled via rAF.
  var scrollSyncQueued = false;
  function onCanvasScroll() {
    if (scrollSyncQueued) return;
    scrollSyncQueued = true;
    window.requestAnimationFrame(function () {
      scrollSyncQueued = false;
      var surface = $('vEditSurface');
      if (!surface || surface.hidden) return;
      var all = surface.querySelectorAll('.slide');
      if (!all.length) return;
      var mid = surface.parentNode.getBoundingClientRect();
      var center = mid.top + mid.height / 2;
      var best = -1, bestDist = Infinity;
      Array.prototype.forEach.call(all, function (el, domIdx) {
        var r = el.getBoundingClientRect();
        var d = Math.abs((r.top + r.height / 2) - center);
        if (d < bestDist) { bestDist = d; best = domIdx; }
      });
      var idx = best - 1; // subtract the auto title slide at DOM index 0
      if (idx >= 0 && idx !== activeSlideIndex) { activeSlideIndex = idx; highlightPanel(idx); }
    });
  }

  // Reorder: move slide i by dir (-1 up / +1 down). Collect any in-flight zone edit
  // BEFORE re-rendering (else unmountAll drops it), reorder the working model, keep the
  // moved slide active, mark dirty, re-render (which rebuilds the panel + remounts zones).
  function moveSlide(i, dir) {
    var slides = state.viewSlides;
    var j = i + dir;
    if (j < 0 || j >= slides.length) return;
    collectZoneEdits();
    var moved = slides.splice(i, 1)[0];
    slides.splice(j, 0, moved);
    activeSlideIndex = j;
    state.dirty = true;
    scrollActiveOnRender = true;
    renderViewEditor();
  }

  // Delete slide i. Confirm only when it has authored/linked content (an empty slide is a
  // cheap mistake to undo). Collect zone edits first so a delete never strands an edit to
  // another slide; then drop it, clamp the active index, mark dirty, re-render.
  function slideHasContent(slide) {
    if (slide.title && slide.title.trim()) return true;
    if (slide.subtitle && slide.subtitle.trim()) return true;
    return Object.keys(slide.regions || {}).some(function (k) {
      return typeof slide.regions[k] === 'string' && slide.regions[k].trim();
    });
  }

  function deleteSlide(i) {
    var slides = state.viewSlides;
    var slide = slides[i];
    if (!slide) return;
    var proceed = function () {
      collectZoneEdits();
      slides.splice(i, 1);
      if (activeSlideIndex >= slides.length) activeSlideIndex = Math.max(0, slides.length - 1);
      state.dirty = true;
      scrollActiveOnRender = true;
      renderViewEditor();
    };
    if (!slideHasContent(slide)) { proceed(); return; }
    openModal('Delete slide',
      '<p class="mhelp">Delete slide <b>' + (i + 1) + '</b> (' + esc(slideLabelFor(slide)) + ')? ' +
      'Its content is removed from this view. Linked blocks just stop being shown here — ' +
      'the source chapter is not touched.</p>',
      function () { proceed(); }, null, { okLabel: 'Delete slide', danger: true });
  }


  // Normalize a slide loaded from /api/view into the editor's working model:
  // always { layout, title, subtitle, regions:{name:{include|markdown}} }. Legacy
  // slides (no layout, no regions) map to title-content with their include/markdown
  // in the 'body' region — mirroring SlideDef.EffectiveRegions on the backend so the
  // inspector always works in terms of regions. Unknown layouts pass through so the
  // author can see and re-pick them rather than silently losing content.
  function normalizeSlide(s) {
    var layout = s.layout || null;
    var regions = {};
    if (s.regions && Object.keys(s.regions).length > 0) {
      // Canonical contract (SlideDef.Regions): region name → markdown STRING. A linked
      // section is just markdown holding a '::: embed dw:… :::' directive (resolved by
      // the pipeline on render). Keep that string verbatim in the working model.
      Object.keys(s.regions).forEach(function (name) {
        if (s.regions[name] != null) regions[name] = String(s.regions[name]);
      });
      if (!layout) layout = DEFAULT_LAYOUT;
    } else if (!layout) {
      // Legacy single-content slide → title-content body region (mirrors
      // SlideDef.EffectiveRegions): include → an embed directive; markdown → as-is.
      layout = DEFAULT_LAYOUT;
      if (s.include) regions.body = embedDirective(s.include);
      else if (s.markdown != null) regions.body = String(s.markdown);
    }
    return { layout: layout, title: s.title || null, subtitle: s.subtitle || null, regions: regions };
  }

  // The '::: embed dw:<ref> :::' directive string. Byte-exact mirror of the canonical
  // DocWizEmbed.DirectiveFor ("::: embed dw:{ref}\n:::", NO trailing newline) — the one
  // wire form. The same string is what an 'embed' node serializes back to
  // (serialize.js case 'embed' → DirectiveFor(node.attrs.ref)), so the producers agree
  // and a clean embed round-trips byte-stably.
  function embedDirective(ref) {
    return '::: embed dw:' + stripDw(ref) + '\n:::';
  }

  // Working-model slide → API payload. Regions are markdown STRINGS (the canonical
  // SlideDef.Regions contract). Prunes regions that are blank or not valid for the
  // layout so the YAML stays clean (an empty valid region is only a warning).
  function slidePayload(slide) {
    var valid = SLIDE_REGIONS[slide.layout] || [];
    var regions = {};
    var any = false;
    valid.forEach(function (name) {
      var md = slide.regions[name];
      if (typeof md === 'string' && md.trim()) { regions[name] = md; any = true; }
    });
    return {
      layout: slide.layout,
      title: slide.title || null,
      subtitle: slide.subtitle || null,
      regions: any ? regions : {},
    };
  }

  function openView(file) {
    if (!confirmDiscard(openView, arguments)) return;
    api.json('/api/view?file=' + encodeURIComponent(file)).then(function (v) {
      state.currentView = file;
      state.currentFile = null;
      state.currentYaml = null;
      state.dirty = false;
      show('viewEditor');
      $('vTitle').value = v.title || '';
      // While zone editors are mounted, a debounced full re-render would clobber the
      // editor the author is typing in (Task 7b). The title shows on the auto title
      // slide / slide headings, which are not editable zones, so deferring its live
      // re-render until the next structural change is the right trade for 7b — just
      // mark dirty. Document mode (no zone editors) re-renders live as before.
      $('vTitle').oninput = function () {
        state.dirty = true;
        if (!zoneEditorsLive()) schedulePreview(PREVIEW_DEBOUNCE_TITLE);
      };
      $('vAudience').innerHTML = state.project.audiences.map(function (a) {
        return '<option value="' + esc(a.id) + '"' + (a.id === v.audience ? ' selected' : '') + '>' + esc(a.title) + '</option>';
      }).join('');
      // Audience changes how embeds filter, so a re-render IS wanted — re-render and
      // re-mount the zones cleanly (refreshEditSurface unmounts first).
      $('vAudience').onchange = function () { state.dirty = true; schedulePreview(0); };
      $('vMode').value = v.mode === 'slides' ? 'slides' : 'document';
      $('vMode').onchange = function () { state.dirty = true; renderViewEditor(); };
      // Working copy of the slides, normalized to the structured layout+regions
      // model (legacy include/markdown slides become title-content body regions).
      state.viewSlides = (v.slides || []).map(normalizeSlide);
      activeSlideIndex = 0;
      $('vStatus').textContent = '';
      renderViewEditor();
      renderTree(state.project);
    });
  }

  function chapterTitleFor(id) {
    var found = null;
    state.project.sections.forEach(function (sec) {
      sec.chapters.forEach(function (c) { if (c.id === id) found = c.title; });
    });
    return found;
  }

  function chapterFileFor(id) {
    var found = null;
    state.project.sections.forEach(function (sec) {
      sec.chapters.forEach(function (c) { if (c.id === id) found = c.file; });
    });
    return found;
  }

  /* ---- View editor: the live authoring surface (Slides v2, ADR-007).
     The surface renders the DRAFT via the real pipeline (/api/preview-view), so what
     you see is what ships. Slides mode renders every slide stacked at natural size
     (?edit=1); document mode renders the filtered page. ---- */

  // Pull each edited slide zone's live content back into state.viewSlides before any
  // payload is built (Task 7c). collectRegions re-serializes ONLY dirty zones in place;
  // untouched regions keep their original markdown verbatim. Safe when no zones are
  // mounted (document mode, or before the surface renders) — it no-ops.
  function collectZoneEdits() {
    if (window.DocWizSlideEdit && window.DocWizSlideEdit.collectRegions) {
      window.DocWizSlideEdit.collectRegions(state.viewSlides);
    }
  }

  function currentViewPayload() {
    collectZoneEdits();
    return {
      id: viewIdFromFile(state.currentView),
      title: $('vTitle').value,
      audience: $('vAudience').value,
      mode: $('vMode').value,
      slides: state.viewSlides.map(slidePayload),
    };
  }

  /* Slides v2 (ADR-007), step 7a — the read-only scaffold of the live authoring
     surface. The deck is rendered into #vEditSurface, a .dw-live-surface container in
     THIS (parent) document — exactly like the chapter editor's #editor2 — so site.css
     applies inside it via the /app/site-scoped.css @scope wrapper and 7b can mount
     per-zone editors in one document (no cross-document ProseMirror). The server returns
     just the RenderSlides fragment (?edit=1); we inject it and mark each .slide .active.
     Each slide is rendered as the SAME 1920×1080 frame as the present deck, scaled to the
     column with CSS `zoom` (app.css .dw-slide-edit + fitSlideZoom) so EDIT is pixel-identical
     to Read while carets stay correct under the scale. 7a is read-only: zones render
     through the real pipeline (embeds resolved) but are not yet editable — 7b mounts the
     editors, 7c saves them back. The present/handover deck (scaled) lives behind
     "Read / present" (vReadBtn). Document mode uses the #vDocSurface iframe instead. */
  function renderViewEditor() {
    schedulePreview(0);
  }

  var previewTimer = null;
  function schedulePreview(delay) {
    clearTimeout(previewTimer);
    previewTimer = setTimeout(refreshEditSurface, delay == null ? PREVIEW_DEBOUNCE : delay);
  }

  // True when per-zone slide editors are mounted (Task 7b). Used to suppress the
  // incidental debounced canvas re-render that would clobber the editor in use.
  function zoneEditorsLive() {
    return !!(window.DocWizSlideEdit && window.DocWizSlideEdit.isMounted());
  }

  // WYSIWYG scale (Slides v2): each edit slide is the SAME 1920×1080 frame as the present deck
  // (so typography/layout are proportional and faithful), scaled to the column with CSS `zoom`
  // — `zoom`, not transform:scale, so ProseMirror carets stay correct under the scale in Edge.
  // NOTE (audit F10): the edit and present surfaces deliberately use DIFFERENT fit math and do
  // NOT render the slide at an identical on-screen size — the present deck (templates/site/
  // slides.js `fit()`) does a CONTAIN fit of min(availW/1920, availH/1080) with no cap, while
  // the edit column fits WIDTH only (avail/1920) capped at SLIDE_MAX_ZOOM (editing comfort on
  // wide monitors). They are separate surfaces and cannot share one function; the frame and its
  // content are identical, only the on-screen scale differs by design. Driven on first render
  // and on resize while the slide surface is showing.
  var SLIDE_DESIGN_W = 1920;
  var SLIDE_MAX_ZOOM = 0.82; // ~1574px frame — comfortable ceiling on wide monitors
  function fitSlideZoom() {
    var surface = $('vEditSurface');
    if (!surface || surface.hidden) return;
    var cs = getComputedStyle(surface);
    var padX = parseFloat(cs.paddingLeft) + parseFloat(cs.paddingRight);
    var avail = surface.clientWidth - padX;
    if (avail <= 0) return;
    var z = Math.min(avail / SLIDE_DESIGN_W, SLIDE_MAX_ZOOM);
    surface.style.setProperty('--dw-edit-zoom', String(z));
  }
  window.addEventListener('resize', fitSlideZoom);

  // Load the authoring surface. Slides mode → fetch the slide FRAGMENT (?edit=1, which
  // emits editable zones for every region + editable title/subtitle) and inject it into the
  // parent-document .dw-live-surface canvas, scale each 1920×1080 frame to the column, mark
  // every .slide .active (site.css hides inactive slides; injected HTML never runs the
  // server's <script>, so we activate them here). Document mode → the SAME editable fragment
  // (?edit=1) rendered as natural-width .view-sec cards (.dw-doc-edit), with per-zone editors
  // mounted on this live surface just like slides; the #vDocSurface iframe is now only the
  // "Read / present" view.
  function refreshEditSurface() {
    if (!state.currentView) return;
    var slides = $('vMode').value === 'slides';
    var surface = $('vEditSurface');
    var docFrame = $('vDocSurface');
    // Pull any live zone edits back into state.viewSlides BEFORE tearing the editors
    // down (Task 7c review C1). unmountAll() empties the zone list and destroys the PM
    // views, so collecting AFTER it would serialize over zero zones — a no-op that
    // silently drops the edit on every structural re-render (Add slide, audience/mode
    // change). currentViewPayload() (the fetch body below) reads state.viewSlides, so it
    // now reflects the just-collected edits; collecting again in the save path is
    // idempotent for unchanged docs (dirty zones are gone after unmount/remount).
    collectZoneEdits();
    // Tear down any mounted zone editors before we replace the surface HTML — otherwise
    // innerHTML would orphan their ProseMirror views (Task 7b clobber guard). They are
    // re-mounted below after the fresh fragment is injected.
    if (window.DocWizSlideEdit) window.DocWizSlideEdit.unmountAll();
    // Set the surface's layout look SYNCHRONOUSLY — BEFORE the async fetch — so a structural
    // re-render (Add slide, mode/audience change) never flashes the slide-frame layout before
    // the document page paints (or vice-versa). Both follow the editor page theme (no pinned
    // data-theme — Rule 16); only the framing/zoom differs. The .then below injects content.
    if (slides) {
      surface.classList.remove('dw-doc-edit', 'dw-zoomable');
      surface.classList.add('dw-slide-edit');
      surface.style.zoom = '';            // CRITICAL: clear the inline content-zoom or it overrides
                                          // .dw-slide-edit's zoom:var(--dw-edit-zoom) and the slide
                                          // canvas gets stuck at the user's content zoom.
    } else {
      surface.classList.remove('dw-slide-edit');
      surface.classList.add('dw-doc-edit', 'dw-zoomable');
    }
    if (window.__dwApplyZoom) window.__dwApplyZoom();
    // Both slides AND document modes now request the editable FRAGMENT (?edit=1) and mount
    // zone editors on the parent-document .dw-live-surface. (The iframe is only the read view.)
    var url = '/api/preview-view?edit=1';
    fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(currentViewPayload()),
    }).then(function (r) { return r.text(); })
      .then(function (html) {
        if (slides) {
          docFrame.hidden = true;
          surface.hidden = false;
          surface.innerHTML = html;
          // site.css's `.slide { display:none }` keeps slides hidden until .active; the
          // injected fragment's <script> never runs, so activate them here. That triggers
          // the `.slide.active[data-layout]` layout rules from site.css.
          Array.prototype.forEach.call(surface.querySelectorAll('.slide'), function (s) {
            s.classList.add('active');
          });
          // Scale every 1920×1080 frame to the column (WYSIWYG with Read) before mounting
          // zones, so the zones mount inside an already-zoomed frame and carets are correct.
          fitSlideZoom();
          // Rebuild the slide panel from the (possibly mutated) working model so it stays
          // in lock-step with the canvas, and keep the canvas scroll → active-item sync.
          renderSlidePanel();
          surface.parentNode.onscroll = onCanvasScroll;
          if (scrollLastOnRender) {
            scrollLastOnRender = false;
            var allLast = surface.querySelectorAll('.slide');
            if (allLast.length) {
              activeSlideIndex = Math.max(0, state.viewSlides.length - 1);
              highlightPanel(activeSlideIndex);
              allLast[allLast.length - 1].scrollIntoView({ block: 'center' });
            }
          } else if (scrollActiveOnRender) {
            scrollActiveOnRender = false;
            var elActive = canvasSlideEl(activeSlideIndex);
            if (elActive) elActive.scrollIntoView({ block: 'center' });
          }
          // Task 7b: mount a live ProseMirror editor on every .slide-region zone and let
          // the shared chapter toolbar follow whichever zone has focus. Per-zone save is
          // Task 7c — here editing is live but only marks the view dirty.
          if (window.DocWizSlideEdit) {
            window.DocWizSlideEdit.mountAll({
              surface: surface,
              slides: state.viewSlides,
              audience: $('vAudience').value,
              onZoneDirty: function () { state.dirty = true; },
            });
          }
        } else {
          // Document mode is now a LIVE surface (parent document), like slides — natural
          // reading width (no slide frame/zoom). The iframe is only used by "Read / present".
          docFrame.hidden = true;
          surface.hidden = false;
          surface.innerHTML = html; // surface look already set synchronously above
          renderSlidePanel(); // document mode → keeps the slide panel hidden
          // Mount a ProseMirror editor on every .view-sec card's zones; the shared chapter
          // toolbar follows the focused zone. Embeds resolve read-only; save is unchanged.
          if (window.DocWizSlideEdit) {
            window.DocWizSlideEdit.mountAll({
              surface: surface,
              slides: state.viewSlides,
              audience: $('vAudience').value,
              cardSelector: '.view-sec',
              onZoneDirty: function () { state.dirty = true; },
            });
          }
          if (window.__dwApplyZoom) window.__dwApplyZoom();
        }
      });
  }

  // "Read / present": the scaled present deck (the handover/Reading path, unchanged).
  // Opened in a new window so the click-through deck and its keyboard nav are whole,
  // and the authoring canvas stays put. Renders the same draft via the real pipeline.
  function presentDeck() {
    if (!state.currentView) return;
    var id = viewIdFromFile(state.currentView);
    // Save first (like the chapter Read button), then open the SAVED deck by URL. A URL is what
    // lets it open in the default browser: the native DocWiz shell routes _blank → the browser,
    // and in browser mode it's a new tab. (The old client-built popup could not be handed off.)
    saveView().then(function () {
      window.open('/preview-root/view?id=' + encodeURIComponent(id), '_blank');
    }).catch(quiet);
  }

  // The element picker may yield a dw:-prefixed value; the backend include field
  // wants the bare "chapter#anchor" ref.
  function stripDw(ref) {
    return (ref || '').replace(/^dw:/, '');
  }

  // Add slide → layout gallery: pick one of the four structured layouts. The new
  // slide is empty ({ layout, title:'', subtitle:'', regions:{} }) and is appended
  // to the end of the deck; the author fills it in live on the canvas (Task 7b
  // mounts a per-zone editor on each region). Default highlight is title-content.
  function addSlide() {
    var cards = LAYOUT_ORDER.map(function (name) {
      var info = LAYOUT_INFO[name];
      return '<button type="button" class="vlay-gallery-card" data-layout="' + esc(name) + '" ' +
        'data-tip="' + esc(info.desc) + '">' +
        layoutThumbSvg(name) +
        '<span class="vlay-name">' + esc(info.label) + '</span>' +
        '<span class="vlay-desc">' + esc(info.desc) + '</span>' +
        '</button>';
    }).join('');
    openModal('Add a slide',
      '<p class="mhelp">Pick a layout for the new slide, then fill in its content ' +
      'directly on the slide canvas.</p>' +
      '<div class="vlay-gallery">' + cards + '</div>',
      null,
      function (root) {
        Array.prototype.forEach.call(root.querySelectorAll('.vlay-gallery-card'), function (btn) {
          btn.onclick = function () {
            insertSlide(btn.dataset.layout);
            $('modal').close();
          };
        });
      },
      { hideOk: true });
  }

  // Set when a structural change should reveal the last slide once the canvas
  // re-renders (the canvas lives in this document now, so we scroll it directly
  // after the fragment is injected — no iframe onload).
  var scrollLastOnRender = false;
  // Set when a reorder/delete should re-reveal the (clamped) active slide after re-render.
  var scrollActiveOnRender = false;

  function insertSlide(layout) {
    state.viewSlides.push({ layout: layout, title: '', subtitle: '', regions: {} });
    state.dirty = true;
    scrollLastOnRender = true;
    renderViewEditor();
  }

  function saveView() {
    if (!state.currentView) return Promise.resolve();
    // currentViewPayload() first pulls any edited slide zones back into state.viewSlides
    // (Task 7c) — the one place zone edits enter the save path.
    var payload = currentViewPayload();
    return api.send('PUT', '/api/view?file=' + encodeURIComponent(state.currentView), payload)
      .then(function () {
        state.dirty = false;
        // Reset the per-zone + module dirty flags so DocWizSlideEdit.isDirty() reads
        // false after a save (Task 7c review I1). The zones stay mounted (save does not
        // re-render), so without this they would report dirty forever post-save.
        if (window.DocWizSlideEdit && window.DocWizSlideEdit.markClean) window.DocWizSlideEdit.markClean();
        $('vStatus').textContent = 'Saved ✓';
        $('vStatus').className = 'ok';
        // Re-check after a save, but only interrupt with the panel when something is broken
        // (the Check button shows the full list, warnings included, any time).
        return loadProject()
          .then(function () { return api.json('/api/check'); })
          .then(function (issues) {
            if (issues.some(function (i) { return i.severity === 'Error'; })) showIssues('Check results', issues);
          });
      })
      .catch(function (e) {
        $('vStatus').textContent = e.message;
        $('vStatus').className = 'err';
        throw e; // callers chaining Build/Export must not proceed on stale content
      });
  }

  function viewIdFromFile(file) {
    var v = (state.project.views || []).filter(function (x) { return x.file === file; })[0];
    return v ? v.id : baseName(file).replace(/\.yaml$/, '');
  }

  function deleteChapter() {
    if (!state.currentFile) return;
    var title = $('fmTitle').value || state.currentFile;
    openModal('Delete chapter',
      '<p class="mhelp">This permanently deletes “<b>' + esc(title) + '</b>”. If other ' +
      'chapters or views still link to it, the delete is refused so nothing ends up broken.</p>',
      function (root) {
        api.send('DELETE', '/api/chapter?file=' + encodeURIComponent(state.currentFile))
          .then(function () {
            $('modal').close();
            state.currentFile = null;
            state.dirty = false;
            show('welcome');
            return loadProject();
          })
          .catch(function (e) { modalWarn(root, e.message); });
        return false; // async: the dialog closes itself on success, warns on refusal
      }, null, { okLabel: 'Delete chapter', danger: true });
  }

  // Duplicate = the template workflow: build one proven view structure, stamp out a
  // copy per project/audience and adjust. Saves the source first so the copy is
  // exactly what is on screen AND on disk.
  function duplicateView() {
    if (!state.currentView) return;
    saveView().then(function () {
      var srcTitle = $('vTitle').value;
      openModal('Duplicate view',
        '<p class="mhelp">Creates a copy of "<b>' + esc(srcTitle) + '</b>" with all its ' +
        'slides — start a new training or handover from a structure that already works.</p>' +
        '<div class="row"><label>Title</label><input id="dvTitle"></div>',
        function (root) {
          var title = root.querySelector('#dvTitle').value.trim();
          var id = slugify(title);
          if (!title || !id) return modalWarn(root, 'Give the copy a title.');
          api.send('POST', '/api/view', {
            id: id, title: title, audience: $('vAudience').value, mode: $('vMode').value,
          }).then(function (res) {
            return api.send('PUT', '/api/view?file=' + encodeURIComponent(res.file), {
              id: id, title: title, audience: $('vAudience').value, mode: $('vMode').value,
              // saveView() above already pulled edited zones into state.viewSlides (7c).
              slides: state.viewSlides.map(slidePayload),
            }).then(function () { return res.file; });
          }).then(function (file) {
            $('modal').close();
            return loadProject().then(function () { openView(file); });
          }).catch(function (e) { modalWarn(root, e.message); });
          return false;
        },
        function (root) {
          // value set via DOM, not markup — titles may contain quotes
          root.querySelector('#dvTitle').value = srcTitle + ' copy';
        });
    }).catch(quiet); // save failure already shown in vStatus
  }

  function deleteView() {
    confirmDialog('Delete this view?',
      '“' + ($('vTitle').value || 'This view') + '” and all its slides will be removed. The chapters it shows are not affected.',
      'Delete view', deleteViewConfirmed, true);
  }

  function deleteViewConfirmed() {
    api.send('DELETE', '/api/view?file=' + encodeURIComponent(state.currentView))
      .then(function () {
        state.currentView = null;
        state.dirty = false;
        show('welcome');
        return loadProject();
      })
      .catch(function (e) {
        $('vStatus').textContent = 'Delete failed: ' + e.message;
        $('vStatus').className = 'err';
      });
  }

  function newView() {
    if (!confirmDiscard(newView, arguments)) return;
    var audiences = state.project.audiences.map(function (a) {
      return '<option value="' + esc(a.id) + '">' + esc(a.title) + '</option>';
    }).join('');
    openModal('New view',
      '<p class="mhelp">A view is an audience-specific output assembled from chapter sections — ' +
      'a user training deck, a support quick reference, an administrator guide.</p>' +
      '<div class="row"><label>Title</label><input id="nvTitle" placeholder="User Training"></div>' +
      '<div class="row"><label>ID</label><input id="nvId" placeholder="user-training"></div>' +
      '<div class="row"><label>Audience</label><select id="nvAud">' + audiences + '</select></div>' +
      '<div class="row"><label>Mode</label><select id="nvMode">' +
      '<option value="slides">Slides (presentation)</option>' +
      '<option value="document">Document (page)</option></select></div>',
      function (root) {
        var title = root.querySelector('#nvTitle').value.trim();
        var id = slugify(root.querySelector('#nvId').value || title);
        if (!title || !id) return modalWarn(root, 'Give the view a title.');
        api.send('POST', '/api/view', {
          id: id, title: title,
          audience: root.querySelector('#nvAud').value,
          mode: root.querySelector('#nvMode').value,
        }).then(function (res) {
          root.close();
          return loadProject().then(function () { openView(res.file); });
        }).catch(function (e) { modalWarn(root, e.message); });
        return false; // async: closes on success, keeps the input and warns on failure
      },
      function (root) {
        var t = root.querySelector('#nvTitle');
        var idEl = root.querySelector('#nvId');
        t.oninput = function () { idEl.value = slugify(t.value); };
      });
  }

  // Draft-deck-from-manual (Slides v2, Task 10): pick a chapter, POST /api/draft-view,
  // then open the drafted deck in the slide editor. The deck is a best-effort starting
  // point of linked ::: embed ::: blocks (one slide per section) — the same DeckDrafter
  // the `docwiz draft-view` CLI verb uses. Audience defaults to the chapter's first
  // tagged audience so the embeds stay clean; the author can override it here.
  function draftFromManual() {
    if (!confirmDiscard(draftFromManual, arguments)) return;
    var picker = chapterOptgroups();
    var chapters = picker.chapters;
    var groups = picker.html;
    var allAud = state.project.audiences;
    openModal('Draft a deck from a chapter',
      '<p class="mhelp">Generates a starting slide deck from one chapter: a slide per ' +
      'section, each showing the live chapter content (linked, not copied). It is a first ' +
      'draft to trim and rearrange — delete slides you don’t need and split content live.</p>' +
      '<div class="row"><label>Chapter</label><select id="dvChap">' + groups + '</select></div>' +
      '<div class="row"><label>Audience</label><select id="dvAud">' +
      allAud.map(function (a) { return '<option value="' + esc(a.id) + '">' + esc(a.title) + '</option>'; }).join('') +
      '</select></div>',
      function (root) {
        var id = root.querySelector('#dvChap').value;
        if (!id) return modalWarn(root, 'Pick a chapter first.');
        api.send('POST', '/api/draft-view', {
          chapter: id,
          audience: root.querySelector('#dvAud').value,
        }).then(function (res) {
          root.close();
          return loadProject().then(function () { openView(res.file); });
        }).catch(function (e) { modalWarn(root, e.message); });
        return false; // async: closes on success, keeps the input and warns on failure
      },
      function (root) {
        // Default the audience to the chosen chapter's first tagged audience (keeps the
        // drafted embeds clean — a chapter not tagged for the view audience would warn).
        var chapSel = root.querySelector('#dvChap');
        var audSel = root.querySelector('#dvAud');
        function syncAudience() {
          var ch = chapters.filter(function (c) { return c.id === chapSel.value; })[0];
          var first = ch && ch.audiences && ch.audiences[0];
          if (first) audSel.value = first;
        }
        chapSel.onchange = syncAudience;
        syncAudience();
      });
  }

  /* ---------- check / build / new chapter ---------- */

  function showIssues(title, issues) {
    $('issuesTitle').textContent = title;
    var list = $('issuesList');
    list.innerHTML = '';
    if (issues.length === 0) {
      list.appendChild(el('li', null, 'No issues ✓'));
    }
    issues.forEach(function (i) {
      var li = el('li');
      li.appendChild(el('span', 'sev ' + i.severity, i.severity.toUpperCase()));
      li.appendChild(el('span', 'src', i.source));
      li.appendChild(document.createTextNode(i.message));
      list.appendChild(li);
    });
    $('issuesPanel').hidden = false;
  }

  function runCheck() {
    return api.json('/api/check').then(function (issues) { showIssues('Check results', issues); });
  }

  function runBuild() {
    var save = state.currentFile ? saveChapter()
      : state.currentView ? saveView()
      : Promise.resolve();
    save.then(function () { return api.send('POST', '/api/build', {}); })
      .then(function (res) {
        showIssues(res.ok ? 'Build OK' : 'Build failed — fix the errors', res.issues);
        $('handoverLink').hidden = !res.ok;
        $('packageLink').hidden = !res.ok;
      })
      .catch(function (e) {
        // Save failed (build skipped — never build stale content) or build request failed.
        showIssues('Build did not run', [{ severity: 'Error', source: 'editor', message: e.message }]);
      });
  }

  function newChapter() {
    var sections = state.project.sections.map(function (s) {
      return '<option value="' + s.folder + '">' + s.title + '</option>';
    }).join('');
    openModal('New chapter',
      '<div class="row"><label>Section</label><select id="mSection">' + sections +
      '<option value="__new">— new section —</option></select></div>' +
      '<div class="row" id="mNewSecRow" style="display:none"><label>New section</label><input id="mNewSec" placeholder="integrations"></div>' +
      '<div class="row"><label>Title</label><input id="mTitle" placeholder="Integration Guide"></div>' +
      '<div class="row"><label>ID</label><input id="mId" placeholder="integration-guide"></div>' +
      '<div class="row"><label>Discipline</label><select id="mDisc"><option>overview</option><option>setup</option>' +
      '<option>operations</option><option>troubleshooting</option><option>reference</option>' +
      '<option>integration</option></select></div>',
      function (root) {
        var secSel = root.querySelector('#mSection').value;
        var section = secSel === '__new' ? root.querySelector('#mNewSec').value : secSel;
        var title = root.querySelector('#mTitle').value.trim();
        var id = root.querySelector('#mId').value.trim();
        if (!section || !title || !id)
          return modalWarn(root, 'Section, title, and ID are all required.');
        api.send('POST', '/api/chapter', {
          section: section,
          slug: id,
          id: id,
          title: title,
          discipline: root.querySelector('#mDisc').value,
          audiences: [],
        }).then(function (res) {
          root.close();
          return loadProject().then(function () { openChapter(res.file); });
        }).catch(function (e) { modalWarn(root, e.message); });
        return false; // async: closes on success, keeps the input and warns on failure
      },
      function (root) {
        var sel = root.querySelector('#mSection');
        var titleInput = root.querySelector('#mTitle');
        var idInput = root.querySelector('#mId');
        sel.onchange = function () {
          root.querySelector('#mNewSecRow').style.display = sel.value === '__new' ? '' : 'none';
        };
        // Sync once at open: in a project with no sections yet, "— new section —" is
        // preselected and no change event ever fires — the name input must show
        // immediately or the dialog is a dead end (stakeholder bug, 2026-06-12).
        sel.onchange();
        titleInput.oninput = function () {
          idInput.value = slugify(titleInput.value);
        };
      });
  }

  /* ---------- Word import (two-phase: preview → cherry-pick → commit) ---------- */

  // Welcome "New project from Word…": pick an empty folder, scaffold a blank project, then
  // resume into the import dialog after the reload (a sessionStorage flag survives it).
  function newProjectFromWord() {
    setBusy('Waiting for the folder picker…');
    api.send('POST', '/api/new', { template: 'blank', path: null }).then(function (res) {
      if (res.cancelled) { $('wError').hidden = true; return; }
      if (!res.ok) { showWelcomeError(new Error(res.error || 'Could not create the project.')); return; }
      sessionStorage.setItem('dwImportOnLoad', '1');
      location.reload();
    }).catch(showWelcomeError);
  }

  // Header Convert EMF…: one-shot EMF/WMF → PNG for the open project. A multi-second
  // synchronous request (up to 162 metafiles), so disable + show a busy label, then reload
  // so every surface re-renders with the now-PNG links (and the button hides — emfCount 0).
  function convertEmf() {
    var n = state.project && state.project.emfCount;
    if (!n) return;
    confirmDialog('Convert ' + n + ' Word image' + (n === 1 ? '' : 's') + ' to PNG?',
      'Browsers can’t show EMF/WMF images. The originals are moved to _emf-originals/ (kept, not shipped).',
      'Convert images', function () {
        var btn = $('convertEmfBtn');
        btn.disabled = true;
        $('convertEmfLabel').textContent = 'Converting…';
        api.send('POST', '/api/convert-emf', {}).then(function (r) {
          flashAfterReload('Images converted', 'Converted ' + r.converted + ' image(s) to PNG and updated ' +
            r.linksRewritten + ' link(s).' + (r.failed ? ' ' + r.failed + ' could not be converted and were left in place.' : ''));
          location.reload();
        }).catch(function (e) {
          btn.disabled = false;
          $('convertEmfLabel').textContent = 'Convert ' + n + ' EMF/WMF…';
          showToast('Conversion failed', e.message, 'error');
        });
      });
  }

  // A success message that must survive a page reload (shown by init()).
  function flashAfterReload(title, message) {
    try { sessionStorage.setItem('dwFlash', JSON.stringify({ title: title, message: message })); } catch (e) { /* storage off */ }
  }

  /* ---------- Export PDF (background job) ----------
     Pick output folder + which items (full manual / per view), kick off a background
     job (POST /api/export-pdf), then poll its status. Toasts keep the user informed
     without blocking the editor; the done toast opens the output folder on click. */
  function openExportPdf() {
    var p = state.project;
    if (!p) return;
    var defaultPath = (p.root || '') + '\\output\\Export';

    // Chapters start checked: "Full manual" is the select-all master over them, so an
    // untouched dialog means "the whole manual" (every chapter selected).
    var chapterRows = (p.sections || []).map(function (s) {
      var opts = (s.chapters || []).map(function (c) {
        return '<label class="mradio mradio-sub"><input type="checkbox" class="expChapter exp-cb" value="' +
               esc(c.id) + '" checked> ' + esc(c.title) + '</label>';
      }).join('');
      return opts ? '<div class="mgroup-lbl">' + esc(s.title) + '</div>' + opts : '';
    }).join('');

    var viewRows = (p.views || []).map(function (v) {
      return '<label class="mradio"><input type="checkbox" class="expView exp-cb" value="' + esc(v.id) + '"> ' +
             esc(v.title) + ' <span class="mono">(' + esc(v.mode) + ')</span></label>';
    }).join('');

    var html =
      '<div class="mlbl">Format</div>' +
      '<label class="mradio"><input type="radio" class="exp-cb" name="expFormat" value="pdf" checked> PDF</label>' +
      '<label class="mradio"><input type="radio" class="exp-cb" name="expFormat" value="html"> Single HTML file</label>' +
      '<label class="mlbl">Output folder</label>' +
      '<div class="row"><input type="text" id="expPath" value="' + esc(defaultPath) + '">' +
      '<button type="button" id="expPick">Change…</button></div>' +
      '<div class="mlbl">What to export</div>' +
      '<label class="mradio"><input type="checkbox" class="exp-cb" id="expManual" checked> Full manual <span class="mono">(all chapters)</span></label>' +
      (chapterRows ? '<div class="mgroup-lbl">Chapters</div>' + chapterRows : '') +
      (viewRows ? '<div class="mgroup-lbl">Views</div>' + viewRows : '');

    openModal('Export', html, function (modal) {
      var path = modal.querySelector('#expPath').value.trim();
      var format = modal.querySelector('input[name="expFormat"]:checked').value;
      var allChapters = modal.querySelectorAll('.expChapter');
      var checkedChapters = modal.querySelectorAll('.expChapter:checked');
      // Every chapter selected == the whole manual (proper cover); a partial set is a subset.
      var manual = allChapters.length > 0 && checkedChapters.length === allChapters.length;
      var chapterIds = manual ? [] : Array.prototype.map.call(checkedChapters, function (c) { return c.value; });
      var viewIds = Array.prototype.map.call(modal.querySelectorAll('.expView:checked'), function (c) { return c.value; });
      if (!manual && chapterIds.length === 0 && viewIds.length === 0) {
        return modalWarn(modal, 'Pick at least one chapter or view to export.');
      }
      api.send('POST', '/api/export-pdf', { path: path, manual: manual, chapterIds: chapterIds, viewIds: viewIds, format: format })
        .then(function (r) { showToast('Exporting…', 'This runs in the background; you can keep working.', 'success'); pollPdf(r.id); })
        .catch(function (e) { reportError(e.message || String(e)); });
      return true;
    }, function (modal) {
      modal.querySelector('#expPick').onclick = function () {
        api.json('/api/pick-folder').then(function (res) { if (res && res.path) modal.querySelector('#expPath').value = res.path; });
      };
      // "Full manual" is the select-all master for the chapter list: toggling it sets every
      // chapter; toggling a chapter drives it (all → checked, some → indeterminate, none → off).
      var manualCb = modal.querySelector('#expManual');
      var chapterCbs = Array.prototype.slice.call(modal.querySelectorAll('.expChapter'));
      function syncManual() {
        var n = chapterCbs.filter(function (c) { return c.checked; }).length;
        manualCb.checked = n === chapterCbs.length && chapterCbs.length > 0;
        manualCb.indeterminate = n > 0 && n < chapterCbs.length;
      }
      manualCb.addEventListener('change', function () {
        chapterCbs.forEach(function (c) { c.checked = manualCb.checked; });
        manualCb.indeterminate = false;
      });
      chapterCbs.forEach(function (c) { c.addEventListener('change', syncManual); });
      syncManual();
    });
  }

  function pollPdf(id) {
    var tick = function () {
      api.json('/api/export-pdf/status?id=' + encodeURIComponent(id)).then(function (s) {
        if (s.status === 'running') { setTimeout(tick, 1000); return; }
        if (s.status === 'error') { reportError('Export failed: ' + (s.error || 'unknown')); return; }
        var ok = (s.items || []).filter(function (i) { return i.success; }).length;
        var bad = (s.items || []).filter(function (i) { return !i.success; });
        var msg = 'Exported ' + ok + ' file' + (ok === 1 ? '' : 's') + (bad.length ? '; ' + bad.length + ' failed' : '');
        var t = showToast('Export done', msg + ' — click to open folder', 'success');
        t.style.cursor = 'pointer';
        t.onclick = function () { api.send('POST', '/api/open-path', { path: s.outDir }).catch(quiet); };
      }).catch(function (e) { reportError(e.message || String(e)); });
    };
    setTimeout(tick, 1000);
  }

  // Header Import…: the cherry-pick dialog for the mounted project.
  function importDocx() {
    if (!confirmDiscard(importDocx, arguments)) return;
    var sections = state.project.sections.map(function (s) {
      return '<option value="' + esc(s.folder) + '">' + esc(s.title) + '</option>';
    }).join('');
    state.importToken = null;
    openModal('Import from Word',
      '<p class="mhelp">Pick a Word (.docx) document. DocWiz converts it, then lets you choose ' +
      'which of its sections to add as chapters. The document itself is not changed.</p>' +
      '<div class="row"><label>Word file</label><input type="file" id="impFile" accept=".docx"></div>' +
      '<div class="row"><label>Add to section</label><select id="impSection">' + sections +
      '<option value="__new">— new section —</option></select></div>' +
      '<div class="row" id="impNewSecRow" style="display:none"><label>New section</label>' +
      '<input id="impNewSec" placeholder="040-imported"></div>' +
      '<div id="impSections" class="imp-sections"></div>' +
      '<p class="mhelp imp-status" id="impStatus" hidden></p>',
      function (root) { return commitImport(root); },
      function (root) {
        var secSel = root.querySelector('#impSection');
        secSel.onchange = function () {
          root.querySelector('#impNewSecRow').style.display = secSel.value === '__new' ? '' : 'none';
        };
        secSel.onchange();
        root.querySelector('#impFile').onchange = function (e) {
          var file = e.target.files && e.target.files[0];
          if (file) previewImport(root, file);
        };
        var ok = root.querySelector('#mOk');
        if (ok) { ok.textContent = 'Import selected'; ok.disabled = true; }
      });
  }

  function previewImport(root, file) {
    var status = root.querySelector('#impStatus');
    status.hidden = false; status.classList.remove('warn');
    status.textContent = 'Reading “' + file.name + '” — converting…';
    state.importToken = null;
    setImportOk(root, false);
    fetch('/api/import-docx/preview?name=' + encodeURIComponent(file.name), { method: 'POST', body: file })
      .then(function (r) { return r.json(); })
      .then(function (p) {
        if (!p.pandocFound) { status.classList.add('warn'); status.textContent = p.error || 'Word import is not available on this machine.'; return; }
        if (!p.token) { status.classList.add('warn'); status.textContent = p.error || 'Could not read this document.'; return; }
        state.importToken = p.token;
        status.hidden = true;
        renderImportSections(root, p);
      })
      .catch(function (e) { status.classList.add('warn'); status.textContent = e.message; });
  }

  function renderImportSections(root, p) {
    var box = root.querySelector('#impSections');
    box.innerHTML = '';
    if (!p.sections.length) {
      box.appendChild(el('p', 'mhelp', 'No sections were detected in this document.'));
      setImportOk(root, false);
      return;
    }
    box.appendChild(el('div', 'imp-head', 'Sections to import'));
    p.sections.forEach(function (s) {
      var row = el('label', 'imp-row' + (s.looksLikeFrontMatter ? ' imp-junk' : ''));
      var cb = el('input', 'imp-cb');
      cb.type = 'checkbox';
      cb.value = s.slug;
      cb.checked = !s.looksLikeFrontMatter; // front-matter junk is unchecked by default
      cb.onchange = function () { updateImportCount(root); };
      row.appendChild(cb);
      var info = el('div', 'imp-info');
      info.appendChild(el('div', 'imp-title', s.title));
      var stats = [];
      if (s.paragraphs) stats.push(s.paragraphs + ' ¶');
      if (s.tables) stats.push(s.tables + (s.tables === 1 ? ' table' : ' tables'));
      if (s.images) stats.push(s.images + (s.images === 1 ? ' image' : ' images'));
      var meta = el('div', 'imp-meta', stats.join(' · ') || 'empty');
      if (s.looksLikeFrontMatter) {
        meta.appendChild(document.createTextNode(' · '));
        meta.appendChild(el('span', 'imp-flag', 'looks like front-matter — usually skip'));
      }
      info.appendChild(meta);
      row.appendChild(info);
      box.appendChild(row);
    });
    updateImportCount(root);
  }

  function importCheckboxes(root) {
    return Array.prototype.slice.call(root.querySelectorAll('.imp-cb'));
  }
  function updateImportCount(root) {
    var n = importCheckboxes(root).filter(function (c) { return c.checked; }).length;
    setImportOk(root, n > 0);
    var ok = root.querySelector('#mOk');
    if (ok) ok.textContent = n > 0 ? 'Import ' + n + ' section' + (n === 1 ? '' : 's') : 'Import selected';
  }
  function setImportOk(root, enabled) {
    var ok = root.querySelector('#mOk');
    if (ok) ok.disabled = !enabled;
  }

  // onOk for the import dialog: kicks the commit, then the dialog closes (commit is async).
  function commitImport(root) {
    if (!state.importToken) return modalWarn(root, 'Pick a Word file first.');
    var selected = importCheckboxes(root).filter(function (c) { return c.checked; }).map(function (c) { return c.value; });
    if (!selected.length) return modalWarn(root, 'Select at least one section to import.');
    var secSel = root.querySelector('#impSection');
    var section = secSel.value === '__new' ? root.querySelector('#impNewSec').value.trim() : secSel.value;
    if (!section) return modalWarn(root, 'Name the new section.');
    var allAudiences = state.project.audiences.map(function (a) { return a.id; });
    api.send('POST', '/api/import-docx/commit', {
      token: state.importToken, selected: selected, section: section,
      discipline: '', audiences: allAudiences, // None + all by default; the walkthrough refines
    }).then(function (outcome) {
      state.importToken = null;
      if (outcome.error) { noticeDialog('Import didn’t finish', outcome.error); return; }
      loadProject().then(function () { afterImport(outcome); });
    }).catch(function (e) { noticeDialog('Import didn’t finish', e.message); });
  }

  function afterImport(outcome) {
    var created = outcome.chaptersCreated || [];
    if (created.length) openChapter(created[0].file);
    if (created.length)
      offerWalkthrough(created, function () { showImportReview(outcome); });
    else
      showImportReview(outcome);
  }

  function offerWalkthrough(created, done) {
    var n = created.length;
    openModal('Imported ' + n + ' chapter' + (n === 1 ? '' : 's'),
      '<p class="mhelp">They were added with <b>all audiences</b> and no discipline. Want to set ' +
      'who each chapter is for now? You can also change it later per chapter.</p>',
      null,
      function (root) {
        var bar = root.querySelector('.buttons');
        bar.innerHTML =
          '<button type="button" id="wtLater">Later</button>' +
          '<button type="button" id="wtGo" class="primary">Walk me through</button>';
        root.querySelector('#wtLater').onclick = function () { root.close(); if (done) done(); };
        root.querySelector('#wtGo').onclick = function () { root.close(); runWalkthrough(created.slice(), 0, done); };
      },
      { hideOk: true });
  }

  var WALK_DISCIPLINES = ['', 'overview', 'setup', 'operations', 'troubleshooting', 'reference', 'integration'];

  function runWalkthrough(chapters, i, done) {
    if (i >= chapters.length) { if (done) done(); return; }
    var ch = chapters[i];
    var auds = state.project.audiences.map(function (a) {
      return '<label class="wt-aud"><input type="checkbox" class="wt-aud-cb" value="' + esc(a.id) + '" checked> ' + esc(a.title) + '</label>';
    }).join('');
    var guess = guessDiscipline(ch.title);
    var discOpts = WALK_DISCIPLINES.map(function (d) {
      return '<option value="' + d + '"' + (d === guess ? ' selected' : '') + '>' + (d === '' ? 'None' : d) + '</option>';
    }).join('');
    openModal('Chapter ' + (i + 1) + ' of ' + chapters.length + ': ' + esc(ch.title),
      '<p class="mhelp">Who is this chapter for? Audiences decide which views it can appear in. ' +
      'Discipline is just a label — leave it as None if unsure.</p>' +
      '<div class="row"><label>Audiences</label><div class="wt-auds">' + auds + '</div></div>' +
      '<div class="row"><label>Discipline</label><select id="wtDisc">' + discOpts + '</select></div>',
      null,
      function (root) {
        var bar = root.querySelector('.buttons');
        var last = i + 1 >= chapters.length;
        bar.innerHTML =
          '<button type="button" id="wtSkip">Skip</button>' +
          '<button type="button" id="wtNext" class="primary">' + (last ? 'Finish' : 'Next') + '</button>';
        root.querySelector('#wtSkip').onclick = function () { root.close(); runWalkthrough(chapters, i + 1, done); };
        root.querySelector('#wtNext').onclick = function () {
          var picked = Array.prototype.slice.call(root.querySelectorAll('.wt-aud-cb'))
            .filter(function (c) { return c.checked; }).map(function (c) { return c.value; });
          api.send('POST', '/api/chapter-frontmatter', {
            file: ch.file, discipline: root.querySelector('#wtDisc').value, audiences: picked,
          }).then(function () { root.close(); runWalkthrough(chapters, i + 1, done); })
            .catch(function (e) { modalWarn(root, e.message); });
        };
      },
      { hideOk: true });
  }

  // Keyword guess for the discipline label — a suggestion the human confirms in the
  // walkthrough, never a silent default (discipline is inert in the build).
  function guessDiscipline(title) {
    var t = (title || '').toLowerCase();
    var map = [
      ['install', 'setup'], ['setup', 'setup'], ['configur', 'setup'], ['requirement', 'setup'],
      ['procedure', 'operations'], ['daily', 'operations'], ['how to', 'operations'], ['operation', 'operations'],
      ['troubleshoot', 'troubleshooting'], ['error', 'troubleshooting'], ['faq', 'troubleshooting'], ['problem', 'troubleshooting'],
      ['api', 'integration'], ['integration', 'integration'], ['import', 'integration'], ['export', 'integration'],
      ['reference', 'reference'], ['schema', 'reference'], ['glossary', 'reference'],
      ['overview', 'overview'], ['introduction', 'overview'],
    ];
    for (var k = 0; k < map.length; k++)
      if (t.indexOf(map[k][0]) >= 0) return map[k][1];
    return '';
  }

  // Review worklist after an import: the converter's review items (deep-linked to the
  // chapter that needs attention) plus the post-import validation issues. Reuses the
  // issues panel chrome.
  function showImportReview(outcome) {
    var report = outcome.report || [];
    var issues = outcome.issues || [];
    var created = outcome.chaptersCreated || [];
    $('issuesTitle').textContent = 'Imported ' + created.length + ' chapter' + (created.length === 1 ? '' : 's') +
      (report.length ? ' — ' + report.length + ' to review' : '');
    var list = $('issuesList');
    list.innerHTML = '';
    if (!report.length && !issues.length)
      list.appendChild(el('li', null, 'Nothing to review ✓'));
    report.forEach(function (line) {
      var li = el('li', 'imp-task');
      li.appendChild(el('span', 'sev Warning', 'REVIEW'));
      var colon = line.indexOf(':');
      var slug = colon > 0 ? line.slice(0, colon) : '';
      var match = created.filter(function (c) { return c.id === slug || c.id.indexOf(slug) === 0; })[0];
      if (match) {
        var link = el('a', 'imp-link', match.title);
        link.href = '#';
        link.onclick = function (e) { e.preventDefault(); $('issuesPanel').hidden = true; openChapter(match.file); };
        li.appendChild(link);
        li.appendChild(document.createTextNode(' — ' + line.slice(colon + 1).trim()));
      } else {
        li.appendChild(document.createTextNode(line));
      }
      list.appendChild(li);
    });
    issues.forEach(function (iss) {
      var li = el('li');
      li.appendChild(el('span', 'sev ' + iss.severity, iss.severity.toUpperCase()));
      li.appendChild(el('span', 'src', iss.source));
      li.appendChild(document.createTextNode(iss.message));
      list.appendChild(li);
    });
    $('issuesPanel').hidden = false;
  }

  /* ---------- modal ---------- */

  // opts (all optional; callers that omit opts get the original Cancel / OK footer):
  //   hideOk     — no primary button, for click-to-pick galleries where the card click acts and closes;
  //   okLabel    — name the action ("Delete chapter"), never a bare OK on a step that changes things;
  //   danger     — the action destroys something: danger outline, never a primary (DESIGN_SYSTEM rule 8);
  //   hideCancel — a notice with a single button.
  function openModal(title, bodyHtml, onOk, onMounted, opts) {
    opts = opts || {};
    var hideOk = opts.hideOk;
    var modal = $('modal');
    modal.innerHTML = '<h3>' + title + '</h3>' + bodyHtml +
      '<div class="buttons">' +
      (opts.hideCancel ? '' : '<button type="button" id="mCancel">Cancel</button>') +
      (hideOk ? '' : '<button type="button" id="mOk" class="' + (opts.danger ? 'danger' : 'primary') + '">' +
        esc(opts.okLabel || 'OK') + '</button>') + '</div>';
    var cancel = modal.querySelector('#mCancel');
    if (cancel) cancel.onclick = function () { modal.close(); };
    if (!hideOk) {
      modal.querySelector('#mOk').onclick = function () {
        // onOk returning false = validation failed: keep the dialog open (it shows
        // an inline warning) instead of silently discarding the input.
        if (onOk(modal) === false) return;
        modal.close();
      };
    }
    if (onMounted) onMounted(modal);
    modal.showModal();
  }

  // In-app replacements for the browser's confirm() / alert() / prompt(): same dialog, same
  // look, and the button names the action. onConfirm / onValue run after the dialog has
  // closed, so they may open the next dialog (the one #modal element is reused).
  function confirmDialog(title, message, actionLabel, onConfirm, danger) {
    openModal(esc(title), '<p class="mhelp">' + esc(message) + '</p>',
      function () { setTimeout(onConfirm, 0); },
      null, { okLabel: actionLabel, danger: !!danger });
  }
  function noticeDialog(title, message) {
    openModal(esc(title), '<p class="mhelp">' + esc(message) + '</p>',
      function () {}, null, { okLabel: 'OK', hideCancel: true });
  }
  function promptDialog(title, label, placeholder, actionLabel, onValue) {
    openModal(esc(title),
      '<div class="row"><label>' + esc(label) + '</label><input id="mPrompt" placeholder="' + esc(placeholder) + '"></div>',
      function (root) {
        var v = root.querySelector('#mPrompt').value.trim();
        if (!v) return modalWarn(root, 'Type a ' + label.toLowerCase() + ' first.');
        setTimeout(function () { onValue(v); }, 0);
      },
      function (root) {
        var input = root.querySelector('#mPrompt');
        input.onkeydown = function (e) { if (e.key === 'Enter') { e.preventDefault(); root.querySelector('#mOk').click(); } };
        setTimeout(function () { input.focus(); }, 0);
      },
      { okLabel: actionLabel });
  }

  /// Shows an inline warning at the top of an open dialog and returns false
  /// (so callbacks can `return modalWarn(...)` to keep the dialog open).
  function modalWarn(root, message) {
    var warn = root.querySelector('.modal-warn');
    if (!warn) {
      warn = el('p', 'mhelp warn modal-warn');
      var h3 = root.querySelector('h3');
      h3.parentNode.insertBefore(warn, h3.nextSibling);
    }
    warn.textContent = message;
    return false;
  }

  /* ---------- Handover artifacts: Documentation packages ----------
     Manage the handover's external HTML doc tabs (e.g. a generated API site → API Reference).
     Backed by /api/external-docs (list/add/remove) + /api/pick-folder. */
  var PKG_ICON = '<svg viewBox="0 0 24 24" width="24" height="24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="3.5" y="5" width="17" height="14" rx="2"/><path d="M7 9v6M11 9v6M15 9l2.5 6M7 9h4"/></svg>';
  // Labels MUST equal HandoverBuilder.ExternalTabTitle(key) — the ONE source for the
  // shipped tab title (ADR-008). Pinned by ExternalTabTitleParityTests; do not hand-edit
  // a label to disagree with what the handover renders.
  var PKG_TYPES = [
    ['api_reference', 'API Reference'],
    ['release_notes', 'Release Notes'],
    ['integration_guide', 'Integration Guide'],
  ];

  function fmtBytes(n) {
    if (!n) return '—';
    var u = ['B', 'KB', 'MB', 'GB'], i = 0, v = n;
    while (v >= 1024 && i < u.length - 1) { v /= 1024; i++; }
    return (v >= 10 || i === 0 ? Math.round(v) : v.toFixed(1)) + ' ' + u[i];
  }

  function openDocs() {
    if (!confirmDiscard(openDocs, arguments)) return;
    state.currentArtifact = 'docs';
    state.currentFile = null; state.currentView = null; state.currentYaml = null;
    show('viewDocs');
    renderTree(state.project);
    loadPkgList();
  }

  function loadPkgList() {
    api.json('/api/external-docs').then(renderPkgList).catch(function (e) { reportError(e.message || String(e)); });
  }

  function renderPkgList(pkgs) {
    state.packages = pkgs; // addPackage warns before replacing one of these
    var list = $('pkgList'), empty = $('pkgEmpty');
    list.innerHTML = '';
    empty.hidden = pkgs.length > 0;
    list.hidden = pkgs.length === 0;
    pkgs.forEach(function (p) { list.appendChild(pkgCard(p)); });
  }

  function pkgCard(p) {
    var card = el('div', 'dwa-pkg-card');
    var icon = el('div', 'dwa-pkg-icon' + (p.valid ? '' : ' bad'));
    icon.innerHTML = PKG_ICON;
    card.appendChild(icon);

    var main = el('div', 'dwa-pkg-main');
    var top = el('div', 'dwa-pkg-toprow');
    top.appendChild(el('span', 'dwa-pkg-tab', p.title));
    top.appendChild(el('span', 'dwa-pkg-type', p.source === 'embedded' ? 'Embedded' : 'Referenced'));
    main.appendChild(top);
    var meta = el('div', 'dwa-pkg-meta');
    meta.appendChild(el('span', 'dwa-mi dwa-path', p.path));
    meta.appendChild(el('span', 'dwa-mi dwa-size', fmtBytes(p.bytes)));
    meta.appendChild(el('span', 'dwa-status ' + (p.valid ? 'ok' : 'bad'),
      p.valid ? p.entry + ' present' : 'no entry page (index.html / handover.html)'));
    main.appendChild(meta);
    card.appendChild(main);

    var actions = el('div', 'dwa-pkg-actions');
    var open = el('button', 'dwa-btn dwa-btn-secondary dwa-btn-sm', 'Open');
    open.type = 'button'; open.disabled = !p.valid;
    open.setAttribute('data-tip', 'Open this documentation in the browser (available after the handover is built).');
    // External docs are bundled under the handover's _site/ folder (HandoverBuilder.SiteFolder);
    // EditorSiteFolderParityTests pins this literal to the C# constant.
    open.onclick = function () { window.open('/handover/_site/external/' + encodeURIComponent(p.key) + '/' + encodeURIComponent(p.entry), '_blank'); };
    var rm = el('button', 'dwa-btn dwa-btn-danger dwa-btn-sm', 'Remove');
    rm.type = 'button';
    rm.setAttribute('data-tip', 'Remove this package from the handover (an embedded copy is deleted; a referenced folder is left in place).');
    rm.onclick = function () { removePackage(p); };
    actions.appendChild(open); actions.appendChild(rm);
    card.appendChild(actions);
    return card;
  }

  function removePackage(p) {
    confirmDialog('Remove “' + p.title + '” from the handover?',
      p.source === 'embedded'
        ? 'Its copy inside the project is deleted. The folder you originally added it from is not touched.'
        : 'The handover stops including it. The referenced folder is left in place.',
      'Remove package', function () {
        api.send('DELETE', '/api/external-docs?key=' + encodeURIComponent(p.key))
          .then(loadPkgList).catch(function (e) { reportError(e.message || String(e)); });
      }, true);
  }

  function addPackage() {
    api.json('/api/pick-folder').then(function (res) {
      var folder = res && res.path;
      if (!folder) return; // cancelled
      var opts = PKG_TYPES.map(function (t) { return '<option value="' + t[0] + '">' + esc(t[1]) + '</option>'; }).join('');
      var html =
        '<p class="mhelp">Selected folder: <span class="mono">' + esc(folder) + '</span><br>' +
        'It must contain an <span class="mono">index.html</span> or <span class="mono">handover.html</span> entry point.</p>' +
        '<label class="mlbl">Type (becomes the handover tab)</label>' +
        '<select id="pkgTypeSel" style="width:100%">' + opts + '</select>' +
        '<p class="mhelp warn" id="pkgReplaceNote" hidden></p>' +
        '<label class="mlbl">How to bundle it</label>' +
        '<label class="mradio"><input type="radio" name="pkgCopy" value="copy" checked> ' +
        'Copy into the project (self-contained — recommended)</label>' +
        '<label class="mradio"><input type="radio" name="pkgCopy" value="ref"> ' +
        'Reference the folder (advanced — it must stay reachable at build time)</label>';
      openModal('Add documentation package', html, function (modal) {
        var key = modal.querySelector('#pkgTypeSel').value;
        var copyIn = modal.querySelector('input[name=pkgCopy]:checked').value === 'copy';
        api.send('POST', '/api/external-docs', { key: key, folder: folder, copyIn: copyIn })
          .then(loadPkgList)
          .catch(function (e) { reportError(e.message || String(e)); });
        return true;
      }, function (modal) {
        // Adding a type that already exists replaces it (and deletes its embedded copy) —
        // say so before it happens, and name the button for what it does.
        var sel = modal.querySelector('#pkgTypeSel'), note = modal.querySelector('#pkgReplaceNote');
        var ok = modal.querySelector('#mOk');
        var sync = function () {
          var existing = (state.packages || []).filter(function (p) { return p.key === sel.value; })[0];
          note.hidden = !existing;
          if (existing) note.textContent = 'This project already has an “' + existing.title + '” package. Adding this folder replaces it' +
            (existing.source === 'embedded' ? ' and deletes the current copy inside the project.' : '.');
          ok.textContent = existing ? 'Replace package' : 'Add package';
        };
        sel.onchange = sync;
        sync();
      });
    }).catch(function (e) { reportError(e.message || String(e)); });
  }

  /* ---------- Handover artifacts: Attachments manager ---------- */
  var ATTACH_ICON = '<svg viewBox="0 0 24 24" width="24" height="24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M5 4h11l3 3v13H5z"/><path d="M8 12h8M8 15.5h5"/></svg>';

  function openAttach() {
    if (!confirmDiscard(openAttach, arguments)) return;
    state.currentFile = null; state.currentView = null; state.currentYaml = null;
    show('viewAttach');
    state.currentArtifact = 'attach';
    renderTree(state.project);
    loadAttachList();
  }

  function loadAttachList() {
    api.json('/api/attachments').then(function (r) { renderAttach(r.files || []); })
      .catch(function (e) { reportError(e.message || String(e)); });
  }

  function renderAttach(files) {
    var list = $('attachList'), empty = $('attachEmpty');
    list.innerHTML = '';
    empty.hidden = files.length > 0;
    list.hidden = files.length === 0;
    files.forEach(function (name) {
      var card = el('div', 'dwa-pkg-card');
      var icon = el('div', 'dwa-pkg-icon'); icon.innerHTML = ATTACH_ICON; card.appendChild(icon);
      var main = el('div', 'dwa-pkg-main');
      var top = el('div', 'dwa-pkg-toprow'); top.appendChild(el('span', 'dwa-pkg-tab', name)); main.appendChild(top);
      card.appendChild(main);
      var actions = el('div', 'dwa-pkg-actions');
      var del = el('button', 'dwa-btn dwa-btn-danger dwa-btn-sm', 'Delete'); del.type = 'button';
      del.onclick = function () { deleteAttach(name); };
      actions.appendChild(del); card.appendChild(actions);
      list.appendChild(card);
    });
  }

  function deleteAttach(name) {
    confirmDialog('Delete “' + name + '”?',
      'The file is removed from the project’s attachments folder. Links to it in chapters will stop working.',
      'Delete attachment', function () {
        api.send('DELETE', '/api/attachment?name=' + encodeURIComponent(name))
          .then(loadAttachList).catch(function (e) { reportError(e.message || String(e)); });
      }, true);
  }

  function uploadAttach(file) {
    fetch('/api/attachment?name=' + encodeURIComponent(file.name), { method: 'POST', body: file })
      .then(function (r) { if (!r.ok) return r.text().then(function (t) { throw new Error(t || r.statusText); }); })
      .then(loadAttachList).catch(function (e) { reportError(e.message || String(e)); });
  }

  /* ---------- Settings (interactive docwiz.yaml editor + app preferences) ---------- */
  var SET_LANGS = [
    ['en', 'English'], ['en-US', 'English (United States)'], ['en-GB', 'English (United Kingdom)'],
    ['de', 'German'], ['it', 'Italian'], ['fr', 'French'], ['es', 'Spanish'], ['cs', 'Czech'],
    ['pl', 'Polish'], ['sk', 'Slovak'], ['pt', 'Portuguese'], ['nl', 'Dutch'],
  ];

  function openSettings(section) {
    if (!confirmDiscard(openSettings, arguments)) return;
    // "Back to editor" returns to whatever was open before Settings.
    if (state.currentFile || state.currentView) state.settingsReturn = { file: state.currentFile, view: state.currentView };
    state.currentFile = null; state.currentView = null; state.currentYaml = null;
    show('setPane');
    state.currentArtifact = 'settings';
    renderTree(state.project);
    loadSettings();
    selectSettingsSection(section || 'general');
  }

  function setDirty(on) { $('setPane').classList.toggle('dirty', !!on); }

  function selectSettingsSection(sec) {
    Array.prototype.forEach.call(document.querySelectorAll('#setRail .set-item'), function (it) {
      it.classList.toggle('active', it.getAttribute('data-sec') === sec);
    });
    Array.prototype.forEach.call(document.querySelectorAll('#setPane .set-section'), function (s) {
      s.classList.toggle('active', s.id === 'sec' + sec.charAt(0).toUpperCase() + sec.slice(1));
    });
    var crumb = $('crumbSec');
    var active = document.querySelector('#setRail .set-item[data-sec="' + sec + '"]');
    if (crumb && active) crumb.textContent = active.textContent;
  }

  function loadSettings() {
    // Language options
    var langSel = $('setLanguage');
    langSel.innerHTML = SET_LANGS.map(function (l) { return '<option value="' + l[0] + '">' + esc(l[1]) + '</option>'; }).join('');
    api.json('/api/settings-project').then(function (d) {
      $('setProject').value = d.project || '';
      $('setCode').value = d.code || '';
      $('setCustomer').value = d.customer || '';
      $('setVersion').value = d.version || '';
      if (!SET_LANGS.some(function (l) { return l[0] === d.language; })) {
        langSel.insertAdjacentHTML('afterbegin', '<option value="' + esc(d.language || '') + '">' + esc(d.language || '') + '</option>');
      }
      langSel.value = d.language || 'en';
      $('setFolder').textContent = (state.project && state.project.root) || '';
      setNumbering(d.numbering !== false);
      renderAudiences(d.audiences || []);
      setDirty(false);
      validateVersion();
    }).catch(function (e) { reportError(e.message || String(e)); });
    // App prefs (window mode) — separate store, applies immediately.
    api.json('/api/settings').then(function (s) {
      Array.prototype.forEach.call(document.querySelectorAll('#winMode button'), function (b) {
        b.classList.toggle('active', b.getAttribute('data-mode') === (s.windowMode === 'browser' ? 'browser' : 'native'));
      });
    }).catch(quiet);
    // Raw YAML (the whole file)
    api.json('/api/yaml?path=docwiz.yaml').then(function (r) { $('setRawText').value = r.text || ''; }).catch(quiet);
  }

  function setNumbering(on) {
    var sw = $('numSwitch');
    sw.setAttribute('aria-checked', on ? 'true' : 'false');
    $('numLabel').textContent = on ? 'On — headings numbered' : 'Off — no numbers';
    $('numExample').classList.toggle('nonum', !on);
  }

  function renderAudiences(auds) {
    var list = $('audList');
    list.innerHTML = '';
    auds.forEach(function (a, i) { list.appendChild(audienceRow(a, i, auds.length)); });
  }

  function audienceRow(a, i, total) {
    var row = el('div', 'audrow');
    var title = el('input', 'audtitle');
    title.value = a.title || '';
    title.setAttribute('data-id', a.id);
    title.oninput = function () { setDirty(true); };
    row.appendChild(title);
    var id = el('span', 'audid', a.id);
    row.appendChild(id);
    var reorder = el('div', 'reorder');
    var up = el('button', 'mini', '▲'); up.type = 'button'; up.disabled = i === 0;
    up.onclick = function () { moveAudience(i, -1); };
    var down = el('button', 'mini', '▼'); down.type = 'button'; down.disabled = i === total - 1;
    down.onclick = function () { moveAudience(i, 1); };
    reorder.appendChild(up); reorder.appendChild(down);
    row.appendChild(reorder);
    var del = el('button', 'auddel', '✕'); del.type = 'button';
    del.setAttribute('data-tip', 'Remove this audience. Warns if chapters or views still reference it.');
    del.onclick = function () { deleteAudience(a.id); };
    row.appendChild(del);
    return row;
  }

  function collectAudiences() {
    return Array.prototype.map.call(document.querySelectorAll('#audList .audrow'), function (row) {
      return { id: row.querySelector('.audtitle').getAttribute('data-id'),
               title: row.querySelector('.audtitle').value.trim() };
    });
  }

  function moveAudience(i, dir) {
    var auds = collectAudiences();
    var j = i + dir;
    if (j < 0 || j >= auds.length) return;
    var t = auds[i]; auds[i] = auds[j]; auds[j] = t;
    renderAudiences(auds);
    setDirty(true);
  }

  // Deleting an audience: the server reports what it touches (GET /api/audience-usage). A view
  // filtered by it blocks the delete; chapter tags are removed on Save; `::: audience` blocks
  // naming it are left for the author (Check lists them).
  function deleteAudience(id) {
    var aud = collectAudiences().filter(function (a) { return a.id === id; })[0];
    var name = (aud && aud.title) || id;
    api.json('/api/audience-usage?id=' + encodeURIComponent(id)).then(function (u) {
      if (u.views.length) {
        noticeDialog('“' + name + '” is still in use',
          'The output “' + u.views[0].title + '” is for ' + name + '. Pick another audience for it first, then delete ' + name + '.');
        return;
      }
      var nCh = u.chapters.length;
      var nBl = u.blocks.reduce(function (n, b) { return n + b.count; }, 0);
      var msg = nCh
        ? nCh + ' chapter' + (nCh === 1 ? '' : 's') + ' will lose this tag when you save.'
        : 'No chapter is tagged with it.';
      if (nBl) msg += ' ' + nBl + ' “only for” block' + (nBl === 1 ? '' : 's') + ' in ' +
        u.blocks.map(function (b) { return '“' + b.title + '”'; }).join(', ') +
        ' still name' + (nBl === 1 ? 's' : '') + ' it; Check will list ' + (nBl === 1 ? 'it' : 'them') + ' so you can decide what to keep.';
      confirmDialog('Delete “' + name + '”?', msg, 'Delete audience', function () {
        renderAudiences(collectAudiences().filter(function (a) { return a.id !== id; }));
        setDirty(true);
      }, true);
    }).catch(function (e) { reportError(e.message || String(e)); });
  }

  function addAudience() {
    promptDialog('Add audience', 'Name', 'e.g. Trainer', 'Add audience', function (title) {
      var auds = collectAudiences();
      var base = title.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '') || 'audience';
      var id = base, n = 2;
      while (auds.some(function (a) { return a.id === id; })) { id = base + '-' + (n++); }
      auds.push({ id: id, title: title });
      renderAudiences(auds);
      setDirty(true);
    });
  }

  function validateVersion() {
    var inp = $('setVersion'), msg = $('verMsg');
    var ok = /^\d+\.\d+\.\d+/.test(inp.value.trim());
    inp.classList.toggle('invalid', !ok && inp.value.trim().length > 0);
    if (ok || inp.value.trim().length === 0) { msg.hidden = true; }
    else { msg.hidden = false; msg.className = 'field-msg warn'; msg.textContent = 'Use semantic version major.minor.patch (e.g. 1.0.0).'; }
    return ok || inp.value.trim().length === 0;
  }

  function saveSettings() {
    if (!$('setPane').classList.contains('dirty')) return;
    if (!validateVersion()) { selectSettingsSection('general'); reportError('Fix the version (use major.minor.patch) before saving.'); return; }
    var payload = {
      project: $('setProject').value, code: $('setCode').value, customer: $('setCustomer').value,
      version: $('setVersion').value, language: $('setLanguage').value,
      numbering: $('numSwitch').getAttribute('aria-checked') === 'true',
      audiences: collectAudiences(),
    };
    api.send('PUT', '/api/settings-project', payload).then(function () {
      setDirty(false);
      // Refresh the project model + chrome (name/code may have changed) and the raw view.
      return api.json('/api/project');
    }).then(function (p) {
      state.project = p; setProjectHeader(p); renderTree(p);
      return api.json('/api/yaml?path=docwiz.yaml');
    }).then(function (r) { $('setRawText').value = r.text || ''; })
      .catch(function (e) { showToast('Settings not saved', e.message || String(e), 'error'); });
  }

  function setWindowMode(mode) {
    Array.prototype.forEach.call(document.querySelectorAll('#winMode button'), function (b) {
      b.classList.toggle('active', b.getAttribute('data-mode') === mode);
    });
    api.send('POST', '/api/settings', { windowMode: mode }).catch(function (e) { reportError(e.message || String(e)); });
  }

  function show(sectionId) {
    // Leaving the view editor: tear down any mounted slide-zone editors (Task 7b) so
    // their ProseMirror views don't leak and the shared toolbar's active view resets.
    if (sectionId !== 'viewEditor' && window.DocWizSlideEdit) window.DocWizSlideEdit.unmountAll();
    // Leaving the chapter editor: destroy the chapter EditorView (close()) so the hidden
    // chapter doesn't linger as getActiveView()'s fallback (main.js current.view). Without
    // this, a pre-focus slide-toolbar click would silently mutate the hidden chapter
    // (7b review I-2). Runs AFTER each caller's confirmDiscard(), so the dirty guard is
    // never bypassed; re-entering a chapter re-opens cleanly (DocWizEditor2.open calls
    // close() itself), so we never close the view we're about to mount.
    if (sectionId !== 'chapterEditor' && window.DocWizEditor2) window.DocWizEditor2.close();
    if (sectionId !== 'viewDocs' && sectionId !== 'viewAttach' && sectionId !== 'setPane') state.currentArtifact = null;
    ['welcome', 'chapterEditor', 'yamlEditor', 'viewEditor', 'viewDocs', 'viewAttach', 'setPane'].forEach(function (id) {
      $(id).hidden = id !== sectionId;
    });
  }

  /* ---------- tooltip system ----------
     Any element with a data-tip attribute gets an explain box on hover or keyboard
     focus. Event delegation: works for elements created later (tree, modals). */

  (function initTooltips() {
    var tip = el('div');
    tip.id = 'tooltip';
    document.body.appendChild(tip);
    var timer = null;
    var current = null;

    function place(target) {
      tip.textContent = target.getAttribute('data-tip');
      tip.classList.add('show');
      var r = target.getBoundingClientRect();
      var x = Math.min(Math.max(8, r.left), window.innerWidth - tip.offsetWidth - 8);
      var y = r.bottom + 8;
      if (y + tip.offsetHeight > window.innerHeight - 8) y = r.top - tip.offsetHeight - 8;
      tip.style.left = x + 'px';
      tip.style.top = y + 'px';
    }

    function show(target, delay) {
      if (target === current) return;
      current = target;
      clearTimeout(timer);
      timer = setTimeout(function () { place(target); }, delay);
    }

    function hide() {
      clearTimeout(timer);
      tip.classList.remove('show');
      current = null;
    }

    // The element just clicked gets no tip until the pointer leaves it: a click opens menus
    // and dialogs, and a late tip would land on top of them.
    var clicked = null;
    document.addEventListener('mouseover', function (e) {
      var target = e.target.closest && e.target.closest('[data-tip]');
      if (target && target === clicked) return;
      clicked = null;
      if (target) show(target, TIP_HOVER_DELAY); // see the constant for the rationale
      else if (current) hide();
    });
    document.addEventListener('focusin', function (e) {
      var target = e.target.closest && e.target.closest('[data-tip]');
      // Keyboard focus only — a mouse click also focuses, and that is not a request for help.
      if (target && target !== clicked && target.matches(':focus-visible')) show(target, TIP_FOCUS_DELAY);
    });
    document.addEventListener('focusout', hide);
    document.addEventListener('mousedown', function (e) {
      hide();
      clicked = e.target.closest ? e.target.closest('[data-tip]') : null;
    });
    window.addEventListener('scroll', hide, true);
  })();

  /* ---------- wiring ----------
     on() guards each binding so a single missing element can never abort the rest
     (which previously left later buttons like Add section/text dead). */

  /* ---------- Save As / Save as template ---------- */

  // Copy the whole project to a folder you pick, then switch to the copy (the original
  // is left as it is). Server runs the native folder picker.
  function saveAs() {
    if (!confirmDiscard(saveAs, arguments)) return;
    api.send('POST', '/api/save-as', { path: null }).then(function (res) {
      if (res.cancelled) return;
      if (res.ok) { location.reload(); return; }
      noticeDialog('Copy not saved', res.error || 'Could not save a copy.');
    }).catch(function (e) { noticeDialog('Copy not saved', e.message); });
  }

  // Copy the current project into the Templates library + a template.yaml wrapper.
  // Stays on the current project — the live surface is file-backed, so the copy on disk
  // already reflects saved work.
  function saveAsTemplate() {
    var defName = (state.project && state.project.project) || '';
    openModal('Save as template',
      '<p class="mhelp">Saves a copy of this project into your Templates folder so you and ' +
      'your colleagues can start new projects from it. You keep working on the current ' +
      'project — the template is a separate copy.</p>' +
      '<div class="row"><label>Name</label><input id="stName" placeholder="Product handbook"></div>' +
      '<div class="row"><label>Description</label><input id="stDesc" placeholder="What this template is for (optional)"></div>',
      function (root) {
        var name = root.querySelector('#stName').value.trim();
        if (!name) return modalWarn(root, 'Give the template a name.');
        api.send('POST', '/api/save-as-template', {
          name: name,
          description: root.querySelector('#stDesc').value.trim(),
        }).then(function (res) {
          if (!res.ok) noticeDialog('Template not saved', res.error || 'Could not save the template.');
          else showToast('Template saved', '“' + name + '” is now in your Templates folder.', 'success');
        }).catch(function (e) { noticeDialog('Template not saved', e.message); });
      },
      function (root) { root.querySelector('#stName').value = defName; });
  }

  function on(id, handler) {
    var elm = $(id);
    if (elm) elm.onclick = handler;
  }

  on('homeBtn', function closeProject() {
    if (!confirmDiscard(closeProject, arguments)) return;
    api.send('POST', '/api/close-project', {}).then(function () { location.reload(); });
  });
  on('openFolderBtn', function () { api.send('POST', '/api/open-folder', {}).catch(quiet); });
  on('wOpenBtn', function () { openProject(null); });
  on('wNewBtn', function () { newProject('blank'); });
  // Saves rethrow on failure (so chained Build/Export stop); for direct button
  // clicks the status line already shows the reason — swallow the rethrow here.
  var quiet = function () {};
  on('addPkgBtn', addPackage);
  on('attachUploadBtn', function () { $('attachFile').click(); });
  (function wireAttachUpload() {
    var f = $('attachFile');
    if (f) f.addEventListener('change', function () {
      if (f.files && f.files[0]) { uploadAttach(f.files[0]); f.value = ''; }
    });
  })();
  // Settings surface wiring (static elements in index.html).
  on('setBack', function backFromSettings() {
    if (!confirmDiscard(backFromSettings, arguments)) return;
    var back = state.settingsReturn; state.settingsReturn = null;
    if (back && back.file) openChapter(back.file);
    else if (back && back.view) openView(back.view);
    else show('welcome');
  });
  on('setOpenFolder', function () { api.send('POST', '/api/open-folder', {}).catch(quiet); });
  on('setSave', saveSettings);
  on('addAud', addAudience);
  on('numSwitch', function () {
    setNumbering($('numSwitch').getAttribute('aria-checked') !== 'true'); setDirty(true);
  });
  on('setRawSave', function () {
    api.send('PUT', '/api/yaml?path=docwiz.yaml', { text: $('setRawText').value })
      .then(function () { showToast('Saved', 'docwiz.yaml was saved.', 'success'); loadSettings(); })
      .catch(function (e) { reportError(e.message || String(e)); });
  });
  (function wireSettingsStatic() {
    var rail = $('setRail');
    if (rail) rail.addEventListener('click', function (e) {
      var item = e.target.closest('.set-item[data-sec]');
      if (item) selectSettingsSection(item.getAttribute('data-sec'));
    });
    var wm = $('winMode');
    if (wm) wm.addEventListener('click', function (e) {
      var b = e.target.closest('button[data-mode]');
      if (b) setWindowMode(b.getAttribute('data-mode'));
    });
    ['setProject', 'setCode', 'setCustomer', 'setLanguage'].forEach(function (id) {
      var elx = $(id);
      if (elx) elx.addEventListener('input', function () { setDirty(true); });
    });
    var ver = $('setVersion');
    if (ver) ver.addEventListener('input', function () { setDirty(true); validateVersion(); });
  })();
  on('saveBtn', function () { saveChapter().catch(quiet); });
  on('yamlSaveBtn', function () { saveYaml().catch(quiet); });
  on('vSaveBtn', function () { saveView().catch(quiet); });
  on('vReadBtn', presentDeck);
  on('vExportBtn', function () {
    var go = function () {
      window.location = '/api/export-view?file=' + encodeURIComponent(state.currentView);
    };
    if (state.dirty) saveView().then(go).catch(quiet); // failed save = no export
    else go();
  });
  on('vDeleteBtn', deleteView);
  on('vDuplicateBtn', duplicateView);
  on('chDeleteBtn', deleteChapter);
  // Kebab popup: toggle on button click, close on outside click.
  on('cbKebabBtn', function (e) {
    var menu = $('cbKebabMenu');
    var btn = $('cbKebabBtn');
    if (!menu) return;
    var open = menu.classList.toggle('cb-open');
    btn.setAttribute('aria-expanded', open ? 'true' : 'false');
    e.stopPropagation();
  });
  document.addEventListener('click', function (e) {
    var menu = $('cbKebabMenu');
    var btn = $('cbKebabBtn');
    if (menu && menu.classList.contains('cb-open')) {
      if (!menu.contains(e.target) && e.target !== btn) {
        menu.classList.remove('cb-open');
        if (btn) btn.setAttribute('aria-expanded', 'false');
      }
    }
  });
  // Header "More" overflow menu: on a narrow window the nav cluster collapses into a dropdown
  // (CSS @media). Toggle open; close on outside click, after choosing an item, or on Escape.
  // Same pattern as the chapter-bar kebab above.
  var closeMore = function () {
    var menu = $('navCluster');
    if (!menu || !menu.classList.contains('hd-open')) return;
    menu.classList.remove('hd-open');
    var btn = $('moreBtn');
    if (btn) btn.setAttribute('aria-expanded', 'false');
  };
  on('moreBtn', function (e) {
    var menu = $('navCluster');
    var btn = $('moreBtn');
    if (!menu) return;
    var open = menu.classList.toggle('hd-open');
    if (btn) btn.setAttribute('aria-expanded', open ? 'true' : 'false');
    e.stopPropagation();
  });
  document.addEventListener('click', function (e) {
    var menu = $('navCluster');
    if (!menu || !menu.classList.contains('hd-open')) return;
    // A click on a nav button inside runs its action AND dismisses the menu; an outside click
    // just dismisses. (The toggle handler above stops its own click from reaching here.)
    if (!menu.contains(e.target) || e.target.closest('.hd-cbtn')) closeMore();
  });
  document.addEventListener('keydown', function (e) {
    if (e.key !== 'Escape') return;
    closeMore();
    var kebab = $('cbKebabMenu');
    if (kebab && kebab.classList.contains('cb-open')) {
      kebab.classList.remove('cb-open');
      var kb = $('cbKebabBtn');
      if (kb) { kb.setAttribute('aria-expanded', 'false'); kb.focus(); }
    }
  });
  on('vAddSlide', addSlide);
  on('vPanelAdd', addSlide);
  on('checkBtn', runCheck);
  on('buildBtn', runBuild);
  on('newChapterBtn', newChapter);
  on('saveAsBtn', saveAs);
  on('saveAsTemplateBtn', saveAsTemplate);
  on('importDocxBtn', importDocx);
  on('convertEmfBtn', convertEmf);
  on('exportPdfBtn', openExportPdf);
  on('wImportBtn', newProjectFromWord);
  function requestQuit() {
    if (!confirmDiscard(requestQuit, arguments)) return;
    fetch('/api/shutdown', { method: 'POST' }).finally(function () {
      document.body.innerHTML =
        '<div style="max-width:520px;margin:80px auto;font:15px/1.6 \'Segoe UI\',sans-serif">' +
        '<h1>Editor stopped</h1><p>Your work is saved in the project folder. ' +
        'You can close this window now. Double-click docwiz.exe to start again.</p></div>';
    });
  }
  on('quitBtn', requestQuit);

  // Native window (custom title bar): the DocWiz app window injects window.chrome.webview.
  // Reveal the min/max/close buttons, message the host, and let the header act as a drag bar.
  // A plain browser tab has none of this and keeps the browser's own window controls.
  (function setupNativeChrome() {
    var wv = window.chrome && window.chrome.webview;
    if (!wv) return;
    var send = function (m) { try { wv.postMessage(m); } catch (e) { /* host gone */ } };
    document.documentElement.classList.add('dw-native');
    on('winMin', function () { send('win:minimize'); });
    on('winMax', function () { send('win:toggle-maximize'); });
    on('winClose', requestQuit); // close == quit, so the unsaved-changes guard still runs
    var header = document.querySelector('header');
    if (header) {
      var isDragSurface = function (t) {
        return !t.closest('button, a, input, select, textarea, [data-nodrag]');
      };
      // Drag the window from the header's empty areas. (No double-click-to-maximize here: a
      // mousedown hands straight to the OS move-loop, which would swallow the second click —
      // the maximize button is the reliable path.)
      header.addEventListener('mousedown', function (e) {
        if (e.button === 0 && isDragSurface(e.target)) send('win:drag');
      });
    }
    wv.addEventListener('message', function (e) {
      if (e.data === 'win-state:maximized') document.documentElement.classList.add('dw-maximized');
      else if (e.data === 'win-state:normal') document.documentElement.classList.remove('dw-maximized');
    });
  })();
  on('issuesClose', function () { $('issuesPanel').hidden = true; });
  document.addEventListener('keydown', function (e) {
    if ((e.ctrlKey || e.metaKey) && e.key === 's') {
      e.preventDefault();
      if (state.currentFile) saveChapter().catch(quiet);
      else if (state.currentYaml) saveYaml().catch(quiet);
      else if (state.currentView) saveView().catch(quiet);
    }
  });
  window.addEventListener('beforeunload', function (e) {
    if (state.dirty || slideZonesDirty() || settingsDirty()) e.preventDefault();
  });

  init();
})();
