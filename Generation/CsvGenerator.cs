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

        var needsSpread = generation.Template == ExportTemplate.MetaTrader5 || generation.IncludeSpread;

        if (download.Timeframe == DukascopyTimeframe.Second1)
        {
            var ticks = await LoadTicksAsync(download, cancellationToken);
            if (ticks.Count == 0)
            {
                _logger.Warn("No tick data available for the requested range; skipping CSV export.");
                return;
            }

            IReadOnlyDictionary<DateTimeOffset, int>? spreads = null;
            int? fixedSpread = null;
            if (needsSpread)
            {
                var spreadPlan = SpreadPlanResolver.Resolve(ticks, generation, download.Timeframe, _logger);
                spreads = spreadPlan.Mode == SpreadMode.FromTicks
                    ? SpreadCalculator.AggregateSpreads(ticks, download.Timeframe, generation.TimeZone, spreadPlan.TickSize!.Value, generation.SpreadAggregation)
                    : null;
                fixedSpread = spreadPlan.FixedSpreadPoints;
            }

            var baseTickSize = generation.TickSize ?? 0.0001m;
            var candles = CandleAggregator.AggregateSeconds(ticks, generation.TimeZone, baseTickSize);
            candles = CandleSeriesFiller.IncludeInactivePeriods(candles, download, generation.TimeZone);
            if (needsSpread)
            {
                candles = ApplySpreads(candles, spreads, fixedSpread);
            }
            await WriteCandleCsvAsync(candles, download, generation, cancellationToken);
            return;
        }

        var minutes = await LoadMinutesAsync(download, cancellationToken);
        if (minutes.Count == 0)
        {
            _logger.Warn("No minute data available for the requested range; skipping CSV export.");
            return;
        }

        IReadOnlyList<TickRecord> ticksForSpread = Array.Empty<TickRecord>();
        SpreadPlan? spreadPlanMinutes = null;
        var needVolumeCounts = generation.IncludeVolume && generation.FixedVolume is null;
        if (needsSpread)
        {
            if (generation.TickSize.HasValue || generation.InferTickSize)
            {
                ticksForSpread = await LoadTicksAsync(download with { Timeframe = DukascopyTimeframe.Tick }, cancellationToken);
            }

            spreadPlanMinutes = SpreadPlanResolver.Resolve(ticksForSpread, generation, download.Timeframe, _logger);
        }
        else if (needVolumeCounts)
        {
            // Load ticks solely to count volumes if available
            ticksForSpread = await LoadTicksAsync(download with { Timeframe = DukascopyTimeframe.Tick }, cancellationToken);
        }

        var aggregated = CandleAggregator.AggregateMinutes(minutes, download.Timeframe, generation.TimeZone);
        aggregated = CandleSeriesFiller.IncludeInactivePeriods(aggregated, download, generation.TimeZone);
        var volumeCounts = needVolumeCounts && ticksForSpread.Count > 0
            ? CountVolumes(ticksForSpread, download.Timeframe, generation.TimeZone)
            : null;
        if (needsSpread && spreadPlanMinutes is not null)
        {
            var spreads = spreadPlanMinutes.Value.Mode == SpreadMode.FromTicks && ticksForSpread.Count > 0
                ? SpreadCalculator.AggregateSpreads(ticksForSpread, download.Timeframe, generation.TimeZone, spreadPlanMinutes.Value.TickSize!.Value, generation.SpreadAggregation)
                : null;

            aggregated = ApplySpreads(aggregated, spreads, spreadPlanMinutes.Value.FixedSpreadPoints);
        }
        if (needVolumeCounts && volumeCounts is null && generation.FixedVolume is null)
        {
            _logger.Warn("Volume counts unavailable (no ticks in cache); using source candle volume.");
        }

        await WriteCandleCsvAsync(aggregated, download, generation, cancellationToken, volumeCounts);
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
                ? "timestamp,bid,ask,last,volume"
                : "timestamp,ask,bid,askVolume,bidVolume");
        }

        decimal? lastBid = null;
        decimal? lastAsk = null;

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
                    // Skip ticks where bid/ask haven't changed (volume-only change irrelevant for MT5)
                    if (lastBid.HasValue && lastAsk.HasValue && tick.Bid == lastBid.Value && tick.Ask == lastAsk.Value)
                    {
                        continue;
                    }

                    var bidField = string.Empty;
                    var askField = string.Empty;

                    if (!lastBid.HasValue || tick.Bid != lastBid.Value)
                    {
                        bidField = tick.Bid.ToString(CultureInfo.InvariantCulture);
                        lastBid = tick.Bid;
                    }

                    if (!lastAsk.HasValue || tick.Ask != lastAsk.Value)
                    {
                        askField = tick.Ask.ToString(CultureInfo.InvariantCulture);
                        lastAsk = tick.Ask;
                    }

                    // MT5 ticks expect 5 columns; leave last/volume empty for FX/CFDs
                    line = string.Join(",",
                        FormatTimestamp(local, generation),
                        bidField,
                        askField,
                        string.Empty,
                        string.Empty);
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

    private IReadOnlyDictionary<DateTimeOffset, int> CountVolumes(
        IReadOnlyList<TickRecord> ticks,
        DukascopyTimeframe timeframe,
        TimeZoneInfo timeZone)
    {
        var counts = new Dictionary<DateTimeOffset, int>();
        foreach (var tick in ticks)
        {
            var bucket = timeframe == DukascopyTimeframe.Second1
                ? CandleAggregator.AlignToSecond(tick.TimestampUtc, timeZone)
                : CandleAggregator.AlignToTimeframe(tick.TimestampUtc, timeframe, timeZone);
            if (!counts.TryGetValue(bucket, out var count))
            {
                count = 0;
            }
            counts[bucket] = count + 1;
        }
        return counts;
    }

    private async Task WriteCandleCsvAsync(
        IReadOnlyList<CandleRecord> candles,
        DownloadOptions download,
        GenerationOptions generation,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<DateTimeOffset, int>? volumeCounts = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveExportPath(download);
        var rowsWritten = 0;

        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: 64 * 1024, useAsync: true);
        await using var writer = new StreamWriter(stream, Encoding.UTF8);

        if (generation.IncludeHeader)
        {
            if (generation.Template == ExportTemplate.MetaTrader5)
            {
                await writer.WriteLineAsync("timestamp,open,high,low,close,tickVolume,volume,spread");
            }
            else
            {
                var header = "timestamp,open,high,low,close";
                if (generation.IncludeVolume)
                {
                    header += ",volume";
                }
                if (generation.IncludeSpread)
                {
                    header += ",spread";
                }
                await writer.WriteLineAsync(header);
            }
        }

        foreach (var candle in candles)
        {
            string line;
            if (generation.Template == ExportTemplate.MetaTrader5)
            {
                var baseVolumeValue = generation.FixedVolume.HasValue
                    ? generation.FixedVolume.Value
                    : volumeCounts?.TryGetValue(candle.LocalStart, out var vCount) == true
                        ? vCount
                        : candle.Volume;
                var tickVolume = baseVolumeValue.ToString(CultureInfo.InvariantCulture);
                var volume = "0"; // MT5 volume is unknown for FX/CFDs; keep 0
                var spreadPoints = candle.SpreadPoints.ToString(CultureInfo.InvariantCulture);
                line = string.Join(",",
                    FormatTimestamp(candle.LocalStart, generation),
                    candle.Open.ToString(CultureInfo.InvariantCulture),
                    candle.High.ToString(CultureInfo.InvariantCulture),
                    candle.Low.ToString(CultureInfo.InvariantCulture),
                    candle.Close.ToString(CultureInfo.InvariantCulture),
                    tickVolume,
                    volume,
                    spreadPoints);
            }
            else
            {
                var parts = new List<string>
                {
                    FormatTimestamp(candle.LocalStart, generation),
                    candle.Open.ToString(CultureInfo.InvariantCulture),
                    candle.High.ToString(CultureInfo.InvariantCulture),
                    candle.Low.ToString(CultureInfo.InvariantCulture),
                    candle.Close.ToString(CultureInfo.InvariantCulture)
                };

                if (generation.IncludeVolume)
                {
                    var volumeValue = generation.FixedVolume.HasValue
                        ? generation.FixedVolume.Value
                        : volumeCounts?.TryGetValue(candle.LocalStart, out var count) == true
                            ? count
                            : candle.Volume;
                    parts.Add(volumeValue.ToString(CultureInfo.InvariantCulture));
                }

                if (generation.IncludeSpread)
                {
                    parts.Add(candle.SpreadPoints.ToString(CultureInfo.InvariantCulture));
                }

                line = string.Join(",", parts);
            }

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

    private IReadOnlyList<CandleRecord> ApplySpreads(
        IReadOnlyList<CandleRecord> candles,
        IReadOnlyDictionary<DateTimeOffset, int>? spreads,
        int? fixedSpread)
    {
        if (spreads is null && fixedSpread is null)
        {
            return candles;
        }

        var result = new List<CandleRecord>(candles.Count);
        bool warnedMissing = false;
        int lastSpread = fixedSpread ?? 0;

        foreach (var candle in candles)
        {
            int spread = 0;
            var hasSpread = false;
            if (spreads is not null && spreads.TryGetValue(candle.LocalStart, out var s))
            {
                spread = s;
                hasSpread = true;
            }
            else if (fixedSpread is not null)
            {
                spread = fixedSpread.Value;
                hasSpread = true;
            }

            if (!hasSpread)
            {
                spread = lastSpread;
                if (!warnedMissing)
                {
                    _logger.Warn($"Missing spread for candle {candle.LocalStart:o}; reusing last known (or 0). Provide --spread-points to force a value.");
                    warnedMissing = true;
                }
            }

            lastSpread = spread;
            result.Add(candle with { SpreadPoints = spread });
        }

        return result;
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
