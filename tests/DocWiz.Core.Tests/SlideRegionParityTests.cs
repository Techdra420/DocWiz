using System.Linq;
using System.Text.RegularExpressions;
using DocWiz.Core.Models;
using Xunit;

namespace DocWiz.Core.Tests;

public class SlideRegionParityTests
{
    [Fact]
    public void AppJsSlideRegionsMatchSlideLayout()
    {
        var appJs = TestRepo.Read("src", "DocWiz.App", "wwwroot", "app.js");
        var dflt = Regex.Match(appJs, @"DEFAULT_LAYOUT\s*=\s*'([^']+)'").Groups[1].Value;
        Assert.Equal(SlideLayout.TitleContent, dflt);
        foreach (var layout in new[] { SlideLayout.TitleContent, SlideLayout.TwoColumn, SlideLayout.BigMedia, SlideLayout.SectionHeader })
        {
            var expected = string.Join(",", SlideLayout.RegionsFor(layout));
            var jsBlock = Regex.Match(appJs, $@"['""]?{Regex.Escape(layout)}['""]?\s*:\s*\[([^\]]*)\]").Groups[1].Value;
            var jsRegions = string.Join(",", Regex.Matches(jsBlock, @"['""]([^'""]+)['""]").Select(m => m.Groups[1].Value));
            Assert.Equal(expected, jsRegions);
        }
    }
}
