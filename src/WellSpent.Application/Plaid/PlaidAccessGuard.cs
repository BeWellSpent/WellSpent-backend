using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Plaid;

/// <summary>Mirrors plaid_service.go's requireUS/requireProOrLifetime — Plaid-specific, not shared.</summary>
public sealed class PlaidAccessGuard(IUserRepository users)
{
    public async Task<User> RequireUsAsync(Guid userId, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(userId, ct);
        if (user.CountryCode != "US")
        {
            throw new ForbiddenException("Plaid is only available for US users");
        }

        return user;
    }

    /// <summary>Checked at link time and on manual resync, so a free-tier user gets a clear error instead of a silently-skipped sync.</summary>
    public async Task<User> RequireProOrLifetimeAsync(Guid userId, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(userId, ct);
        if (user.Plan == "free")
        {
            throw new AppValidationException("free tier: Plaid bank sync requires a Pro subscription");
        }

        return user;
    }
}
