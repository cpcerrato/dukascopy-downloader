using DukascopyDownloader.Core.Logging;
using Microsoft.Extensions.Logging;

namespace DukascopyDownloader.Core.Tests.Support;

internal sealed class TestLogger : ILogger, IProgress<DownloadProgressSnapshot>, IProgress<GenerationProgressSnapshot>
{
    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        // Intentionally no-op for tests
    }

    public void Report(DownloadProgressSnapshot value) { }

    public void Report(GenerationProgressSnapshot value) { }
}
