using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Globalization;
using DukascopyDownloader.Cli;
using DukascopyDownloader.Cli.Logging;
using DukascopyDownloader.Download;
using DukascopyDownloader.Generation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DukascopyDownloader;

internal static class Program
{
    public static Task<int> Main(string[] args)
    {
        var rootCommand = BuildRootCommand();
        var parser = new CommandLineBuilder(rootCommand)
            .UseDefaults()
            .UseVersionOption("--version", "-V")
            .Build();
        return parser.InvokeAsync(args);
    }

    private static RootCommand BuildRootCommand()
    {
        var instrumentOption = new Option<string>("--instrument", "Symbol (e.g. EURUSD)") { IsRequired = true };
        instrumentOption.AddAlias("-i");

        var fromOption = new Option<DateOnly>("--from", ParseDate, description: "Start date (UTC, YYYY-MM-DD)") { IsRequired = true };
        fromOption.AddAlias("-f");

        var toOption = new Option<DateOnly>("--to", ParseDate, description: "End date (UTC, inclusive)") { IsRequired = true };
        toOption.AddAlias("-t");

        var timeframeOption = new Option<string>("--timeframe", description: "tick|s1|m1|m5|m15|m30|h1|h4|d1|w1|mn1") { IsRequired = true };
        timeframeOption.AddAlias("-T");
        timeframeOption.AddValidator(result =>
        {
            var token = result.Tokens.FirstOrDefault()?.Value;
            if (string.IsNullOrWhiteSpace(token))
            {
                result.ErrorMessage = "Option '--timeframe' requires a value.";
                return;
            }

            if (!TryParseTimeframeToken(token, out _))
            {
                result.ErrorMessage = $"Timeframe '{token}' is not supported.";
            }
        });

        var cacheRootOption = new Option<string>("--cache-root", description: "Cache folder for BI5 files.");
        cacheRootOption.SetDefaultValue(Path.Combine(Environment.CurrentDirectory, ".dukascopy-downloader-cache"));

        var outputOption = new Option<string?>("--out", description: "Optional directory to mirror BI5 files and CSV exports (defaults to CWD for reports).");
        outputOption.AddAlias("-o");
        var downloadOnlyOption = new Option<bool>("--download-only", "Only download/verify BI5 files (skip CSV generation).");

        var concurrencyOption = new Option<int>("--concurrency", result => ParsePositiveInt(result, 1, Math.Max(1, Environment.ProcessorCount - 1)), description: "Parallel downloads (default: cores - 1).");
        concurrencyOption.AddAlias("-c");
        concurrencyOption.SetDefaultValue(Math.Max(1, Environment.ProcessorCount - 1));

        var maxRetriesOption = new Option<int>("--max-retries", result => ParsePositiveInt(result, 0, 4), description: "Attempts per file (default: 4).");
        maxRetriesOption.SetDefaultValue(4);

        var retryDelayOption = new Option<TimeSpan>("--retry-delay", result => ParseDuration(result, TimeSpan.FromSeconds(5)), description: "Delay between retries (default: 5s).");
        retryDelayOption.SetDefaultValue(TimeSpan.FromSeconds(5));

        var rateLimitPauseOption = new Option<TimeSpan>("--rate-limit-pause", result => ParseDuration(result, TimeSpan.FromSeconds(30)), description: "Pause after HTTP 429 (default: 30s).");
        rateLimitPauseOption.SetDefaultValue(TimeSpan.FromSeconds(30));

        var rateLimitRetriesOption = new Option<int>("--rate-limit-retries", result => ParsePositiveInt(result, 1, 5), description: "Allowed rate-limit retries (default: 5).");
        rateLimitRetriesOption.SetDefaultValue(5);

        var forceOption = new Option<bool>("--force", "Ignore cache entries.");
        var noCacheOption = new Option<bool>("--no-cache", "Disable cache reads (still writes).");

        var timezoneOption = new Option<string?>("--timezone", "Output timezone for CSV timestamps.");
        var dateFormatOption = new Option<string?>("--date-format", "Custom timestamp format.");

        var templateOption = new Option<ExportTemplate>("--export-template", ParseTemplate, description: "Preset output format (mt5).");
        templateOption.SetDefaultValue(ExportTemplate.None);
        var headerOption = new Option<bool?>("--header", "Force CSV header on/off; default depends on template (on for none, off for mt5).");

        var tickSizeOption = new Option<decimal?>("--tick-size", ParsePositiveDecimal, description: "Tick size/point value for spread calculation (bars).");
        var inferTickSizeOption = new Option<bool>("--infer-tick-size", "Infer tick size from tick deltas (requires cached ticks).");

        var minNonZeroDeltasOption = new Option<int>("--min-nonzero-deltas", result => ParsePositiveInt(result, 1, 100), description: "Minimum deltas > 0 to accept inference (default: 100).");
        minNonZeroDeltasOption.SetDefaultValue(100);

        var spreadPointsOption = new Option<int?>("--spread-points", result => ParseOptionalPositiveInt(result, 0), description: "Fixed spread in points for bars (fallback if no tick size).");

        var spreadAggregationOption = new Option<SpreadAggregation>("--spread-agg", ParseSpreadAggregation, description: "Spread aggregation: median (p50), mean (average), min (best), last (most recent) (default: median).");
        spreadAggregationOption.SetDefaultValue(SpreadAggregation.Median);

        var includeSpreadOption = new Option<bool>("--include-spread", "Append spread column to candle exports.");

        var includeVolumeOption = new Option<bool>("--include-volume", description: "Keep volume columns (default on; use --no-volume to drop for non-MT5).");
        includeVolumeOption.SetDefaultValue(true);

        var noVolumeOption = new Option<bool>("--no-volume", "Drop volume columns for non-MT5 output.");
        var fixedVolumeOption = new Option<int?>("--fixed-volume", result => ParseOptionalPositiveInt(result, 0), description: "Fixed volume value for candles (overrides calculated tick count).");

        var includeInactiveOption = new Option<bool>("--include-inactive", "Fill closed-market intervals with flat candles (0 volume).");
        var preferTicksOption = new Option<bool>("--prefer-ticks", "Build bars from tick data (downloads tick feed if needed).");
        var sideOption = new Option<PriceSidePreference>("--side", () => PriceSidePreference.Bid, "Price side to download: bid (default), ask, or both.");

        var verboseOption = new Option<bool>(new[] { "--verbose", "-v" }, "Verbose logging.");

        var root = new RootCommand("Download Dukascopy history and optionally generate CSV exports.");
        root.AddOption(instrumentOption);
        root.AddOption(fromOption);
        root.AddOption(toOption);
        root.AddOption(timeframeOption);
        root.AddOption(cacheRootOption);
        root.AddOption(outputOption);
        root.AddOption(downloadOnlyOption);
        root.AddOption(concurrencyOption);
        root.AddOption(maxRetriesOption);
        root.AddOption(retryDelayOption);
        root.AddOption(rateLimitPauseOption);
        root.AddOption(rateLimitRetriesOption);
        root.AddOption(forceOption);
        root.AddOption(noCacheOption);
        root.AddOption(timezoneOption);
        root.AddOption(dateFormatOption);
        root.AddOption(templateOption);
        root.AddOption(headerOption);
        root.AddOption(tickSizeOption);
        root.AddOption(inferTickSizeOption);
        root.AddOption(minNonZeroDeltasOption);
        root.AddOption(spreadPointsOption);
        root.AddOption(spreadAggregationOption);
        root.AddOption(includeSpreadOption);
        root.AddOption(includeVolumeOption);
        root.AddOption(noVolumeOption);
        root.AddOption(fixedVolumeOption);
        root.AddOption(includeInactiveOption);
        root.AddOption(preferTicksOption);
        root.AddOption(sideOption);
        root.AddOption(verboseOption);

        root.SetHandler(async (InvocationContext context) =>
        {
            var parse = context.ParseResult;
            var instrument = parse.GetValueForOption(instrumentOption)!;
            var from = parse.GetValueForOption(fromOption);
            var to = parse.GetValueForOption(toOption);
            var timeframeToken = parse.GetValueForOption(timeframeOption)!;
            if (!TryParseTimeframeToken(timeframeToken, out var timeframe))
            {
                Console.Error.WriteLine($"Timeframe '{timeframeToken}' is not supported.");
                context.ExitCode = 1;
                return;
            }
            var cacheRoot = parse.GetValueForOption(cacheRootOption) ?? Path.Combine(Environment.CurrentDirectory, ".dukascopy-downloader-cache");
            var output = parse.GetValueForOption(outputOption);
            var downloadOnly = parse.GetValueForOption(downloadOnlyOption);
            var concurrency = parse.GetValueForOption(concurrencyOption);
            var maxRetries = parse.GetValueForOption(maxRetriesOption);
            var retryDelay = parse.GetValueForOption(retryDelayOption);
            var rateLimitPause = parse.GetValueForOption(rateLimitPauseOption);
            var rateLimitRetries = parse.GetValueForOption(rateLimitRetriesOption);
            var force = parse.GetValueForOption(forceOption);
            var noCache = parse.GetValueForOption(noCacheOption);
            var includeInactive = parse.GetValueForOption(includeInactiveOption);
            var timezone = parse.GetValueForOption(timezoneOption);
            var dateFormat = parse.GetValueForOption(dateFormatOption);
            var template = parse.GetValueForOption(templateOption);
            var headerOverride = parse.GetValueForOption(headerOption);
            var tickSize = parse.GetValueForOption(tickSizeOption);
            var spreadPoints = parse.GetValueForOption(spreadPointsOption);
            var inferTickSize = parse.GetValueForOption(inferTickSizeOption);
            var minNonZeroDeltas = parse.GetValueForOption(minNonZeroDeltasOption);
            var spreadAggregation = parse.GetValueForOption(spreadAggregationOption);
        var includeSpread = parse.GetValueForOption(includeSpreadOption);
        var includeVolumeFlag = parse.GetValueForOption(includeVolumeOption);
        var noVolume = parse.GetValueForOption(noVolumeOption);
        var fixedVolume = parse.GetValueForOption(fixedVolumeOption);
        var preferTicks = parse.GetValueForOption(preferTicksOption);
        var sidePreference = parse.GetValueForOption(sideOption);
        var verbose = parse.GetValueForOption(verboseOption);

        context.ExitCode = await RunAsync(
            instrument,
            from,
            to,
                timeframe,
                cacheRoot,
                output,
                downloadOnly,
                concurrency,
                maxRetries,
                retryDelay,
                rateLimitPause,
                rateLimitRetries,
                force,
                noCache,
                includeInactive,
                timezone,
                dateFormat,
                template,
                headerOverride,
                tickSize,
                spreadPoints,
                inferTickSize,
                minNonZeroDeltas,
                spreadAggregation,
            includeSpread,
            includeVolumeFlag,
            noVolume,
            fixedVolume,
            preferTicks,
            sidePreference,
            verbose);
    });

    return root;
}

