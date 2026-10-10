using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Configuration;
using WellSpent.Application.Plaid;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.Tests.Plaid;

/// <summary>Mirrors a representative subset of internal/service/plaid_sync_test.go's TestSyncItem_*/TestSyncAll_*/TestSyncProfile_* suite.</summary>
public sealed class PlaidSyncEngineTests
{
    private readonly IPlaidItemRepository _items = Substitute.For<IPlaidItemRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IBudgetProfileRepository _budgets = Substitute.For<IBudgetProfileRepository>();
    private readonly ITransactionRepository _transactions = Substitute.For<ITransactionRepository>();
    private readonly IFixedExpenseRepository _fixedExpenses = Substitute.For<IFixedExpenseRepository>();
    private readonly ITransactionReviewRepository _reviews = Substitute.For<ITransactionReviewRepository>();
    private readonly IPlaidClient _plaid = Substitute.For<IPlaidClient>();
    private readonly ICryptoService _crypto = Substitute.For<ICryptoService>();

    private PlaidSyncEngine CreateEngine() => new(
        _items, _users, _budgets, _transactions, _fixedExpenses, _reviews, _plaid, _crypto,
        Options.Create(new AuthOptions { EncryptionKey = "test-key" }),
        NullLogger<PlaidSyncEngine>.Instance);

