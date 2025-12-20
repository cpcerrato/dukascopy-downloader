# DukascopyDownloader.Core

Core library that handles downloads, cache verification, and CSV generation. This is the logic consumed by the CLI; you can also reference the project from your own .NET solutions if you need programmatic access.

## Structure

- `Download/` – slice planning, cache management, HTTP downloads, rate-limit handling, verification.
- `Generation/` – BI5 decoding, candle aggregation, spread/volume calculations, CSV generation.
- `Logging/` – lightweight console logger abstraction.

## Referencing

Add a project reference:

```bash
dotnet add <your-project>.csproj reference src/DukascopyDownloader.Core/DukascopyDownloader.Core.csproj
```

Then use the types under the `DukascopyDownloader` namespace (e.g., `DownloadOrchestrator`, `CsvGenerator`, `DownloadOptions`, `GenerationOptions`). The CLI project is a working example of how to wire these pieces together.

## Tests

Core tests live under `tests/DukascopyDownloader.Core.Tests`. Run them with:

```bash
dotnet test tests/DukascopyDownloader.Core.Tests/DukascopyDownloader.Core.Tests.csproj
```

