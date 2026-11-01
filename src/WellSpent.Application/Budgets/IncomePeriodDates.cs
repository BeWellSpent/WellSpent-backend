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
            "yearly" => NextYearly(anchor, onOrAfter),
            _ => NextMonthly(anchor, onOrAfter), // "monthly" and any unrecognized value default to monthly
        };
    }

    private static DateOnly NextIntervalDays(DateOnly anchor, DateOnly onOrAfter, int intervalDays)
    {
        var daysSince = onOrAfter.DayNumber - anchor.DayNumber;
        var steps = (daysSince + intervalDays - 1) / intervalDays; // ceil
        return anchor.AddDays(steps * intervalDays);
    }

    private static DateOnly NextMonthly(DateOnly anchor, DateOnly onOrAfter)
    {
        var monthStart = new DateOnly(onOrAfter.Year, onOrAfter.Month, 1);
        var candidate = FixedExpenseScheduling.DateInMonth(anchor.Day, monthStart);
        if (candidate < onOrAfter)
        {
            monthStart = monthStart.AddMonths(1);
            candidate = FixedExpenseScheduling.DateInMonth(anchor.Day, monthStart);
        }
        return candidate;
    }

    private static DateOnly NextYearly(DateOnly anchor, DateOnly onOrAfter)
    {
        var monthStart = new DateOnly(onOrAfter.Year, anchor.Month, 1);
        var candidate = FixedExpenseScheduling.DateInMonth(anchor.Day, monthStart);
        if (candidate < onOrAfter)
        {
            monthStart = new DateOnly(onOrAfter.Year + 1, anchor.Month, 1);
            candidate = FixedExpenseScheduling.DateInMonth(anchor.Day, monthStart);
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

        var end = qualifying.Min(s => NextPayDate(s, today.AddDays(1))).AddDays(-1);
        return (today, end);
    }

    /// <summary>The period following one that ended on `prevEnd`. Falls back to calendar-month math when no income source has a qualifying schedule.</summary>
    public static (DateOnly Start, DateOnly End) ComputeNext(IReadOnlyList<IncomeSource> sources, string fallbackCycle, DateOnly prevEnd)
    {
        var start0 = prevEnd.AddDays(1);
        var qualifying = Qualifying(sources, start0);
        if (qualifying.Count == 0) return BudgetPeriodDates.ComputeNext(fallbackCycle, prevEnd);

        var start = qualifying.Min(s => NextPayDate(s, start0));
        var nextStart = qualifying.Min(s => NextPayDate(s, start.AddDays(1)));
        return (start, nextStart.AddDays(-1));
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