    private static PlaidItem NewItem(Guid profileId) =>
        new() { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), BudgetProfileId = profileId, AccessToken = "encrypted", ItemId = "item-1" };

    /// <summary>Wires every dependency to a harmless default — individual tests override only what they need.</summary>
    private void SetUpDefaults(PlaidItem item, string plan = "pro")
    {
        _users.GetByIdAsync(item.UserId, Arg.Any<CancellationToken>())
            .Returns(new User { Id = item.UserId, Email = "owner@example.com", Plan = plan });
        _transactions.ListSystemCategoriesAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<string, int>());
        _crypto.Decrypt("encrypted", "test-key").Returns("real-access-token");
        // AutoUpdatePlannedAmount=false sidesteps the template-sync branch of
        // FixedExpensePaymentSync.MarkPaidAsync, which has its own dedicated
        // coverage elsewhere (B5 batch 5) — these tests are about the sync
        // engine's own branching, not template propagation.
        _budgets.GetByIdAsync(item.BudgetProfileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = item.BudgetProfileId, UserId = item.UserId, Name = "x", Cycle = "monthly", AutoUpdatePlannedAmount = false });
        _fixedExpenses.ListAsync(item.BudgetProfileId, Arg.Any<CancellationToken>()).Returns([]);
        _reviews.ListAliasesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        _transactions.GetTransactionByPlaidIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Transaction?)null);
        _transactions.ExistsTransactionByPlaidIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        _transactions.GetPaymentMethodByPlaidAccountIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((PaymentMethod?)null);
        _items.UpdateSyncAsync(item.Id, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(item);
        _transactions.CreateTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>())
            .Returns(info =>
            {
                var tx = (Transaction)info[0];
                tx.Id = Guid.NewGuid();
                return Task.FromResult(tx);
            });
    }

    private static PlaidImportedTransaction Tx(string plaidId, string name, decimal amount, DateOnly date, string accountId = "", string pendingId = "") =>
        new(plaidId, accountId, name, amount, date, "", "", "", "", pendingId);

    [Fact]
    public async Task CursorPersistFailure_SurfacesAsError()
    {
        var item = NewItem(Guid.NewGuid());
        SetUpDefaults(item);
        _plaid.SyncTransactionsAsync("real-access-token", "", Arg.Any<CancellationToken>())
            .Returns(new PlaidSyncResult([], [], [], "new-cursor"));
        _items.UpdateSyncAsync(item.Id, "new-cursor", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PlaidItem>(new Exception("connection reset")));

        var result = await CreateEngine().SyncItemAsync(item, CancellationToken.None);

        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task AutoConfirmedMatch_ExcludesInsteadOfDeleting()
    {
        var item = NewItem(Guid.NewGuid());
        SetUpDefaults(item);
        var periodId = Guid.NewGuid();
        var feId = Guid.NewGuid();
        var unpaidTxId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var period = new BudgetPeriod { Id = periodId, BudgetProfileId = item.BudgetProfileId, StartDate = today.AddDays(-10), EndDate = today.AddDays(10) };

        _plaid.SyncTransactionsAsync("real-access-token", "", Arg.Any<CancellationToken>())
            .Returns(new PlaidSyncResult([Tx("plaid-tx-1", "Patreon", 15.00m, today)], [], [], "new-cursor"));
        _budgets.GetPeriodByDateAsync(item.BudgetProfileId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(period);
        _fixedExpenses.ListAsync(item.BudgetProfileId, Arg.Any<CancellationToken>())
            .Returns([new FixedExpense { Id = feId, BudgetProfileId = item.BudgetProfileId, Name = "Creator Support", PlannedAmount = 15.00m }]);
        _reviews.ListAliasesAsync(feId, Arg.Any<CancellationToken>()).Returns(["Patreon"]);
        _fixedExpenses.GetUnpaidTransactionInPeriodAsync(feId, periodId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = unpaidTxId, BudgetPeriodId = periodId, PlannedAmount = 15.00m, Amount = 15.00m });

        Guid markedPaidId = Guid.Empty;
        _transactions.MarkTransactionAsPaidAsync(Arg.Any<Guid>(), periodId, 15.00m, today, Arg.Any<CancellationToken>())
            .Returns(info =>
            {
                markedPaidId = (Guid)info[0];
                return Task.FromResult(new Transaction { Id = markedPaidId, BudgetPeriodId = periodId });
            });

        var reviewId = Guid.NewGuid();
        Guid reviewTransactionId = Guid.Empty, reviewMatchedId = Guid.Empty;
        _reviews.UpsertAsync(periodId, Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(info =>
            {
                reviewTransactionId = (Guid)info[1];
                reviewMatchedId = (Guid)info[2];
                return Task.FromResult(new TransactionReview { Id = reviewId, BudgetPeriodId = periodId, TransactionId = reviewTransactionId, MatchedTransactionId = reviewMatchedId });
            });

        string? confirmedStatus = null;
        _reviews.UpdateStatusAsync(reviewId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(info =>
            {
                confirmedStatus = (string)info[1];
                return Task.CompletedTask;
            });

        Guid excludedId = Guid.Empty;
        bool excludedFlag = false;
        _transactions.SetTransactionExcludedAsync(Arg.Any<Guid>(), periodId, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(info =>
            {
                excludedId = (Guid)info[0];
                excludedFlag = (bool)info[2];
                return Task.FromResult(new Transaction { Id = excludedId, IsExcluded = excludedFlag });
            });

        var result = await CreateEngine().SyncItemAsync(item, CancellationToken.None);

        Assert.Equal(unpaidTxId, markedPaidId);
        Assert.Equal(reviewTransactionId, excludedId);
        Assert.True(excludedFlag);
        Assert.Equal(unpaidTxId, reviewMatchedId);
        Assert.Equal("confirmed", confirmedStatus);
        Assert.Equal(1, result.AutoConfirmed);
        await _transactions.DidNotReceive().DeleteTransactionByPlaidIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QueuesForReview_WhenScoreMeetsThresholdWithoutAliasAndAmount()
    {
        var item = NewItem(Guid.NewGuid());
        SetUpDefaults(item);
        var periodId = Guid.NewGuid();
        var feId = Guid.NewGuid();
        var unpaidTxId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var period = new BudgetPeriod { Id = periodId, BudgetProfileId = item.BudgetProfileId, StartDate = today.AddDays(-10), EndDate = today.AddDays(10) };

        // Name word-overlap (20) + payment method match (20) + category match
        // (20) = 60 — below the auto-confirm threshold (needs amount+alias)
        // but queuing only needs score >= 80, so push it over with the amount too.
        var catId = 5;
        var pmId = Guid.NewGuid();
        _plaid.SyncTransactionsAsync("real-access-token", "", Arg.Any<CancellationToken>())
            .Returns(new PlaidSyncResult([Tx("plaid-tx-1", "Amex Renewal Membership", 150.00m, today, accountId: "acct-1")], [], [], "new-cursor"));
        _budgets.GetPeriodByDateAsync(item.BudgetProfileId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(period);
        _transactions.GetPaymentMethodByPlaidAccountIdAsync("acct-1", Arg.Any<CancellationToken>())
            .Returns(new PaymentMethod { Id = pmId, Name = "Amex" });
        _fixedExpenses.ListAsync(item.BudgetProfileId, Arg.Any<CancellationToken>())
            .Returns([new FixedExpense { Id = feId, BudgetProfileId = item.BudgetProfileId, Name = "Renewal Membership", PlannedAmount = 150.00m, PaymentMethodId = pmId, CategoryId = catId }]);
        _fixedExpenses.GetUnpaidTransactionInPeriodAsync(feId, periodId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = unpaidTxId, BudgetPeriodId = periodId, PlannedAmount = 150.00m, Amount = 150.00m });

        var result = await CreateEngine().SyncItemAsync(item, CancellationToken.None);

        Assert.Equal(1, result.Queued);
        Assert.Equal(0, result.AutoConfirmed);
        await _reviews.Received(1).UpsertAsync(periodId, Arg.Any<Guid>(), unpaidTxId, Arg.Any<decimal>(), Arg.Any<CancellationToken>());
        await _transactions.DidNotReceive().MarkTransactionAsPaidAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<decimal>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SkipsNoPeriod()
    {
        var item = NewItem(Guid.NewGuid());
        SetUpDefaults(item);
        _plaid.SyncTransactionsAsync("real-access-token", "", Arg.Any<CancellationToken>())
            .Returns(new PlaidSyncResult([Tx("plaid-tx-1", "Mystery", 10.00m, DateOnly.FromDateTime(DateTime.Today))], [], [], "new-cursor"));
        _budgets.GetPeriodByDateAsync(item.BudgetProfileId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns((BudgetPeriod?)null);

        var result = await CreateEngine().SyncItemAsync(item, CancellationToken.None);

        Assert.Equal(1, result.SkippedNoPeriod);
        Assert.Equal(0, result.Imported);
        await _transactions.DidNotReceive().CreateTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SkipsDuplicateAlreadyImported()
    {
        var item = NewItem(Guid.NewGuid());
        SetUpDefaults(item);
        var periodId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.Today);
        _plaid.SyncTransactionsAsync("real-access-token", "", Arg.Any<CancellationToken>())
            .Returns(new PlaidSyncResult([Tx("plaid-tx-1", "Already There", 10.00m, today)], [], [], "new-cursor"));
        _budgets.GetPeriodByDateAsync(item.BudgetProfileId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = item.BudgetProfileId, StartDate = today.AddDays(-5), EndDate = today.AddDays(5) });
        _transactions.ExistsTransactionByPlaidIdAsync("plaid-tx-1", Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateEngine().SyncItemAsync(item, CancellationToken.None);

        Assert.Equal(1, result.SkippedDuplicate);
        Assert.Equal(0, result.Imported);
        await _transactions.DidNotReceive().CreateTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SkipsUnentitledOwner_ReportsRatherThanSwallows()
    {
        var item = NewItem(Guid.NewGuid());
        item.InstitutionName = "Chase";
        SetUpDefaults(item, plan: "free");

        var result = await CreateEngine().SyncItemAsync(item, CancellationToken.None);

        // Previously this returned a bare nil and logged one line, which is
        // how a connection sat unsynced for over two weeks without anyone noticing.
        Assert.True(result.SkippedUnentitled, "a free-tier owner's connection must be reported, not silently skipped");
        Assert.Null(result.Error);
        Assert.Equal("Chase", result.InstitutionName);
        await _plaid.DidNotReceive().SyncTransactionsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PendingTransactionSettles_ConfirmedReview_RepointsAndUpdatesPaidAmount()
    {
        var item = NewItem(Guid.NewGuid());
        SetUpDefaults(item);
        var periodId = Guid.NewGuid();
        var existingTxId = Guid.NewGuid();
        var matchedTxId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.Today);
        const string pendingPlaidId = "plaid-tx-pending";
        const string settledPlaidId = "plaid-tx-settled";

        _plaid.SyncTransactionsAsync("real-access-token", "", Arg.Any<CancellationToken>())
            .Returns(new PlaidSyncResult([Tx(settledPlaidId, "Coffee Shop", 17.50m, today, pendingId: pendingPlaidId)], [], [], "new-cursor"));
        _budgets.GetPeriodByDateAsync(item.BudgetProfileId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = item.BudgetProfileId, StartDate = today.AddDays(-5), EndDate = today.AddDays(5) });
        _transactions.GetTransactionByPlaidIdAsync(pendingPlaidId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = existingTxId, Amount = 15.00m });

        bool repointCalled = false;
        _transactions.RepointTransactionPlaidIdAsync(pendingPlaidId, settledPlaidId, Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                repointCalled = true;
                return Task.FromResult(new Transaction { Id = existingTxId, Amount = 17.50m });
            });

        _reviews.GetByTransactionIdAsync(existingTxId, Arg.Any<CancellationToken>())
            .Returns(new TransactionReview { Status = "confirmed", TransactionId = existingTxId, MatchedTransactionId = matchedTxId });
        _transactions.GetTransactionAsync(matchedTxId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = matchedTxId, BudgetPeriodId = periodId, PaidDate = today });

        decimal markPaidAmount = 0;
        Guid markPaidId = Guid.Empty;
        _transactions.MarkTransactionAsPaidAsync(Arg.Any<Guid>(), periodId, Arg.Any<decimal>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(info =>
            {
                markPaidId = (Guid)info[0];
                markPaidAmount = (decimal)info[2];
                return Task.FromResult(new Transaction { Id = markPaidId });
            });

        // No fixed expenses to score against — this transaction is already settled, not a fresh import.
        _fixedExpenses.ListAsync(item.BudgetProfileId, Arg.Any<CancellationToken>()).Returns([]);

        var result = await CreateEngine().SyncItemAsync(item, CancellationToken.None);

        Assert.True(repointCalled);
        Assert.Equal(1, result.Repointed);
        Assert.Equal(matchedTxId, markPaidId);
        Assert.Equal(17.50m, markPaidAmount);
        await _transactions.DidNotReceive().ExistsTransactionByPlaidIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _transactions.DidNotReceive().CreateTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PendingTransactionSettles_NoReview_JustRepoints()
    {
        var item = NewItem(Guid.NewGuid());
        SetUpDefaults(item);
        var periodId = Guid.NewGuid();
        var existingTxId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.Today);
        const string pendingPlaidId = "plaid-tx-pending";
        const string settledPlaidId = "plaid-tx-settled";

        _plaid.SyncTransactionsAsync("real-access-token", "", Arg.Any<CancellationToken>())
            .Returns(new PlaidSyncResult([Tx(settledPlaidId, "Coffee Shop", 17.50m, today, pendingId: pendingPlaidId)], [], [], "new-cursor"));
        _budgets.GetPeriodByDateAsync(item.BudgetProfileId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = item.BudgetProfileId, StartDate = today.AddDays(-5), EndDate = today.AddDays(5) });
        _transactions.GetTransactionByPlaidIdAsync(pendingPlaidId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = existingTxId, Amount = 15.00m });
        _transactions.RepointTransactionPlaidIdAsync(pendingPlaidId, settledPlaidId, Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = existingTxId, Amount = 17.50m });
        _reviews.GetByTransactionIdAsync(existingTxId, Arg.Any<CancellationToken>()).Returns((TransactionReview?)null);

        var result = await CreateEngine().SyncItemAsync(item, CancellationToken.None);

        Assert.Equal(1, result.Repointed);
        await _transactions.DidNotReceive().MarkTransactionAsPaidAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<decimal>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ModifiedAndRemoved_UpdateAndDelete()
    {
        var item = NewItem(Guid.NewGuid());
        SetUpDefaults(item);
        _plaid.SyncTransactionsAsync("real-access-token", "", Arg.Any<CancellationToken>())
            .Returns(new PlaidSyncResult([], [Tx("plaid-mod-1", "Updated Name", 25.00m, DateOnly.FromDateTime(DateTime.Today))], ["plaid-removed-1"], "new-cursor"));

        var result = await CreateEngine().SyncItemAsync(item, CancellationToken.None);

        Assert.Equal(1, result.Modified);
        Assert.Equal(1, result.Removed);
        await _transactions.Received(1).UpdateTransactionFromPlaidAsync("plaid-mod-1", "Updated Name", 25.00m, Arg.Any<CancellationToken>());
        await _transactions.Received(1).DeleteTransactionByPlaidIdAsync("plaid-removed-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncAll_GroupsConnectionsByBudgetProfile()
    {
        var profileA = Guid.NewGuid();
        var profileB = Guid.NewGuid();
        var itemA1 = NewItem(profileA);
        var itemB1 = NewItem(profileB);
        var itemA2 = NewItem(profileA);
        foreach (var it in new[] { itemA1, itemB1, itemA2 })
        {
            SetUpDefaults(it);
        }

        _items.ListActiveForSyncAsync(Arg.Any<CancellationToken>()).Returns([itemA1, itemB1, itemA2]);
        _plaid.SyncTransactionsAsync("real-access-token", "", Arg.Any<CancellationToken>())
            .Returns(new PlaidSyncResult([], [], [], "new-cursor"));

        var profiles = await CreateEngine().SyncAllAsync(CancellationToken.None);

        Assert.Equal(2, profiles.Count);
        var byProfile = profiles.ToDictionary(p => p.ProfileId, p => p.Items.Count);
        Assert.Equal(2, byProfile[profileA]);
        Assert.Equal(1, byProfile[profileB]);
    }

    [Fact]
    public async Task SyncProfile_QueriesOnlyThatProfilesConnections()
    {
        var profileId = Guid.NewGuid();
        var item = NewItem(profileId);
        SetUpDefaults(item);
        Guid gotProfileId = Guid.Empty;
        _items.ListActiveForProfileSyncAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(info =>
            {
                gotProfileId = (Guid)info[0];
                return Task.FromResult(new List<PlaidItem> { item });
            });
        _plaid.SyncTransactionsAsync("real-access-token", "", Arg.Any<CancellationToken>())
            .Returns(new PlaidSyncResult([], [], [], "new-cursor"));

        var result = await CreateEngine().SyncProfileAsync(profileId, CancellationToken.None);

        Assert.Equal(profileId, gotProfileId);
        Assert.Equal(profileId, result.ProfileId);
        Assert.Single(result.Items);
    }
}
