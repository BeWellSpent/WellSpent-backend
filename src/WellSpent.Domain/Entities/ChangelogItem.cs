namespace WellSpent.Domain.Entities;

/// <summary>Maps 1:1 to the existing `changelog_item` table.</summary>
public sealed class ChangelogItem
{
    public Guid Id { get; set; }
    public Guid ReleaseId { get; set; }
    public ChangelogRelease? Release { get; set; }

    /// <summary>"added" | "fixed" | "changed".</summary>
    public required string ChangeType { get; set; }

    public required string SummaryEn { get; set; }
    public string SummaryEs { get; set; } = "";

    /// <summary>Preserves the order the operator wrote them in.</summary>
    public int Position { get; set; }

    public DateTime CreatedAt { get; set; }
}
