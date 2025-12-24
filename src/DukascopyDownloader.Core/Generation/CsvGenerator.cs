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

    /// <summary>
    /// Creates a CSV generator that reads cached BI5 data and emits CSV exports while reporting progress.
    /// </summary>
    /// <param name="logger">Logger for informational/warning/error output.</param>
    /// <param name="progress">Optional progress sink; <see cref="NullProgress{T}"/> is used when null.</param>
    public CsvGenerator(ILogger<CsvGenerator> logger, IProgress<GenerationProgressSnapshot>? progress = null)
    {
        _logger = logger;
        _progress = progress ?? NullProgress<GenerationProgressSnapshot>.Instance;
    }

    /// <summary>
    /// Generates a CSV for the requested timeframe using cached downloads (or ticks when preferred).
    /// </summary>
    /// <param name="download">Download options describing instrument/range/cache.</param>
    /// <param name="generation">Generation options (timezone, format, template, spreads/volume).</param>
    /// <param name="targetTimeframe">Timeframe to export.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when required data is missing or options are inconsistent.</exception>
    public async Task GenerateAsync(DownloadOptions download, GenerationOptions generation, DukascopyTimeframe targetTimeframe, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var needsSpread = generation.Template == ExportTemplate.MetaTrader5 || generation.IncludeSpread;
        var preferTicks = generation.PreferTicks && targetTimeframe != DukascopyTimeframe.Tick;
        var exportSides = ResolveSides(download.SidePreference);
        var feedKind = targetTimeframe.GetFeedKind();

        // Tick timeframe: write ticks directly from cache.
        if (targetTimeframe == DukascopyTimeframe.Tick)
        {
            var tickDownload = download.Timeframe == DukascopyTimeframe.Tick ? download : download with { Timeframe = DukascopyTimeframe.Tick };
            await WriteTickCsvAsync(tickDownload, generation, cancellationToken);
            return;
        }

        // 1-second candles always come from ticks.
        if (targetTimeframe == DukascopyTimeframe.Second1)
        {
            await GenerateSecondsFromTicksAsync(download, generation, needsSpread, cancellationToken);
            return;
        }

        // Prefer ticks: aggregate candles from tick stream.
        if (preferTicks)
        {
            await GenerateFromTicksAsync(download, generation, targetTimeframe, needsSpread, cancellationToken);
            return;
        }

        var dataDownload = download.Timeframe == targetTimeframe ? download : download with { Timeframe = targetTimeframe };
        var candleSets = new Dictionary<DukascopyPriceSide, IReadOnlyList<MinuteRecord>>();
        var allowFallback = !generation.PreferTicks &&
                            !(generation.SpreadPoints.HasValue && generation.SpreadPoints.Value == 0);
        var feedOrder = BuildFeedSearchOrder(feedKind, allowFallback).ToList();

        foreach (var side in exportSides)
        {
            IReadOnlyList<MinuteRecord> candles = Array.Empty<MinuteRecord>();
            foreach (var candidateFeed in feedOrder)
            {
                var loadOptions = dataDownload with { Timeframe = TimeframeForFeed(candidateFeed) };
                candles = await LoadCandlesAsync(
                    loadOptions,
                    candidateFeed,
                    side,
                    cancellationToken,
                    showProgress: true,
                    phase: $"Loading {side.ToString().ToLowerInvariant()} {candidateFeed.ToString().ToLowerInvariant()} candles");

                if (candles.Count > 0)
                {
                    break;
                }
            }

            if (candles.Count == 0)
            {
                _logger.LogWarning("{Side} feed has no data for the requested range.", side);
                continue;
            }

            candleSets[side] = candles;
        }

        if (candleSets.Count == 0)
        {
            _logger.LogWarning("No candle data available for the requested range; skipping CSV export.");
            return;
        }

        IReadOnlyDictionary<DateTimeOffset, int>? spreads = null;
        int? fixedSpread = null;
        IReadOnlyList<TickRecord> ticksForSpread = Array.Empty<TickRecord>();
        if (needsSpread)
        {
            var needTicksForSpread = generation.TickSize.HasValue || generation.InferTickSize;
            if (needTicksForSpread)
            {
                ticksForSpread = await LoadTicksAsync(
                    download with { Timeframe = DukascopyTimeframe.Tick },
                    cancellationToken,
                    showProgress: true,
                    phase: "Loading ticks (spread)");
            }

            var spreadPlan = SpreadPlanResolver.Resolve(ticksForSpread, generation, targetTimeframe, _logger);
            if (spreadPlan.Mode == SpreadMode.Fixed)
            {
                fixedSpread = spreadPlan.FixedSpreadPoints;
            }
            else if (spreadPlan.Mode == SpreadMode.FromTicks && spreadPlan.TickSize is not null && ticksForSpread.Count > 0)
            {
                spreads = SpreadCalculator.AggregateSpreads(
                    ticksForSpread,
                    targetTimeframe,
                    generation.TimeZone,
                    spreadPlan.TickSize.Value,
                    generation.SpreadAggregation);
            }
        }

        foreach (var side in exportSides)
        {
            if (!candleSets.TryGetValue(side, out var source) || source.Count == 0)
            {
                continue;
            }

            var aggregated = AggregateCandles(source, targetTimeframe, dataDownload, generation);
            var withSpreads = needsSpread ? ApplySpreads(aggregated, spreads, fixedSpread) : aggregated;
            var sideForPath = exportSides.Count > 1 ? side : (DukascopyPriceSide?)null;
            await WriteCandleCsvAsync(withSpreads, dataDownload, generation, targetTimeframe, cancellationToken, volumeCounts: null, side: sideForPath);
        }
    }

    private async Task GenerateSecondsFromTicksAsync(
        DownloadOptions download,
        GenerationOptions generation,
        bool needsSpread,
        CancellationToken cancellationToken)
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
            var spreadPlan = SpreadPlanResolver.Resolve(ticks, generation, DukascopyTimeframe.Second1, _logger);
            secondSpreads = spreadPlan.Mode == SpreadMode.FromTicks
                ? SpreadCalculator.AggregateSpreads(ticks, DukascopyTimeframe.Second1, generation.TimeZone, spreadPlan.TickSize!.Value, generation.SpreadAggregation)
                : null;
            secondFixedSpread = spreadPlan.FixedSpreadPoints;
            if (spreadPlan.TickSize.HasValue)
            {
                baseTickSize = spreadPlan.TickSize.Value;
            }
        }

        var secondsDownload = tickDownload with { Timeframe = DukascopyTimeframe.Second1 };
        var candles = CandleAggregator.AggregateSeconds(ticks, generation.TimeZone, baseTickSize);
        candles = CandleSeriesFiller.IncludeInactivePeriods(candles, secondsDownload, generation.TimeZone);
        if (needsSpread)
        {
            candles = ApplySpreads(candles, secondSpreads, secondFixedSpread);
        }
        await WriteCandleCsvAsync(candles, secondsDownload, generation, DukascopyTimeframe.Second1, cancellationToken, side: null);
    }

    private async Task GenerateFromTicksAsync(
        DownloadOptions download,
        GenerationOptions generation,
        DukascopyTimeframe targetTimeframe,
        bool needsSpread,
        CancellationToken cancellationToken)
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
        aggregated = CandleSeriesFiller.IncludeInactivePeriods(aggregated, tickDownload with { Timeframe = targetTimeframe }, generation.TimeZone);

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

        await WriteCandleCsvAsync(aggregated, tickDownload with { Timeframe = targetTimeframe }, generation, targetTimeframe, cancellationToken, volumeCounts, side: null);
    }

    private IReadOnlyList<CandleRecord> AggregateCandles(
        IReadOnlyList<MinuteRecord> candles,
        DukascopyTimeframe targetTimeframe,
        DownloadOptions download,
        GenerationOptions generation)
    {
        var aggregated = CandleAggregator.AggregateMinutes(candles, targetTimeframe, generation.TimeZone);
        aggregated = CandleSeriesFiller.IncludeInactivePeriods(aggregated, download with { Timeframe = targetTimeframe }, generation.TimeZone);
        return aggregated;
    }

    private IReadOnlyList<DukascopyPriceSide> ResolveSides(PriceSidePreference preference) =>
        preference switch
        {
            PriceSidePreference.Bid => new[] { DukascopyPriceSide.Bid },
            PriceSidePreference.Ask => new[] { DukascopyPriceSide.Ask },
            _ => new[] { DukascopyPriceSide.Bid, DukascopyPriceSide.Ask }
        };

    private static IEnumerable<DukascopyFeedKind> BuildFeedSearchOrder(DukascopyFeedKind primary, bool allowFallback) =>
        allowFallback
            ? primary switch
            {
                DukascopyFeedKind.Day => new[] { DukascopyFeedKind.Day, DukascopyFeedKind.Hour, DukascopyFeedKind.Minute },
                DukascopyFeedKind.Hour => new[] { DukascopyFeedKind.Hour, DukascopyFeedKind.Minute },
                _ => new[] { primary }
            }
            : new[] { primary };

    private static DukascopyTimeframe TimeframeForFeed(DukascopyFeedKind feedKind) =>
        feedKind switch
        {
            DukascopyFeedKind.Tick => DukascopyTimeframe.Tick,
            DukascopyFeedKind.Hour => DukascopyTimeframe.Hour1,
            DukascopyFeedKind.Day => DukascopyTimeframe.Day1,
            _ => DukascopyTimeframe.Minute1
        };

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

    private async Task<IReadOnlyList<MinuteRecord>> LoadCandlesAsync(
        DownloadOptions download,
        DukascopyFeedKind feedKind,
        DukascopyPriceSide side,
        CancellationToken cancellationToken,
        bool showProgress = false,
        string phase = "Loading candles")
    {
        var cacheManager = new CacheManager(download.CacheRoot, null);
        var slices = DownloadSlicePlanner.Build(download)
            .Where(s => s.FeedKind == feedKind && s.Side == side)
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
                 _logger.LogWarning($"Candle slice missing on disk: {path}");
                return ValueTask.CompletedTask;
            }

            IReadOnlyList<MinuteRecord> minutes;
            try
            {
                minutes = Bi5Decoder.ReadCandles(path, slice.Start);
            }
            catch (Exception ex)
            {
                 _logger.LogWarning($"Failed to decode candle file {path}: {ex.Message}");
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
        DateTimeOffset? firstTickUtc = null;
        DateTimeOffset? lastTickUtc = null;

        await using (var stream = new FileStream(exportPath, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: 64 * 1024, useAsync: true))
        await using (var writer = new StreamWriter(stream, Encoding.UTF8))
        {
            if (generation.IncludeHeader)
            {
                await writer.WriteLineAsync(generation.Template == ExportTemplate.MetaTrader5
                    ? "timestamp,bid,ask,last,volume"
                    : "timestamp,bid,ask,bidVolume,askVolume");
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
                            tick.Bid.ToString(CultureInfo.InvariantCulture),
                            tick.Ask.ToString(CultureInfo.InvariantCulture),
                            tick.BidVolume.ToString(CultureInfo.InvariantCulture),
                            tick.AskVolume.ToString(CultureInfo.InvariantCulture));
                    }

                    await writer.WriteLineAsync(line);
                    rowsWritten++;
                    firstTickUtc ??= tick.TimestampUtc;
                    lastTickUtc = tick.TimestampUtc;
                }

                processedSlices++;
                RenderGenerationProgress(totalSlices, processedSlices, "Writing ticks");
            }
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

        var desiredPath = exportPath;
        if (firstTickUtc.HasValue && lastTickUtc.HasValue)
        {
            desiredPath = ResolveExportPath(
                download,
                targetTimeframe: download.Timeframe,
                side: null,
                actualStartUtc: firstTickUtc,
                actualEndUtc: lastTickUtc);
            LogTrimmedRange(download, firstTickUtc, lastTickUtc);
        }

        if (!string.Equals(desiredPath, exportPath, StringComparison.Ordinal))
        {
            File.Move(exportPath, desiredPath, overwrite: true);
            exportPath = desiredPath;
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
        IReadOnlyDictionary<DateTimeOffset, int>? volumeCounts = null,
        DukascopyPriceSide? side = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DateTimeOffset? actualStartUtc = null;
        DateTimeOffset? actualEndUtc = null;
        if (candles.Count > 0)
        {
            actualStartUtc = candles.First().LocalStart.ToUniversalTime();
            actualEndUtc = candles.Last().LocalStart.ToUniversalTime();
        }

        var path = ResolveExportPath(download, targetTimeframe, side, actualStartUtc, actualEndUtc);
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

        LogTrimmedRange(download, actualStartUtc, actualEndUtc);

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

    private string ResolveExportPath(
        DownloadOptions download,
        DukascopyTimeframe targetTimeframe,
        DukascopyPriceSide? side = null,
        DateTimeOffset? actualStartUtc = null,
        DateTimeOffset? actualEndUtc = null)
    {
        var exportsFolder = string.IsNullOrWhiteSpace(download.OutputDirectory)
            ? Environment.CurrentDirectory
            : download.OutputDirectory!;
        Directory.CreateDirectory(exportsFolder);
        var startUtc = actualStartUtc.HasValue && actualStartUtc.Value > download.FromUtc
            ? actualStartUtc.Value
            : download.FromUtc;
        var endUtc = actualEndUtc ?? download.ToUtc.AddDays(-1);
        if (endUtc < startUtc)
        {
            endUtc = startUtc;
        }
        var sideSuffix = side is null ? string.Empty : $"_{side.Value.ToString().ToLowerInvariant()}";
        var fileName = $"{download.Instrument}_{targetTimeframe.ToDisplayString()}{sideSuffix}_{startUtc.UtcDateTime:yyyyMMdd}_{endUtc.UtcDateTime:yyyyMMdd}.csv";
        return Path.Combine(exportsFolder, fileName);
    }

    private void LogTrimmedRange(DownloadOptions download, DateTimeOffset? actualStartUtc, DateTimeOffset? actualEndUtc)
    {
        if (!actualStartUtc.HasValue && !actualEndUtc.HasValue)
        {
            return;
        }

        var requestedStart = download.FromUtc;
        var requestedEndInclusive = download.ToUtc.AddDays(-1);
        var finalStart = actualStartUtc ?? requestedStart;
        var finalEnd = actualEndUtc ?? requestedEndInclusive;

        var truncatedStart = finalStart > requestedStart;
        var truncatedEnd = finalEnd < requestedEndInclusive;

        if (truncatedStart || truncatedEnd)
        {
             _logger.LogWarning(
                "Requested range {RequestedStart:u}..{RequestedEnd:u} trimmed to available data {ActualStart:u}..{ActualEnd:u}; export filename reflects the available range.",
                requestedStart,
                requestedEndInclusive,
                finalStart,
                finalEnd);
        }
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
