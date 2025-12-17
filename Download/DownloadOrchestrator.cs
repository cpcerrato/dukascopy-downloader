using System.Net;
using System.Net.Http.Headers;
using DukascopyDownloader.Logging;

namespace DukascopyDownloader.Download;

internal sealed class DownloadOrchestrator
{
    private readonly ConsoleLogger _logger;
    private readonly HttpClient _httpClient;
    private readonly RateLimitGate _rateLimitGate = new();

    public DownloadOrchestrator(ConsoleLogger logger)
    {
        _logger = logger;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(120)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("dukascopy-downloader", "1.0"));
    }

    public async Task ExecuteAsync(DownloadOptions options, CancellationToken cancellationToken)
    {
        var slices = DownloadSlicePlanner.Build(options).ToList();
        if (slices.Count == 0)
        {
            _logger.Info("No downloads were scheduled. Check the requested date range.");
            return;
        }

        Directory.CreateDirectory(options.CacheRoot);
        if (!string.IsNullOrWhiteSpace(options.OutputDirectory))
        {
            Directory.CreateDirectory(options.OutputDirectory!);
        }

        _logger.Info($"Preparing {slices.Count} files for {options.Instrument} ({options.Timeframe.ToDisplayString()}).");

        var cacheManager = new CacheManager(options.CacheRoot, options.OutputDirectory);
        var progress = new DownloadProgress();

        using var throttler = new SemaphoreSlim(options.Concurrency, options.Concurrency);
        var tasks = slices.Select(slice => ProcessSliceAsync(slice, options, cacheManager, throttler, progress, cancellationToken));
        await Task.WhenAll(tasks);

        _logger.Success($"Downloads completed. New: {progress.SuccessCount}, Cache hits: {progress.CacheHits}, Missing: {progress.SkippedMissing}, Failed: {progress.Failures}.");

        if (progress.Failures > 0)
        {
            throw new DownloadException("Some files failed to download. See logs above for details.");
        }
    }

    private async Task ProcessSliceAsync(
        DownloadSlice slice,
        DownloadOptions options,
        CacheManager cacheManager,
        SemaphoreSlim throttler,
        DownloadProgress progress,
        CancellationToken cancellationToken)
    {
        await throttler.WaitAsync(cancellationToken);
        try
        {
            await ProcessSliceInternalAsync(slice, options, cacheManager, progress, cancellationToken);
        }
        finally
        {
            throttler.Release();
        }
    }

    private async Task ProcessSliceInternalAsync(
        DownloadSlice slice,
        DownloadOptions options,
        CacheManager cacheManager,
        DownloadProgress progress,
        CancellationToken cancellationToken)
    {
        var destination = cacheManager.ResolveCachePath(slice);
        var summary = slice.Describe();

        if (options.UseCache && !options.ForceRefresh && File.Exists(destination))
        {
            progress.IncrementCacheHit();
            _logger.Verbose($"Cache hit {summary} -> {destination}");
            await cacheManager.SyncToOutputAsync(destination, slice, cancellationToken);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var tempPath = destination + ".partial";

        var attempt = 0;
        var rateLimitHits = 0;
        Exception? lastError = null;

        while (attempt <= options.MaxRetries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;

            try
            {
                await _rateLimitGate.WaitIfNeededAsync(cancellationToken);
                using var response = await _httpClient.GetAsync(slice.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    rateLimitHits++;
                    if (rateLimitHits > options.RateLimitRetryLimit)
                    {
                        throw new DownloadException($"Persistent rate limit after {rateLimitHits} attempts for {summary}.");
                    }

                    _logger.Warn($"Rate limit detected ({summary}). Attempt {rateLimitHits}/{options.RateLimitRetryLimit}. Pausing {options.RateLimitPause.TotalSeconds:F0}s.");
                    _rateLimitGate.Trigger(options.RateLimitPause, _logger);
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    progress.IncrementMissing();
                    _logger.Warn($"No data available (404) for {summary}. Skipping.");
                    return;
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
                _logger.Info($"OK {summary}");
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (attempt > options.MaxRetries)
                {
                    break;
                }

                _logger.Warn($"Failure downloading {summary} (attempt {attempt}/{options.MaxRetries + 1}): {ex.Message}. Retrying in {options.RetryDelay.TotalSeconds:F0}s.");
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                await Task.Delay(options.RetryDelay, cancellationToken);
            }
        }

        progress.IncrementFailure();
        throw new DownloadException($"Unable to download {summary} after {options.MaxRetries + 1} attempts.", lastError);
    }
}
