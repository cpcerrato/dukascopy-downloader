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
    Month1 = 9
}

internal enum DukascopyFeedKind
{
    Tick,
    Minute
}

internal static class DukascopyTimeframeExtensions
{
    public static DukascopyFeedKind GetFeedKind(this DukascopyTimeframe timeframe) =>
        timeframe <= DukascopyTimeframe.Second1 ? DukascopyFeedKind.Tick : DukascopyFeedKind.Minute;

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
    int RateLimitRetryLimit)
{
    /// <summary>
    /// Returns a short human-readable description of the download request.
    /// </summary>
    public override string ToString() =>
        $"{Instrument} {Timeframe.ToDisplayString()} {FromUtc:yyyy-MM-dd}..{ToUtc:yyyy-MM-dd}";
}
