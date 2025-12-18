namespace DukascopyDownloader.Download;

internal sealed record DownloadSummary(
    int Total,
    int NewFiles,
    int CacheHits,
    int Missing,
    int Failed,
    TimeSpan Duration);
