using WellSpent.Domain.Entities;

namespace WellSpent.Domain.Abstractions;

/// <summary>
/// Mirrors Go's internal/repository.UserRepository. Lookups throw
/// <see cref="Exceptions.NotFoundException"/> rather than returning null —
/// several callers (Google/Apple sign-in) specifically distinguish "not
/// found, create one" from any other failure, the same way the Go service
/// layer does via <c>errors.As</c> against <c>apperr.NotFoundError</c>.
/// </summary>
public interface IUserRepository
{
    Task<User> GetByIdAsync(Guid id, CancellationToken ct);
    Task<User> GetByEmailAsync(string email, CancellationToken ct);
    Task<User> CreateAsync(User user, CancellationToken ct);

    /// <summary>
    /// Full-replace profile update (mirrors the Go UpdateUser SQL, which sets
    /// all eight columns unconditionally) — a null/cleared value is written as
    /// NULL, not skipped. Scoped to exactly these columns; email, password,
    /// and every other field are untouched regardless of what the caller's
    /// own in-memory User looks like.
    /// </summary>
    Task<User> UpdateAsync(
        Guid id,
        string? firstName,
        string? lastName,
        string? countryCode,
        string? stateCode,
        string filingStatus,
        int taxPaymentFrequency,
        string language,
        string currency,
        CancellationToken ct);

    Task UpdatePasswordAsync(Guid id, string hashedPassword, CancellationToken ct);

    /// <summary>Also resets IsVerified to false, same as the underlying SQL.</summary>
    Task<User> UpdateEmailAsync(Guid id, string email, CancellationToken ct);

    Task SoftDeleteAsync(Guid id, CancellationToken ct);

    Task<User> SetEmailVerificationTokenAsync(Guid id, Guid token, DateTime expiresAt, DateTime lastSentAt, CancellationToken ct);
    Task<User> GetByVerificationTokenAsync(Guid token, CancellationToken ct);
    Task MarkVerifiedAsync(Guid id, CancellationToken ct);

    Task<OAuthAccount> GetOAuthAccountAsync(string oauthName, string accountId, CancellationToken ct);
    Task<OAuthAccount> CreateOAuthAccountAsync(OAuthAccount account, CancellationToken ct);
    Task UpdateOAuthAccountRefreshTokenAsync(Guid oauthAccountId, string? refreshToken, CancellationToken ct);
    Task<List<OAuthAccount>> ListOAuthAccountsByUserAsync(Guid userId, CancellationToken ct);
}
