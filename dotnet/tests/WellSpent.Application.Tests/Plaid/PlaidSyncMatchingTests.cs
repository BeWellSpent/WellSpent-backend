using WellSpent.Application.Plaid;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.Tests.Plaid;

/// <summary>Mirrors internal/service/plaid_sync_test.go's TestSyncScoreBestMatch_* suite.</summary>
public sealed class PlaidSyncMatchingTests
{
    private static FixedExpense Fe(Guid id, string name, decimal amount) =>
        new() { Id = id, BudgetProfileId = Guid.NewGuid(), Name = name, PlannedAmount = amount };

    [Fact]
    public void AliasHitPicksAmountCompatibleCandidate()
    {
        var fe15Id = Guid.NewGuid();
        var fe5Id = Guid.NewGuid();
        var fe15 = Fe(fe15Id, "Creator Support A", 15.00m);
        var fe5 = Fe(fe5Id, "Creator Support B", 5.33m);
        var aliases = new Dictionary<Guid, List<string>> { [fe15Id] = ["Patreon"], [fe5Id] = ["Patreon"] };

        var (score, bestFe, aliasHit, amountOk) = PlaidSyncMatching.ScoreBestMatch("Patreon", 5.33m, null, null, [fe15, fe5], aliases);

        Assert.NotNull(bestFe);
        Assert.Equal(fe5Id, bestFe.Id);
        Assert.True(aliasHit);
        Assert.True(amountOk);
        Assert.Equal(60.0, score);
    }

    [Fact]
    public void AliasHitWithoutAmountMatchDoesNotReachAutoConfirmThreshold()
    {
        var feId = Guid.NewGuid();
        var fe = Fe(feId, "Creator Support", 15.00m);
        var aliases = new Dictionary<Guid, List<string>> { [feId] = ["Patreon"] };

        var (score, bestFe, aliasHit, amountOk) = PlaidSyncMatching.ScoreBestMatch("Patreon", 5.33m, null, null, [fe], aliases);

        Assert.NotNull(bestFe);
        Assert.True(aliasHit);
        Assert.False(amountOk);
        Assert.True(score < 80.0);
    }

    [Fact]
    public void FallsBackToWordOverlapWithoutAlias()
    {
        var feId = Guid.NewGuid();
        var fe = Fe(feId, "Amex Renewal Membership", 150.00m);

        var (score, bestFe, aliasHit, amountOk) = PlaidSyncMatching.ScoreBestMatch("RENEWAL MEMBERSHIP FEE", 150.00m, null, null, [fe], new());

        Assert.NotNull(bestFe);
        Assert.False(aliasHit);
        Assert.True(amountOk);
        Assert.Equal(60.0, score);
    }

    [Fact]
    public void AliasHitSurvivesADifferentTrailingDate()
    {
        var feId = Guid.NewGuid();
        var fe = Fe(feId, "Emma - broker", 120.00m);
        var aliases = new Dictionary<Guid, List<string>> { [feId] = ["Manual DB-Bkrg 09/02"] };

        var (score, bestFe, aliasHit, amountOk) = PlaidSyncMatching.ScoreBestMatch("Manual DB-Bkrg 10/02", 120.00m, null, null, [fe], aliases);

        Assert.NotNull(bestFe);
        Assert.Equal(feId, bestFe.Id);
        Assert.True(aliasHit, "the alias should still hit via word overlap once the date is stripped");
        Assert.True(amountOk);
        Assert.Equal(60.0, score);
    }

    [Fact]
    public void AliasDoesNotMatchWhenNoWordsAreShared()
    {
        var feId = Guid.NewGuid();
        var fe = Fe(feId, "Emma - broker", 120.00m);
        var aliases = new Dictionary<Guid, List<string>> { [feId] = ["Manual DB-Bkrg 09/02"] };

        var (_, _, aliasHit, _) = PlaidSyncMatching.ScoreBestMatch("Zelle Payment To John 10/02", 120.00m, null, null, [fe], aliases);

        Assert.False(aliasHit);
    }

    [Fact]
    public void NoCandidatesReturnsNull()
    {
        var (score, bestFe, aliasHit, amountOk) = PlaidSyncMatching.ScoreBestMatch("Patreon", 5.33m, null, null, [], new());

        Assert.Null(bestFe);
        Assert.Equal(0.0, score);
        Assert.False(aliasHit);
        Assert.False(amountOk);
    }

    [Fact]
    public void ExactShortNameMatchesEvenWithoutFourCharWords()
    {
        var feId = Guid.NewGuid();
        var pmId = Guid.NewGuid();
        var catId = 3;
        var fe = Fe(feId, "F1", 15.00m);
        fe.PaymentMethodId = pmId;
        fe.CategoryId = catId;

        var (score, bestFe, _, amountOk) = PlaidSyncMatching.ScoreBestMatch("F1", 15.00m, catId, pmId, [fe], new());

        Assert.NotNull(bestFe);
        Assert.True(amountOk);
        Assert.Equal(100.0, score);
    }

    [Fact]
    public void InstallmentPlanFixedExpenseIsNotSkipped_UnlikeTheManualPathScorer()
    {
        // The deliberate asymmetry this class exists to preserve: Go's
        // syncScoreBestMatch (what this mirrors) does NOT skip
        // IsInstallmentPlan fixed expenses, unlike scoreBestMatch (the
        // manual-match path, TransactionReviews.TransactionMatching in this
        // port). An installment plan must still be matchable here.
        var feId = Guid.NewGuid();
        var fe = Fe(feId, "Card Installment", 50.00m);
        fe.IsInstallmentPlan = true;

        var (score, bestFe, _, amountOk) = PlaidSyncMatching.ScoreBestMatch("Card Installment", 50.00m, null, null, [fe], new());

        Assert.NotNull(bestFe);
        Assert.Equal(feId, bestFe.Id);
        Assert.True(amountOk);
        Assert.Equal(60.0, score);
    }
}
