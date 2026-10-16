using WellSpent.Application.Common;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.Budgets;

/// <summary>
/// Manual entity-to-DTO mapping for the three money-bearing Budget entities —
/// not AutoMapper, since each needs the decimal-to-Money conversion plus the
/// Go wire convention of "0 = unattributed" for a nullable person id, neither
/// of which AutoMapper's convention-based mapping does for free.
/// </summary>
public static class IncomeSourceMapping
{
    public static IncomeSourceDto ToDto(IncomeSource s) => new(
        s.Id, s.BudgetProfileId, s.Name, s.IncomeType, Money.FromDecimal(s.DefaultAmount),
        s.Recurring, s.BudgetPersonId ?? 0, s.PaymentFrequency, s.BeforeTax);
}

public static class SavingsSourceMapping
{
    public static SavingsSourceDto ToDto(SavingsSource s) => new(
        s.Id, s.BudgetProfileId, s.Name, Money.FromDecimal(s.Amount), s.Frequency, s.IsTaxReserve,
        s.BudgetPersonId ?? 0,
        s.FederalAmount is { } fed ? Money.FromDecimal(fed) : null,
        s.StateAmount is { } state ? Money.FromDecimal(state) : null,
        s.PaymentMethodId, s.PaymentDays);
}

public static class IncomeEntryMapping
{
    public static IncomeEntryDto ToDto(IncomeEntry e) => new(
        e.Id, e.BudgetPeriodId, e.IncomeSourceId ?? 0, e.Name, Money.FromDecimal(e.Amount), e.BudgetPersonId ?? 0);
}
