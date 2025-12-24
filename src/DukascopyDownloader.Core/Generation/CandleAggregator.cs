using DukascopyDownloader.Download;

namespace DukascopyDownloader.Generation;

internal static class CandleAggregator
{
    private const decimal DefaultPipValue = 0.0001m;

    /// <summary>
    /// Aggregates tick records into 1-second candles using ask/bid for OHLC and counting ticks as volume.
    /// </summary>
    /// <param name="ticks">Tick records to aggregate.</param>
    /// <param name="timeZone">Target timezone for candle start alignment.</param>
    /// <param name="pipValue">Price increment (point value) used for spread calculation in seconds aggregation.</param>
    /// <returns>List of second candles aligned to the provided timezone.</returns>
    /// <summary>
    /// Aggregates tick records into 1-second candles.
    /// </summary>
    /// <param name="ticks">Tick stream (UTC) to aggregate.</param>
    /// <param name="timeZone">Target timezone for candle start times.</param>
    /// <param name="pipValue">Tick size/point used to compute spread points.</param>
    /// <returns>Sequence of second-level candles ordered by start time.</returns>
    public static IReadOnlyList<CandleRecord> AggregateSeconds(IEnumerable<TickRecord> ticks, TimeZoneInfo timeZone, decimal pipValue)
    {
        var buckets = new SortedDictionary<DateTimeOffset, CandleAccumulator>();
        foreach (var tick in ticks)
        {
            var bucketStart = AlignToSecond(tick.TimestampUtc, timeZone);
            if (!buckets.TryGetValue(bucketStart, out var acc))
            {
                acc = new CandleAccumulator(bucketStart, pipValue);
                buckets[bucketStart] = acc;
            }
            acc.AddTick(tick);
        }

        return buckets.Values.Select(acc => acc.Build()).ToList();
    }

    /// <summary>
    /// Aggregates minute BI5 candles into the requested timeframe, dropping zero-volume bars.
    /// </summary>
    /// <param name="minutes">Minute candles from BI5.</param>
    /// <param name="timeframe">Target timeframe (>= m1).</param>
    /// <param name="timeZone">Target timezone for bucket alignment.</param>
    /// <returns>Aggregated candles, excluding zero-volume entries.</returns>
    /// <summary>
    /// Aggregates Dukascopy minute records into higher timeframes.
    /// </summary>
    /// <param name="minutes">Minute bars to aggregate.</param>
    /// <param name="timeframe">Target timeframe (must be minute-or-higher).</param>
    /// <param name="timeZone">Target timezone for candle start times.</param>
    /// <returns>Aggregated candles ordered by start time.</returns>
    public static IReadOnlyList<CandleRecord> AggregateMinutes(IEnumerable<MinuteRecord> minutes, DukascopyTimeframe timeframe, TimeZoneInfo timeZone)
    {
        var buckets = new SortedDictionary<DateTimeOffset, CandleAccumulator>();
        foreach (var candle in minutes)
        {
            var bucketStart = AlignToTimeframe(candle.TimestampUtc, timeframe, timeZone);
            if (!buckets.TryGetValue(bucketStart, out var acc))
            {
                acc = new CandleAccumulator(bucketStart, DefaultPipValue);
                buckets[bucketStart] = acc;
            }
            acc.AddCandle(candle);
        }

        return buckets.Values
            .Select(acc => acc.Build())
            .Where(c => c.Volume > 0)
            .ToList();
    }

    /// <summary>
    /// Aggregates tick records directly into the requested timeframe (>= m1), counting ticks as volume.
    /// </summary>
    /// <summary>
    /// Aggregates tick records directly into the requested timeframe, computing spreads from tick deltas.
    /// </summary>
    /// <param name="ticks">Tick stream (UTC) to aggregate.</param>
    /// <param name="timeframe">Target timeframe (minute-or-higher).</param>
    /// <param name="timeZone">Target timezone for candle start times.</param>
    /// <param name="tickSize">Tick size/point used to convert bid-ask deltas into spread points.</param>
    /// <returns>Aggregated candles ordered by start time.</returns>
    public static IReadOnlyList<CandleRecord> AggregateTicks(IEnumerable<TickRecord> ticks, DukascopyTimeframe timeframe, TimeZoneInfo timeZone, decimal tickSize)
    {
        var buckets = new SortedDictionary<DateTimeOffset, CandleAccumulator>();
        foreach (var tick in ticks)
        {
            var bucketStart = AlignToTimeframe(tick.TimestampUtc, timeframe, timeZone);
            if (!buckets.TryGetValue(bucketStart, out var acc))
            {
                acc = new CandleAccumulator(bucketStart, tickSize);
                buckets[bucketStart] = acc;
            }
            acc.AddTick(tick);
        }

        return buckets.Values
            .Select(acc => acc.Build())
            .Where(c => c.Volume > 0)
            .ToList();
    }

