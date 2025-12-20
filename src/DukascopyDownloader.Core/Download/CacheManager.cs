using System.Globalization;

namespace DukascopyDownloader.Download;

internal sealed class CacheManager
{
    private readonly string _cacheRoot;
    private readonly string? _outputRoot;

    public CacheManager(string cacheRoot, string? outputRoot)
    {
        _cacheRoot = cacheRoot;
        _outputRoot = string.IsNullOrWhiteSpace(outputRoot) ? null : outputRoot;
    }

    /// <summary>
    /// Resolves the on-disk cache path for a given slice, creating intermediate directories.
    /// </summary>
    /// <param name="slice">Slice describing instrument, timeframe and start time.</param>
    /// <returns>Full file path under the cache root.</returns>
    public string ResolveCachePath(DownloadSlice slice)
    {
        var yearSegment = slice.Start.UtcDateTime.Year.ToString("D4", CultureInfo.InvariantCulture);
        var folder = Path.Combine(_cacheRoot, slice.Instrument, yearSegment, slice.CacheScope);
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, slice.CacheFileName);
    }

    /// <summary>
    /// Mirrors a verified cache file into the configured output directory (if any), skipping when already present.
    /// </summary>
    /// <param name="cachePath">Path to the cached BI5 file.</param>
    /// <param name="slice">Slice metadata used to build the mirror path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SyncToOutputAsync(string cachePath, DownloadSlice slice, CancellationToken cancellationToken)
    {
        if (_outputRoot is null)
        {
            return;
        }

        var yearSegment = slice.Start.UtcDateTime.Year.ToString("D4", CultureInfo.InvariantCulture);
        var destinationFolder = Path.Combine(_outputRoot, slice.Instrument, yearSegment, slice.CacheScope);
        Directory.CreateDirectory(destinationFolder);
        var destinationFile = Path.Combine(destinationFolder, Path.GetFileName(cachePath));

        if (File.Exists(destinationFile))
        {
            return;
        }

        await using var source = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        await using var target = new FileStream(destinationFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await source.CopyToAsync(target, cancellationToken);
    }
}
