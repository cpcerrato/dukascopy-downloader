namespace DukascopyDownloader.Core.Logging;

/// <summary>
/// Snapshot of download progress, emitted through <see cref="IProgress{T}"/> while slices are processed.
/// </summary>
/// <param name="Total">Total slices scheduled for the run.</param>
/// <param name="Completed">Total slices finished (regardless of cache/new state).</param>
/// <param name="New">Slices successfully downloaded and verified.</param>
/// <param name="Cache">Slices served from cache.</param>
/// <param name="Missing">Slices that were unavailable/0-byte and intentionally skipped.</param>
/// <param name="Failed">Slices that exhausted retries and are recorded in the failure manifest.</param>
/// <param name="Stage">Optional stage label (e.g., “Downloading”, “Finalizing”).</param>
/// <param name="IsFinal">True when the run is complete and no more updates will follow.</param>
/// <param name="InFlight">Optional list of slices currently being processed.</param>
public sealed record DownloadProgressSnapshot(
    int Total,
    int Completed,
    int New,
    int Cache,
    int Missing,
    int Failed,
    string? Stage = null,
    bool IsFinal = false,
    IReadOnlyList<string>? InFlight = null);

/// <summary>
/// Snapshot of CSV generation progress, emitted through <see cref="IProgress{T}"/> while exports are produced.
/// </summary>
/// <param name="Total">Total slices/candles planned for generation.</param>
/// <param name="Completed">Number of slices/candles already written.</param>
/// <param name="Stage">Optional stage label (e.g., “Writing ticks”, “Aggregating m1”).</param>
/// <param name="IsFinal">True when generation has completed.</param>
public sealed record GenerationProgressSnapshot(
    int Total,
    int Completed,
    string? Stage = null,
    bool IsFinal = false);

internal sealed class NullProgress<T> : IProgress<T>
{
    public static readonly NullProgress<T> Instance = new();

    private NullProgress() { }

    /// <summary>No-op implementation of <see cref="IProgress{T}"/>.</summary>
    public void Report(T value)
    {
        // Intentionally no-op
    }
}
