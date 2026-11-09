using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Configuration;
using WellSpent.Application.Notifications;
using WellSpent.Application.Plaid;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.Tests.Plaid;

public sealed class PlaidItemSyncTests
{
    private readonly IPlaidItemRepository _items = Substitute.For<IPlaidItemRepository>();
    private readonly IPlaidClient _plaid = Substitute.For<IPlaidClient>();
    private readonly PlaidItem _item = new() { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), BudgetProfileId = Guid.NewGuid(), AccessToken = "encrypted", ItemId = "item-1" };

    private PlaidItemSync CreateSync()
    {
        var users = Substitute.For<IUserRepository>();
        var budgets = Substitute.For<IBudgetProfileRepository>();
        var transactions = Substitute.For<ITransactionRepository>();
        var crypto = Substitute.For<ICryptoService>();
        var fixedExpenses = Substitute.For<IFixedExpenseRepository>();
        fixedExpenses.ListAsync(_item.BudgetProfileId, Arg.Any<CancellationToken>()).Returns([]); // NSubstitute would return null here
        users.GetByIdAsync(_item.UserId, Arg.Any<CancellationToken>()).Returns(new User { Id = _item.UserId, Email = "o@example.com", Plan = "pro" });
        transactions.ListSystemCategoriesAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<string, int>());
        crypto.Decrypt("encrypted", "test-key").Returns("token");
        budgets.GetByIdAsync(_item.BudgetProfileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = _item.BudgetProfileId, UserId = _item.UserId, Name = "x", Cycle = "monthly", AutoUpdatePlannedAmount = false });
        _items.GetByIdAsync(_item.Id, Arg.Any<CancellationToken>()).Returns(_item);

        var engine = new PlaidSyncEngine(
            _items, users, budgets, transactions, fixedExpenses, Substitute.For<ITransactionReviewRepository>(),
            _plaid, crypto,
            new NotificationDispatcher(Substitute.For<INotificationRepository>(), budgets, users, Substitute.For<IPushSender>(), transactions,
                Substitute.For<IExpenseAllocationRepository>(), Substitute.For<IEmailSender>(), Options.Create(new AuthOptions()), NullLogger<NotificationDispatcher>.Instance),
            Options.Create(new AuthOptions { EncryptionKey = "test-key" }), NullLogger<PlaidSyncEngine>.Instance);

        var services = new ServiceCollection();
        services.AddSingleton(_items);
        services.AddScoped(_ => engine);
        return new PlaidItemSync(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), NullLogger<PlaidItemSync>.Instance);
    }

    [Fact]
    public async Task SyncItemAsync_DoesNotCompleteUntilTheSyncHasFinished()
    {
        var gate = new TaskCompletionSource<PlaidSyncResult>();
        _plaid.SyncTransactionsAsync("token", "", Arg.Any<CancellationToken>()).Returns(gate.Task);

        var call = CreateSync().SyncItemAsync(_item.Id);
        await Task.Delay(50);
        Assert.False(call.IsCompleted);

        gate.SetResult(new PlaidSyncResult([], [], [], "next"));
        await call;

        await _items.Received(1).UpdateSyncAsync(_item.Id, "next", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OverlappingCallsForOneItem_ShareASingleRun()
    {
        var gate = new TaskCompletionSource<PlaidSyncResult>();
        _plaid.SyncTransactionsAsync("token", "", Arg.Any<CancellationToken>()).Returns(gate.Task);
        var sync = CreateSync();

        var first = sync.SyncItemAsync(_item.Id);
        var retry = sync.SyncItemAsync(_item.Id);
        gate.SetResult(new PlaidSyncResult([], [], [], "next"));
        await Task.WhenAll(first, retry);

        await _plaid.Received(1).SyncTransactionsAsync("token", "", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailedRun_IsLoggedNotThrown_AndAFollowUpCallRunsAgain()
    {
        _plaid.SyncTransactionsAsync("token", "", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PlaidSyncResult>(new TimeoutException("plaid down")));
        var sync = CreateSync();

        await sync.SyncItemAsync(_item.Id);
        await sync.SyncItemAsync(_item.Id);

        await _plaid.Received(2).SyncTransactionsAsync("token", "", Arg.Any<CancellationToken>());
    }
}
