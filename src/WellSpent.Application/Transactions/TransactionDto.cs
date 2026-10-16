using WellSpent.Application.Common;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.Transactions;

public sealed record TransactionDto(
    Guid Id, string? Name, Money Amount, Money PlannedAmount, DateOnly? Date, DateOnly? RenewalDate,
    Guid? BudgetPeriodId, int CategoryId, Guid? PaymentMethodId, string TransactionFrequency, string TransactionType,
    bool IsPaid, DateOnly? PaidAt, Guid? FixedExpenseId, bool IsExcluded, bool IsPlaidImported,
    Guid? InstallmentFixedExpenseId, Guid? CarriedFromBudgetPeriodId);

/// <summary>Plain-string wire convention (matches role/cycle/paymentType/incomeType elsewhere), keyed on the seeded lookup-table order (migration 000001) — same ordinals as the proto RecurringType/ExpenseType enums, so no PaymentType-style numbering mismatch here.</summary>
public static class TransactionFrequencyMapping
{
    private static readonly string[] Names = ["unspecified", "one_off", "weekly", "bi_weekly", "monthly", "yearly"];
    public static string ToName(int? id) => id is { } v && v >= 0 && v < Names.Length ? Names[v] : "unspecified";
    public static int? ToId(string? name) => name is null ? null : Array.IndexOf(Names, name) is var i && i > 0 ? i : null;
}

public static class TransactionTypeMapping
{
    private static readonly string[] Names = ["unspecified", "fixed", "variable"];
    public static string ToName(int? id) => id is { } v && v >= 0 && v < Names.Length ? Names[v] : "unspecified";
    public static int? ToId(string? name) => name is null ? null : Array.IndexOf(Names, name) is var i && i > 0 ? i : null;
}

public static class TransactionMapping
{
    public static TransactionDto ToDto(Transaction t) => new(
        t.Id, t.Name, Money.FromDecimal(t.Amount), Money.FromDecimal(t.PlannedAmount), t.Date, t.RenewalDate,
        t.BudgetPeriodId, t.CategoryId ?? 0, t.PaymentMethodId,
        TransactionFrequencyMapping.ToName(t.TransactionFrequencyId), TransactionTypeMapping.ToName(t.TransactionTypeId),
        t.IsPaid, t.PaidDate, t.FixedExpenseId, t.IsExcluded, t.PlaidTransactionId is not null,
        t.InstallmentFixedExpenseId, t.CarriedFromBudgetPeriodId);
}
