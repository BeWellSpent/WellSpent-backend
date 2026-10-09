using WellSpent.Domain.Entities;

namespace WellSpent.Domain.Abstractions;

/// <summary>
/// Covers Transaction, Category, and PaymentMethod — mirroring Go's single
/// transactionRepository grouping all three. Batch 5 (Installment plans +
/// Fixed expenses) adds the installment-plan link methods; batch 7
/// (Transaction review) stays its own interface, matching Go's separate
/// TransactionReviewRepository.
/// </summary>
public interface ITransactionRepository
{
    // ── Transactions ─────────────────────────────────────────────────────────
    Task<Transaction> GetTransactionAsync(Guid id, CancellationToken ct);

    Task<List<Transaction>> ListTransactionsAsync(
        Guid budgetPeriodId, int? categoryId, int? transactionTypeId, int? focusedPersonId, CancellationToken ct);

    Task<Transaction> CreateTransactionAsync(Transaction transaction, CancellationToken ct);

    /// <summary>Full replace of the editable fields (Name/Amount/PlannedAmount/Date/Category/PaymentMethod/Frequency/Type) — not BudgetPeriodId, RenewalDate, or any paid/excluded/Plaid/installment field.</summary>
    Task<Transaction> UpdateTransactionAsync(Transaction transaction, CancellationToken ct);

    /// <summary>
    /// Mirrors Go's SQL exactly, including its edge case: `budget_period_id = NULL`
    /// is never true in Postgres, so a transaction with no period silently
    /// fails to delete here too — not fixed, since it's the same real
    /// behavior the Go query has.
    /// </summary>
    Task DeleteTransactionAsync(Guid id, Guid? budgetPeriodId, CancellationToken ct);

    Task<Transaction> MarkTransactionAsPaidAsync(Guid id, Guid budgetPeriodId, decimal amount, DateOnly paidDate, CancellationToken ct);

    /// <summary>Resets Amount back to PlannedAmount, matching Go's SQL exactly.</summary>
    Task<Transaction> UnmarkTransactionAsPaidAsync(Guid id, Guid budgetPeriodId, CancellationToken ct);

    /// <summary>Sets IsExcluded and the link in one call, so a transaction can never be excluded without the plan that explains why.</summary>
    Task<Transaction> SetInstallmentPlanAsync(Guid id, Guid budgetPeriodId, Guid installmentFixedExpenseId, CancellationToken ct);

    /// <summary>Reverses SetInstallmentPlanAsync: un-excludes and unlinks.</summary>
    Task<Transaction> ClearInstallmentPlanAsync(Guid id, Guid budgetPeriodId, CancellationToken ct);

    Task<List<Transaction>> ListByFixedExpenseAsync(Guid fixedExpenseId, CancellationToken ct);
    Task DeleteByFixedExpenseAsync(Guid fixedExpenseId, CancellationToken ct);

    Task<Transaction> SetTransactionExcludedAsync(Guid id, Guid budgetPeriodId, bool excluded, CancellationToken ct);

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
