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
    int? FixedVolume = null)
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
        FixedVolume.HasValue;
}

internal sealed class GenerationOptionsFactory
{
    /// <summary>
    /// Builds validated generation options from raw CLI inputs (timezone, date format, spread/volume settings, templates).
    /// </summary>
    public bool TryCreate(
        string? timeZoneValue,
        string? formatValue,
        ExportTemplate template,
        decimal? tickSize,
        int? spreadPoints,
        bool inferTickSize,
        int minNonZeroDeltas,
        SpreadAggregation spreadAggregation,
        bool includeSpread,
        bool includeVolume,
        int? fixedVolume,
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

        var includeHeader = true;
        var includeSpreadFlag = includeSpread;
        var includeVolumeFlag = includeVolume;
        if (template == ExportTemplate.MetaTrader5)
        {
            includeHeader = false;
            dateFormat ??= "yyyy.MM.dd HH:mm:ss.fff";
            includeSpreadFlag = true;
            includeVolumeFlag = true; // MT5 requires volumes
        }

        options = new GenerationOptions(
            timeZone,
            dateFormat,
            includeHeader,
            template,
            tickSize,
            spreadPoints,
            inferTickSize,
            minNonZeroDeltas,
            spreadAggregation,
            includeSpreadFlag,
            includeVolumeFlag,
            fixedVolume);
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
