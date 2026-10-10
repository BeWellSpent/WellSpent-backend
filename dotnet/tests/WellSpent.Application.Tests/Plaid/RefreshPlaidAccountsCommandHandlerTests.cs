using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Configuration;
using WellSpent.Application.Plaid;
using WellSpent.Application.Plaid.RefreshPlaidAccounts;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Tests.Plaid;

/// <summary>Mirrors internal/service/plaid_service_test.go's TestPlaid_RefreshAccounts_* suite.</summary>
public sealed class RefreshPlaidAccountsCommandHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();
    private readonly IPlaidItemRepository _items = Substitute.For<IPlaidItemRepository>();
    private readonly ITransactionRepository _transactions = Substitute.For<ITransactionRepository>();
    private readonly IPlaidClient _plaid = Substitute.For<IPlaidClient>();
    private readonly ICryptoService _crypto = Substitute.For<ICryptoService>();

    private RefreshPlaidAccountsCommandHandler CreateHandler() => new(
        new PlaidAccessGuard(_users), _items, _plaid, _crypto,
        Options.Create(new AuthOptions { EncryptionKey = "test-key" }),
        _transactions,
        new PlaidPaymentMethodSync(_profiles, _transactions, NullLogger<PlaidPaymentMethodSync>.Instance),
        NullLogger<RefreshPlaidAccountsCommandHandler>.Instance);

    private static User UsUser() => new() { Id = Guid.NewGuid(), Email = "u@example.com", CountryCode = "US", Plan = "lifetime" };

    [Fact]
    public async Task CreatesNewAndDeactivatesRemoved()
    {
        var user = UsUser();
        var profileId = Guid.NewGuid();
        var connId = Guid.NewGuid();

        // Plaid now reports only "acct-kept" and "acct-new" — "acct-removed"
        // (an existing payment method) is gone.
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _items.GetByIdAsync(connId, Arg.Any<CancellationToken>())
            .Returns(new PlaidItem { Id = connId, UserId = user.Id, BudgetProfileId = profileId, AccessToken = "encrypted", ItemId = "plaid-item-1" });
        _crypto.Decrypt("encrypted", "test-key").Returns("real-access-token");
        _plaid.GetAccountsAsync("real-access-token", Arg.Any<CancellationToken>()).Returns(new PlaidAccountsResult(
            [
                new PlaidLinkedAccount("acct-kept", "Checking", "", "depository", "checking"),
                new PlaidLinkedAccount("acct-new", "Savings", "", "depository", "savings"),
            ], "inst-1"));
        _profiles.GetPersonByUserIdAsync(profileId, user.Id, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 1, BudgetProfileId = profileId, UserId = user.Id, UserName = "u", Role = "admin" });

        _transactions.GetPaymentMethodByPlaidAccountIdAsync("acct-kept", Arg.Any<CancellationToken>())
            .Returns(new PaymentMethod { Id = Guid.NewGuid(), Name = "Checking", PlaidAccountId = "acct-kept" });
        _transactions.GetPaymentMethodByPlaidAccountIdAsync("acct-new", Arg.Any<CancellationToken>())
            .Returns((PaymentMethod?)null);
        _transactions.GetPaymentMethodByUserAndNameAsync(user.Id, "Savings", Arg.Any<CancellationToken>())
            .Returns((PaymentMethod?)null);

        var createdNames = new List<string>();
        _transactions.CreatePaymentMethodAsync(Arg.Any<PaymentMethod>(), Arg.Any<CancellationToken>())
            .Returns(info =>
            {
                var pm = (PaymentMethod)info[0];
                createdNames.Add(pm.Name);
                pm.Id = Guid.NewGuid();
                return Task.FromResult(pm);
            });

        _transactions.ListActivePaymentMethodsByPlaidItemIdAsync(connId, Arg.Any<CancellationToken>())
            .Returns(
            [
                new PaymentMethod { Id = Guid.NewGuid(), Name = "Old Removed Account", PlaidAccountId = "acct-removed" },
                new PaymentMethod { Id = Guid.NewGuid(), Name = "Checking", PlaidAccountId = "acct-kept" },
            ]);

        var deactivatedIds = new List<Guid>();
        _transactions.DeactivatePaymentMethodAsync(Arg.Do<Guid>(id => deactivatedIds.Add(id)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await CreateHandler().Handle(new RefreshPlaidAccountsCommand(user.Id, connId), CancellationToken.None);

        Assert.Equal(connId, result.Id);
        Assert.Equal(["Savings"], createdNames);
        Assert.Single(deactivatedIds);
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
            CreateHandler().Handle(new RefreshPlaidAccountsCommand(user.Id, connId), CancellationToken.None));
    }
}
