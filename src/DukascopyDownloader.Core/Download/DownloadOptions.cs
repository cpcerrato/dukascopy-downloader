using System.Globalization;

namespace DukascopyDownloader.Download;

internal enum DukascopyTimeframe
{
    Tick = 0,
    Second1 = 1,
    Minute1 = 2,
    Minute5 = 3,
    Minute15 = 4,
    Minute30 = 5,
    Hour1 = 6,
    Hour4 = 7,
    Day1 = 8,
    Week1 = 9,
    Month1 = 10
}

internal enum DukascopyFeedKind
{
    Tick,
    Minute,
    Hour,
    Day
}

internal enum DukascopyPriceSide
{
    Bid,
    Ask
}

internal enum PriceSidePreference
{
    Bid,
    Ask,
    Both
}

internal static class DukascopyTimeframeExtensions
{
    /// <summary>
    /// Maps a Dukascopy timeframe to its underlying feed kind (tick or minute).
    /// </summary>
    /// <param name="timeframe">Requested timeframe.</param>
    /// <returns>Base Dukascopy feed kind.</returns>
    public static DukascopyFeedKind GetFeedKind(this DukascopyTimeframe timeframe) =>
        timeframe switch
        {
            DukascopyTimeframe.Tick or DukascopyTimeframe.Second1 => DukascopyFeedKind.Tick,
            DukascopyTimeframe.Minute1 or DukascopyTimeframe.Minute5 or DukascopyTimeframe.Minute15 or DukascopyTimeframe.Minute30 => DukascopyFeedKind.Minute,
            DukascopyTimeframe.Hour1 or DukascopyTimeframe.Hour4 => DukascopyFeedKind.Hour,
            DukascopyTimeframe.Day1 or DukascopyTimeframe.Week1 or DukascopyTimeframe.Month1 => DukascopyFeedKind.Day,
            _ => DukascopyFeedKind.Minute
        };

    /// <summary>
    /// Returns the Dukascopy feed shorthand string for the timeframe (e.g., m1, h1).
    /// </summary>
    /// <param name="timeframe">Requested timeframe.</param>
    /// <returns>Lowercase shorthand used in filenames/URLs.</returns>
    public static string ToDisplayString(this DukascopyTimeframe timeframe) =>
        timeframe switch
        {
            DukascopyTimeframe.Tick => "tick",
            DukascopyTimeframe.Second1 => "s1",
            DukascopyTimeframe.Minute1 => "m1",
            DukascopyTimeframe.Minute5 => "m5",
            DukascopyTimeframe.Minute15 => "m15",
            DukascopyTimeframe.Minute30 => "m30",
            DukascopyTimeframe.Hour1 => "h1",
            DukascopyTimeframe.Hour4 => "h4",
            DukascopyTimeframe.Day1 => "d1",
            DukascopyTimeframe.Week1 => "w1",
            DukascopyTimeframe.Month1 => "mn1",
            _ => timeframe.ToString()
        };
}

internal sealed record DownloadOptions(
    string Instrument,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    DukascopyTimeframe Timeframe,
    string CacheRoot,
    string? OutputDirectory,
    bool UseCache,
    bool ForceRefresh,
    bool IncludeInactivePeriods,
    int Concurrency,
    int MaxRetries,
    TimeSpan RetryDelay,
    TimeSpan RateLimitPause,
    int RateLimitRetryLimit,
    PriceSidePreference SidePreference = PriceSidePreference.Bid)
{
    /// <summary>
    /// Returns a short human-readable description of the download request.
    /// </summary>
    public override string ToString() =>
        $"{Instrument} {Timeframe.ToDisplayString()} {FromUtc:yyyy-MM-dd}..{ToUtc:yyyy-MM-dd}";
}
