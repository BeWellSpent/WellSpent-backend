using WellSpent.Application.Common;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.FixedExpenses;

public sealed record FixedExpenseDto(
    Guid Id, Guid BudgetProfileId, string Name, Money PlannedAmount, int CategoryId, Guid? PaymentMethodId,
    int DayOfMonth, bool IsActive, int IntervalMonths, DateOnly NextDueDate, DateOnly? AnchorDate,
    string FrequencyUnit, int IntervalWeeks, int DayOfWeek, DateOnly? EndDate, int TotalPayments,
    bool IsInstallmentPlan, int PaymentsMade, Money RemainingBalance, DateOnly? PaidOffDate);

public static class FixedExpenseMapping
{
    public static string FrequencyUnitToName(short unit) => unit == 2 ? "week" : "month";
    public static short FrequencyUnitToId(string? name) => name == "week" ? (short)2 : (short)1;

    /// <summary>amountPaid is the amount-based basis for RemainingBalance (sum of every paid transaction linked to fe) — see FixedExpensePayoff.AmountPaidAsync.</summary>
    public static FixedExpenseDto ToDto(FixedExpense fe, DateOnly today, decimal amountPaid)
    {
        return new(
            fe.Id, fe.BudgetProfileId, fe.Name, Money.FromDecimal(fe.PlannedAmount), fe.CategoryId ?? 0, fe.PaymentMethodId,
            fe.DayOfMonth, fe.IsActive, fe.IntervalMonths, FixedExpenseScheduling.NextDueDate(fe, today), fe.AnchorDate,
            FrequencyUnitToName(fe.FrequencyUnit), fe.IntervalWeeks, fe.DayOfWeek, fe.EndDate, fe.TotalPayments ?? 0,
            fe.IsInstallmentPlan, FixedExpenseScheduling.PaymentsMade(fe, today),
            Money.FromDecimal(FixedExpensePayoff.RemainingBalance(fe, amountPaid)), fe.PaidOffDate);
    }
}
