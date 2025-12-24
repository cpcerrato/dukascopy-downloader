
using Microsoft.Extensions.Logging;

namespace DukascopyDownloader.Download;

internal sealed class RateLimitGate
{
    private readonly object _sync = new();
    private DateTimeOffset _resumeAt = DateTimeOffset.MinValue;

    /// <summary>
    /// Waits until the current rate-limit pause has elapsed.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to abort waiting.</param>
    public async Task WaitIfNeededAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan wait;
            lock (_sync)
            {
                var remaining = _resumeAt - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    return;
                }

                wait = remaining;
            }

            await Task.Delay(wait, cancellationToken);
        }
    }

    /// <summary>
    /// Schedules a new rate-limit pause; keeps the longest pause if multiple are triggered.
    /// </summary>
    /// <param name="pause">Duration to pause new work.</param>
    /// <param name="logger">Logger for warning output.</param>
    public void Trigger(TimeSpan pause, ILogger logger)
    {
        bool scheduled = false;
        DateTimeOffset resumeTime;

        lock (_sync)
        {
            var candidate = DateTimeOffset.UtcNow + pause;
            if (candidate > _resumeAt)
            {
                _resumeAt = candidate;
                resumeTime = candidate;
                scheduled = true;
            }
            else
            {
                resumeTime = _resumeAt;
            }
        }

        if (scheduled)
        {
            logger.LogWarning("Rate limit pause in effect until {ResumeTime}Z.", resumeTime.ToString("HH:mm:ss"));
        }
        else
        {
            logger.LogWarning("Rate limit pause already active; reusing the longest delay.");
        }
    }
}
