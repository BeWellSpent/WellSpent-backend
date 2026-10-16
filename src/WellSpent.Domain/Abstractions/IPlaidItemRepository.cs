using WellSpent.Domain.Entities;

namespace WellSpent.Domain.Abstractions;

/// <summary>Mirrors Go's ListActivePlaidItemsWithOwnerByBudgetProfileRow.</summary>
public sealed record PlaidItemWithOwnerRow(PlaidItem Item, string OwnerName, string OwnerPlan);

/// <summary>Mirrors Go's ListUnsyncableConnectionsForUserRow.</summary>
public sealed record UnsyncableConnectionRow(Guid BudgetProfileId, string BudgetName, Guid MemberUserId, string MemberName, int ConnectionCount);

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

    /// <summary>Active-or-errored items due for a sync, excluding budgets with no live period.</summary>
    Task<List<PlaidItem>> ListActiveForSyncAsync(CancellationToken ct);

    /// <summary>Same as ListActiveForSyncAsync but scoped to one profile, no cooldown — used by cycle-budgets (B7).</summary>
    Task<List<PlaidItem>> ListActiveForProfileSyncAsync(Guid profileId, CancellationToken ct);

    /// <summary>Only called after a successful sync, so it also clears a prior 'error' status back to 'active'.</summary>
    Task<PlaidItem> UpdateSyncAsync(Guid id, string cursor, CancellationToken ct);
}
