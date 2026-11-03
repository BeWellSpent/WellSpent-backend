namespace WellSpent.Application.Budgets;

/// <summary>Pure port of Go's computeFirstPeriodDates/computeNextPeriodDates (budget_profile_service.go).</summary>
public static class BudgetPeriodDates
{
    public static (DateOnly Start, DateOnly End) ComputeFirst(string cycle, DateOnly today) => cycle switch
    {
        "weekly" => (today, today.AddDays(6)),
        "bi_weekly" => (today, today.AddDays(13)),
        "yearly" => (new DateOnly(today.Year, 1, 1), new DateOnly(today.Year, 12, 31)),
        _ => (new DateOnly(today.Year, today.Month, 1), new DateOnly(today.Year, today.Month, 1).AddMonths(1).AddDays(-1)),
    };

    public static (DateOnly Start, DateOnly End) ComputeNext(string cycle, DateOnly prevEnd)
    {
        var start = prevEnd.AddDays(1);
        return cycle switch
        {
            "weekly" => (start, start.AddDays(6)),
            "bi_weekly" => (start, start.AddDays(13)),
            "yearly" => (start, new DateOnly(start.Year, 12, 31)),
            _ => (start, new DateOnly(start.Year, start.Month, 1).AddMonths(1).AddDays(-1)),
        };
    }

    /// <summary>
    /// `date` advanced by exactly one cycle. Used by <see cref="IncomePeriodDates.ComputeNext"/>
    /// as the target a real payday is judged against — not to compute a period's boundaries
    /// directly, since those come from actual pay dates, not the calendar.
    /// </summary>
    public static DateOnly AddCycle(DateOnly date, string cycle) => cycle switch
    {
        "weekly" => date.AddDays(7),
        "bi_weekly" => date.AddDays(14),
        "yearly" => date.AddYears(1),
        _ => date.AddMonths(1),
    };
}
