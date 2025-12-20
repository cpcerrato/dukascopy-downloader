using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Channels;
using DukascopyDownloader.Logging;

namespace DukascopyDownloader.Download;

internal sealed class DownloadOrchestrator
{
    private readonly ConsoleLogger _logger;
    private readonly HttpClient _httpClient;
    private readonly RateLimitGate _rateLimitGate = new();
    private long _lastProgressTick;
    private int _spinnerIndex;
    private readonly char[] _spinner = new[] { '|', '/', '-', '\\' };

    internal sealed record DownloadJob(DownloadSlice Slice, int Attempts, int RateLimitHits);

    internal enum JobDecisionKind
    {
        Completed,
        Requeue,
        Failed
    }

    internal sealed record JobDecision(JobDecisionKind Kind, DownloadJob? Job, TimeSpan Delay, Exception? Error)
    {
        public static JobDecision Completed() => new(JobDecisionKind.Completed, null, TimeSpan.Zero, null);
        public static JobDecision Requeue(DownloadJob job, TimeSpan delay) => new(JobDecisionKind.Requeue, job, delay, null);
        public static JobDecision Failed(Exception error) => new(JobDecisionKind.Failed, null, TimeSpan.Zero, error);
    }

    public DownloadOrchestrator(ConsoleLogger logger, HttpMessageHandler? httpHandler = null)
    {
        _logger = logger;
        _httpClient = httpHandler is null
            ? new HttpClient()
            : new HttpClient(httpHandler, disposeHandler: false);
        _httpClient.Timeout = TimeSpan.FromSeconds(120);
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("dukascopy-downloader", "1.0"));
    }

    /// <summary>
    /// Executes the download pipeline: plans slices, downloads with retries and rate-limit handling,
    /// verifies BI5 integrity, mirrors to output if configured, and returns a summary.
    /// Throws <see cref="DownloadException"/> when any slice exhausts retries.
    /// </summary>
    public async Task<DownloadSummary> ExecuteAsync(DownloadOptions options, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var slices = DownloadSlicePlanner.Build(options).ToList();
        if (slices.Count == 0)
        {
            _logger.Info("No downloads were scheduled. Check the requested date range.");
            return new DownloadSummary(0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        Directory.CreateDirectory(options.CacheRoot);
        if (!string.IsNullOrWhiteSpace(options.OutputDirectory))
        {
            Directory.CreateDirectory(options.OutputDirectory!);
        }

        _logger.Info($"Preparing {slices.Count} files for {options.Instrument} ({options.Timeframe.ToDisplayString()}).");

        var cacheManager = new CacheManager(options.CacheRoot, options.OutputDirectory);
        var progress = new DownloadProgress();
        using var failureManifest = new FailureManifest(options.CacheRoot);
        RenderProgress(slices.Count, progress, force: true);
        RenderProgress(slices.Count, progress);

        var channel = Channel.CreateUnbounded<DownloadJob>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        foreach (var slice in slices)
        {
            channel.Writer.TryWrite(new DownloadJob(slice, Attempts: 0, RateLimitHits: 0));
        }

        var remainingJobs = slices.Count;
        void MarkJobCompleted()
        {
            if (Interlocked.Decrement(ref remainingJobs) == 0)
            {
                channel.Writer.TryComplete();
            }
        }

        var workers = Enumerable.Range(0, options.Concurrency)
            .Select(_ => RunWorkerAsync(
                channel.Reader,
                channel.Writer,
                slices.Count,
                options,
                cacheManager,
                progress,
                failureManifest,
                MarkJobCompleted,
                cancellationToken))
            .ToArray();

        await Task.WhenAll(workers);

        RenderProgress(slices.Count, progress, force: true);
        _logger.CompleteProgressLine();
        _logger.Success($"Downloads completed. New: {progress.SuccessCount}, Cache hits: {progress.CacheHits}, Missing: {progress.SkippedMissing}, Failed: {progress.Failures}.");

        if (progress.Failures > 0)
        {
            throw new DownloadException("Some files failed to download. See logs above for details.");
        }

        sw.Stop();
        return new DownloadSummary(
            Total: slices.Count,
            NewFiles: progress.SuccessCount,
            CacheHits: progress.CacheHits,
            Missing: progress.SkippedMissing,
            Failed: progress.Failures,
            Duration: sw.Elapsed);
    }

    private async Task RunWorkerAsync(
        ChannelReader<DownloadJob> reader,
        ChannelWriter<DownloadJob> writer,
        int totalJobs,
        DownloadOptions options,
        CacheManager cacheManager,
        DownloadProgress progress,
        FailureManifest failureManifest,
        Action markJobCompleted,
        CancellationToken cancellationToken)
    {
        while (await reader.WaitToReadAsync(cancellationToken))
        {
            while (reader.TryRead(out var job))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var decision = await ProcessJobAsync(job, options, cacheManager, progress, failureManifest, cancellationToken);

                switch (decision.Kind)
                {
                    case JobDecisionKind.Completed:
                        markJobCompleted();
                        RenderProgress(totalJobs, progress);
                        break;
                    case JobDecisionKind.Requeue:
                        ScheduleRetry(decision.Job!, decision.Delay, writer, cancellationToken);
                        break;
                    case JobDecisionKind.Failed:
                        markJobCompleted();
                        writer.TryComplete(decision.Error);
                        throw decision.Error!;
                }
            }
        }
    }

    private void RenderProgress(int totalJobs, DownloadProgress progress, bool force = false)
    {
        var completed = progress.SuccessCount + progress.CacheHits + progress.SkippedMissing + progress.Failures;
        var percent = totalJobs == 0 ? 100 : (int)Math.Round((double)completed * 100 / totalJobs);
        var now = Stopwatch.GetTimestamp();
        var elapsedMs = now * 1000 / Stopwatch.Frequency;
        if (!force && elapsedMs - _lastProgressTick < 200)
        {
            return;
        }

        _lastProgressTick = elapsedMs;
        var spinner = _spinner[_spinnerIndex++ % _spinner.Length];
        var message =
            $"Downloads {spinner} {percent,3}% ({completed}/{totalJobs}) | New {progress.SuccessCount} | Cache {progress.CacheHits} | Missing {progress.SkippedMissing} | Failed {progress.Failures}";
        _logger.Progress(message);
    }
    internal async Task<JobDecision> ProcessJobAsync(
        DownloadJob job,
        DownloadOptions options,
        CacheManager cacheManager,
        DownloadProgress progress,
        FailureManifest failureManifest,
        CancellationToken cancellationToken)
    {
        var slice = job.Slice;
        var destination = cacheManager.ResolveCachePath(slice);
        var summary = slice.Describe();

        if (options.UseCache && !options.ForceRefresh && File.Exists(destination))
        {
            progress.IncrementCacheHit();
            _logger.Verbose($"Cache hit {summary} -> {destination}");
            await cacheManager.SyncToOutputAsync(destination, slice, cancellationToken);
            return JobDecision.Completed();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var tempPath = destination + ".partial";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        var attemptNumber = job.Attempts + 1;
        var maxAttempts = options.MaxRetries + 1;

        try
        {
            await _rateLimitGate.WaitIfNeededAsync(cancellationToken);
            using var response = await _httpClient.GetAsync(slice.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var nextRateLimitHits = job.RateLimitHits + 1;
                if (nextRateLimitHits > options.RateLimitRetryLimit)
                {
                    var error = new DownloadException($"Persistent rate limit after {nextRateLimitHits} attempts for {summary}.");
                    progress.IncrementFailure();
                    failureManifest.Record(summary, error.Message);
                    return JobDecision.Failed(error);
                }

                _logger.Warn($"Rate limit detected ({summary}). Attempt {nextRateLimitHits}/{options.RateLimitRetryLimit}. Pausing {options.RateLimitPause.TotalSeconds:F0}s.");
                _rateLimitGate.Trigger(options.RateLimitPause, _logger);
                var retryJob = job with { Attempts = attemptNumber, RateLimitHits = nextRateLimitHits };
                return JobDecision.Requeue(retryJob, TimeSpan.Zero);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                progress.IncrementMissing();
                _logger.Warn($"No data available (404) for {summary}. Skipping.");
                return JobDecision.Completed();
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await response.Content.CopyToAsync(fs, cancellationToken);
            }

            await Bi5Verifier.VerifyAsync(tempPath, cancellationToken);
            File.Move(tempPath, destination, overwrite: true);
            await cacheManager.SyncToOutputAsync(destination, slice, cancellationToken);

            progress.IncrementSuccess();
            return JobDecision.Completed();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (attemptNumber >= maxAttempts)
            {
                progress.IncrementFailure();
                failureManifest.Record(summary, ex.Message);
                var error = new DownloadException($"Unable to download {summary} after {maxAttempts} attempts.", ex);
                return JobDecision.Failed(error);
            }

            _logger.Warn($"Failure downloading {summary} (attempt {attemptNumber}/{maxAttempts}): {ex.Message}. Retrying in {options.RetryDelay.TotalSeconds:F0}s.");
            var retryJob = job with { Attempts = attemptNumber };
            return JobDecision.Requeue(retryJob, options.RetryDelay);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static void ScheduleRetry(DownloadJob job, TimeSpan delay, ChannelWriter<DownloadJob> writer, CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }

                await writer.WriteAsync(job, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Cancellation requested; drop the job so shutdown can proceed.
            }
            catch (ChannelClosedException)
            {
                // Channel closed due to fatal failure or completion.
            }
        });
    }
}
