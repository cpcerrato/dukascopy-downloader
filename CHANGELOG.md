# Changelog

## 0.1.4-beta.1 (prerelease)
- Prerelease that includes all changes since 0.1.3; use `INCLUDE_PRERELEASE=true` to install.
- Architecture split into Core (library) and CLI (executable); solution/tests reorganized and CLI assembly named `dukascopy-downloader`.
- Logging now uses Microsoft.Extensions.Logging with typed progress events; console rendering lives in the CLI with spinner, pending counts, and color.
- Added `--prefer-ticks` to build bars from tick data (downloads tick feed if needed), enabling tick-based spread/tickVolume; warnings when spread/tick-size flags are provided without it.
- Export fixes: MT5/plain CSV rules clarified (headerless MT5, sparse ticks, spread/volume handling); output paths align to target timeframe.
- Packaging: publish script fixed to package all RIDs from CLI publish output.
- Docs refreshed (README, cli.md recipes, core docs) and tests updated for new options and target-timeframe aggregation.
- CSV export filenames now reflect the last available timestamp when the requested range is truncated, with a warning that logs the actual range.
- Spread handling tightened: fixed spreads respect `--spread-points` without falling back to lower feeds; tick-based spread warns when it will download/process full tick history.
- Added `w1` timeframe support and clarified side/header options in run summaries.
- Tools: added `generate-instruments` (System.CommandLine) that combines the Dukascopy instruments JSONP API with `HistoryStart.bi5` to emit `docs/instruments.md`; snapshot files removed, markdown now cites the public API as sole source.

## 0.1.3
- CLI help clarified: `-o/--output` also applies to CSV exports (defaults to current working directory when not provided).

## 0.1.2
- Stream tick and candle CSV exports directly to disk (no large in-memory buffers) and honor `--output`/CWD for exports.
- Default CSV timestamps are UTC Unix milliseconds unless a timezone/date format is provided.
- CLI improvements: run summary (instrument/timeframe/range/TZ), single spinner-style progress line instead of per-file logs, and download/generation/total timing plus counts.
- Added `DownloadSummary` to surface counts/durations.
- Expanded integration coverage for tick UTC vs TZ parity, Unix-ms defaults, and s1-to-candle aggregation; updated E2E/Integration tests for export paths.
- New PR CI workflow to run tests on pull requests.

## 0.1.1
- Initial release with cache-first downloads, CSV generation, and baseline test suite.
