using WellSpent.Domain.Entities;

namespace WellSpent.Domain.Abstractions;

/// <summary>
/// Mirrors Go's ListActivePlaidItemsWithOwnerByBudgetProfileRow — the item
/// plus the owning user's display name and plan, needed when several
/// members' connections appear in one budget-scoped list.
/// </summary>
public sealed record PlaidItemWithOwnerRow(PlaidItem Item, string OwnerName, string OwnerPlan);

/// <summary>Mirrors Go's ListUnsyncableConnectionsForUserRow — one row per (budget, free-plan member) whose connections the sync job skips.</summary>
public sealed record UnsyncableConnectionRow(Guid BudgetProfileId, string BudgetName, Guid MemberUserId, string MemberName, int ConnectionCount);

/// <summary>Scoped to what B6 batch 2 (the 6 interactive RPCs) needs. Batch 3 (the sync engine) extends this with the cursor/sync-state methods it needs, rather than adding them speculatively now.</summary>
public interface IPlaidItemRepository
{
    Task<PlaidItem> CreateAsync(PlaidItem item, CancellationToken ct);

    Task<PlaidItem> GetByIdAsync(Guid id, CancellationToken ct);

    Task<List<PlaidItem>> ListByUserIdAsync(Guid userId, CancellationToken ct);

    /// <summary>Every live (non-disconnected) connection on a budget, each with its owner's display name and plan.</summary>
    Task<List<PlaidItemWithOwnerRow>> ListActiveWithOwnerByBudgetProfileAsync(Guid profileId, CancellationToken ct);

    /// <summary>Budgets the caller owns or belongs to whose connections are being skipped by the sync job because their owner is on the free plan.</summary>
    Task<List<UnsyncableConnectionRow>> ListUnsyncableForUserAsync(Guid userId, CancellationToken ct);

    Task<PlaidItem> UpdateStatusAsync(Guid id, string status, CancellationToken ct);

    /// <summary>Clears cursor and LastSyncedAt, stamps LastManualResyncAt, resets status to active — all atomically.</summary>
    Task<PlaidItem> ResetCursorAsync(Guid id, CancellationToken ct);
}
