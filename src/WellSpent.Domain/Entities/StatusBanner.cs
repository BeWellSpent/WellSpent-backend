namespace WellSpent.Domain.Entities;

/// <summary>Maps 1:1 to the existing `status_banner` table.</summary>
public sealed class StatusBanner
{
    public Guid Id { get; set; }

    /// <summary>"info" | "warning" | "critical".</summary>
    public required string Severity { get; set; }

    /// <summary>Locale-keyed text, e.g. {"en": "...", "es": "..."}. "en" is required by the handler, not the DB.</summary>
    public required Dictionary<string, string> Message { get; set; }

    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }

    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}
