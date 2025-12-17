using DukascopyDownloader.Cli;
using DukascopyDownloader.Download;
using DukascopyDownloader.Generation;
using DukascopyDownloader.Logging;

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
            await downloader.ExecuteAsync(options.Download, cts.Token);
            await generator.GenerateAsync(options.Download, options.Generation, cts.Token);

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
}
