using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DukascopyDownloader.Download;

internal sealed class FailureManifest : IDisposable
{
    private readonly string _path;
    private readonly ConcurrentBag<Entry> _entries = new();
    private bool _disposed;

    /// <summary>
    /// Creates a manifest that records download failures and writes them to a JSON file under the cache root.
    /// </summary>
    /// <param name="cacheRoot">Cache root where the manifest will be stored.</param>
    public FailureManifest(string cacheRoot)
    {
        _path = Path.Combine(cacheRoot, "download-failures.json");
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    /// <summary>
    /// Adds a failure entry to the manifest (not flushed until disposal).
    /// </summary>
    /// <param name="sliceSummary">Human-friendly slice descriptor.</param>
    /// <param name="message">Failure reason.</param>
    public void Record(string sliceSummary, string message)
    {
        _entries.Add(new Entry(sliceSummary, message, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Flushes pending entries to disk (or removes the manifest if empty).
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Flush();
    }

    private void Flush()
    {
        if (_entries.IsEmpty)
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
            return;
        }

        var ordered = _entries.OrderBy(e => e.Timestamp).ToArray();
        var payload = BuildJson(ordered);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, payload);
    }

    private static string BuildJson(IReadOnlyList<Entry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[");
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var slice = JsonEncodedText.Encode(entry.Slice).ToString();
            var message = JsonEncodedText.Encode(entry.Message).ToString();
            var timestamp = JsonEncodedText.Encode(entry.Timestamp.ToString("o", CultureInfo.InvariantCulture)).ToString();
            sb.Append("  {\"slice\":\"")
                .Append(slice)
                .Append("\",\"message\":\"")
                .Append(message)
                .Append("\",\"timestamp\":\"")
                .Append(timestamp)
                .Append("\"}");
            if (i < entries.Count - 1)
            {
                sb.Append(',');
            }
            sb.AppendLine();
        }
        sb.AppendLine("]");
        return sb.ToString();
    }

    private sealed record Entry(string Slice, string Message, DateTimeOffset Timestamp);
}
