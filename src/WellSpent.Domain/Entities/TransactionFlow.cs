namespace WellSpent.Domain.Entities;

/// <summary>
/// What a movement of money is, separate from its category (what it was for).
/// Spend, Refund and Transfer count toward spending, signed by direction; Income
/// and Payment never do. Set explicitly, or by the sign for plain spending and refunds.
/// </summary>
public static class TransactionFlow
{
    public const string Spend = "spend";
    public const string Refund = "refund";
    public const string Income = "income";
    public const string Transfer = "transfer";
    public const string Payment = "payment";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Spend, Refund, Income, Transfer, Payment };

    public static string BySign(decimal amount) => amount < 0 ? Refund : Spend;

    /// <summary>Only spend/refund follow the sign; the others keep their meaning whatever the amount.</summary>
    public static string OnAmountChange(string? currentFlow, decimal amount) =>
        currentFlow is null or Spend or Refund ? BySign(amount) : currentFlow;

    public static bool CountsAsSpending(string? flow) => flow is not (Income or Payment);
}
