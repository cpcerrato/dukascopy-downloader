using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DukascopyDownloader.Download;
using DukascopyDownloader.Generation;
using DukascopyDownloader.Core.Logging;
using DukascopyDownloader.Core.Tests.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DukascopyDownloader.Tests.Integration;

public sealed class CsvGeneratorIntegrationTests : IDisposable
{
    private readonly string _cacheRoot = Path.Combine(Path.GetTempPath(), $"cache-int-{Guid.NewGuid():N}");

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GenerateAsync_FromCachedMinuteFiles_WritesAggregatedCsv()
    {
        var dayStart = new DateTimeOffset(2025, 1, 14, 0, 0, 0, TimeSpan.Zero);
        var exportRoot = Path.Combine(_cacheRoot, "exports");
        var download = CreateDownloadOptions(dayStart, dayStart.AddDays(1), includeInactive: false, outputRoot: exportRoot);

        PopulateMinuteCache(dayStart);

        var generator = new CsvGenerator(NullLoggerFactory.Instance.CreateLogger<CsvGenerator>(), new TestLogger());
        var generation = new GenerationOptions(TimeZoneInfo.Utc, "yyyy-MM-dd HH:mm:ss");

        await generator.GenerateAsync(download, generation, CancellationToken.None);

        var exportPath = Path.Combine(exportRoot, "EURUSD_d1_20250114_20250114.csv");
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

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GenerateAsync_DefaultOptions_UseUnixMilliseconds()
    {
        var dayStart = new DateTimeOffset(2025, 1, 14, 0, 0, 0, TimeSpan.Zero);
        var download = CreateDownloadOptions(dayStart, dayStart.AddDays(1), includeInactive: false, outputRoot: null);

        PopulateMinuteCache(dayStart);

        var generator = new CsvGenerator(NullLoggerFactory.Instance.CreateLogger<CsvGenerator>(), new TestLogger());
        var generation = new GenerationOptions(TimeZoneInfo.Utc, null);

        var workDir = Path.Combine(_cacheRoot, "cwd");
        Directory.CreateDirectory(workDir);
        var original = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workDir);
        try
        {
            await generator.GenerateAsync(download, generation, CancellationToken.None);
        }
        finally
        {
            Directory.SetCurrentDirectory(original);
        }

        var exportPath = Path.Combine(workDir, "EURUSD_d1_20250114_20250114.csv");
        Assert.True(File.Exists(exportPath));

        var lines = File.ReadAllLines(exportPath);
        var row = lines[1].Split(',');
        Assert.True(row[0].All(char.IsDigit), "Timestamp should be Unix milliseconds.");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GenerateAsync_TickCsv_DefaultUtcMatchesCustomTimezoneBody()
    {
        var start = new DateTimeOffset(2025, 1, 14, 0, 0, 0, TimeSpan.Zero);
        var end = start.AddDays(1);
        PopulateTickCache(start);

        var generator = new CsvGenerator(NullLoggerFactory.Instance.CreateLogger<CsvGenerator>(), new TestLogger());
        var utcDownload = CreateTickOptions(start, end, outputRoot: Path.Combine(_cacheRoot, "utc-export"));
        var tzDownload = CreateTickOptions(start, end, outputRoot: Path.Combine(_cacheRoot, "tz-export"));

        var utcGeneration = new GenerationOptions(TimeZoneInfo.Utc, null);
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var tzGeneration = new GenerationOptions(tz, "yyyy.MM.dd HH:mm:ss");

        await generator.GenerateAsync(utcDownload, utcGeneration, CancellationToken.None);
        await generator.GenerateAsync(tzDownload, tzGeneration, CancellationToken.None);

        var utcPath = Directory.GetFiles(utcDownload.OutputDirectory!, "*.csv").Single();
        var tzPath = Directory.GetFiles(tzDownload.OutputDirectory!, "*.csv").Single();

        var utcBody = File.ReadLines(utcPath).Skip(1).ToList();
        var tzBody = File.ReadLines(tzPath).Skip(1).ToList();

        Assert.NotEmpty(utcBody);
        Assert.Equal(utcBody.Count, tzBody.Count);

        for (var i = 0; i < utcBody.Count; i++)
        {
            var utcParts = utcBody[i].Split(',');
            var tzParts = tzBody[i].Split(',');
            Assert.True(utcParts[0].All(char.IsDigit), "UTC timestamp should be milliseconds.");
            Assert.Equal(utcParts.Skip(1), tzParts.Skip(1));
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GenerateAsync_TickCsv_DefaultUtcUsesUnixMilliseconds()
    {
        var start = new DateTimeOffset(2025, 1, 14, 0, 0, 0, TimeSpan.Zero);
        var end = start.AddDays(1);
        PopulateTickCache(start);

        var generator = new CsvGenerator(NullLoggerFactory.Instance.CreateLogger<CsvGenerator>(), new TestLogger());
        var utcDownload = CreateTickOptions(start, end, outputRoot: Path.Combine(_cacheRoot, "utc-export"));
        var utcGeneration = new GenerationOptions(TimeZoneInfo.Utc, null);

        await generator.GenerateAsync(utcDownload, utcGeneration, CancellationToken.None);
        var utcPath = Directory.GetFiles(utcDownload.OutputDirectory!, "*.csv").Single();

        var lines = File.ReadAllLines(utcPath);
        Assert.True(lines.Length > 1);

        var firstTimestamp = lines[1].Split(',')[0];
        Assert.True(firstTimestamp.All(char.IsDigit), "Timestamp should be Unix milliseconds.");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GenerateAsync_TickCsv_MetaTraderTemplate_OmitsHeaderAndFormats()
    {
        var start = new DateTimeOffset(2025, 1, 14, 0, 0, 0, TimeSpan.Zero);
        var end = start.AddDays(1);
        PopulateTickCache(start);

        var generator = new CsvGenerator(NullLoggerFactory.Instance.CreateLogger<CsvGenerator>(), new TestLogger());
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var mt5Generation = new GenerationOptions(tz, "yyyy.MM.dd HH:mm:ss.fff", false, ExportTemplate.MetaTrader5, SpreadPoints: 1);
        var download = CreateTickOptions(start, end, outputRoot: Path.Combine(_cacheRoot, "mt5-export"));

        await generator.GenerateAsync(download, mt5Generation, CancellationToken.None);
        var path = Directory.GetFiles(download.OutputDirectory!, "*.csv").Single();
        var lines = File.ReadAllLines(path);

        Assert.True(lines.Length > 0);
        Assert.DoesNotContain("timestamp", lines[0], StringComparison.OrdinalIgnoreCase);

        var parts = lines[0].Split(',');
        Assert.Equal(5, parts.Length);
        Assert.True(DateTime.TryParseExact(parts[0], "yyyy.MM.dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
        Assert.NotEqual(string.Empty, parts[1] + parts[2]); // At least one of bid/ask present
        Assert.Equal(string.Empty, parts[3]); // Last empty for FX/CFD ticks
        Assert.Equal(string.Empty, parts[4]); // Volume empty for FX/CFD ticks
        Assert.All(lines, line =>
        {
            var p = line.Split(',');
            Assert.NotEqual(string.Empty, p[1] + p[2]); // no fully empty bid/ask lines
        });
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GenerateAsync_CandleCsv_MetaTraderTemplate_OmitsHeaderAndFormats()
    {
        var dayStart = new DateTimeOffset(2025, 1, 14, 0, 0, 0, TimeSpan.Zero);
        PopulateMinuteCache(dayStart);

        var exportRoot = Path.Combine(_cacheRoot, "mt5-candles");
        var download = CreateDownloadOptions(dayStart, dayStart.AddDays(1), includeInactive: false, outputRoot: exportRoot);

        var generator = new CsvGenerator(NullLoggerFactory.Instance.CreateLogger<CsvGenerator>(), new TestLogger());
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var generation = new GenerationOptions(tz, "yyyy.MM.dd HH:mm:ss.fff", false, ExportTemplate.MetaTrader5, SpreadPoints: 1);

        await generator.GenerateAsync(download, generation, CancellationToken.None);

        var exportPath = Directory.GetFiles(exportRoot, "*.csv").Single();
        var lines = File.ReadAllLines(exportPath);

        Assert.True(lines.Length > 0);
        Assert.DoesNotContain("timestamp", lines[0], StringComparison.OrdinalIgnoreCase);

        var parts = lines[0].Split(',');
        Assert.Equal(8, parts.Length);
        Assert.True(DateTime.TryParseExact(parts[0], "yyyy.MM.dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
        Assert.Equal("0", parts[6]);      // Volume column remains 0 for FX/CFDs
        Assert.Equal("1", parts[7]);      // Spread fallback
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GenerateAsync_FromSeconds_AggregatesToCandles()
    {
        var hourStart = new DateTimeOffset(2025, 1, 14, 0, 0, 0, TimeSpan.Zero);
        PopulateTickCache(hourStart);

        var generator = new CsvGenerator(NullLoggerFactory.Instance.CreateLogger<CsvGenerator>(), new TestLogger());
        var download = CreateTickOptions(hourStart, hourStart.AddHours(1), outputRoot: Path.Combine(_cacheRoot, "exports-s1"));
        download = download with { Timeframe = DukascopyTimeframe.Second1 };

        var generation = new GenerationOptions(TimeZoneInfo.Utc, "yyyy-MM-dd HH:mm:ss");

        await generator.GenerateAsync(download, generation, CancellationToken.None);

        var exportPath = Directory.GetFiles(download.OutputDirectory!, "*.csv").Single();
        var lines = File.ReadAllLines(exportPath);

        Assert.Equal("timestamp,open,high,low,close,volume", lines[0]);
        Assert.True(lines.Length > 1);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GenerateAsync_WithNewYorkTimezone_FormatsLocalTimestamps()
    {
        var dayStart = new DateTimeOffset(2025, 1, 14, 0, 0, 0, TimeSpan.Zero);
        var exportRoot = Path.Combine(_cacheRoot, "exports");
        var download = CreateDownloadOptions(dayStart, dayStart.AddDays(1), includeInactive: false, outputRoot: exportRoot);

        PopulateMinuteCache(dayStart);

        var generator = new CsvGenerator(NullLoggerFactory.Instance.CreateLogger<CsvGenerator>(), new TestLogger());
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var generation = new GenerationOptions(tz, "yyyy-MM-dd HH:mm:ss");

        await generator.GenerateAsync(download, generation, CancellationToken.None);

        var exportPath = Path.Combine(exportRoot, "EURUSD_d1_20250114_20250114.csv");
        Assert.True(File.Exists(exportPath));

        var lines = File.ReadAllLines(exportPath);
        var row = lines[1].Split(',');
        Assert.Equal("2025-01-13 00:00:00", row[0]); // midnight local in America/New_York
    }

    private DownloadOptions CreateTickOptions(DateTimeOffset fromUtc, DateTimeOffset toUtc, string? outputRoot)
    {
        return new DownloadOptions(
            "EURUSD",
            fromUtc,
            toUtc,
            DukascopyTimeframe.Tick,
            _cacheRoot,
            outputRoot,
            UseCache: true,
            ForceRefresh: false,
            IncludeInactivePeriods: false,
            Concurrency: 1,
            MaxRetries: 0,
            RetryDelay: TimeSpan.FromSeconds(1),
            RateLimitPause: TimeSpan.FromSeconds(1),
            RateLimitRetryLimit: 1);
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

    private string ResolveTickCachePath(DateTimeOffset hourStart)
    {
        var year = hourStart.UtcDateTime.Year.ToString("D4");
        return Path.Combine(_cacheRoot, "EURUSD", year, "tick", $"{hourStart:yyyyMMdd_HH}.tick.bi5");
    }

    private string ResolveMinuteCachePath(DateTimeOffset dayStart)
    {
        var year = dayStart.UtcDateTime.Year.ToString("D4");
        return Path.Combine(_cacheRoot, "EURUSD", year, "m1", $"{dayStart:yyyyMMdd}.m1.bi5");
    }

    private void PopulateMinuteCache(DateTimeOffset dayStart)
    {
        var cachePath = ResolveMinuteCachePath(dayStart);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        var sample = Bi5TestSamples.WriteMinuteSample();
        File.Copy(sample, cachePath, overwrite: true);
        File.Delete(sample);
    }

    private void PopulateTickCache(DateTimeOffset hourStart)
    {
        var path = ResolveTickCachePath(hourStart);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var sample = Bi5TestSamples.WriteTickSample();
        File.Copy(sample, path, overwrite: true);
        File.Delete(sample);
    }

    public void Dispose()
    {
        if (Directory.Exists(_cacheRoot))
        {
            Directory.Delete(_cacheRoot, recursive: true);
        }
    }
}
