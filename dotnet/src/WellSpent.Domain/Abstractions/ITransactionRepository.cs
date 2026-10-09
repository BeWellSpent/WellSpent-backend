using WellSpent.Domain.Entities;

namespace WellSpent.Domain.Abstractions;

/// <summary>
/// Covers Category and PaymentMethod (B5 batch 3) — the two Go groups this
/// interface will host when B5 batch 4 (Transactions core) extends it with
/// Transaction CRUD, mirroring Go's single transactionRepository grouping all
/// three. A second interface isn't introduced for the same reason
/// IBudgetProfileRepository stays one interface across its own batches.
/// </summary>
public interface ITransactionRepository
{
    // ── Categories ───────────────────────────────────────────────────────────
    Task<Category> GetCategoryAsync(int id, CancellationToken ct);

    /// <summary>Every global system category plus the caller's own active ones.</summary>
    Task<List<Category>> ListCategoriesAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Same set as ListCategoriesAsync, plus any category referenced by a
    /// transaction or fixed expense in the given budget — so a collaborator
    /// sees categories the owner created. Raw SQL: the transaction/
    /// fixed_expense subqueries reach tables not mapped as EF entities yet
    /// (B5 batches 4/5).
    /// </summary>
    Task<List<Category>> ListCategoriesForBudgetAsync(Guid userId, Guid budgetProfileId, CancellationToken ct);

    Task<Category> CreateCategoryAsync(string name, Guid userId, string color, CancellationToken ct);

    /// <summary>Only the caller's own, non-system categories — scoped in the WHERE clause, not a separate check.</summary>
    Task<Category> UpdateCategoryAsync(int id, Guid userId, string name, string color, CancellationToken ct);

    Task<Category> UpdateSystemCategoryColorAsync(int id, string color, CancellationToken ct);

    /// <summary>
    /// Reassigns every transaction and fixed-expense template from `id` to
    /// `replacementId`, then soft-deletes `id`. Raw SQL for the same reason as
    /// ListCategoriesForBudgetAsync. No budget scoping — categories are
    /// user-scoped, so reassignment spans every period.
    /// </summary>
    Task DeleteCategoryAndReassignAsync(int id, Guid userId, int replacementId, CancellationToken ct);

    // ── Payment methods ──────────────────────────────────────────────────────
    Task<PaymentMethod> GetPaymentMethodAsync(Guid id, CancellationToken ct);

    Task<List<PaymentMethod>> ListPaymentMethodsAsync(Guid budgetProfileId, CancellationToken ct);

    Task<PaymentMethod> CreatePaymentMethodAsync(PaymentMethod method, CancellationToken ct);

    /// <summary>Name/color/alias only — PaymentType can't be changed after creation. Null alias clears it.</summary>
    Task<PaymentMethod> UpdatePaymentMethodAsync(Guid id, string name, string color, string? alias, CancellationToken ct);

    /// <summary>
    /// Reassigns every transaction, savings source, and fixed-expense template
    /// from `id` to `replacementId`, then soft-deletes `id`. Raw SQL: the
    /// transaction/fixed_expense updates reach tables not mapped as EF
    /// entities yet (B5 batches 4/5); savings_source is mapped but included in
    /// the same statement to match Go's single CTE exactly.
    /// </summary>
    Task DeletePaymentMethodAndReassignAsync(Guid id, Guid replacementId, Guid budgetProfileId, CancellationToken ct);
}
