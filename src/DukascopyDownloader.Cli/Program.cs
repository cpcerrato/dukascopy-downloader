using System.Diagnostics;
using DukascopyDownloader.Cli;
using DukascopyDownloader.Cli.Logging;
using DukascopyDownloader.Download;
using DukascopyDownloader.Generation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DukascopyDownloader;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var parser = new CliParser(new GenerationOptionsFactory());
        var parseResult = parser.Parse(args);
        var logger = new ConsoleLogger(parseResult.Options?.Verbose ?? false);

        if (parseResult.ShowHelp)
        {
            UsagePrinter.Print();
            return 0;
        }

        if (parseResult.ShowVersion)
        {
            Console.WriteLine($"dukascopy-downloader {VersionInfo.GetVersion()}");
            return 0;
        }

        if (!parseResult.IsValid)
        {
            logger.LogError(parseResult.Error ?? "Invalid arguments.");
            UsagePrinter.Print();
            return 1;
        }

        var options = parseResult.Options!;

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
            logger.LogWarning("Cancellation requested. Waiting for in-flight downloads to finish...");
        };

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new ForwardingProvider(logger)));
        var downloaderLogger = loggerFactory.CreateLogger<DownloadOrchestrator>();
        var generatorLogger = loggerFactory.CreateLogger<CsvGenerator>();

        var downloader = new DownloadOrchestrator(downloaderLogger, logger);
        var generator = new CsvGenerator(generatorLogger, logger);

        try
        {
            PrintOptionsSummary(logger, options);
            var swTotal = Stopwatch.StartNew();

            var swDownload = Stopwatch.StartNew();
            var downloadSummary = await downloader.ExecuteAsync(options.Download, cts.Token);
            swDownload.Stop();

            var generationElapsed = TimeSpan.Zero;
            if (!options.DownloadOnly)
            {
                var swGeneration = Stopwatch.StartNew();
                await generator.GenerateAsync(options.Download, options.Generation, cts.Token);
                swGeneration.Stop();
                generationElapsed = swGeneration.Elapsed;
            }

            swTotal.Stop();

            logger.LogInformation($"Downloads: New {downloadSummary.NewFiles}, Cache {downloadSummary.CacheHits}, Missing {downloadSummary.Missing}, Failed {downloadSummary.Failed} (total {downloadSummary.Total}).");
            logger.LogInformation($"Download time: {swDownload.Elapsed.TotalSeconds:F2}s");
            if (options.DownloadOnly)
            {
                logger.LogInformation("Generation skipped (--download-only).");
            }
            else
            {
                logger.LogInformation($"Generation time: {generationElapsed.TotalSeconds:F2}s");
            }
            logger.LogInformation($"Total time: {swTotal.Elapsed.TotalSeconds:F2}s");

            return 0;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Operation cancelled by user.");
            return 2;
        }
        catch (Exception ex)
        {
            logger.LogError($"Fatal error: {ex.Message}");
            logger.LogDebug(ex.ToString());
            return 1;
        }
    }

    private static void PrintOptionsSummary(ILogger logger, AppOptions options)
    {
        var download = options.Download;
        var generation = options.Generation;
        var toInclusive = download.ToUtc.AddDays(-1);
        var tf = download.Timeframe.ToDisplayString();
        var tsDescription = generation.HasCustomSettings
            ? $"{generation.TimeZone.Id} format '{generation.DateFormat ?? "yyyy-MM-dd HH:mm:ss"}'"
            : "UTC (Unix ms)";
        var template = generation.Template != ExportTemplate.None ? $"Template: {generation.Template}" : "Template: none";
        var mode = options.DownloadOnly ? "Mode: download-only (CSV skipped)" : "Mode: download + CSV";

        logger.LogInformation($"Instrument: {download.Instrument}, Timeframe: {tf}, Dates: {download.FromUtc:yyyy-MM-dd}..{toInclusive:yyyy-MM-dd}, Timestamps: {tsDescription}");
        logger.LogInformation($"Cache: {download.CacheRoot}, Output: {(string.IsNullOrWhiteSpace(download.OutputDirectory) ? "cwd" : download.OutputDirectory)}");
        logger.LogInformation($"Concurrency: {download.Concurrency}, Retries: {download.MaxRetries}, Rate-limit pause: {download.RateLimitPause.TotalSeconds:F0}s x{download.RateLimitRetryLimit}, {template}, {mode}");
    }
}
