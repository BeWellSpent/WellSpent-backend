using WellSpent.Domain.Entities;

namespace WellSpent.Domain.Abstractions;

public interface IExpenseAllocationRepository
{
    Task<List<ExpenseAllocation>> ListAsync(Guid budgetProfileId, CancellationToken ct);

    /// <summary>Inserts or, on a (budgetProfileId, categoryId, budgetPersonId) collision, overwrites plannedAmount — mirrors the DB's own ON CONFLICT upsert (unique index treats a null budgetPersonId as -1).</summary>
    Task<ExpenseAllocation> UpsertAsync(Guid budgetProfileId, int categoryId, int? budgetPersonId, decimal plannedAmount, CancellationToken ct);

    Task DeleteAsync(int id, Guid budgetProfileId, CancellationToken ct);
}
