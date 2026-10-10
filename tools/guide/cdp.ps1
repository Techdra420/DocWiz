# Minimal Chrome DevTools Protocol client for headless Edge (dot-source this file).
# Used by capture-screenshots.ps1 to drive the editor into a state (open a menu, a
# dialog) before taking a screenshot - things a plain --screenshot run cannot do.
# ASCII only - PS 5.1 reads BOM-less scripts as ANSI.

$script:CdpSocket = $null
$script:CdpNextId = 0
$script:CdpToken = [Threading.CancellationToken]::None

# Starts headless Edge with a debugging port on its own profile and connects to its page.
# Returns the Edge process.
function Start-CdpEdge([string] $edge, [string] $profileDir, [int] $debugPort) {
    $proc = Start-Process -FilePath $edge -PassThru -ArgumentList @(
        '--headless=new', '--disable-gpu', '--hide-scrollbars', '--no-first-run',
        "--user-data-dir=$profileDir", "--remote-debugging-port=$debugPort",
        '--allow-file-access-from-files', 'about:blank')
    $page = $null
    for ($i = 0; $i -lt 30 -and -not $page; $i++) {
        Start-Sleep -Milliseconds 500
        try {
            # Assign first: PS 5.1 passes a JSON array down the pipeline as ONE object.
            $targets = Invoke-RestMethod "http://127.0.0.1:$debugPort/json/list" -TimeoutSec 2
            $page = $targets | Where-Object { $_.type -eq 'page' } | Select-Object -First 1
        } catch { }
    }
    if (-not $page) { throw "headless Edge did not open a debugging page on port $debugPort" }
    $script:CdpSocket = New-Object System.Net.WebSockets.ClientWebSocket
    $script:CdpSocket.ConnectAsync([Uri] $page.webSocketDebuggerUrl, $script:CdpToken).Wait()
    return $proc
}

function Receive-CdpMessage {
    $buffer = New-Object byte[] 262144
    $stream = New-Object IO.MemoryStream
    do {
        $segment = New-Object 'System.ArraySegment[byte]' -ArgumentList @(, $buffer)
        $result = $script:CdpSocket.ReceiveAsync($segment, $script:CdpToken).Result
        $stream.Write($buffer, 0, $result.Count)
    } while (-not $result.EndOfMessage)
    return [Text.Encoding]::UTF8.GetString($stream.ToArray())
}

# Sends one command and returns the raw JSON text of its response (events are skipped).
# Raw text, not ConvertFrom-Json: PS 5.1's parser rejects strings over 2 MB (screenshots).
function Invoke-CdpRaw([string] $method, [hashtable] $params = @{}) {
    $script:CdpNextId++
    $id = $script:CdpNextId
    $json = @{ id = $id; method = $method; params = $params } | ConvertTo-Json -Depth 10 -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($json)
    $segment = New-Object 'System.ArraySegment[byte]' -ArgumentList @(, $bytes)
    $script:CdpSocket.SendAsync($segment, [Net.WebSockets.WebSocketMessageType]::Text, $true, $script:CdpToken).Wait()
    while ($true) {
        $text = Receive-CdpMessage
        if ($text -match ('^\{"id":' + $id + '[,}]')) {
            if ($text -match '"error":\{') { throw "CDP $method failed: $text" }
            return $text
        }
    }
}

# Evaluates a JS expression in the page (promises awaited) and returns its value.
function Invoke-CdpEval([string] $expression) {
    $text = Invoke-CdpRaw 'Runtime.evaluate' @{ expression = $expression; awaitPromise = $true; returnByValue = $true }
    $response = $text | ConvertFrom-Json
    if ($response.result.exceptionDetails) {
        throw "page script failed: $($response.result.exceptionDetails.exception.description)"
    }
    return $response.result.result.value
}

# Polls a JS expression until it is truthy.
function Wait-CdpCondition([string] $expression, [int] $timeoutMs = 20000) {
    $waited = 0
    while ($waited -lt $timeoutMs) {
        if (Invoke-CdpEval "!!($expression)") { return }
        Start-Sleep -Milliseconds 250
        $waited += 250
    }
    throw "timed out waiting for: $expression"
}

# A real mouse click at viewport coordinates (CSS pixels) - ProseMirror and the editor's
# pills react to genuine pointer events, not to element.click().
function Invoke-CdpClick([double] $x, [double] $y) {
    $null = Invoke-CdpRaw 'Input.dispatchMouseEvent' @{ type = 'mouseMoved'; x = $x; y = $y }
    foreach ($type in 'mousePressed', 'mouseReleased') {
        $null = Invoke-CdpRaw 'Input.dispatchMouseEvent' @{ type = $type; x = $x; y = $y; button = 'left'; clickCount = 1 }
    }
}

function Send-CdpText([string] $text) {
    $null = Invoke-CdpRaw 'Input.insertText' @{ text = $text }
}

# Presses one named key (Escape, Enter, Tab, End).
function Send-CdpKey([string] $key) {
    $codes = @{ Escape = 27; Enter = 13; Tab = 9; End = 35 }
    foreach ($type in 'rawKeyDown', 'keyUp') {
        $null = Invoke-CdpRaw 'Input.dispatchKeyEvent' @{
            type = $type; key = $key; code = $key; windowsVirtualKeyCode = $codes[$key] }
    }
}

function Set-CdpViewport([int] $width, [int] $height, [double] $scale) {
    $null = Invoke-CdpRaw 'Emulation.setDeviceMetricsOverride' @{
        width = $width; height = $height; deviceScaleFactor = $scale; mobile = $false }
}

function Set-CdpColorScheme([string] $scheme) {
    $null = Invoke-CdpRaw 'Emulation.setEmulatedMedia' @{
        features = @(@{ name = 'prefers-color-scheme'; value = $scheme }) }
}

# Saves a PNG of the viewport, or of the CSS-pixel rectangle $clip (x, y, width, height).
function Save-CdpScreenshot([string] $path, $clip = $null) {
    $params = @{ format = 'png' }
    if ($clip) { $params.clip = @{ x = $clip.x; y = $clip.y; width = $clip.width; height = $clip.height; scale = 1 } }
    $text = Invoke-CdpRaw 'Page.captureScreenshot' $params
    $m = [regex]::Match($text, '"data":"([^"]+)"')
    if (-not $m.Success) { throw "no screenshot data in the response" }
    [IO.File]::WriteAllBytes($path, [Convert]::FromBase64String($m.Groups[1].Value))
}

function Stop-CdpEdge($proc) {
    try { $script:CdpSocket.Dispose() } catch { }
    if ($proc) { cmd /c "taskkill /PID $($proc.Id) /T /F >nul 2>nul" }
}
