using Microsoft.EntityFrameworkCore;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

public sealed class NotificationRepository(WellSpentDbContext db) : INotificationRepository
{
    public async Task<List<Notification>> ListAsync(Guid userId, Guid? budgetProfileId, int limit, CancellationToken ct)
    {
        var query = db.Notifications.Where(n => n.UserId == userId);
        if (budgetProfileId is { } profileId)
        {
            query = query.Where(n => n.BudgetProfileId == profileId);
        }
        return await query.OrderByDescending(n => n.CreatedAt).Take(limit).ToListAsync(ct);
    }

    public Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct) =>
        db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct);

    public async Task MarkReadAsync(Guid userId, List<Guid> ids, CancellationToken ct)
    {
        var query = db.Notifications.Where(n => n.UserId == userId && !n.IsRead);
        if (ids.Count > 0)
        {
            query = query.Where(n => ids.Contains(n.Id));
        }
        await query.ExecuteUpdateAsync(setters => setters.SetProperty(n => n.IsRead, true), ct);
    }

    public Task<List<AlertSubscription>> ListSubscriptionsAsync(Guid userId, Guid budgetProfileId, CancellationToken ct) =>
        db.AlertSubscriptions
            .Where(s => s.UserId == userId && s.BudgetProfileId == budgetProfileId)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(ct);

    public async Task<AlertSubscription> UpsertSubscriptionAsync(AlertSubscription subscription, CancellationToken ct)
    {
        // Mirrors the DB's ON CONFLICT (user_id, budget_profile_id,
        // alert_type, COALESCE(category_id, -1)) DO UPDATE via a
        // find-then-update/insert — see the interface doc comment for why.
        var existing = await db.AlertSubscriptions.FirstOrDefaultAsync(s =>
            s.UserId == subscription.UserId &&
            s.BudgetProfileId == subscription.BudgetProfileId &&
            s.AlertType == subscription.AlertType &&
            (s.CategoryId ?? -1) == (subscription.CategoryId ?? -1), ct);

        if (existing is null)
        {
            db.AlertSubscriptions.Add(subscription);
            await db.SaveChangesAsync(ct);
            return subscription;
        }

        existing.Channel = subscription.Channel;
        existing.ThresholdPct = subscription.ThresholdPct;
        existing.ThresholdScope = subscription.ThresholdScope;
        existing.NotifyAllMembers = subscription.NotifyAllMembers;
        await db.SaveChangesAsync(ct);
        return existing;
    }

    public async Task DeleteSubscriptionAsync(Guid id, Guid userId, CancellationToken ct)
    {
        // No-op (not an error) when the row doesn't exist or isn't owned by
        // this user — mirrors the Go repository's unconditional DELETE.
        await db.AlertSubscriptions
            .Where(s => s.Id == id && s.UserId == userId)
            .ExecuteDeleteAsync(ct);
    }

    public async Task<DeviceToken> UpsertDeviceTokenAsync(Guid userId, string platform, string token, CancellationToken ct)
    {
        // Mirrors the DB's ON CONFLICT (token) DO UPDATE.
        var existing = await db.DeviceTokens.FirstOrDefaultAsync(t => t.Token == token, ct);
        if (existing is null)
        {
            var created = new DeviceToken { UserId = userId, Platform = platform, Token = token };
            db.DeviceTokens.Add(created);
            await db.SaveChangesAsync(ct);
            return created;
        }

        existing.UserId = userId;
        existing.Platform = platform;
        existing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return existing;
    }
}
