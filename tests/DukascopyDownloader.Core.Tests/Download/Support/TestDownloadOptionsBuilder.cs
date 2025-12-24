using DukascopyDownloader.Download;

namespace DukascopyDownloader.Tests.Download.Support;

internal sealed class TestDownloadOptionsBuilder
{
    private string _instrument = "EURUSD";
    private DateTimeOffset _from = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private DateTimeOffset _to;
    private DukascopyTimeframe _timeframe = DukascopyTimeframe.Tick;
    private string _cache = Path.Combine(Path.GetTempPath(), $"dukascopy-test-{Guid.NewGuid():N}");
    private string? _output = null;
    private bool _useCache = false;
    private bool _force = false;
    private bool _includeInactive = false;
    private int _concurrency = 2;
    private int _maxRetries = 1;
    private TimeSpan _retryDelay = TimeSpan.FromMilliseconds(5);
    private TimeSpan _rateLimitPause = TimeSpan.FromMilliseconds(5);
    private int _rateLimitRetries = 2;

    public TestDownloadOptionsBuilder()
    {
        _to = _from.AddHours(1);
    }

    public TestDownloadOptionsBuilder WithRange(DateTimeOffset from, DateTimeOffset to)
    {
        _from = from;
        _to = to;
        return this;
    }

    public TestDownloadOptionsBuilder WithConcurrency(int concurrency)
    {
        _concurrency = concurrency;
        return this;
    }

    public TestDownloadOptionsBuilder WithMaxRetries(int maxRetries)
    {
        _maxRetries = maxRetries;
        return this;
    }

    public TestDownloadOptionsBuilder WithRetryDelay(TimeSpan delay)
    {
        _retryDelay = delay;
        return this;
    }

    public TestDownloadOptionsBuilder WithRateLimit(TimeSpan pause, int retries)
    {
        _rateLimitPause = pause;
        _rateLimitRetries = retries;
        return this;
    }

    public DownloadOptions Build() => new(
        _instrument,
        _from,
        _to,
        _timeframe,
        _cache,
        _output,
        _useCache,
        _force,
        _includeInactive,
        _concurrency,
        _maxRetries,
        _retryDelay,
        _rateLimitPause,
        _rateLimitRetries);
}
