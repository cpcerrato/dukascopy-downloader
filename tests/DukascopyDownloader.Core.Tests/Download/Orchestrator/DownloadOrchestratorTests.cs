using System.Net;
using DukascopyDownloader.Download;
using DukascopyDownloader.Core.Logging;
using DukascopyDownloader.Tests.Download.Fakes;
using DukascopyDownloader.Tests.Download.Support;
using System.Diagnostics;
using DukascopyDownloader.Core.Tests.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DukascopyDownloader.Tests.Download.Orchestrator;

public class DownloadOrchestratorTests
{
    [Fact]
    public async Task ProcessJobAsync_WhenDownloadSucceeds_WritesCacheAndCountsSuccess()
    {
        var options = CreateOptions();
        var cacheManager = new CacheManager(options.CacheRoot, options.OutputDirectory);
        var progress = new DownloadProgress();
        using var manifest = new FailureManifest(options.CacheRoot);
        var slice = DownloadSlicePlanner.Build(options).First();
        var handler = new FakeHttpMessageHandler(FakeHttpMessageHandler.Respond(HttpStatusCode.OK, Bi5TestSamples.TickBytes));
        var logger = new TestLogger();
        var orchestrator = new DownloadOrchestrator(NullLoggerFactory.Instance.CreateLogger<DownloadOrchestrator>(), logger, handler);
        var job = new DownloadOrchestrator.DownloadJob(slice, 0, 0);

        try
        {
            var decision = await orchestrator.ProcessJobAsync(job, options, cacheManager, progress, manifest, CancellationToken.None);

            Assert.Equal(DownloadOrchestrator.JobDecisionKind.Completed, decision.Kind);
            Assert.Equal(1, progress.SuccessCount);
            Assert.True(File.Exists(cacheManager.ResolveCachePath(slice)));
            Assert.False(File.Exists(Path.Combine(options.CacheRoot, "download-failures.json")));
        }
        finally
        {
            Cleanup(options.CacheRoot);
        }
    }

    [Fact]
    public async Task ProcessJobAsync_WhenRateLimited_RequeuesJobWithoutCountingFailure()
    {
        var options = CreateOptions(builder => builder.WithRateLimit(TimeSpan.FromMilliseconds(1), retries: 2));
        var cacheManager = new CacheManager(options.CacheRoot, options.OutputDirectory);
        var progress = new DownloadProgress();
        using var manifest = new FailureManifest(options.CacheRoot);
        var slice = DownloadSlicePlanner.Build(options).First();
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.Respond(HttpStatusCode.TooManyRequests, Array.Empty<byte>()));
            var logger = new TestLogger();
            var orchestrator = new DownloadOrchestrator(NullLoggerFactory.Instance.CreateLogger<DownloadOrchestrator>(), logger, handler);
        var job = new DownloadOrchestrator.DownloadJob(slice, 0, 0);

        try
        {
            var decision = await orchestrator.ProcessJobAsync(job, options, cacheManager, progress, manifest, CancellationToken.None);

            Assert.Equal(DownloadOrchestrator.JobDecisionKind.Requeue, decision.Kind);
            Assert.NotNull(decision.Job);
            Assert.Equal(1, decision.Job!.RateLimitHits);
            Assert.Equal(0, progress.SuccessCount);
            Assert.False(File.Exists(Path.Combine(options.CacheRoot, "download-failures.json")));
        }
        finally
        {
            Cleanup(options.CacheRoot);
        }
    }

    [Fact]
    public async Task ProcessJobAsync_WhenRetriesExhausted_ReturnsFailedAndRecordsManifest()
    {
        var options = CreateOptions(builder => builder.WithMaxRetries(1));
        var cacheManager = new CacheManager(options.CacheRoot, options.OutputDirectory);
        var progress = new DownloadProgress();
        using var manifest = new FailureManifest(options.CacheRoot);
        var slice = DownloadSlicePlanner.Build(options).First();
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.Respond(HttpStatusCode.InternalServerError, Array.Empty<byte>()),
            FakeHttpMessageHandler.Respond(HttpStatusCode.InternalServerError, Array.Empty<byte>()));
        var logger = new TestLogger();
        var orchestrator = new DownloadOrchestrator(NullLoggerFactory.Instance.CreateLogger<DownloadOrchestrator>(), logger, handler);
        var job = new DownloadOrchestrator.DownloadJob(slice, 0, 0);

        try
        {
            var first = await orchestrator.ProcessJobAsync(job, options, cacheManager, progress, manifest, CancellationToken.None);
            Assert.Equal(DownloadOrchestrator.JobDecisionKind.Requeue, first.Kind);
            Assert.NotNull(first.Job);

            var second = await orchestrator.ProcessJobAsync(first.Job!, options, cacheManager, progress, manifest, CancellationToken.None);
            Assert.Equal(DownloadOrchestrator.JobDecisionKind.Failed, second.Kind);
            Assert.Equal(1, progress.Failures);

            manifest.Dispose();
            var manifestPath = Path.Combine(options.CacheRoot, "download-failures.json");
            Assert.True(File.Exists(manifestPath));
            var contents = await File.ReadAllTextAsync(manifestPath);
            Assert.Contains(slice.Describe(), contents);
        }
        finally
        {
            Cleanup(options.CacheRoot);
        }
    }

    private static DownloadOptions CreateOptions(Action<TestDownloadOptionsBuilder>? configure = null)
    {
        var builder = new TestDownloadOptionsBuilder();
        configure?.Invoke(builder);
        return builder.Build();
    }

    private static void Cleanup(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
