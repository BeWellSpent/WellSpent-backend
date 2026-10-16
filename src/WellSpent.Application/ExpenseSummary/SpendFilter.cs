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
    /// Resolves the system categories that are never spending, whatever
    /// their sign or type: Income (a payroll deposit is not a purchase) and
    /// Payment (a credit-card payment settles a balance already counted when
    /// it was spent — imported twice, positive on the paying account and
    /// negative on the card, so counting both made a linked card cancel out
    /// its own purchases). A key may legitimately be missing from
    /// systemCategories in a fresh environment; nothing is filtered on it
    /// then. Transfer is deliberately NOT included — see Go's own comment in
    /// spend_filter.go for why only the card-payment case is double-counted.
    /// </summary>
    public static HashSet<int> NonSpendCategoryIds(Dictionary<string, int> systemCategories)
    {
        var ids = new HashSet<int>();
        foreach (var key in new[] { "income", "payment" })
        {
            if (systemCategories.TryGetValue(key, out var id)) ids.Add(id);
        }
        return ids;
    }

    /// <summary>Explicitly excluded by the user, or filed under a category that never counts as spend.</summary>
    public static bool IsNonSpendTransaction(Transaction tx, HashSet<int> nonSpend) =>
        tx.IsExcluded || (tx.CategoryId is { } catId && nonSpend.Contains(catId));

    /// <summary>A Fixed obligation that hasn't been marked paid: planned, not spent.</summary>
    public static bool IsUnpaidFixed(Transaction tx) => IsFixed(tx) && !tx.IsPaid;

    public static bool IsFixed(Transaction tx) => tx.TransactionTypeId == FixedTransactionTypeId;
}
