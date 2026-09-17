using System.Diagnostics;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace DocWiz.Build.Pdf;

/// <summary>Renders one print-HTML file to PDF via headless Edge over the DevTools Protocol:
/// launch → wait until <paramref name="readyExpression"/> is true → Page.printToPDF (native
/// Chromium pagination — fast at any scale; Paged.js was dropped 2026-06-29 as it timed out
/// on real manuals). Footers come from printToPDF's footerTemplate; navigation from PDF
/// bookmarks (generateDocumentOutline).</summary>
public sealed class EdgePdfDriver
{
    private readonly string _edgeExe;
    public EdgePdfDriver(string edgeExe) => _edgeExe = edgeExe;

    // Edge discovery is NOT duplicated here: DocWiz.Build cannot reference DocWiz.App, so the
    // msedge.exe path is found once in App's BrowserLauncher.FindEdge() and injected via PdfExporter.

    /// <param name="readyExpression">JS boolean expression polled until true before printing
    /// (e.g. images-loaded for documents, window.__slidesReady for slides).</param>
    /// <param name="footer">true → "Page X of Y" running footer (documents); false → none (slides).</param>
    /// <param name="footerLeft">left-aligned footer text (project name); HTML-escaped here.</param>
    public void PrintToPdf(string htmlPath, string pdfPath, string profileDir, int timeoutMs,
        string readyExpression, bool footer, string footerLeft)
    {
        Directory.CreateDirectory(profileDir);
        var url = new Uri(Path.GetFullPath(htmlPath)).AbsoluteUri;
        var psi = new ProcessStartInfo(_edgeExe) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[]
        {
            "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check",
            $"--user-data-dir={profileDir}", "--remote-debugging-port=0", url,
        }) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Could not start Edge.");
        try
        {
            var wsUrl = GetPageWebSocketUrl(profileDir, timeoutMs);
            RunCdp(wsUrl, pdfPath, timeoutMs, readyExpression, footer, footerLeft).GetAwaiter().GetResult();
        }
        finally
        {
            try { if (!proc.HasExited) proc.Kill(true); } catch { /* best effort */ }
        }
        if (!File.Exists(pdfPath) || new FileInfo(pdfPath).Length < 5)
            throw new InvalidOperationException("Edge produced no PDF.");
        var head = new byte[5];
        using (var fs = File.OpenRead(pdfPath)) { fs.ReadExactly(head, 0, 5); }
        if (!(head[0] == 0x25 && head[1] == 0x50 && head[2] == 0x44 && head[3] == 0x46)) // %PDF
            throw new InvalidOperationException("Edge produced a corrupt (non-PDF) file.");
    }

    private static string GetPageWebSocketUrl(string profileDir, int timeoutMs)
    {
        var portFile = Path.Combine(profileDir, "DevToolsActivePort");
        var deadline = Environment.TickCount64 + timeoutMs;
        int port = 0;
        while (Environment.TickCount64 < deadline)
        {
            if (File.Exists(portFile) &&
                File.ReadAllLines(portFile) is { Length: > 0 } lines &&
                int.TryParse(lines[0], out port)) break;
            Thread.Sleep(50);
        }
        if (port == 0) throw new InvalidOperationException("Edge DevTools port not found.");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        while (Environment.TickCount64 < deadline)
        {
            try
            {
                var json = http.GetStringAsync($"http://127.0.0.1:{port}/json/list").GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(json);
                foreach (var t in doc.RootElement.EnumerateArray())
                    if (t.GetProperty("type").GetString() == "page" &&
                        t.TryGetProperty("webSocketDebuggerUrl", out var w))
                        return w.GetString()!;
            }
            catch { /* page not ready yet */ }
            Thread.Sleep(100);
        }
        throw new InvalidOperationException("Edge page target not found.");
    }

    private static async Task RunCdp(string wsUrl, string pdfPath, int timeoutMs,
        string readyExpression, bool footer, string footerLeft)
    {
        using var ws = new ClientWebSocket();
        using var cts = new CancellationTokenSource(timeoutMs);
        await ws.ConnectAsync(new Uri(wsUrl), cts.Token);
        int id = 0;

        async Task<JsonElement> Send(string method, object? prms = null)
        {
            var msgId = ++id;
            var payload = JsonSerializer.Serialize(new { id = msgId, method, @params = prms ?? new { } });
            await ws.SendAsync(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, true, cts.Token);
            var buf = new byte[1 << 16];
            while (true)
            {
                var sb = new StringBuilder();
                WebSocketReceiveResult r;
                do { r = await ws.ReceiveAsync(buf, cts.Token); sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count)); }
                while (!r.EndOfMessage);
                using var d = JsonDocument.Parse(sb.ToString());
                if (d.RootElement.TryGetProperty("id", out var rid) && rid.GetInt32() == msgId)
                {
                    var el = d.RootElement.Clone();
                    if (el.TryGetProperty("error", out var cdpErr))
                        throw new InvalidOperationException($"CDP {method} failed: {cdpErr}");
                    return el;
                }
            }
        }

        await Send("Page.enable");

        var deadline = Environment.TickCount64 + timeoutMs;
        while (true)
        {
            var res = await Send("Runtime.evaluate", new { expression = readyExpression, returnByValue = true });
            if (res.TryGetProperty("result", out var rr) && rr.TryGetProperty("result", out var v) &&
                v.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.True)
                break;
            if (Environment.TickCount64 > deadline)
                throw new InvalidOperationException("Page did not become ready before timeout.");
            await Task.Delay(150, cts.Token);
        }

        var prms = new Dictionary<string, object>
        {
            ["printBackground"] = true,
            ["preferCSSPageSize"] = true,
            ["generateDocumentOutline"] = true, // PDF bookmarks from the heading structure
        };
        if (footer)
        {
            prms["displayHeaderFooter"] = true;
            prms["headerTemplate"] = "<div></div>";
            prms["footerTemplate"] =
                "<div style=\"font-size:9px;width:100%;margin:0 16mm;color:#666;" +
                "display:flex;justify-content:space-between\"><span>" + Esc(footerLeft) +
                "</span><span>Page <span class=\"pageNumber\"></span> of " +
                "<span class=\"totalPages\"></span></span></div>";
        }

        var pdf = await Send("Page.printToPDF", prms);
        var data = pdf.GetProperty("result").GetProperty("data").GetString()!;
        await File.WriteAllBytesAsync(pdfPath, Convert.FromBase64String(data), cts.Token);

        try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None); } catch { }
    }

    private static string Esc(string s) => System.Net.WebUtility.HtmlEncode(s);
}
