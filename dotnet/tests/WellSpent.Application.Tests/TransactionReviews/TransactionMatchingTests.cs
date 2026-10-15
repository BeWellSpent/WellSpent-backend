using WellSpent.Application.TransactionReviews;
using WellSpent.Domain.Entities;
using Xunit;

namespace WellSpent.Application.Tests.TransactionReviews;

public sealed class TransactionMatchingTests
{
    private static readonly Dictionary<Guid, List<string>> NoAliases = new();

    private static FixedExpense Fe(string name, decimal amount, int? categoryId = null, Guid? paymentMethodId = null, bool isInstallmentPlan = false) =>
        new() { Id = Guid.NewGuid(), BudgetProfileId = Guid.NewGuid(), Name = name, PlannedAmount = amount, CategoryId = categoryId, PaymentMethodId = paymentMethodId, IsInstallmentPlan = isInstallmentPlan };

    [Fact]
    public void ScoreBestMatch_AmountAndNameMatch_Scores60()
    {
        var fe = Fe("Netflix", 15.49m);
        var (score, match) = TransactionMatching.ScoreBestMatch("Netflix", 15.49m, null, null, [fe], NoAliases);

        Assert.Equal(60, score);
        Assert.Same(fe, match);
    }

    [Fact]
    public void ScoreBestMatch_AllFourSignals_Scores100()
    {
        var catId = 5;
        var pmId = Guid.NewGuid();
        var fe = Fe("Netflix", 15.49m, catId, pmId);
        var (score, match) = TransactionMatching.ScoreBestMatch("Netflix", 15.49m, catId, pmId, [fe], NoAliases);

        Assert.Equal(100, score);
        Assert.NotNull(match);
    }

    [Fact]
    public void ScoreBestMatch_AmountWithinThreeDollarTolerance_StillCounts()
    {
        var fe = Fe("Netflix", 15.49m);
        var (score, _) = TransactionMatching.ScoreBestMatch("Netflix", 18.49m, null, null, [fe], NoAliases);
        Assert.Equal(60, score); // amount (40) + name (20)
    }

    [Fact]
    public void ScoreBestMatch_AmountJustOverTolerance_DoesNotCount()
    {
        var fe = Fe("Netflix", 15.49m);
        var (score, _) = TransactionMatching.ScoreBestMatch("Netflix", 18.50m, null, null, [fe], NoAliases);
        Assert.Equal(20, score); // name only
    }

    [Fact]
    public void ScoreBestMatch_ShortExactName_MatchesEvenWithNoFourLetterWords()
    {
        // "F1" has no words >= 4 chars, so word-overlap alone would score 0 — exact match must still hit.
        var fe = Fe("F1", 10.00m);
        var (score, _) = TransactionMatching.ScoreBestMatch("F1", 10.00m, null, null, [fe], NoAliases);
        Assert.Equal(60, score);
    }

    [Fact]
    public void ScoreBestMatch_WordOverlap_MatchesDespiteDifferentFullName()
    {
        var fe = Fe("Geico Auto Insurance", 120.00m);
        var (score, _) = TransactionMatching.ScoreBestMatch("GEICO *AUTO PMT 04/02", 120.00m, null, null, [fe], NoAliases);
        Assert.Equal(60, score); // amount + overlapping word "geico"/"auto"
    }

    [Fact]
    public void ScoreBestMatch_AliasExactMatch_CountsAsNameMatch()
    {
        var fe = Fe("Netflix", 15.49m);
        var aliases = new Dictionary<Guid, List<string>> { [fe.Id] = ["NETFLIX.COM 09/02"] };
        var (score, _) = TransactionMatching.ScoreBestMatch("netflix.com 09/02", 15.49m, null, null, [fe], aliases);
        Assert.Equal(60, score);
    }

    [Fact]
    public void ScoreBestMatch_InstallmentPlanTemplate_NeverMatches()
    {
        var fe = Fe("Sofa", 100.00m, isInstallmentPlan: true);
        var (score, match) = TransactionMatching.ScoreBestMatch("Sofa", 100.00m, null, null, [fe], NoAliases);
        Assert.Equal(0, score);
        Assert.Null(match);
    }

    [Fact]
    public void ScoreBestMatch_NoMatchAtAll_ReturnsZero()
    {
        var fe = Fe("Rent", 1500.00m);
        var (score, match) = TransactionMatching.ScoreBestMatch("Random Coffee Shop", 4.50m, null, null, [fe], NoAliases);
        Assert.Equal(0, score);
        Assert.Null(match);
    }

    [Fact]
    public void ScoreBestMatch_PicksHighestAmongMultiple()
    {
        var low = Fe("Netflix", 15.49m);
        var high = Fe("Netflix", 15.49m, categoryId: 7);
        var (score, match) = TransactionMatching.ScoreBestMatch("Netflix", 15.49m, 7, null, [low, high], NoAliases);
        Assert.Equal(80, score);
        Assert.Same(high, match);
    }
}
