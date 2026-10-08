using WellSpent.Domain.Entities;

namespace WellSpent.Domain.Abstractions;

/// <summary>
/// Deliberately minimal — exactly the lookups Notification/Invite need for
/// ownership/membership/role checks and invite-acceptance writes (B4). B5
/// (the full Budget domain) adds periods, income, transactions, etc. to this
/// same interface rather than introducing a second one.
/// </summary>
public interface IBudgetProfileRepository
{
    Task<BudgetProfile> GetByIdAsync(Guid id, CancellationToken ct);

    Task<BudgetPerson> GetPersonAsync(int personId, Guid profileId, CancellationToken ct);

    /// <summary>Active person only (IsActive = true), matching the Go query exactly.</summary>
    Task<BudgetPerson> GetPersonByUserIdAsync(Guid profileId, Guid userId, CancellationToken ct);

    Task<bool> ExistsPersonForUserAsync(Guid profileId, Guid userId, CancellationToken ct);

    Task<BudgetPerson> AddPersonAsync(BudgetPerson person, CancellationToken ct);

    /// <summary>Only affects an active person row — mirrors the Go query's `WHERE id = ... AND is_active = TRUE`.</summary>
    Task<BudgetPerson> LinkPersonToUserAsync(int personId, Guid userId, string role, CancellationToken ct);
}
