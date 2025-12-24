using System;
using System.Collections.Generic;
using System.IO;
using DukascopyDownloader.Download;
using DukascopyDownloader.Generation;
using Xunit;

namespace DukascopyDownloader.Tests.Generation;

public class CandleSeriesFillerTests
{
    [Fact]
    public void IncludeInactivePeriods_FillsMissingIntervalsWithFlatBars()
    {
        var candles = new List<CandleRecord>
        {
            new(
                new DateTimeOffset(2025, 1, 10, 0, 0, 0, TimeSpan.Zero),
                1.1000m,
                1.1010m,
                1.0990m,
                1.1005m,
                12,
                2),
            new(
                new DateTimeOffset(2025, 1, 10, 0, 2, 0, TimeSpan.Zero),
                1.1010m,
                1.1020m,
                1.1000m,
                1.1015m,
                7,
                3)
        };

        var download = CreateDownloadOptions(
            includeInactive: true,
            timeframe: DukascopyTimeframe.Minute1,
            fromUtc: new DateTimeOffset(2025, 1, 10, 0, 0, 0, TimeSpan.Zero),
            toUtc: new DateTimeOffset(2025, 1, 10, 0, 4, 0, TimeSpan.Zero));

        var filled = CandleSeriesFiller.IncludeInactivePeriods(candles, download, TimeZoneInfo.Utc);

        Assert.Equal(4, filled.Count);

        var filler = filled[1];
        Assert.Equal(new DateTimeOffset(2025, 1, 10, 0, 1, 0, TimeSpan.Zero), filler.LocalStart);
        Assert.Equal(0, filler.Volume);
        Assert.Equal(1.1005m, filler.Open);
        Assert.Equal(1.1005m, filler.Close);
        Assert.Equal(2, filler.SpreadPoints);

        var trailing = filled[3];
        Assert.Equal(new DateTimeOffset(2025, 1, 10, 0, 3, 0, TimeSpan.Zero), trailing.LocalStart);
        Assert.Equal(1.1015m, trailing.Open);
        Assert.Equal(0, trailing.Volume);
        Assert.Equal(3, trailing.SpreadPoints);
    }

    [Fact]
    public void IncludeInactivePeriods_WhenDisabled_ReturnsOriginalSequence()
    {
        var candles = new List<CandleRecord>
        {
            new(
                new DateTimeOffset(2025, 1, 10, 0, 0, 0, TimeSpan.Zero),
                1.1000m,
                1.1010m,
                1.0990m,
                1.1005m,
                12,
                2)
        };

        var download = CreateDownloadOptions(
            includeInactive: false,
            timeframe: DukascopyTimeframe.Minute1,
            fromUtc: new DateTimeOffset(2025, 1, 10, 0, 0, 0, TimeSpan.Zero),
            toUtc: new DateTimeOffset(2025, 1, 10, 0, 4, 0, TimeSpan.Zero));

        var filled = CandleSeriesFiller.IncludeInactivePeriods(candles, download, TimeZoneInfo.Utc);

        Assert.Same(candles, filled);
    }

    private static DownloadOptions CreateDownloadOptions(
        bool includeInactive,
        DukascopyTimeframe timeframe,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc)
    {
        return new DownloadOptions(
            "EURUSD",
            fromUtc,
            toUtc,
            timeframe,
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            null,
            UseCache: true,
            ForceRefresh: false,
            IncludeInactivePeriods: includeInactive,
            Concurrency: 1,
            MaxRetries: 0,
            RetryDelay: TimeSpan.FromSeconds(1),
            RateLimitPause: TimeSpan.FromSeconds(1),
            RateLimitRetryLimit: 1);
    }
}
