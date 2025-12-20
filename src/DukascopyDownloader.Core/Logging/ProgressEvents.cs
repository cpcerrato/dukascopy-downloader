namespace DukascopyDownloader.Core.Logging;

public sealed record DownloadProgressSnapshot(
    int Total,
    int Completed,
    int New,
    int Cache,
    int Missing,
    int Failed,
    string? Stage = null,
    bool IsFinal = false);

public sealed record GenerationProgressSnapshot(
    int Total,
    int Completed,
    string? Stage = null,
    bool IsFinal = false);

internal sealed class NullProgress<T> : IProgress<T>
{
    public static readonly NullProgress<T> Instance = new();

    private NullProgress() { }

    public void Report(T value)
    {
        // Intentionally no-op
    }
}
