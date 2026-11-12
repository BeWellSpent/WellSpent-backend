using Microsoft.EntityFrameworkCore;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

public sealed class AppAttestRepository(WellSpentDbContext db) : IAppAttestRepository
{
    public async Task CreateChallengeAsync(string challenge, DateTime expiresAt, CancellationToken ct)
    {
        // Housekeeping on the write path: nothing reads a challenge an hour after it expired.
        var stale = DateTime.UtcNow.AddHours(-1);
        await db.AppAttestChallenges.Where(c => c.ExpiresAt < stale).ExecuteDeleteAsync(ct);
        db.AppAttestChallenges.Add(new AppAttestChallenge { Challenge = challenge, ExpiresAt = expiresAt });
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> ConsumeChallengeAsync(string challenge, DateTime now, CancellationToken ct) =>
        await db.AppAttestChallenges
            .Where(c => c.Challenge == challenge && c.UsedAt == null && c.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedAt, now), ct) == 1;

    public async Task<AppAttestKey?> GetKeyAsync(string keyId, CancellationToken ct) =>
        await db.AppAttestKeys.AsNoTracking().FirstOrDefaultAsync(k => k.KeyId == keyId, ct);

    public async Task SaveKeyAsync(AppAttestKey key, CancellationToken ct)
    {
        var existing = await db.AppAttestKeys.FirstOrDefaultAsync(k => k.KeyId == key.KeyId, ct);
        if (existing is null)
        {
            db.AppAttestKeys.Add(key);
        }
        else
        {
            existing.PublicKey = key.PublicKey;
            existing.SignCount = key.SignCount;
            existing.Environment = key.Environment;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> AdvanceCounterAsync(string keyId, long signCount, DateTime now, CancellationToken ct) =>
        await db.AppAttestKeys
            .Where(k => k.KeyId == keyId && k.SignCount < signCount)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.SignCount, signCount).SetProperty(k => k.LastUsedAt, now), ct) == 1;
}
