namespace WellSpent.Domain.Entities;

/// <summary>`Id` is the table's SERIAL (int4) PK, matching the Go repository's int32 usage throughout.</summary>
public sealed class BudgetPerson
{
    public int Id { get; set; }
    public Guid BudgetProfileId { get; set; }
    public string? UserName { get; set; }
    public Guid? UserId { get; set; }
    public bool IsActive { get; set; } = true;
    public string Color { get; set; } = "";

    /// <summary>"admin" | "collaborator" | "viewer" | "unspecified".</summary>
    public required string Role { get; set; }

    /// <summary>"pie" | "bar" | null (null = use the client default).</summary>
    public string? PlanChartType { get; set; }
    public string? OverviewChartType { get; set; }
    public bool ManualMatchReviewEnabled { get; set; } = true;
    public bool FocusedViewEnabled { get; set; }
}
