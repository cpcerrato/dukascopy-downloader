using System.Globalization;
using DukascopyDownloader.Core.Logging;
using Microsoft.Extensions.Logging;

namespace DukascopyDownloader.Cli.Logging;

internal sealed class ConsoleLogger : ILogger, IProgress<DownloadProgressSnapshot>, IProgress<GenerationProgressSnapshot>
{
    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }

    private readonly object _gate = new();
    private bool _progressActive;
    private int _progressWidth;
    private int _spinnerIndex;
    private readonly char[] _spinner = new[] { '|', '/', '-', '\\' };
    private readonly LogLevel _minLevel;

    public ConsoleLogger(bool verbose = false)
    {
        _minLevel = verbose ? LogLevel.Debug : LogLevel.Information;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        if (exception != null)
        {
            message = message + Environment.NewLine + exception;
        }

        var (levelLabel, color) = ResolveStyle(logLevel, eventId);
        Write(levelLabel, message, color);
    }

    public void Report(DownloadProgressSnapshot snapshot)
    {
        lock (_gate)
        {
            var message = FormatDownloadSnapshot(snapshot, NextSpinner());
            var padded = PadAndClamp(message);
            _progressActive = true;
            Console.Write("\r" + padded);
            if (snapshot.IsFinal)
            {
                Console.WriteLine();
                _progressActive = false;
                _progressWidth = 0;
            }
        }
    }

    public void Report(GenerationProgressSnapshot snapshot)
    {
        lock (_gate)
        {
            var message = FormatGenerationSnapshot(snapshot, NextSpinner());
            var padded = PadAndClamp(message);
            _progressActive = true;
            Console.Write("\r" + padded);
            if (snapshot.IsFinal)
            {
                Console.WriteLine();
                _progressActive = false;
                _progressWidth = 0;
            }
        }
    }

    private (string Label, ConsoleColor Color) ResolveStyle(LogLevel level, EventId eventId)
    {
        if (eventId.Id == 1000 && eventId.Name == "Success")
        {
            return (" OK ", ConsoleColor.Green);
        }

        return level switch
        {
            LogLevel.Warning => ("WARN", ConsoleColor.Yellow),
            LogLevel.Error or LogLevel.Critical => ("FAIL", ConsoleColor.Red),
            LogLevel.Debug or LogLevel.Trace => ("VERB", ConsoleColor.DarkGray),
            _ => ("INFO", ConsoleColor.Gray),
        };
    }

    private void Write(string level, string message, ConsoleColor color)
    {
        lock (_gate)
        {
            if (_progressActive)
            {
                Console.WriteLine();
                _progressActive = false;
                _progressWidth = 0;
            }

            var timestamp = DateTimeOffset.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            var previousColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write($"[{timestamp}] {level}: ");
            Console.ForegroundColor = previousColor;
            Console.WriteLine(message);
        }
    }

    private static string FormatDownloadSnapshot(DownloadProgressSnapshot snapshot, char spinner)
    {
        var percent = snapshot.Total == 0 ? 100 : (int)Math.Round((double)snapshot.Completed * 100 / snapshot.Total);
        var stage = string.IsNullOrWhiteSpace(snapshot.Stage) ? "" : $" {snapshot.Stage}";
        var pending = Math.Max(0, snapshot.Total - snapshot.Completed);
        var counters = $"New {snapshot.New} | Cache {snapshot.Cache} | Missing {snapshot.Missing} | Failed {snapshot.Failed}";

        return $"Downloads {spinner} {percent,3}% ({snapshot.Completed}/{snapshot.Total}, pending {pending}) | {counters}";
    }

    private static string FormatGenerationSnapshot(GenerationProgressSnapshot snapshot, char spinner)
    {
        var percent = snapshot.Total == 0 ? 100 : (int)Math.Round((double)snapshot.Completed * 100 / snapshot.Total);
        var stage = string.IsNullOrWhiteSpace(snapshot.Stage) ? "" : $" {snapshot.Stage}";
        var pending = Math.Max(0, snapshot.Total - snapshot.Completed);
        return $"Generation {spinner}{stage}: {percent,3}% ({snapshot.Completed}/{snapshot.Total}, pending {pending})";
    }

    private char NextSpinner() => _spinner[_spinnerIndex++ % _spinner.Length];

    private string PadAndClamp(string message)
    {
        var targetWidth = Math.Max(_progressWidth, message.Length);
        var maxWidth = Console.BufferWidth > 0 ? Console.BufferWidth - 1 : targetWidth;
        targetWidth = Math.Min(targetWidth, Math.Max(10, maxWidth));
        _progressWidth = targetWidth;
        return message.Length > targetWidth
            ? message[..targetWidth]
            : message.PadRight(targetWidth);
    }
}
