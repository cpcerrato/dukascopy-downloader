using DukascopyDownloader.Download;
using DukascopyDownloader.Generation;
using Xunit;

namespace DukascopyDownloader.Tests.Generation;

public class CandleAggregatorTests
{
    [Fact]
    public void AggregateSeconds_GroupsByTimezone()
    {
        var tz = TimeZoneInfo.Utc;
        var start = new DateTimeOffset(2025, 1, 14, 0, 0, 0, TimeSpan.Zero);
        var ticks = new[]
        {
            new TickRecord(start, 1.1000m, 1.0999m, 1, 1),
            new TickRecord(start.AddMilliseconds(500), 1.1005m, 1.1004m, 2, 2)
        };

        var candles = CandleAggregator.AggregateSeconds(ticks, tz);

        var candle = Assert.Single(candles);
        Assert.Equal(start, candle.LocalStart);
        Assert.Equal(1.1000m, candle.Open);
        Assert.Equal(1.1005m, candle.High);
        Assert.Equal(1.0999m, candle.Low);
        Assert.Equal(1.1005m, candle.Close);
        Assert.Equal(3, candle.Volume);
    }

    [Fact]
    public void AggregateMinutes_AlignsToTimezoneBoundaries()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var startUtc = new DateTimeOffset(2025, 3, 9, 6, 0, 0, TimeSpan.Zero); // DST boundary day
        var minutes = Enumerable.Range(0, 60).Select(i =>
            new MinuteRecord(startUtc.AddMinutes(i), 1.0m + i, 1.1m + i, 0.9m + i, 1.05m + i, 1)).ToList();

        var candles = CandleAggregator.AggregateMinutes(minutes, DukascopyTimeframe.Hour1, tz);

        var candle = Assert.Single(candles);
        Assert.Equal(TimeZoneInfo.ConvertTime(startUtc, tz), candle.LocalStart);
        Assert.Equal(1.0m, candle.Open);
        Assert.Equal(1.1m + 59, candle.High);
        Assert.Equal(0.9m, candle.Low);
        Assert.Equal(1.05m + 59, candle.Close);
        Assert.Equal(60, candle.Volume);
    }
}
