using DukascopyDownloader.Logging;

namespace DukascopyDownloader.Core.Tests.Support;

internal sealed class TestLogger : ILogger
{
    public bool VerboseEnabled { get; set; }

    public void Info(string message) { }
    public void Success(string message) { }
    public void Warn(string message) { }
    public void Error(string message) { }
    public void Verbose(string message) { }
    public void Progress(string message) { }
    public void CompleteProgressLine() { }
}
