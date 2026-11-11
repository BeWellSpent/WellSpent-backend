using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Transactions;

/// <summary>Pure port of transaction_service.go's assertNotBackdated/assertOnlyCategoryChanged. C#'s decimal compares exactly by value regardless of scale, so no Go-style float64 workaround is needed for amount equality.</summary>
public static class TransactionRules
{
    /// <summary>The contract's 0 means "no category"; stored as null, since no category row has id 0.</summary>
    public static int? CategoryOrNull(int? categoryId) => categoryId is > 0 ? categoryId : null;

    public static void AssertValidFlow(string? flow)
    {
        if (flow is not null && !TransactionFlow.All.Contains(flow))
        {
            throw new AppValidationException($"flow must be one of {string.Join(", ", TransactionFlow.All)}");
        }
    }

    /// <summary>Rejects an update unless it differs from the existing transaction only in CategoryId (and Flow) — used for Plaid-imported rows and any row whose period has archived.</summary>
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
