using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Text;
using DukascopyDownloader.Download;
using DukascopyDownloader.Logging;

namespace DukascopyDownloader.Generation;

internal sealed class CsvGenerator
{
    private readonly ConsoleLogger _logger;

    public CsvGenerator(ConsoleLogger logger)
    {
        _logger = logger;
    }

    public async Task GenerateAsync(DownloadOptions download, GenerationOptions generation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (download.Timeframe == DukascopyTimeframe.Tick)
        {
            await WriteTickCsvAsync(download, generation, cancellationToken);
            return;
        }

        if (download.Timeframe == DukascopyTimeframe.Second1)
        {
            var ticks = await LoadTicksAsync(download, cancellationToken);
            if (ticks.Count == 0)
            {
                _logger.Warn("No tick data available for the requested range; skipping CSV export.");
                return;
            }

            var candles = CandleAggregator.AggregateSeconds(ticks, generation.TimeZone);
            candles = CandleSeriesFiller.IncludeInactivePeriods(candles, download, generation.TimeZone);
            await WriteCandleCsvAsync(candles, download, generation, cancellationToken);
            return;
        }

        var minutes = await LoadMinutesAsync(download, cancellationToken);
        if (minutes.Count == 0)
        {
            _logger.Warn("No minute data available for the requested range; skipping CSV export.");
            return;
        }

        var aggregated = CandleAggregator.AggregateMinutes(minutes, download.Timeframe, generation.TimeZone);
        aggregated = CandleSeriesFiller.IncludeInactivePeriods(aggregated, download, generation.TimeZone);
        await WriteCandleCsvAsync(aggregated, download, generation, cancellationToken);
    }

    private async Task<IReadOnlyList<TickRecord>> LoadTicksAsync(DownloadOptions download, CancellationToken cancellationToken)
    {
        var cacheManager = new CacheManager(download.CacheRoot, null);
        var slices = DownloadSlicePlanner.Build(download)
            .Where(s => s.FeedKind == DukascopyFeedKind.Tick)
            .ToList();

        var results = new ConcurrentBag<TickRecord>();

        await Parallel.ForEachAsync(slices, cancellationToken, (slice, ct) =>
        {
            var path = cacheManager.ResolveCachePath(slice);
            if (!File.Exists(path))
            {
                _logger.Warn($"Tick slice missing on disk: {path}");
                return ValueTask.CompletedTask;
            }

            IReadOnlyList<TickRecord> ticks;
            try
            {
                ticks = Bi5Decoder.ReadTicks(path, slice.Start);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to decode tick file {path}: {ex.Message}");
                return ValueTask.CompletedTask;
            }

            foreach (var tick in ticks)
            {
                if (tick.TimestampUtc >= download.FromUtc && tick.TimestampUtc < download.ToUtc)
                {
                    results.Add(tick);
                }
            }

            return ValueTask.CompletedTask;
        });

        return results.OrderBy(t => t.TimestampUtc).ToList();
    }

    private async Task<IReadOnlyList<MinuteRecord>> LoadMinutesAsync(DownloadOptions download, CancellationToken cancellationToken)
    {
        var cacheManager = new CacheManager(download.CacheRoot, null);
        var slices = DownloadSlicePlanner.Build(download)
            .Where(s => s.FeedKind == DukascopyFeedKind.Minute)
            .ToList();

        var results = new ConcurrentBag<MinuteRecord>();

        await Parallel.ForEachAsync(slices, cancellationToken, (slice, ct) =>
        {
            var path = cacheManager.ResolveCachePath(slice);
            if (!File.Exists(path))
            {
                _logger.Warn($"Minute slice missing on disk: {path}");
                return ValueTask.CompletedTask;
            }

            IReadOnlyList<MinuteRecord> minutes;
            try
            {
                minutes = Bi5Decoder.ReadMinutes(path, slice.Start);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to decode minute file {path}: {ex.Message}");
                return ValueTask.CompletedTask;
            }

            foreach (var candle in minutes)
            {
                if (candle.TimestampUtc >= download.FromUtc && candle.TimestampUtc < download.ToUtc)
                {
                    results.Add(candle);
                }
            }

            return ValueTask.CompletedTask;
        });

        return results.OrderBy(c => c.TimestampUtc).ToList();
    }

