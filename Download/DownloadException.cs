namespace DukascopyDownloader.Download;

internal sealed class DownloadException : Exception
{
    public DownloadException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
