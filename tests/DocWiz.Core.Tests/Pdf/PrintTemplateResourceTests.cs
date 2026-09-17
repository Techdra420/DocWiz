using DocWiz.Build;
using Xunit;

namespace DocWiz.Core.Tests.Pdf;

public class PrintTemplateResourceTests
{
    [Theory]
    [InlineData("manual.print.html")]
    [InlineData("slides.print.html")]
    [InlineData("document.print.html")]
    [InlineData("print.css")]
    public void PrintResource_resolves_with_nonempty_content(string name)
    {
        var content = SiteTemplates.Read(name);
        Assert.False(string.IsNullOrWhiteSpace(content));
    }

    [Fact]
    public void Manual_template_has_content_placeholder()
    {
        Assert.Contains("{{CONTENT}}", SiteTemplates.Read("manual.print.html"));
    }
}
