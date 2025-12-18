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
| `-c, --concurrency` | Parallel downloads (default: CPU cores − 1). |
| `--max-retries` | Retry attempts per file (default 4). |
| `--retry-delay` | Delay between retries (default `5s`). |
| `--rate-limit-pause` | Sleep duration after HTTP 429 (default `30s`). |
| `--rate-limit-retries` | Allowed rate-limit retries before failing (default 5). |
| `--force` | Ignore cache hits (forces re-download). |
| `--no-cache` | Do not read from cache (still writes). |

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

- Update `<Version>` in `dukascopy-downloader.csproj`.
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
