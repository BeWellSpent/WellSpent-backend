namespace WellSpent.Domain.Entities;

/// <summary>Maps 1:1 to the existing `status_banner` table.</summary>
public sealed class StatusBanner
{
    public Guid Id { get; set; }

    /// <summary>"info" | "warning" | "critical".</summary>
    public required string Severity { get; set; }

    public required string MessageEn { get; set; }
    public string MessageEs { get; set; } = "";

    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }

    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}
