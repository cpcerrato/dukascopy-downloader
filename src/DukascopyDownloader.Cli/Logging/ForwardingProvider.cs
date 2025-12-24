using Microsoft.Extensions.Logging;

namespace DukascopyDownloader.Cli.Logging;

/// <summary>
/// Minimal logger provider that forwards to an existing ILogger (used to bridge CLI console logger into ILogger&lt;T&gt; dependencies).
/// </summary>
internal sealed class ForwardingProvider : ILoggerProvider
{
    private readonly ILogger _target;

    public ForwardingProvider(ILogger target)
    {
        _target = target;
    }

    public ILogger CreateLogger(string categoryName) => new ForwardingLogger(_target, categoryName);

    public void Dispose()
    {
        if (_target is IDisposable d)
        {
            d.Dispose();
        }
    }

    private sealed class ForwardingLogger : ILogger
    {
        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }

        private readonly ILogger _target;
        private readonly string _category;

        public ForwardingLogger(ILogger target, string category)
        {
            _target = target;
            _category = category;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => _target.BeginScope(state) ?? NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => _target.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _target.Log(logLevel, eventId, state, exception, formatter);
        }
    }
}
