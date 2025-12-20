using System.Globalization;
using DukascopyDownloader.Download;
using DukascopyDownloader.Generation;

namespace DukascopyDownloader.Cli;

internal sealed class CliParser
{
    private readonly GenerationOptionsFactory _generationFactory;

    public CliParser(GenerationOptionsFactory generationFactory)
    {
        _generationFactory = generationFactory;
    }

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["i"] = "instrument",
        ["symbol"] = "instrument",
        ["asset"] = "instrument",
        ["instrument"] = "instrument",
        ["f"] = "from",
        ["from"] = "from",
        ["t"] = "to",
        ["to"] = "to",
        ["timeframe"] = "timeframe",
        ["tf"] = "timeframe",
        ["cache-root"] = "cache-root",
        ["cache"] = "cache-root",
        ["cacheDir"] = "cache-root",
        ["output"] = "output",
        ["out"] = "output",
        ["o"] = "output",
        ["tick-size"] = "tick-size",
        ["point"] = "tick-size",
        ["spread-points"] = "spread-points",
        ["spread"] = "spread-points",
        ["infer-tick-size"] = "infer-tick-size",
        ["min-nonzero-deltas"] = "min-nonzero-deltas",
        ["spread-agg"] = "spread-agg",
        ["include-volume"] = "include-volume",
        ["no-volume"] = "no-volume",
        ["fixed-volume"] = "fixed-volume",
        ["include-spread"] = "include-spread",
        ["download-only"] = "download-only",
        ["downloadonly"] = "download-only",
        ["concurrency"] = "concurrency",
        ["workers"] = "concurrency",
        ["c"] = "concurrency",
        ["force"] = "force",
        ["max-retries"] = "max-retries",
        ["retries"] = "max-retries",
        ["retry-delay"] = "retry-delay",
        ["rate-limit-pause"] = "rate-limit-pause",
        ["rate-limit-wait"] = "rate-limit-pause",
        ["rate-limit-retries"] = "rate-limit-retries",
        ["rate-limit-attempts"] = "rate-limit-retries",
        ["no-cache"] = "no-cache",
        ["skip-cache"] = "no-cache",
        ["timezone"] = "timezone",
        ["tz"] = "timezone",
        ["date-format"] = "date-format",
        ["dateformat"] = "date-format",
        ["export-template"] = "export-template",
        ["include-inactive"] = "include-inactive",
        ["fill-inactive"] = "include-inactive",
        ["fill-gaps"] = "include-inactive",
        ["verbose"] = "verbose",
        ["v"] = "verbose",
        ["version"] = "version",
        ["V"] = "version",
        ["help"] = "help",
        ["h"] = "help"
    };

    private static readonly HashSet<string> FlagOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "force",
        "no-cache",
        "include-inactive",
        "include-spread",
        "include-volume",
        "no-volume",
        "verbose",
        "version",
        "download-only",
        "infer-tick-size",
        "help"
    };

    private static readonly Dictionary<string, DukascopyTimeframe> TimeframeAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tick"] = DukascopyTimeframe.Tick,
        ["t"] = DukascopyTimeframe.Tick,
        ["s1"] = DukascopyTimeframe.Second1,
        ["1s"] = DukascopyTimeframe.Second1,
        ["m1"] = DukascopyTimeframe.Minute1,
        ["1m"] = DukascopyTimeframe.Minute1,
        ["m5"] = DukascopyTimeframe.Minute5,
        ["5m"] = DukascopyTimeframe.Minute5,
        ["m15"] = DukascopyTimeframe.Minute15,
        ["15m"] = DukascopyTimeframe.Minute15,
        ["m30"] = DukascopyTimeframe.Minute30,
        ["30m"] = DukascopyTimeframe.Minute30,
        ["h1"] = DukascopyTimeframe.Hour1,
        ["1h"] = DukascopyTimeframe.Hour1,
        ["h4"] = DukascopyTimeframe.Hour4,
        ["4h"] = DukascopyTimeframe.Hour4,
        ["d1"] = DukascopyTimeframe.Day1,
        ["1d"] = DukascopyTimeframe.Day1,
        ["mn1"] = DukascopyTimeframe.Month1,
        ["1mo"] = DukascopyTimeframe.Month1
    };

    public CliParseResult Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return new CliParseResult(showHelp: true, showVersion: false, options: null, error: null);
        }

        var normalized = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        string? pendingOption = null;

        foreach (var raw in args)
        {
            if (IsOption(raw))
            {
                var (name, value, isHelp) = ExtractNameValue(raw);
                if (isHelp)
                {
                    return new CliParseResult(showHelp: true, showVersion: false, options: null, error: null);
                }

                if (FlagOptions.Contains(name))
                {
                    normalized[name] = "true";
                    pendingOption = null;
                    continue;
                }

                if (value is null)
                {
                    pendingOption = name;
                    continue;
                }

                normalized[name] = value;
                pendingOption = null;
                continue;
            }

            if (pendingOption is null)
            {
                return new CliParseResult(false, false, null, $"Unexpected value '{raw}'. Prefix options with '-' or '--'.");
            }

            normalized[pendingOption] = raw;
            pendingOption = null;
        }

        if (pendingOption is not null)
        {
            return new CliParseResult(false, false, null, $"Option '--{pendingOption}' requires a value.");
        }

        if (normalized.ContainsKey("version"))
        {
            return new CliParseResult(showHelp: false, showVersion: true, options: null, error: null);
        }

        if (!TryValidateMandatory(normalized, "instrument", out var instrumentValue, out var error))
        {
            return new CliParseResult(false, false, null, error);
        }

        if (!TryValidateMandatory(normalized, "from", out var fromValue, out error) ||
            !TryValidateMandatory(normalized, "to", out var toValue, out error))
        {
            return new CliParseResult(false, false, null, error);
        }

        if (!TryParseDate(fromValue!, out var fromUtc, out error) ||
            !TryParseDate(toValue!, out var toUtc, out error))
        {
            return new CliParseResult(false, false, null, error);
        }

        var toExclusive = toUtc.AddDays(1);
        if (toExclusive <= fromUtc)
        {
            return new CliParseResult(false, false, null, "'--to' must be after '--from'.");
        }

        var timeframe = DukascopyTimeframe.Tick;
        if (normalized.TryGetValue("timeframe", out var timeframeValue) && !string.IsNullOrWhiteSpace(timeframeValue))
        {
            if (!TimeframeAliases.TryGetValue(timeframeValue.Trim(), out timeframe))
            {
                return new CliParseResult(false, false, null, $"Timeframe '{timeframeValue}' is not supported.");
            }
        }

        var cacheRoot = normalized.TryGetValue("cache-root", out var cacheValue) && !string.IsNullOrWhiteSpace(cacheValue)
            ? cacheValue!
            : Path.Combine(Environment.CurrentDirectory, ".dukascopy-downloader-cache");

        var outputDir = normalized.TryGetValue("output", out var outputValue) && !string.IsNullOrWhiteSpace(outputValue)
            ? outputValue
            : null;

        var useCache = !normalized.ContainsKey("no-cache");
        var force = normalized.ContainsKey("force");
        var includeInactive = normalized.ContainsKey("include-inactive");
        var downloadOnly = normalized.ContainsKey("download-only");
        var inferTickSize = normalized.ContainsKey("infer-tick-size");
        var includeVolume = normalized.ContainsKey("include-volume") || !normalized.ContainsKey("no-volume");

        var concurrency = TryParsePositiveInt(normalized, "concurrency", Environment.ProcessorCount - 1, minValue: 1);
        var maxRetries = TryParsePositiveInt(normalized, "max-retries", 4, minValue: 0);

        var retryDelay = TryParseDuration(normalized, "retry-delay", TimeSpan.FromSeconds(5));
        var rateLimitPause = TryParseDuration(normalized, "rate-limit-pause", TimeSpan.FromSeconds(30));
        var rateLimitRetries = TryParsePositiveInt(normalized, "rate-limit-retries", 5, minValue: 1);

        var timezoneValue = normalized.TryGetValue("timezone", out var tz) ? tz : null;
        var dateFormatValue = normalized.TryGetValue("date-format", out var df) ? df : null;
        var tickSize = TryParseDecimal(normalized, "tick-size", out error);
        if (error is not null)
        {
            return new CliParseResult(false, false, null, error);
        }

        var spreadPoints = TryParsePositiveIntAllowNull(normalized, "spread-points", minValue: 1, out error);
        if (error is not null)
        {
            return new CliParseResult(false, false, null, error);
        }

        var minNonZeroDeltas = TryParsePositiveInt(normalized, "min-nonzero-deltas", 100, minValue: 1);
        var spreadAggregation = SpreadAggregation.Median;
        if (normalized.TryGetValue("spread-agg", out var aggValue) && !string.IsNullOrWhiteSpace(aggValue))
        {
            spreadAggregation = aggValue.Trim().ToLowerInvariant() switch
            {
                "median" => SpreadAggregation.Median,
                "min" => SpreadAggregation.Min,
                "mean" => SpreadAggregation.Mean,
                "last" => SpreadAggregation.Last,
                _ => SpreadAggregation.Median
            };
        }
        var includeSpread = normalized.ContainsKey("include-spread");
        var fixedVolume = TryParsePositiveIntAllowNull(normalized, "fixed-volume", minValue: 0, out error);
        if (error is not null)
        {
            return new CliParseResult(false, false, null, error);
        }

        var template = ExportTemplate.None;
        if (normalized.TryGetValue("export-template", out var templateValue) && !string.IsNullOrWhiteSpace(templateValue))
        {
            template = templateValue.Trim().ToLowerInvariant() switch
            {
                "mt5" or "metatrader5" or "metatrader" => ExportTemplate.MetaTrader5,
                _ => ExportTemplate.None
            };
        }

        if (!_generationFactory.TryCreate(
                timezoneValue,
                dateFormatValue,
                template,
                tickSize,
                spreadPoints,
                inferTickSize,
                minNonZeroDeltas,
                spreadAggregation,
                includeSpread,
                includeVolume,
                fixedVolume,
                out var generationOptions,
                out error))
        {
            return new CliParseResult(false, false, null, error);
        }

        var downloadOptions = new DownloadOptions(
            instrumentValue!.Trim().ToUpperInvariant(),
            fromUtc,
            toExclusive,
            timeframe,
            cacheRoot,
            outputDir,
            useCache,
            force,
            includeInactive,
            concurrency,
            maxRetries,
            retryDelay,
            rateLimitPause,
            rateLimitRetries);

        var appOptions = new AppOptions(downloadOptions, generationOptions, normalized.ContainsKey("verbose"), downloadOnly);

        return new CliParseResult(false, false, appOptions, null);
    }

    private static bool TryParseDate(string value, out DateTimeOffset utcDate, out string? error)
    {
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
        {
            var dateTime = dateOnly.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            utcDate = new DateTimeOffset(dateTime);
            error = null;
            return true;
        }

        utcDate = default;
        error = $"Date '{value}' must follow the 'YYYY-MM-DD' format.";
        return false;
    }

    private static TimeSpan TryParseDuration(IDictionary<string, string?> map, string key, TimeSpan fallback)
    {
        if (!map.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        if (TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return fallback;
    }

    private static int TryParsePositiveInt(IDictionary<string, string?> map, string key, int fallback, int minValue)
    {
        if (map.TryGetValue(key, out var raw) &&
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
            parsed >= minValue)
        {
            return parsed;
        }

        return Math.Max(minValue, fallback);
    }

    private static int? TryParsePositiveIntAllowNull(IDictionary<string, string?> map, string key, int minValue, out string? error)
    {
        error = null;
        if (!map.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= minValue)
        {
            return parsed;
        }

        error = $"Option '--{key}' must be an integer >= {minValue}.";
        return null;
    }

    private static decimal? TryParseDecimal(IDictionary<string, string?> map, string key, out string? error)
    {
        error = null;
        if (!map.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
        {
            return parsed;
        }

        error = $"Option '--{key}' must be a decimal greater than 0.";
        return null;
    }

    private static bool TryValidateMandatory(
        IDictionary<string, string?> map,
        string key,
        out string? value,
        out string? error)
    {
        if (!map.TryGetValue(key, out value) || string.IsNullOrWhiteSpace(value))
        {
            error = $"Option '--{key}' is required.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool IsOption(string token) =>
        token.StartsWith("-", StringComparison.Ordinal) && token.Length > 1;

    private static (string Name, string? Value, bool IsHelp) ExtractNameValue(string token)
    {
        string rawName;
        string? value = null;

        if (token.StartsWith("--", StringComparison.Ordinal))
        {
            var payload = token[2..];
            var eqIndex = payload.IndexOf('=', StringComparison.Ordinal);
            if (eqIndex >= 0)
            {
                rawName = payload[..eqIndex];
                value = payload[(eqIndex + 1)..];
            }
            else
            {
                rawName = payload;
            }
        }
        else
        {
            rawName = token[1..];
        }

        var normalizedName = Aliases.TryGetValue(rawName, out var mapped)
            ? mapped
            : rawName.ToLowerInvariant();

        var isHelp = string.Equals(normalizedName, "help", StringComparison.OrdinalIgnoreCase);
        return (normalizedName, value, isHelp);
    }
}
