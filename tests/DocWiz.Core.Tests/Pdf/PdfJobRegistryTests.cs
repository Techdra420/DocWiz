using DocWiz.App;
using Xunit;

namespace DocWiz.Core.Tests.Pdf;

public sealed class PdfJobRegistryTests
{
    [Fact]
    public void Get_UnknownId_ReturnsNull()
    {
        var registry = new PdfJobRegistry();
        Assert.Null(registry.Get("does-not-exist"));
    }

    [Theory]
    [InlineData("done")]
    [InlineData("error")]
    public void Get_TerminalJob_IsReturnedOnceThenEvicted(string status)
    {
        var registry = new PdfJobRegistry();
        var seeded = registry.SeedTerminal(status);

        // First poll delivers the terminal outcome to the client...
        var first = registry.Get(seeded.Id);
        Assert.NotNull(first);
        Assert.Equal(status, first!.Status);

        // ...and the entry is freed so its ExportResult does not leak for the session.
        Assert.Null(registry.Get(seeded.Id));
    }
}
