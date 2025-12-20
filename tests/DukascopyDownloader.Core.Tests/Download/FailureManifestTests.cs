using DukascopyDownloader.Download;

namespace DukascopyDownloader.Tests.Download;

public class FailureManifestTests
{
    [Fact]
    public void Record_WhenDisposed_WritesJson()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "download-failures.json");

        using (var manifest = new FailureManifest(root))
        {
            manifest.Record("EURUSD-d1@2025-01-14Z", "HTTP 404");
            manifest.Record("EURUSD-d1@2025-01-15Z", "Data Error");
        }

        Assert.True(File.Exists(path));

        var json = File.ReadAllText(path);
        Assert.Contains("EURUSD-d1@2025-01-14Z", json);
        Assert.Contains("HTTP 404", json);
        Assert.Contains("EURUSD-d1@2025-01-15Z", json);
    }
}
