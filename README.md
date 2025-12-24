# Dukascopy Downloader (beta)

Fast, cache-first downloader for Dukascopy BI5 data with verified downloads, optional tick-based aggregation, and ready-to-import CSV exports (including an MT5 template).

> Status: beta. APIs/flags may still change; prerelease builds carry a `-beta` suffix.

## Install

**macOS / Linux**
```bash
curl -fsSL https://raw.githubusercontent.com/cpcerrato/dukascopy-downloader/refs/heads/main/scripts/install.sh | sudo bash
```
Use `INCLUDE_PRERELEASE=true` to grab the latest prerelease (recommended while the project is in beta). Override `INSTALL_DIR` or `VERSION` as needed.

**Windows**
```powershell
irm https://raw.githubusercontent.com/cpcerrato/dukascopy-downloader/refs/heads/main/scripts/install.ps1 | iex
```
Pass `-Version` and `-InstallDir` to pin a specific release.

Prebuilt archives (`*-linux-x64`, `*-osx-arm64`, `*-win-x64`, etc.) are published with every GitHub release.

## Quick start

Download and export a week of EURUSD ticks as MT5-ready CSV (New York timestamps, inferred tick size):
```bash
dukascopy-downloader \
  --instrument EURUSD \
  --timeframe tick \
  --from 2024-01-01 --to 2024-01-08 \
  --export-template mt5 \
  --infer-tick-size \
  --timezone America/New_York \
  --output ./exports
```

More examples and all flags live in the CLI docs.

## Documentation

- [CLI guide](docs/cli.md) – full flag reference, templates, and recipes.
- [Core library](docs/core.md) – architecture, API surface, and usage from .NET projects.
- [Instrument reference](docs/instruments.md) – start dates per timeframe and feed coverage for all symbols.
- [Tools](docs/tools.md) – repository utilities (e.g., instrument catalog generation).

## Development

Run tests for both projects:
```bash
dotnet test
```

Build self-contained binaries for all targets:
```bash
./scripts/publish-all.sh
```

The CLI uses the core library directly; both projects are independent and can be referenced or built separately.

## License

Licensed under the Apache License 2.0. See `LICENSE` for details.

## Disclaimers

- This project is an independent, open-source effort with **no affiliation, endorsement, or funding from Dukascopy**.
- Use at your own risk: data quality, regulatory compliance, and trading outcomes remain your responsibility.
