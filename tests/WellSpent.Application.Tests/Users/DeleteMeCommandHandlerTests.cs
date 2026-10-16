using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Configuration;
using WellSpent.Application.Users.DeleteMe;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using Xunit;

namespace WellSpent.Application.Tests.Users;

public sealed class DeleteMeCommandHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IAppleAuthClient _apple = Substitute.For<IAppleAuthClient>();
    private readonly ICryptoService _crypto = Substitute.For<ICryptoService>();

    private DeleteMeCommandHandler CreateHandler(string encryptionKey = "hex-key") =>
        new(_users, _apple, _crypto, Options.Create(new AuthOptions { EncryptionKey = encryptionKey }),
            NullLogger<DeleteMeCommandHandler>.Instance);

    [Fact]
    public async Task NoOAuthAccounts_StillSoftDeletes()
    {
        var userId = Guid.NewGuid();
        _users.ListOAuthAccountsByUserAsync(userId, Arg.Any<CancellationToken>()).Returns([]);

        await CreateHandler().Handle(new DeleteMeCommand(userId), CancellationToken.None);

        await _users.Received(1).SoftDeleteAsync(userId, Arg.Any<CancellationToken>());
        await _apple.DidNotReceive().RevokeRefreshTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AppleAccountWithRefreshToken_DecryptsAndRevokes()
    {
        var userId = Guid.NewGuid();
        _users.ListOAuthAccountsByUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns([new OAuthAccount { Id = Guid.NewGuid(), UserId = userId, OauthName = "apple", AccountId = "sub", AccountEmail = "a@example.com", RefreshToken = "encrypted" }]);
        _crypto.Decrypt("encrypted", "hex-key").Returns("plain-refresh-token");

        await CreateHandler().Handle(new DeleteMeCommand(userId), CancellationToken.None);

        await _apple.Received(1).RevokeRefreshTokenAsync("plain-refresh-token", Arg.Any<CancellationToken>());
        await _users.Received(1).SoftDeleteAsync(userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GoogleAccount_NeverAttemptsAppleRevocation()
    {
        var userId = Guid.NewGuid();
        _users.ListOAuthAccountsByUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns([new OAuthAccount { Id = Guid.NewGuid(), UserId = userId, OauthName = "google", AccountId = "sub", AccountEmail = "a@example.com", RefreshToken = null }]);

        await CreateHandler().Handle(new DeleteMeCommand(userId), CancellationToken.None);

        await _apple.DidNotReceive().RevokeRefreshTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _users.Received(1).SoftDeleteAsync(userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokeFailure_IsSwallowed_StillSoftDeletes()
    {
        var userId = Guid.NewGuid();
        _users.ListOAuthAccountsByUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns([new OAuthAccount { Id = Guid.NewGuid(), UserId = userId, OauthName = "apple", AccountId = "sub", AccountEmail = "a@example.com", RefreshToken = "encrypted" }]);
        _crypto.Decrypt("encrypted", "hex-key").Returns("plain-refresh-token");
        _apple.RevokeRefreshTokenAsync("plain-refresh-token", Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new HttpRequestException("apple unreachable")));

        await CreateHandler().Handle(new DeleteMeCommand(userId), CancellationToken.None);

        await _users.Received(1).SoftDeleteAsync(userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoEncryptionKeyConfigured_SkipsRevocation_StillSoftDeletes()
    {
        var userId = Guid.NewGuid();
        _users.ListOAuthAccountsByUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns([new OAuthAccount { Id = Guid.NewGuid(), UserId = userId, OauthName = "apple", AccountId = "sub", AccountEmail = "a@example.com", RefreshToken = "encrypted" }]);

        await CreateHandler(encryptionKey: "").Handle(new DeleteMeCommand(userId), CancellationToken.None);

        await _apple.DidNotReceive().RevokeRefreshTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _users.Received(1).SoftDeleteAsync(userId, Arg.Any<CancellationToken>());
    }
}
