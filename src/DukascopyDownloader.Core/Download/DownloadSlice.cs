using System.Globalization;

namespace DukascopyDownloader.Download;

internal readonly record struct DownloadSlice(
    string Instrument,
    DateTimeOffset Start,
    DateTimeOffset End,
    DukascopyTimeframe RequestedTimeframe,
    DukascopyFeedKind FeedKind)
{
    private const string RootUrl = "https://datafeed.dukascopy.com/datafeed";

    public string CacheScope => FeedKind == DukascopyFeedKind.Tick ? "tick" : "m1";
    public string CacheFileName => FeedKind == DukascopyFeedKind.Tick
        ? $"{Start:yyyyMMdd_HH}.tick.bi5"
        : $"{Start:yyyyMMdd}.m1.bi5";

    public string Url
    {
        get
        {
            var utc = Start.UtcDateTime;
            var year = utc.Year;
            var month = utc.Month - 1;
            var day = utc.Day;
            var hour = utc.Hour;

            return FeedKind switch
            {
                DukascopyFeedKind.Tick => $"{RootUrl}/{Instrument}/{year:D4}/{month:D2}/{day:D2}/{hour:D2}h_ticks.bi5",
                DukascopyFeedKind.Minute => $"{RootUrl}/{Instrument}/{year:D4}/{month:D2}/{day:D2}/BID_candles_min_1.bi5",
                _ => throw new InvalidOperationException("Unknown feed kind.")
            };
        }
    }

    public string Describe()
    {
        var window = FeedKind == DukascopyFeedKind.Tick
            ? $"{Start:yyyy-MM-dd HH}:00Z"
            : $"{Start:yyyy-MM-dd}Z";

        return $"{Instrument}-{RequestedTimeframe.ToDisplayString()}@{window}";
    }
}
