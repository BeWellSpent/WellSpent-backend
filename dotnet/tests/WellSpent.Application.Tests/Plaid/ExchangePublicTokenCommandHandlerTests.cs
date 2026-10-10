using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Common;
using WellSpent.Application.Configuration;
using WellSpent.Application.Plaid;
using WellSpent.Application.Plaid.ExchangePublicToken;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Tests.Plaid;

/// <summary>Mirrors internal/service/plaid_service_test.go's TestPlaid_ExchangePublicToken_* suite.</summary>
public sealed class ExchangePublicTokenCommandHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();
    private readonly IPlaidItemRepository _items = Substitute.For<IPlaidItemRepository>();
    private readonly ITransactionRepository _transactions = Substitute.For<ITransactionRepository>();
    private readonly IPlaidClient _plaid = Substitute.For<IPlaidClient>();
    private readonly ICryptoService _crypto = Substitute.For<ICryptoService>();

    private ExchangePublicTokenCommandHandler CreateHandler() => new(
        new PlaidAccessGuard(_users), new BudgetAccessGuard(_profiles), _items, _plaid, _crypto,
        Options.Create(new AuthOptions { EncryptionKey = "test-key" }),
        new PlaidPaymentMethodSync(_profiles, _transactions, NullLogger<PlaidPaymentMethodSync>.Instance),
        NullLogger<ExchangePublicTokenCommandHandler>.Instance);

    private static User UsUser(string plan = "lifetime") => new() { Id = Guid.NewGuid(), Email = "u@example.com", CountryCode = "US", Plan = plan };

    [Fact]
    public async Task Success_StoresItemForCallerAndProfile()
    {
        var user = UsUser();
        var profileId = Guid.NewGuid();
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>()).Returns(new BudgetProfile { Id = profileId, UserId = user.Id, Name = "x", Cycle = "monthly" });
        _plaid.ExchangePublicTokenAsync("public-token-sandbox", Arg.Any<CancellationToken>())
            .Returns(new PlaidExchangedToken("access-token", "item-id-123"));
        _crypto.Encrypt("access-token", "test-key").Returns("encrypted-access-token");
        _plaid.GetAccountsAsync("access-token", Arg.Any<CancellationToken>())
            .Returns(new PlaidAccountsResult([], ""));
        _items.CreateAsync(Arg.Any<PlaidItem>(), Arg.Any<CancellationToken>())
            .Returns(info =>
            {
                var item = (PlaidItem)info[0];
                item.Id = Guid.NewGuid();
                item.Status = "active";
                return Task.FromResult(item);
            });

        var result = await CreateHandler().Handle(new ExchangePublicTokenCommand(user.Id, profileId, "public-token-sandbox"), CancellationToken.None);

        Assert.Equal(profileId, result.BudgetProfileId);
        Assert.Equal("active", result.Status);
    }

    [Fact]
    public async Task FreeTier_ThrowsAppValidationException()
    {
        var user = UsUser("free");
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        await Assert.ThrowsAsync<AppValidationException>(() =>
            CreateHandler().Handle(new ExchangePublicTokenCommand(user.Id, Guid.NewGuid(), "public-token-sandbox"), CancellationToken.None));
    }
}
