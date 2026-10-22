namespace WellSpent.Domain.Entities;

/// <summary>Maps 1:1 to the existing `changelog_item` table.</summary>
public sealed class ChangelogItem
{
    public Guid Id { get; set; }
    public Guid ReleaseId { get; set; }
    public ChangelogRelease? Release { get; set; }

    /// <summary>"added" | "fixed" | "changed".</summary>
    public required string ChangeType { get; set; }

    /// <summary>Locale-keyed text, e.g. {"en": "...", "es": "..."}. "en" is required by the handler, not the DB.</summary>
    public required Dictionary<string, string> Summary { get; set; }

    /// <summary>Preserves the order the operator wrote them in.</summary>
    public int Position { get; set; }

    public DateTime CreatedAt { get; set; }
}
