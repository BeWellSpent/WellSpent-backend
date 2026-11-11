namespace WellSpent.Domain.Entities;

/// <summary>
/// What a movement of money is, separate from its category (what it was for).
/// Spend, Refund and Transfer count toward spending, signed by direction; Income
/// and Payment never do. Derived from the category and amount unless set explicitly.
/// </summary>
public static class TransactionFlow
{
    public const string Spend = "spend";
    public const string Refund = "refund";
    public const string Income = "income";
    public const string Transfer = "transfer";
    public const string Payment = "payment";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Spend, Refund, Income, Transfer, Payment };

    /// <summary>The system categories that imply a flow; their keys match the flow names.</summary>
    public static string? ForSystemKey(string? systemKey) => systemKey switch
    {
        "payment" => Payment,
        "income" => Income,
        "transfer" => Transfer,
        _ => null,
    };

    public static string BySign(decimal amount) => amount < 0 ? Refund : Spend;

    public static string Infer(string? systemKey, decimal amount) => ForSystemKey(systemKey) ?? BySign(amount);

    /// <summary>
    /// A category change with no explicit flow. A flow-implying category sets it. Otherwise
    /// spend/refund follow the sign, income/payment fall back to it only when the old category
    /// was what made them non-spending, and a transfer stays a transfer (it counts either way).
    /// </summary>
    public static string OnCategoryChange(string? currentFlow, string? oldSystemKey, string? newSystemKey, decimal amount)
    {
        if (ForSystemKey(newSystemKey) is { } forced) return forced;
        return currentFlow switch
        {
            null or Spend or Refund => BySign(amount),
            Income or Payment when ForSystemKey(oldSystemKey) == currentFlow => BySign(amount),
            _ => currentFlow,
        };
    }

    /// <summary>Only spend/refund follow the sign; the others keep their meaning whatever the amount.</summary>
    public static string OnAmountChange(string? currentFlow, decimal amount) =>
        currentFlow is null or Spend or Refund ? BySign(amount) : currentFlow;

    public static bool CountsAsSpending(string? flow) => flow is not (Income or Payment);
}
