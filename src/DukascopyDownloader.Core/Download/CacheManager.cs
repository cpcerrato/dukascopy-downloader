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

    public string ResolveCachePath(DownloadSlice slice)
    {
        var yearSegment = slice.Start.UtcDateTime.Year.ToString("D4", CultureInfo.InvariantCulture);
        var folder = Path.Combine(_cacheRoot, slice.Instrument, yearSegment, slice.CacheScope);
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, slice.CacheFileName);
    }

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
