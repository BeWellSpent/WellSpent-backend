using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Transactions;

/// <summary>Pure port of transaction_service.go's assertNotBackdated/assertOnlyCategoryChanged. C#'s decimal compares exactly by value regardless of scale, so no Go-style float64 workaround is needed for amount equality.</summary>
public static class TransactionRules
{
    private const int FixedTypeId = 1;

    /// <summary>Rejects a Variable transaction dated before the period's own start — Fixed is exempt (template-driven, not manually dated).</summary>
    public static void AssertNotBackdated(int? transactionTypeId, DateOnly? date, BudgetPeriod period)
    {
        var isFixed = transactionTypeId == FixedTypeId;
        if (isFixed || date is null) return;
        if (date < period.StartDate)
        {
            throw new AppValidationException("transaction date falls within an archived period");
        }
    }

    /// <summary>Rejects an update unless it differs from the existing transaction only in CategoryId — used for Plaid-imported rows and any row whose period has archived.</summary>
    public static void AssertOnlyCategoryChanged(Transaction edit, Transaction existing)
    {
        if (edit.Name == existing.Name &&
            edit.Amount == existing.Amount &&
            edit.PlannedAmount == existing.PlannedAmount &&
            edit.Date == existing.Date &&
            edit.PaymentMethodId == existing.PaymentMethodId &&
            edit.TransactionFrequencyId == existing.TransactionFrequencyId &&
            edit.TransactionTypeId == existing.TransactionTypeId)
        {
            return;
        }
        throw new AppValidationException("only the category can be changed for this transaction");
    }
}
