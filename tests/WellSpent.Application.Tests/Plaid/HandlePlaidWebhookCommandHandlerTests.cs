using Going.Plaid.Webhook;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Plaid;
using WellSpent.Application.Plaid.HandlePlaidWebhook;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.Tests.Plaid;

public sealed class HandlePlaidWebhookCommandHandlerTests
{
    private readonly IPlaidItemRepository _items = Substitute.For<IPlaidItemRepository>();

    // Unconfigured CreateScope() returns null; PlaidBackgroundSync catches and logs it rather than throwing, so FireAndForgetSyncItem is safe to call in tests.
    private readonly IServiceScopeFactory _scopeFactory = Substitute.For<IServiceScopeFactory>();

    private HandlePlaidWebhookCommandHandler CreateHandler() => new(
        _items, new PlaidBackgroundSync(_scopeFactory, NullLogger<PlaidBackgroundSync>.Instance), NullLogger<HandlePlaidWebhookCommandHandler>.Instance);

    [Fact]
    public async Task SyncUpdatesAvailable_KnownItem_FiresSync()
    {
        var item = new PlaidItem { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), BudgetProfileId = Guid.NewGuid(), AccessToken = "enc", ItemId = "item-123" };
        _items.GetByItemIdAsync("item-123", Arg.Any<CancellationToken>()).Returns(item);

        // No throw is the assertion — FireAndForgetSyncItem is a detached Task.Run the handler doesn't await the result of.
        await CreateHandler().Handle(new HandlePlaidWebhookCommand(new SyncUpdatesAvailableWebhook { ItemId = "item-123" }), CancellationToken.None);

        await _items.Received(1).GetByItemIdAsync("item-123", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncUpdatesAvailable_UnknownItem_NoThrowNoStatusUpdate()
    {
        _items.GetByItemIdAsync("unknown-item", Arg.Any<CancellationToken>()).Returns((PlaidItem?)null);

        await CreateHandler().Handle(new HandlePlaidWebhookCommand(new SyncUpdatesAvailableWebhook { ItemId = "unknown-item" }), CancellationToken.None);

        await _items.DidNotReceive().UpdateStatusAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ItemError_KnownItem_UpdatesStatusToError()
    {
        var item = new PlaidItem { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), BudgetProfileId = Guid.NewGuid(), AccessToken = "enc", ItemId = "item-456" };
        _items.GetByItemIdAsync("item-456", Arg.Any<CancellationToken>()).Returns(item);

        await CreateHandler().Handle(new HandlePlaidWebhookCommand(new ItemErrorWebhook { ItemId = "item-456" }), CancellationToken.None);

        await _items.Received(1).UpdateStatusAsync(item.Id, "error", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ItemError_UnknownItem_NoOp()
    {
        _items.GetByItemIdAsync("unknown-item", Arg.Any<CancellationToken>()).Returns((PlaidItem?)null);

        await CreateHandler().Handle(new HandlePlaidWebhookCommand(new ItemErrorWebhook { ItemId = "unknown-item" }), CancellationToken.None);

        await _items.DidNotReceive().UpdateStatusAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnrecognizedWebhook_NoOpNoThrow()
    {
        await CreateHandler().Handle(new HandlePlaidWebhookCommand(new HistoricalUpdateWebhook { ItemId = "item-789" }), CancellationToken.None);

        await _items.DidNotReceive().GetByItemIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
