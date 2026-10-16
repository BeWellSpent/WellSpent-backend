namespace WellSpent.Domain.Entities;

/// <summary>A connected Plaid bank item — stores the encrypted access token and sync state. US users only.</summary>
public sealed class PlaidItem
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid BudgetProfileId { get; set; }

    /// <summary>Encrypted at rest (AES-256-GCM, via ICryptoService) — never the raw Plaid access token.</summary>
    public required string AccessToken { get; set; }

    public required string ItemId { get; set; }
    public string? InstitutionId { get; set; }
    public string? InstitutionName { get; set; }

    /// <summary>active | disconnected | error.</summary>
    public string Status { get; set; } = "active";

    public string? Cursor { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>When the owner last requested a manual resync — drives the 24-hour cooldown. Deliberately separate from LastSyncedAt, which a resync clears.</summary>
    public DateTime? LastManualResyncAt { get; set; }
}
