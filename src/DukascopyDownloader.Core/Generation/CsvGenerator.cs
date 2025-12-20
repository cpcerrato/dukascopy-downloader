using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using DukascopyDownloader.Core.Logging;

using DukascopyDownloader.Download;
using Microsoft.Extensions.Logging;

namespace DukascopyDownloader.Generation;

internal sealed class CsvGenerator
{
    private readonly ILogger<CsvGenerator> _logger;
    private readonly IProgress<GenerationProgressSnapshot> _progress;
    private long _lastProgressTick;

    public CsvGenerator(ILogger<CsvGenerator> logger, IProgress<GenerationProgressSnapshot>? progress = null)
    {
        _logger = logger;
        _progress = progress ?? NullProgress<GenerationProgressSnapshot>.Instance;
    }

    public async Task GenerateAsync(DownloadOptions download, GenerationOptions generation, DukascopyTimeframe targetTimeframe, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Tick timeframe: write ticks directly from cache.
        if (targetTimeframe == DukascopyTimeframe.Tick)
        {
            await WriteTickCsvAsync(download, generation, cancellationToken);
            return;
        }

        var needsSpread = generation.Template == ExportTemplate.MetaTrader5 || generation.IncludeSpread;
        var preferTicks = generation.PreferTicks;

        if (targetTimeframe == DukascopyTimeframe.Second1)
        {
            var tickDownload = download.Timeframe == DukascopyTimeframe.Tick ? download : download with { Timeframe = DukascopyTimeframe.Tick };
            var ticks = await LoadTicksAsync(tickDownload, cancellationToken, showProgress: true, phase: "Loading ticks");
            if (ticks.Count == 0)
            {
                throw new InvalidOperationException("No tick data available to build 1-second candles. Use --prefer-ticks to download ticks or ensure ticks are cached.");
            }

            IReadOnlyDictionary<DateTimeOffset, int>? secondSpreads = null;
            int? secondFixedSpread = null;
            var baseTickSize = generation.TickSize ?? 0.0001m;
            if (needsSpread)
            {
                var spreadPlan = SpreadPlanResolver.Resolve(ticks, generation, download.Timeframe, _logger);
                secondSpreads = spreadPlan.Mode == SpreadMode.FromTicks
                    ? SpreadCalculator.AggregateSpreads(ticks, download.Timeframe, generation.TimeZone, spreadPlan.TickSize!.Value, generation.SpreadAggregation)
                    : null;
                secondFixedSpread = spreadPlan.FixedSpreadPoints;
                if (spreadPlan.TickSize.HasValue)
                {
                    baseTickSize = spreadPlan.TickSize.Value;
                }
            }

            var candles = CandleAggregator.AggregateSeconds(ticks, generation.TimeZone, baseTickSize);
            candles = CandleSeriesFiller.IncludeInactivePeriods(candles, download, generation.TimeZone);
            if (needsSpread)
            {
                candles = ApplySpreads(candles, secondSpreads, secondFixedSpread);
            }
            await WriteCandleCsvAsync(candles, download, generation, targetTimeframe, cancellationToken);
            return;
        }

        // Prefer ticks: aggregate candles from tick stream, otherwise use Dukascopy minute feed.
        if (preferTicks)
        {
            var tickDownload = download.Timeframe == DukascopyTimeframe.Tick ? download : download with { Timeframe = DukascopyTimeframe.Tick };
            var ticks = await LoadTicksAsync(tickDownload, cancellationToken, showProgress: true, phase: "Loading ticks");
            if (ticks.Count == 0)
            {
                throw new InvalidOperationException("Prefer-ticks requested but no tick data available. Ensure ticks are cached or let the downloader fetch ticks for this range.");
            }

            SpreadPlan? spreadPlanTicks = null;

            if (needsSpread || generation.IncludeVolume)
            {
                if (generation.TickSize.HasValue || generation.InferTickSize)
                {
                    spreadPlanTicks = SpreadPlanResolver.Resolve(ticks, generation, targetTimeframe, _logger);
                }
                else if (generation.SpreadPoints.HasValue)
                {
                    spreadPlanTicks = SpreadPlan.Fixed(generation.SpreadPoints.Value);
                }
            }

            var pip = spreadPlanTicks?.TickSize ?? generation.TickSize ?? 0.0001m;
            var aggregated = CandleAggregator.AggregateTicks(ticks, targetTimeframe, generation.TimeZone, pip);
            aggregated = CandleSeriesFiller.IncludeInactivePeriods(aggregated, download with { Timeframe = targetTimeframe }, generation.TimeZone);

            IReadOnlyDictionary<DateTimeOffset, int>? volumeCounts = null;
            if (generation.IncludeVolume && generation.FixedVolume is null)
            {
                volumeCounts = CountVolumes(ticks, targetTimeframe, generation.TimeZone);
            }

            if (needsSpread && spreadPlanTicks is not null)
            {
                var tickSpreadsAgg = spreadPlanTicks.Value.Mode == SpreadMode.FromTicks
                    ? SpreadCalculator.AggregateSpreads(ticks, targetTimeframe, generation.TimeZone, spreadPlanTicks.Value.TickSize!.Value, generation.SpreadAggregation)
                    : null;
                aggregated = ApplySpreads(aggregated, tickSpreadsAgg, spreadPlanTicks.Value.FixedSpreadPoints);
            }

            await WriteCandleCsvAsync(aggregated, download, generation, targetTimeframe, cancellationToken, volumeCounts);
            return;
        }

        var minutes = await LoadMinutesAsync(download, cancellationToken, showProgress: true, phase: "Loading minutes");
        if (minutes.Count == 0)
        {
            _logger.LogWarning("No minute data available for the requested range; skipping CSV export.");
            return;
        }

        IReadOnlyList<TickRecord> ticksForSpread = Array.Empty<TickRecord>();
        SpreadPlan? spreadPlanMinutes = null;
        var needVolumeCounts = generation.IncludeVolume && generation.FixedVolume is null;
        if (needsSpread)
        {
            if (generation.TickSize.HasValue || generation.InferTickSize)
            {
                ticksForSpread = await LoadTicksAsync(download with { Timeframe = DukascopyTimeframe.Tick }, cancellationToken, showProgress: true, phase: "Loading ticks (spreads)");
            }

            spreadPlanMinutes = SpreadPlanResolver.Resolve(ticksForSpread, generation, targetTimeframe, _logger);
        }
        else if (needVolumeCounts)
        {
            // Load ticks solely to count volumes if available
            ticksForSpread = await LoadTicksAsync(download with { Timeframe = DukascopyTimeframe.Tick }, cancellationToken, showProgress: true, phase: "Loading ticks (volume)");
        }

        var aggregatedMinutes = CandleAggregator.AggregateMinutes(minutes, targetTimeframe, generation.TimeZone);
        aggregatedMinutes = CandleSeriesFiller.IncludeInactivePeriods(aggregatedMinutes, download with { Timeframe = targetTimeframe }, generation.TimeZone);
        var volumeCountsMinutes = needVolumeCounts && ticksForSpread.Count > 0
            ? CountVolumes(ticksForSpread, targetTimeframe, generation.TimeZone)
            : null;
        if (needsSpread && spreadPlanMinutes is not null)
        {
            var spreads = spreadPlanMinutes.Value.Mode == SpreadMode.FromTicks && ticksForSpread.Count > 0
                ? SpreadCalculator.AggregateSpreads(ticksForSpread, targetTimeframe, generation.TimeZone, spreadPlanMinutes.Value.TickSize!.Value, generation.SpreadAggregation)
                : null;

            aggregatedMinutes = ApplySpreads(aggregatedMinutes, spreads, spreadPlanMinutes.Value.FixedSpreadPoints);
        }
        if (needVolumeCounts && volumeCountsMinutes is null && generation.FixedVolume is null)
        {
            _logger.LogWarning("Volume counts unavailable (no ticks in cache); using source candle volume.");
        }

        await WriteCandleCsvAsync(aggregatedMinutes, download, generation, targetTimeframe, cancellationToken, volumeCountsMinutes);
    }

