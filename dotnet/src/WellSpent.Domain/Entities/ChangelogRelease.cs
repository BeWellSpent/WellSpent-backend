namespace WellSpent.Domain.Entities;

/// <summary>Maps 1:1 to the existing `changelog_release` table. UNIQUE (component, version) in the schema.</summary>
public sealed class ChangelogRelease
{
    public Guid Id { get; set; }

    /// <summary>"web" | "ios" | "server".</summary>
    public required string Component { get; set; }

    /// <summary>As that component spells it — semver for web/server, MARKETING_VERSION for iOS.</summary>
    public required string Version { get; set; }

    public DateTime ReleasedAt { get; set; }

    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<ChangelogItem> Items { get; set; } = [];
}
