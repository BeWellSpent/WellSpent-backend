using WellSpent.Domain.Entities;

namespace WellSpent.Domain.Abstractions;

/// <summary>
/// Covers the 7 client-facing NotificationService RPCs only. The Go
/// service's fan-out/delivery methods (GetBudgetSubscribers, Create,
/// ListDeviceTokensForUser used by push, email sending) exist to fire
/// notifications when *other* domains' events happen (a transaction is
/// created, a period starts, a Plaid import needs review) — none of which
/// are ported yet. Those arrive with whichever sub-issue ports the
/// triggering domain (B5 for transactions/periods, B6 for Plaid), not here.
/// </summary>
public interface INotificationRepository
{
    Task<List<Notification>> ListAsync(Guid userId, Guid? budgetProfileId, int limit, CancellationToken ct);
    Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct);

    /// <summary>Empty ids means mark ALL of the user's unread notifications as read.</summary>
    Task MarkReadAsync(Guid userId, List<Guid> ids, CancellationToken ct);

    Task<List<AlertSubscription>> ListSubscriptionsAsync(Guid userId, Guid budgetProfileId, CancellationToken ct);

    /// <summary>
    /// Upserts on (UserId, BudgetProfileId, AlertType, CategoryId) — mirrors
    /// the DB's partial unique index (COALESCE(category_id, -1)) via a
    /// find-then-update/insert, not a native Postgres ON CONFLICT. The
    /// resulting TOCTOU race is immaterial for a personal alert-preference
    /// toggle with no realistic concurrent double-submit.
    /// </summary>
    Task<AlertSubscription> UpsertSubscriptionAsync(AlertSubscription subscription, CancellationToken ct);

    /// <summary>No-op (not an error) when the subscription doesn't exist or isn't owned by this user — mirrors the Go repository's unconditional DELETE WHERE id = ? AND user_id = ?.</summary>
    Task DeleteSubscriptionAsync(Guid id, Guid userId, CancellationToken ct);

    /// <summary>Upserts by token — mirrors the DB's ON CONFLICT (token) DO UPDATE.</summary>
    Task<DeviceToken> UpsertDeviceTokenAsync(Guid userId, string platform, string token, CancellationToken ct);
}
