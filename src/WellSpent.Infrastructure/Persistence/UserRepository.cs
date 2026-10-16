using Microsoft.EntityFrameworkCore;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Infrastructure.Persistence;

public sealed class UserRepository(WellSpentDbContext db) : IUserRepository
{
    public async Task<User> GetByIdAsync(Guid id, CancellationToken ct) =>
        await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NotFoundException("user", id.ToString());

    public async Task<User> GetByEmailAsync(string email, CancellationToken ct) =>
        await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct)
            ?? throw new NotFoundException("user", email);

    public async Task<User> CreateAsync(User user, CancellationToken ct)
    {
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task<User> UpdateAsync(
        Guid id, string? firstName, string? lastName, string? countryCode, string? stateCode,
        string filingStatus, int taxPaymentFrequency, string language, string currency, CancellationToken ct)
    {
        var user = await GetByIdAsync(id, ct);
        user.FirstName = firstName;
        user.LastName = lastName;
        user.CountryCode = countryCode;
        user.StateCode = stateCode;
        user.FilingStatus = filingStatus;
        user.TaxPaymentFrequency = taxPaymentFrequency;
        user.Language = language;
        user.Currency = currency;
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task UpdatePasswordAsync(Guid id, string hashedPassword, CancellationToken ct)
    {
        var user = await GetByIdAsync(id, ct);
        user.HashedPassword = hashedPassword;
        await db.SaveChangesAsync(ct);
    }

    public async Task<User> UpdateEmailAsync(Guid id, string email, CancellationToken ct)
    {
        var user = await GetByIdAsync(id, ct);
        user.Email = email;
        // Clearing is_verified is the point, not a side effect — the new
        // address is unproven until its own verification link is redeemed.
        user.IsVerified = false;
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken ct)
    {
        var user = await GetByIdAsync(id, ct);
        user.Status = "disabled";
        user.IsActive = false;
        user.ActiveUntil = DateTime.UtcNow.AddDays(30);
        await db.SaveChangesAsync(ct);
    }

    public async Task<User> SetEmailVerificationTokenAsync(Guid id, Guid token, DateTime expiresAt, DateTime lastSentAt, CancellationToken ct)
    {
        var user = await GetByIdAsync(id, ct);
        user.EmailVerificationToken = token;
        user.EmailVerificationExpiresAt = expiresAt;
        user.EmailVerificationLastSentAt = lastSentAt;
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task<User> GetByVerificationTokenAsync(Guid token, CancellationToken ct) =>
        await db.Users.FirstOrDefaultAsync(u => u.EmailVerificationToken == token, ct)
            ?? throw new NotFoundException("user", token.ToString());

    public async Task MarkVerifiedAsync(Guid id, CancellationToken ct)
    {
        var user = await GetByIdAsync(id, ct);
        user.IsVerified = true;
        user.EmailVerificationToken = null;
        user.EmailVerificationExpiresAt = null;
        await db.SaveChangesAsync(ct);
    }

    public async Task<OAuthAccount> GetOAuthAccountAsync(string oauthName, string accountId, CancellationToken ct) =>
        await db.OAuthAccounts.FirstOrDefaultAsync(o => o.OauthName == oauthName && o.AccountId == accountId, ct)
            ?? throw new NotFoundException("oauth_account", $"{oauthName}:{accountId}");

    public async Task<OAuthAccount> CreateOAuthAccountAsync(OAuthAccount account, CancellationToken ct)
    {
        db.OAuthAccounts.Add(account);
        await db.SaveChangesAsync(ct);
        return account;
    }

    public async Task UpdateOAuthAccountRefreshTokenAsync(Guid oauthAccountId, string? refreshToken, CancellationToken ct)
    {
        var account = await db.OAuthAccounts.FirstOrDefaultAsync(o => o.Id == oauthAccountId, ct)
            ?? throw new NotFoundException("oauth_account", oauthAccountId.ToString());
        account.RefreshToken = refreshToken;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<OAuthAccount>> ListOAuthAccountsByUserAsync(Guid userId, CancellationToken ct) =>
        await db.OAuthAccounts.Where(o => o.UserId == userId).OrderBy(o => o.OauthName).ToListAsync(ct);
}
