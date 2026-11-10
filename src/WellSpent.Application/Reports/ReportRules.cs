using WellSpent.Domain.Entities;

namespace WellSpent.Application.Reports;

/// <summary>Which periods a report covers and which of them the caller's plan unlocks.</summary>
public static class ReportRules
{
    /// <summary>Free sees the current period and the 3 before it.</summary>
    public const int FreeUnlockedPeriods = 4;
    public const int MaxPeriods = 12;

    public static bool IsPro(User user) => user.Plan is "pro" or "lifetime";

    /// <summary>The last <paramref name="count"/> periods that have started, oldest first; the last is the current one.</summary>
    public static List<BudgetPeriod> RecentStarted(IEnumerable<BudgetPeriod> periods, DateOnly today, int count) =>
        periods.Where(p => p.StartDate <= today)
            .OrderByDescending(p => p.StartDate)
            .Take(Math.Clamp(count, 1, MaxPeriods))
            .OrderBy(p => p.StartDate)
            .ToList();

    /// <summary><paramref name="indexFromEnd"/> 0 is the current period.</summary>
    public static bool IsLocked(bool isPro, int indexFromEnd) => !isPro && indexFromEnd >= FreeUnlockedPeriods;

    public static double? Percent(decimal part, decimal whole) =>
        whole > 0 ? Math.Round((double)(part / whole * 100), 1) : null;

}
