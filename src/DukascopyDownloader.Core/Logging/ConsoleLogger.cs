using System.Globalization;

namespace DukascopyDownloader.Logging;

internal sealed class ConsoleLogger
{
    private readonly object _gate = new();
    private bool _progressActive;
    private int _progressWidth;
    public bool VerboseEnabled { get; set; }

    /// <summary>
    /// Writes an informational message with timestamp.
    /// </summary>
    public void Info(string message) => Write("INFO", message, ConsoleColor.Gray);
    /// <summary>
    /// Writes a success message with timestamp.
    /// </summary>
    public void Success(string message) => Write(" OK ", message, ConsoleColor.Green);
    /// <summary>
    /// Writes a warning message with timestamp.
    /// </summary>
    public void Warn(string message) => Write("WARN", message, ConsoleColor.Yellow);
    /// <summary>
    /// Writes an error message with timestamp.
    /// </summary>
    public void Error(string message) => Write("FAIL", message, ConsoleColor.Red);

    /// <summary>
    /// Renders an in-place progress line (overwritten on each call).
    /// </summary>
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

    /// <summary>
    /// Finalizes the current progress line by writing a newline.
    /// </summary>
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