    private static async Task<int> RunAsync(
        string instrument,
        DateOnly fromDate,
        DateOnly toDate,
        DukascopyTimeframe timeframe,
        string cacheRoot,
        string? output,
        bool downloadOnly,
        int concurrency,
        int maxRetries,
        TimeSpan retryDelay,
        TimeSpan rateLimitPause,
        int rateLimitRetries,
        bool force,
        bool noCache,
        bool includeInactive,
        string? timezone,
        string? dateFormat,
        ExportTemplate template,
        bool? includeHeaderOverride,
        decimal? tickSize,
        int? spreadPoints,
        bool inferTickSize,
        int minNonZeroDeltas,
        SpreadAggregation spreadAggregation,
        bool includeSpread,
        bool includeVolumeFlag,
        bool noVolume,
        int? fixedVolume,
        bool preferTicks,
        PriceSidePreference sidePreference,
        bool verbose)
    {
        var fromUtc = new DateTimeOffset(fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var toExclusive = new DateTimeOffset(toDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).AddDays(1);
        if (toExclusive <= fromUtc)
        {
            Console.Error.WriteLine("'--to' must be after '--from'.");
            return 1;
        }

        var sideLogger = new ConsoleLogger(verbose);
        var generationFactory = new GenerationOptionsFactory();
        var includeVolume = noVolume ? false : includeVolumeFlag;
        if (!generationFactory.TryCreate(
                timezone,
                dateFormat,
                template,
                includeHeaderOverride,
                tickSize,
                spreadPoints,
                inferTickSize,
                minNonZeroDeltas,
                spreadAggregation,
                includeSpread,
                includeVolume,
                fixedVolume,
                preferTicks,
                out var generationOptions,
                out var error))
        {
            Console.Error.WriteLine(error ?? "Invalid generation options.");
            return 1;
        }

        var downloadOptions = new DownloadOptions(
            instrument.Trim().ToUpperInvariant(),
            fromUtc,
            toExclusive,
            timeframe,
            cacheRoot,
            string.IsNullOrWhiteSpace(output) ? null : output,
            UseCache: !noCache,
            ForceRefresh: force,
            IncludeInactivePeriods: includeInactive,
            Concurrency: concurrency,
            MaxRetries: maxRetries,
            RetryDelay: retryDelay,
            RateLimitPause: rateLimitPause,
            RateLimitRetryLimit: rateLimitRetries,
            SidePreference: sidePreference);

        var appOptions = new AppOptions(downloadOptions, generationOptions!, verbose, downloadOnly);
        return await ExecuteAsync(appOptions);
    }

    private static async Task<int> ExecuteAsync(AppOptions options)
    {
        var logger = new ConsoleLogger(options.Verbose);
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
            var targetTimeframe = options.Download.Timeframe;
            var preferTicks = options.Generation.PreferTicks && targetTimeframe != DukascopyTimeframe.Tick;

            var needsTickBasedSpread = options.Generation.TickSize.HasValue || options.Generation.InferTickSize;
            var spreadWithoutFixed = options.Generation.IncludeSpread && !options.Generation.SpreadPoints.HasValue;
            if (!preferTicks && targetTimeframe != DukascopyTimeframe.Tick &&
                (needsTickBasedSpread || spreadWithoutFixed))
            {
                logger.LogWarning("Spread/tick-size options were provided but --prefer-ticks is not set; bars will be taken from Dukascopy M1 feed without tick-based spread.");
            }
            else if (preferTicks && options.Generation.IncludeSpread)
            {
                logger.LogWarning("Spread will be computed from ticks; this may download/process the full tick history and take significantly longer.");
            }

            if (preferTicks)
            {
                logger.LogInformation("Prefer-ticks enabled: exporting {Tf} from tick feed (may download tick history if not cached).", targetTimeframe.ToDisplayString());
            }

            var swTotal = Stopwatch.StartNew();

            var downloadForRun = options.Download;
            if (preferTicks)
            {
                downloadForRun = options.Download with { Timeframe = DukascopyTimeframe.Tick };
            }

            var swDownload = Stopwatch.StartNew();
            var downloadSummary = await downloader.ExecuteAsync(downloadForRun, cts.Token);
            swDownload.Stop();

            var generationElapsed = TimeSpan.Zero;
            if (!options.DownloadOnly)
            {
                var swGeneration = Stopwatch.StartNew();
                await generator.GenerateAsync(downloadForRun, options.Generation, targetTimeframe, cts.Token);
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
        var template = generation.Template != ExportTemplate.None ? generation.Template.ToString() : "none";
        var mode = options.DownloadOnly ? "download-only (CSV skipped)" : "download + CSV";
        var preferTicks = generation.PreferTicks && download.Timeframe != DukascopyTimeframe.Tick
            ? "yes (bars aggregated from tick feed)"
            : "no";

        const string Reset = "\u001b[0m";
        const string Cyan = "\u001b[36m";
        const string Magenta = "\u001b[35m";
        const string Yellow = "\u001b[33m";

        string Colorize(string value, string color) => $"{color}{value}{Reset}";
        static string YesNo(bool value) => value ? "yes" : "no";

        var rows = new List<(string Label, string Value)>
        {
            ("Instrument", Colorize(download.Instrument, Cyan)),
            ("Timeframe", Colorize(tf, Magenta)),
            ("Side", Colorize(download.SidePreference.ToString().ToLowerInvariant(), Cyan)),
            ("Dates", Colorize($"{download.FromUtc:yyyy-MM-dd}..{toInclusive:yyyy-MM-dd}", Yellow)),
            ("Timestamps", tsDescription),
            ("Cache", download.CacheRoot),
            ("Output", string.IsNullOrWhiteSpace(download.OutputDirectory) ? "cwd" : download.OutputDirectory),
            ("Use cache", YesNo(download.UseCache)),
            ("Force refresh", YesNo(download.ForceRefresh)),
            ("Include inactive", YesNo(download.IncludeInactivePeriods)),
            ("Concurrency", download.Concurrency.ToString(CultureInfo.InvariantCulture)),
            ("Retries", download.MaxRetries.ToString(CultureInfo.InvariantCulture)),
            ("Rate-limit", $"{download.RateLimitPause.TotalSeconds:F0}s x{download.RateLimitRetryLimit}"),
            ("Template", template),
            ("Header", generation.IncludeHeader ? "on" : "off"),
            ("Mode", mode),
            ("Prefer ticks", preferTicks),
            ("Timezone", generation.TimeZone.Id),
            ("Date format", generation.DateFormat ?? "yyyy-MM-dd HH:mm:ss"),
            ("Tick size", generation.TickSize?.ToString(CultureInfo.InvariantCulture) ?? "auto"),
            ("Spread points", generation.SpreadPoints?.ToString(CultureInfo.InvariantCulture) ?? "none"),
            ("Infer tick size", YesNo(generation.InferTickSize)),
            ("Min non-zero deltas", generation.MinNonZeroDeltas.ToString(CultureInfo.InvariantCulture)),
            ("Spread agg", generation.SpreadAggregation.ToString().ToLowerInvariant()),
            ("Include spread", YesNo(generation.IncludeSpread)),
            ("Include volume", YesNo(generation.IncludeVolume)),
            ("Fixed volume", generation.FixedVolume?.ToString(CultureInfo.InvariantCulture) ?? "none")
        };

        var labelWidth = rows.Max(r => r.Label.Length);
        var lines = new List<string>(rows.Count + 2) { "", "Run configuration:" };
        lines.AddRange(rows.Select(row =>
        {
            var label = row.Label.PadRight(labelWidth);
            return $"  {label}  {row.Value}";
        }));
        lines.Add(string.Empty);

        logger.LogInformation(string.Join('\n', lines));
    }

    private static DateOnly ParseDate(ArgumentResult result)
    {
        if (result.Tokens.Count == 0)
        {
            result.ErrorMessage = "Date is required (YYYY-MM-DD).";
            return default;
        }

        var raw = result.Tokens[0].Value;
        if (DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed;
        }

        result.ErrorMessage = $"Date '{raw}' must follow the 'YYYY-MM-DD' format.";
        return default;
    }

    private static TimeSpan ParseDuration(ArgumentResult result, TimeSpan fallback)
    {
        if (result.Tokens.Count == 0)
        {
            return fallback;
        }

        var raw = result.Tokens[0].Value;
        if (TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        result.ErrorMessage = $"Value '{raw}' is not a valid duration. Use '5' (seconds) or '00:00:05'.";
        return fallback;
    }

    private static ExportTemplate ParseTemplate(ArgumentResult result)
    {
        if (result.Tokens.Count == 0)
        {
            return ExportTemplate.None;
        }

        var raw = result.Tokens[0].Value.ToLowerInvariant();
        return raw switch
        {
            "mt5" or "metatrader5" or "metatrader" => ExportTemplate.MetaTrader5,
            _ => ExportTemplate.None
        };
    }

    private static SpreadAggregation ParseSpreadAggregation(ArgumentResult result)
    {
        if (result.Tokens.Count == 0)
        {
            return SpreadAggregation.Median;
        }

        var raw = result.Tokens[0].Value.ToLowerInvariant();
        return raw switch
        {
            "median" => SpreadAggregation.Median,
            "min" => SpreadAggregation.Min,
            "mean" => SpreadAggregation.Mean,
            "last" => SpreadAggregation.Last,
            _ => SpreadAggregation.Median
        };
    }

    private static decimal? ParsePositiveDecimal(ArgumentResult result)
    {
        if (result.Tokens.Count == 0)
        {
            return null;
        }

        var raw = result.Tokens[0].Value;
        if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0)
        {
            return value;
        }

        result.ErrorMessage = $"Option '--{result.Argument.Name}' must be a decimal greater than 0.";
        return null;
    }

    private static int ParsePositiveInt(ArgumentResult result, int minValue, int fallback)
    {
        if (result.Tokens.Count == 0)
        {
            return fallback;
        }

        var raw = result.Tokens[0].Value;
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= minValue)
        {
            return parsed;
        }

        result.ErrorMessage = $"Option '--{result.Argument.Name}' must be an integer >= {minValue}.";
        return fallback;
    }

    private static int? ParseOptionalPositiveInt(ArgumentResult result, int minValue)
    {
        if (result.Tokens.Count == 0)
        {
            return null;
        }

        var raw = result.Tokens[0].Value;
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= minValue)
        {
            return parsed;
        }

        result.ErrorMessage = $"Option '--{result.Argument.Name}' must be an integer >= {minValue}.";
        return null;
    }

    private static bool TryParseTimeframeToken(string token, out DukascopyTimeframe timeframe)
    {
        timeframe = token.ToLowerInvariant() switch
        {
            "tick" or "t" => DukascopyTimeframe.Tick,
            "s1" or "1s" => DukascopyTimeframe.Second1,
            "m1" or "1m" => DukascopyTimeframe.Minute1,
            "m5" or "5m" => DukascopyTimeframe.Minute5,
            "m15" or "15m" => DukascopyTimeframe.Minute15,
            "m30" or "30m" => DukascopyTimeframe.Minute30,
            "h1" or "1h" => DukascopyTimeframe.Hour1,
            "h4" or "4h" => DukascopyTimeframe.Hour4,
            "d1" or "1d" => DukascopyTimeframe.Day1,
            "w1" or "1w" => DukascopyTimeframe.Week1,
            "mn1" or "1mo" => DukascopyTimeframe.Month1,
            _ => (DukascopyTimeframe)(-1)
        };

        return Enum.IsDefined(typeof(DukascopyTimeframe), timeframe);
    }
}
