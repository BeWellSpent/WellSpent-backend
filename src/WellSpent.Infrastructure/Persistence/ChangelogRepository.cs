using Microsoft.EntityFrameworkCore;
using Npgsql;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Infrastructure.Persistence;

public sealed class ChangelogRepository(WellSpentDbContext db) : IChangelogRepository
{
    private const string UniqueViolationSqlState = "23505";

    public async Task<List<ChangelogRelease>> ListReleasesWithItemsAsync(IReadOnlyList<string> components, CancellationToken ct)
    {
        var query = db.ChangelogReleases.Include(r => r.Items).AsQueryable();
        if (components.Count > 0)
        {
            query = query.Where(r => components.Contains(r.Component));
        }

        var releases = await query
            .OrderBy(r => r.Component)
            .ThenByDescending(r => r.ReleasedAt)
            .ThenByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        // EF Core doesn't order an included collection server-side; sorting
        // here is cheap given how small a release's item list is.
        foreach (var release in releases)
        {
            release.Items = release.Items.OrderBy(i => i.Position).ThenBy(i => i.CreatedAt).ToList();
        }

        return releases;
    }

    public async Task<ChangelogRelease> CreateReleaseAsync(ChangelogRelease release, CancellationToken ct)
    {
        db.ChangelogReleases.Add(release);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolationSqlState })
        {
            // UNIQUE (component, version) — republishing a version has to
            // fail loudly rather than leave a reader with the same release
            // listed twice.
            throw new DuplicateException("changelog_release", "version", $"{release.Component} {release.Version}");
        }
        return release;
    }
}
