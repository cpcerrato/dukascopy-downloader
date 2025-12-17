namespace DukascopyDownloader.Cli;

internal static class UsagePrinter
{
    public static void Print()
    {
        var executable = AppDomain.CurrentDomain.FriendlyName;
        Console.WriteLine();
        Console.WriteLine($"{executable} --instrument EURUSD --from 2025-01-14 --to 2025-01-18 [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -i, --instrument           Symbol to download (e.g. EURUSD).");
        Console.WriteLine("  -f, --from                 Start date (UTC) in YYYY-MM-DD.");
        Console.WriteLine("  -t, --to                   End date (UTC, inclusive) in YYYY-MM-DD.");
        Console.WriteLine("      --timeframe            tick|s1|m1|m5|m15|m30|h1|h4|d1|mn1 (tick by default).");
        Console.WriteLine("      --cache-root           Cache folder (.dukascopy-downloader-cache by default).");
        Console.WriteLine("  -o, --output               Optional directory to mirror verified BI5 files.");
        Console.WriteLine("      --timezone             Output timezone (stored for generator module).");
        Console.WriteLine("      --date-format          Output date format (stored for generator module).");
        Console.WriteLine("      --include-inactive     Fill closed-market intervals with flat candles (0 volume).");
        Console.WriteLine("  -c, --concurrency          Parallel downloads (default: cores - 1).");
        Console.WriteLine("      --max-retries          Attempts per file (default: 4).");
        Console.WriteLine("      --retry-delay          Delay between retries (default: 5s).");
        Console.WriteLine("      --rate-limit-pause     Pause after 429 (default: 30s).");
        Console.WriteLine("      --rate-limit-retries   Allowed rate-limit retries (default: 5).");
        Console.WriteLine("      --force                Ignore cache entries.");
        Console.WriteLine("      --no-cache             Disable cache reads (still writes).");
        Console.WriteLine("      --verbose              Verbose logging.");
        Console.WriteLine("  -h, --help                 Show this help.");
        Console.WriteLine();
        Console.WriteLine("Note: '--to' is inclusive; downloads stop at the end of that UTC day.");
        Console.WriteLine();
    }
}
