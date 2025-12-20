# DukascopyDownloader.Core API Reference (overview)

This document summarizes the public (and primary internal) surface of the core library so you can consume it programmatically. Namespaces: `DukascopyDownloader.Download`, `DukascopyDownloader.Generation`, `DukascopyDownloader.Logging`.

> Note: the library is currently internal by default and exposed to tests/CLI via `InternalsVisibleTo`. If you package it as a NuGet, mark the APIs you want to expose as `public`.

## Download module

### `enum DukascopyTimeframe`
`Tick`, `Second1`, `Minute1`, `Minute5`, `Minute15`, `Minute30`, `Hour1`, `Hour4`, `Day1`, `Month1`.

Helper:
- `GetFeedKind()` → `DukascopyFeedKind` (`Tick` or `Minute`).
- `ToDisplayString()` → `"tick"`, `"s1"`, `"m1"`, etc.

### `record DownloadOptions`
Holds download configuration:
- `Instrument` (`string`) – symbol, uppercase recommended (e.g., `EURUSD`).
- `FromUtc`, `ToUtc` (`DateTimeOffset`) – inclusive/exclusive UTC range (planner treats `ToUtc` as exclusive).
- `Timeframe` (`DukascopyTimeframe`) – desired feed/frame.
- `CacheRoot` (`string`) – path to cache root.
- `OutputDirectory` (`string?`) – optional mirror/output path.
- `UseCache`, `ForceRefresh` (`bool`) – cache behavior.
- `IncludeInactivePeriods` (`bool`) – fill gaps with flat bars (generation step).
- `Concurrency` (`int`) – parallel download workers.
- `MaxRetries` (`int`), `RetryDelay` (`TimeSpan`) – retry policy per slice.
- `RateLimitPause` (`TimeSpan`), `RateLimitRetryLimit` (`int`) – rate-limit backoff policy.

### `class DownloadSlicePlanner`
- `static IEnumerable<DownloadSlice> Build(DownloadOptions)` – partitions the requested range into BI5 slices (tick: hourly; minute: daily).

### `class CacheManager`
- `string ResolveCachePath(DownloadSlice)` – path under cache root.
- `Task SyncToOutputAsync(string cachePath, DownloadSlice slice, CancellationToken)` – mirrors verified files to output directory if configured.

### `class DownloadOrchestrator`
- `Task<DownloadSummary> ExecuteAsync(DownloadOptions, CancellationToken)` – downloads/validates all slices with retries, rate-limit handling, progress output. Throws `DownloadException` on failures after retries.

### `record DownloadSummary`
- `Total`, `NewFiles`, `CacheHits`, `Missing`, `Failed` (`int`), `Duration` (`TimeSpan`).

### `class Bi5Verifier`
- `static bool Verify(string path, DukascopyFeedKind)` – LZMA decode check to ensure BI5 file integrity (used internally before caching).

### Supporting types
- `DownloadSlice` – describes a single BI5 slice (instrument, timeframe, UTC start/end, feed kind).
- `DownloadProgress` – counters for progress reporting.
- `FailureManifest` – writes `download-failures.json` under cache root for exhausted retries.
- `RateLimitGate` – coordinates 429 pauses across workers.

## Generation module

### `class Bi5Decoder`
- `static IReadOnlyList<TickRecord> ReadTicks(string path, DateTimeOffset sliceStartUtc)`
- `static IReadOnlyList<MinuteRecord> ReadMinutes(string path, DateTimeOffset sliceStartUtc)`

### `records TickRecord / MinuteRecord / CandleRecord`
- `TickRecord`: `TimestampUtc`, `Ask`, `Bid`, `AskVolume`, `BidVolume`.
- `MinuteRecord`: `TimestampUtc`, `Open`, `High`, `Low`, `Close`, `Volume`.
- `CandleRecord`: same OHLC, plus `LocalStart` (timestamp in output TZ), `Volume`, `SpreadPoints`.

### `class CandleAggregator`
- `AggregateSeconds(IEnumerable<TickRecord>, TimeZoneInfo, decimal pipValue)` → list of second-level `CandleRecord` (volume counts ticks).
- `AggregateMinutes(IEnumerable<MinuteRecord>, DukascopyTimeframe, TimeZoneInfo)` → aggregates minute BI5 into requested timeframe; drops zero-volume bars.
- Helpers: `AlignToSecond`, `AlignToTimeframe`.

### `class CandleSeriesFiller`
- `IncludeInactivePeriods(IReadOnlyList<CandleRecord>, DownloadOptions, TimeZoneInfo)` – fills gaps with flat, zero-volume candles when `IncludeInactivePeriods` is true.

### `class SpreadCalculator`
- `decimal? InferTickSize(IEnumerable<TickRecord> ticks, int minNonZeroDeltas, out int nonZeroDeltas)` – GCD-based tick size inference on bid deltas.
- `IReadOnlyDictionary<DateTimeOffset,int> AggregateSpreads(IEnumerable<TickRecord>, DukascopyTimeframe, TimeZoneInfo, decimal tickSize, SpreadAggregation aggregation)` – computes per-bucket spreads in points.

### `class SpreadPlanResolver`
- `SpreadPlan Resolve(IReadOnlyList<TickRecord> ticks, GenerationOptions generation, DukascopyTimeframe timeframe, ConsoleLogger logger)` – decides spread source: explicit tick size, inferred tick size, or fixed spread points (with warnings/error when inference insufficient).

### `enum SpreadAggregation`
`Median` (default), `Min`, `Mean`, `Last` – used by `AggregateSpreads`.

### `class CsvGenerator`
- `Task GenerateAsync(DownloadOptions, GenerationOptions, CancellationToken)` – loads cached BI5, aggregates to timeframe, applies spread/volume rules, writes CSV with progress.
- MT5 template: headerless, timestamp `yyyy.MM.dd HH:mm:ss.fff` for ticks, sparse bid/ask (only when changed), empty last/volume for ticks; candles include tickVolume, volume=0, spread in points.
- Default template: headers, Unix ms timestamps unless a format/timezone is set.

### `record GenerationOptions`
- `TimeZone` (`TimeZoneInfo`), `DateFormat` (`string?`), `IncludeHeader` (`bool`), `Template` (`ExportTemplate`), `TickSize` (`decimal?`), `SpreadPoints` (`int?`), `InferTickSize` (`bool`), `MinNonZeroDeltas` (`int`), `SpreadAggregation`, `IncludeSpread` (`bool`), `IncludeVolume` (`bool`), `FixedVolume` (`int?`).
- `HasCustomSettings` helper.

### `class GenerationOptionsFactory`
- `TryCreate(...)` – parses/validates timezone, date format, spread/volume flags, MT5 defaults (no header, MS timestamps).

### `enum ExportTemplate`
- `None`, `MetaTrader5`.

## Logging

### `class ConsoleLogger`
- `Info`, `Success`, `Warn`, `Error`, `Verbose` – timestamped console output with color.
- `Progress(string)`, `CompleteProgressLine()` – in-place progress updates (used by downloader/generator).
- `VerboseEnabled` flag to control verbosity.

## Typical programmatic flow

1) Build `DownloadOptions` with instrument/date range/timeframe/cache.
2) Call `DownloadOrchestrator.ExecuteAsync` to populate/verify cache.
3) Build `GenerationOptions` (timezone, date format, spread/volume settings).
4) Call `CsvGenerator.GenerateAsync` to produce CSV exports from cache.

See `src/DukascopyDownloader.Cli/Program.cs` for a concrete wiring example.***
