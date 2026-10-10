# Visual probe runner: drives DocWizProbe in CONTENT mode (chapter editor or handover page)
# and prints the verdict + any findings. Exit 0 on PASS, exit 1 on FAIL or error.
# ASCII only (PS 5.1 reads BOM-less scripts as ANSI).
#
# Usage (chapter editor surface):
#   powershell -File tools\fidelity\run-probe.ps1 -Project samples\stockroom-demo `
#              -Chapter content/030-operations/020-reports-overview.md
#
# Usage (built handover, first chapter page):
#   powershell -File tools\fidelity\run-probe.ps1 -Project samples\stockroom-demo -Handover
#   powershell -File tools\fidelity\run-probe.ps1 -Project samples\stockroom-demo -Handover `
#              -Page some-chapter-id.html

param(
    [Parameter(Mandatory = $true)]  [string] $Project,
    [string] $Chapter,       # relative chapter path, e.g. content/030-operations/020-reports-overview.md
    [switch] $Handover,      # probe a built handover page instead
    [switch] $SlidesFit,     # print per-slide-fit results (a handover slides page auto-selects this mode)
    [string] $Page,          # handover page file name (default: index.html)
    [int]    $Port = 5344,
    [int]    $VtBudgetMs = 20000,
    [int]    $StartupRetries = 30
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$outDir = Join-Path $PSScriptRoot 'out'
New-Item -ItemType Directory -Force $outDir | Out-Null
$edge = Get-Edge

# ---- Helpers (must appear before the call sites in PS 5.1) ----

function Show-ProbeResult([string] $dom, [string] $surface) {
    # Extract the <pre id="dwProbeOut"> block.
    $m = [regex]::Match($dom, '<pre[^>]*id="dwProbeOut"[^>]*>(.*?)</pre>', 'Singleline')
    if (-not $m.Success) {
        # Check whether the probe title was stamped (alternative success path).
        $titleMatch = [regex]::Match($dom, '<title>(PROBE-[^<]+)</title>')
        if ($titleMatch.Success) {
            Write-Host "Title: $($titleMatch.Groups[1].Value)"
            Write-Host "WARNING: probe output pre not found in DOM but title was stamped; DOM may have been truncated."
        }
        throw "No <pre id=`"dwProbeOut`"> found in $surface DOM dump. The probe did not complete (timeout? JS error? wrong URL?)."
    }

    $json = [System.Net.WebUtility]::HtmlDecode($m.Groups[1].Value)
    $result = ConvertFrom-Json $json

    $verdict = $result.verdict
    $mode    = if ($result.mode) { $result.mode } else { 'slides' }

    Write-Host ""
    Write-Host "=== PROBE RESULT ($surface / $mode mode) ==="
    Write-Host "Verdict : $verdict"

    if ($result.totals) {
        $t = $result.totals
        if ($mode -eq 'content') {
            Write-Host ("Totals  : elements={0}  clipped={1}  overflowing={2}  lowContrast={3}" -f `
                $t.elements, $t.clipped, $t.overflowing, $t.lowContrast)
        } elseif ($mode -eq 'slides-fit') {
            Write-Host ("Totals  : slides={0}  clipped={1}" -f $t.slides, $t.clipped)
        } else {
            Write-Host ("Totals  : slides={0}  zones={1}  overlaps={2}  clipped={3}  lowContrast={4}" -f `
                $t.slides, $t.zones, $t.overlaps, $t.clipped, $t.lowContrast)
        }
    }

    if ($mode -eq 'slides-fit' -and $result.clipped -and $result.clipped.Count -gt 0) {
        Write-Host ""
        Write-Host "CLIPPED SLIDES ($($result.clipped.Count)):"
        foreach ($c in $result.clipped) {
            Write-Host ("  slide {0} ({1}) clipped by {2}px  | {3}" -f $c.index, $c.anchor, $c.by, $c.title)
        }
    }

    if ($mode -ne 'slides-fit' -and $result.clipped -and $result.clipped.Count -gt 0) {
        Write-Host ""
        Write-Host "CLIPPED ($($result.clipped.Count)):"
        foreach ($c in $result.clipped) {
            Write-Host ("  [{0}] .{1} clipped by {2}px  | {3}" -f $c.tag, $c.cls, $c.by, $c.text)
        }
    }

    if ($result.overflowing -and $result.overflowing.Count -gt 0) {
        Write-Host ""
        Write-Host "OVERFLOWING ($($result.overflowing.Count)):"
        foreach ($o in $result.overflowing) {
            Write-Host ("  [{0}] .{1} overflows by {2}px  | {3}" -f $o.tag, $o.cls, $o.by, $o.text)
        }
    }

    if ($result.lowContrast -and $result.lowContrast.Count -gt 0) {
        Write-Host ""
        Write-Host "LOW CONTRAST ($($result.lowContrast.Count)):"
        foreach ($lc in $result.lowContrast) {
            Write-Host ("  ratio={0} (min {1})  fg={2}  bg={3}  {4}px  | '{5}'" -f `
                $lc.ratio, $lc.min, $lc.fg, $lc.bg, $lc.fontPx, $lc.text)
        }
    }

    # Slide mode: print per-slide overlap and clipping details.
    if ($mode -ne 'content' -and $result.slides) {
        foreach ($slide in $result.slides) {
            if ($slide.overlaps -and $slide.overlaps.Count -gt 0) {
                Write-Host ("  Slide {0} ({1}): OVERLAPS {2}" -f $slide.index, $slide.layout, ($slide.overlaps | ConvertTo-Json -Compress))
            }
            if ($slide.regions) {
                foreach ($r in $slide.regions) {
                    if ($r.clipped) {
                        Write-Host ("  Slide {0} region '{1}': CLIPPED by {2}px" -f $slide.index, $r.region, $r.clipBy)
                    }
                }
            }
        }
    }

    Write-Host ""
    Write-Host "When    : $($result.when)"

    if ($verdict -ne 'PASS') {
        Write-Host ""
        Write-Host "PROBE FAIL"
        exit 1
    }

    Write-Host "PROBE PASS"
    exit 0
}

