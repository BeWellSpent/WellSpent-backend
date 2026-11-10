using WellSpent.Application.Reports;
using Xunit;

namespace WellSpent.Application.Tests.Reports;

public sealed class ScorecardRulesTests
{
    private const int Food = 1, Fun = 2, Savings = 9;

    [Fact]
    public void Evaluate_UnplannedSpendingCounts_ButCantFailACategory()
    {
        var score = ScorecardRules.Evaluate(
            new Dictionary<int, decimal> { [Food] = 500m },
            new Dictionary<int, decimal> { [Food] = 400m, [Fun] = 200m },
            Savings);

        Assert.Equal((500m, 600m), (score.PlannedSpending, score.ActualSpending));
        Assert.False(score.OnPlan); // within on food, but the total went over
        Assert.Equal((1, 1), (score.CategoriesWithin, score.CategoriesPlanned));
    }

    [Fact]
    public void Evaluate_SavingsIsScoredSeparately()
    {
        var score = ScorecardRules.Evaluate(
            new Dictionary<int, decimal> { [Food] = 500m, [Savings] = 300m },
            new Dictionary<int, decimal> { [Food] = 500m, [Savings] = 100m },
            Savings);

        Assert.True(score.OnPlan); // spending exactly on plan counts
        Assert.False(score.SavingsMet);
        Assert.Equal((300m, 100m), (score.SavingsPlanned, score.SavingsActual));
    }

    [Fact]
    public void Evaluate_NothingPlanned_IsNotScored()
    {
        var score = ScorecardRules.Evaluate(new Dictionary<int, decimal>(), new Dictionary<int, decimal> { [Food] = 50m }, Savings);

        Assert.Null(score.OnPlan);
        Assert.Null(score.SavingsMet);
    }

    [Fact]
    public void Streak_CountsBackFromTheLatest_SkippingUnscored()
    {
        Assert.Equal(2, ScorecardRules.Streak([true, false, true, null, true, null]));
        Assert.Equal(0, ScorecardRules.Streak([true, false]));
        Assert.Equal(0, ScorecardRules.Streak([]));
    }
}
