using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Plaid;

/// <summary>
/// The two access checks specific to the Plaid domain — not shared with any
/// other domain, unlike budget membership (see Common.BudgetAccessGuard).
/// Mirrors internal/service/plaid_service.go's requireUS/requireProOrLifetime
/// exactly, including each doing its own independent user lookup rather than
/// being combined into one fetch.
/// </summary>
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

    /// <summary>
    /// Checked at link time (CreateLinkToken/ExchangePublicToken) and on
    /// manual resync, so a free-tier user gets a clear error instead of
    /// successfully linking (or resyncing) an item the sync job then
    /// silently skips forever.
    /// </summary>
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
