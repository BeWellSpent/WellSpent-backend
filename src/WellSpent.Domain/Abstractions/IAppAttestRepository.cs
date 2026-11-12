using WellSpent.Domain.Entities;

namespace WellSpent.Domain.Abstractions;

public interface IAppAttestRepository
{
    /// <summary>Stores a new challenge, pruning ones that expired a while ago.</summary>
    Task CreateChallengeAsync(string challenge, DateTime expiresAt, CancellationToken ct);

    /// <summary>Marks the challenge used. False when it's unknown, already used or expired: atomic, so it can't be redeemed twice.</summary>
    Task<bool> ConsumeChallengeAsync(string challenge, DateTime now, CancellationToken ct);

    Task<AppAttestKey?> GetKeyAsync(string keyId, CancellationToken ct);

    /// <summary>Inserts the key or, after a re-attestation of the same key ID, replaces it.</summary>
    Task SaveKeyAsync(AppAttestKey key, CancellationToken ct);

    /// <summary>Raises the counter. False when it isn't higher than the stored one (a replayed assertion).</summary>
    Task<bool> AdvanceCounterAsync(string keyId, long signCount, DateTime now, CancellationToken ct);
}
