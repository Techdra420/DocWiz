# Pre-merge gate (review I12): the single runner that ties the existing checks
# together. There is no CI - this is the manual step to run before merging a branch.
# Runs, in order, and FAILS the whole run (exit 1) if ANY step fails:
#   1. dotnet build DocWiz.sln              (warnings-as-errors)
#   2. dotnet test tests/DocWiz.Core.Tests
#   3. run-edit-scenario.ps1                (editor scenario gate, samples\stockroom-demo)
#   4. run-probe.ps1 -Chapter ...           (visual probe, chapter editor surface)
#   5. run-probe.ps1 -Handover              (visual probe, built handover page)
# Prints one final PASS/FAIL summary. Idempotent and re-runnable. Needs Edge for
# steps 3-5. ASCII only (PS 5.1 reads BOM-less scripts as ANSI).
#
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools\fidelity\run-all.ps1

$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$project = Join-Path $repo 'samples\stockroom-demo'
$chapter = 'content/010-overview/010-system-overview.md'

# Each step records its own PASS/FAIL; we never bail early, so the summary always
# reports the state of every step even when an earlier one failed.
$results = [ordered]@{}

function Invoke-Step([string] $label, [scriptblock] $body) {
    Write-Host ""
    Write-Host "=== STEP: $label ==="
    $ok = $false
    try {
        & $body
        $ok = ($LASTEXITCODE -eq 0)
    }
    catch {
        Write-Host ("STEP ERROR ({0}): {1}" -f $label, $_.Exception.Message)
        $ok = $false
    }
    $script:results[$label] = $ok
    Write-Host ("--- {0}: {1} ---" -f $label, ($(if ($ok) { 'PASS' } else { 'FAIL' })))
}

# 1. Build (warnings are errors per Directory.Build.props).
Invoke-Step 'build' {
    & dotnet build (Join-Path $repo 'DocWiz.sln')
}

# 2. Unit tests.
Invoke-Step 'test' {
    & dotnet test (Join-Path $repo 'tests\DocWiz.Core.Tests')
}

# 3. Editor scenario gate. run-edit-scenario.ps1 exits 0 on GATE PASS, 1 on FAIL;
#    it takes -SourceProject and -Chapter and uses its own default port (5343, not
#    the protected 5210). Mode 'insertall' is the self-asserting gate that drives the
#    full insert vocabulary through the editor on a scratch copy and ends by running
#    `docwiz check` (must report 0 errors) - the right stable round-trip check for the
#    committed sample fixture (the default 'paragraph' mode assumes an unwrapped
#    single-line paragraph, which this hard-wrapped fixture chapter does not have).
Invoke-Step 'scenario' {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'run-edit-scenario.ps1') `
        -SourceProject $project -Chapter $chapter -Mode insertall
}

# 4. Visual probe - chapter editor surface (default port 5344).
Invoke-Step 'probe(chapter)' {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'run-probe.ps1') `
        -Project $project -Chapter $chapter
}

# 5. Visual probe - built handover page.
Invoke-Step 'probe(handover)' {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'run-probe.ps1') `
        -Project $project -Handover
}

# ---- Final summary ----
Write-Host ""
Write-Host "================ PRE-MERGE GATE SUMMARY ================"
$allPass = $true
$parts = @()
foreach ($k in $results.Keys) {
    $state = if ($results[$k]) { 'PASS' } else { 'FAIL'; }
    if (-not $results[$k]) { $allPass = $false }
    $parts += ("{0}: {1}" -f $k, $state)
}
Write-Host ($parts -join ' / ')
Write-Host "======================================================="

if ($allPass) {
    Write-Host "RESULT: PASS"
    exit 0
}
Write-Host "RESULT: FAIL"
exit 1
