using System.Globalization;
using DukascopyDownloader.Logging;

namespace DukascopyDownloader.Cli.Logging;

internal sealed class ConsoleLogger : ILogger
{
    private readonly object _gate = new();
    private bool _progressActive;
    private int _progressWidth;
    private long _lastProgressTick;
    private int _spinnerIndex;
    private readonly char[] _spinner = new[] { '|', '/', '-', '\\' };

    public bool VerboseEnabled { get; set; }

    public void Info(string message) => Write("INFO", message, ConsoleColor.Gray);
    public void Success(string message) => Write(" OK ", message, ConsoleColor.Green);
    public void Warn(string message) => Write("WARN", message, ConsoleColor.Yellow);
    public void Error(string message) => Write("FAIL", message, ConsoleColor.Red);

    public void Progress(string message)
    {
        lock (_gate)
        {
            var padded = message.PadRight(_progressWidth);
            _progressWidth = padded.Length;
            _progressActive = true;
            Console.Write("\r" + padded);
        }
    }

    public void CompleteProgressLine()
    {
        lock (_gate)
        {
            if (_progressActive)
            {
                Console.WriteLine();
                _progressActive = false;
                _progressWidth = 0;
            }
        }
    }

    public void Verbose(string message)
    {
        if (VerboseEnabled)
        {
            Write("VERB", message, ConsoleColor.DarkGray);
        }
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
}
