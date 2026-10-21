using WellSpent.Domain.Entities;

namespace WellSpent.Domain.Abstractions;

/// <summary>
/// Refresh-token persistence. GetByHashAsync returns null (not a throw) on a
/// miss — an unknown token is an ordinary auth failure, not an exceptional repo state.
/// </summary>
public interface IRefreshTokenRepository
{
    /// <summary>Revokes any existing active row for (userId, deviceId) first, then inserts — "replace, don't accumulate".</summary>
    Task<RefreshToken> IssueAsync(Guid userId, string deviceId, string tokenHash, int lifetimeSeconds, DateTime expiresAt, CancellationToken ct);

    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct);
    Task RevokeAsync(Guid id, CancellationToken ct);
    Task RevokeAllForDeviceAsync(Guid userId, string deviceId, CancellationToken ct);

    /// <summary>Theft-detection (reuse of a revoked token) and the password/email-change/delete remediation path.</summary>
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct);
}
