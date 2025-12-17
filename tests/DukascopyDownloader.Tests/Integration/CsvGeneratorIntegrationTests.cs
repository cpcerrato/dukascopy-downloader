using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DukascopyDownloader.Download;
using DukascopyDownloader.Generation;
using DukascopyDownloader.Logging;
using DukascopyDownloader.Tests.Support;

namespace DukascopyDownloader.Tests.Integration;

public sealed class CsvGeneratorIntegrationTests : IDisposable
{
    private readonly string _cacheRoot = Path.Combine(Path.GetTempPath(), $"cache-int-{Guid.NewGuid():N}");

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GenerateAsync_FromCachedMinuteFiles_WritesAggregatedCsv()
    {
        var dayStart = new DateTimeOffset(2025, 1, 14, 0, 0, 0, TimeSpan.Zero);
        var download = CreateDownloadOptions(dayStart, dayStart.AddDays(1), includeInactive: false);

        var cachePath = ResolveMinuteCachePath(dayStart);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        var sample = Bi5TestSamples.WriteMinuteSample();
        File.Copy(sample, cachePath, overwrite: true);
        File.Delete(sample);

        var generator = new CsvGenerator(new ConsoleLogger());
        var generation = new GenerationOptions(TimeZoneInfo.Utc, "yyyy-MM-dd HH:mm:ss");

        await generator.GenerateAsync(download, generation, CancellationToken.None);

        var exportPath = Path.Combine(_cacheRoot, "exports", "EURUSD_d1_20250114_20250114.csv");
        Assert.True(File.Exists(exportPath));

        var lines = File.ReadAllLines(exportPath);
        Assert.Equal(2, lines.Length);

        var row = lines[1].Split(',');
        Assert.Equal("2025-01-14 00:00:00", row[0]);
        Assert.Equal("1.2", row[1]);    // open
        Assert.Equal("1.34", row[2]);   // high
        Assert.Equal("1.18", row[3]);   // low
        Assert.Equal("1.35", row[4]);   // close
        Assert.Equal("11.5", row[5]);   // volume
    }

    private DownloadOptions CreateDownloadOptions(DateTimeOffset fromUtc, DateTimeOffset toUtc, bool includeInactive)
    {
        return new DownloadOptions(
            "EURUSD",
            fromUtc,
            toUtc,
            DukascopyTimeframe.Day1,
            _cacheRoot,
            null,
            UseCache: true,
            ForceRefresh: false,
            IncludeInactivePeriods: includeInactive,
            Concurrency: 1,
            MaxRetries: 0,
            RetryDelay: TimeSpan.FromSeconds(1),
            RateLimitPause: TimeSpan.FromSeconds(1),
            RateLimitRetryLimit: 1);
    }

    private string ResolveMinuteCachePath(DateTimeOffset dayStart)
    {
        var year = dayStart.UtcDateTime.Year.ToString("D4");
        return Path.Combine(_cacheRoot, "EURUSD", year, "m1", $"{dayStart:yyyyMMdd}.m1.bi5");
    }

    public void Dispose()
    {
        if (Directory.Exists(_cacheRoot))
        {
            Directory.Delete(_cacheRoot, recursive: true);
        }
    }
}
