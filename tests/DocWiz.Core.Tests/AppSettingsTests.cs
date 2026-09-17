using DocWiz.App;
using Xunit;

namespace DocWiz.Core.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "docwiz-settings-" + Guid.NewGuid() + ".json");

    public AppSettingsTests() => AppSettings.StorePathOverride = _tmp;

    public void Dispose()
    {
        AppSettings.StorePathOverride = null;
        try { File.Delete(_tmp); } catch { /* ignore */ }
    }

    [Fact]
    public void Default_window_mode_is_native()
        => Assert.Equal("native", AppSettings.Load().WindowMode);

    [Theory]
    [InlineData("browser")]
    [InlineData("app")]
    [InlineData("native")]
    public void Save_then_Load_round_trips_window_mode(string mode)
    {
        AppSettings.Save(new AppSettings { WindowMode = mode });
        Assert.Equal(mode, AppSettings.Load().WindowMode);
    }

    [Fact]
    public void Corrupt_file_falls_back_to_default()
    {
        File.WriteAllText(_tmp, "{ not valid json ");
        Assert.Equal("native", AppSettings.Load().WindowMode);
    }

    [Theory]
    [InlineData("native", "native")]
    [InlineData("browser", "browser")]
    [InlineData("app", "native")]       // legacy Edge app-window mode migrates to the native window
    [InlineData("nonsense", "native")]
    [InlineData("", "native")]
    public void NormalizedWindowMode_reduces_to_browser_or_native(string stored, string expected)
        => Assert.Equal(expected, new AppSettings { WindowMode = stored }.NormalizedWindowMode);
}
