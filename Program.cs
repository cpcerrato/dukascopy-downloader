using DukascopyDownloader.Cli;
using DukascopyDownloader.Download;
using DukascopyDownloader.Generation;
using DukascopyDownloader.Logging;
using System.Diagnostics;

namespace DukascopyDownloader;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var logger = new ConsoleLogger();
        var parser = new CliParser(new GenerationOptionsFactory());
        var parseResult = parser.Parse(args);

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
            logger.Error(parseResult.Error ?? "Invalid arguments.");
            UsagePrinter.Print();
            return 1;
        }

        var options = parseResult.Options!;
        logger.VerboseEnabled = options.Verbose;

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
            logger.Warn("Cancellation requested. Waiting for in-flight downloads to finish...");
        };

        var downloader = new DownloadOrchestrator(logger);
        var generator = new CsvGenerator(logger);

        try
        {
            PrintOptionsSummary(logger, options);
            var swTotal = Stopwatch.StartNew();

            var swDownload = Stopwatch.StartNew();
            var downloadSummary = await downloader.ExecuteAsync(options.Download, cts.Token);
            swDownload.Stop();

            var swGeneration = Stopwatch.StartNew();
            await generator.GenerateAsync(options.Download, options.Generation, cts.Token);
            swGeneration.Stop();

            swTotal.Stop();

            logger.Success($"Downloads: New {downloadSummary.NewFiles}, Cache {downloadSummary.CacheHits}, Missing {downloadSummary.Missing}, Failed {downloadSummary.Failed} (total {downloadSummary.Total}).");
            logger.Success($"Download time: {swDownload.Elapsed.TotalSeconds:F2}s");
            logger.Success($"Generation time: {swGeneration.Elapsed.TotalSeconds:F2}s");
            logger.Success($"Total time: {swTotal.Elapsed.TotalSeconds:F2}s");

            return 0;
        }
        catch (OperationCanceledException)
        {
            logger.Warn("Operation cancelled by user.");
            return 2;
        }
        catch (Exception ex)
        {
            logger.Error($"Fatal error: {ex.Message}");
            logger.Verbose(ex.ToString());
            return 1;
        }
    }

    private static void PrintOptionsSummary(ConsoleLogger logger, AppOptions options)
    {
        var download = options.Download;
        var generation = options.Generation;
        var toInclusive = download.ToUtc.AddDays(-1);
        var tf = download.Timeframe.ToDisplayString();
        var tsDescription = generation.HasCustomSettings
            ? $"{generation.TimeZone.Id} format '{generation.DateFormat ?? "yyyy-MM-dd HH:mm:ss"}'"
            : "UTC (Unix ms)";
        var template = generation.Template != ExportTemplate.None ? $"Template: {generation.Template}" : "Template: none";

        logger.Info($"Instrument: {download.Instrument}, Timeframe: {tf}, Dates: {download.FromUtc:yyyy-MM-dd}..{toInclusive:yyyy-MM-dd}, Timestamps: {tsDescription}");
        logger.Info($"Cache: {download.CacheRoot}, Output: {(string.IsNullOrWhiteSpace(download.OutputDirectory) ? "cwd" : download.OutputDirectory)}");
        logger.Info($"Concurrency: {download.Concurrency}, Retries: {download.MaxRetries}, Rate-limit pause: {download.RateLimitPause.TotalSeconds:F0}s x{download.RateLimitRetryLimit}, {template}");
    }
}
