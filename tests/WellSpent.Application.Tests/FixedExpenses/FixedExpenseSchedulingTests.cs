using WellSpent.Application.FixedExpenses;
using WellSpent.Domain.Entities;
using Xunit;

namespace WellSpent.Application.Tests.FixedExpenses;

public sealed class FixedExpenseSchedulingTests
{
    private static FixedExpense Monthly(DateTime createdAt, int intervalMonths, int dayOfMonth = 1, DateOnly? anchorDate = null, int? totalPayments = null) =>
        new()
        {
            Name = "fe", CreatedAt = createdAt, IntervalMonths = intervalMonths, DayOfMonth = dayOfMonth,
            AnchorDate = anchorDate, FrequencyUnit = 1, TotalPayments = totalPayments,
        };

    private static FixedExpense Weekly(DateTime createdAt, int intervalWeeks, int dayOfWeek, DateOnly? anchorDate = null, int? totalPayments = null) =>
        new()
        {
            Name = "fe", CreatedAt = createdAt, IntervalWeeks = intervalWeeks, DayOfWeek = (short)dayOfWeek,
            AnchorDate = anchorDate, FrequencyUnit = 2, TotalPayments = totalPayments,
        };

    // ── IsDueInMonth ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, 2026, 1, true)]   // monthly, same month as anchor
    [InlineData(1, 2026, 6, true)]   // monthly, every month is due
    [InlineData(0, 2026, 3, true)]   // unset interval treated as monthly
    [InlineData(3, 2026, 1, true)]   // quarterly, anchor month is due
    [InlineData(3, 2026, 2, false)]  // quarterly, one month after anchor is not due
    [InlineData(3, 2026, 3, false)]  // quarterly, two months after anchor is not due
    [InlineData(3, 2026, 4, true)]   // quarterly, three months after anchor is due
    [InlineData(3, 2026, 7, true)]   // quarterly, six months after anchor is due
    [InlineData(12, 2027, 1, true)]  // yearly, same month next year is due
    [InlineData(12, 2026, 7, false)] // yearly, six months later is not due
    [InlineData(1, 2025, 12, false)] // month before anchor is never due
    public void IsDueInMonth_Monthly(int intervalMonths, int year, int month, bool want)
    {
        var fe = Monthly(new DateTime(2026, 1, 15), intervalMonths);
        Assert.Equal(want, FixedExpenseScheduling.IsDueInMonth(fe, new DateOnly(year, month, 1)));
    }

    [Fact]
    public void IsDueInMonth_AnchorDateOverridesCreatedAt()
    {
        var fe = Monthly(new DateTime(2020, 1, 1), 1, anchorDate: new DateOnly(2027, 3, 10));

        Assert.False(FixedExpenseScheduling.IsDueInMonth(fe, new DateOnly(2026, 7, 1)));
        Assert.True(FixedExpenseScheduling.IsDueInMonth(fe, new DateOnly(2027, 3, 1)));
        Assert.True(FixedExpenseScheduling.IsDueInMonth(fe, new DateOnly(2027, 4, 1)));
    }

    // ── NextDueDate (monthly) ─────────────────────────────────────────────

    [Fact]
    public void NextDueDate_Monthly_FindsNextDueMonthAhead()
    {
        var fe = Monthly(new DateTime(2026, 1, 15), 3, dayOfMonth: 15);

        Assert.Equal(new DateOnly(2026, 4, 15), FixedExpenseScheduling.NextDueDate(fe, new DateOnly(2026, 2, 1)));
        Assert.Equal(new DateOnly(2026, 7, 15), FixedExpenseScheduling.NextDueDate(fe, new DateOnly(2026, 7, 1)));
    }

    [Fact]
    public void NextDueDate_ClampsToLastDayOfMonth()
    {
        var fe = Monthly(new DateTime(2026, 1, 31), 1, dayOfMonth: 31);
        Assert.Equal(new DateOnly(2026, 2, 28), FixedExpenseScheduling.NextDueDate(fe, new DateOnly(2026, 2, 1)));
    }

    [Fact]
    public void NextDueDate_WithFutureAnchorDate()
    {
        var futureAnchor = new DateOnly(2027, 3, 10);
        var fe = Monthly(new DateTime(2026, 7, 10), 1, dayOfMonth: 10, anchorDate: futureAnchor);
        Assert.Equal(futureAnchor, FixedExpenseScheduling.NextDueDate(fe, new DateOnly(2026, 7, 1)));
    }

    // ── IsDueInWeek ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, 2026, 1, 5, true)]    // weekly, anchor week is due
    [InlineData(1, 2026, 2, 2, true)]    // weekly, every week is due
    [InlineData(0, 2026, 1, 26, true)]   // unset interval treated as weekly
    [InlineData(2, 2026, 1, 5, true)]    // bi-weekly, anchor week is due
    [InlineData(2, 2026, 1, 12, false)]  // bi-weekly, one week after anchor is not due
    [InlineData(2, 2026, 1, 19, true)]   // bi-weekly, two weeks after anchor is due
    [InlineData(1, 2025, 12, 29, false)] // week before anchor is never due
    public void IsDueInWeek(int intervalWeeks, int year, int month, int day, bool want)
    {
        var fe = Weekly(new DateTime(2026, 1, 5), intervalWeeks, dayOfWeek: 1); // anchor is a Monday
        Assert.Equal(want, FixedExpenseScheduling.IsDueInWeek(fe, new DateOnly(year, month, day)));
    }

