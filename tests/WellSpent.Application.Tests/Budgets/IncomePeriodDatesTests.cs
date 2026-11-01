using WellSpent.Application.Budgets;
using WellSpent.Domain.Entities;
using Xunit;

namespace WellSpent.Application.Tests.Budgets;

public sealed class IncomePeriodDatesTests
{
    private static IncomeSource Source(
        string frequency, DateOnly? anchor, int toleranceDays = 0,
        DateOnly? effectiveFrom = null, DateOnly? effectiveTo = null) =>
        new()
        {
            Name = "income", PaymentFrequency = frequency, AnchorDate = anchor,
            EarlyArrivalToleranceDays = toleranceDays, EffectiveFrom = effectiveFrom, EffectiveTo = effectiveTo,
        };

    // ── Worked example #1 (spec's own example) ─────────────────────────────

    [Fact]
    public void ComputeNext_MonthlyAnchorOct31_ProducesOct31ToNov29ThenNov30ToDec30()
    {
        var sources = new[] { Source("monthly", new DateOnly(2026, 10, 31)) };

        var first = IncomePeriodDates.ComputeNext(sources, "monthly", new DateOnly(2026, 10, 30));
        Assert.Equal((new DateOnly(2026, 10, 31), new DateOnly(2026, 11, 29)), first);

        var second = IncomePeriodDates.ComputeNext(sources, "monthly", first.End);
        Assert.Equal((new DateOnly(2026, 11, 30), new DateOnly(2026, 12, 30)), second);
    }

    // ── Worked example #2 (user's real scenario: bi-weekly + monthly interleaved) ──

    [Fact]
    public void ComputeNext_BiWeeklyAndMonthlySources_Interleave()
    {
        var sources = new[]
        {
            Source("bi_weekly", new DateOnly(2026, 10, 31)),
            Source("monthly", new DateOnly(2026, 10, 28)),
        };

        var p1 = IncomePeriodDates.ComputeNext(sources, "monthly", new DateOnly(2026, 10, 30));
        Assert.Equal((new DateOnly(2026, 10, 31), new DateOnly(2026, 11, 13)), p1);

        var p2 = IncomePeriodDates.ComputeNext(sources, "monthly", p1.End);
        Assert.Equal((new DateOnly(2026, 11, 14), new DateOnly(2026, 11, 27)), p2);

        var p3 = IncomePeriodDates.ComputeNext(sources, "monthly", p2.End);
        Assert.Equal((new DateOnly(2026, 11, 28), new DateOnly(2026, 12, 11)), p3);

        var p4 = IncomePeriodDates.ComputeNext(sources, "monthly", p3.End);
        Assert.Equal((new DateOnly(2026, 12, 12), new DateOnly(2026, 12, 25)), p4);

        // After Nov 28, the bi-weekly (every 14 days from Oct 31) and monthly (day 28) schedules
        // diverge: A's dates are Dec 12, Dec 26, Jan 9...; B's are Dec 28, Jan 28... So Dec 26 (A)
        // is followed almost immediately by Dec 28 (B) — a short two-day period — before B's own
        // monthly cadence resumes governing into January.
        var p5 = IncomePeriodDates.ComputeNext(sources, "monthly", p4.End);
        Assert.Equal((new DateOnly(2026, 12, 26), new DateOnly(2026, 12, 27)), p5);

        var p6 = IncomePeriodDates.ComputeNext(sources, "monthly", p5.End);
        Assert.Equal((new DateOnly(2026, 12, 28), new DateOnly(2027, 1, 8)), p6);
    }

    // ── NextPayDate: month-length / leap-year clamping (monthly) ───────────

    [Theory]
    [InlineData(2026, 1, 28, 2026, 2, 28)] // 28-day month, exact day exists
    [InlineData(2024, 1, 29, 2024, 2, 29)] // leap year: Feb has 29
    [InlineData(2026, 1, 29, 2026, 2, 28)] // non-leap year: clamp 29 -> 28
    [InlineData(2026, 1, 30, 2026, 2, 28)] // clamp 30 -> 28 in Feb
    [InlineData(2026, 1, 31, 2026, 4, 30)] // clamp 31 -> 30 in April
    public void NextPayDate_Monthly_ClampsToMonthLength(int anchorYear, int anchorMonth, int anchorDay, int wantYear, int wantMonth, int wantDay)
    {
        var anchor = new DateOnly(anchorYear, anchorMonth, anchorDay);
        var source = Source("monthly", anchor);

        // Ask for the pay date strictly after the anchor month, landing on wantYear/wantMonth.
        var onOrAfter = new DateOnly(wantYear, wantMonth, 1);
        var result = IncomePeriodDates.NextPayDate(source, onOrAfter);

        Assert.Equal(new DateOnly(wantYear, wantMonth, wantDay), result);
    }

    // ── NextPayDate: yearly, crossing Feb 29 ────────────────────────────────

