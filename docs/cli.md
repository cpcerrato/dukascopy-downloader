# DukascopyDownloader CLI

Command-line tool that wraps the core library to download, verify, cache, and export Dukascopy data.

## Install

```bash
curl -fsSL https://raw.githubusercontent.com/cpcerrato/dukascopy-downloader/refs/heads/main/scripts/install.sh | sudo bash
```

For prereleases:

```bash
INCLUDE_PRERELEASE=true curl -fsSL https://raw.githubusercontent.com/cpcerrato/dukascopy-downloader/refs/heads/main/scripts/install.sh | sudo bash
```

## Usage

Common example:

```bash
dukascopy-downloader \
  --instrument EURUSD \
  --from 2024-01-01 --to 2024-01-08 \
  --timeframe tick \
  --export-template mt5 \
  --infer-tick-size \
  --timezone "America/New_York" \
  --output ./exports
```

Run `dukascopy-downloader --help` for the full list of options.

## Quick recipes

**Plain CSV ticks (UTC ms, headers)**
```bash
dukascopy-downloader --instrument EURUSD --from 2024-01-01 --to 2024-01-08 --timeframe tick --output ./exports
```

**MT5 ticks (sparse bid/ask, NY timezone, inferred tick size)**
```bash
dukascopy-downloader --instrument EURUSD --from 2024-01-01 --to 2024-01-08 \
  --timeframe tick --export-template mt5 --infer-tick-size \
  --timezone "America/New_York" --output ./mt5-ticks
```

**MT5 m1 candles (fixed spread when ticks unavailable)**
```bash
dukascopy-downloader --instrument EURUSD --from 2024-01-01 --to 2024-01-08 \
  --timeframe m1 --export-template mt5 --spread-points 10 --timezone "America/New_York" \
  --output ./mt5-bars
```

## Tests

CLI tests live under `tests/DukascopyDownloader.Cli.Tests`. Run them with:

```bash
dotnet test tests/DukascopyDownloader.Cli.Tests/DukascopyDownloader.Cli.Tests.csproj
```
