using System;
using System.Collections.Generic;
using DukascopyDownloader.Download;
using DukascopyDownloader.Generation;
using DukascopyDownloader.Logging;
using Xunit;

namespace DukascopyDownloader.Tests.Generation;

public class SpreadPlanResolverTests
{
    private static readonly ConsoleLogger Logger = new();

    [Fact]
    public void Resolve_WithTickSize_UsesFromTicks()
    {
        var ticks = new List<TickRecord>
        {
            new(DateTimeOffset.UtcNow, 1.1002m, 1.1000m, 1, 1)
        };
        var opts = new GenerationOptions(TimeZoneInfo.Utc, "f", Template: ExportTemplate.MetaTrader5, TickSize: 0.0001m);

        var plan = SpreadPlanResolver.Resolve(ticks, opts, DukascopyTimeframe.Minute1, Logger);

        Assert.Equal(SpreadMode.FromTicks, plan.Mode);
        Assert.Equal(0.0001m, plan.TickSize);
    }

    [Fact]
    public void Resolve_InferTickSize_Succeeds_WhenEnoughDeltas()
    {
        var now = DateTimeOffset.UtcNow;
        var ticks = new List<TickRecord>
        {
            new(now, 1.1000m, 1.0998m, 1, 1),
            new(now.AddMilliseconds(1), 1.1001m, 1.0999m, 1, 1),
            new(now.AddMilliseconds(2), 1.1002m, 1.1000m, 1, 1)
        };
        var opts = new GenerationOptions(TimeZoneInfo.Utc, "f", Template: ExportTemplate.MetaTrader5, InferTickSize: true, MinNonZeroDeltas: 1);

        var plan = SpreadPlanResolver.Resolve(ticks, opts, DukascopyTimeframe.Minute1, Logger);

        Assert.Equal(SpreadMode.FromTicks, plan.Mode);
        Assert.Equal(0.0001m, plan.TickSize);
    }

    [Fact]
    public void Resolve_InferFails_WithFallbackSpreadPoints()
    {
        var now = DateTimeOffset.UtcNow;
        var ticks = new List<TickRecord>
        {
            new(now, 1.1000m, 1.1000m, 1, 1),
            new(now.AddMilliseconds(1), 1.1000m, 1.1000m, 1, 1)
        };
        var opts = new GenerationOptions(TimeZoneInfo.Utc, "f", Template: ExportTemplate.MetaTrader5, InferTickSize: true, MinNonZeroDeltas: 10, SpreadPoints: 5);

        var plan = SpreadPlanResolver.Resolve(ticks, opts, DukascopyTimeframe.Minute1, Logger);

        Assert.Equal(SpreadMode.Fixed, plan.Mode);
        Assert.Equal(5, plan.FixedSpreadPoints);
    }

    [Fact]
    public void Resolve_InferFailsWithoutFallback_Throws()
    {
        var ticks = new List<TickRecord>
        {
            new(DateTimeOffset.UtcNow, 1.1000m, 1.1000m, 1, 1)
        };
        var opts = new GenerationOptions(TimeZoneInfo.Utc, "f", Template: ExportTemplate.MetaTrader5, InferTickSize: true, MinNonZeroDeltas: 5);

        Assert.Throws<InvalidOperationException>(() =>
            SpreadPlanResolver.Resolve(ticks, opts, DukascopyTimeframe.Minute1, Logger));
    }
}
