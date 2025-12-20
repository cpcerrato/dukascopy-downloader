using System.Collections.Generic;
using System.Linq;
using DukascopyDownloader.Download;

namespace DukascopyDownloader.Generation;

internal static class CandleSeriesFiller
{
    public static IReadOnlyList<CandleRecord> IncludeInactivePeriods(
        IReadOnlyList<CandleRecord> candles,
        DownloadOptions options,
        TimeZoneInfo timeZone)
    {
        if (!options.IncludeInactivePeriods || options.Timeframe == DukascopyTimeframe.Tick || candles.Count == 0)
        {
            return candles;
        }

        var startUtc = AlignRangeStart(options.FromUtc, options.Timeframe, timeZone);
        var endUtc = options.ToUtc;
        if (startUtc >= endUtc)
        {
            return candles;
        }

        var lookup = candles.ToDictionary(c => c.LocalStart);
        var expected = new List<CandleRecord>();

        CandleRecord? lastActual = null;
        for (var cursorUtc = startUtc; cursorUtc < endUtc; cursorUtc = Advance(cursorUtc, options.Timeframe))
        {
            var localStart = TimeZoneInfo.ConvertTime(cursorUtc, timeZone);
            if (lookup.TryGetValue(localStart, out var actual))
            {
                expected.Add(actual);
                lastActual = actual;
                continue;
            }

            if (lastActual is null)
            {
                continue;
            }

            expected.Add(new CandleRecord(
                localStart,
                lastActual.Close,
                lastActual.Close,
                lastActual.Close,
                lastActual.Close,
                0,
                lastActual.SpreadPoints));
        }

        return expected;
    }

    private static DateTimeOffset AlignRangeStart(DateTimeOffset timestampUtc, DukascopyTimeframe timeframe, TimeZoneInfo timeZone) =>
        timeframe switch
        {
            DukascopyTimeframe.Second1 => CandleAggregator.AlignToSecond(timestampUtc, timeZone).ToUniversalTime(),
            _ => CandleAggregator.AlignToTimeframe(timestampUtc, timeframe, timeZone).ToUniversalTime()
        };

    private static DateTimeOffset Advance(DateTimeOffset currentUtc, DukascopyTimeframe timeframe) =>
        timeframe switch
        {
            DukascopyTimeframe.Second1 => currentUtc.AddSeconds(1),
            DukascopyTimeframe.Minute1 => currentUtc.AddMinutes(1),
            DukascopyTimeframe.Minute5 => currentUtc.AddMinutes(5),
            DukascopyTimeframe.Minute15 => currentUtc.AddMinutes(15),
            DukascopyTimeframe.Minute30 => currentUtc.AddMinutes(30),
            DukascopyTimeframe.Hour1 => currentUtc.AddHours(1),
            DukascopyTimeframe.Hour4 => currentUtc.AddHours(4),
            DukascopyTimeframe.Day1 => currentUtc.AddDays(1),
            DukascopyTimeframe.Month1 => currentUtc.AddMonths(1),
            _ => currentUtc.AddMinutes(1)
        };
}
