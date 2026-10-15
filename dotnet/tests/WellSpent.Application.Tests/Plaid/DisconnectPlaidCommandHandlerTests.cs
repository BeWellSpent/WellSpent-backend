using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Configuration;
using WellSpent.Application.Plaid;
using WellSpent.Application.Plaid.DisconnectPlaid;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Tests.Plaid;

/// <summary>Mirrors internal/service/plaid_service_test.go's TestPlaid_Disconnect_* suite.</summary>
public sealed class DisconnectPlaidCommandHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPlaidItemRepository _items = Substitute.For<IPlaidItemRepository>();
    private readonly IPlaidClient _plaid = Substitute.For<IPlaidClient>();
    private readonly ICryptoService _crypto = Substitute.For<ICryptoService>();

    private DisconnectPlaidCommandHandler CreateHandler() => new(
        new PlaidAccessGuard(_users), _items, _plaid, _crypto,
        Options.Create(new AuthOptions { EncryptionKey = "test-key" }),
        NullLogger<DisconnectPlaidCommandHandler>.Instance);

    private static User UsUser(string plan = "lifetime") => new() { Id = Guid.NewGuid(), Email = "u@example.com", CountryCode = "US", Plan = plan };

    [Fact]
    public async Task Success_MarksDisconnected()
    {
        var user = UsUser();
        var connId = Guid.NewGuid();
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _items.GetByIdAsync(connId, Arg.Any<CancellationToken>())
            .Returns(new PlaidItem { Id = connId, UserId = user.Id, AccessToken = "access-sandbox", ItemId = "item-1" });
        _crypto.Decrypt("access-sandbox", "test-key").Returns("decrypted");

        await CreateHandler().Handle(new DisconnectPlaidCommand(user.Id, connId), CancellationToken.None);

        await _items.Received(1).UpdateStatusAsync(connId, "disconnected", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WrongUser_ThrowsForbiddenException()
    {
        var user = UsUser();
        var otherUserId = Guid.NewGuid();
        var connId = Guid.NewGuid();
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _items.GetByIdAsync(connId, Arg.Any<CancellationToken>())
            .Returns(new PlaidItem { Id = connId, UserId = otherUserId, AccessToken = "x", ItemId = "item-1" });

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            CreateHandler().Handle(new DisconnectPlaidCommand(user.Id, connId), CancellationToken.None));
    }

    [Fact]
    public async Task FreeTier_StillAllowed()
    {
        // Disconnect must never be gated by plan.
        var user = UsUser("free");
        var connId = Guid.NewGuid();
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _items.GetByIdAsync(connId, Arg.Any<CancellationToken>())
            .Returns(new PlaidItem { Id = connId, UserId = user.Id, AccessToken = "access-sandbox", ItemId = "item-1" });
        _crypto.Decrypt("access-sandbox", "test-key").Returns("decrypted");

        await CreateHandler().Handle(new DisconnectPlaidCommand(user.Id, connId), CancellationToken.None);

        await _items.Received(1).UpdateStatusAsync(connId, "disconnected", Arg.Any<CancellationToken>());
    }
}
