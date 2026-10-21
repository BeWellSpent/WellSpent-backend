using Microsoft.EntityFrameworkCore;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

public sealed class RefreshTokenRepository(WellSpentDbContext db) : IRefreshTokenRepository
{
    public async Task<RefreshToken> IssueAsync(Guid userId, string deviceId, string tokenHash, int lifetimeSeconds, DateTime expiresAt, CancellationToken ct)
    {
        await RevokeAllForDeviceAsync(userId, deviceId, ct);

        var token = new RefreshToken
        {
            UserId = userId,
            DeviceId = deviceId,
            TokenHash = tokenHash,
            LifetimeSeconds = lifetimeSeconds,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
        };
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync(ct);
        return token;
    }

    public async Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct) =>
        await db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == tokenHash, ct);

    public async Task RevokeAsync(Guid id, CancellationToken ct)
    {
        var token = await db.RefreshTokens.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (token is null || token.RevokedAt is not null)
        {
            return;
        }
        token.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public Task RevokeAllForDeviceAsync(Guid userId, string deviceId, CancellationToken ct) =>
        RevokeAllAsync(db.RefreshTokens.Where(r => r.UserId == userId && r.DeviceId == deviceId && r.RevokedAt == null), ct);

    public Task RevokeAllForUserAsync(Guid userId, CancellationToken ct) =>
        RevokeAllAsync(db.RefreshTokens.Where(r => r.UserId == userId && r.RevokedAt == null), ct);

    private async Task RevokeAllAsync(IQueryable<RefreshToken> query, CancellationToken ct)
    {
        var active = await query.ToListAsync(ct);
        if (active.Count == 0)
        {
            return;
        }
        var now = DateTime.UtcNow;
        foreach (var token in active)
        {
            token.RevokedAt = now;
        }
        await db.SaveChangesAsync(ct);
    }
}
