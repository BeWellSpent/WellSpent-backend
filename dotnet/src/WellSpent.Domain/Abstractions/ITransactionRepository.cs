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

    /// <summary>Null when no transaction carries this Plaid id — a normal case (not yet imported), not an error.</summary>
    Task<Transaction?> GetTransactionByPlaidIdAsync(string plaidTransactionId, CancellationToken ct);

    Task<bool> ExistsTransactionByPlaidIdAsync(string plaidTransactionId, CancellationToken ct);

    /// <summary>Name/Amount only, matched by PlaidTransactionId — mirrors Go's UpdateTransactionFromPlaid exactly (a Plaid 'modified' entry never changes anything else).</summary>
    Task UpdateTransactionFromPlaidAsync(string plaidTransactionId, string name, decimal amount, CancellationToken ct);

    Task DeleteTransactionByPlaidIdAsync(string plaidTransactionId, CancellationToken ct);

    /// <summary>
    /// Repoints a pending transaction onto the posted one that settled it —
    /// an UPDATE-in-place (new Plaid id, name, amount, date) on the existing
    /// row, preserving its own id so FKs like transaction_review.transaction_id
    /// survive. Never delete+reinsert (issue #67).
    /// </summary>
    Task<Transaction> RepointTransactionPlaidIdAsync(string oldPlaidTransactionId, string newPlaidTransactionId, string name, decimal amount, DateOnly date, CancellationToken ct);

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

    /// <summary>Every seeded system category's stable key -&gt; id, for resolving a <c>system_key</c> (e.g. "income", "savings") without a user context. A row with no key is skipped, never keyed by name.</summary>
    Task<Dictionary<string, int>> ListSystemCategoriesAsync(CancellationToken ct);

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

    /// <summary>Also used to create a method from a linked Plaid account (PlaidAccountId/PlaidItemId set on the entity) — Go has a separate named SQL query for this, but the INSERT itself is identical either way.</summary>
    Task<PaymentMethod> CreatePaymentMethodAsync(PaymentMethod method, CancellationToken ct);

    /// <summary>Name/color/alias only — PaymentType can't be changed after creation. Null alias clears it.</summary>
    Task<PaymentMethod> UpdatePaymentMethodAsync(Guid id, string name, string color, string? alias, CancellationToken ct);

    /// <summary>Null when no active method has this Plaid account id — a normal case (the account is new), not an error.</summary>
    Task<PaymentMethod?> GetPaymentMethodByPlaidAccountIdAsync(string plaidAccountId, CancellationToken ct);

    /// <summary>Null when the user has no active method with this exact name — a normal case, not an error.</summary>
    Task<PaymentMethod?> GetPaymentMethodByUserAndNameAsync(Guid userId, string name, CancellationToken ct);

    Task UpdatePaymentMethodPlaidAccountIdAsync(Guid id, string plaidAccountId, CancellationToken ct);

    Task<List<PaymentMethod>> ListActivePaymentMethodsByPlaidItemIdAsync(Guid plaidItemId, CancellationToken ct);

    /// <summary>
    /// Deactivates without reassigning transactions — used when an account is
    /// removed from a Plaid connection via update mode. Existing transactions
    /// keep pointing at the now-inactive method (correct: they record what
    /// really happened), but FixedExpense/SavingsSource templates pointing
    /// here are cleared first, atomically, so a recurring bill doesn't keep
    /// respawning onto a dead account forever.
    /// </summary>
    Task DeactivatePaymentMethodAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Reassigns every transaction, savings source, and fixed-expense template
    /// from `id` to `replacementId`, then soft-deletes `id`. Raw SQL: the
    /// transaction/fixed_expense updates reach tables not mapped as EF
    /// entities yet (B5 batches 4/5); savings_source is mapped but included in
    /// the same statement to match Go's single CTE exactly.
    /// </summary>
    Task DeletePaymentMethodAndReassignAsync(Guid id, Guid replacementId, Guid budgetProfileId, CancellationToken ct);
}
