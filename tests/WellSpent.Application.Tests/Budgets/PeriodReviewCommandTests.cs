using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Budgets;
using WellSpent.Application.Budgets.ConfirmPeriodReview;
using WellSpent.Application.Budgets.RetriggerPeriodReview;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Budgets;

public sealed class ConfirmPeriodReviewCommandTests
{
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();
    private static readonly AutoMapper.IMapper Mapper = new AutoMapper.MapperConfiguration(
        cfg => cfg.AddProfile<BudgetMappingProfile>(), NullLoggerFactory.Instance).CreateMapper();

    private ConfirmPeriodReviewCommandHandler Handler => new(new BudgetAccessGuard(_profiles), _profiles, Mapper);

    [Fact]
    public async Task Admin_NeedsReviewTrue_ConfirmsAndClears()
    {
        var profileId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, NeedsReview = true });
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = ownerId, Name = "x" });

        var result = await Handler.Handle(new ConfirmPeriodReviewCommand(ownerId, periodId), CancellationToken.None);

        Assert.False(result.NeedsReview);
        await _profiles.Received(1).SetPeriodNeedsReviewAsync(periodId, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Admin_NeedsReviewAlreadyFalse_IsNoOp()
    {
        var profileId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, NeedsReview = false });
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = ownerId, Name = "x" });

        var result = await Handler.Handle(new ConfirmPeriodReviewCommand(ownerId, periodId), CancellationToken.None);

        Assert.False(result.NeedsReview);
        await _profiles.DidNotReceive().SetPeriodNeedsReviewAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NonAdmin_ThrowsForbidden()
    {
        var profileId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var viewerId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, NeedsReview = true });
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = ownerId, Name = "x" });
        _profiles.GetPersonByUserIdAsync(profileId, viewerId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 1, BudgetProfileId = profileId, UserId = viewerId, Role = "viewer" });

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Handler.Handle(new ConfirmPeriodReviewCommand(viewerId, periodId), CancellationToken.None));
    }
}

public sealed class RetriggerPeriodReviewCommandTests
{
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();
    private readonly ITransactionRepository _transactions = Substitute.For<ITransactionRepository>();
    private static readonly AutoMapper.IMapper Mapper = new AutoMapper.MapperConfiguration(
        cfg => cfg.AddProfile<BudgetMappingProfile>(), NullLoggerFactory.Instance).CreateMapper();

    private RetriggerPeriodReviewCommandHandler Handler =>
        new(new BudgetAccessGuard(_profiles), _profiles, _transactions, NullLogger<RetriggerPeriodReviewCommandHandler>.Instance, Mapper);

    [Fact]
    public async Task NoPreviousPeriod_IsNoOp()
    {
        var ownerId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var period = new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 1, 31) };
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>()).Returns(period);
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = ownerId, Name = "x" });
        _profiles.ListPeriodsAsync(profileId, Arg.Any<CancellationToken>()).Returns([period]);

        await Handler.Handle(new RetriggerPeriodReviewCommand(ownerId, periodId), CancellationToken.None);

        await _transactions.DidNotReceive().DeleteCarriedTransactionsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HasPreviousPeriod_DeletesAndRecomputesCarryover()
    {
        var ownerId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var previous = new BudgetPeriod { Id = Guid.NewGuid(), BudgetProfileId = profileId, StartDate = new DateOnly(2025, 12, 1), EndDate = new DateOnly(2025, 12, 31) };
        var period = new BudgetPeriod { Id = Guid.NewGuid(), BudgetProfileId = profileId, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 1, 31) };
        _profiles.GetPeriodByIdAsync(period.Id, Arg.Any<CancellationToken>()).Returns(period);
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = ownerId, Name = "x", CarryoverEnabled = true });
        _profiles.ListPeriodsAsync(profileId, Arg.Any<CancellationToken>()).Returns([period, previous]);
        _transactions.CountCarriedAsync(period.Id, previous.Id, Arg.Any<CancellationToken>()).Returns(0);
        _transactions.ListSystemCategoriesAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int> { ["savings"] = 10, ["debt"] = 11, ["income"] = 1, ["payment"] = 2 });
        _transactions.ListTransactionsAsync(previous.Id, null, null, null, Arg.Any<CancellationToken>())
            .Returns([new Transaction { Amount = 1500m, TransactionTypeId = 2 }]);
        _profiles.ListIncomeEntriesAsync(previous.Id, Arg.Any<CancellationToken>()).Returns([new IncomeEntry { Amount = 2000m }]);

        await Handler.Handle(new RetriggerPeriodReviewCommand(ownerId, period.Id), CancellationToken.None);

        await _transactions.Received(1).DeleteCarriedTransactionsAsync(period.Id, previous.Id, Arg.Any<CancellationToken>());
        await _transactions.Received(1).CreateTransactionAsync(Arg.Is<Transaction>(t =>
            t.Amount == 500m && t.BudgetPeriodId == period.Id && t.CarriedFromBudgetPeriodId == previous.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NonAdmin_ThrowsForbidden()
    {
        var profileId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var viewerId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId });
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = ownerId, Name = "x" });
        _profiles.GetPersonByUserIdAsync(profileId, viewerId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 1, BudgetProfileId = profileId, UserId = viewerId, Role = "collaborator" });

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Handler.Handle(new RetriggerPeriodReviewCommand(viewerId, periodId), CancellationToken.None));
    }
}
