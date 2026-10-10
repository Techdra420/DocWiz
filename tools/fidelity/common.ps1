# Shared plumbing of the fidelity runners (dot-source this file). ASCII only -
# PS 5.1 reads BOM-less scripts as ANSI.

# The stakeholder's live editor runs on this port. NOTHING in the harness may ever
# kill a listener on it - every sweep below checks first.
$ProtectedPort = 5210

# Headless runs use their own browser profile. Without one, headless Edge shares the
# user's default profile and can stall indefinitely while their own Edge is open.
$HeadlessProfile = Join-Path $env:TEMP 'dw-fidelity-edge'

function Get-Edge {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
        "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    throw 'msedge.exe not found'
}

# Headless Edge's launcher can return while a child process still writes the dump;
# wait until the file is unlocked and its size is stable.
function Wait-FileReady([string] $path, [int] $timeoutSec = 120) {
    $lastSize = -1
    for ($i = 0; $i -lt $timeoutSec; $i++) {
        Start-Sleep -Seconds 1
        if (-not (Test-Path $path)) { continue }
        $size = (Get-Item $path).Length
        if ($size -gt 0 -and $size -eq $lastSize) {
            try { $fs = [IO.File]::Open($path, 'Open', 'Read', 'None'); $fs.Close(); return }
            catch [System.IO.IOException] { }
        }
        $lastSize = $size
    }
    throw "timed out waiting for $path"
}

# Pre-start sweep: a leftover listener on the test port would serve stale code or
# block startup entirely; any repo-bin docwiz zombie locks the build outputs for
# EVERY later run. Refuses to touch $ProtectedPort.
function Clear-TestPort([string] $repo, [int] $Port) {
    if ($Port -ne $ProtectedPort) {
        $stale = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
        if ($stale) { Stop-Process -Id $stale.OwningProcess -Force -ErrorAction SilentlyContinue; Start-Sleep -Seconds 1 }
    }
    foreach ($z in (Get-Process docwiz -ErrorAction SilentlyContinue)) {
        if ($z.Path -like (Join-Path $repo "src\DocWiz.Cli\bin\*")) {
            $zp = Get-NetTCPConnection -OwningProcess $z.Id -LocalPort $ProtectedPort -State Listen -ErrorAction SilentlyContinue
            if (-not $zp) { Stop-Process -Id $z.Id -Force -ErrorAction SilentlyContinue }
        }
    }
    Start-Sleep -Seconds 1
}

# Starts `docwiz edit` via dotnet run and waits for the API to answer. dotnet run
# builds first - retries, not a fixed sleep. Returns the wrapper process.
function Start-EditorServer([string] $repo, [string] $Project, [int] $Port, [int] $StartupRetries = 30) {
    $server = Start-Process -FilePath dotnet -ArgumentList @(
        'run', '--project', (Join-Path $repo 'src\DocWiz.Cli'), '-c', 'Debug', '--',
        'edit', $Project, '--port', "$Port", '--no-browser'
    ) -PassThru -WindowStyle Hidden
    $up = $false
    for ($i = 0; $i -lt $StartupRetries -and -not $up; $i++) {
        Start-Sleep -Seconds 2
        try { $null = Invoke-WebRequest "http://127.0.0.1:$Port/api/project" -UseBasicParsing -TimeoutSec 5; $up = $true }
        catch { }
    }
    if (-not $up) {
        cmd /c "taskkill /PID $($server.Id) /T /F >nul 2>nul"
        throw "server did not come up on port $Port"
    }
    return $server
}

# Cleanup for the finally block: dotnet run spawns the app as a CHILD - killing the
# wrapper leaves a zombie holding the port (and serving stale code). /T kills the
# whole dotnet-run tree, incl. a child that started listening after this cleanup
# began (the orphan class that locked bin/ outputs). Then kill the PORT OWNER too -
# but never anything on $ProtectedPort, the stakeholder's real editor.
function Stop-EditorServer($server, [int] $Port) {
    cmd /c "taskkill /PID $($server.Id) /T /F >nul 2>nul"
    if ($Port -ne $ProtectedPort) {
        $own = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
        if ($own) { Stop-Process -Id $own.OwningProcess -Force -ErrorAction SilentlyContinue }
    }
}
