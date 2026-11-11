using WellSpent.Domain.Entities;

namespace WellSpent.Application.Plaid;

/// <summary>
/// What an imported transaction is, from Plaid's classification (issue #119). Null means
/// ordinary spending or a refund, which the sign decides on save.
/// </summary>
public static class PlaidFlowMapping
{
    public static string? Resolve(string name, string? pfcPrimary, string? pfcDetailed)
    {
        // A payroll deposit Plaid didn't classify (no PFC data) is still income.
        if (name.Contains("payroll", StringComparison.OrdinalIgnoreCase)) return TransactionFlow.Income;
        if (pfcDetailed == "LOAN_PAYMENTS_CREDIT_CARD_PAYMENT") return TransactionFlow.Payment;
        // Moves into savings are saving, not a transfer: they keep the Savings category and count.
        if (pfcDetailed is "TRANSFER_IN_SAVINGS" or "TRANSFER_OUT_SAVINGS") return null;
        return pfcPrimary switch
        {
            "INCOME" => TransactionFlow.Income,
            "TRANSFER_IN" or "TRANSFER_OUT" => TransactionFlow.Transfer,
            _ => null,
        };
    }
}
