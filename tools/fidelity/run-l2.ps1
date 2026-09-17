# L2 fidelity runner (live-editor harness): starts the editor on the given project,
# drives /app/editor2/fidelity.html headlessly (touched-block round trip for every
# block of every chapter), and prints the summary. Exit 1 on any SEMANTIC-DIFF,
# unstable, or error finding. ASCII only (PS 5.1 reads BOM-less scripts as ANSI).
#
# Usage: powershell -File tools\fidelity\run-l2.ps1 -Project samples\stockroom-demo [-Port 5341]

param(
    [Parameter(Mandatory = $true)] [string] $Project,
    [int] $Port = 5341,
    [int] $TimeBudgetMs = 120000,
    [int] $StartupRetries = 30
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$outDir = Join-Path $PSScriptRoot 'out'
New-Item -ItemType Directory -Force $outDir | Out-Null

Clear-TestPort $repo $Port
$server = Start-EditorServer $repo $Project $Port $StartupRetries

try {
    $edge = Get-Edge
    $domFile = Join-Path $outDir 'l2-dom.html'
    cmd /c "`"$edge`" --headless=new --disable-gpu --virtual-time-budget=$TimeBudgetMs --dump-dom `"http://127.0.0.1:$Port/app/editor2/fidelity.html`" > `"$domFile`" 2>nul"
    Wait-FileReady $domFile 180

    $dom = [IO.File]::ReadAllText($domFile)
    $m = [regex]::Match($dom, '<pre id="out">(.*?)</pre>', 'Singleline')
    if (-not $m.Success) { throw 'fidelity page produced no results (timeout? JS error?)' }
    $data = ConvertFrom-Json ([System.Net.WebUtility]::HtmlDecode($m.Groups[1].Value))

    $s = $data.summary
    Write-Host ("chapters {0}  blocks {1}  byte-identical {2}  normalized {3}  BROKEN {4}  errors {5}" -f `
        $s.chapters, $s.blocks, $s.identical, $s.normalized, $s.broken, $s.errors)

    $jsonOut = Join-Path $outDir 'l2-findings.json'
    $data.findings | ConvertTo-Json -Depth 6 | Out-File $jsonOut -Encoding utf8
    Write-Host "findings -> $jsonOut"

    foreach ($f in $data.findings) {
        if ($f.kind -in @('SEMANTIC-DIFF', 'unstable', 'serialize-error', 'check-error', 'fatal')) {
            Write-Host ("[{0}] {1} ({2}): {3}" -f $f.kind, $f.file, $f.type, ($f.detail))
            if ($f.src) { Write-Host ("  src: " + ($f.src -replace "`n", '\n')) }
            if ($f.out) { Write-Host ("  out: " + ($f.out -replace "`n", '\n')) }
        }
    }

    if ($s.broken -gt 0 -or $s.errors -gt 0) { exit 1 } else { exit 0 }
}
finally {
    Stop-EditorServer $server $Port
}
