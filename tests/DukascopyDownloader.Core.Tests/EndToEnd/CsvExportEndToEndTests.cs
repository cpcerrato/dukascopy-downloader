using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DukascopyDownloader.Download;
using DukascopyDownloader.Generation;
using DukascopyDownloader.Core.Logging;
using DukascopyDownloader.Core.Tests.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DukascopyDownloader.Tests.EndToEnd;

public sealed class CsvExportEndToEndTests : IDisposable
{
    private readonly string _cacheRoot = Path.Combine(Path.GetTempPath(), $"cache-e2e-{Guid.NewGuid():N}");

    [Fact]
    [Trait("Category", "EndToEnd")]
    public async Task CsvGenerator_WithIncludeInactive_FillsMissingDays()
    {
        var start = new DateTimeOffset(2025, 1, 14, 0, 0, 0, TimeSpan.Zero);
        var endExclusive = start.AddDays(3); // 14,15,16
        var exportRoot = Path.Combine(_cacheRoot, "exports");
        var download = CreateDownloadOptions(start, endExclusive, includeInactive: true, outputRoot: exportRoot);

        var cachePath = ResolveMinuteCachePath(start);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        var sample = Bi5TestSamples.WriteMinuteSample();
        File.Copy(sample, cachePath, overwrite: true);
        File.Delete(sample);

        var logger = new TestLogger();
        var generator = new CsvGenerator(NullLoggerFactory.Instance.CreateLogger<CsvGenerator>(), logger);
        var generation = new GenerationOptions(TimeZoneInfo.Utc, "yyyy-MM-dd HH:mm:ss");

        await generator.GenerateAsync(download, generation, CancellationToken.None);

        var exportPath = Path.Combine(exportRoot, "EURUSD_d1_20250114_20250116.csv");
        Assert.True(File.Exists(exportPath));

        var rows = File.ReadAllLines(exportPath).Skip(1).ToList();
        Assert.Equal(3, rows.Count);

        var first = rows[0].Split(',');
        Assert.Equal("11.5", first[5]);

        var second = rows[1].Split(',');
        Assert.Equal("2025-01-15 00:00:00", second[0]);
        Assert.Equal("0", second[5]);
        Assert.Equal(first[4], second[4]); // flat close
        Assert.Equal(first[4], second[1]); // open matches previous close

        var third = rows[2].Split(',');
        Assert.Equal("2025-01-16 00:00:00", third[0]);
        Assert.Equal("0", third[5]);
        Assert.Equal(first[4], third[4]);
    }

    private DownloadOptions CreateDownloadOptions(DateTimeOffset fromUtc, DateTimeOffset toUtc, bool includeInactive, string? outputRoot)
    {
        return new DownloadOptions(
            "EURUSD",
            fromUtc,
            toUtc,
            DukascopyTimeframe.Day1,
            _cacheRoot,
            outputRoot,
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
