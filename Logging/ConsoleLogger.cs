using System.Globalization;

namespace DukascopyDownloader.Logging;

internal sealed class ConsoleLogger
{
    private readonly object _gate = new();
    public bool VerboseEnabled { get; set; }

    public void Info(string message) => Write("INFO", message, ConsoleColor.Gray);
    public void Success(string message) => Write(" OK ", message, ConsoleColor.Green);
    public void Warn(string message) => Write("WARN", message, ConsoleColor.Yellow);
    public void Error(string message) => Write("FAIL", message, ConsoleColor.Red);

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
            var timestamp = DateTimeOffset.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            var previousColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write($"[{timestamp}] {level}: ");
            Console.ForegroundColor = previousColor;
            Console.WriteLine(message);
        }
    }
}
