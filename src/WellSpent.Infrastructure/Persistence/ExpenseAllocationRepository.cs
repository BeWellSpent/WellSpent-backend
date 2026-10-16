using Microsoft.EntityFrameworkCore;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

public sealed class ExpenseAllocationRepository(WellSpentDbContext db) : IExpenseAllocationRepository
{
    public async Task<List<ExpenseAllocation>> ListAsync(Guid budgetProfileId, CancellationToken ct) =>
        await db.ExpenseAllocations
            .Where(a => a.BudgetProfileId == budgetProfileId)
            .OrderBy(a => a.CategoryId).ThenBy(a => a.Id)
            .ToListAsync(ct);

    // ON CONFLICT has no LINQ equivalent — raw SQL against the unique index
    // (budget_profile_id, category_id, COALESCE(budget_person_id, -1)),
    // mirroring Go's UpsertExpenseAllocation exactly. FromSqlInterpolated maps
    // the RETURNING row straight onto the entity since every column is a
    // plain mapped property, no SqlQuery<T> scalar workaround needed.
    public async Task<ExpenseAllocation> UpsertAsync(
        Guid budgetProfileId, int categoryId, int? budgetPersonId, decimal plannedAmount, CancellationToken ct)
    {
        var rows = await db.ExpenseAllocations.FromSqlInterpolated($"""
            INSERT INTO expense_allocation (budget_profile_id, category_id, budget_person_id, planned_amount)
            VALUES ({budgetProfileId}, {categoryId}, {budgetPersonId}, {plannedAmount})
            ON CONFLICT (budget_profile_id, category_id, COALESCE(budget_person_id, -1))
            DO UPDATE SET planned_amount = EXCLUDED.planned_amount
            RETURNING id, budget_profile_id, category_id, budget_person_id, planned_amount
            """).ToListAsync(ct);
        return rows[0];
    }

    // Mirrors Go's bare `DELETE ... WHERE id = $1 AND budget_profile_id = $2`
    // exactly: a no-op, not a 404, when nothing matches.
    public async Task DeleteAsync(int id, Guid budgetProfileId, CancellationToken ct) =>
        await db.ExpenseAllocations
            .Where(a => a.Id == id && a.BudgetProfileId == budgetProfileId)
            .ExecuteDeleteAsync(ct);
}
