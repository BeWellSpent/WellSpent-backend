using WellSpent.Domain.Entities;

namespace WellSpent.Domain.Abstractions;

/// <summary>Mirrors Go's separate FixedExpenseRepository (not folded into ITransactionRepository, matching that same split in Go).</summary>
public interface IFixedExpenseRepository
{
    Task<FixedExpense> CreateAsync(FixedExpense fixedExpense, CancellationToken ct);
    Task<FixedExpense> GetByIdAsync(Guid id, CancellationToken ct);
    Task<List<FixedExpense>> ListAsync(Guid budgetProfileId, CancellationToken ct);

    /// <summary>Full replace, scoped by (id, budgetProfileId).</summary>
    Task<FixedExpense> UpdateAsync(FixedExpense fixedExpense, CancellationToken ct);

    /// <summary>Brings the template up to what actually happened when a bill was paid: amount, due date, and (when observed) category/payment method.</summary>
    Task UpdateFromPaymentAsync(
        Guid id, decimal plannedAmount, int dayOfMonth, int dayOfWeek, DateOnly? anchorDate,
        int? categoryId, Guid? paymentMethodId, CancellationToken ct);

    Task DeactivateAsync(Guid id, Guid budgetProfileId, CancellationToken ct);

    Task<bool> HasTransactionInMonthAsync(Guid fixedExpenseId, DateOnly monthStart, DateOnly monthEnd, CancellationToken ct);
    Task<bool> HasTransactionOnDateAsync(Guid fixedExpenseId, DateOnly targetDate, CancellationToken ct);

    /// <summary>
    /// This fixed expense's transaction in any live period, preferring an
    /// unpaid one — null when none exists. Used by UpdateFixedExpense to ask
    /// "does the bill exist at all", not "is it unpaid" (issue #62: asking the
    /// narrower question made an already-paid bill look absent and spawned a
    /// duplicate).
    /// </summary>
    Task<Transaction?> GetTransactionAsync(Guid fixedExpenseId, Guid budgetProfileId, CancellationToken ct);

    Task DeleteUnpaidTransactionsAsync(Guid fixedExpenseId, Guid budgetProfileId, CancellationToken ct);

    /// <summary>Propagates a template edit onto the current period's unpaid transaction.</summary>
    Task UpdateTransactionFromFixedExpenseAsync(
        Guid fixedExpenseId, Guid budgetProfileId, string name, decimal plannedAmount,
        int? categoryId, Guid? paymentMethodId, DateOnly date, CancellationToken ct);

    /// <summary>Propagates a template edit onto an already-paid transaction — name/category/payment method only, never the amount or paid state.</summary>
    Task UpdatePaidTransactionFromFixedExpenseAsync(
        Guid fixedExpenseId, Guid budgetProfileId, string name, int? categoryId, Guid? paymentMethodId, CancellationToken ct);
}
