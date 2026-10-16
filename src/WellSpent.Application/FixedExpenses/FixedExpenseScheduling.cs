using WellSpent.Domain.Entities;

namespace WellSpent.Application.FixedExpenses;

/// <summary>
/// Pure port of budget_profile_service.go's fixed-expense scheduling math
/// (month-based and week-based cadence, next-due-date, payments-made). No
/// I/O, no clock dependency beyond the `asOf`/`from` parameter the caller
/// supplies — every function here is a one-line unit test.
/// </summary>
public static class FixedExpenseScheduling
{
    private static readonly DateOnly MondayEpoch = new(1970, 1, 5);

    /// <summary>1 = Monday .. 7 = Sunday (ISO 8601), from .NET's Sunday=0..Saturday=6.</summary>
    public static int IsoWeekday(DateOnly d) => d.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)d.DayOfWeek;

    /// <summary>The one place day_of_month/day_of_week/anchor_date are derived together from an explicit anchor — shared by Create/UpdateFixedExpense and the mark-paid template sync.</summary>
    public static (int DayOfMonth, int DayOfWeek, DateOnly AnchorDate) ScheduleFromAnchor(DateOnly anchor) =>
        (anchor.Day, IsoWeekday(anchor), anchor);

    private static int MonthIndex(DateOnly d) => d.Year * 12 + d.Month;

    /// <summary>AnchorDate when set, otherwise CreatedAt's date.</summary>
    public static DateOnly Anchor(FixedExpense fe) => fe.AnchorDate ?? DateOnly.FromDateTime(fe.CreatedAt);

    public static bool IsWeekUnit(FixedExpense fe) => fe.FrequencyUnit == 2;

    /// <summary>Due when the number of months elapsed since the anchor's month is a multiple of IntervalMonths. Never due before the anchor month (covers a future AnchorDate not having arrived yet).</summary>
    public static bool IsDueInMonth(FixedExpense fe, DateOnly monthStart)
    {
        var interval = Math.Max(fe.IntervalMonths, 1);
        var diff = MonthIndex(monthStart) - MonthIndex(Anchor(fe));
        return diff >= 0 && diff % interval == 0;
    }

    /// <summary>fe's transaction date within monthStart's month, DayOfMonth clamped to that month's last day.</summary>
    public static DateOnly DateInMonth(FixedExpense fe, DateOnly monthStart)
    {
        var lastDay = DateTime.DaysInMonth(monthStart.Year, monthStart.Month);
        var day = Math.Clamp(fe.DayOfMonth < 1 ? 1 : fe.DayOfMonth, 1, lastDay);
        return new DateOnly(monthStart.Year, monthStart.Month, day);
    }

    /// <summary>The Monday of d's week.</summary>
    public static DateOnly WeekStart(DateOnly d)
    {
        var offset = ((int)d.DayOfWeek + 6) % 7;
        return d.AddDays(-offset);
    }

    private static int WeekIndex(DateOnly d) => FloorDiv(d.DayNumber - MondayEpoch.DayNumber, 7);

    private static int FloorDiv(int a, int b) => a >= 0 ? a / b : (a - b + 1) / b;

    public static bool IsDueInWeek(FixedExpense fe, DateOnly weekStart)
    {
        var interval = Math.Max(fe.IntervalWeeks, 1);
        var diff = WeekIndex(weekStart) - WeekIndex(WeekStart(Anchor(fe)));
        return diff >= 0 && diff % interval == 0;
    }

    /// <summary>fe's transaction date within the week starting at weekStart. Every week has all 7 days, so no clamping is needed (unlike day-of-month).</summary>
    public static DateOnly DateInWeek(FixedExpense fe, DateOnly weekStart)
    {
        var dow = fe.DayOfWeek is >= 1 and <= 7 ? fe.DayOfWeek : 1;
        return weekStart.AddDays(dow - 1);
    }

    /// <summary>Next date on or after `from` that fe is due, as a full transaction date.</summary>
    public static DateOnly NextDueDate(FixedExpense fe, DateOnly from)
    {
        if (IsWeekUnit(fe)) return NextDueDateWeekly(fe, from);

        var interval = Math.Max(fe.IntervalMonths, 1);
        var monthStart = new DateOnly(from.Year, from.Month, 1);
        var anchorMonthStart = new DateOnly(Anchor(fe).Year, Anchor(fe).Month, 1);
        if (anchorMonthStart > monthStart) monthStart = anchorMonthStart;

        for (var i = 0; i < interval; i++)
        {
            if (IsDueInMonth(fe, monthStart)) return DateInMonth(fe, monthStart);
            monthStart = monthStart.AddMonths(1);
        }
        // Unreachable in practice — a multiple of interval always appears within `interval` months.
        return DateInMonth(fe, new DateOnly(from.Year, from.Month, 1));
    }

    private static DateOnly NextDueDateWeekly(FixedExpense fe, DateOnly from)
    {
        var interval = Math.Max(fe.IntervalWeeks, 1);
        var ws = WeekStart(from);
        var anchorWeekStart = WeekStart(Anchor(fe));
        if (anchorWeekStart > ws) ws = anchorWeekStart;

        for (var i = 0; i < interval; i++)
        {
            if (IsDueInWeek(fe, ws)) return DateInWeek(fe, ws);
            ws = ws.AddDays(7);
        }
        return DateInWeek(fe, WeekStart(from));
    }

    /// <summary>How many of fe's planned payments have come due as of `asOf`, clamped to [0, TotalPayments]. 0 when TotalPayments is unset.</summary>
    public static int PaymentsMade(FixedExpense fe, DateOnly asOf)
    {
        if (fe.TotalPayments is not { } total || total <= 0) return 0;
        var made = IsWeekUnit(fe) ? PaymentsMadeWeekly(fe, asOf) : PaymentsMadeMonthly(fe, asOf);
        return Math.Clamp(made, 0, total);
    }

    private static int PaymentsMadeMonthly(FixedExpense fe, DateOnly asOf)
    {
        var interval = Math.Max(fe.IntervalMonths, 1);
        var diff = MonthIndex(asOf) - MonthIndex(Anchor(fe));
        if (diff < 0) return 0;
        var elapsed = diff / interval;
        if (diff % interval != 0) return elapsed + 1;
        var monthStart = new DateOnly(asOf.Year, asOf.Month, 1);
        return DateInMonth(fe, monthStart) > asOf ? elapsed : elapsed + 1;
    }

    private static int PaymentsMadeWeekly(FixedExpense fe, DateOnly asOf)
    {
        var interval = Math.Max(fe.IntervalWeeks, 1);
        var ws = WeekStart(asOf);
        var diff = WeekIndex(ws) - WeekIndex(WeekStart(Anchor(fe)));
        if (diff < 0) return 0;
        var elapsed = diff / interval;
        if (diff % interval != 0) return elapsed + 1;
        return DateInWeek(fe, ws) > asOf ? elapsed : elapsed + 1;
    }
}