    private async Task WriteTickCsvAsync(DownloadOptions download, GenerationOptions generation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cacheManager = new CacheManager(download.CacheRoot, null);
        var slices = DownloadSlicePlanner.Build(download)
            .Where(s => s.FeedKind == DukascopyFeedKind.Tick)
            .OrderBy(s => s.Start)
            .ToList();

        if (slices.Count == 0)
        {
            _logger.Warn("No tick data available for the requested range; skipping CSV export.");
            return;
        }

        var exportPath = ResolveExportPath(download);
        var rowsWritten = 0;

        {
            await using var stream = new FileStream(exportPath, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: 64 * 1024, useAsync: true);
        await using var writer = new StreamWriter(stream, Encoding.UTF8);

        if (generation.IncludeHeader)
        {
            await writer.WriteLineAsync(generation.Template == ExportTemplate.MetaTrader5
                ? "timestamp,bid,ask,volume"
                : "timestamp,ask,bid,askVolume,bidVolume");
        }

            foreach (var slice in slices)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var path = cacheManager.ResolveCachePath(slice);
                if (!File.Exists(path))
                {
                    _logger.Warn($"Tick slice missing on disk: {path}");
                    continue;
                }

                IReadOnlyList<TickRecord> ticks;
                try
                {
                    ticks = Bi5Decoder.ReadTicks(path, slice.Start);
                }
                catch (Exception ex)
                {
                    _logger.Warn($"Failed to decode tick file {path}: {ex.Message}");
                    continue;
                }

                foreach (var tick in ticks)
                {
                    if (tick.TimestampUtc < download.FromUtc || tick.TimestampUtc >= download.ToUtc)
                    {
                        continue;
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    var local = TimeZoneInfo.ConvertTime(tick.TimestampUtc, generation.TimeZone);
                string line;
                if (generation.Template == ExportTemplate.MetaTrader5)
                {
                    var volume = (tick.AskVolume + tick.BidVolume).ToString(CultureInfo.InvariantCulture);
                    line = string.Join(",",
                        FormatTimestamp(local, generation),
                        tick.Bid.ToString(CultureInfo.InvariantCulture),
                        tick.Ask.ToString(CultureInfo.InvariantCulture),
                        volume);
                }
                else
                {
                    line = string.Join(",",
                        FormatTimestamp(local, generation),
                        tick.Ask.ToString(CultureInfo.InvariantCulture),
                        tick.Bid.ToString(CultureInfo.InvariantCulture),
                        tick.AskVolume.ToString(CultureInfo.InvariantCulture),
                        tick.BidVolume.ToString(CultureInfo.InvariantCulture));
                }

                await writer.WriteLineAsync(line);
                rowsWritten++;
            }
        }
        }

        if (rowsWritten == 0)
        {
            if (File.Exists(exportPath))
            {
                File.Delete(exportPath);
            }

            _logger.Warn("No tick data available for the requested range; skipping CSV export.");
            return;
        }

        _logger.Success($"CSV written to {exportPath}");
    }

    private async Task WriteCandleCsvAsync(IReadOnlyList<CandleRecord> candles, DownloadOptions download, GenerationOptions generation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveExportPath(download);
        var rowsWritten = 0;

        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: 64 * 1024, useAsync: true);
        await using var writer = new StreamWriter(stream, Encoding.UTF8);

        if (generation.IncludeHeader)
        {
            await writer.WriteLineAsync("timestamp,open,high,low,close,volume");
        }

        foreach (var candle in candles)
        {
            var line = string.Join(",",
                FormatTimestamp(candle.LocalStart, generation),
                candle.Open.ToString(CultureInfo.InvariantCulture),
                candle.High.ToString(CultureInfo.InvariantCulture),
                candle.Low.ToString(CultureInfo.InvariantCulture),
                candle.Close.ToString(CultureInfo.InvariantCulture),
                candle.Volume.ToString(CultureInfo.InvariantCulture));

            await writer.WriteLineAsync(line);
            rowsWritten++;
        }

        if (rowsWritten == 0)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            _logger.Warn("No candle data available for the requested range; skipping CSV export.");
            return;
        }

        _logger.Success($"CSV written to {path}");
    }

    private string ResolveExportPath(DownloadOptions download)
    {
        var exportsFolder = string.IsNullOrWhiteSpace(download.OutputDirectory)
            ? Environment.CurrentDirectory
            : download.OutputDirectory!;
        Directory.CreateDirectory(exportsFolder);
        var actualEnd = download.ToUtc.AddDays(-1);
        var fileName = $"{download.Instrument}_{download.Timeframe.ToDisplayString()}_{download.FromUtc:yyyyMMdd}_{actualEnd:yyyyMMdd}.csv";
        return Path.Combine(exportsFolder, fileName);
    }

    private static string FormatTimestamp(DateTimeOffset localTime, GenerationOptions options)
    {
        if (!options.HasCustomSettings)
        {
            var utc = localTime.ToUniversalTime();
            return utc.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        }

        var format = string.IsNullOrWhiteSpace(options.DateFormat) ? "yyyy-MM-dd HH:mm:ss" : options.DateFormat!;
        return localTime.ToString(format, CultureInfo.InvariantCulture);
    }

}
