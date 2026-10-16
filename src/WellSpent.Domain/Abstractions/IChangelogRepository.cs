using WellSpent.Domain.Entities;

namespace WellSpent.Domain.Abstractions;

public interface IChangelogRepository
{
    /// <summary>
    /// Releases for the given components (empty means every component),
    /// ordered by component then releasedAt/createdAt descending, each with
    /// its Items already populated and ordered by position — one round trip,
    /// via EF Core's relational Include rather than Go's separate
    /// ListItems-for-a-release-set query.
    /// </summary>
    Task<List<ChangelogRelease>> ListReleasesWithItemsAsync(IReadOnlyList<string> components, CancellationToken ct);

    /// <summary>
    /// Saves the release and its items as one unit — unlike the Go service,
    /// which writes the release row and then each item in a loop with no
    /// transaction. EF Core wraps one SaveChanges in an implicit transaction,
    /// so this is strictly more atomic for the same shape, not a behavior
    /// change a caller can observe short of a partial-failure scenario Go
    /// doesn't protect against either.
    /// </summary>
    Task<ChangelogRelease> CreateReleaseAsync(ChangelogRelease release, CancellationToken ct);
}
