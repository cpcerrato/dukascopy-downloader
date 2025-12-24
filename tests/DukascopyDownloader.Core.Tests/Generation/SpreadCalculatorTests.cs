using System;
using System.Collections.Generic;
using DukascopyDownloader.Download;
using DukascopyDownloader.Generation;
using Xunit;

namespace DukascopyDownloader.Tests.Generation;

public class SpreadCalculatorTests
{
    [Fact]
    public void InferTickSize_ReturnsGcdWhenEnoughDeltas()
    {
        var ticks = new List<TickRecord>
        {
            new(DateTimeOffset.UtcNow, 1.10000m, 1.09990m, 1, 1),
            new(DateTimeOffset.UtcNow.AddSeconds(1), 1.10001m, 1.09991m, 1, 1),
            new(DateTimeOffset.UtcNow.AddSeconds(2), 1.10002m, 1.09992m, 1, 1),
            new(DateTimeOffset.UtcNow.AddSeconds(3), 1.10003m, 1.09993m, 1, 1),
        };

        var tickSize = SpreadCalculator.InferTickSize(ticks, minNonZeroDeltas: 2, out var count);

        Assert.Equal(3, count);
        Assert.Equal(0.00001m, tickSize);
    }

    [Fact]
    public void InferTickSize_ReturnsNullWhenInsufficientDeltas()
    {
        var ticks = new List<TickRecord>
        {
            new(DateTimeOffset.UtcNow, 1.10000m, 1.09990m, 1, 1),
            new(DateTimeOffset.UtcNow.AddSeconds(1), 1.10000m, 1.09990m, 1, 1),
            new(DateTimeOffset.UtcNow.AddSeconds(2), 1.10000m, 1.09990m, 1, 1),
        };

        var tickSize = SpreadCalculator.InferTickSize(ticks, minNonZeroDeltas: 2, out var count);

        Assert.Equal(0, count);
        Assert.Null(tickSize);
    }

    [Fact]
    public void AggregateSpreads_ComputesMedianPerBucket()
    {
        var tz = TimeZoneInfo.Utc;
        var start = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var ticks = new List<TickRecord>
        {
            new(start, 1.10000m, 1.09990m, 1, 1), // spread 10
            new(start.AddSeconds(10), 1.10002m, 1.09992m, 1, 1), // spread 10
            new(start.AddSeconds(20), 1.10005m, 1.09995m, 1, 1), // spread 10
            new(start.AddMinutes(1), 1.10010m, 1.10000m, 1, 1), // spread 10 (next bucket)
            new(start.AddMinutes(1).AddSeconds(5), 1.10020m, 1.10005m, 1, 1), // spread 15
        };

        var spreads = SpreadCalculator.AggregateSpreads(
            ticks,
            DukascopyTimeframe.Minute1,
            tz,
            tickSize: 0.00001m,
            aggregation: SpreadAggregation.Median);

        Assert.Equal(2, spreads.Count);
        Assert.True(spreads[start] > 0);
        Assert.Equal(13, spreads[start.AddMinutes(1)]); // median of [10,15] -> 12.5 -> 13 (rounded away from zero)
    }
}
