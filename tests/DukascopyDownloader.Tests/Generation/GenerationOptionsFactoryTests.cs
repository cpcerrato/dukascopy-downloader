using DukascopyDownloader.Generation;
using Xunit;

namespace DukascopyDownloader.Tests.Generation;

public class GenerationOptionsFactoryTests
{
    private readonly GenerationOptionsFactory _factory = new();

    [Fact]
    public void TryCreate_WithValidTimezoneAndFormat_Succeeds()
    {
        var success = _factory.TryCreate("UTC", "yyyy-MM-dd", out var options, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Equal(TimeZoneInfo.Utc, options!.TimeZone);
        Assert.Equal("yyyy-MM-dd", options.DateFormat);
    }

    [Fact]
    public void TryCreate_WithInvalidTimezone_Fails()
    {
        var success = _factory.TryCreate("Not/AZone", null, out _, out var error);

        Assert.False(success);
        Assert.Contains("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryCreate_TrimsFormatAndStoresIt()
    {
        var success = _factory.TryCreate("UTC", "  yyyy/MM/dd HH:mm  ", out var options, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal("yyyy/MM/dd HH:mm", options!.DateFormat);
    }
}
