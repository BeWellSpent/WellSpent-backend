using WellSpent.Application.Common;

namespace WellSpent.Application.Budgets;

public sealed record IncomeSourceDto(
    long Id, Guid BudgetProfileId, string Name, string IncomeType, Money DefaultAmount,
    bool Recurring, long BudgetPersonId, string PaymentFrequency, bool BeforeTax);

public sealed record IncomeEntryDto(
    long Id, Guid BudgetPeriodId, long IncomeSourceId, string? Name, Money Amount, long BudgetPersonId);

public sealed record SavingsSourceDto(
    long Id, Guid BudgetProfileId, string Name, Money Amount, string Frequency, bool IsTaxReserve,
    long BudgetPersonId, Money? FederalAmount, Money? StateAmount, Guid? PaymentMethodId, int[] PaymentDays);

public sealed record BudgetProfileDto(
    Guid Id, Guid UserId, string Name, string Cycle, string? CountryCode,
    bool CarryoverEnabled, bool AutoUpdatePlannedAmount);

public sealed record BudgetPeriodDto(
    Guid Id, Guid BudgetProfileId, DateOnly StartDate, DateOnly EndDate, bool IsArchived);

public sealed record BudgetPersonDto(
    long Id, Guid BudgetProfileId, string? UserName, Guid? UserId, string Color, string Role,
    string? PlanChartType, string? OverviewChartType, bool ManualMatchReviewEnabled, bool FocusedViewEnabled);
