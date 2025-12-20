using System;
using System.Globalization;
using System.Reflection;
using DukascopyDownloader.Generation;
using Xunit;

namespace DukascopyDownloader.Tests.Generation;

public class FormatTimestampDstTests
{
    private static string Format(DateTimeOffset local, GenerationOptions options)
    {
        var method = typeof(CsvGenerator).GetMethod("FormatTimestamp", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (string)method!.Invoke(null, new object[] { local, options })!;
    }

    [Fact]
    public void Formats_Through_DST_SpringForward()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var opts = new GenerationOptions(tz, "yyyy-MM-dd HH:mm:ss");

        var utc1 = new DateTimeOffset(2024, 3, 10, 6, 0, 0, TimeSpan.Zero);
        var utc2 = utc1.AddHours(1); // skip to 03:00 local

        var local1 = TimeZoneInfo.ConvertTime(utc1, tz);
        var local2 = TimeZoneInfo.ConvertTime(utc2, tz);

        Assert.Equal("2024-03-10 01:00:00", Format(local1, opts));
        Assert.Equal("2024-03-10 03:00:00", Format(local2, opts));
    }

    [Fact]
    public void Formats_Through_DST_FallBack()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var opts = new GenerationOptions(tz, "yyyy-MM-dd HH:mm:ss");

        var utc1 = new DateTimeOffset(2024, 11, 3, 5, 0, 0, TimeSpan.Zero);
        var utc2 = utc1.AddHours(1); // repeats 01:00 local but with new offset

        var local1 = TimeZoneInfo.ConvertTime(utc1, tz);
        var local2 = TimeZoneInfo.ConvertTime(utc2, tz);

        Assert.Equal("2024-11-03 01:00:00", Format(local1, opts));
        Assert.Equal("2024-11-03 01:00:00", Format(local2, opts));
    }
}
