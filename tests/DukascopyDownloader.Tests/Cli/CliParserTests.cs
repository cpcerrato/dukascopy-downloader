using DukascopyDownloader.Cli;
using DukascopyDownloader.Download;
using DukascopyDownloader.Generation;
using Xunit;

namespace DukascopyDownloader.Tests.Cli;

public class CliParserTests
{
    private static CliParser CreateParser() => new(new GenerationOptionsFactory());

    [Fact]
    public void Parse_WithValidArguments_ReturnsConfiguredOptions()
    {
        var args = new[]
        {
            "--instrument", "EURUSD",
            "--from", "2025-01-14",
            "--to", "2025-01-18",
            "--timeframe", "d1",
            "--timezone", "America/New_York",
            "--date-format", "yyyy.MM.dd HH:mm:ss",
            "--include-inactive",
            "--verbose"
        };

        var parser = CreateParser();
        var result = parser.Parse(args);

        Assert.True(result.IsValid);
        Assert.False(result.ShowHelp);

        var options = result.Options!;
        Assert.True(options.Verbose);
        Assert.Equal(DukascopyTimeframe.Day1, options.Download.Timeframe);
        Assert.True(options.Download.IncludeInactivePeriods);
        Assert.Equal(new DateTimeOffset(2025, 1, 14, 0, 0, 0, TimeSpan.Zero), options.Download.FromUtc);
        Assert.Equal(new DateTimeOffset(2025, 1, 19, 0, 0, 0, TimeSpan.Zero), options.Download.ToUtc);

        Assert.Equal("America/New_York", options.Generation.TimeZone.Id);
        Assert.Equal("yyyy.MM.dd HH:mm:ss", options.Generation.DateFormat);
    }

    [Fact]
    public void Parse_WhenInstrumentMissing_ReturnsError()
    {
        var args = new[]
        {
            "--from", "2025-01-14",
            "--to", "2025-01-18"
        };

        var parser = CreateParser();
        var result = parser.Parse(args);

        Assert.False(result.IsValid);
        Assert.Contains("instrument", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_WhenToBeforeFrom_ReturnsError()
    {
        var args = new[]
        {
            "--instrument", "EURUSD",
            "--from", "2025-01-18",
            "--to", "2025-01-14"
        };

        var parser = CreateParser();
        var result = parser.Parse(args);

        Assert.False(result.IsValid);
        Assert.Contains("'--to' must be after '--from'", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
