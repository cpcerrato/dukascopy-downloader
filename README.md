# Dukascopy Downloader

High performance .NET console app for downloading Dukascopy BI5 feeds, caching them safely, and exporting validated CSV datasets across multiple timeframes.

---

## Features

- CLI-compatible parameter set covering instruments, date ranges, timeframes (`tick`, `s1`, `m1`, `m5`, `m15`, `m30`, `h1`, `h4`, `d1`, `mn1`).
- Cache-first download strategy: verified BI5 files are never re-downloaded unless `--force` is provided.
- Structured cache layout (`.dukascopy-downloader-cache/{Instrument}/{Year}/{tick|m1}`) to avoid unwieldy directories.
- Intelligent rate-limit handling with configurable pauses and retry caps.
- CSV generation module that:
  - Aggregates ticks to `s1`, `m1`, `m5`, … `mn1`.
  - Applies custom timezone and timestamp formatting at export time.
- Optional flat-bar gap filling (`--include-inactive`) to keep candles contiguous across market closures.
- Verbose logging, graceful cancellation, and clean separation between CLI, download, generation, and logging layers.

---

## Usage

```bash
dotnet run -- \
  --instrument EURUSD \
  --from 2025-01-14 \
  --to 2025-01-18 \
  --timeframe d1 \
  --timezone "UTC" \
  --date-format "yyyy-MM-dd HH:mm:ss" \
  --cache-root "/path/to/.dukascopy-downloader-cache" \
  --output "/path/to/mirror" \
  --concurrency 4 \
  --max-retries 4 \
  --retry-delay 5s \
  --rate-limit-pause 30s \
  --rate-limit-retries 5 \
  --include-inactive \
  --verbose
```

### Notes

- `--from` / `--to` use **inclusive** UTC calendar dates (format `YYYY-MM-DD`). Internally, downloads run until the end of the `--to` day.
- Cache writes always occur; reads are skipped when `--no-cache` is supplied or when `--force` is used to bypass existing entries.
- CSV exports are saved under `cache-root/exports/{instrument}_{timeframe}_{from}_{to}.csv`.
- Timezone/date-format options affect only the CSV timestamps; the downloader always uses UTC for fetching.
- On rate-limit responses (`HTTP 429`), all downloads pause for the configured window before retrying.
- Dukascopy sometimes serves **0-byte BI5 files** on inactive sessions (weekends, holidays). These are treated as valid “no activity” slices; combine with `--include-inactive` to synthesize flat, zero-volume candles spanning the gap using the last traded price.

---

## Developer Guide

### Project Structure

```
├── Cli/                # CLI parser, usage printer, shared option model
├── Download/           # Download orchestrator, cache manager, slice planner, rate-limit gate
├── Generation/         # BI5 decoder, candle aggregators, CSV generator, option factory
├── Logging/            # Console logger abstraction
├── tests/
│   └── DukascopyDownloader.Tests/  # xUnit test suite
└── Program.cs          # Entry point wiring modules together
```

### Build

```bash
dotnet build
```

### Tests

```bash
dotnet test tests/DukascopyDownloader.Tests/DukascopyDownloader.Tests.csproj
```

> ⚠️ The built-in vstest runner requires socket permissions for inter-process communication. If the test command fails with `SocketException (Permission denied)`, rerun from an environment where TCP listeners are allowed (or use `dotnet test -- --port <allowed-port>` when running under constrained sandboxes).

### Adding Tests

- New unit tests belong under `tests/DukascopyDownloader.Tests/`.
- Favor pure unit coverage by injecting testable helpers (`CandleAggregator`, `Bi5Decoder.Parse*`, etc.).
- Integration tests (full CLI runs) should target temporary cache roots to avoid polluting shared directories.

### Coding Standards

- Public CLI surface: English help text, ISO formats, UTC default timezone.
- Avoid re-downloading BI5 files whenever cache entries exist; verification already occurred on first download.
- Keep modules decoupled (CLI ↔ download ↔ generation) so they can be extended independently.

---

## License

Licensed under the [Apache License 2.0](LICENSE).
