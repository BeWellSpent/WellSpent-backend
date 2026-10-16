using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Common;
using WellSpent.Application.Notifications;
using WellSpent.Application.Notifications.UpsertAlertSubscription;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Notifications;

public sealed class UpsertAlertSubscriptionCommandHandlerTests
{
    private readonly INotificationRepository _notifications = Substitute.For<INotificationRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();
    private static readonly IMapper Mapper = new MapperConfiguration(
        cfg => cfg.AddProfile<NotificationMappingProfile>(), NullLoggerFactory.Instance).CreateMapper();

    private UpsertAlertSubscriptionCommandHandler CreateHandler() =>
        new(_notifications, _users, new BudgetAccessGuard(_profiles), Mapper);

    private (Guid UserId, Guid ProfileId) SetUpOwner(string plan)
    {
        var profileId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = userId, Name = "Budget" });
        _users.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new User { Id = userId, Email = "x@example.com", Plan = plan });
        return (userId, profileId);
    }

    private UpsertAlertSubscriptionCommand Command(Guid userId, Guid profileId, string alertType = "period_created") =>
        new(userId, profileId, alertType, "in_app", 0, "", null, false);

    [Fact]
    public async Task NonMember_PropagatesNotFoundException()
    {
        var profileId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = Guid.NewGuid(), Name = "Budget" });
        _profiles.GetPersonByUserIdAsync(profileId, strangerId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BudgetPerson>(new NotFoundException("budget_person", strangerId.ToString())));

        await Assert.ThrowsAsync<NotFoundException>(
            () => CreateHandler().Handle(Command(strangerId, profileId), CancellationToken.None));
    }

    [Fact]
    public async Task FreeTier_NewTransaction_Throws()
    {
        var (userId, profileId) = SetUpOwner("free");

        await Assert.ThrowsAsync<AppValidationException>(() => CreateHandler().Handle(
            Command(userId, profileId, NotificationConstants.AlertTypeNewTransaction), CancellationToken.None));
    }

    [Fact]
    public async Task FreeTier_AtLimit_NewAlertType_Throws()
    {
        var (userId, profileId) = SetUpOwner("free");
        _notifications.ListSubscriptionsAsync(userId, profileId, Arg.Any<CancellationToken>()).Returns(
        [
            new AlertSubscription { Id = Guid.NewGuid(), UserId = userId, BudgetProfileId = profileId, AlertType = "period_created", Channel = "in_app" },
            new AlertSubscription { Id = Guid.NewGuid(), UserId = userId, BudgetProfileId = profileId, AlertType = "review_pending", Channel = "in_app" },
        ]);

        await Assert.ThrowsAsync<AppValidationException>(() => CreateHandler().Handle(
            Command(userId, profileId, "review_pending_different"), CancellationToken.None));
    }

    [Fact]
    public async Task FreeTier_AtLimit_UpdatingExistingAlertType_Succeeds()
    {
        var (userId, profileId) = SetUpOwner("free");
        _notifications.ListSubscriptionsAsync(userId, profileId, Arg.Any<CancellationToken>()).Returns(
        [
            new AlertSubscription { Id = Guid.NewGuid(), UserId = userId, BudgetProfileId = profileId, AlertType = "period_created", Channel = "in_app" },
            new AlertSubscription { Id = Guid.NewGuid(), UserId = userId, BudgetProfileId = profileId, AlertType = "review_pending", Channel = "in_app" },
        ]);
        _notifications.UpsertSubscriptionAsync(Arg.Any<AlertSubscription>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<AlertSubscription>());

        // "period_created" already exists, so this is an update, not an addition.
        var result = await CreateHandler().Handle(Command(userId, profileId, "period_created"), CancellationToken.None);

        Assert.Equal("period_created", result.AlertType);
    }

    [Fact]
    public async Task ProTier_NoLimit()
    {
        var (userId, profileId) = SetUpOwner("pro");
        _notifications.UpsertSubscriptionAsync(Arg.Any<AlertSubscription>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<AlertSubscription>());

        var result = await CreateHandler().Handle(
            Command(userId, profileId, NotificationConstants.AlertTypeNewTransaction), CancellationToken.None);

        Assert.Equal(NotificationConstants.AlertTypeNewTransaction, result.AlertType);
    }

    [Fact]
    public async Task SpendingThreshold_StoresThresholdPct_OtherTypesDoNot()
    {
        var (userId, profileId) = SetUpOwner("pro");
        _notifications.UpsertSubscriptionAsync(Arg.Any<AlertSubscription>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<AlertSubscription>());

        var command = new UpsertAlertSubscriptionCommand(
            userId, profileId, NotificationConstants.AlertTypeSpendingThreshold, "email", 80, "budget", null, false);
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        Assert.Equal(80, result.ThresholdPct);

        var nonThresholdResult = await CreateHandler().Handle(Command(userId, profileId), CancellationToken.None);
        Assert.Null(nonThresholdResult.ThresholdPct);
    }
}
