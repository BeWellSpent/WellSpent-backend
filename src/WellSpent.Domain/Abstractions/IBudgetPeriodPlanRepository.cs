using WellSpent.Domain.Entities;

namespace WellSpent.Domain.Abstractions;

public interface IBudgetPeriodPlanRepository
{
    Task<List<BudgetPeriodPlan>> ListAsync(IReadOnlyCollection<Guid> budgetPeriodIds, CancellationToken ct);

    Task<bool> ExistsAsync(Guid budgetPeriodId, CancellationToken ct);

    Task AddRangeAsync(IReadOnlyCollection<BudgetPeriodPlan> rows, CancellationToken ct);
}
