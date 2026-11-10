using Microsoft.EntityFrameworkCore;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

public sealed class BudgetPeriodPlanRepository(WellSpentDbContext db) : IBudgetPeriodPlanRepository
{
    public async Task<List<BudgetPeriodPlan>> ListAsync(IReadOnlyCollection<Guid> budgetPeriodIds, CancellationToken ct) =>
        await db.BudgetPeriodPlans.AsNoTracking()
            .Where(p => budgetPeriodIds.Contains(p.BudgetPeriodId))
            .ToListAsync(ct);

    public async Task<bool> ExistsAsync(Guid budgetPeriodId, CancellationToken ct) =>
        await db.BudgetPeriodPlans.AnyAsync(p => p.BudgetPeriodId == budgetPeriodId, ct);

    public async Task AddRangeAsync(IReadOnlyCollection<BudgetPeriodPlan> rows, CancellationToken ct)
    {
        db.BudgetPeriodPlans.AddRange(rows);
        await db.SaveChangesAsync(ct);
    }
}