    private async Task<IReadOnlyList<TickRecord>> LoadTicksAsync(
        DownloadOptions download,
        CancellationToken cancellationToken,
        bool showProgress = false,
        string phase = "Loading ticks")
    {
        var cacheManager = new CacheManager(download.CacheRoot, null);
        var slices = DownloadSlicePlanner.Build(download)
            .Where(s => s.FeedKind == DukascopyFeedKind.Tick)
            .ToList();
        var total = slices.Count;
        var processed = 0;

        if (showProgress && total > 0)
        {
            RenderGenerationProgress(total, processed, phase, force: true);
        }

        var results = new ConcurrentBag<TickRecord>();

        await Parallel.ForEachAsync(slices, cancellationToken, (slice, ct) =>
        {
            var path = cacheManager.ResolveCachePath(slice);
            if (!File.Exists(path))
            {
                 _logger.LogWarning($"Tick slice missing on disk: {path}");
                return ValueTask.CompletedTask;
            }

            IReadOnlyList<TickRecord> ticks;
            try
            {
                ticks = Bi5Decoder.ReadTicks(path, slice.Start);
            }
            catch (Exception ex)
            {
                 _logger.LogWarning($"Failed to decode tick file {path}: {ex.Message}");
                if (showProgress)
                {
                    var done = Interlocked.Increment(ref processed);
                    RenderGenerationProgress(total, done, phase);
                }
                return ValueTask.CompletedTask;
            }

            foreach (var tick in ticks)
            {
                if (tick.TimestampUtc >= download.FromUtc && tick.TimestampUtc < download.ToUtc)
                {
                    results.Add(tick);
                }
            }

            if (showProgress)
            {
                var done = Interlocked.Increment(ref processed);
                RenderGenerationProgress(total, done, phase);
            }

            return ValueTask.CompletedTask;
        });

        if (showProgress && total > 0)
        {
            RenderGenerationProgress(total, processed, phase, force: true, isFinal: true);
        }

        return results.OrderBy(t => t.TimestampUtc).ToList();
    }

