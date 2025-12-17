using DukascopyDownloader.Download;

namespace DukascopyDownloader.Generation;

internal static class CandleAggregator
{
    public static IReadOnlyList<CandleRecord> AggregateSeconds(IEnumerable<TickRecord> ticks, TimeZoneInfo timeZone)
    {
        var buckets = new SortedDictionary<DateTimeOffset, CandleAccumulator>();
        foreach (var tick in ticks)
        {
            var bucketStart = AlignToSecond(tick.TimestampUtc, timeZone);
            if (!buckets.TryGetValue(bucketStart, out var acc))
            {
                acc = new CandleAccumulator(bucketStart);
                buckets[bucketStart] = acc;
            }
            acc.AddTick(tick);
        }

        return buckets.Values.Select(acc => acc.Build()).ToList();
    }

    public static IReadOnlyList<CandleRecord> AggregateMinutes(IEnumerable<MinuteRecord> minutes, DukascopyTimeframe timeframe, TimeZoneInfo timeZone)
    {
        var buckets = new SortedDictionary<DateTimeOffset, CandleAccumulator>();
        foreach (var candle in minutes)
        {
            var bucketStart = AlignToTimeframe(candle.TimestampUtc, timeframe, timeZone);
            if (!buckets.TryGetValue(bucketStart, out var acc))
            {
                acc = new CandleAccumulator(bucketStart);
                buckets[bucketStart] = acc;
            }
            acc.AddCandle(candle);
        }

        return buckets.Values.Select(acc => acc.Build()).Where(c => c.Volume > 0 || c.Open != 0 || c.Close != 0).ToList();
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

    private sealed class CandleAccumulator
    {
        private readonly DateTimeOffset _localStart;
        private decimal _open;
        private decimal _high;
        private decimal _low;
        private decimal _close;
        private double _volume;
        private bool _initialized;

        public CandleAccumulator(DateTimeOffset localStart)
        {
            _localStart = localStart;
            _high = decimal.MinValue;
            _low = decimal.MaxValue;
        }

        public void AddTick(TickRecord tick)
        {
            var openClose = tick.Ask;
            var high = tick.Ask >= tick.Bid ? tick.Ask : tick.Bid;
            var low = tick.Ask <= tick.Bid ? tick.Ask : tick.Bid;
            Update(openClose, high, low, openClose, tick.AskVolume);
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
                _volume);
    }
}
