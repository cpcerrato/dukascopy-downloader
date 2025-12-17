using DukascopyDownloader.Download;
using Xunit;

namespace DukascopyDownloader.Tests.Download;

public class CacheManagerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cache-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _output = Path.Combine(Path.GetTempPath(), "cache-tests-output-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ResolveCachePath_UsesInstrumentYearAndScope()
    {
        var manager = new CacheManager(_root, _output);
        var slice = CreateSlice(DukascopyFeedKind.Tick, new DateTimeOffset(2025, 1, 14, 0, 0, 0, TimeSpan.Zero));

        var path = manager.ResolveCachePath(slice);

        Assert.Contains(Path.Combine(_root, "EURUSD", "2025", "tick"), path);
        Assert.EndsWith(".tick.bi5", path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SyncToOutputAsync_CopiesFile_WhenMissing()
    {
        var manager = new CacheManager(_root, _output);
        var slice = CreateSlice(DukascopyFeedKind.Minute, new DateTimeOffset(2025, 1, 14, 0, 0, 0, TimeSpan.Zero));
        var cachePath = manager.ResolveCachePath(slice);
        await File.WriteAllTextAsync(cachePath, "payload");

        await manager.SyncToOutputAsync(cachePath, slice, CancellationToken.None);

        var expected = Path.Combine(_output, "EURUSD", "2025", "m1", Path.GetFileName(cachePath));
        Assert.True(File.Exists(expected));
        Assert.Equal("payload", await File.ReadAllTextAsync(expected));
    }

    private static DownloadSlice CreateSlice(DukascopyFeedKind kind, DateTimeOffset start) =>
        new("EURUSD", start, start.AddHours(1), DukascopyTimeframe.Tick, kind);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
        if (Directory.Exists(_output))
        {
            Directory.Delete(_output, recursive: true);
        }
    }
}