    private async Task<IReadOnlyList<MinuteRecord>> LoadMinutesAsync(
        DownloadOptions download,
        CancellationToken cancellationToken,
        bool showProgress = false,
        string phase = "Loading minutes")
    {
        var cacheManager = new CacheManager(download.CacheRoot, null);
        var slices = DownloadSlicePlanner.Build(download)
            .Where(s => s.FeedKind == DukascopyFeedKind.Minute)
            .ToList();
        var total = slices.Count;
        var processed = 0;

        if (showProgress && total > 0)
        {
            RenderGenerationProgress(total, processed, phase, force: true);
        }

        var results = new ConcurrentBag<MinuteRecord>();

        await Parallel.ForEachAsync(slices, cancellationToken, (slice, ct) =>
        {
            var path = cacheManager.ResolveCachePath(slice);
            if (!File.Exists(path))
            {
                 _logger.LogWarning($"Minute slice missing on disk: {path}");
                return ValueTask.CompletedTask;
            }

            IReadOnlyList<MinuteRecord> minutes;
            try
            {
                minutes = Bi5Decoder.ReadMinutes(path, slice.Start);
            }
            catch (Exception ex)
            {
                 _logger.LogWarning($"Failed to decode minute file {path}: {ex.Message}");
                if (showProgress)
                {
                    var done = Interlocked.Increment(ref processed);
                    RenderGenerationProgress(total, done, phase);
                }
                return ValueTask.CompletedTask;
            }

            foreach (var candle in minutes)
            {
                if (candle.TimestampUtc >= download.FromUtc && candle.TimestampUtc < download.ToUtc)
                {
                    results.Add(candle);
                }
            }

            if (showProgress)
            {
                var done = Interlocked.Increment(ref processed);
                RenderGenerationProgress(total, done, phase);
            }

            return ValueTask.CompletedTask;
        });

        if (showProgress && total > 0)
        {
            RenderGenerationProgress(total, processed, phase, force: true, isFinal: true);
        }

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
        var totalSlices = slices.Count;
        var processedSlices = 0;

        if (slices.Count == 0)
        {
             _logger.LogWarning("No tick data available for the requested range; skipping CSV export.");
            return;
        }

        var exportPath = ResolveExportPath(download, targetTimeframe: download.Timeframe);
        var rowsWritten = 0;
        RenderGenerationProgress(totalSlices, processedSlices, "Writing ticks", force: true);

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
                 _logger.LogWarning($"Tick slice missing on disk: {path}");
                processedSlices++;
                RenderGenerationProgress(totalSlices, processedSlices, "Writing ticks");
                continue;
            }

