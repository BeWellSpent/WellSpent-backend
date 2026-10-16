using WellSpent.Application.Common;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.FixedExpenses;

/// <summary>Shared input shape for CreateFixedExpense/UpdateFixedExpense, mirroring Go's single FixedExpenseInput struct used by both.</summary>
public sealed record FixedExpenseFields(
    string Name, Money PlannedAmount, int? CategoryId, Guid? PaymentMethodId,
    int DayOfMonth, int IntervalMonths, DateOnly? AnchorDate, string? FrequencyUnit,
    int IntervalWeeks, int DayOfWeek, DateOnly? EndDate, int TotalPayments);

public static class FixedExpenseNormalization
{
    /// <summary>Mirrors Go's shared normalization in Create/UpdateFixedExpense: clamps day fields, derives day_of_month/day_of_week/anchor_date together when an explicit anchor is given, and floors interval fields to 1.</summary>
    public static FixedExpense BuildEntity(FixedExpenseFields f)
    {
        var unit = FixedExpenseMapping.FrequencyUnitToId(f.FrequencyUnit);
        var day = f.DayOfMonth < 1 ? 1 : f.DayOfMonth;
        var dayOfWeek = f.DayOfWeek is < 1 or > 7 ? 1 : f.DayOfWeek;
        DateOnly? anchorDate = null;
        if (f.AnchorDate is { } anchor)
        {
            (day, dayOfWeek, var derivedAnchor) = FixedExpenseScheduling.ScheduleFromAnchor(anchor);
            anchorDate = derivedAnchor;
        }

        return new FixedExpense
        {
            Name = f.Name,
            PlannedAmount = f.PlannedAmount.ToDecimal(),
            CategoryId = f.CategoryId,
            PaymentMethodId = f.PaymentMethodId,
            DayOfMonth = day,
            IntervalMonths = f.IntervalMonths < 1 ? 1 : f.IntervalMonths,
            AnchorDate = anchorDate,
            FrequencyUnit = unit,
            IntervalWeeks = f.IntervalWeeks < 1 ? 1 : f.IntervalWeeks,
            DayOfWeek = (short)dayOfWeek,
            EndDate = f.EndDate,
            TotalPayments = f.TotalPayments > 0 ? f.TotalPayments : null,
        };
    }
}
