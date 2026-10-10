using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Common;
using WellSpent.Application.Plaid;
using WellSpent.Application.Plaid.GetPlaidConnections;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Tests.Plaid;

/// <summary>Mirrors internal/service/plaid_service_test.go's TestPlaid_GetConnections_ByBudget_* suite.</summary>
public sealed class GetPlaidConnectionsQueryHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();
    private readonly IPlaidItemRepository _items = Substitute.For<IPlaidItemRepository>();

    private GetPlaidConnectionsQueryHandler CreateHandler() => new(
        new PlaidAccessGuard(_users), new BudgetAccessGuard(_profiles), _items, _users,
        NullLogger<GetPlaidConnectionsQueryHandler>.Instance);

    private static User UsUser() => new() { Id = Guid.NewGuid(), Email = "u@example.com", CountryCode = "US", Plan = "lifetime" };

    [Fact]
    public async Task ByBudget_MarksOwnershipAndEntitlement()
    {
        var caller = UsUser();
        var profileId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        _users.GetByIdAsync(caller.Id, Arg.Any<CancellationToken>()).Returns(caller);
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>()).Returns(new BudgetProfile { Id = profileId, UserId = caller.Id, Name = "x", Cycle = "monthly" });
        _items.ListActiveWithOwnerByBudgetProfileAsync(profileId, Arg.Any<CancellationToken>()).Returns(
        [
            new PlaidItemWithOwnerRow(new PlaidItem { Id = Guid.NewGuid(), UserId = caller.Id, Status = "active", AccessToken = "x", ItemId = "i1" }, "Ada Lovelace", "lifetime"),
            new PlaidItemWithOwnerRow(new PlaidItem { Id = Guid.NewGuid(), UserId = otherUserId, Status = "active", AccessToken = "x", ItemId = "i2" }, "Grace Hopper", "free"),
        ]);
        _items.ListUnsyncableForUserAsync(caller.Id, Arg.Any<CancellationToken>()).Returns([]);

        var result = await CreateHandler().Handle(new GetPlaidConnectionsQuery(caller.Id, profileId), CancellationToken.None);

        Assert.Equal(2, result.Connections.Count);
        Assert.True(result.Connections[0].IsOwner);
        Assert.Equal("Ada Lovelace", result.Connections[0].OwnerName);
        Assert.True(result.Connections[0].SyncEnabled);

        // A co-member's connection: visible, attributed, but not actionable —
        // and flagged as never syncing, which status alone would report as healthy.
        Assert.False(result.Connections[1].IsOwner);
        Assert.Equal("Grace Hopper", result.Connections[1].OwnerName);
        Assert.False(result.Connections[1].SyncEnabled);
    }

    [Fact]
    public async Task ByBudget_NotAMember_ThrowsForbiddenException()
    {
        var caller = UsUser();
        var profileId = Guid.NewGuid();
        _users.GetByIdAsync(caller.Id, Arg.Any<CancellationToken>()).Returns(caller);
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>()).Returns(new BudgetProfile { Id = profileId, UserId = Guid.NewGuid(), Name = "x", Cycle = "monthly" });
        _profiles.GetPersonByUserIdAsync(profileId, caller.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BudgetPerson>(new NotFoundException("budget_person", caller.Id.ToString())));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            CreateHandler().Handle(new GetPlaidConnectionsQuery(caller.Id, profileId), CancellationToken.None));
    }
}
