using System.Collections.Generic;
using System.Linq;
using DukascopyDownloader.Download;

namespace DukascopyDownloader.Generation;

internal static class SpreadCalculator
{
    private const long Scale = 1_000_000_000L; // 1e9 to preserve decimal precision

    /// <summary>
    /// Infers tick size (point value) by computing the GCD of bid deltas. Returns null when insufficient deltas.
    /// </summary>
    /// <param name="ticks">Tick records to inspect.</param>
    /// <param name="minNonZeroDeltas">Minimum number of non-zero deltas required to accept inference.</param>
    /// <param name="nonZeroDeltas">Outputs the count of non-zero deltas observed.</param>
    /// <returns>Inferred tick size, or null when there is not enough signal.</returns>
    internal static decimal? InferTickSize(IEnumerable<TickRecord> ticks, int minNonZeroDeltas, out int nonZeroDeltas)
    {
        long gcd = 0;
        nonZeroDeltas = 0;
        decimal? previous = null;

        foreach (var tick in ticks)
        {
            if (previous is null)
            {
                previous = tick.Bid;
                continue;
            }

            var delta = tick.Bid - previous.Value;
            previous = tick.Bid;
            if (delta == 0)
            {
                continue;
            }

            nonZeroDeltas++;
            var scaled = ToScaledLong(Math.Abs(delta));
            gcd = gcd == 0 ? scaled : Gcd(gcd, scaled);
            if (gcd == 1)
            {
                // cannot get smaller than 1 unit of scale; break early
                break;
            }
        }

        if (nonZeroDeltas < minNonZeroDeltas || gcd <= 0)
        {
            return null;
        }

        return gcd / (decimal)Scale;
    }

    /// <summary>
    /// Computes spread-in-points per timeframe bucket using the provided tick size and aggregation mode.
    /// </summary>
    /// <param name="ticks">Tick records supplying bid/ask deltas.</param>
    /// <param name="timeframe">Target aggregation timeframe.</param>
    /// <param name="timeZone">Timezone for bucket alignment.</param>
    /// <param name="tickSize">Point value used to convert price diff to points.</param>
    /// <param name="aggregation">Aggregation mode (median/min/mean/last).</param>
    /// <returns>Dictionary keyed by bucket start (local) with spread points.</returns>
    internal static IReadOnlyDictionary<DateTimeOffset, int> AggregateSpreads(
        IEnumerable<TickRecord> ticks,
        DukascopyTimeframe timeframe,
        TimeZoneInfo timeZone,
        decimal tickSize,
        SpreadAggregation aggregation)
    {
        var bucketSpreads = new Dictionary<DateTimeOffset, List<int>>();
        foreach (var tick in ticks)
        {
            var spreadPointsTick = ComputeSpreadPoints(tick, tickSize);
            if (spreadPointsTick < 0)
            {
                spreadPointsTick = 0;
            }

            var bucketStart = timeframe == DukascopyTimeframe.Second1
                ? CandleAggregator.AlignToSecond(tick.TimestampUtc, timeZone)
                : CandleAggregator.AlignToTimeframe(tick.TimestampUtc, timeframe, timeZone);

            if (!bucketSpreads.TryGetValue(bucketStart, out var list))
            {
                list = new List<int>();
                bucketSpreads[bucketStart] = list;
            }

            list.Add(spreadPointsTick);
        }

        var result = new Dictionary<DateTimeOffset, int>();
        foreach (var kvp in bucketSpreads)
        {
            var values = kvp.Value;
            if (values.Count == 0)
            {
                continue;
            }

            int aggregated = aggregation switch
            {
                SpreadAggregation.Min => values.Min(),
                SpreadAggregation.Mean => (int)Math.Round(values.Average(), MidpointRounding.AwayFromZero),
                SpreadAggregation.Last => values[^1],
                _ => Median(values)
            };

            result[kvp.Key] = aggregated;
        }

        return result;
    }

    private static int ComputeSpreadPoints(TickRecord tick, decimal tickSize)
    {
        if (tickSize <= 0)
        {
            return 0;
        }

        var raw = (tick.Ask - tick.Bid) / tickSize;
        return (int)Math.Round(raw, MidpointRounding.AwayFromZero);
    }

    private static int ComputeSpreadPoints(decimal ask, decimal bid, decimal tickSize)
    {
        if (tickSize <= 0)
        {
            return 0;
        }

        var raw = (ask - bid) / tickSize;
        return (int)Math.Round(raw, MidpointRounding.AwayFromZero);
    }

    private static int Median(List<int> values)
    {
        values.Sort();
        var mid = values.Count / 2;
        if (values.Count % 2 == 1)
        {
            return values[mid];
        }

        var mean = (values[mid - 1] + values[mid]) / 2.0;
        return (int)Math.Round(mean, MidpointRounding.AwayFromZero);
    }

    private static long ToScaledLong(decimal value)
    {
        return (long)Math.Round(value * Scale, MidpointRounding.AwayFromZero);
    }

    private static long Gcd(long a, long b)
    {
        while (b != 0)
        {
            var temp = b;
            b = a % b;
            a = temp;
        }

        return Math.Abs(a);
    }
}
