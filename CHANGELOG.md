# Changelog

## 0.1.2
- Stream tick and candle CSV exports directly to disk (no large in-memory buffers) and honor `--output`/CWD for exports.
- Default CSV timestamps are UTC Unix milliseconds unless a timezone/date format is provided.
- CLI improvements: run summary (instrument/timeframe/range/TZ), single spinner-style progress line instead of per-file logs, and download/generation/total timing plus counts.
- Added `DownloadSummary` to surface counts/durations.
- Expanded integration coverage for tick UTC vs TZ parity, Unix-ms defaults, and s1-to-candle aggregation; updated E2E/Integration tests for export paths.
- New PR CI workflow to run tests on pull requests.

## 0.1.1
- Initial release with cache-first downloads, CSV generation, and baseline test suite.
