using DukascopyDownloader.Generation;
using Xunit;

namespace DukascopyDownloader.Tests.Generation;

public class GenerationOptionsFactoryTests
{
    private readonly GenerationOptionsFactory _factory = new();

    [Fact]
    public void TryCreate_WithValidTimezoneAndFormat_Succeeds()
    {
        var success = _factory.TryCreate("UTC", "yyyy-MM-dd", ExportTemplate.None, null, null, null, false, 100, SpreadAggregation.Median, false, true, null, false, out var options, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal(TimeZoneInfo.Utc, options.TimeZone);
        Assert.Equal("yyyy-MM-dd", options.DateFormat);
        Assert.True(options.IncludeHeader);
        Assert.Equal(ExportTemplate.None, options.Template);
    }

    [Fact]
    public void TryCreate_WithInvalidTimezone_Fails()
    {
        var success = _factory.TryCreate("Not/AZone", null, ExportTemplate.None, null, null, null, false, 100, SpreadAggregation.Median, false, true, null, false, out _, out var error);

        Assert.False(success);
        Assert.Contains("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryCreate_TrimsFormatAndStoresIt()
    {
        var success = _factory.TryCreate("UTC", "  yyyy/MM/dd HH:mm  ", ExportTemplate.None, null, null, null, false, 100, SpreadAggregation.Median, false, true, null, false, out var options, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal("yyyy/MM/dd HH:mm", options!.DateFormat);
    }

    [Fact]
    public void TryCreate_WithMetaTraderTemplate_SetsDefaults()
    {
        var success = _factory.TryCreate("Europe/London", null, ExportTemplate.MetaTrader5, null, null, null, false, 100, SpreadAggregation.Median, false, true, null, false, out var options, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal(ExportTemplate.MetaTrader5, options.Template);
        Assert.False(options.IncludeHeader);
        Assert.Equal("yyyy.MM.dd HH:mm:ss.fff", options.DateFormat);
    }

    [Fact]
    public void TryCreate_WithPreferTicks_SetsFlag()
    {
        var success = _factory.TryCreate("UTC", null, ExportTemplate.None, null, null, null, false, 100, SpreadAggregation.Median, false, true, null, true, out var options, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.True(options.PreferTicks);
    }
}
