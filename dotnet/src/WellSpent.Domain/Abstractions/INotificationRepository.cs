using WellSpent.Domain.Entities;

namespace WellSpent.Domain.Abstractions;

/// <summary>
/// Covers the 7 client-facing NotificationService RPCs plus the two methods
/// NotificationDispatcher needs (GetBudgetSubscribersAsync, CreateAsync) to
/// fire notifications when other domains' events happen. Push
/// (ListDeviceTokensForUser + APNs sending) stays unported — no AppConfig
/// fields or APNs client exist yet; email and in-app are the only channels.
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

    /// <summary>Every subscription on this budget for this alert type, regardless of subscriber.</summary>
    Task<List<AlertSubscription>> GetBudgetSubscribersAsync(Guid budgetProfileId, string alertType, CancellationToken ct);

    Task<Notification> CreateAsync(Notification notification, CancellationToken ct);
}
