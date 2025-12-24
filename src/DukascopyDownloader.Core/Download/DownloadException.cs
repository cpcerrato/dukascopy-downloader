namespace DukascopyDownloader.Download;

internal sealed class DownloadException : Exception
{
    /// <summary>
    /// Represents a fatal failure during a download run (e.g., exhausted retries).
    /// </summary>
    /// <param name="message">Error message describing the failure.</param>
    /// <param name="inner">Optional inner exception.</param>
    public DownloadException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
