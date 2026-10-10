using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Common;
using WellSpent.Application.TransactionReviews.ConfirmTransactionReview;
using WellSpent.Application.TransactionReviews.DismissTransactionReview;
using WellSpent.Application.TransactionReviews.ListTransactionReviews;
using WellSpent.Application.TransactionReviews.MarkTransactionForReview;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.TransactionReviews;

public sealed class TransactionReviewHandlerTests
{
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();
    private readonly ITransactionRepository _transactions = Substitute.For<ITransactionRepository>();
    private readonly IFixedExpenseRepository _fixedExpenses = Substitute.For<IFixedExpenseRepository>();
    private readonly ITransactionReviewRepository _reviews = Substitute.For<ITransactionReviewRepository>();
    private BudgetAccessGuard Access => new(_profiles);

    private (Guid AdminId, Guid ProfileId) SetUpProfile()
    {
        var profileId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = adminId, Name = "B" });
        return (adminId, profileId);
    }

    // ── ListTransactionReviews ────────────────────────────────────────────

    [Fact]
    public async Task List_NonMember_ThrowsForbidden_NeverNotFound()
    {
        var (_, profileId) = SetUpProfile();
        var strangerId = Guid.NewGuid();
        _profiles.GetPersonByUserIdAsync(profileId, strangerId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BudgetPerson>(new NotFoundException("budget_person", strangerId.ToString())));

        await Assert.ThrowsAsync<ForbiddenException>(() => new ListTransactionReviewsQueryHandler(Access, _reviews)
            .Handle(new ListTransactionReviewsQuery(strangerId, profileId), CancellationToken.None));
    }

    [Fact]
    public async Task List_Member_Succeeds()
    {
        var (adminId, profileId) = SetUpProfile();
        _reviews.ListAsync(profileId, Arg.Any<CancellationToken>()).Returns([
            new TransactionReviewListRow(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 80m, "pending", DateTime.UtcNow, "Netflix", 15.49m, "Netflix (fixed)", 1, 1),
        ]);

        var result = await new ListTransactionReviewsQueryHandler(Access, _reviews)
            .Handle(new ListTransactionReviewsQuery(adminId, profileId), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(80, result[0].MatchScore);
    }

    // ── MarkTransactionForReview ──────────────────────────────────────────

    [Fact]
    public async Task MarkForReview_Success()
    {
        var (adminId, profileId) = SetUpProfile();
        var periodId = Guid.NewGuid();
        var txId = Guid.NewGuid();
        var matchedId = Guid.NewGuid();
        _transactions.GetTransactionAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = txId, TransactionTypeId = 2, BudgetPeriodId = periodId });
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId });
        _transactions.GetTransactionAsync(matchedId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = matchedId, TransactionTypeId = 1, BudgetPeriodId = periodId });
        _reviews.UpsertAsync(periodId, txId, matchedId, 100.0m, Arg.Any<CancellationToken>())
            .Returns(new TransactionReview { Id = Guid.NewGuid(), BudgetPeriodId = periodId, TransactionId = txId, MatchedTransactionId = matchedId, MatchScore = 100.0m, Status = "pending" });

        var result = await new MarkTransactionForReviewCommandHandler(Access, _transactions, _profiles, _reviews)
            .Handle(new MarkTransactionForReviewCommand(adminId, txId, matchedId, profileId), CancellationToken.None);

        Assert.Equal(100, result.MatchScore);
        Assert.Equal("pending", result.Status);
    }

    [Fact]
    public async Task MarkForReview_Forbidden_WhenViewer()
    {
        var (_, profileId) = SetUpProfile();
        var viewerId = Guid.NewGuid();
        _profiles.GetPersonByUserIdAsync(profileId, viewerId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 1, BudgetProfileId = profileId, UserId = viewerId, Role = "viewer" });

        await Assert.ThrowsAsync<ForbiddenException>(() => new MarkTransactionForReviewCommandHandler(Access, _transactions, _profiles, _reviews)
            .Handle(new MarkTransactionForReviewCommand(viewerId, Guid.NewGuid(), Guid.NewGuid(), profileId), CancellationToken.None));
    }

    [Fact]
    public async Task MarkForReview_Invalid_WhenFixed()
    {
        var (adminId, profileId) = SetUpProfile();
        var txId = Guid.NewGuid();
        _transactions.GetTransactionAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = txId, TransactionTypeId = 1, BudgetPeriodId = Guid.NewGuid() });

        await Assert.ThrowsAsync<AppValidationException>(() => new MarkTransactionForReviewCommandHandler(Access, _transactions, _profiles, _reviews)
            .Handle(new MarkTransactionForReviewCommand(adminId, txId, Guid.NewGuid(), profileId), CancellationToken.None));
    }

    [Fact]
    public async Task MarkForReview_Forbidden_WhenMatchedTransactionOtherPeriod()
    {
        var (adminId, profileId) = SetUpProfile();
        var periodId = Guid.NewGuid();
        var otherPeriodId = Guid.NewGuid();
        var txId = Guid.NewGuid();
        var matchedId = Guid.NewGuid();
        _transactions.GetTransactionAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = txId, TransactionTypeId = 2, BudgetPeriodId = periodId });
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId });
        _transactions.GetTransactionAsync(matchedId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = matchedId, TransactionTypeId = 1, BudgetPeriodId = otherPeriodId });

        await Assert.ThrowsAsync<ForbiddenException>(() => new MarkTransactionForReviewCommandHandler(Access, _transactions, _profiles, _reviews)
            .Handle(new MarkTransactionForReviewCommand(adminId, txId, matchedId, profileId), CancellationToken.None));
    }

    // ── ConfirmTransactionReview ──────────────────────────────────────────

    [Fact]
    public async Task Confirm_FixedExpenseMatch_MarksPaidAndSavesAlias()
    {
        var (adminId, profileId) = SetUpProfile();
        var periodId = Guid.NewGuid();
        var reviewId = Guid.NewGuid();
        var importedTxId = Guid.NewGuid();
        var matchedTxId = Guid.NewGuid();
        var feId = Guid.NewGuid();

        _reviews.GetByIdAsync(reviewId, Arg.Any<CancellationToken>())
            .Returns(new TransactionReview { Id = reviewId, BudgetPeriodId = periodId, TransactionId = importedTxId, MatchedTransactionId = matchedTxId, Status = "pending" });
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId });
        _transactions.GetTransactionAsync(matchedTxId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = matchedTxId, IsPaid = false, BudgetPeriodId = periodId, FixedExpenseId = feId });
        _transactions.GetTransactionAsync(importedTxId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = importedTxId, Name = "NETFLIX.COM", Amount = 800.00m });
        _reviews.ListByMatchedTransactionIdAsync(matchedTxId, Arg.Any<CancellationToken>()).Returns([]);
        _transactions.MarkTransactionAsPaidAsync(matchedTxId, periodId, 800.00m, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = matchedTxId, FixedExpenseId = feId, IsPaid = true });
        _fixedExpenses.GetByIdAsync(feId, Arg.Any<CancellationToken>())
            .Returns(new FixedExpense { Id = feId, BudgetProfileId = profileId, Name = "Netflix", PlannedAmount = 15.49m });
        _transactions.SetTransactionExcludedAsync(importedTxId, periodId, true, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = importedTxId, IsExcluded = true });

        await new ConfirmTransactionReviewCommandHandler(Access, _reviews, _transactions, _fixedExpenses, _profiles, NullLogger<ConfirmTransactionReviewCommandHandler>.Instance)
            .Handle(new ConfirmTransactionReviewCommand(adminId, reviewId), CancellationToken.None);

        await _transactions.Received(1).MarkTransactionAsPaidAsync(matchedTxId, periodId, 800.00m, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        await _reviews.Received(1).CreateAliasAsync(feId, "NETFLIX.COM", Arg.Any<CancellationToken>());
        await _transactions.Received(1).SetTransactionExcludedAsync(importedTxId, periodId, true, Arg.Any<CancellationToken>());
        await _reviews.Received(1).UpdateStatusAsync(reviewId, "confirmed", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Confirm_AlreadyPaid_SkipsMarkAsPaid()
    {
        var (adminId, profileId) = SetUpProfile();
        var periodId = Guid.NewGuid();
        var reviewId = Guid.NewGuid();
        var matchedTxId = Guid.NewGuid();
        var importedTxId = Guid.NewGuid();

        _reviews.GetByIdAsync(reviewId, Arg.Any<CancellationToken>())
            .Returns(new TransactionReview { Id = reviewId, BudgetPeriodId = periodId, TransactionId = importedTxId, MatchedTransactionId = matchedTxId, Status = "pending" });
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId });
        _transactions.GetTransactionAsync(matchedTxId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = matchedTxId, IsPaid = true, BudgetPeriodId = periodId });
        _transactions.GetTransactionAsync(importedTxId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = importedTxId });
        _reviews.ListByMatchedTransactionIdAsync(matchedTxId, Arg.Any<CancellationToken>()).Returns([]);
        _transactions.SetTransactionExcludedAsync(importedTxId, periodId, true, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = importedTxId, IsExcluded = true });

        await new ConfirmTransactionReviewCommandHandler(Access, _reviews, _transactions, _fixedExpenses, _profiles, NullLogger<ConfirmTransactionReviewCommandHandler>.Instance)
            .Handle(new ConfirmTransactionReviewCommand(adminId, reviewId), CancellationToken.None);

        await _transactions.DidNotReceive().MarkTransactionAsPaidAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<decimal>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    // A split match's second confirm must sum in the first's amount too.
    [Fact]
    public async Task Confirm_SumsAlreadyConfirmedSiblings()
    {
        var (adminId, profileId) = SetUpProfile();
        var periodId = Guid.NewGuid();
        var reviewId = Guid.NewGuid();
        var matchedTxId = Guid.NewGuid();
        var thisTxId = Guid.NewGuid();
        var siblingReviewId = Guid.NewGuid();
        var siblingTxId = Guid.NewGuid();

        _reviews.GetByIdAsync(reviewId, Arg.Any<CancellationToken>())
            .Returns(new TransactionReview { Id = reviewId, BudgetPeriodId = periodId, TransactionId = thisTxId, MatchedTransactionId = matchedTxId, Status = "pending" });
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId });
        _transactions.GetTransactionAsync(matchedTxId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = matchedTxId, IsPaid = true, BudgetPeriodId = periodId });
        _transactions.GetTransactionAsync(thisTxId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = thisTxId, Amount = 300.00m });
        _transactions.GetTransactionAsync(siblingTxId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = siblingTxId, Amount = 200.00m });
        _reviews.ListByMatchedTransactionIdAsync(matchedTxId, Arg.Any<CancellationToken>()).Returns([
            new TransactionReview { Id = reviewId, BudgetPeriodId = periodId, TransactionId = thisTxId, MatchedTransactionId = matchedTxId, Status = "pending" },
            new TransactionReview { Id = siblingReviewId, BudgetPeriodId = periodId, TransactionId = siblingTxId, MatchedTransactionId = matchedTxId, Status = "confirmed" },
        ]);
        _transactions.MarkTransactionAsPaidAsync(matchedTxId, periodId, 500.00m, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = matchedTxId, IsPaid = true });
        _transactions.SetTransactionExcludedAsync(thisTxId, periodId, true, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = thisTxId, IsExcluded = true });

        await new ConfirmTransactionReviewCommandHandler(Access, _reviews, _transactions, _fixedExpenses, _profiles, NullLogger<ConfirmTransactionReviewCommandHandler>.Instance)
            .Handle(new ConfirmTransactionReviewCommand(adminId, reviewId), CancellationToken.None);

        await _transactions.Received(1).MarkTransactionAsPaidAsync(matchedTxId, periodId, 500.00m, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    // ── DismissTransactionReview ──────────────────────────────────────────

    [Fact]
    public async Task Dismiss_Success()
    {
        var (adminId, profileId) = SetUpProfile();
        var periodId = Guid.NewGuid();
        var reviewId = Guid.NewGuid();
        _reviews.GetByIdAsync(reviewId, Arg.Any<CancellationToken>())
            .Returns(new TransactionReview { Id = reviewId, BudgetPeriodId = periodId, Status = "pending" });
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId });

        await new DismissTransactionReviewCommandHandler(Access, _reviews)
            .Handle(new DismissTransactionReviewCommand(adminId, reviewId), CancellationToken.None);

        await _reviews.Received(1).UpdateStatusAsync(reviewId, "dismissed", Arg.Any<CancellationToken>());
    }
}
