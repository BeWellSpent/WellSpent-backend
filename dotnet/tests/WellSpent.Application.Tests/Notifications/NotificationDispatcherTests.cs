using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Configuration;
using WellSpent.Application.Notifications;
using WellSpent.Application.Plaid;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using Xunit;

namespace WellSpent.Application.Tests.Notifications;

public sealed class NotificationDispatcherTests
{
    private readonly INotificationRepository _notifications = Substitute.For<INotificationRepository>();
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ITransactionRepository _transactions = Substitute.For<ITransactionRepository>();
    private readonly IExpenseAllocationRepository _allocations = Substitute.For<IExpenseAllocationRepository>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();

    private NotificationDispatcher CreateDispatcher() => new(
        _notifications, _profiles, _users, _transactions, _allocations, _emailSender,
        Options.Create(new AuthOptions { FrontendUrl = "https://bewellspent.com" }),
        NullLogger<NotificationDispatcher>.Instance);

    private static AlertSubscription Sub(Guid userId, Guid profileId, string alertType, string channel = "in_app", bool notifyAll = false) =>
        new() { Id = Guid.NewGuid(), UserId = userId, BudgetProfileId = profileId, AlertType = alertType, Channel = channel, NotifyAllMembers = notifyAll };

    [Fact]
    public async Task HandleNewTransaction_DoesNotNotifyTheCreator()
    {
        var profileId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var creatorId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 1, 31) });
        _notifications.GetBudgetSubscribersAsync(profileId, "new_transaction", Arg.Any<CancellationToken>())
            .Returns([Sub(creatorId, profileId, "new_transaction"), Sub(otherUserId, profileId, "new_transaction")]);

        var tx = new Transaction { Id = Guid.NewGuid(), Name = "Coffee", Amount = 5m, PlannedAmount = 5m, BudgetPeriodId = periodId, TransactionTypeId = 2 };
        await CreateDispatcher().HandleNewTransactionAsync(tx, periodId, creatorId, CancellationToken.None);

        await _notifications.Received(1).CreateAsync(Arg.Is<Notification>(n => n.UserId == otherUserId), Arg.Any<CancellationToken>());
        await _notifications.DidNotReceive().CreateAsync(Arg.Is<Notification>(n => n.UserId == creatorId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deliver_FansOutToEveryActiveMember_WhenNotifyAllMembersIsSet()
    {
        var profileId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var subscriberId = Guid.NewGuid();
        var memberA = Guid.NewGuid();
        var memberB = Guid.NewGuid();
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 1, 31) });
        _notifications.GetBudgetSubscribersAsync(profileId, "new_transaction", Arg.Any<CancellationToken>())
            .Returns([Sub(subscriberId, profileId, "new_transaction", notifyAll: true)]);
        _profiles.ListPeopleAsync(profileId, Arg.Any<CancellationToken>()).Returns([
            new BudgetPerson { Id = 1, BudgetProfileId = profileId, UserId = memberA, IsActive = true, Role = "admin" },
            new BudgetPerson { Id = 2, BudgetProfileId = profileId, UserId = memberB, IsActive = true, Role = "collaborator" },
            new BudgetPerson { Id = 3, BudgetProfileId = profileId, UserId = Guid.NewGuid(), IsActive = false, Role = "viewer" },
        ]);

        var tx = new Transaction { Id = Guid.NewGuid(), Name = "Rent", Amount = 100m, PlannedAmount = 100m, BudgetPeriodId = periodId, TransactionTypeId = 2 };
        await CreateDispatcher().HandleNewTransactionAsync(tx, periodId, Guid.NewGuid(), CancellationToken.None);

        // Both active members notified, the inactive one skipped, and the subscriber's own id (not in the member list) never used directly.
        await _notifications.Received(1).CreateAsync(Arg.Is<Notification>(n => n.UserId == memberA), Arg.Any<CancellationToken>());
        await _notifications.Received(1).CreateAsync(Arg.Is<Notification>(n => n.UserId == memberB), Arg.Any<CancellationToken>());
        await _notifications.Received(2).CreateAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandlePlaidTransactionsImported_NoOp_WhenTotalIsZero()
    {
        var profileId = Guid.NewGuid();
        await CreateDispatcher().HandlePlaidTransactionsImportedAsync(profileId, [], CancellationToken.None);

        await _notifications.DidNotReceive().GetBudgetSubscribersAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandlePlaidTransactionsImported_NotifiesSubscribers_WhenImportsExist()
    {
        var profileId = Guid.NewGuid();
        var subscriberId = Guid.NewGuid();
        _notifications.GetBudgetSubscribersAsync(profileId, "new_transaction", Arg.Any<CancellationToken>())
            .Returns([Sub(subscriberId, profileId, "new_transaction")]);

        await CreateDispatcher().HandlePlaidTransactionsImportedAsync(profileId, [new AccountImport("Chase", 3)], CancellationToken.None);

        await _notifications.Received(1).CreateAsync(
            Arg.Is<Notification>(n => n.UserId == subscriberId && n.Title == "New transactions imported"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleReviewPendingBatch_NoOp_WhenCountIsZero()
    {
        await CreateDispatcher().HandleReviewPendingBatchAsync(Guid.NewGuid(), 0, CancellationToken.None);
        await _notifications.DidNotReceive().GetBudgetSubscribersAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckSpendingThreshold_FiresOnlyWhenCrossingFromBelowToAtOrAboveThePct()
    {
        var profileId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var subscriberId = Guid.NewGuid();
        _notifications.GetBudgetSubscribersAsync(profileId, "spending_threshold", Arg.Any<CancellationToken>())
            .Returns([new AlertSubscription { Id = Guid.NewGuid(), UserId = subscriberId, BudgetProfileId = profileId, AlertType = "spending_threshold", Channel = "in_app", ThresholdPct = 80m, ThresholdScope = "budget" }]);
        _allocations.ListAsync(profileId, Arg.Any<CancellationToken>())
            .Returns([new ExpenseAllocation { Id = 1, BudgetProfileId = profileId, CategoryId = 1, PlannedAmount = 100m }]);

        // Prior spend $70 (below 80%); the new $15 transaction brings the total to $85 (85% — crosses 80%).
        var newTx = new Transaction { Id = Guid.NewGuid(), Name = "Groceries", Amount = 15m, PlannedAmount = 15m, CategoryId = 1, BudgetPeriodId = periodId, TransactionTypeId = 2 };
        _transactions.ListTransactionsAsync(periodId, null, 2, null, Arg.Any<CancellationToken>()).Returns([
            new Transaction { Id = Guid.NewGuid(), Amount = 70m, CategoryId = 1, TransactionTypeId = 2 },
            newTx,
        ]);
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 1, 31) });

        await CreateDispatcher().HandleNewTransactionAsync(newTx, periodId, Guid.NewGuid(), CancellationToken.None);

        await _notifications.Received(1).CreateAsync(
            Arg.Is<Notification>(n => n.UserId == subscriberId && n.AlertType == "spending_threshold"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckSpendingThreshold_DoesNotFire_WhenAlreadyPastThresholdBeforeThisTransaction()
    {
        var profileId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var subscriberId = Guid.NewGuid();
        _notifications.GetBudgetSubscribersAsync(profileId, "spending_threshold", Arg.Any<CancellationToken>())
            .Returns([new AlertSubscription { Id = Guid.NewGuid(), UserId = subscriberId, BudgetProfileId = profileId, AlertType = "spending_threshold", Channel = "in_app", ThresholdPct = 80m, ThresholdScope = "budget" }]);
        _allocations.ListAsync(profileId, Arg.Any<CancellationToken>())
            .Returns([new ExpenseAllocation { Id = 1, BudgetProfileId = profileId, CategoryId = 1, PlannedAmount = 100m }]);

        // Prior spend already $90 (90%, already past 80%); adding $5 more never "crosses" — it was already over.
        var newTx = new Transaction { Id = Guid.NewGuid(), Name = "Snack", Amount = 5m, PlannedAmount = 5m, CategoryId = 1, BudgetPeriodId = periodId, TransactionTypeId = 2 };
        _transactions.ListTransactionsAsync(periodId, null, 2, null, Arg.Any<CancellationToken>()).Returns([
            new Transaction { Id = Guid.NewGuid(), Amount = 90m, CategoryId = 1, TransactionTypeId = 2 },
            newTx,
        ]);
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 1, 31) });

        await CreateDispatcher().HandleNewTransactionAsync(newTx, periodId, Guid.NewGuid(), CancellationToken.None);

        await _notifications.DidNotReceive().CreateAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>());
    }
}
