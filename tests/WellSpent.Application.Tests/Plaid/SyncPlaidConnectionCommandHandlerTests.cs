using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Plaid;
using WellSpent.Application.Plaid.SyncPlaidConnection;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Tests.Plaid;

public sealed class SyncPlaidConnectionCommandHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPlaidItemRepository _items = Substitute.For<IPlaidItemRepository>();
    private readonly PlaidItemSync _itemSync =
        Substitute.For<PlaidItemSync>(Substitute.For<IServiceScopeFactory>(), NullLogger<PlaidItemSync>.Instance);

    private SyncPlaidConnectionCommandHandler CreateHandler() =>
        new(new PlaidAccessGuard(_users), _items, _itemSync, NullLogger<SyncPlaidConnectionCommandHandler>.Instance);

    private static User UsUser(string plan = "lifetime") => new() { Id = Guid.NewGuid(), Email = "u@example.com", CountryCode = "US", Plan = plan };

    private PlaidItem SetUp(User user, string ownerId = "", string status = "active", bool syncSucceeds = true)
    {
        var item = new PlaidItem
        {
            Id = Guid.NewGuid(), UserId = ownerId == "other" ? Guid.NewGuid() : user.Id,
            AccessToken = "x", ItemId = "i1", Status = status, LastManualResyncAt = DateTime.UtcNow.AddHours(-1),
        };
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _items.GetByIdAsync(item.Id, Arg.Any<CancellationToken>()).Returns(item);
        _itemSync.SyncItemAsync(item.Id).Returns(Task.FromResult(syncSucceeds));
        return item;
    }

    [Fact]
    public async Task Success_SyncsAndReturnsTheRefreshedConnection_WithoutTouchingTheCursor()
    {
        var user = UsUser();
        var item = SetUp(user);

        var result = await CreateHandler().Handle(new SyncPlaidConnectionCommand(user.Id, item.Id), CancellationToken.None);

        await _itemSync.Received(1).SyncItemAsync(item.Id);
        await _items.DidNotReceive().ResetCursorAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        Assert.True(result.IsOwner);
        Assert.NotNull(result.ResyncAvailableAt); // the daily resync stays on cooldown; Sync now doesn't reset it
    }

    [Fact]
    public async Task SyncFailure_ThrowsSoTheAppCanSayIt()
    {
        var user = UsUser();
        var item = SetUp(user, syncSucceeds: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateHandler().Handle(new SyncPlaidConnectionCommand(user.Id, item.Id), CancellationToken.None));
    }

    [Fact]
    public async Task NotTheOwner_ThrowsForbidden_AndDoesNotSync()
    {
        var user = UsUser();
        var item = SetUp(user, ownerId: "other");

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            CreateHandler().Handle(new SyncPlaidConnectionCommand(user.Id, item.Id), CancellationToken.None));
        await _itemSync.DidNotReceive().SyncItemAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task FreeTier_ThrowsValidation_AndDoesNotSync()
    {
        var user = UsUser("free");
        var item = SetUp(user);

        await Assert.ThrowsAsync<AppValidationException>(() =>
            CreateHandler().Handle(new SyncPlaidConnectionCommand(user.Id, item.Id), CancellationToken.None));
        await _itemSync.DidNotReceive().SyncItemAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task Disconnected_ThrowsValidation_AndDoesNotSync()
    {
        var user = UsUser();
        var item = SetUp(user, status: "disconnected");

        await Assert.ThrowsAsync<AppValidationException>(() =>
            CreateHandler().Handle(new SyncPlaidConnectionCommand(user.Id, item.Id), CancellationToken.None));
        await _itemSync.DidNotReceive().SyncItemAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task ErroredConnection_IsAllowed()
    {
        var user = UsUser();
        var item = SetUp(user, status: "error");

        await CreateHandler().Handle(new SyncPlaidConnectionCommand(user.Id, item.Id), CancellationToken.None);

        await _itemSync.Received(1).SyncItemAsync(item.Id);
    }
}
