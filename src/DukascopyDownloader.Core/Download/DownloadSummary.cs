namespace DukascopyDownloader.Download;

/// <summary>
/// Summary of a download run.
/// </summary>
/// <param name="Total">Total slices scheduled.</param>
/// <param name="NewFiles">Number of slices downloaded from the server.</param>
/// <param name="CacheHits">Number of slices served from cache.</param>
/// <param name="Missing">Number of slices that were valid but empty/missing from Dukascopy.</param>
/// <param name="Failed">Number of slices that exhausted retries.</param>
/// <param name="Duration">Elapsed time for the run.</param>
internal sealed record DownloadSummary(
    int Total,
    int NewFiles,
    int CacheHits,
    int Missing,
    int Failed,
    TimeSpan Duration);
