namespace DukascopyDownloader.Download;

internal static class DownloadSlicePlanner
{
    /// <summary>
    /// Builds the list of BI5 slices required to cover the requested range and timeframe.
    /// Tick feeds are hourly; minute feeds are daily.
    /// </summary>
    /// <param name="options">Download options describing the instrument, timeframe, and date range.</param>
    /// <returns>Enumerable of <see cref="DownloadSlice"/> objects covering the requested range.</returns>
    public static IEnumerable<DownloadSlice> Build(DownloadOptions options)
    {
        var feedKind = options.Timeframe.GetFeedKind();

        if (feedKind == DukascopyFeedKind.Tick)
        {
            var startHour = AlignDownToHour(options.FromUtc);
            var endHourExclusive = AlignUpToHour(options.ToUtc);

            for (var cursor = startHour; cursor < endHourExclusive; cursor = cursor.AddHours(1))
            {
                yield return new DownloadSlice(
                    options.Instrument,
                    cursor,
                    cursor.AddHours(1),
                    options.Timeframe,
                    feedKind);
            }

            yield break;
        }

        var startDay = AlignDownToDay(options.FromUtc);
        var endDayExclusive = AlignUpToDay(options.ToUtc);

        for (var cursor = startDay; cursor < endDayExclusive; cursor = cursor.AddDays(1))
        {
            yield return new DownloadSlice(
                options.Instrument,
                cursor,
                cursor.AddDays(1),
                options.Timeframe,
                feedKind);
        }
    }

    private static DateTimeOffset AlignDownToHour(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
    }

    private static DateTimeOffset AlignUpToHour(DateTimeOffset value)
    {
        var aligned = AlignDownToHour(value);
        return value.ToUniversalTime() == aligned ? aligned : aligned.AddHours(1);
    }

    private static DateTimeOffset AlignDownToDay(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);
    }

    private static DateTimeOffset AlignUpToDay(DateTimeOffset value)
    {
        var aligned = AlignDownToDay(value);
        var utc = value.ToUniversalTime();
        return utc.Date == aligned.Date ? aligned : aligned.AddDays(1);
    }
}
