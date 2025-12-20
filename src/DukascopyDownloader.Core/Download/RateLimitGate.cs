
using Microsoft.Extensions.Logging;

namespace DukascopyDownloader.Download;

internal sealed class RateLimitGate
{
    private readonly object _sync = new();
    private DateTimeOffset _resumeAt = DateTimeOffset.MinValue;

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
