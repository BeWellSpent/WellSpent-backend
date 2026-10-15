namespace WellSpent.Domain.Entities;

/// <summary>Maps 1:1 to the existing `device_token` table — an APNs push token registered for a user.</summary>
public sealed class DeviceToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>"ios" — the only platform this project registers tokens for.</summary>
    public required string Platform { get; set; }

    /// <summary>UNIQUE — a device re-registering (e.g. after reinstall) upserts by this value.</summary>
    public required string Token { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
