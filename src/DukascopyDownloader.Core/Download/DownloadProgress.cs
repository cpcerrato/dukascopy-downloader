namespace DukascopyDownloader.Download;

internal sealed class DownloadProgress
{
    private int _success;
    private int _cache;
    private int _failed;
    private int _missing;

    public int SuccessCount => _success;
    public int CacheHits => _cache;
    public int Failures => _failed;
    public int SkippedMissing => _missing;

    public void IncrementSuccess() => Interlocked.Increment(ref _success);
    public void IncrementCacheHit() => Interlocked.Increment(ref _cache);
    public void IncrementFailure() => Interlocked.Increment(ref _failed);
    public void IncrementMissing() => Interlocked.Increment(ref _missing);
}
