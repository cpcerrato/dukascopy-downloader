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

    [Fact]
    public void AggregateMinutes_UsesTimezoneOffsetsAcrossDst()
    {
        var startUtc = new DateTimeOffset(2025, 3, 9, 4, 0, 0, TimeSpan.Zero);
        var minutes = new[]
        {
            new MinuteRecord(startUtc, 1.2m, 1.3m, 1.1m, 1.25m, 10)
        };

        var tzUs = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var tzEu = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

        var usCandle = CandleAggregator.AggregateMinutes(minutes, DukascopyTimeframe.Day1, tzUs).Single();
        var euCandle = CandleAggregator.AggregateMinutes(minutes, DukascopyTimeframe.Day1, tzEu).Single();

        Assert.Equal(TimeSpan.FromHours(-5), usCandle.LocalStart.Offset);
        Assert.Equal(TimeSpan.FromHours(1), euCandle.LocalStart.Offset);
        Assert.NotEqual(usCandle.LocalStart, euCandle.LocalStart);
    }
}
