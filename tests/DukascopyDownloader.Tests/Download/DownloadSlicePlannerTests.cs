using DukascopyDownloader.Download;
using Xunit;

namespace DukascopyDownloader.Tests.Download;

public class DownloadSlicePlannerTests
{
    [Fact]
    public void Build_ForTickTimeframe_ReturnsHourlySlices()
    {
        var options = CreateOptions(DukascopyTimeframe.Tick);

        var slices = DownloadSlicePlanner.Build(options).ToList();

        Assert.Equal(24, slices.Count);
        Assert.All(slices, slice => Assert.Equal(DukascopyFeedKind.Tick, slice.FeedKind));
        Assert.Equal(new DateTimeOffset(2025, 1, 14, 0, 0, 0, TimeSpan.Zero), slices.First().Start);
        Assert.Equal(new DateTimeOffset(2025, 1, 14, 23, 0, 0, TimeSpan.Zero), slices.Last().Start);
    }

    [Fact]
    public void Build_ForMinuteTimeframe_ReturnsDailySlices()
    {
        var options = CreateOptions(DukascopyTimeframe.Day1);

        var slices = DownloadSlicePlanner.Build(options).ToList();

        var slice = Assert.Single(slices);
        Assert.Equal(DukascopyFeedKind.Minute, slice.FeedKind);
        Assert.Equal(new DateTimeOffset(2025, 1, 14, 0, 0, 0, TimeSpan.Zero), slice.Start);
        Assert.Equal(new DateTimeOffset(2025, 1, 15, 0, 0, 0, TimeSpan.Zero), slice.End);
    }

    private static DownloadOptions CreateOptions(DukascopyTimeframe timeframe)
    {
        var start = new DateTimeOffset(2025, 1, 14, 0, 0, 0, TimeSpan.Zero);
        var end = start.AddDays(1);
        return new DownloadOptions(
            "EURUSD",
            start,
            end,
            timeframe,
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            null,
            true,
            false,
            false,
            1,
            0,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            1);
    }
}
