using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Plaid;
using WellSpent.Application.Plaid.ResyncPlaidConnection;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Tests.Plaid;

/// <summary>Mirrors internal/service/plaid_service_test.go's TestPlaid_Resync_* suite.</summary>
public sealed class ResyncPlaidConnectionCommandHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPlaidItemRepository _items = Substitute.For<IPlaidItemRepository>();

    // Unconfigured: CreateScope() returns null, which the real
    // PlaidBackgroundSync catches and logs internally.
    private readonly IServiceScopeFactory _scopeFactory = Substitute.For<IServiceScopeFactory>();

    private ResyncPlaidConnectionCommandHandler CreateHandler() =>
        new(new PlaidAccessGuard(_users), _items,
            new PlaidBackgroundSync(_scopeFactory, NullLogger<PlaidBackgroundSync>.Instance),
            NullLogger<ResyncPlaidConnectionCommandHandler>.Instance);

    private static User UsUser(string plan = "lifetime") => new() { Id = Guid.NewGuid(), Email = "u@example.com", CountryCode = "US", Plan = plan };

    private void SetUpResetCursor(PlaidItem item, Action onReset)
    {
        _items.GetByIdAsync(item.Id, Arg.Any<CancellationToken>()).Returns(item);
        _items.ResetCursorAsync(item.Id, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            onReset();
            return Task.FromResult(new PlaidItem { Id = item.Id, UserId = item.UserId, AccessToken = item.AccessToken, ItemId = item.ItemId, Status = "active" });
        });
    }

    [Fact]
    public async Task Success_ClearsCursor()
    {
        var user = UsUser();
        var item = new PlaidItem { Id = Guid.NewGuid(), UserId = user.Id, AccessToken = "x", ItemId = "i1", Status = "active" };
        var reset = false;
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        SetUpResetCursor(item, () => reset = true);

        var result = await CreateHandler().Handle(new ResyncPlaidConnectionCommand(user.Id, item.Id), CancellationToken.None);

        Assert.True(reset, "expected the cursor to be cleared");
        Assert.True(result.IsOwner);
        Assert.Null(result.ResyncAvailableAt);
    }

    [Fact]
    public async Task WrongUser_ThrowsForbiddenException_AndDoesNotResetCursor()
    {
        var user = UsUser();
        var item = new PlaidItem { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), AccessToken = "x", ItemId = "i1", Status = "active" };
        var reset = false;
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        SetUpResetCursor(item, () => reset = true);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            CreateHandler().Handle(new ResyncPlaidConnectionCommand(user.Id, item.Id), CancellationToken.None));
        Assert.False(reset, "a non-owner must not clear anyone's cursor");
    }

    [Fact]
    public async Task FreeTier_ThrowsAppValidationException_AndDoesNotResetCursor()
    {
        var user = UsUser("free");
        var item = new PlaidItem { Id = Guid.NewGuid(), UserId = user.Id, AccessToken = "x", ItemId = "i1", Status = "active" };
        var reset = false;
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        SetUpResetCursor(item, () => reset = true);

        await Assert.ThrowsAsync<AppValidationException>(() =>
            CreateHandler().Handle(new ResyncPlaidConnectionCommand(user.Id, item.Id), CancellationToken.None));
        Assert.False(reset, "an unentitled connection must keep its cursor");
    }

    [Fact]
    public async Task Disconnected_ThrowsAppValidationException_AndDoesNotResetCursor()
    {
        var user = UsUser();
        var item = new PlaidItem { Id = Guid.NewGuid(), UserId = user.Id, AccessToken = "x", ItemId = "i1", Status = "disconnected" };
        var reset = false;
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        SetUpResetCursor(item, () => reset = true);

        await Assert.ThrowsAsync<AppValidationException>(() =>
            CreateHandler().Handle(new ResyncPlaidConnectionCommand(user.Id, item.Id), CancellationToken.None));
        Assert.False(reset);
    }

    [Fact]
    public async Task WithinCooldown_ThrowsAppValidationException_AndDoesNotResetCursor()
    {
        var user = UsUser();
        var item = new PlaidItem
        {
            Id = Guid.NewGuid(), UserId = user.Id, AccessToken = "x", ItemId = "i1", Status = "active",
            LastManualResyncAt = DateTime.UtcNow.AddHours(-1),
        };
        var reset = false;
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        SetUpResetCursor(item, () => reset = true);

        await Assert.ThrowsAsync<AppValidationException>(() =>
            CreateHandler().Handle(new ResyncPlaidConnectionCommand(user.Id, item.Id), CancellationToken.None));
        Assert.False(reset, "a second resync inside the cooldown must not replay the history again");
    }

    [Fact]
    public async Task AfterCooldownElapsed_Allowed()
    {
        var user = UsUser();
        var item = new PlaidItem
        {
            Id = Guid.NewGuid(), UserId = user.Id, AccessToken = "x", ItemId = "i1", Status = "active",
            LastManualResyncAt = DateTime.UtcNow.AddHours(-25),
        };
        var reset = false;
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        SetUpResetCursor(item, () => reset = true);

        await CreateHandler().Handle(new ResyncPlaidConnectionCommand(user.Id, item.Id), CancellationToken.None);

        Assert.True(reset);
    }
}
