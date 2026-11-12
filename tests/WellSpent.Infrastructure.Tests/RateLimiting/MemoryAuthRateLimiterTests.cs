using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using WellSpent.Domain.Exceptions;
using WellSpent.Infrastructure.RateLimiting;
using Xunit;

namespace WellSpent.Infrastructure.Tests.RateLimiting;

public sealed class MemoryAuthRateLimiterTests
{
    private readonly MemoryAuthRateLimiter _limiter = new(new MemoryCache(new MemoryCacheOptions()), NullLogger<MemoryAuthRateLimiter>.Instance);

    [Fact]
    public void OverLimit_ThrowsWithRetryAfterWithinWindow()
    {
        for (var i = 0; i < 3; i++)
        {
            _limiter.Hit("register", "1.1.1.1", 3, TimeSpan.FromDays(1));
        }

        var ex = Assert.Throws<TooManyRequestsException>(() => _limiter.Hit("register", "1.1.1.1", 3, TimeSpan.FromDays(1)));
        Assert.InRange(ex.RetryAfter, TimeSpan.Zero, TimeSpan.FromDays(1));
    }

    [Fact]
    public void ClientsAndBuckets_CountedSeparately()
    {
        _limiter.Hit("register", "1.1.1.1", 1, TimeSpan.FromHours(1));
        _limiter.Hit("register", "2.2.2.2", 1, TimeSpan.FromHours(1));
        _limiter.Hit("login", "1.1.1.1", 1, TimeSpan.FromHours(1));
    }
}
