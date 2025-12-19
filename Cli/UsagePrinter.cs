namespace DukascopyDownloader.Cli;

internal static class UsagePrinter
{
    public static void Print()
    {
        var executable = AppDomain.CurrentDomain.FriendlyName;
        var version = VersionInfo.GetVersion();
        Console.WriteLine();
        Console.WriteLine($"{executable} v{version}");
        Console.WriteLine($"{executable} --instrument EURUSD --from 2025-01-14 --to 2025-01-18 [options]");
        Console.WriteLine();
        Console.WriteLine("Download options:");
        Console.WriteLine("  -i, --instrument           Symbol (e.g. EURUSD).");
        Console.WriteLine("  -f, --from                 Start date (UTC, YYYY-MM-DD).");
        Console.WriteLine("  -t, --to                   End date (UTC, inclusive).");
        Console.WriteLine("      --timeframe            tick|s1|m1|m5|m15|m30|h1|h4|d1|mn1 (default: tick).");
        Console.WriteLine("      --cache-root           Cache folder (.dukascopy-downloader-cache).");
        Console.WriteLine("  -o, --output               Optional directory to mirror BI5 files and CSV exports (defaults to CWD for reports).");
        Console.WriteLine("      --concurrency          Parallel downloads (default: cores - 1).");
        Console.WriteLine("      --max-retries          Attempts per file (default: 4).");
        Console.WriteLine("      --retry-delay          Delay between retries (default: 5s).");
        Console.WriteLine("      --rate-limit-pause     Pause after HTTP 429 (default: 30s).");
        Console.WriteLine("      --rate-limit-retries   Allowed rate-limit retries (default: 5).");
        Console.WriteLine("      --force                Ignore cache entries.");
        Console.WriteLine("      --no-cache             Disable cache reads (still writes).");
        Console.WriteLine();
        Console.WriteLine("Generation options:");
        Console.WriteLine("      --timezone             Output timezone for CSV timestamps.");
        Console.WriteLine("      --date-format          Custom timestamp format.");
        Console.WriteLine("      --export-template      Preset output format (mt5 supported). Overrides header/format.");
        Console.WriteLine("      --include-inactive     Fill closed-market intervals with flat candles (0 volume).");
        Console.WriteLine();
        Console.WriteLine("General:");
        Console.WriteLine("      --verbose              Verbose logging.");
        Console.WriteLine("      --version              Show version information and exit.");
        Console.WriteLine("  -h, --help                 Show this help.");
        Console.WriteLine();
        Console.WriteLine("Note: '--to' is inclusive; downloads stop at the end of that UTC day.");
        Console.WriteLine();
    }
}
