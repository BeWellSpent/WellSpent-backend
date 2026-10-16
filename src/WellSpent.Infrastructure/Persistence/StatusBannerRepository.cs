using Microsoft.EntityFrameworkCore;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Infrastructure.Persistence;

public sealed class StatusBannerRepository(WellSpentDbContext db) : IStatusBannerRepository
{
    public async Task<StatusBanner> GetActiveAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        // At most one banner is ever shown: highest severity first, then most
        // recently created. Mirrors the Go SQL's CASE severity WHEN 'critical'
        // THEN 3 WHEN 'warning' THEN 2 ELSE 1 END ordering exactly.
        var banner = await db.StatusBanners
            .Where(b => b.StartsAt <= now && b.EndsAt > now)
            .OrderByDescending(b => b.Severity == "critical" ? 3 : b.Severity == "warning" ? 2 : 1)
            .ThenByDescending(b => b.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return banner ?? throw new NotFoundException("status_banner", "active");
    }

    public async Task<StatusBanner> CreateAsync(StatusBanner banner, CancellationToken ct)
    {
        db.StatusBanners.Add(banner);
        await db.SaveChangesAsync(ct);
        return banner;
    }

    public async Task<List<StatusBanner>> ListAsync(int limit, CancellationToken ct) =>
        await db.StatusBanners.OrderByDescending(b => b.CreatedAt).Take(limit).ToListAsync(ct);

    public async Task<StatusBanner> ExpireAsync(Guid id, CancellationToken ct)
    {
        var banner = await db.StatusBanners.FirstOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new NotFoundException("status_banner", id.ToString());

        // LEAST(ends_at, GREATEST(starts_at, NOW())) in one statement in Go;
        // computed client-side here on the already-fetched row instead of a
        // raw-SQL function call. Same three outcomes: live -> ends_at = now,
        // scheduled -> ends_at = starts_at (empty window, never shows),
        // expired -> unchanged. The fetch-then-write race this opens (two
        // concurrent expires of the same banner) is immaterial for a
        // superuser-only, incident-triggered action with no normal concurrent
        // callers.
        var now = DateTime.UtcNow;
        var floor = banner.StartsAt > now ? banner.StartsAt : now;
        banner.EndsAt = banner.EndsAt < floor ? banner.EndsAt : floor;

        await db.SaveChangesAsync(ct);
        return banner;
    }
}
