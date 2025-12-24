# DukascopyDownloader.Core (beta)

Core library that handles slice planning, verified downloads, cache mirroring, and CSV generation. The CLI consumes this package; you can also reference it directly from your own .NET projects. **Beta status:** public APIs and defaults may change before 1.0; prerelease packages carry a `-beta` suffix.

## Referencing

```bash
dotnet add <your-project>.csproj reference src/DukascopyDownloader.Core/DukascopyDownloader.Core.csproj
```
Use the `DukascopyDownloader` namespaces in your code; the CLI project shows full wiring (dependency injection, progress rendering, templates).

## Architecture

- `Download/` – timeframe planning, cache resolution, HTTP downloads with retry/rate-limit handling, BI5 verification, and optional output mirroring.
- `Generation/` – BI5 decoding, tick/minute aggregation into any Dukascopy timeframe, spread/tickVolume calculation, CSV emission.
- `Logging/` – progress snapshots (`DownloadProgressSnapshot`, `GenerationProgressSnapshot`) for hosts to render alongside `ILogger<T>`.

## Key types

### Download (namespace `DukascopyDownloader.Download`)
- `DownloadOptions` – instrument, UTC range, timeframe, cache/output paths, concurrency, retry/rate-limit policy, and cache behavior.
- `DownloadSlicePlanner.Build(options)` – yields per-hour (tick) or per-day (minute) slices.
- `DownloadOrchestrator.ExecuteAsync(options, ct)` – runs the full pipeline (plan → download → verify → mirror). Reports `DownloadProgressSnapshot` through `IProgress<T>`, throws `DownloadException` on exhausted retries, and returns `DownloadSummary`.
- `Bi5Verifier.VerifyAsync(path, ct)` – LZMA decode check before a BI5 is accepted.
- `CacheManager` – resolves cache paths and mirrors verified BI5 files to an optional output folder.

### Generation (namespace `DukascopyDownloader.Generation`)
- `Bi5Decoder.ReadTicks/ReadMinutes(path, sliceStart)` – parse Dukascopy BI5 into tick/minute records.
- `CandleAggregator.AggregateTicks/Seconds/Minutes(...)` – build candles in any Dukascopy timeframe; supports tick-based aggregation when tick size is known.
- `SpreadCalculator` / `SpreadPlanResolver` – infer or apply tick size and aggregate spreads per candle (spreads are derived from ticks; `--spread-points` forces a fixed value; `--prefer-ticks` may warn about heavier processing).
- `CandleSeriesFiller.IncludeInactivePeriods(...)` – optionally synthesize flat, zero-volume bars for closed sessions.
- `CsvGenerator.GenerateAsync(downloadOptions, generationOptions, targetTimeframe, ct)` – reads from cache, aggregates (optionally from ticks when `PreferTicks` is set), applies spread/volume rules, and writes CSV while emitting `GenerationProgressSnapshot`.
- `GenerationOptionsFactory.TryCreate(...)` – validates timezone/date-format/template/spread inputs into `GenerationOptions` (supports overriding CSV headers regardless of template).

### Logging & progress
- Use `Microsoft.Extensions.Logging.ILogger<T>` for structured logs.
- Wire `IProgress<DownloadProgressSnapshot>` / `IProgress<GenerationProgressSnapshot>` to render progress; the CLI uses console rendering, but any host can subscribe.

## Usage snippets

**Download + export**
```csharp
var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
var progress = new Progress<DownloadProgressSnapshot>(p => {/* render */});
var genProgress = new Progress<GenerationProgressSnapshot>(p => {/* render */});

var downloader = new DownloadOrchestrator(loggerFactory.CreateLogger<DownloadOrchestrator>(), progress);
var generator = new CsvGenerator(loggerFactory.CreateLogger<CsvGenerator>(), genProgress);

var downloadOptions = new DownloadOptions(/* instrument, range, cache, concurrency, retries, etc. */);
var generationOptions = GenerationOptionsFactoryDefaults.Utc; // or build via GenerationOptionsFactory.TryCreate

await downloader.ExecuteAsync(downloadOptions, CancellationToken.None);
await generator.GenerateAsync(downloadOptions, generationOptions, DukascopyTimeframe.Minute1, CancellationToken.None);
```

**Tick-based bar generation**
Set `PreferTicks = true` in `GenerationOptions` to aggregate candles from tick data (tick cache/download is used automatically), enabling tick-based spread and tickVolume computation.

## Tests

Core tests live under `tests/DukascopyDownloader.Core.Tests`:
```bash
dotnet test tests/DukascopyDownloader.Core.Tests/DukascopyDownloader.Core.Tests.csproj
```
