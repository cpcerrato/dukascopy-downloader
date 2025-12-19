using System.Globalization;

namespace DukascopyDownloader.Generation;

internal enum ExportTemplate
{
    None = 0,
    MetaTrader5 = 1
}

internal sealed record GenerationOptions(TimeZoneInfo TimeZone, string? DateFormat, bool IncludeHeader = true, ExportTemplate Template = ExportTemplate.None)
{
    public bool HasCustomSettings =>
        TimeZone != TimeZoneInfo.Utc || !string.IsNullOrWhiteSpace(DateFormat) || Template != ExportTemplate.None;
}

internal sealed class GenerationOptionsFactory
{
    public bool TryCreate(string? timeZoneValue, string? formatValue, ExportTemplate template, out GenerationOptions options, out string? error)
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
        if (template == ExportTemplate.MetaTrader5)
        {
            includeHeader = false;
            dateFormat ??= "yyyy.MM.dd HH:mm:ss";
        }

        options = new GenerationOptions(timeZone, dateFormat, includeHeader, template);
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