            IReadOnlyList<TickRecord> ticks;
            try
            {
                ticks = Bi5Decoder.ReadTicks(path, slice.Start);
            }
            catch (Exception ex)
            {
                 _logger.LogWarning($"Failed to decode tick file {path}: {ex.Message}");
                processedSlices++;
                RenderGenerationProgress(totalSlices, processedSlices, "Writing ticks");
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

            processedSlices++;
            RenderGenerationProgress(totalSlices, processedSlices, "Writing ticks");
        }

        RenderGenerationProgress(totalSlices, totalSlices, "Writing ticks", force: true, isFinal: true);

        if (rowsWritten == 0)
        {
            if (File.Exists(exportPath))
            {
                File.Delete(exportPath);
            }

             _logger.LogWarning("No tick data available for the requested range; skipping CSV export.");
            return;
        }

         _logger.LogInformation($"CSV written to {exportPath}");
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
        DukascopyTimeframe targetTimeframe,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<DateTimeOffset, int>? volumeCounts = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveExportPath(download, targetTimeframe);
        var rowsWritten = 0;
        var total = candles.Count;

        if (total > 0)
        {
            RenderGenerationProgress(total, 0, "Writing candles", force: true);
        }

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

            if (total > 0)
            {
                RenderGenerationProgress(total, rowsWritten, "Writing candles");
            }
        }

        if (total > 0)
        {
            RenderGenerationProgress(total, total, "Writing candles", force: true, isFinal: true);
        }

        if (rowsWritten == 0)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

             _logger.LogWarning("No candle data available for the requested range; skipping CSV export.");
            return;
        }

         _logger.LogInformation($"CSV written to {path}");
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
                     _logger.LogWarning($"Missing spread for candle {candle.LocalStart:o}; reusing last known (or 0). Provide --spread-points to force a value.");
                    warnedMissing = true;
                }
            }

            lastSpread = spread;
            result.Add(candle with { SpreadPoints = spread });
        }

        return result;
    }

    private string ResolveExportPath(DownloadOptions download, DukascopyTimeframe targetTimeframe)
    {
        var exportsFolder = string.IsNullOrWhiteSpace(download.OutputDirectory)
            ? Environment.CurrentDirectory
            : download.OutputDirectory!;
        Directory.CreateDirectory(exportsFolder);
        var actualEnd = download.ToUtc.AddDays(-1);
        var fileName = $"{download.Instrument}_{targetTimeframe.ToDisplayString()}_{download.FromUtc:yyyyMMdd}_{actualEnd:yyyyMMdd}.csv";
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

    private void RenderGenerationProgress(int total, int completed, string phase, bool force = false, bool isFinal = false)
    {
        if (total <= 0)
        {
            return;
        }

        var percent = total == 0 ? 100 : (int)Math.Round((double)completed * 100 / total);
        var now = Stopwatch.GetTimestamp();
        var elapsedMs = now * 1000 / Stopwatch.Frequency;
        if (!force && elapsedMs - _lastProgressTick < 200)
        {
            return;
        }

        _lastProgressTick = elapsedMs;
        _progress.Report(new GenerationProgressSnapshot(
            total,
            completed,
            phase,
            isFinal));
    }
}
