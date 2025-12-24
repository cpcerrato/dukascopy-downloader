namespace DukascopyDownloader.Download;

internal static class DownloadSlicePlanner
{
    /// <summary>
    /// Builds the list of BI5 slices required to cover the requested range and timeframe.
    /// Tick feeds are hourly; minute feeds are daily; hour feeds are monthly; day feeds are yearly.
    /// </summary>
    /// <param name="options">Download options describing the instrument, timeframe, and date range.</param>
    /// <returns>Enumerable of <see cref="DownloadSlice"/> objects covering the requested range.</returns>
    public static IEnumerable<DownloadSlice> Build(DownloadOptions options)
    {
        var feedKind = options.Timeframe.GetFeedKind();
        var sides = ResolveSides(options.SidePreference);

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
                    feedKind,
                    DukascopyPriceSide.Bid);
            }

            yield break;
        }

        if (feedKind == DukascopyFeedKind.Minute)
        {
            var startDay = AlignDownToDay(options.FromUtc);
            var endDayExclusive = AlignUpToDay(options.ToUtc);

            for (var cursor = startDay; cursor < endDayExclusive; cursor = cursor.AddDays(1))
            {
                foreach (var side in sides)
                {
                    yield return new DownloadSlice(
                        options.Instrument,
                        cursor,
                        cursor.AddDays(1),
                        options.Timeframe,
                        feedKind,
                        side);
                }
            }

            yield break;
        }

        if (feedKind == DukascopyFeedKind.Hour)
        {
            var startMonth = AlignDownToMonth(options.FromUtc);
            var endMonthExclusive = AlignUpToMonth(options.ToUtc);

            for (var cursor = startMonth; cursor < endMonthExclusive; cursor = cursor.AddMonths(1))
            {
                foreach (var side in sides)
                {
                    yield return new DownloadSlice(
                        options.Instrument,
                        cursor,
                        cursor.AddMonths(1),
                        options.Timeframe,
                        feedKind,
                        side);
                }
            }

            yield break;
        }

        // Day feed
        var startYear = AlignDownToYear(options.FromUtc);
        var endYearExclusive = AlignUpToYear(options.ToUtc);

        for (var cursor = startYear; cursor < endYearExclusive; cursor = cursor.AddYears(1))
        {
            foreach (var side in sides)
            {
                yield return new DownloadSlice(
                    options.Instrument,
                    cursor,
                    cursor.AddYears(1),
                    options.Timeframe,
                    feedKind,
                    side);
            }
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

    private static DateTimeOffset AlignDownToMonth(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private static DateTimeOffset AlignUpToMonth(DateTimeOffset value)
    {
        var aligned = AlignDownToMonth(value);
        return value.ToUniversalTime() == aligned ? aligned : aligned.AddMonths(1);
    }

    private static DateTimeOffset AlignDownToYear(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Year, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private static DateTimeOffset AlignUpToYear(DateTimeOffset value)
    {
        var aligned = AlignDownToYear(value);
        return value.ToUniversalTime() == aligned ? aligned : aligned.AddYears(1);
    }

    private static IEnumerable<DukascopyPriceSide> ResolveSides(PriceSidePreference preference) =>
        preference switch
        {
            PriceSidePreference.Bid => new[] { DukascopyPriceSide.Bid },
            PriceSidePreference.Ask => new[] { DukascopyPriceSide.Ask },
            _ => new[] { DukascopyPriceSide.Bid, DukascopyPriceSide.Ask }
        };
}