# ---- Validate params ----

if ($Chapter -and $Handover) { throw 'Supply -Chapter OR -Handover, not both.' }
if (-not $Chapter -and -not $Handover) { throw 'Supply -Chapter <relpath> or -Handover.' }

# Resolve the project path to absolute.
$projectAbs = Resolve-Path $Project

# ---- CHAPTER EDITOR mode ----
if ($Chapter) {
    Clear-TestPort $repo $Port
    $server = Start-EditorServer $repo $projectAbs $Port $StartupRetries

    try {
        $domFile = Join-Path $outDir 'probe-dom.html'
        # URL: ?probe=1 activates the probe; #chapter=<path> opens the chapter.
        # The probe auto-runs in CONTENT mode (no .slide elements on the chapter editor).
        $chapterEnc = [uri]::EscapeDataString($Chapter)
        $url = "http://127.0.0.1:$Port/?probe=1#chapter=$chapterEnc"
        Write-Host "Probing chapter editor: $url"
        cmd /c "`"$edge`" --headless=new --disable-gpu --user-data-dir=`"$HeadlessProfile`" --virtual-time-budget=$VtBudgetMs --dump-dom `"$url`" > `"$domFile`" 2>nul"
        Wait-FileReady $domFile 60

        $dom = [IO.File]::ReadAllText($domFile)
        Show-ProbeResult $dom 'chapter'
    }
    finally {
        Stop-EditorServer $server $Port
    }
}

# ---- HANDOVER mode ----
if ($Handover) {
    # Build the handover to a temp directory.
    $tmpOut = Join-Path $env:TEMP 'dw-probe-handover'
    Remove-Item -Recurse -Force $tmpOut -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $tmpOut | Out-Null

    Write-Host "Building handover for $projectAbs -> $tmpOut ..."
    & dotnet run --project (Join-Path $repo 'src\DocWiz.Cli') -c Debug -- build $projectAbs -o $tmpOut
    if ($LASTEXITCODE -ne 0) { throw "docwiz build failed (exit $LASTEXITCODE)" }

    # Determine which page to probe. Generated content pages live under _site/; the
    # entry/home page is the single *.html at the output root.
    if ($Page) {
        $pageFile = Join-Path $tmpOut (Join-Path '_site' $Page)
        if (-not (Test-Path $pageFile)) { $pageFile = Join-Path $tmpOut $Page }
    }
    else {
        $entry = Get-ChildItem $tmpOut -Filter '*.html' | Select-Object -First 1
        if (-not $entry) { throw "No entry html found in handover root: $tmpOut" }
        $pageFile = $entry.FullName
    }

    # Confirm _site/_dw/probe.js exists (catches a missing WriteStaticAssets wire-up).
    $probeAsset = Join-Path $tmpOut '_site\_dw\probe.js'
    if (-not (Test-Path $probeAsset)) {
        throw "_site/_dw/probe.js was NOT written to the handover output. Check HandoverBuilder.WriteStaticAssets."
    }
    Write-Host "_site/_dw/probe.js confirmed in handover output."

    # Open the page as a file:// URL with ?probe=1. The handover uses no fetch() calls
    # for content (file:// constraint), so opening locally is valid.
    $fileUrl = 'file:///' + ($pageFile -replace '\\', '/') + '?probe=1'
    Write-Host "Probing handover page: $pageFile"

    $domFile = Join-Path $outDir 'probe-handover-dom.html'
    cmd /c "`"$edge`" --headless=new --disable-gpu --user-data-dir=`"$HeadlessProfile`" --allow-file-access-from-files --virtual-time-budget=$VtBudgetMs --dump-dom `"$fileUrl`" > `"$domFile`" 2>nul"
    Wait-FileReady $domFile 60

    $dom = [IO.File]::ReadAllText($domFile)
    Show-ProbeResult $dom 'handover'
}
