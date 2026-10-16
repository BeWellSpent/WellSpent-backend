using WellSpent.Application.Common;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.ExpenseAllocations;

public sealed record ExpenseAllocationDto(long Id, Guid BudgetProfileId, int CategoryId, long BudgetPersonId, Money PlannedAmount);

public static class ExpenseAllocationMapping
{
    public static ExpenseAllocationDto ToDto(ExpenseAllocation a) =>
        new(a.Id, a.BudgetProfileId, a.CategoryId, a.BudgetPersonId ?? 0, Money.FromDecimal(a.PlannedAmount));
}
