using WellSpent.Application.FixedExpenses;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.Budgets;

/// <summary>
/// Pure period-boundary calculator driven by a household's actual income schedule
/// instead of the calendar. No I/O, no clock dependency beyond the parameters the
/// caller supplies. Falls back to <see cref="BudgetPeriodDates"/>'s calendar math
/// when no income source has a regular, currently-effective schedule.
/// </summary>
public static class IncomePeriodDates
{
    /// <summary>Has a schedule to compute from — not one-off, and an anchor is set.</summary>
    public static bool IsRegular(IncomeSource source) => source.PaymentFrequency != "one_off" && source.AnchorDate is not null;

    /// <summary>Whether this schedule version is the one in force on `asOf` (EffectiveFrom inclusive, EffectiveTo exclusive).</summary>
    public static bool IsEffectiveAt(IncomeSource source, DateOnly asOf) =>
        (source.EffectiveFrom is null || source.EffectiveFrom <= asOf) &&
        (source.EffectiveTo is null || asOf < source.EffectiveTo);

    /// <summary>The next date on or after `onOrAfter` that `source` pays, per its own frequency/anchor.</summary>
    public static DateOnly NextPayDate(IncomeSource source, DateOnly onOrAfter)
    {
        var anchor = source.AnchorDate!.Value;
        if (onOrAfter <= anchor) return anchor;

        return source.PaymentFrequency switch
        {
            "weekly" => NextIntervalDays(anchor, onOrAfter, 7),
            "bi_weekly" => NextIntervalDays(anchor, onOrAfter, 14),
            "yearly" => NextYearly(source, onOrAfter),
            _ => NextMonthly(source, onOrAfter), // "monthly" and any unrecognized value default to monthly
        };
    }

    private static DateOnly NextIntervalDays(DateOnly anchor, DateOnly onOrAfter, int intervalDays)
    {
        var daysSince = onOrAfter.DayNumber - anchor.DayNumber;
        var steps = (daysSince + intervalDays - 1) / intervalDays; // ceil
        return anchor.AddDays(steps * intervalDays);
    }

    /// <summary>Saturday rolls back 1 day, Sunday rolls back 2 days, to the preceding Friday. Anything else is unchanged.</summary>
    private static DateOnly AdjustToWeekday(DateOnly date) => date.DayOfWeek switch
    {
        DayOfWeek.Saturday => date.AddDays(-1),
        DayOfWeek.Sunday => date.AddDays(-2),
        _ => date,
    };

    private static DateOnly MaybeAdjust(IncomeSource source, DateOnly date) =>
        source.AdjustForWeekends ? AdjustToWeekday(date) : date;

    private static DateOnly NextMonthly(IncomeSource source, DateOnly onOrAfter)
    {
        var anchor = source.AnchorDate!.Value;
        var monthStart = new DateOnly(onOrAfter.Year, onOrAfter.Month, 1);
        var candidate = MaybeAdjust(source, FixedExpenseScheduling.DateInMonth(anchor.Day, monthStart));
        // Weekend adjustment only ever moves a candidate backward, so it can push an
        // on-or-after-qualifying date into the past relative to `onOrAfter` — same fix as
        // the pre-existing "already passed" case below: roll to next month instead.
        if (candidate < onOrAfter)
        {
            monthStart = monthStart.AddMonths(1);
            candidate = MaybeAdjust(source, FixedExpenseScheduling.DateInMonth(anchor.Day, monthStart));
        }
        return candidate;
    }

    private static DateOnly NextYearly(IncomeSource source, DateOnly onOrAfter)
    {
        var anchor = source.AnchorDate!.Value;
        var monthStart = new DateOnly(onOrAfter.Year, anchor.Month, 1);
        var candidate = MaybeAdjust(source, FixedExpenseScheduling.DateInMonth(anchor.Day, monthStart));
        if (candidate < onOrAfter)
        {
            monthStart = new DateOnly(onOrAfter.Year + 1, anchor.Month, 1);
            candidate = MaybeAdjust(source, FixedExpenseScheduling.DateInMonth(anchor.Day, monthStart));
        }
        return candidate;
    }

