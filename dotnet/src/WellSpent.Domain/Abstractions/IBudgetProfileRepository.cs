using WellSpent.Domain.Entities;

namespace WellSpent.Domain.Abstractions;

/// <summary>
/// Covers BudgetProfile CRUD, BudgetPeriod, and People (B5 batch 1). Income
/// sources, savings sources, and income entries are added to this same
/// interface in a later B5 batch rather than introducing a second one.
/// </summary>
public interface IBudgetProfileRepository
{
    // ── Profile ──────────────────────────────────────────────────────────────
    Task<List<BudgetProfile>> ListByUserIdAsync(Guid userId, CancellationToken ct);

    /// <summary>Owned profiles plus profiles where the caller is an active member.</summary>
    Task<List<BudgetProfile>> ListByUserOrMemberAsync(Guid userId, CancellationToken ct);

    Task<BudgetProfile> GetByIdAsync(Guid id, CancellationToken ct);
    Task<bool> ExistsByNameAndUserAsync(string name, Guid userId, CancellationToken ct);
    Task<BudgetProfile> CreateAsync(BudgetProfile profile, CancellationToken ct);
    Task<BudgetProfile> UpdateAsync(Guid id, string name, string cycle, CancellationToken ct);
    Task<BudgetProfile> SetCarryoverEnabledAsync(Guid id, bool enabled, CancellationToken ct);
    Task<BudgetProfile> SetAutoUpdatePlannedAmountAsync(Guid id, bool enabled, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// payment_methods has no budget_profile_id of its own (see
    /// docs/features/budget-creation-flow.md) — this is the only way to find
    /// a budget's payment methods before deleting the profile severs the
    /// link. Raw SQL rather than a mapped entity: PaymentMethod isn't ported
    /// until B5 batch 3.
    /// </summary>
    Task<List<Guid>> ListPaymentMethodIdsByBudgetProfileAsync(Guid profileId, CancellationToken ct);

    Task DeletePaymentMethodsByIdsAsync(List<Guid> ids, CancellationToken ct);

    // ── Period ───────────────────────────────────────────────────────────────
    Task<BudgetPeriod> CreatePeriodAsync(BudgetPeriod period, CancellationToken ct);
    Task<BudgetPeriod> GetPeriodByIdAsync(Guid id, CancellationToken ct);
    Task<List<BudgetPeriod>> ListPeriodsAsync(Guid profileId, CancellationToken ct);

    /// <summary>Throws NotFoundException when the profile has no period yet.</summary>
    Task<BudgetPeriod> GetLatestPeriodAsync(Guid profileId, CancellationToken ct);

    Task ArchivePeriodAsync(Guid id, CancellationToken ct);

    // ── People ───────────────────────────────────────────────────────────────
    Task<List<BudgetPerson>> ListPeopleAsync(Guid profileId, CancellationToken ct);
    Task<BudgetPerson> GetPersonAsync(int personId, Guid profileId, CancellationToken ct);

    /// <summary>Active person only (IsActive = true), matching the Go query exactly.</summary>
    Task<BudgetPerson> GetPersonByUserIdAsync(Guid profileId, Guid userId, CancellationToken ct);

    Task<bool> ExistsPersonForUserAsync(Guid profileId, Guid userId, CancellationToken ct);
    Task<bool> ExistsPersonAsync(Guid profileId, string userName, CancellationToken ct);
    Task<BudgetPerson> AddPersonAsync(BudgetPerson person, CancellationToken ct);
    Task<BudgetPerson> UpdatePersonColorAsync(int personId, Guid profileId, string color, CancellationToken ct);
    Task<BudgetPerson> UpdatePersonRoleAsync(int personId, Guid profileId, string role, CancellationToken ct);

    /// <summary>Matched on (profileId, userId), not a person id — resolved from the caller's own JWT.</summary>
    Task<BudgetPerson> UpdatePersonPreferencesAsync(Guid profileId, Guid userId, string? planChartType, string? overviewChartType, CancellationToken ct);

    Task<BudgetPerson> UpdatePersonManualMatchReviewPreferenceAsync(Guid profileId, Guid userId, bool enabled, CancellationToken ct);
    Task<BudgetPerson> UpdatePersonFocusedViewPreferenceAsync(Guid profileId, Guid userId, bool enabled, CancellationToken ct);

    /// <summary>Only affects an active person row — mirrors the Go query's `WHERE id = ... AND is_active = TRUE`.</summary>
    Task<BudgetPerson> LinkPersonToUserAsync(int personId, Guid userId, string role, CancellationToken ct);

    Task SoftRemovePersonAsync(int personId, Guid profileId, CancellationToken ct);
    Task SoftRemovePersonAndReassignAsync(int personId, Guid profileId, Guid replacementPaymentMethodId, int replacementPersonId, CancellationToken ct);

    // ── Income sources ───────────────────────────────────────────────────────
    Task<List<IncomeSource>> ListIncomeSourcesAsync(Guid profileId, CancellationToken ct);
    Task<IncomeSource> AddIncomeSourceAsync(IncomeSource source, CancellationToken ct);
    Task<IncomeSource> UpdateIncomeSourceAsync(IncomeSource source, CancellationToken ct);
    Task DeleteIncomeSourceAsync(int id, Guid profileId, CancellationToken ct);

    // ── Income entries ───────────────────────────────────────────────────────
    Task<List<IncomeEntry>> ListIncomeEntriesAsync(Guid periodId, CancellationToken ct);
    Task<IncomeEntry> CreateIncomeEntryAsync(IncomeEntry entry, CancellationToken ct);
    Task<IncomeEntry> UpdateIncomeEntryAsync(int id, Guid periodId, decimal amount, CancellationToken ct);

    // ── Savings sources ──────────────────────────────────────────────────────
    Task<SavingsSource> GetSavingsSourceAsync(int id, Guid profileId, CancellationToken ct);
    Task<SavingsSource> AddSavingsSourceAsync(SavingsSource source, CancellationToken ct);
    Task<List<SavingsSource>> ListSavingsSourcesAsync(Guid profileId, CancellationToken ct);
    Task<SavingsSource> UpdateSavingsSourceAsync(SavingsSource source, CancellationToken ct);
    Task DeleteSavingsSourceAsync(int id, Guid profileId, CancellationToken ct);
    Task<SavingsSource> UpsertTaxReserveSavingsSourceAsync(Guid profileId, int personId, decimal amount, decimal federalAmount, decimal stateAmount, CancellationToken ct);
    Task DeleteTaxReserveSavingsSourceAsync(Guid profileId, CancellationToken ct);

    /// <summary>
    /// payment_methods isn't a mapped EF entity yet (B5 batch 3) — raw SQL, same
    /// escape-hatch pattern as the other cross-table operations in this
    /// repository. Throws NotFoundException if the payment method doesn't exist,
    /// mirroring Go's GetPaymentMethod.
    /// </summary>
    Task<int?> GetPaymentMethodBudgetPersonIdAsync(Guid paymentMethodId, CancellationToken ct);
}
