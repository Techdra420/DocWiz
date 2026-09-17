# M2 gate: end-to-end edit through the live editor on a SCRATCH COPY of a project.
# Proves the byte-surgical save: exactly the touched line differs, frontmatter aside
# (W1). ASCII only (PS 5.1 reads BOM-less scripts as ANSI).
#
# Usage: powershell -File tools\fidelity\run-edit-scenario.ps1 `
#          -SourceProject samples\stockroom-demo -Chapter content/040-troubleshooting/010-troubleshooting.md

param(
    [Parameter(Mandatory = $true)] [string] $SourceProject,
    [Parameter(Mandatory = $true)] [string] $Chapter,
    [string] $Mode = 'paragraph',   # paragraph | cell (first table, -Row/-Col) | gate modes
    [int] $Row = 0,
    [int] $Col = 1,
    [int] $Port = 5343,
    [int] $VtBudgetMs = 60000,   # headless virtual-time budget; big-table sweeps need more
    [int] $StartupRetries = 30
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')

# Scratch copy: manifest + content + assets. The real project is never touched.
$scratch = Join-Path $env:TEMP 'dw-edit-scenario'
Remove-Item -Recurse -Force $scratch -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $scratch | Out-Null
Copy-Item (Join-Path $SourceProject 'docwiz.yaml') $scratch
# ALL content sections, not just the chapter's own: cross-section dw: links must
# resolve or docwiz check fails the insertall gate on a harness artifact.
Copy-Item (Join-Path $SourceProject 'content') (Join-Path $scratch 'content') -Recurse
# Assets too: an EXISTING asset renders as a real <img> while a missing one becomes
# the placeholder span - two different editor code paths. Without the assets every
# image degraded to a placeholder and the gates were blind to locks that only real
# images cause (the figure-in-cell lock the stakeholder hit).
$assetsSrc = Join-Path $SourceProject 'assets'
if (Test-Path $assetsSrc) { Copy-Item $assetsSrc (Join-Path $scratch 'assets') -Recurse }

$chapterPath = Join-Path $scratch ($Chapter -replace '/', '\')
$before = [IO.File]::ReadAllText($chapterPath)

# datarescue gate: seed an inline base64 data: image so the save-time server rescue
# (DataImageRescue) has something to decode - no fixture project carries a base64 blob.
# A valid 1x1 transparent PNG; the gate page saves the chapter and asserts the blob became
# an assets/pasted/ link.
if ($Mode -eq 'datarescue') {
    $seedPng = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M8AAAMBAQDX/ueWAAAAAElFTkSuQmCC'
    [IO.File]::AppendAllText($chapterPath, "`n![seeded](data:image/png;base64,$seedPng)`n")
}

Clear-TestPort $repo $Port
$server = Start-EditorServer $repo $scratch $Port $StartupRetries

try {
    $outDir = Join-Path $PSScriptRoot 'out'
    New-Item -ItemType Directory -Force $outDir | Out-Null
    $domFile = Join-Path $outDir 'scenario-dom.html'
    $url = "http://127.0.0.1:$Port/app/editor2/scenario.html?file=" + [uri]::EscapeDataString($Chapter) +
        "&mode=$Mode&row=$Row&col=$Col"
    $edge = Get-Edge
    cmd /c "`"$edge`" --headless=new --disable-gpu --virtual-time-budget=$VtBudgetMs --dump-dom `"$url`" > `"$domFile`" 2>nul"
    Wait-FileReady $domFile

    $dom = [IO.File]::ReadAllText($domFile)
    $m = [regex]::Match($dom, '<pre id="out">(.*?)</pre>', 'Singleline')
    if (-not $m.Success) { throw 'scenario page produced no result' }
    $result = ConvertFrom-Json ([System.Net.WebUtility]::HtmlDecode($m.Groups[1].Value))

    if ($Mode -eq 'insertall') {
        Write-Host ("scenario result: " + (ConvertTo-Json $result -Compress -Depth 6))
        if (-not $result.ok) { Write-Host 'GATE FAIL: insertall (scenario error)'; exit 1 }
        $after = [IO.File]::ReadAllText($chapterPath)
        $must = @('::: note', '::: warning', '::: steps', '::: details More information',
                  '::: center', '::: audience', '![Gate figure](../../assets/inserted-by-gate.png)',
                  '](dw:', 'Gate attachment](../../attachments/gate.pdf)')
        $missing = @($must | Where-Object { -not $after.Contains($_) })
        foreach ($m2 in $missing) { Write-Host "MISSING in saved file: $m2" }
        Write-Host ("file: " + $chapterPath + "  length: " + $after.Length)
        $idx = $after.IndexOf('::: note')
        if ($idx -ge 0) { Write-Host ("inserted region: " + ($after.Substring([Math]::Max(0,$idx-80), [Math]::Min(700, $after.Length-[Math]::Max(0,$idx-80))) -replace "`n", '\n')) }
        # The validator must accept everything we inserted (missing asset/attachment
        # files are warnings by design, never errors).
        $check = & dotnet run --project (Join-Path $repo 'src\DocWiz.Cli') -c Debug --no-build -- check $scratch 2>&1
        $checkLine = ($check | Select-Object -Last 1)
        Write-Host "check: $checkLine"
        if ($missing.Count -eq 0 -and $checkLine -match ' 0 error') {
            Write-Host 'GATE PASS: insertall'; exit 0
        }
        Write-Host 'GATE FAIL: insertall'; exit 1
    }

    if ($Mode -eq 'typecell') {
        if (-not $result.ok) { throw "scenario failed: $($result.error) $(ConvertTo-Json $result.checks -Compress)" }
        $after = [IO.File]::ReadAllText($chapterPath)
        if ($after.Contains('GATETYPED')) { Write-Host 'GATE PASS: typecell (typed text reached the file)'; exit 0 }
        Write-Host 'GATE FAIL: typecell - typed text not in the saved file'
        exit 1
    }

    if ($Mode -in @('clickcells', 'cellopen', 'aligncycle', 'autofmt', 'undo', 'dragwidth2', 'toolbarcell', 'cellimgtools', 'cellresize', 'escexit', 'cellpasteimg', 'dataguard', 'datarescue', 'cellsaveflush', 'sourceflush', 'celltab', 'cellbreak', 'cellwrap', 'tablecaption', 'tablecaptionquote', 'figcaptiontoggle', 'cellimgalt', 'figxs', 'figpx', 'figalignleft', 'addpara', 'calloutexit', 'calloutimg', 'tablefocus', 'cellwsclean', 'cellpasteinline', 'capabilityparity', 'tablemenu', 'figmenu', 'tasklist', 'listindent')) {
        # Self-asserting gates: the page's own result.ok is the verdict. Most do no save;
        # datarescue saves in-page (its scratch chapter was seeded with a data: URI above)
        # and asserts the saved file no longer carries the base64 blob.
        Write-Host ("checks: " + ($result.checks | ConvertTo-Json -Compress))
        if ($result.detail) { Write-Host ("detail: " + $result.detail) }
        if ($result.error) { Write-Host ("error: " + $result.error) }
        if ($result.ok) { Write-Host "GATE PASS: $Mode"; exit 0 }
        Write-Host "GATE FAIL: $Mode"
        exit 1
    }

    if (-not $result.ok) { throw "scenario failed: $($result.error)" }

    $after = [IO.File]::ReadAllText($chapterPath)
    if ($after -eq $before) { throw 'file did not change - the save did nothing' }

    # Body comparison: count changed lines (frontmatter restyle is W1; compare bodies).
    function Get-Body([string] $text) {
        $t = $text.Replace("`r`n", "`n")
        if ($t.StartsWith("---`n")) {
            $end = $t.IndexOf("`n---", 4)
            if ($end -ge 0) {
                $bs = $t.IndexOf("`n", $end + 1)
                if ($bs -ge 0) { return $t.Substring($bs + 1).TrimStart("`n") }
            }
        }
        return $t
    }
    $bodyBefore = (Get-Body $before) -split "`n"
    $bodyAfter = (Get-Body $after) -split "`n"
    $diff = Compare-Object -ReferenceObject $bodyBefore -DifferenceObject $bodyAfter
    $removed = @($diff | Where-Object SideIndicator -eq '<=')
    $added = @($diff | Where-Object SideIndicator -eq '=>')

    Write-Host ("body lines: {0} -> {1}; changed: -{2} / +{3}" -f $bodyBefore.Count, $bodyAfter.Count, $removed.Count, $added.Count)
    foreach ($r in $removed) { Write-Host ("  - " + $r.InputObject) }
    foreach ($a in $added) { Write-Host ("  + " + $a.InputObject) }

    if ($removed.Count -le 1 -and $added.Count -le 1 -and ($added | Where-Object { $_.InputObject -like '*scenario edit*' })) {
        Write-Host 'GATE PASS: single-line byte-surgical edit confirmed.'
        exit 0
    }
    Write-Host 'GATE FAIL: more than the touched line changed.'
    exit 1
}
finally {
    Stop-EditorServer $server $Port
}
