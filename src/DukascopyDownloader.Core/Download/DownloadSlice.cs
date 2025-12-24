using System.Globalization;

namespace DukascopyDownloader.Download;

internal readonly record struct DownloadSlice(
    string Instrument,
    DateTimeOffset Start,
    DateTimeOffset End,
    DukascopyTimeframe RequestedTimeframe,
    DukascopyFeedKind FeedKind,
    DukascopyPriceSide Side = DukascopyPriceSide.Bid)
{
    private const string RootUrl = "https://datafeed.dukascopy.com/datafeed";

    /// <summary>Cache subfolder (feed + side) associated with this slice.</summary>
    public string CacheScope => FeedKind switch
    {
        DukascopyFeedKind.Tick => "tick",
        DukascopyFeedKind.Minute => $"m1-{Side.ToString().ToLowerInvariant()}",
        DukascopyFeedKind.Hour => $"h1-{Side.ToString().ToLowerInvariant()}",
        DukascopyFeedKind.Day => $"d1-{Side.ToString().ToLowerInvariant()}",
        _ => "unknown"
    };
    /// <summary>Expected cache filename for the slice.</summary>
    public string CacheFileName => FeedKind switch
    {
        DukascopyFeedKind.Tick => $"{Start:yyyyMMdd_HH}.tick.bi5",
        DukascopyFeedKind.Minute => $"{Start:yyyyMMdd}.m1.{Side.ToString().ToLowerInvariant()}.bi5",
        DukascopyFeedKind.Hour => $"{Start:yyyyMM}.h1.{Side.ToString().ToLowerInvariant()}.bi5",
        DukascopyFeedKind.Day => $"{Start:yyyy}.d1.{Side.ToString().ToLowerInvariant()}.bi5",
        _ => $"{Start:yyyyMMdd}.bi5"
    };

    /// <summary>Dukascopy HTTP URL for the slice.</summary>
    public string Url
    {
        get
        {
            var utc = Start.UtcDateTime;
            var year = utc.Year;
            var month = utc.Month - 1;
            var day = utc.Day;
            var hour = utc.Hour;
            var sidePrefix = Side == DukascopyPriceSide.Ask ? "ASK" : "BID";

            return FeedKind switch
            {
                DukascopyFeedKind.Tick => $"{RootUrl}/{Instrument}/{year:D4}/{month:D2}/{day:D2}/{hour:D2}h_ticks.bi5",
                DukascopyFeedKind.Minute => $"{RootUrl}/{Instrument}/{year:D4}/{month:D2}/{day:D2}/{sidePrefix}_candles_min_1.bi5",
                DukascopyFeedKind.Hour => $"{RootUrl}/{Instrument}/{year:D4}/{month:D2}/{sidePrefix}_candles_hour_1.bi5",
                DukascopyFeedKind.Day => $"{RootUrl}/{Instrument}/{year:D4}/{sidePrefix}_candles_day_1.bi5",
                _ => throw new InvalidOperationException("Unknown feed kind.")
            };
        }
    }

    /// <summary>Short textual description used in logs and manifests.</summary>
    public string Describe()
    {
        var window = FeedKind == DukascopyFeedKind.Tick
            ? $"{Start:yyyy-MM-dd HH}:00Z"
            : $"{Start:yyyy-MM-dd}Z";

        var sideSuffix = FeedKind == DukascopyFeedKind.Tick ? string.Empty : $"-{Side.ToString().ToLowerInvariant()}";
        return $"{Instrument}-{RequestedTimeframe.ToDisplayString()}{sideSuffix}@{window}";
    }
}
