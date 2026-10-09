using Microsoft.EntityFrameworkCore;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Infrastructure.Persistence;

public sealed class BudgetProfileRepository(WellSpentDbContext db) : IBudgetProfileRepository
{
    // ── Profile ──────────────────────────────────────────────────────────────

    public async Task<List<BudgetProfile>> ListByUserIdAsync(Guid userId, CancellationToken ct) =>
        await db.BudgetProfiles.Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt).ToListAsync(ct);

    public async Task<List<BudgetProfile>> ListByUserOrMemberAsync(Guid userId, CancellationToken ct) =>
        await db.BudgetProfiles
            .Where(p => p.UserId == userId ||
                db.BudgetPeople.Any(m => m.BudgetProfileId == p.Id && m.UserId == userId && m.IsActive))
            .OrderByDescending(p => p.CreatedAt).ToListAsync(ct);

    public async Task<BudgetProfile> GetByIdAsync(Guid id, CancellationToken ct) =>
        await db.BudgetProfiles.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("budget_profile", id.ToString());

    public async Task<bool> ExistsByNameAndUserAsync(string name, Guid userId, CancellationToken ct) =>
        await db.BudgetProfiles.AnyAsync(p => p.Name == name && p.UserId == userId, ct);

    public async Task<BudgetProfile> CreateAsync(BudgetProfile profile, CancellationToken ct)
    {
        db.BudgetProfiles.Add(profile);
        await db.SaveChangesAsync(ct);
        return profile;
    }

    public async Task<BudgetProfile> UpdateAsync(Guid id, string name, string cycle, CancellationToken ct)
    {
        var profile = await GetByIdAsync(id, ct);
        profile.Name = name;
        profile.Cycle = cycle;
        await db.SaveChangesAsync(ct);
        return profile;
    }

    public async Task<BudgetProfile> SetCarryoverEnabledAsync(Guid id, bool enabled, CancellationToken ct)
    {
        var profile = await GetByIdAsync(id, ct);
        profile.CarryoverEnabled = enabled;
        await db.SaveChangesAsync(ct);
        return profile;
    }

    public async Task<BudgetProfile> SetAutoUpdatePlannedAmountAsync(Guid id, bool enabled, CancellationToken ct)
    {
        var profile = await GetByIdAsync(id, ct);
        profile.AutoUpdatePlannedAmount = enabled;
        await db.SaveChangesAsync(ct);
        return profile;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await db.BudgetProfiles.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
    }

    public async Task<List<Guid>> ListPaymentMethodIdsByBudgetProfileAsync(Guid profileId, CancellationToken ct) =>
        await db.Database.SqlQuery<Guid>(
            $"""
            SELECT pm.id FROM payment_methods pm
            JOIN budget_to_profile_mapping btpm ON btpm.id = pm.budget_person_id
            WHERE btpm.budget_profile_id = {profileId}
            """).ToListAsync(ct);

    public async Task DeletePaymentMethodsByIdsAsync(List<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM payment_methods WHERE id = ANY({ids.ToArray()})", ct);
    }

    // ── Period ───────────────────────────────────────────────────────────────

    public async Task<BudgetPeriod> CreatePeriodAsync(BudgetPeriod period, CancellationToken ct)
    {
        db.BudgetPeriods.Add(period);
        await db.SaveChangesAsync(ct);
        return period;
    }

    public async Task<BudgetPeriod> GetPeriodByIdAsync(Guid id, CancellationToken ct) =>
        await db.BudgetPeriods.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("budget_period", id.ToString());

    public async Task<List<BudgetPeriod>> ListPeriodsAsync(Guid profileId, CancellationToken ct) =>
        await db.BudgetPeriods.Where(p => p.BudgetProfileId == profileId)
            .OrderByDescending(p => p.StartDate).ToListAsync(ct);

    public async Task<BudgetPeriod> GetLatestPeriodAsync(Guid profileId, CancellationToken ct) =>
        await db.BudgetPeriods.Where(p => p.BudgetProfileId == profileId)
            .OrderByDescending(p => p.StartDate).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("budget_period", "latest for " + profileId);

    public async Task ArchivePeriodAsync(Guid id, CancellationToken ct)
    {
        await db.BudgetPeriods.Where(p => p.Id == id).ExecuteUpdateAsync(
            s => s.SetProperty(p => p.IsArchived, true), ct);
    }

    // ── People ───────────────────────────────────────────────────────────────

    public async Task<List<BudgetPerson>> ListPeopleAsync(Guid profileId, CancellationToken ct) =>
        await db.BudgetPeople.Where(p => p.BudgetProfileId == profileId && p.IsActive)
            .OrderBy(p => p.Id).ToListAsync(ct);

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

    public async Task<bool> ExistsPersonAsync(Guid profileId, string userName, CancellationToken ct) =>
        await db.BudgetPeople.AnyAsync(
            p => p.BudgetProfileId == profileId && p.UserName == userName && p.IsActive, ct);

    public async Task<BudgetPerson> AddPersonAsync(BudgetPerson person, CancellationToken ct)
    {
        db.BudgetPeople.Add(person);
        await db.SaveChangesAsync(ct);
        return person;
    }

    public async Task<BudgetPerson> UpdatePersonColorAsync(int personId, Guid profileId, string color, CancellationToken ct)
    {
        var person = await db.BudgetPeople.FirstOrDefaultAsync(
            p => p.Id == personId && p.BudgetProfileId == profileId && p.IsActive, ct)
            ?? throw new NotFoundException("budget_person", personId.ToString());
        person.Color = color;
        await db.SaveChangesAsync(ct);
        return person;
    }

    public async Task<BudgetPerson> UpdatePersonRoleAsync(int personId, Guid profileId, string role, CancellationToken ct)
    {
        var person = await db.BudgetPeople.FirstOrDefaultAsync(
            p => p.Id == personId && p.BudgetProfileId == profileId && p.IsActive, ct)
            ?? throw new NotFoundException("budget_person", personId.ToString());
        person.Role = role;
        await db.SaveChangesAsync(ct);
        return person;
    }

    public async Task<BudgetPerson> UpdatePersonPreferencesAsync(
        Guid profileId, Guid userId, string? planChartType, string? overviewChartType, CancellationToken ct)
    {
        var person = await db.BudgetPeople.FirstOrDefaultAsync(
            p => p.BudgetProfileId == profileId && p.UserId == userId && p.IsActive, ct)
            ?? throw new NotFoundException("budget_person", userId.ToString());
        person.PlanChartType = planChartType;
        person.OverviewChartType = overviewChartType;
        await db.SaveChangesAsync(ct);
        return person;
    }

    public async Task<BudgetPerson> UpdatePersonManualMatchReviewPreferenceAsync(
        Guid profileId, Guid userId, bool enabled, CancellationToken ct)
    {
        var person = await db.BudgetPeople.FirstOrDefaultAsync(
            p => p.BudgetProfileId == profileId && p.UserId == userId && p.IsActive, ct)
            ?? throw new NotFoundException("budget_person", userId.ToString());
        person.ManualMatchReviewEnabled = enabled;
        await db.SaveChangesAsync(ct);
        return person;
    }

    public async Task<BudgetPerson> UpdatePersonFocusedViewPreferenceAsync(
        Guid profileId, Guid userId, bool enabled, CancellationToken ct)
    {
        var person = await db.BudgetPeople.FirstOrDefaultAsync(
            p => p.BudgetProfileId == profileId && p.UserId == userId && p.IsActive, ct)
            ?? throw new NotFoundException("budget_person", userId.ToString());
        person.FocusedViewEnabled = enabled;
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

    public async Task SoftRemovePersonAsync(int personId, Guid profileId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE payment_methods SET is_active = FALSE WHERE budget_person_id = {personId}", ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE budget_to_profile_mapping SET is_active = FALSE
            WHERE id = {personId} AND budget_profile_id = {profileId}::uuid
            """, ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// Mirrors Go's SoftRemovePersonAndReassignFromProfile CTE exactly. Raw SQL
    /// against `transaction`/`income_entry` rather than EF entities — those
    /// tables aren't mapped yet (B5 batches 2/4) and this reassignment needs
    /// to work correctly as soon as People ships, not once those batches land.
    /// </summary>
    public async Task SoftRemovePersonAndReassignAsync(
        int personId, Guid profileId, Guid replacementPaymentMethodId, int replacementPersonId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE transaction
            SET payment_method_id = {replacementPaymentMethodId}::uuid
            WHERE payment_method_id IN (SELECT pm.id FROM payment_methods pm WHERE pm.budget_person_id = {personId})
              AND budget_period_id IN (SELECT bp.id FROM budget_period bp WHERE bp.budget_profile_id = {profileId}::uuid)
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE payment_methods SET is_active = FALSE WHERE budget_person_id = {personId}", ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE income_entry
            SET budget_person_id = {replacementPersonId}
            WHERE budget_person_id = {personId}
              AND budget_period_id IN (SELECT bp.id FROM budget_period bp WHERE bp.budget_profile_id = {profileId}::uuid)
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE budget_to_profile_mapping SET is_active = FALSE
            WHERE id = {personId} AND budget_profile_id = {profileId}::uuid
            """, ct);
        await tx.CommitAsync(ct);
    }
}
