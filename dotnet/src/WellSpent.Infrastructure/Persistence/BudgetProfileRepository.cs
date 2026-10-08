using Microsoft.EntityFrameworkCore;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Infrastructure.Persistence;

public sealed class BudgetProfileRepository(WellSpentDbContext db) : IBudgetProfileRepository
{
    public async Task<BudgetProfile> GetByIdAsync(Guid id, CancellationToken ct) =>
        await db.BudgetProfiles.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("budget_profile", id.ToString());

    public async Task<BudgetPerson> GetPersonAsync(int personId, Guid profileId, CancellationToken ct) =>
        await db.BudgetPeople.FirstOrDefaultAsync(p => p.Id == personId && p.BudgetProfileId == profileId, ct)
            ?? throw new NotFoundException("budget_person", personId.ToString());

    public async Task<BudgetPerson> GetPersonByUserIdAsync(Guid profileId, Guid userId, CancellationToken ct) =>
        await db.BudgetPeople.FirstOrDefaultAsync(
            p => p.BudgetProfileId == profileId && p.UserId == userId && p.IsActive, ct)
            ?? throw new NotFoundException("budget_person", userId.ToString());

    public async Task<bool> ExistsPersonForUserAsync(Guid profileId, Guid userId, CancellationToken ct) =>
        await db.BudgetPeople.AnyAsync(
            p => p.BudgetProfileId == profileId && p.UserId == userId && p.IsActive, ct);

    public async Task<BudgetPerson> AddPersonAsync(BudgetPerson person, CancellationToken ct)
    {
        db.BudgetPeople.Add(person);
        await db.SaveChangesAsync(ct);
        return person;
    }

    public async Task<BudgetPerson> LinkPersonToUserAsync(int personId, Guid userId, string role, CancellationToken ct)
    {
        // Only affects an active row — mirrors the Go query's WHERE id = ? AND is_active = TRUE.
        var person = await db.BudgetPeople.FirstOrDefaultAsync(p => p.Id == personId && p.IsActive, ct)
            ?? throw new NotFoundException("budget_person", personId.ToString());
        person.UserId = userId;
        person.Role = role;
        await db.SaveChangesAsync(ct);
        return person;
    }
}
