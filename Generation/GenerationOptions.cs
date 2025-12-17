using System.Globalization;

namespace DukascopyDownloader.Generation;

internal sealed record GenerationOptions(TimeZoneInfo TimeZone, string? DateFormat)
{
    public bool HasCustomSettings =>
        TimeZone != TimeZoneInfo.Utc || !string.IsNullOrWhiteSpace(DateFormat);
}

internal sealed class GenerationOptionsFactory
{
    public bool TryCreate(string? timeZoneValue, string? formatValue, out GenerationOptions options, out string? error)
    {
        var timeZone = TimeZoneInfo.Utc;
        if (!string.IsNullOrWhiteSpace(timeZoneValue))
        {
            if (!TryResolveTimeZone(timeZoneValue!, out timeZone, out error))
            {
                options = new GenerationOptions(TimeZoneInfo.Utc, null);
                return false;
            }
        }

        string? dateFormat = null;
        if (!string.IsNullOrWhiteSpace(formatValue))
        {
            if (!TryValidateDateFormat(formatValue!, out error))
            {
                options = new GenerationOptions(timeZone, null);
                return false;
            }

            dateFormat = formatValue!.Trim();
        }

        options = new GenerationOptions(timeZone, dateFormat);
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
