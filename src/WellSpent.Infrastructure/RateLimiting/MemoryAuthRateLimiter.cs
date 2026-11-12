using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using WellSpent.Application.AppAttest;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Infrastructure.RateLimiting;

/// <summary>Fixed-window counters in memory.</summary>
/// <remarks>shortcut: per Cloud Run instance, so the real limit is limit × instances; move to Postgres if abuse gets past it.</remarks>
public sealed class MemoryAuthRateLimiter(IMemoryCache cache, ILogger<MemoryAuthRateLimiter> logger) : IAuthRateLimiter
{
    public void Hit(string bucket, string clientKey, int limit, TimeSpan window)
    {
        var now = DateTimeOffset.UtcNow;
        var windowIndex = now.Ticks / window.Ticks;
        var windowEnd = new DateTimeOffset((windowIndex + 1) * window.Ticks, TimeSpan.Zero);
        var counter = cache.GetOrCreate($"auth-limit:{bucket}:{clientKey}:{windowIndex}", e =>
        {
            e.AbsoluteExpiration = windowEnd;
            return new StrongBox();
        })!;

        if (Interlocked.Increment(ref counter.Value) > limit)
        {
            logger.LogWarning("auth.rate_limited bucket={Bucket} client={Client} limit={Limit} window={Window}", bucket, clientKey, limit, window);
            throw new TooManyRequestsException("too many attempts, try again later", windowEnd - now);
        }
    }

    private sealed class StrongBox
    {
        public int Value;
    }
}
