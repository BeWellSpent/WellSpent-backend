namespace WellSpent.Application.Reports;

/// <summary>How a period is judged against its plan (docs/features/plan-scorecard.md).</summary>
public static class ScorecardRules
{
    public sealed record Score(
        decimal PlannedSpending, decimal ActualSpending, bool? OnPlan, int CategoriesWithin, int CategoriesPlanned,
        decimal SavingsPlanned, decimal SavingsActual, bool? SavingsMet);

    /// <summary>
    /// Spending excludes savings, which is scored on its own. A category with no
    /// plan can't fail but its spending still counts toward the total; a period
    /// with nothing planned isn't scored.
    /// </summary>
    public static Score Evaluate(IReadOnlyDictionary<int, decimal> planned, IReadOnlyDictionary<int, decimal> actual, int? savingsCategoryId)
    {
        bool IsSavings(int id) => id == savingsCategoryId;
        var plannedSpending = planned.Where(p => !IsSavings(p.Key)).Sum(p => p.Value);
        var actualSpending = actual.Where(a => !IsSavings(a.Key)).Sum(a => a.Value);
        var plannedCategories = planned.Where(p => !IsSavings(p.Key) && p.Value > 0).ToList();
        var within = plannedCategories.Count(p => actual.GetValueOrDefault(p.Key) <= p.Value);
        var savingsPlanned = savingsCategoryId is { } s ? planned.GetValueOrDefault(s) : 0m;
        var savingsActual = savingsCategoryId is { } s2 ? actual.GetValueOrDefault(s2) : 0m;

        return new Score(
            plannedSpending, actualSpending,
            plannedSpending > 0 ? actualSpending <= plannedSpending : null,
            within, plannedCategories.Count,
            savingsPlanned, savingsActual,
            savingsPlanned > 0 ? savingsActual >= savingsPlanned : null);
    }

    /// <summary>Consecutive on-plan periods counting back from the most recent scored one; unscored periods are skipped, not breaks.</summary>
    public static int Streak(IEnumerable<bool?> onPlanOldestFirst)
    {
        var streak = 0;
        foreach (var onPlan in onPlanOldestFirst.Reverse())
        {
            if (onPlan is null) continue;
            if (onPlan == false) break;
            streak++;
        }
        return streak;
    }
}
