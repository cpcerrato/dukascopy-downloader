using System.Globalization;

namespace DukascopyDownloader.Generation;

internal enum ExportTemplate
{
    None = 0,
    MetaTrader5 = 1
}

internal enum SpreadAggregation
{
    Median = 0,
    Min = 1,
    Mean = 2,
    Last = 3
}

internal sealed record GenerationOptions(
    TimeZoneInfo TimeZone,
    string? DateFormat,
    bool IncludeHeader = true,
    ExportTemplate Template = ExportTemplate.None,
    decimal? TickSize = null,
    int? SpreadPoints = null,
    bool InferTickSize = false,
    int MinNonZeroDeltas = 100,
    SpreadAggregation SpreadAggregation = SpreadAggregation.Median,
    bool IncludeSpread = false,
    bool IncludeVolume = true,
    int? FixedVolume = null,
    bool PreferTicks = false)
{
    /// <summary>
    /// Indicates whether non-default timestamp or template settings are in use.
    /// </summary>
    public bool HasCustomSettings =>
        TimeZone != TimeZoneInfo.Utc ||
        !string.IsNullOrWhiteSpace(DateFormat) ||
        Template != ExportTemplate.None ||
        TickSize.HasValue ||
        SpreadPoints.HasValue ||
        InferTickSize ||
        IncludeSpread ||
        !IncludeVolume ||
        FixedVolume.HasValue ||
        PreferTicks;
}

internal sealed class GenerationOptionsFactory
{
    /// <summary>
    /// Builds validated generation options from raw inputs (timezone, date format, spread/volume settings, templates).
    /// </summary>
    /// <param name="timeZoneValue">Timezone identifier (IANA/Windows); null for UTC.</param>
    /// <param name="formatValue">Custom date format; null to use defaults (UTC ms or template default).</param>
    /// <param name="template">Export template (e.g., MetaTrader5).</param>
    /// <param name="includeHeaderOverride">Optional explicit header toggle; null uses the template default (on for none, off for MT5).</param>
    /// <param name="tickSize">Explicit tick size/point value for spread calculation.</param>
    /// <param name="spreadPoints">Fixed spread in points (fallback when no tick size).</param>
    /// <param name="inferTickSize">Whether to infer tick size from tick deltas.</param>
    /// <param name="minNonZeroDeltas">Minimum non-zero deltas to accept inference.</param>
    /// <param name="spreadAggregation">Aggregation mode for spreads per candle.</param>
    /// <param name="includeSpread">Whether to append spread column for non-template exports.</param>
    /// <param name="includeVolume">Whether to include volume column for non-template exports.</param>
    /// <param name="fixedVolume">Optional fixed volume per candle.</param>
    /// <param name="preferTicks">Whether to aggregate candles from tick data instead of Dukascopy M1 feed.</param>
    /// <param name="options">Output generation options when successful.</param>
    /// <param name="error">Error message when validation fails.</param>
    /// <returns>True when options are valid; otherwise false with an error message.</returns>
    public bool TryCreate(
        string? timeZoneValue,
        string? formatValue,
        ExportTemplate template,
        bool? includeHeaderOverride,
        decimal? tickSize,
        int? spreadPoints,
        bool inferTickSize,
        int minNonZeroDeltas,
        SpreadAggregation spreadAggregation,
        bool includeSpread,
        bool includeVolume,
        int? fixedVolume,
        bool preferTicks,
        out GenerationOptions options,
        out string? error)
    {
        var timeZone = TimeZoneInfo.Utc;
        if (!string.IsNullOrWhiteSpace(timeZoneValue))
        {
            if (!TryResolveTimeZone(timeZoneValue!, out timeZone, out error))
            {
                options = new GenerationOptions(TimeZoneInfo.Utc, null, IncludeHeader: true, Template: ExportTemplate.None);
                return false;
            }
        }

        string? dateFormat = null;
        if (!string.IsNullOrWhiteSpace(formatValue))
        {
            if (!TryValidateDateFormat(formatValue!, out error))
            {
                options = new GenerationOptions(timeZone, null, IncludeHeader: true, Template: ExportTemplate.None);
                return false;
            }

            dateFormat = formatValue!.Trim();
        }

        var includeHeader = includeHeaderOverride ?? true;
        var includeSpreadFlag = includeSpread;
        var includeVolumeFlag = includeVolume;
        var spreadPointsValue = spreadPoints;
        if (template == ExportTemplate.MetaTrader5)
        {
            includeHeader = includeHeaderOverride ?? false;
            dateFormat ??= "yyyy.MM.dd HH:mm:ss.fff";
            includeSpreadFlag = true;
            includeVolumeFlag = true; // MT5 requires volumes
            if (!tickSize.HasValue && !spreadPointsValue.HasValue && !inferTickSize)
            {
                // Default MT5 spread to 0 points when nothing is specified to avoid hard failures.
                spreadPointsValue = 0;
            }
        }

        options = new GenerationOptions(
            timeZone,
            dateFormat,
            includeHeader,
            template,
            tickSize,
            spreadPointsValue,
            inferTickSize,
            minNonZeroDeltas,
            spreadAggregation,
            includeSpreadFlag,
            includeVolumeFlag,
            fixedVolume,
            preferTicks);
        error = null;
        return true;
    }

    private static bool TryValidateDateFormat(string format, out string? error)
    {
        try
        {
            _ = DateTime.UtcNow.ToString(format, CultureInfo.InvariantCulture);
            error = null;
            return true;
        }
        catch (FormatException)
        {
            error = $"Date format '{format}' is not valid.";
            return false;
        }
    }

    private static bool TryResolveTimeZone(string value, out TimeZoneInfo timeZone, out string? error)
    {
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(value);
            error = null;
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }

        var normalizedInput = Normalize(value);
        var candidate = TimeZoneInfo.GetSystemTimeZones()
            .FirstOrDefault(tz =>
                Normalize(tz.Id).Equals(normalizedInput, StringComparison.OrdinalIgnoreCase) ||
                Normalize(tz.DisplayName).Contains(normalizedInput, StringComparison.OrdinalIgnoreCase));

        if (candidate is not null)
        {
            timeZone = candidate;
            error = null;
            return true;
        }

        timeZone = TimeZoneInfo.Utc;
        error = $"Timezone '{value}' was not found on this system.";
        return false;
    }

    private static string Normalize(string value) =>
        new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}
