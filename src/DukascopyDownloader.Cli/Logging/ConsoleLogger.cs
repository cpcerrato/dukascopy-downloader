using System.Globalization;
using System.IO;
using DukascopyDownloader.Core.Logging;
using System.Linq;
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
    private int _progressLines;
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
            var (main, mainColor, details) = FormatDownloadSnapshot(snapshot, NextSpinner());
            RenderProgress(main, mainColor, details, snapshot.IsFinal);
        }
    }

    public void Report(GenerationProgressSnapshot snapshot)
    {
        lock (_gate)
        {
            var (main, mainColor, details) = FormatGenerationSnapshot(snapshot, NextSpinner());
            RenderProgress(main, mainColor, details, snapshot.IsFinal);
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
            ClearProgressBlock();

            var timestamp = DateTimeOffset.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            var previousColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write($"[{timestamp}] {level}: ");
            Console.ForegroundColor = previousColor;
            Console.WriteLine(message);
        }
    }

    private void ClearProgressBlock()
    {
        if (!_progressActive)
        {
            return;
        }

        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Cyan;

        Console.Write("\r");
        if (_progressLines > 0)
        {
            Console.Write($"\u001b[{_progressLines - 1}F");
        }

        // Clear from cursor to end of screen to avoid leaving blank padding.
        Console.Write("\u001b[J");

        Console.ForegroundColor = prev;
        _progressActive = false;
        _progressWidth = 0;
        _progressLines = 0;
    }

    private static (string Main, ConsoleColor MainColor, IReadOnlyList<(string Line, ConsoleColor Color)>? Details) FormatDownloadSnapshot(DownloadProgressSnapshot snapshot, char spinner)
    {
        var percent = snapshot.Total == 0 ? 100 : (int)Math.Round((double)snapshot.Completed * 100 / snapshot.Total);
        var stage = string.IsNullOrWhiteSpace(snapshot.Stage) ? "" : $" {snapshot.Stage}";
        var pending = Math.Max(0, snapshot.Total - snapshot.Completed);
        var counters = $"New {snapshot.New} | Cache {snapshot.Cache} | Missing {snapshot.Missing} | Failed {snapshot.Failed}";
        var main = $"Downloads {spinner}{stage} {percent,3}% ({snapshot.Completed}/{snapshot.Total}, pending {pending}) | {counters}";
        var mainColor = ConsoleColor.Green;
        if (snapshot.InFlight is { Count: > 0 })
        {
            var detailLines = BuildInFlightLines(snapshot.InFlight, ConsoleColor.Blue);
            return (main, mainColor, detailLines);
        }

        return (main, mainColor, null);
    }

    private static (string Main, ConsoleColor MainColor, IReadOnlyList<(string Line, ConsoleColor Color)>? Details) FormatGenerationSnapshot(GenerationProgressSnapshot snapshot, char spinner)
    {
        var percent = snapshot.Total == 0 ? 100 : (int)Math.Round((double)snapshot.Completed * 100 / snapshot.Total);
        var stage = string.IsNullOrWhiteSpace(snapshot.Stage) ? "" : $" {snapshot.Stage}";
        var pending = Math.Max(0, snapshot.Total - snapshot.Completed);
        var main = $"Generation {spinner}{stage}: {percent,3}% ({snapshot.Completed}/{snapshot.Total}, pending {pending})";
        return (main, ConsoleColor.Cyan, null);
    }

    private char NextSpinner() => _spinner[_spinnerIndex++ % _spinner.Length];

    private static IReadOnlyList<(string Line, ConsoleColor Color)> BuildInFlightLines(IReadOnlyList<string> items, ConsoleColor color)
    {
        var parsed = new List<(string Symbol, string Tf, string Window)>(items.Count);
        foreach (var item in items)
        {
            var dash = item.IndexOf('-');
            var at = item.IndexOf('@');
            if (dash > 0 && at > dash)
            {
                var symbol = item[..dash];
                var tf = item[(dash + 1)..at];
                var window = item[(at + 1)..];
                parsed.Add((symbol, tf, window));
            }
            else
            {
                parsed.Add((item, string.Empty, string.Empty));
            }
        }

        var wSymbol = parsed.Count == 0 ? 0 : parsed.Max(p => p.Symbol.Length);
        var wTf = parsed.Count == 0 ? 0 : parsed.Max(p => p.Tf.Length);

        var lines = new List<(string Line, ConsoleColor Color)>(parsed.Count + 1)
        {
            ("In-flight:", color)
        };

        foreach (var p in parsed)
        {
            var symbol = p.Symbol.PadRight(wSymbol);
            var tf = p.Tf.PadRight(wTf);
            var window = p.Window;
            var line = $"  {symbol}  {tf}  {window}";
            lines.Add((line.TrimEnd(), color));
        }

        return lines;
    }

    private void RenderProgress(string main, ConsoleColor mainColor, IReadOnlyList<(string Line, ConsoleColor Color)>? details, bool isFinal)
    {
        var maxDetail = details is { Count: > 0 } ? details.Max(l => l.Line.Length) : 0;
        var desiredWidth = Math.Max(main.Length, maxDetail);
        var consoleWidth = Console.BufferWidth > 0 ? Console.BufferWidth - 1 : desiredWidth;
        _progressWidth = Math.Min(Math.Max(_progressWidth, desiredWidth), Math.Max(10, consoleWidth));

        var lines = new List<(string Line, ConsoleColor Color)>
        {
            (Pad(main, _progressWidth), mainColor)
        };
        if (details is { Count: > 0 })
        {
            lines.AddRange(details.Select(d => (Pad(d.Line, _progressWidth), d.Color)));
        }

        while (lines.Count < _progressLines)
        {
            lines.Add((Pad(string.Empty, _progressWidth), mainColor));
        }

        var prev = Console.ForegroundColor;

        if (_progressActive)
        {
            if (_progressLines > 0)
            {
                Console.Write($"\r\u001b[{_progressLines - 1}F");
            }
            else
            {
                Console.Write("\r");
            }
        }

        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                Console.WriteLine();
            }

            Console.ForegroundColor = lines[i].Color;
            Console.Write(lines[i].Line);
        }

        Console.ForegroundColor = prev;

        if (isFinal)
        {
            Console.WriteLine();
            _progressActive = false;
            _progressWidth = 0;
            _progressLines = 0;
        }
        else
        {
            _progressActive = true;
            _progressLines = lines.Count;
        }
    }

    private static string Pad(string message, int width)
    {
        if (width <= 0)
        {
            return message;
        }

        return message.Length > width
            ? message[..width]
            : message.PadRight(width);
    }
}