    [Fact]
    public void NextPayDate_Yearly_LeapAnchorClampsInNonLeapYear()
    {
        var source = Source("yearly", new DateOnly(2024, 2, 29)); // leap-year anchor

        // Asking from the start of a non-leap year clamps Feb 29 -> Feb 28.
        Assert.Equal(new DateOnly(2025, 2, 28), IncomePeriodDates.NextPayDate(source, new DateOnly(2025, 1, 1)));

        // Asking from the start of the next leap year keeps the real Feb 29.
        Assert.Equal(new DateOnly(2028, 2, 29), IncomePeriodDates.NextPayDate(source, new DateOnly(2028, 1, 1)));
    }

    [Fact]
    public void NextPayDate_Yearly_OnOrAfterAnchor_ReturnsAnchorUnchanged()
    {
        var source = Source("yearly", new DateOnly(2026, 6, 15));
        Assert.Equal(new DateOnly(2026, 6, 15), IncomePeriodDates.NextPayDate(source, new DateOnly(2026, 1, 1)));
    }

    // ── Fallback: no qualifying source -> BudgetPeriodDates unchanged ──────

    [Fact]
    public void ComputeFirst_NoRegularSources_FallsBackToCalendarMonth()
    {
        var today = new DateOnly(2026, 6, 10);
        var sources = new[] { Source("one_off", new DateOnly(2026, 6, 1)), Source("monthly", null) };

        var result = IncomePeriodDates.ComputeFirst(sources, "monthly", today);
        var expected = BudgetPeriodDates.ComputeFirst("monthly", today);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ComputeFirst_EmptySourceList_FallsBackToCalendarMonth()
    {
        var today = new DateOnly(2026, 6, 10);
        Assert.Equal(BudgetPeriodDates.ComputeFirst("yearly", today), IncomePeriodDates.ComputeFirst([], "yearly", today));
    }

    [Fact]
    public void ComputeNext_NoRegularSources_FallsBackToCalendarMonth()
    {
        var prevEnd = new DateOnly(2026, 6, 30);
        var sources = new[] { Source("one_off", new DateOnly(2026, 6, 1)) };

        var result = IncomePeriodDates.ComputeNext(sources, "weekly", prevEnd);
        var expected = BudgetPeriodDates.ComputeNext("weekly", prevEnd);

        Assert.Equal(expected, result);
    }

    // ── Mixed household: one regular, one irregular -> only the regular one governs ──

    [Fact]
    public void ComputeNext_IrregularMemberAlongsideRegular_DoesNotAffectBoundary()
    {
        var regular = Source("monthly", new DateOnly(2026, 10, 31));
        var irregularOneOff = Source("one_off", new DateOnly(2026, 11, 5));
        var irregularNoAnchor = Source("monthly", null);

        var withOnlyRegular = IncomePeriodDates.ComputeNext([regular], "monthly", new DateOnly(2026, 10, 30));
        var withIrregularsToo = IncomePeriodDates.ComputeNext([regular, irregularOneOff, irregularNoAnchor], "monthly", new DateOnly(2026, 10, 30));

        Assert.Equal(withOnlyRegular, withIrregularsToo);
    }

    // ── EffectiveFrom/EffectiveTo: schedule edit only affects periods on/after it takes effect ──

    [Fact]
    public void ComputeNext_EffectiveFromInFuture_DoesNotGovernEarlierStart_ButDoesGovernLaterStart()
    {
        var prevEnd = new DateOnly(2026, 10, 30); // start0 = Oct 31
        var newSchedule = Source("monthly", new DateOnly(2026, 10, 28), effectiveFrom: new DateOnly(2026, 11, 1));

        // start0 (Oct 31) is before EffectiveFrom (Nov 1) -> source doesn't qualify -> falls back to calendar.
        var beforeEffective = IncomePeriodDates.ComputeNext([newSchedule], "monthly", prevEnd);
        Assert.Equal(BudgetPeriodDates.ComputeNext("monthly", prevEnd), beforeEffective);

        // A later prevEnd whose start0 is on/after EffectiveFrom -> source governs: the next
        // period's boundaries follow the schedule's own paydays (Nov 28, then Dec 28) rather
        // than the calendar-month fallback ((Nov 11, Nov 30) that applied above).
        var laterPrevEnd = new DateOnly(2026, 11, 10); // start0 = Nov 11, >= EffectiveFrom
        var afterEffective = IncomePeriodDates.ComputeNext([newSchedule], "monthly", laterPrevEnd);
        Assert.Equal((new DateOnly(2026, 11, 28), new DateOnly(2026, 12, 27)), afterEffective);
        Assert.NotEqual(BudgetPeriodDates.ComputeNext("monthly", laterPrevEnd), afterEffective);
    }

    [Fact]
    public void ComputeNext_EffectiveToInPast_SourceNoLongerGoverns()
    {
        var prevEnd = new DateOnly(2026, 10, 30); // start0 = Oct 31
        var oldSchedule = Source("monthly", new DateOnly(2026, 10, 31), effectiveTo: new DateOnly(2026, 10, 31));

        // EffectiveTo is exclusive: asOf (Oct 31) is not < EffectiveTo (Oct 31) -> no longer effective.
        var result = IncomePeriodDates.ComputeNext([oldSchedule], "monthly", prevEnd);
        Assert.Equal(BudgetPeriodDates.ComputeNext("monthly", prevEnd), result);
    }

    [Fact]
    public void IsEffectiveAt_BoundaryIsFromInclusiveToExclusive()
    {
        var source = Source("monthly", new DateOnly(2026, 1, 1), effectiveFrom: new DateOnly(2026, 6, 1), effectiveTo: new DateOnly(2026, 12, 1));

        Assert.False(IncomePeriodDates.IsEffectiveAt(source, new DateOnly(2026, 5, 31)));
        Assert.True(IncomePeriodDates.IsEffectiveAt(source, new DateOnly(2026, 6, 1)));
        Assert.True(IncomePeriodDates.IsEffectiveAt(source, new DateOnly(2026, 11, 30)));
        Assert.False(IncomePeriodDates.IsEffectiveAt(source, new DateOnly(2026, 12, 1)));
    }

    // ── GetPeriodForDate: last-day rollover boundary is exclusive-old/inclusive-new ──

    [Fact]
    public void GetPeriodForDate_DateOnNewPeriodStart_BelongsToNewPeriodNotOld()
    {
        var periods = new List<(DateOnly Start, DateOnly End)>
        {
            (new DateOnly(2026, 10, 31), new DateOnly(2026, 11, 29)),
            (new DateOnly(2026, 11, 30), new DateOnly(2026, 12, 30)),
        };

        var result = IncomePeriodDates.GetPeriodForDate(periods, new DateOnly(2026, 11, 30));

        Assert.Equal(periods[1], result);
    }

    [Fact]
    public void GetPeriodForDate_DateOneDayBeforeRollover_BelongsToOldPeriod()
    {
        var periods = new List<(DateOnly Start, DateOnly End)>
        {
            (new DateOnly(2026, 10, 31), new DateOnly(2026, 11, 29)),
            (new DateOnly(2026, 11, 30), new DateOnly(2026, 12, 30)),
        };

        var result = IncomePeriodDates.GetPeriodForDate(periods, new DateOnly(2026, 11, 29));

        Assert.Equal(periods[0], result);
    }

    [Fact]
    public void GetPeriodForDate_DateOutsideEveryPeriod_ReturnsNull()
    {
        var periods = new List<(DateOnly Start, DateOnly End)> { (new DateOnly(2026, 10, 31), new DateOnly(2026, 11, 29)) };

        Assert.Null(IncomePeriodDates.GetPeriodForDate(periods, new DateOnly(2026, 12, 1)));
    }

    // ── ResolveFundedDate: early-arrival tolerance ──────────────────────────

    [Fact]
    public void ResolveFundedDate_ExactlyAtToleranceBoundary_FundsScheduledDate()
    {
        var source = Source("monthly", new DateOnly(2026, 6, 15), toleranceDays: 3);

        var result = IncomePeriodDates.ResolveFundedDate(source, new DateOnly(2026, 6, 12)); // 3 days early

        Assert.Equal(new DateOnly(2026, 6, 15), result);
    }

    [Fact]
    public void ResolveFundedDate_OneDayBeyondTolerance_FundsDepositDateUnchanged()
    {
        var source = Source("monthly", new DateOnly(2026, 6, 15), toleranceDays: 3);

        var result = IncomePeriodDates.ResolveFundedDate(source, new DateOnly(2026, 6, 11)); // 4 days early

        Assert.Equal(new DateOnly(2026, 6, 11), result);
    }

    [Fact]
    public void ResolveFundedDate_DepositAfterScheduledDate_IsNormalNotEarlyArrival()
    {
        var source = Source("monthly", new DateOnly(2026, 6, 15), toleranceDays: 3);

        // Deposit arrives the day after the 15th already passed -> scheduled rolls to next month's
        // 15th, which is far beyond tolerance, so it funds on its own (late) deposit date.
        var result = IncomePeriodDates.ResolveFundedDate(source, new DateOnly(2026, 6, 16));

        Assert.Equal(new DateOnly(2026, 6, 16), result);
    }

    [Fact]
    public void ResolveFundedDate_IrregularSource_AlwaysFundsOnDepositDate()
    {
        var source = Source("one_off", new DateOnly(2026, 6, 15), toleranceDays: 10);

        var result = IncomePeriodDates.ResolveFundedDate(source, new DateOnly(2026, 6, 1));

        Assert.Equal(new DateOnly(2026, 6, 1), result);
    }

    // ── IsRegular ────────────────────────────────────────────────────────────

    [Fact]
    public void IsRegular_OneOffOrNoAnchor_IsFalse()
    {
        Assert.False(IncomePeriodDates.IsRegular(Source("one_off", new DateOnly(2026, 1, 1))));
        Assert.False(IncomePeriodDates.IsRegular(Source("monthly", null)));
        Assert.True(IncomePeriodDates.IsRegular(Source("monthly", new DateOnly(2026, 1, 1))));
    }
}