    internal static DateTimeOffset AlignToSecond(DateTimeOffset timestampUtc, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(timestampUtc, timeZone);
        return new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, local.Minute, local.Second, local.Offset);
    }

    internal static DateTimeOffset AlignToTimeframe(DateTimeOffset timestampUtc, DukascopyTimeframe timeframe, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(timestampUtc, timeZone);
        var offset = local.Offset;

        return timeframe switch
        {
            DukascopyTimeframe.Minute1 => new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, offset),
            DukascopyTimeframe.Minute5 => AlignMinutes(local, 5),
            DukascopyTimeframe.Minute15 => AlignMinutes(local, 15),
            DukascopyTimeframe.Minute30 => AlignMinutes(local, 30),
            DukascopyTimeframe.Hour1 => AlignHours(local, 1),
            DukascopyTimeframe.Hour4 => AlignHours(local, 4),
            DukascopyTimeframe.Day1 => new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, offset),
            DukascopyTimeframe.Week1 => AlignToWeek(local),
            DukascopyTimeframe.Month1 => new DateTimeOffset(local.Year, local.Month, 1, 0, 0, 0, offset),
            _ => new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, offset)
        };
    }

    private static DateTimeOffset AlignMinutes(DateTimeOffset local, int period)
    {
        var minuteBucket = (local.Minute / period) * period;
        return new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, minuteBucket, 0, local.Offset);
    }

    private static DateTimeOffset AlignHours(DateTimeOffset local, int period)
    {
        var hourBucket = (local.Hour / period) * period;
        return new DateTimeOffset(local.Year, local.Month, local.Day, hourBucket, 0, 0, local.Offset);
    }

    private static DateTimeOffset AlignToWeek(DateTimeOffset local)
    {
        var startOfWeek = local;
        while (startOfWeek.DayOfWeek != DayOfWeek.Monday)
        {
            startOfWeek = startOfWeek.AddDays(-1);
        }
        return new DateTimeOffset(startOfWeek.Year, startOfWeek.Month, startOfWeek.Day, 0, 0, 0, local.Offset);
    }

    private sealed class CandleAccumulator
    {
        private readonly DateTimeOffset _localStart;
        private readonly decimal _pipValue;
        private decimal _open;
        private decimal _high;
        private decimal _low;
        private decimal _close;
        private double _volume;
        private bool _initialized;
        private int _spreadPoints = 1;
        private bool _hasSpread;

        public CandleAccumulator(DateTimeOffset localStart, decimal pipValue)
        {
            _localStart = localStart;
            _pipValue = pipValue > 0 ? pipValue : 0.0001m;
            _high = decimal.MinValue;
            _low = decimal.MaxValue;
        }

        public void AddTick(TickRecord tick)
        {
            var openClose = tick.Ask;
            var high = tick.Ask >= tick.Bid ? tick.Ask : tick.Bid;
            var low = tick.Ask <= tick.Bid ? tick.Ask : tick.Bid;
            Update(openClose, high, low, openClose, volume: 1);
            var point = _pipValue;
            var spreadPoints = point <= 0 ? 1 : (int)Math.Round((tick.Ask - tick.Bid) / point, MidpointRounding.AwayFromZero);
            if (spreadPoints <= 0 && tick.Ask > tick.Bid)
            {
                spreadPoints = 1;
            }
            _spreadPoints = spreadPoints;
            _hasSpread = true;
        }

        public void AddCandle(MinuteRecord candle)
        {
            Update(candle.Open, candle.High, candle.Low, candle.Close, candle.Volume);
        }

        private void Update(decimal open, decimal high, decimal low, decimal close, double volume)
        {
            if (!_initialized)
            {
                _open = open;
                _high = high;
                _low = low;
                _close = close;
                _initialized = true;
            }
            else
            {
                _close = close;
                if (high > _high)
                {
                    _high = high;
                }
                if (low < _low)
                {
                    _low = low;
                }
            }

            _volume += volume;
        }

        public CandleRecord Build() =>
            new(
                _localStart,
                _open,
                _high == decimal.MinValue ? _open : _high,
                _low == decimal.MaxValue ? _open : _low,
                _close,
                _volume,
                _hasSpread ? _spreadPoints : 1);
    }
}
