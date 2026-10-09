namespace WellSpent.Application.Budgets;

public sealed record BudgetProfileDto(
    Guid Id, Guid UserId, string Name, string Cycle, string? CountryCode,
    bool CarryoverEnabled, bool AutoUpdatePlannedAmount);

public sealed record BudgetPeriodDto(
    Guid Id, Guid BudgetProfileId, DateOnly StartDate, DateOnly EndDate, bool IsArchived);

public sealed record BudgetPersonDto(
    long Id, Guid BudgetProfileId, string? UserName, Guid? UserId, string Color, string Role,
    string? PlanChartType, string? OverviewChartType, bool ManualMatchReviewEnabled, bool FocusedViewEnabled);
