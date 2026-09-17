using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace DocWiz.Core.Tests;

public class OfflineHandoverTests
{
    [Fact]
    public void ShippedSiteHasNoNetworkCalls()
    {
        var dir = TestRepo.At("templates", "site");
        foreach (var f in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                     .Where(f => f.EndsWith(".js") || f.EndsWith(".html") || f.EndsWith(".css")))
        {
            var t = File.ReadAllText(f);
            Assert.False(Regex.IsMatch(t, @"\bfetch\s*\(|\bXMLHttpRequest\b"), $"network call in {f}");
            // The offline constraint forbids CDN/internet dependencies. Excluded:
            // the SVG namespace literal (not a fetch), and loopback URLs that only
            // appear in probe.js header-comment examples for running the dev server
            // headlessly (127.0.0.1/localhost are not an internet dependency).
            var ext = Regex.Matches(t, @"https?://[^\s""')]+").Select(m => m.Value)
                .Where(u => !u.StartsWith("http://www.w3.org/") && !u.StartsWith("https://www.w3.org/"))
                .Where(u => !u.StartsWith("http://127.0.0.1") && !u.StartsWith("http://localhost")).ToArray();
            Assert.True(ext.Length == 0, $"external URL(s) in {f}: {string.Join(", ", ext)}");
        }
    }
}
