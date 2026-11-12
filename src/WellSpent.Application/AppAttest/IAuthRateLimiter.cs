namespace WellSpent.Application.AppAttest;

/// <summary>Per-client limits on sensitive auth actions. Throws TooManyRequestsException over the limit.</summary>
public interface IAuthRateLimiter
{
    void Hit(string bucket, string clientKey, int limit, TimeSpan window);
}

/// <summary>Register and login limits (#121): tighter without a verified App Attest assertion.</summary>
public static class AuthRateLimits
{
    public static void Apply(IAuthRateLimiter limiter, string action, string clientIp, bool attested)
    {
        var (limit, window) = (action, attested) switch
        {
            ("register", true) => (10, TimeSpan.FromHours(1)),
            ("register", false) => (3, TimeSpan.FromDays(1)),
            ("login", true) => (30, TimeSpan.FromHours(1)),
            _ => (10, TimeSpan.FromHours(1)),
        };
        limiter.Hit($"{action}:{(attested ? "attested" : "unattested")}", clientIp, limit, window);
    }
}
