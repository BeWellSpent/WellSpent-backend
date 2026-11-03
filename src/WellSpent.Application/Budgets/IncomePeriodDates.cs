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

        var end = qualifying.Min(s => NextPayDate(s, today.AddDays(1))).AddDays(-1);
        return (today, end);
    }

    /// <summary>
    /// The period following one that started on `prevStart` and ended on `prevEnd`. Falls back
    /// to calendar-month math when no income source has a qualifying schedule.
    /// </summary>
    /// <remarks>
    /// When more than one qualifying source has a payday due, the period doesn't simply start
    /// at whichever is soonest — a job paid out more than once per cycle (e.g. a monthly salary
    /// split across the 15th and the last day) must still produce periods whose length tracks
    /// the profile's own declared <c>cycle</c>, not the shorter gap between its own paydays. The
    /// candidate closest to exactly one cycle after `prevStart` wins; the others still land as
    /// ordinary income arrivals inside whichever period they fall into (see
    /// <see cref="ResolveFundedDate"/>/<see cref="GetPeriodForDate"/>), they just don't start a
    /// new one themselves. A genuinely independent source (a different person's own schedule)
    /// still competes on equal footing — this only stops *multiple paydays landing close
    /// together* from fragmenting one cycle into several.
    /// </remarks>
    public static (DateOnly Start, DateOnly End) ComputeNext(IReadOnlyList<IncomeSource> sources, string cycle, DateOnly prevStart, DateOnly prevEnd)
    {
        var start0 = prevEnd.AddDays(1);
        var qualifying = Qualifying(sources, start0);
        if (qualifying.Count == 0) return BudgetPeriodDates.ComputeNext(cycle, prevEnd);

        // Paydays are compared on their real dates; the anchoring source's
        // EarlyArrivalToleranceDays then pulls the boundary earlier, so a deposit arriving
        // within that window already sits in the period it funds (same rule ResolveFundedDate
        // applies per transaction). Applied to both ends so periods stay contiguous.
        var anchor = Closest(qualifying, start0, BudgetPeriodDates.AddCycle(prevStart, cycle));
        var start = Max(start0, Offset(anchor));
        var next = Closest(qualifying, anchor.Date.AddDays(1), BudgetPeriodDates.AddCycle(anchor.Date, cycle));
        return (start, Offset(next).AddDays(-1));
    }

    private static (DateOnly Date, IncomeSource Source) Closest(List<IncomeSource> qualifying, DateOnly onOrAfter, DateOnly ideal) =>
        qualifying
            .Select(s => (Date: NextPayDate(s, onOrAfter), Source: s))
            .OrderBy(x => Math.Abs(x.Date.DayNumber - ideal.DayNumber))
            .ThenBy(x => x.Date.DayNumber)
            .First();

    private static DateOnly Offset((DateOnly Date, IncomeSource Source) payday) =>
        payday.Date.AddDays(-payday.Source.EarlyArrivalToleranceDays);

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

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
