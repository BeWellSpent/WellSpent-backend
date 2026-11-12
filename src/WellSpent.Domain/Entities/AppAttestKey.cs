namespace WellSpent.Domain.Entities;

/// <summary>An App Attest key, verified once per app install (#121).</summary>
public sealed class AppAttestKey
{
    /// <summary>Base64, as the device reported it.</summary>
    public required string KeyId { get; set; }

    /// <summary>SubjectPublicKeyInfo (DER) of the P-256 key that signs assertions.</summary>
    public required byte[] PublicKey { get; set; }

    public long SignCount { get; set; }
    public required string Environment { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}

/// <summary>A one-time App Attest challenge.</summary>
public sealed class AppAttestChallenge
{
    public required string Challenge { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
}
