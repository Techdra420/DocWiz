/* Client-side search over the manual. The index ships as _dw/search-index.js
   (window.__dwSearch), loaded via a <script> tag so it works from file:// too
   (fetch of a local file is blocked by browsers). */
(function () {
  var index = window.__dwSearch || [];
  var input = document.getElementById('searchInput');
  var results = document.getElementById('searchResults');
  if (!input || !results) return;

  function escapeHtml(s) {
    return s.replace(/[&<>"]/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c];
    });
  }

  function snippet(text, term) {
    var i = text.toLowerCase().indexOf(term);
    if (i < 0) return text.slice(0, 120) + '…';
    var start = Math.max(0, i - 40);
    var piece = (start > 0 ? '…' : '') + text.slice(start, i + term.length + 80) + '…';
    return escapeHtml(piece).replace(
      new RegExp('(' + term.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + ')', 'ig'),
      '<mark>$1</mark>');
  }

  function run(q) {
    q = q.trim().toLowerCase();
    if (q.length < 2) { results.hidden = true; results.innerHTML = ''; return; }
    var terms = q.split(/\s+/);
    var hits = [];
    index.forEach(function (d) {
      var hayTitle = (d.title + ' ' + d.section).toLowerCase();
      var hayAll = hayTitle + ' ' + d.text.toLowerCase();
      var score = 0, ok = true;
      terms.forEach(function (t) {
        if (hayAll.indexOf(t) < 0) { ok = false; return; }
        score += hayTitle.indexOf(t) >= 0 ? 10 : 1;
      });
      if (ok) hits.push({ d: d, score: score });
    });
    hits.sort(function (a, b) { return b.score - a.score; });

    if (hits.length === 0) {
      results.innerHTML = '<div class="sr-empty">No matches.</div>';
    } else {
      var base = window.__dwBase || '';
      var html = hits.slice(0, 25).map(function (h) {
        return '<a class="sr-item" href="' + base + h.d.url + '">' +
          '<span class="sr-title">' + escapeHtml(h.d.title) + '</span>' +
          '<span class="sr-sec">' + escapeHtml(h.d.section) + '</span>' +
          '<span class="sr-snip">' + snippet(h.d.text, terms[0]) + '</span></a>';
      }).join('');
      if (hits.length > 25) {
        html += '<div class="sr-empty">Showing 25 of ' + hits.length + ' matches.</div>';
      }
      results.innerHTML = html;
    }
    results.hidden = false;
  }

  input.addEventListener('input', function () { run(input.value); });
  input.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') { input.value = ''; results.hidden = true; }
  });
  document.addEventListener('click', function (e) {
    if (!document.getElementById('searchBox').contains(e.target)) results.hidden = true;
  });
})();
