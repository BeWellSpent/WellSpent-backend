using Microsoft.EntityFrameworkCore;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Infrastructure.Persistence;

public sealed class PlaidItemRepository(WellSpentDbContext db) : IPlaidItemRepository
{
    public async Task<PlaidItem> CreateAsync(PlaidItem item, CancellationToken ct)
    {
        db.PlaidItems.Add(item);
        await db.SaveChangesAsync(ct);
        return item;
    }

    public async Task<PlaidItem> GetByIdAsync(Guid id, CancellationToken ct) =>
        await db.PlaidItems.FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new NotFoundException("plaid_item", id.ToString());

    public async Task<List<PlaidItem>> ListByUserIdAsync(Guid userId, CancellationToken ct) =>
        await db.PlaidItems.Where(i => i.UserId == userId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);

    public async Task<List<PlaidItemWithOwnerRow>> ListActiveWithOwnerByBudgetProfileAsync(Guid profileId, CancellationToken ct) =>
        await (
            from pi in db.PlaidItems
            join owner in db.Users on pi.UserId equals owner.Id
            where pi.BudgetProfileId == profileId && pi.Status != "disconnected"
            let ownerName = ((owner.FirstName ?? "") + " " + (owner.LastName ?? "")).Trim()
            let resolvedOwnerName = ownerName.Length > 0 ? ownerName : owner.Email
            orderby resolvedOwnerName, pi.CreatedAt descending
            select new PlaidItemWithOwnerRow(pi, resolvedOwnerName, owner.Plan)
        ).ToListAsync(ct);

    public async Task<List<UnsyncableConnectionRow>> ListUnsyncableForUserAsync(Guid userId, CancellationToken ct) =>
        await (
            from pi in db.PlaidItems
            join bp in db.BudgetProfiles on pi.BudgetProfileId equals bp.Id
            join owner in db.Users on pi.UserId equals owner.Id
            where owner.Plan == "free" && pi.Status != "disconnected"
                  && (bp.UserId == userId
                      || db.BudgetPeople.Any(m => m.BudgetProfileId == bp.Id && m.UserId == userId && m.IsActive))
            group new { owner } by new { pi.BudgetProfileId, bp.Name, owner.Id, owner.FirstName, owner.LastName, owner.Email } into g
            let ownerName = ((g.Key.FirstName ?? "") + " " + (g.Key.LastName ?? "")).Trim()
            let resolvedOwnerName = ownerName.Length > 0 ? ownerName : g.Key.Email
            orderby g.Key.Name, resolvedOwnerName
            select new UnsyncableConnectionRow(g.Key.BudgetProfileId, g.Key.Name, g.Key.Id, resolvedOwnerName, g.Count())
        ).ToListAsync(ct);

    public async Task<PlaidItem> UpdateStatusAsync(Guid id, string status, CancellationToken ct)
    {
        var item = await db.PlaidItems.FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new NotFoundException("plaid_item", id.ToString());
        item.Status = status;
        await db.SaveChangesAsync(ct);
        return item;
    }

    public async Task<PlaidItem> ResetCursorAsync(Guid id, CancellationToken ct)
    {
        var item = await db.PlaidItems.FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new NotFoundException("plaid_item", id.ToString());
        item.Cursor = null;
        item.LastSyncedAt = null;
        item.LastManualResyncAt = DateTime.UtcNow;
        item.Status = "active";
        await db.SaveChangesAsync(ct);
        return item;
    }

    public async Task<List<PlaidItem>> ListActiveForSyncAsync(CancellationToken ct) =>
        await db.PlaidItems
            .Where(pi => (pi.Status == "active" || pi.Status == "error")
                && (pi.LastSyncedAt == null || pi.LastSyncedAt < DateTime.UtcNow.AddDays(-1))
                && db.BudgetPeriods.Any(bp => bp.BudgetProfileId == pi.BudgetProfileId && !bp.IsArchived))
            .OrderBy(pi => pi.BudgetProfileId)
            .ThenBy(pi => pi.LastSyncedAt)
            .ToListAsync(ct);

    public async Task<List<PlaidItem>> ListActiveForProfileSyncAsync(Guid profileId, CancellationToken ct) =>
        await db.PlaidItems
            .Where(pi => pi.BudgetProfileId == profileId
                && (pi.Status == "active" || pi.Status == "error")
                && db.BudgetPeriods.Any(bp => bp.BudgetProfileId == pi.BudgetProfileId && !bp.IsArchived))
            .OrderBy(pi => pi.LastSyncedAt)
            .ToListAsync(ct);

    public async Task<PlaidItem> UpdateSyncAsync(Guid id, string cursor, CancellationToken ct)
    {
        var item = await db.PlaidItems.FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new NotFoundException("plaid_item", id.ToString());
        item.Cursor = cursor;
        item.LastSyncedAt = DateTime.UtcNow;
        item.Status = "active";
        await db.SaveChangesAsync(ct);
        return item;
    }
}
