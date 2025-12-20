namespace DukascopyDownloader.Logging;

/// <summary>
/// Minimal logging interface used by the core library. Progress calls are optional and may be no-ops.
/// </summary>
public interface ILogger
{
    bool VerboseEnabled { get; set; }

    void Info(string message);
    void Success(string message);
    void Warn(string message);
    void Error(string message);
    void Verbose(string message);

    /// <summary>
    /// Optional in-place progress update. Implementations may ignore.
    /// </summary>
    void Progress(string message);

    /// <summary>
    /// Optional completion of a progress line. Implementations may ignore.
    /// </summary>
    void CompleteProgressLine();
}
