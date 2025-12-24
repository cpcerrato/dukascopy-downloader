namespace DukascopyDownloader.Download;

internal sealed class DownloadProgress
{
    private int _success;
    private int _cache;
    private int _failed;
    private int _missing;

    /// <summary>Total successful downloads (new files).</summary>
    public int SuccessCount => _success;
    /// <summary>Total cache hits.</summary>
    public int CacheHits => _cache;
    /// <summary>Total failed slices.</summary>
    public int Failures => _failed;
    /// <summary>Total slices skipped as missing/empty.</summary>
    public int SkippedMissing => _missing;

    /// <summary>Increment new download counter.</summary>
    public void IncrementSuccess() => Interlocked.Increment(ref _success);
    /// <summary>Increment cache hit counter.</summary>
    public void IncrementCacheHit() => Interlocked.Increment(ref _cache);
    /// <summary>Add multiple cache hits (used for precounting).</summary>
    public void AddCacheHits(int count) => Interlocked.Add(ref _cache, count);
    /// <summary>Increment failure counter.</summary>
    public void IncrementFailure() => Interlocked.Increment(ref _failed);
    /// <summary>Increment missing/empty counter.</summary>
    public void IncrementMissing() => Interlocked.Increment(ref _missing);
}
