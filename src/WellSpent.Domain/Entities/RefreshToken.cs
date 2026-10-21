namespace WellSpent.Domain.Entities;

/// <summary>Maps 1:1 to the new `refresh_token` table. TokenHash is a SHA-256 hex digest — the raw token is never stored.</summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Client-generated, stable per app install. Not an APNs device token.</summary>
    public required string DeviceId { get; set; }
    public required string TokenHash { get; set; }

    /// <summary>The window this token's class uses (1 day, or 90 for remember-me/OAuth) — carried forward on every rotation.</summary>
    public int LifetimeSeconds { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}
