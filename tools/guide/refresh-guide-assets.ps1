# Regenerates the generated files of the DocWiz Guide template (templates/projects/docwiz-guide):
# the screenshots in assets/screens, captured from the real editor and handover driven
# headlessly over CDP, and attachments/quick-reference.pdf, made by DocWiz's own PDF export.
# Run it after a UI change, then look at every changed picture before committing.
# ASCII only - PS 5.1 reads BOM-less scripts as ANSI.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\guide\refresh-guide-assets.ps1
#   powershell ... -File tools\guide\refresh-guide-assets.ps1 -Only welcome,history   (screenshots only)
#
# It works on a throw-away copy of the guide in -WorkDir (a neutral path, because the
# project folder shows up in some screenshots), never on the template itself, and removes
# its own entries from the recent-projects list afterwards. The welcome screen's recent
# list is replaced by example entries so no real paths are captured.

param(
    [string]   $WorkDir = 'C:\tmp\DocWiz Guide',
    [string[]] $Only,
    [int]      $Port = 5346,
    [int]      $WelcomePort = 5347,
    [int]      $DebugPort = 9333
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\fidelity\common.ps1')
. (Join-Path $PSScriptRoot 'cdp.ps1')
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$guide = Join-Path $repo 'templates\projects\docwiz-guide'
$outDir = Join-Path $guide 'assets\screens'
$cli = Join-Path $repo 'src\DocWiz.Cli'

# ---- Page helpers, injected into every page before its own scripts ----
$helpers = @'
window.__s = (function () {
  function vis(e) { return !!e && e.getClientRects().length > 0; }
  function norm(t) { return (t || '').replace(/\s+/g, ' ').trim(); }
  function all(sel) { return Array.prototype.slice.call(document.querySelectorAll(sel)); }
  function byText(sel, text) {
    return all(sel).find(function (e) { return vis(e) && norm(e.textContent).indexOf(text) === 0; });
  }
  function btn(label) {
    return all('button').find(function (b) {
      return vis(b) && (b.getAttribute('aria-label') === label || norm(b.textContent) === label);
    });
  }
  function menuItem(text) {
    return byText('[role=menu] [role^=menuitem], [role=menu] button, [role=menu] li', text);
  }
  function box(x) { return x.getBoundingClientRect ? x.getBoundingClientRect() : x; }
  function union() {
    var rs = Array.prototype.slice.call(arguments).filter(Boolean).map(box);
    var l = Math.min.apply(null, rs.map(function (r) { return r.left; }));
    var t = Math.min.apply(null, rs.map(function (r) { return r.top; }));
    var r = Math.max.apply(null, rs.map(function (r) { return r.right; }));
    var b = Math.max.apply(null, rs.map(function (r) { return r.bottom; }));
    return { left: l, top: t, right: r, bottom: b };
  }
  // A clip rectangle for Page.captureScreenshot: padded, kept inside the viewport.
  function clip(r, pad) {
    pad = pad || 0;
    var l = Math.max(0, r.left - pad), t = Math.max(0, r.top - pad);
    var rr = Math.min(innerWidth, r.right + pad), b = Math.min(innerHeight, r.bottom + pad);
    return { x: l + scrollX, y: t + scrollY, width: rr - l, height: b - t };
  }
  function center(e) {
    var r = e.getBoundingClientRect();
    return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
  }
  // A numbered annotation marker, only ever drawn for a screenshot.
  function mark(n, x, y) {
    var d = document.createElement('div');
    d.textContent = n;
    d.style.cssText = 'position:fixed;z-index:2147483647;width:28px;height:28px;border-radius:50%;' +
      'background:#d9480f;color:#fff;font:700 15px/28px "Segoe UI",sans-serif;text-align:center;' +
      'box-shadow:0 0 0 3px #fff,0 2px 6px rgba(0,0,0,.35);left:' + (x - 14) + 'px;top:' + (y - 14) + 'px';
    document.body.appendChild(d);
  }
  // The editor's page scrolls underneath its sticky bars: scroll `e` to sit just below them.
  function reveal(e, gap) {
    var sc = e.parentElement;
    while (sc && !(/(auto|scroll)/.test(getComputedStyle(sc).overflowY) && sc.scrollHeight > sc.clientHeight)) sc = sc.parentElement;
    sc = sc || document.scrollingElement;
    // The toolbar and its context bar, or the Preview as banner while previewing.
    var top = Math.max.apply(null, [0].concat(all('.e2-maintoolbar, .e2-contextbar, #previewBanner').filter(vis)
      .map(function (b) { return b.getBoundingClientRect().bottom; })));
    sc.scrollTop += e.getBoundingClientRect().top - top - (gap == null ? 20 : gap);
  }
  return { vis: vis, all: all, byText: byText, btn: btn, menuItem: menuItem, union: union,
           clip: clip, center: center, mark: mark, reveal: reveal };
})();

// Headless Edge has no dictionary, so every word would get a spelling squiggle.
document.addEventListener('DOMContentLoaded', function () {
  var st = document.createElement('style');
  st.textContent = '::spelling-error, ::grammar-error { text-decoration: none !important; }';
  document.head.appendChild(st);
  document.documentElement.spellcheck = false;
});

// The welcome screen lists recent projects from this computer: show example entries instead.
(function () {
  var realFetch = window.fetch;
  window.fetch = function (url, opts) {
    if (String(url).indexOf('/api/welcome') < 0) return realFetch.apply(this, arguments);
    return realFetch.apply(this, arguments).then(function (r) { return r.json(); }).then(function (w) {
      var d = new Date(), p = function (n) { return (n < 10 ? '0' : '') + n; };
      var today = d.getFullYear() + '-' + p(d.getMonth() + 1) + '-' + p(d.getDate());
      w.recent = [
        { path: 'D:\\Docs\\DocWiz Guide', project: 'DocWiz Guide', code: 'DW-GUIDE', lastOpened: today + ' 09:12', exists: true, pinned: true },
        { path: 'D:\\Docs\\Coffee machine', project: 'CM-200 Coffee Machine', code: 'CM-200', lastOpened: today + ' 08:47', exists: true, pinned: false },
        { path: 'D:\\Docs\\Stockroom', project: 'Stockroom Inventory System', code: 'SR-DEMO-001', lastOpened: '2026-09-17 15:30', exists: true, pinned: false }
      ];
      return new Response(JSON.stringify(w), { headers: { 'Content-Type': 'application/json' } });
    });
  };
})();
'@

# ---- The shots ----
function ChapterUrl([string] $rel) { "http://127.0.0.1:$Port/#chapter=" + [uri]::EscapeDataString($rel) }
$editorReady = "document.querySelector('.ProseMirror') && document.querySelector('.e2-maintoolbar')"
$handoverPage = 'file:///' + (($WorkDir -replace '\\', '/') -replace ' ', '%20') + '/output/handover/_site/building-blocks.html'
$modalClip = "__s.clip(__s.union(document.getElementById('modal')), 24)"
# The editing column (right of the sidebar), from the toolbar down to element `el`.
function ColumnClip([string] $el) {
    "(function () { var m = document.getElementById('main').getBoundingClientRect();" +
    " var t = document.querySelector('.e2-maintoolbar').getBoundingClientRect();" +
    " var e = ($el).getBoundingClientRect();" +
    " return __s.clip({ left: m.left, right: m.right, top: t.top, bottom: e.bottom + 16 }, 0); })()"
}

$shots = @(
    @{ name = 'welcome'; url = "http://127.0.0.1:$WelcomePort/"; height = 1000
       ready = "document.querySelector('#wTemplates') && document.querySelector('#wTemplates').children.length && document.querySelector('#wRecent').children.length"
       steps = @(@{ wait = 600 }) },

    @{ name = 'editor-overview'; url = (ChapterUrl 'content/020-Writing chapters/040-Building blocks.md'); ready = $editorReady
       steps = @(
           # A script click: a real one makes Edge pop up its text-selection mini menu.
           @{ js = "document.querySelector('.dw-callout.dw-warning .dw-callout-tag').click()" },
           @{ wait = 700 },
           @{ js = "__s.reveal(__s.byText('.ProseMirror h2', 'Callouts'))" },
           @{ wait = 300 },
           @{ js = @'
(function () {
  function r(sel) { return document.querySelector(sel).getBoundingClientRect(); }
  var head = r('header'), side = r('#sidebar'), pill = r('#cbReaders');
  var h2 = __s.byText('.ProseMirror h2', 'Callouts').getBoundingClientRect();
  var readers = __s.btn('Readers').getBoundingClientRect();
  var unwrap = __s.btn('Unwrap').getBoundingClientRect();
  var label = document.querySelector('input[placeholder^="Warning"]').getBoundingClientRect();
  __s.mark(1, r('#projBlock').right + 24, head.top + head.height / 2);
  __s.mark(2, side.right - 24, side.top + 112);
  __s.mark(3, pill.right + 28, pill.top + pill.height / 2);
  __s.mark(4, readers.right + 26, readers.top + readers.height / 2);
  __s.mark(5, (side.right + h2.left) / 2, innerHeight * 0.66);
  __s.mark(6, (label.right + unwrap.left) / 2, unwrap.top + unwrap.height / 2);
})()
'@ }) },

    @{ name = 'picture-context'; url = (ChapterUrl 'content/020-Writing chapters/020-Pictures.md'); ready = $editorReady
       steps = @(
           @{ js = "document.querySelector('.ProseMirror img[src*=""docwiz-logo""]').scrollIntoView({ block: 'center' })" },
           @{ wait = 400 },
           @{ click = "document.querySelector('.ProseMirror img[src*=""docwiz-logo""]')" },
           @{ wait = 700 },
           @{ js = "__s.reveal(__s.byText('.ProseMirror h2', 'Captions and figure numbers'))" },
           @{ wait = 300 })
       clip = (ColumnClip "document.querySelector('.ProseMirror img[src*=""docwiz-logo""]')") },

    @{ name = 'table-context'; url = (ChapterUrl 'content/020-Writing chapters/030-Tables.md'); ready = $editorReady
       steps = @(
           @{ js = "__s.byText('.ProseMirror td', 'Amber').scrollIntoView({ block: 'center' })" },
           @{ wait = 400 },
           @{ click = "__s.byText('.ProseMirror td', 'Amber')" },
           @{ wait = 700 },
           @{ js = "__s.reveal(__s.byText('.ProseMirror h2', 'Column widths and table width'))" },
           @{ wait = 300 })
       clip = (ColumnClip "(function (t) { return t.closest('figure') || t.parentElement; })(__s.byText('.ProseMirror td', 'Red').closest('table'))") },

    @{ name = 'building-blocks-dialog'; url = (ChapterUrl 'content/020-Writing chapters/040-Building blocks.md'); ready = $editorReady
       steps = @(
           @{ click = "document.querySelector('.ProseMirror p')" },
           @{ click = "__s.btn('Insert')" }, @{ wait = 300 },
           @{ click = "__s.menuItem('Building block')" }, @{ wait = 600 })
       clip = $modalClip },

    @{ name = 'readers-menu'; url = (ChapterUrl 'content/020-Writing chapters/060-Readers and audiences.md'); ready = $editorReady
       steps = @(
           @{ js = "__s.reveal(__s.byText('.ProseMirror p', 'A tagged block keeps'))" },
           @{ wait = 400 },
           @{ click = "document.querySelector('.ProseMirror .aud[data-aud=""trainer""] p')" },
           @{ click = "__s.btn('Readers')" }, @{ wait = 500 })
       clip = @'
(function () {
  var main = document.getElementById('main').getBoundingClientRect();
  var m = document.querySelector('[role=menu]').getBoundingClientRect();
  var b = __s.btn('Readers').getBoundingClientRect();
  return __s.clip({ left: main.left, right: main.right, top: b.top - 10, bottom: m.bottom + 24 }, 0);
})()
'@ },

    @{ name = 'settings-audiences'; url = "http://127.0.0.1:$Port/#settings"; ready = "document.getElementById('setSheet') && __s.vis(document.getElementById('setSheet'))"
       steps = @(
           @{ click = "document.querySelector('#setRail .set-item[data-sec=""audiences""]')" },
           @{ wait = 700 }) },

    @{ name = 'preview-as'; url = (ChapterUrl 'content/020-Writing chapters/060-Readers and audiences.md'); ready = $editorReady
       steps = @(
           @{ click = "document.getElementById('previewBtn')" }, @{ wait = 300 },
           @{ click = "__s.menuItem('Trainer')" }, @{ wait = 900 },
           @{ js = "__s.reveal(__s.byText('.ProseMirror p', 'A tagged block keeps'))" },
           @{ wait = 500 }) },

    @{ name = 'sidebar-menu'; url = (ChapterUrl 'content/020-Writing chapters/030-Tables.md'); ready = $editorReady
       steps = @(
           @{ click = "document.querySelector('.tree-row.current .tree-act[aria-haspopup]')" },
           @{ wait = 500 })
       clip = @'
(function () {
  var s = document.getElementById('sidebar').getBoundingClientRect();
  var m = document.querySelector('[role=menu]').getBoundingClientRect();
  return __s.clip({ left: 0, top: s.top, right: Math.max(s.right, m.right) + 16, bottom: Math.max(m.bottom + 24, s.top + 420) }, 0);
})()
'@ },

    @{ name = 'new-chapter'; url = (ChapterUrl 'content/020-Writing chapters/030-Tables.md'); ready = $editorReady
       steps = @(
           @{ click = "__s.all('.tree-act').find(function (b) { var l = b.getAttribute('aria-label') || ''; return l.indexOf('Add a chapter to') === 0 && l.indexOf('Organizing the manual') > 0; })" },
           @{ wait = 500 },
           @{ js = @'
(function () {
  var m = document.getElementById('modal');
  var title = m.querySelector('input[placeholder^="e.g. Cleaning"]');
  title.value = 'Cleaning the scanner';
  title.dispatchEvent(new Event('input', { bubbles: true }));
  var start = Array.prototype.slice.call(m.querySelectorAll('select')).find(function (s) {
    return Array.prototype.some.call(s.options, function (o) { return o.text.indexOf('Procedure') === 0; });
  });
  start.value = Array.prototype.find.call(start.options, function (o) { return o.text.indexOf('Procedure') === 0; }).value;
  start.dispatchEvent(new Event('change', { bubbles: true }));
})()
'@ }, @{ wait = 300 })
       clip = $modalClip },

    @{ name = 'history'; url = (ChapterUrl 'content/020-Writing chapters/010-Text, headings and lists.md'); ready = $editorReady
       steps = @(
           @{ click = "(function () { var p = document.querySelector('.ProseMirror p'); var r = p.getBoundingClientRect(); return { getBoundingClientRect: function () { return { left: r.right - 4, top: r.bottom - 12, width: 1, height: 1 }; } }; })()" },
           @{ key = 'End' },
           @{ text = ' This sentence was added to show how History compares versions.' },
           @{ wait = 3000 },
           @{ waitFor = "/Saved/.test(document.getElementById('status').textContent)" },
           @{ click = "document.getElementById('cbHistoryBtn')" }, @{ wait = 900 },
           @{ click = "__s.byText('#histPanel *', 'As it was before history started')" },
           @{ wait = 1200 }) },

    @{ name = 'slide-deck'; url = "http://127.0.0.1:$Port/#view=views/getting-started.yaml"
       ready = "document.querySelectorAll('#vPanelList .vpanel-item[data-i]').length > 4"
       steps = @(
           @{ wait = 800 },
           @{ click = "document.querySelector('#vPanelList .vpanel-item[data-i=""5""]')" },
           @{ wait = 1500 },
           # The canvas shows every slide in a column: put the chosen one at its top.
           @{ js = "__s.all('#vEditSurface .slide')[6].scrollIntoView({ block: 'start' })" },
           @{ wait = 500 }) },

    @{ name = 'export-dialog'; url = (ChapterUrl 'content/020-Writing chapters/030-Tables.md'); ready = $editorReady
       steps = @(
           @{ click = "document.getElementById('projMenuBtn')" }, @{ wait = 300 },
           @{ click = "__s.menuItem('Export PDF or HTML')" }, @{ wait = 800 })
       # The dialog is taller than the window: show its top, down to the first chapters.
       clip = "(function (m) { return __s.clip({ left: m.left, right: m.right, top: m.top, bottom: m.top + 440 }, 24); })(document.getElementById('modal').getBoundingClientRect())" },

    @{ name = 'handover-manual'; url = $handoverPage; ready = "document.readyState === 'complete'"
       steps = @(@{ wait = 600 }) },

    @{ name = 'handover-search'; url = $handoverPage; ready = "document.readyState === 'complete'"
       steps = @(
           @{ click = "document.querySelector('input[placeholder^=""Search""]')" },
           @{ text = 'picture' },
           @{ wait = 800 }) }
)
# With -File, `-Only a,b` arrives as one string: split it.
if ($Only) { $names = $Only -split ','; $shots = @($shots | Where-Object { $names -contains $_.name }) }

# ---- Run ----
function Start-Server([string[]] $editArgs, [int] $port) {
    $server = Start-Process -FilePath dotnet -PassThru -WindowStyle Hidden -ArgumentList (@(
        'run', '--no-build', '--project', $cli, '-c', 'Debug', '--', 'edit') + $editArgs + @('--port', "$port", '--no-browser'))
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Seconds 2
        try { $null = Invoke-WebRequest "http://127.0.0.1:$port/api/version" -UseBasicParsing -TimeoutSec 5; return $server } catch { }
    }
    Stop-EditorServer $server $port
    throw "the editor did not come up on port $port"
}

function Invoke-Step($step) {
    if ($step.wait) { Start-Sleep -Milliseconds $step.wait }
    elseif ($step.js) { $null = Invoke-CdpEval $step.js }
    elseif ($step.waitFor) { Wait-CdpCondition $step.waitFor }
    elseif ($step.text) { Send-CdpText $step.text }
    elseif ($step.key) { Send-CdpKey $step.key }
    elseif ($step.click) {
        $at = Invoke-CdpEval "(function () { var e = ($($step.click)); if (!e) throw new Error('nothing to click: ' + $(ConvertTo-Json $step.click)); if (e.scrollIntoView) e.scrollIntoView({ block: 'nearest' }); return __s.center(e); })()"
        Invoke-CdpClick $at.x $at.y
        Start-Sleep -Milliseconds 150
    }
}

& dotnet build $cli -c Debug --nologo -v q | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }

Remove-Item -Recurse -Force $WorkDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force (Split-Path $WorkDir) | Out-Null
Copy-Item -Recurse $guide $WorkDir
Remove-Item -Recurse -Force (Join-Path $WorkDir '.docwiz'), (Join-Path $WorkDir 'output') -ErrorAction SilentlyContinue
& dotnet run --no-build --project $cli -c Debug -- build $WorkDir | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'building the handover failed' }

# The printable quick reference, exported before the screenshots edit the copy.
if (-not $Only) {
    $pdfDir = Join-Path $env:TEMP 'dw-guide-pdf'
    Remove-Item -Recurse -Force $pdfDir -ErrorAction SilentlyContinue
    & dotnet run --no-build --project $cli -c Debug -- export-pdf $WorkDir -o $pdfDir --all | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'the PDF export failed' }
    Copy-Item (Get-ChildItem $pdfDir -Filter '*-Quick-Reference.pdf').FullName (Join-Path $guide 'attachments\quick-reference.pdf')
}

Clear-TestPort $repo $Port
Clear-TestPort $repo $WelcomePort
$server = $null; $welcome = $null; $edge = $null
$profileDir = Join-Path $env:TEMP 'dw-guide-edge'
Remove-Item -Recurse -Force $profileDir -ErrorAction SilentlyContinue
try {
    # PS 5.1's Start-Process joins arguments without quoting them: quote the path (spaces).
    $server = Start-Server @("`"$WorkDir`"") $Port
    $welcome = Start-Server @() $WelcomePort
    $edge = Start-CdpEdge (Get-Edge) $profileDir $DebugPort
    $null = Invoke-CdpRaw 'Page.enable'
    $null = Invoke-CdpRaw 'Page.addScriptToEvaluateOnNewDocument' @{ source = $helpers }
    Set-CdpColorScheme 'light'

    foreach ($shot in $shots) {
        Write-Host "shot $($shot.name)"
        Set-CdpViewport 1280 $(if ($shot.height) { $shot.height } else { 800 }) 2
        $null = Invoke-CdpRaw 'Page.navigate' @{ url = 'about:blank' }
        Start-Sleep -Milliseconds 300
        $null = Invoke-CdpRaw 'Page.navigate' @{ url = $shot.url }
        Wait-CdpCondition $shot.ready 30000
        Start-Sleep -Milliseconds 800
        foreach ($step in $shot.steps) { Invoke-Step $step }
        $clip = if ($shot.clip) { Invoke-CdpEval $shot.clip } else { $null }
        Save-CdpScreenshot (Join-Path $outDir "$($shot.name).png") $clip
    }
}
finally {
    Stop-CdpEdge $edge
    foreach ($p in @($WorkDir)) {
        try { $null = Invoke-WebRequest -Method Delete -UseBasicParsing "http://127.0.0.1:$Port/api/recent?path=$([uri]::EscapeDataString($p))" } catch { }
    }
    if ($server) { Stop-EditorServer $server $Port }
    if ($welcome) { Stop-EditorServer $welcome $WelcomePort }
}
Write-Host "Screenshots written to $outDir"
