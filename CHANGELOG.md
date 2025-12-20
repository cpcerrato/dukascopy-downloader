# Changelog

## 0.1.3
- CLI help clarified: `-o/--output` also applies to CSV exports (defaults to current working directory when not provided).

## 0.1.4
- Architecture split into Core (library) and CLI (executable); solution/tests reorganized and CLI assembly named `dukascopy-downloader`.
- Logging now uses Microsoft.Extensions.Logging with typed progress events; console rendering lives in the CLI with spinner, pending counts, and color.
- Added `--prefer-ticks` to build bars from tick data (downloads tick feed if needed), enabling tick-based spread/tickVolume; warnings when spread/tick-size flags are provided without it.
- Export fixes: MT5/plain CSV rules clarified (headerless MT5, sparse ticks, spread/volume handling); output paths align to target timeframe.
- Packaging: publish script fixed to package all RIDs from CLI publish output.
- Docs refreshed (README, cli.md recipes, core-api) and tests updated for new options and target-timeframe aggregation.

## 0.1.2
- Stream tick and candle CSV exports directly to disk (no large in-memory buffers) and honor `--output`/CWD for exports.
- Default CSV timestamps are UTC Unix milliseconds unless a timezone/date format is provided.
- CLI improvements: run summary (instrument/timeframe/range/TZ), single spinner-style progress line instead of per-file logs, and download/generation/total timing plus counts.
- Added `DownloadSummary` to surface counts/durations.
- Expanded integration coverage for tick UTC vs TZ parity, Unix-ms defaults, and s1-to-candle aggregation; updated E2E/Integration tests for export paths.
- New PR CI workflow to run tests on pull requests.

## 0.1.1
- Initial release with cache-first downloads, CSV generation, and baseline test suite.
