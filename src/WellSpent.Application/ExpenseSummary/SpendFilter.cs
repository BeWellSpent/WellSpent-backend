using WellSpent.Domain.Entities;

namespace WellSpent.Application.ExpenseSummary;

/// <summary>
/// What counts as spend, in one place. Mirrors Go's spend_filter.go — shared
/// by the Expense Summary (computes the balance the user is shown) and, once
/// ported, period carryover (turns that balance into next period's
/// transactions). They must agree.
/// </summary>
public static class SpendFilter
{
    private const int FixedTransactionTypeId = 1;

    /// <summary>
    /// Explicitly excluded by the user, or a flow that is never spending: income (a
    /// payroll deposit is not a purchase) and payment (a card payment settles a balance
    /// already counted when it was spent; counting it too cancelled the card's purchases).
    /// Transfers count, signed: a Zelle to the landlord is rent.
    /// </summary>
    public static bool IsNonSpendTransaction(Transaction tx) =>
        tx.IsExcluded || !TransactionFlow.CountsAsSpending(tx.Flow);

    /// <summary>A Fixed obligation that hasn't been marked paid: planned, not spent.</summary>
    public static bool IsUnpaidFixed(Transaction tx) => IsFixed(tx) && !tx.IsPaid;

    public static bool IsFixed(Transaction tx) => tx.TransactionTypeId == FixedTransactionTypeId;
}
