# Dukascopy Downloader

Cache-first Dukascopy BI5 downloader inspired by [dukascopy-node](https://github.com/Leo4815162342/dukascopy-node), with an emphasis on robust verification (never persisting corrupt BI5 files), higher throughput, and ready-to-use CSV exports across multiple timeframes.

**Highlights**

- Verifies every BI5 download via LZMA decode before it ever hits the cache/output mirror.
- Structured cache layout (`.dukascopy-downloader-cache/{instrument}/{year}/{tick|m1}`) that avoids churn and keeps subsequent runs fast.
- CSV generation that aggregates ticks/minutes into any Dukascopy timeframe and supports timezone/date-format overrides.
- Optional gap filling (`--include-inactive`) to synthesize flat candles during market closures.
- Clean logging, cancellation handling, and a suite of unit/integration/end-to-end tests.
- Automatic failure manifest (`download-failures.json`) stored under the cache root whenever a slice exhausts all retries, enabling fast reruns.

---

## Installation

### macOS / Linux (curl installer)

```bash
curl -fsSL https://raw.githubusercontent.com/cpcerrato/dukascopy-downloader/refs/heads/main/scripts/install.sh | sudo bash
```

To install the latest prerelease instead of the latest stable, set the environment variable for the shell executing the script:

```bash
curl -fsSL https://raw.githubusercontent.com/cpcerrato/dukascopy-downloader/refs/heads/main/scripts/install.sh \
  | sudo env INCLUDE_PRERELEASE=true bash
```

Set `INSTALL_DIR` (default: `/usr/local/bin`) or `VERSION=v1.2.3` before running to install a specific release. The script detects your OS/architecture, downloads the right archive from the latest GitHub release, and places `dukascopy-downloader` on your PATH.

### Windows (PowerShell)

```powershell
irm https://raw.githubusercontent.com/cpcerrato/dukascopy-downloader/refs/heads/main/scripts/install.ps1 | iex
```

Optional parameters: `-Version v1.2.3` and `-InstallDir "C:\Tools"`. The script fetches the zip from the latest GitHub release and copies `dukascopy-downloader.exe` to the chosen directory.

### Prebuilt archives

Each GitHub release publishes ready-to-run archives:

| Platform | Asset |
| --- | --- |
| macOS ARM | `dukascopy-downloader-<version>-osx-arm64.tar.gz` |
| Linux x64 | `dukascopy-downloader-<version>-linux-x64.tar.gz` |
| Linux ARM64 | `dukascopy-downloader-<version>-linux-arm64.tar.gz` |
| Windows x64 | `dukascopy-downloader-<version>-win-x64.zip` |

Download, extract, and place the binary anywhere on your PATH.

### Building your own binaries

Create single-file, self-contained builds for every target runtime:

```bash
./scripts/publish-all.sh          # outputs archives to ./artifacts
```

Customize runtimes via `RIDS="linux-x64 osx-arm64" ./scripts/publish-all.sh`. Set `VERSION=0.1.1 ./scripts/publish-all.sh` to override the artifact version label. The script packages each binary together with `README.md` and `LICENSE`.

### Triggering manual builds

Run the **Manual build** workflow (GitHub Actions) to execute tests and generate artifacts without tagging a release. Supply the desired version in the workflow input to mirror the archive names.

---

## Usage

```bash
dukascopy-downloader \
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

## Options

**Download options**

| Option | Description |
| --- | --- |
| `-i, --instrument` | Symbol to download (e.g. `EURUSD`). |
| `-f, --from` | Inclusive UTC start date (`YYYY-MM-DD`). |
| `-t, --to` | Inclusive UTC end date (`YYYY-MM-DD`). |
| `--timeframe` | `tick`, `s1`, `m1`, `m5`, `m15`, `m30`, `h1`, `h4`, `d1`, `mn1` (default `tick`). |
| `--cache-root` | Cache directory (default: `./.dukascopy-downloader-cache`). |
| `-o, --output` | Optional mirror folder for verified BI5 files and CSV exports (defaults to current working directory for reports). |
| `--download-only` | Only download/verify BI5 files (skip CSV generation). |
| `-c, --concurrency` | Parallel downloads (default: CPU cores − 1). |
| `--export-template` | Preset output format; `mt5` removes headers, uses `yyyy.MM.dd HH:mm:ss.fff`, and sparsifies tick columns. |
| `--include-spread` | Append a `spread` column to candle exports (non-MT5). MT5 template enables it automatically. |
| `--tick-size`, `--point` | Tick size/point value for spread calculation (bars). |
| `--infer-tick-size` | Infer tick size from tick deltas (requires cached ticks). |
| `--min-nonzero-deltas` | Minimum >0 deltas to accept inference (default `100`). |
| `--spread-points` | Fixed spread in points for bars (fallback if no tick size). |
| `--spread-agg` | Spread aggregation: `median|min|mean|last` (default `median`). |
| `--include-volume` / `--no-volume` | Keep or drop volume columns (non-MT5). MT5 always includes volume. |
| `--fixed-volume` | Fixed volume value for candles (overrides calculated tick count). |
| `--max-retries` | Retry attempts per file (default 4). |
| `--retry-delay` | Delay between retries (default `5s`). |
| `--rate-limit-pause` | Sleep duration after HTTP 429 (default `30s`). |
| `--rate-limit-retries` | Allowed rate-limit retries before failing (default 5). |
| `--force` | Ignore cache hits (forces re-download). |
| `--no-cache` | Do not read from cache (still writes). |

**Export templates**

- `--export-template mt5`:
  - Ticks (no header): `timestamp,bid,ask,last,volume`  
    - `timestamp`: `yyyy.MM.dd HH:mm:ss.fff` in the chosen timezone.  
    - `bid/ask`: Dukascopy top-of-book quotes. Cells are sparse: only written when the value changes vs. the previous tick (timestamp always present).  
    - `last`: left empty for FX/CFDs (no trade print).  
    - `volume`: left empty for FX/CFDs.  
  - Candles (no header): `timestamp,open,high,low,close,tickVolume,volume,spread`  
    - OHLC: aggregated on bid prices.  
    - `tickVolume`: number of ticks in the candle (or `--fixed-volume` when supplied).  
    - `volume`: always `0` for FX/CFDs (MT5 “real volume” is unknown).  
  - `spread`: aggregated from tick bid/ask; fallback is `1` point if no tick data is available for the candle.  
  - Recommended: use `--infer-tick-size` (or `--tick-size 0.00001` for 5-digit FX) and import the generated tick CSV into your MT5 custom symbol. MT5 will rebuild bars/spreads internally, avoiding guesses on our side.

Sin plantilla, los CSV incluyen cabeceras y usan:  
Ticks: `timestamp,ask,bid,askVolume,bidVolume`.  
Velas: `timestamp,open,high,low,close,volume`.  
Por defecto el `timestamp` es Unix ms UTC si no se pasa `--timezone/--date-format`.

**Generation options**

| Option | Description |
| --- | --- |
| `--timezone` | Output timezone for CSV timestamps. |
| `--date-format` | Custom timestamp format (e.g. `yyyy-MM-dd HH:mm:ss`). |
| `--include-inactive` | Fill closed-market periods with flat, zero-volume candles. |

**General**

| Option | Description |
| --- | --- |
| `--verbose` | Verbose logging. |
| `--version` | Print CLI version and exit. |
| `-h, --help` | Show help. |

### Notes

- `--from` / `--to` use **inclusive** UTC calendar dates (format `YYYY-MM-DD`). Internally, downloads run until the end of the `--to` day.
- Cache writes always occur; reads are skipped when `--no-cache` is supplied or when `--force` is used to bypass existing entries.
- CSV exports are saved under `cache-root/exports/{instrument}_{timeframe}_{from}_{to}.csv`.
- Timezone/date-format options affect only the CSV timestamps; the downloader always uses UTC for fetching.
- Without `--timezone`/`--date-format`, CSV `timestamp` values are emitted as Unix milliseconds (UTC). Provide either option to produce human-readable strings.
- On rate-limit responses (`HTTP 429`), all downloads pause for the configured window before retrying.
- Dukascopy sometimes serves **0-byte BI5 files** on inactive sessions (weekends, holidays). These are treated as valid “no activity” slices; combine with `--include-inactive` to synthesize flat, zero-volume candles spanning the gap using the last traded price.
- Use `--version` to print the CLI version and exit immediately.
- When downloads ultimately fail, a structured report is written to `cache-root/download-failures.json` so you can rerun or inspect the problematic slices quickly.

### Export template mapping

| Template | Applies to | Columns (no header) | Field mapping |
| --- | --- | --- | --- |
| `mt5` | Ticks | `timestamp,bid,ask,last,volume` | `timestamp` formatted as `yyyy.MM.dd HH:mm:ss.fff` in the chosen timezone; `bid/ask` from Dukascopy quotes (only written when they change vs. previous tick); `last` empty (no trade print); `volume` empty (FX/CFDs have no trade size). |
| `mt5` | Candles | `timestamp,open,high,low,close,tickVolume,volume,spread` | Bid-based OHLC aggregation; `tickVolume` = candle tick count (or `--fixed-volume` if set); `volume` is always `0` for FX/CFDs; `spread` = aggregated bid/ask spread in MT5 points (uses `--tick-size` or `--infer-tick-size`, or falls back to `--spread-points`). |

Without a template, CSVs keep headers and native Dukascopy fields:  
Ticks `timestamp,ask,bid,askVolume,bidVolume`; candles `timestamp,open,high,low,close,volume`.

Spread precedence for MT5 candles (and optional spread export):
- Use `--tick-size`/`--point` when provided (spread = round((ask-bid)/tickSize) aggregated by `--spread-agg`).
- Else `--infer-tick-size` (requires ticks; falls back to `--spread-points` if provided, otherwise errors when insufficient deltas).
- Else `--spread-points` (fixed spread for all bars).
- Otherwise, exporting candles with SPREAD fails fast (MT5 template requires one of the above).

### Examples

**Plain CSV (ticks, default UTC ms)**
```bash
dukascopy-downloader \
  --instrument EURUSD \
  --from 2024-01-01 --to 2024-01-08 \
  --timeframe tick \
  --output ./exports
```

**Plain CSV (m1, human-readable timestamp)**
```bash
dukascopy-downloader \
  --instrument EURUSD \
  --from 2024-01-01 --to 2024-01-08 \
  --timeframe m1 \
  --timezone "America/New_York" \
  --date-format "yyyy-MM-dd HH:mm:ss" \
  --output ./exports
```

**MT5 ticks (recommended: infer tick size)**
```bash
dukascopy-downloader \
  --instrument EURUSD \
  --from 2024-01-01 --to 2024-01-08 \
  --timeframe tick \
  --export-template mt5 \
  --infer-tick-size \
  --timezone "America/New_York" \
  --output ./mt5-ticks
```
Import the resulting CSV into your MT5 custom symbol; MT5 will rebuild bars/spreads internally using the real tick stream.

**MT5 candles with fixed spread (when ticks are unavailable)**
```bash
dukascopy-downloader \
  --instrument EURUSD \
  --from 2024-01-01 --to 2024-01-08 \
  --timeframe m1 \
  --export-template mt5 \
  --spread-points 10 \
  --fixed-volume 7 \
  --timezone "America/New_York" \
  --output ./mt5-bars
```

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

### Release workflow

- Update `<Version>` in `src/DukascopyDownloader.Cli/DukascopyDownloader.Cli.csproj`.
- Run `./scripts/publish-all.sh` locally for a smoke check.
- Trigger the **Manual build** workflow for CI artifacts, or use **Release from main** (workflow_dispatch) with the desired version to tag the current commit, publish artifacts, and create a GitHub release automatically.

### Benchmarking

Compare this CLI to `dukascopy-node` under the same workload:

```bash
./scripts/benchmark.sh ./dukascopy-downloader 'npx dukascopy-node'
```

Environment variables such as `FROM`, `TO`, `TIMEFRAME`, and `CONCURRENCY` adjust the scenario. The script measures cold-cache performance using `/usr/bin/time`.

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