    private static List<IncomeSource> Qualifying(IReadOnlyList<IncomeSource> sources, DateOnly asOf) =>
        sources.Where(s => IsRegular(s) && IsEffectiveAt(s, asOf)).ToList();

    /// <summary>The budget's very first period, starting today. Falls back to calendar-month math when no income source has a qualifying schedule.</summary>
    public static (DateOnly Start, DateOnly End) ComputeFirst(IReadOnlyList<IncomeSource> sources, string fallbackCycle, DateOnly today)
    {
        var qualifying = Qualifying(sources, today);
        if (qualifying.Count == 0) return BudgetPeriodDates.ComputeFirst(fallbackCycle, today);
        return (today, EndFor(qualifying, fallbackCycle, today));
    }

    /// <summary>The period following one that ended on `prevEnd`. Falls back to calendar-month math when no income source has a qualifying schedule.</summary>
    public static (DateOnly Start, DateOnly End) ComputeNext(IReadOnlyList<IncomeSource> sources, string cycle, DateOnly prevEnd)
    {
        var start = prevEnd.AddDays(1);
        var qualifying = Qualifying(sources, start);
        if (qualifying.Count == 0) return BudgetPeriodDates.ComputeNext(cycle, prevEnd);
        return (start, EndFor(qualifying, cycle, start));
    }

    /// <summary>
    /// A period always starts the day after the previous one ends, so the timeline never has
    /// holes; paydays only decide where it ends. The end is the day before the payday closest
    /// to one full <c>cycle</c> after `start`, pulled earlier by that payday's
    /// <see cref="IncomeSource.EarlyArrivalToleranceDays"/> — so a monthly budget paid on the
    /// 15th and the last day gets month-long periods, not half-month ones, and an early deposit
    /// already sits in the period it funds (the same rule <see cref="ResolveFundedDate"/> applies
    /// per transaction). A payday whose early window already covers `start` belongs to this
    /// period's start, so it's skipped as an end candidate.
    /// </summary>
    private static DateOnly EndFor(List<IncomeSource> qualifying, string cycle, DateOnly start)
    {
        var ideal = BudgetPeriodDates.AddCycle(start, cycle);
        var next = qualifying
            .Select(s => (Date: NextPayDate(s, start.AddDays(1 + s.EarlyArrivalToleranceDays)), Tolerance: s.EarlyArrivalToleranceDays))
            .OrderBy(x => Math.Abs(x.Date.DayNumber - ideal.DayNumber))
            .ThenBy(x => x.Date.DayNumber)
            .First();
        var end = next.Date.AddDays(-1 - next.Tolerance);
        return end < start ? start : end;
    }

    /// <summary>
    /// Which of the already-generated `periods` (contiguous, non-overlapping, inclusive ranges)
    /// contains `date`, or null if none does. Periods are always pre-generated and stored (see
    /// <c>IBudgetProfileRepository.GetPeriodByDateAsync</c>) — this is the pure containment
    /// check a caller runs against that already-known list, not a re-derivation from scratch.
    /// </summary>
    public static (DateOnly Start, DateOnly End)? GetPeriodForDate(IReadOnlyList<(DateOnly Start, DateOnly End)> periods, DateOnly date)
    {
        foreach (var period in periods)
            if (period.Start <= date && date <= period.End) return period;
        return null;
    }

    /// <summary>
    /// Which date an income deposit should be treated as funding: `depositDate` itself, unless
    /// it arrived early enough (within <see cref="IncomeSource.EarlyArrivalToleranceDays"/> of its
    /// next scheduled pay date) to count as funding that scheduled date instead. The caller then
    /// resolves the period for whichever date comes back, via <see cref="GetPeriodForDate"/>.
    /// Irregular sources (no schedule to be early against) always fund on their deposit date.
    /// </summary>
    public static DateOnly ResolveFundedDate(IncomeSource source, DateOnly depositDate)
    {
        if (!IsRegular(source)) return depositDate;

        var scheduled = NextPayDate(source, depositDate); // always >= depositDate
        var earlyByDays = scheduled.DayNumber - depositDate.DayNumber;
        return earlyByDays <= source.EarlyArrivalToleranceDays ? scheduled : depositDate;
    }
}