    [Fact]
    public void WeekStart_ReturnsMonday()
    {
        var ws = FixedExpenseScheduling.WeekStart(new DateOnly(2026, 1, 7)); // Wednesday
        Assert.Equal(new DateOnly(2026, 1, 5), ws);

        // A Sunday rolls back to the Monday that started its own week, not forward into the next one.
        ws = FixedExpenseScheduling.WeekStart(new DateOnly(2026, 1, 11));
        Assert.Equal(new DateOnly(2026, 1, 5), ws);
    }

    [Fact]
    public void NextDueDate_WeeklyUnit()
    {
        var fe = Weekly(new DateTime(2026, 1, 5), intervalWeeks: 2, dayOfWeek: 3); // Monday anchor, due Wednesdays

        Assert.Equal(new DateOnly(2026, 1, 7), FixedExpenseScheduling.NextDueDate(fe, new DateOnly(2026, 1, 5)));
        // One week after anchor (not due, bi-weekly) finds the next due week ahead.
        Assert.Equal(new DateOnly(2026, 1, 21), FixedExpenseScheduling.NextDueDate(fe, new DateOnly(2026, 1, 12)));
    }

    [Fact]
    public void NextDueDate_WeeklyUnit_WithFutureAnchorDate()
    {
        var futureAnchor = new DateOnly(2027, 3, 8); // a Monday
        var fe = Weekly(new DateTime(2026, 7, 10), intervalWeeks: 1, dayOfWeek: 1, anchorDate: futureAnchor);
        Assert.Equal(futureAnchor, FixedExpenseScheduling.NextDueDate(fe, new DateOnly(2026, 7, 1)));
    }

    // ── PaymentsMade (monthly) ────────────────────────────────────────────

    [Theory]
    [InlineData(2025, 12, 20, 0)]  // before the anchor month
    [InlineData(2026, 1, 3, 0)]    // anchor month, before the due day
    [InlineData(2026, 1, 15, 1)]   // anchor month, on the due day
    [InlineData(2026, 1, 20, 1)]   // anchor month, after the due day
    [InlineData(2026, 2, 3, 1)]    // next month, before the due day
    [InlineData(2026, 2, 20, 2)]   // next month, after the due day
    [InlineData(2030, 1, 1, 12)]   // clamped at total_payments
    public void PaymentsMade_Monthly(int year, int month, int day, int want)
    {
        var fe = Monthly(new DateTime(2026, 1, 15), 1, dayOfMonth: 15, totalPayments: 12);
        Assert.Equal(want, FixedExpenseScheduling.PaymentsMade(fe, new DateOnly(year, month, day)));
    }

    [Fact]
    public void PaymentsMade_NoPlanReturnsZero()
    {
        var fe = Monthly(new DateTime(2020, 1, 15), 1, dayOfMonth: 15); // no TotalPayments
        Assert.Equal(0, FixedExpenseScheduling.PaymentsMade(fe, new DateOnly(2026, 3, 1)));
    }

    [Fact]
    public void PaymentsMade_FallsBackToCreatedAtWhenNoAnchorDate()
    {
        var fe = Monthly(new DateTime(2026, 1, 10), 1, dayOfMonth: 10, totalPayments: 6);
        Assert.Null(fe.AnchorDate);
        Assert.Equal(3, FixedExpenseScheduling.PaymentsMade(fe, new DateOnly(2026, 3, 15)));
    }

    [Fact]
    public void PaymentsMade_QuarterlyCountsDueMonthsNotElapsedMonths()
    {
        var fe = Monthly(new DateTime(2026, 1, 15), 3, dayOfMonth: 15, totalPayments: 4);
        // Jan and Apr are due; Feb, Mar, May are not.
        Assert.Equal(1, FixedExpenseScheduling.PaymentsMade(fe, new DateOnly(2026, 3, 31)));
        Assert.Equal(2, FixedExpenseScheduling.PaymentsMade(fe, new DateOnly(2026, 4, 15)));
        Assert.Equal(2, FixedExpenseScheduling.PaymentsMade(fe, new DateOnly(2026, 5, 31)));
    }

    [Fact]
    public void PaymentsMade_WeeklyUnit()
    {
        // Monday 2026-01-05; due every 2 weeks on Wednesday (ISO day 3).
        var fe = Weekly(new DateTime(2026, 1, 5), intervalWeeks: 2, dayOfWeek: 3, totalPayments: 8);

        Assert.Equal(0, FixedExpenseScheduling.PaymentsMade(fe, new DateOnly(2026, 1, 6))); // Tuesday of anchor week
        Assert.Equal(1, FixedExpenseScheduling.PaymentsMade(fe, new DateOnly(2026, 1, 7)));
        Assert.Equal(1, FixedExpenseScheduling.PaymentsMade(fe, new DateOnly(2026, 1, 14))); // skipped week is not due
        Assert.Equal(2, FixedExpenseScheduling.PaymentsMade(fe, new DateOnly(2026, 1, 21)));
    }

    // ── ScheduleFromAnchor ────────────────────────────────────────────────

    [Fact]
    public void ScheduleFromAnchor_DerivesDayOfMonthAndIsoWeekday()
    {
        var (dayOfMonth, dayOfWeek, anchorDate) = FixedExpenseScheduling.ScheduleFromAnchor(new DateOnly(2026, 3, 11)); // a Wednesday
        Assert.Equal(11, dayOfMonth);
        Assert.Equal(3, dayOfWeek);
        Assert.Equal(new DateOnly(2026, 3, 11), anchorDate);
    }

    [Fact]
    public void IsoWeekday_SundayIsSeven()
    {
        Assert.Equal(7, FixedExpenseScheduling.IsoWeekday(new DateOnly(2026, 1, 11))); // a Sunday
        Assert.Equal(1, FixedExpenseScheduling.IsoWeekday(new DateOnly(2026, 1, 5)));  // a Monday
    }
}
