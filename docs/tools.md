# Tools (beta)

Repository utilities that support docs and maintenance. Current toolchain is beta; flags and defaults may evolve.

## generate-instruments

Fetches instrument metadata from the Dukascopy public JSONP API and combines it with `HistoryStart.bi5` (big-endian binary) to produce `docs/instruments.md`.

- **Inputs**
  - JSONP instruments endpoint (default: `https://freeserv.dukascopy.com/2.0/index.php?path=common%2Finstruments`, referrer `https://freeserv.dukascopy.com/`).
  - `HistoryStart.bi5` from `https://datafeed.dukascopy.com/datafeed/metadata/HistoryStart.bi5` (not LZMA-compressed; parsed directly as BE binary).
  - `--root` (optional): repository root (defaults to cwd).
  - `--url` (optional): override instruments endpoint.
  - `--out` (optional): markdown path (default `docs/instruments.md`).

- **Behavior**
  - Parses `HistoryStart.bi5` defensively (BE helpers, EOF checks, max name length 256, max period count 16).
  - For each instrument group in the JSONP payload, renders a table with start dates for tick, M1–M30, H1–H4, and D1/MN1. Dates come from `HistoryStart.bi5` when available; missing entries fall back to the JSONP fields (`history_start_*`).
  - Outputs a single markdown file; no snapshots are stored on disk.
  - Adds a “Sources: Dukascopy public instruments API” footer.

- **Run**
  ```bash
  dotnet run --project src/DukascopyDownloader.Tools generate-instruments \
    --root . \
    --out docs/instruments.md
  ```

## Notes

- All tooling is kept separate from the Core/CLI assemblies; it is for repository maintenance (docs generation) only.
- The tools project shares no public API surface with the downloader. It is safe to evolve tooling independently as long as docs remain consistent.
