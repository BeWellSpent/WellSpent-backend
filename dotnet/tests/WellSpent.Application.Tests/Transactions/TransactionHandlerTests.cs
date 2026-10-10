using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Common;
using WellSpent.Application.Transactions.CreateTransaction;
using WellSpent.Application.Transactions.DeleteTransaction;
using WellSpent.Application.Transactions.ListTransactions;
using WellSpent.Application.Transactions.MarkTransactionAsPaid;
using WellSpent.Application.Transactions.SetTransactionExcluded;
using WellSpent.Application.Transactions.UnmarkTransactionAsPaid;
using WellSpent.Application.Transactions.UpdateTransaction;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Transactions;

public sealed class TransactionHandlerTests
{
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();
    private readonly ITransactionRepository _transactions = Substitute.For<ITransactionRepository>();
    private readonly IFixedExpenseRepository _fixedExpenses = Substitute.For<IFixedExpenseRepository>();
    private readonly ITransactionReviewRepository _reviews = Substitute.For<ITransactionReviewRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private BudgetAccessGuard Access => new(_profiles);

    private CreateTransactionCommandHandler CreateCreateHandler() => new(
        Access, _transactions, _profiles, _fixedExpenses, _reviews, _users, NullLogger<CreateTransactionCommandHandler>.Instance);

    private UpdateTransactionCommandHandler CreateUpdateHandler() => new(
        Access, _transactions, _profiles, _fixedExpenses, _reviews, _users, NullLogger<UpdateTransactionCommandHandler>.Instance);

    private UnmarkTransactionAsPaidCommandHandler CreateUnmarkHandler() => new(
        Access, _transactions, _reviews, NullLogger<UnmarkTransactionAsPaidCommandHandler>.Instance);

    private (Guid AdminId, Guid ProfileId, Guid PeriodId) SetUpOpenPeriod()
    {
        var profileId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, IsArchived = false, StartDate = new DateOnly(2026, 2, 1), EndDate = new DateOnly(2026, 2, 28) });
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = adminId, Name = "x" });
        return (adminId, profileId, periodId);
    }

    // ── List ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_NonMember_ThrowsForbidden_NeverNotFound()
    {
        var (_, profileId, periodId) = SetUpOpenPeriod();
        var strangerId = Guid.NewGuid();
        _profiles.GetPersonByUserIdAsync(profileId, strangerId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BudgetPerson>(new NotFoundException("budget_person", strangerId.ToString())));

        await Assert.ThrowsAsync<ForbiddenException>(() => new ListTransactionsQueryHandler(Access, _transactions, _profiles)
            .Handle(new ListTransactionsQuery(strangerId, periodId, null, null, false), CancellationToken.None));
    }

    [Fact]
    public async Task List_FocusedView_ResolvesCallerPersonId()
    {
        var (adminId, profileId, periodId) = SetUpOpenPeriod();
        _profiles.GetPersonByUserIdAsync(profileId, adminId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 7, BudgetProfileId = profileId, UserId = adminId, Role = "admin" });
        _transactions.ListTransactionsAsync(periodId, null, null, 7, Arg.Any<CancellationToken>()).Returns([]);

        await new ListTransactionsQueryHandler(Access, _transactions, _profiles)
            .Handle(new ListTransactionsQuery(adminId, periodId, null, null, true), CancellationToken.None);

        await _transactions.Received(1).ListTransactionsAsync(periodId, null, null, 7, Arg.Any<CancellationToken>());
    }

    // ── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ArchivedPeriod_ThrowsForbidden()
    {
        var (adminId, profileId, periodId) = SetUpOpenPeriod();
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, IsArchived = true });

        await Assert.ThrowsAsync<ForbiddenException>(() => CreateCreateHandler()
            .Handle(new CreateTransactionCommand(adminId, "x", new Money(10, 0), new Money(10, 0), new DateOnly(2026, 2, 5), null,
                periodId, null, null, null, "variable"), CancellationToken.None));
    }

    [Fact]
    public async Task Create_VariableBackdated_Throws()
    {
        var (adminId, _, periodId) = SetUpOpenPeriod();

        await Assert.ThrowsAsync<AppValidationException>(() => CreateCreateHandler()
            .Handle(new CreateTransactionCommand(adminId, "x", new Money(10, 0), new Money(10, 0), new DateOnly(2026, 1, 1), null,
                periodId, null, null, null, "variable"), CancellationToken.None));
    }

    [Fact]
    public async Task Create_FixedBackdated_Allowed()
    {
        var (adminId, _, periodId) = SetUpOpenPeriod();
        _transactions.CreateTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<Transaction>());

        var result = await CreateCreateHandler()
            .Handle(new CreateTransactionCommand(adminId, "Rent", new Money(10, 0), new Money(10, 0), new DateOnly(2026, 1, 1), null,
                periodId, null, null, null, "fixed"), CancellationToken.None);

        Assert.Equal("Rent", result.Name);
    }

    [Fact]
    public async Task Create_NoPeriod_SkipsAccessChecks()
    {
        var userId = Guid.NewGuid();
        _transactions.CreateTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<Transaction>());

        var result = await CreateCreateHandler()
            .Handle(new CreateTransactionCommand(userId, "Orphan", new Money(5, 0), new Money(5, 0), null, null,
                null, null, null, null, null), CancellationToken.None);

        Assert.Equal("Orphan", result.Name);
    }

    // ── Manual match review (HOOK completed B5 batch 7) ─────────────────────

    private void SetUpManualMatchEligible(Guid profileId, Guid userId)
    {
        _profiles.GetPersonByUserIdAsync(profileId, userId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 1, BudgetProfileId = profileId, UserId = userId, Role = "admin", ManualMatchReviewEnabled = true });
        _users.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new User { Id = userId, Email = "a@b.com", Plan = "pro" });
    }

    [Fact]
    public async Task Create_Variable_QueuesReview_WhenScoreOver80()
    {
        var (adminId, profileId, periodId) = SetUpOpenPeriod();
        SetUpManualMatchEligible(profileId, adminId);
        var feId = Guid.NewGuid();
        var unpaidId = Guid.NewGuid();
        const int catId = 9; // matching category pushes the score to 80 (amount 40 + name 20 + category 20)
        _fixedExpenses.ListAsync(profileId, Arg.Any<CancellationToken>())
            .Returns([new FixedExpense { Id = feId, BudgetProfileId = profileId, Name = "Netflix", PlannedAmount = 15.49m, CategoryId = catId }]);
        _reviews.ListAliasesAsync(feId, Arg.Any<CancellationToken>()).Returns([]);
        _reviews.GetByTransactionIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((TransactionReview?)null);
        _fixedExpenses.GetUnpaidTransactionInPeriodAsync(feId, periodId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = unpaidId, BudgetPeriodId = periodId });
        _transactions.CreateTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>())
            .Returns(ci => { var t = ci.Arg<Transaction>(); t.Id = Guid.NewGuid(); return t; });

        var result = await CreateCreateHandler().Handle(new CreateTransactionCommand(
            adminId, "Netflix", new Money(15, 490_000_000), new Money(15, 490_000_000), new DateOnly(2026, 2, 10), null,
            periodId, catId, null, null, "variable"), CancellationToken.None);

        await _reviews.Received(1).UpsertAsync(periodId, result.Id, unpaidId, 80m, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_Variable_NoReview_WhenScoreUnder80()
    {
        var (adminId, profileId, periodId) = SetUpOpenPeriod();
        SetUpManualMatchEligible(profileId, adminId);
        _fixedExpenses.ListAsync(profileId, Arg.Any<CancellationToken>())
            .Returns([new FixedExpense { Id = Guid.NewGuid(), BudgetProfileId = profileId, Name = "Rent", PlannedAmount = 1500m }]);
        _reviews.ListAliasesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        _reviews.GetByTransactionIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((TransactionReview?)null);
        _transactions.CreateTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>())
            .Returns(ci => { var t = ci.Arg<Transaction>(); t.Id = Guid.NewGuid(); return t; });

        await CreateCreateHandler().Handle(new CreateTransactionCommand(
            adminId, "Coffee Shop", new Money(4, 0), new Money(4, 0), new DateOnly(2026, 2, 10), null,
            periodId, null, null, null, "variable"), CancellationToken.None);

        await _reviews.DidNotReceive().UpsertAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_Fixed_NeverQueuesReview()
    {
        var (adminId, profileId, periodId) = SetUpOpenPeriod();
        SetUpManualMatchEligible(profileId, adminId);
        _transactions.CreateTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>())
            .Returns(ci => { var t = ci.Arg<Transaction>(); t.Id = Guid.NewGuid(); return t; });

        await CreateCreateHandler().Handle(new CreateTransactionCommand(
            adminId, "Netflix", new Money(15, 0), new Money(15, 0), new DateOnly(2026, 2, 10), null,
            periodId, null, null, null, "fixed"), CancellationToken.None);

        await _fixedExpenses.DidNotReceive().ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_Variable_NoReview_WhenActorDisabledThePreference()
    {
        var (adminId, profileId, periodId) = SetUpOpenPeriod();
        _profiles.GetPersonByUserIdAsync(profileId, adminId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 1, BudgetProfileId = profileId, UserId = adminId, Role = "admin", ManualMatchReviewEnabled = false });
        _transactions.CreateTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>())
            .Returns(ci => { var t = ci.Arg<Transaction>(); t.Id = Guid.NewGuid(); return t; });

        await CreateCreateHandler().Handle(new CreateTransactionCommand(
            adminId, "Netflix", new Money(15, 0), new Money(15, 0), new DateOnly(2026, 2, 10), null,
            periodId, null, null, null, "variable"), CancellationToken.None);

        await _fixedExpenses.DidNotReceive().ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_Variable_NoReview_WhenActorIsFreeTier()
    {
        var (adminId, profileId, periodId) = SetUpOpenPeriod();
        _profiles.GetPersonByUserIdAsync(profileId, adminId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 1, BudgetProfileId = profileId, UserId = adminId, Role = "admin", ManualMatchReviewEnabled = true });
        _users.GetByIdAsync(adminId, Arg.Any<CancellationToken>()).Returns(new User { Id = adminId, Email = "a@b.com", Plan = "free" });
        _transactions.CreateTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>())
            .Returns(ci => { var t = ci.Arg<Transaction>(); t.Id = Guid.NewGuid(); return t; });

        await CreateCreateHandler().Handle(new CreateTransactionCommand(
            adminId, "Netflix", new Money(15, 0), new Money(15, 0), new DateOnly(2026, 2, 10), null,
            periodId, null, null, null, "variable"), CancellationToken.None);

        await _fixedExpenses.DidNotReceive().ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_Variable_RemovesStalePendingReview_WhenNoLongerMatches()
    {
        var (adminId, profileId, periodId) = SetUpOpenPeriod();
        SetUpManualMatchEligible(profileId, adminId);
        var existing = new Transaction { Id = Guid.NewGuid(), Amount = 4m, PlannedAmount = 4m, BudgetPeriodId = periodId, TransactionTypeId = 2 };
        _transactions.GetTransactionAsync(existing.Id, Arg.Any<CancellationToken>()).Returns(existing);
        // The real repository returns the full persisted row — BudgetPeriodId
        // included, even though it isn't part of the editable field set — so
        // the mock must preserve it too, not just echo the submitted edit.
        _transactions.UpdateTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>())
            .Returns(ci => { var t = ci.Arg<Transaction>(); t.BudgetPeriodId = periodId; return t; });
        _fixedExpenses.ListAsync(profileId, Arg.Any<CancellationToken>())
            .Returns([new FixedExpense { Id = Guid.NewGuid(), BudgetProfileId = profileId, Name = "Rent", PlannedAmount = 1500m }]);
        _reviews.ListAliasesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        var staleReviewId = Guid.NewGuid();
        _reviews.GetByTransactionIdAsync(existing.Id, Arg.Any<CancellationToken>())
            .Returns(new TransactionReview { Id = staleReviewId, BudgetPeriodId = periodId, TransactionId = existing.Id, MatchedTransactionId = Guid.NewGuid(), Status = "pending" });

        await CreateUpdateHandler().Handle(
            new UpdateTransactionCommand(adminId, existing.Id, "Coffee Shop", new Money(4, 0), new Money(4, 0), new DateOnly(2026, 2, 10), null, null, null, "variable"),
            CancellationToken.None);

        await _reviews.Received(1).DeleteIfPendingAsync(staleReviewId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_Variable_DoesNotReopenConfirmedReview()
    {
        var (adminId, profileId, periodId) = SetUpOpenPeriod();
        SetUpManualMatchEligible(profileId, adminId);
        var existing = new Transaction { Id = Guid.NewGuid(), Amount = 4m, PlannedAmount = 4m, BudgetPeriodId = periodId, TransactionTypeId = 2 };
        _transactions.GetTransactionAsync(existing.Id, Arg.Any<CancellationToken>()).Returns(existing);
        _transactions.UpdateTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>())
            .Returns(ci => { var t = ci.Arg<Transaction>(); t.BudgetPeriodId = periodId; return t; });
        _reviews.GetByTransactionIdAsync(existing.Id, Arg.Any<CancellationToken>())
            .Returns(new TransactionReview { Id = Guid.NewGuid(), BudgetPeriodId = periodId, TransactionId = existing.Id, MatchedTransactionId = Guid.NewGuid(), Status = "confirmed" });

        await CreateUpdateHandler().Handle(
            new UpdateTransactionCommand(adminId, existing.Id, "Coffee Shop", new Money(4, 0), new Money(4, 0), new DateOnly(2026, 2, 10), null, null, null, "variable"),
            CancellationToken.None);

        await _fixedExpenses.DidNotReceive().ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _reviews.DidNotReceive().DeleteIfPendingAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ── Update ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ArchivedPeriod_CategoryOnlyChange_Allowed()
    {
        var (adminId, profileId, periodId) = SetUpOpenPeriod();
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, IsArchived = true, StartDate = new DateOnly(2026, 2, 1), EndDate = new DateOnly(2026, 2, 28) });
        var existing = new Transaction
        {
            Id = Guid.NewGuid(), Name = "Rent", Amount = 100m, PlannedAmount = 100m,
            Date = new DateOnly(2026, 2, 5), BudgetPeriodId = periodId, CategoryId = 1,
        };
        _transactions.GetTransactionAsync(existing.Id, Arg.Any<CancellationToken>()).Returns(existing);
        _transactions.UpdateTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<Transaction>());

        var result = await CreateUpdateHandler().Handle(
            new UpdateTransactionCommand(adminId, existing.Id, "Rent", new Money(100, 0), new Money(100, 0), new DateOnly(2026, 2, 5), 99, null, null, null),
            CancellationToken.None);

        Assert.Equal(99, result.CategoryId);
    }

    [Fact]
    public async Task Update_ArchivedPeriod_AmountChange_Throws()
    {
        var (adminId, profileId, periodId) = SetUpOpenPeriod();
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, IsArchived = true });
        var existing = new Transaction { Id = Guid.NewGuid(), Amount = 100m, PlannedAmount = 100m, BudgetPeriodId = periodId };
        _transactions.GetTransactionAsync(existing.Id, Arg.Any<CancellationToken>()).Returns(existing);

        await Assert.ThrowsAsync<AppValidationException>(() => CreateUpdateHandler().Handle(
            new UpdateTransactionCommand(adminId, existing.Id, null, new Money(500, 0), new Money(100, 0), null, null, null, null, null),
            CancellationToken.None));
    }

    [Fact]
    public async Task Update_PlaidImported_OnOpenPeriod_StillCategoryOnly()
    {
        var (adminId, profileId, periodId) = SetUpOpenPeriod();
        var existing = new Transaction
        {
            Id = Guid.NewGuid(), Amount = 100m, PlannedAmount = 100m, BudgetPeriodId = periodId,
            PlaidTransactionId = "plaid-tx-1", CategoryId = 1,
        };
        _transactions.GetTransactionAsync(existing.Id, Arg.Any<CancellationToken>()).Returns(existing);
        _transactions.UpdateTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<Transaction>());

        var result = await CreateUpdateHandler().Handle(
            new UpdateTransactionCommand(adminId, existing.Id, null, new Money(100, 0), new Money(100, 0), null, 42, null, null, null),
            CancellationToken.None);

        Assert.Equal(42, result.CategoryId);
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_PlaidImported_Throws()
    {
        var (adminId, _, periodId) = SetUpOpenPeriod();
        var tx = new Transaction { Id = Guid.NewGuid(), BudgetPeriodId = periodId, PlaidTransactionId = "plaid-1" };
        _transactions.GetTransactionAsync(tx.Id, Arg.Any<CancellationToken>()).Returns(tx);

        await Assert.ThrowsAsync<AppValidationException>(() => new DeleteTransactionCommandHandler(Access, _transactions)
            .Handle(new DeleteTransactionCommand(adminId, tx.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_ArchivedPeriod_ThrowsForbidden()
    {
        var (adminId, profileId, periodId) = SetUpOpenPeriod();
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, IsArchived = true });
        var tx = new Transaction { Id = Guid.NewGuid(), BudgetPeriodId = periodId };
        _transactions.GetTransactionAsync(tx.Id, Arg.Any<CancellationToken>()).Returns(tx);

        await Assert.ThrowsAsync<ForbiddenException>(() => new DeleteTransactionCommandHandler(Access, _transactions)
            .Handle(new DeleteTransactionCommand(adminId, tx.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_Normal_Succeeds()
    {
        var (adminId, _, periodId) = SetUpOpenPeriod();
        var tx = new Transaction { Id = Guid.NewGuid(), BudgetPeriodId = periodId };
        _transactions.GetTransactionAsync(tx.Id, Arg.Any<CancellationToken>()).Returns(tx);

        await new DeleteTransactionCommandHandler(Access, _transactions).Handle(new DeleteTransactionCommand(adminId, tx.Id), CancellationToken.None);

        await _transactions.Received(1).DeleteTransactionAsync(tx.Id, periodId, Arg.Any<CancellationToken>());
    }

    // ── MarkAsPaid / UnmarkAsPaid / SetExcluded ──────────────────────────────

    [Fact]
    public async Task MarkAsPaid_ArchivedPeriod_ThrowsForbidden()
    {
        var (adminId, profileId, periodId) = SetUpOpenPeriod();
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, IsArchived = true });

        await Assert.ThrowsAsync<ForbiddenException>(() => new MarkTransactionAsPaidCommandHandler(Access, _transactions, _fixedExpenses, _profiles)
            .Handle(new MarkTransactionAsPaidCommand(adminId, Guid.NewGuid(), periodId, new Money(50, 0), new DateOnly(2026, 2, 10)), CancellationToken.None));
    }

    [Fact]
    public async Task MarkAsPaid_Succeeds()
    {
        var (adminId, _, periodId) = SetUpOpenPeriod();
        var txId = Guid.NewGuid();
        _transactions.MarkTransactionAsPaidAsync(txId, periodId, 50m, new DateOnly(2026, 2, 10), Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = txId, Amount = 50m, PlannedAmount = 60m, IsPaid = true, PaidDate = new DateOnly(2026, 2, 10) });

        var result = await new MarkTransactionAsPaidCommandHandler(Access, _transactions, _fixedExpenses, _profiles)
            .Handle(new MarkTransactionAsPaidCommand(adminId, txId, periodId, new Money(50, 0), new DateOnly(2026, 2, 10)), CancellationToken.None);

        Assert.True(result.IsPaid);
    }

    [Fact]
    public async Task MarkAsPaid_LinkedToFixedExpense_AutoUpdateOn_SyncsTemplateToWhatWasActuallyPaid()
    {
        var (adminId, profileId, periodId) = SetUpOpenPeriod();
        var txId = Guid.NewGuid();
        var feId = Guid.NewGuid();
        var categoryId = 7;
        var paymentMethodId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = adminId, Name = "B", Cycle = "monthly", AutoUpdatePlannedAmount = true });
        _transactions.MarkTransactionAsPaidAsync(txId, periodId, 65m, new DateOnly(2026, 2, 12), Arg.Any<CancellationToken>())
            .Returns(new Transaction
            {
                Id = txId, Amount = 65m, PlannedAmount = 60m, IsPaid = true, PaidDate = new DateOnly(2026, 2, 12),
                FixedExpenseId = feId, CategoryId = categoryId, PaymentMethodId = paymentMethodId,
            });
        _fixedExpenses.GetByIdAsync(feId, Arg.Any<CancellationToken>())
            .Returns(new FixedExpense { Id = feId, BudgetProfileId = profileId, Name = "Rent", PlannedAmount = 60m });

        await new MarkTransactionAsPaidCommandHandler(Access, _transactions, _fixedExpenses, _profiles)
            .Handle(new MarkTransactionAsPaidCommand(adminId, txId, periodId, new Money(65, 0), new DateOnly(2026, 2, 12)), CancellationToken.None);

        await _fixedExpenses.Received(1).UpdateFromPaymentAsync(
            feId, 65m, 12, Arg.Any<int>(), new DateOnly(2026, 2, 12), categoryId, paymentMethodId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkAsPaid_LinkedToFixedExpense_AutoUpdateOff_DoesNotSyncTemplate()
    {
        var (adminId, profileId, periodId) = SetUpOpenPeriod();
        var txId = Guid.NewGuid();
        var feId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = adminId, Name = "B", Cycle = "monthly", AutoUpdatePlannedAmount = false });
        _transactions.MarkTransactionAsPaidAsync(txId, periodId, 65m, new DateOnly(2026, 2, 12), Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = txId, Amount = 65m, IsPaid = true, FixedExpenseId = feId });

        await new MarkTransactionAsPaidCommandHandler(Access, _transactions, _fixedExpenses, _profiles)
            .Handle(new MarkTransactionAsPaidCommand(adminId, txId, periodId, new Money(65, 0), new DateOnly(2026, 2, 12)), CancellationToken.None);

        await _fixedExpenses.DidNotReceive().UpdateFromPaymentAsync(
            Arg.Any<Guid>(), Arg.Any<decimal>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<DateOnly?>(), Arg.Any<int?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkAsPaid_TemplateSyncFailure_IsNonFatal_PaymentStillReturned()
    {
        var (adminId, profileId, periodId) = SetUpOpenPeriod();
        var txId = Guid.NewGuid();
        var feId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = adminId, Name = "B", Cycle = "monthly", AutoUpdatePlannedAmount = true });
        _transactions.MarkTransactionAsPaidAsync(txId, periodId, 65m, new DateOnly(2026, 2, 12), Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = txId, Amount = 65m, IsPaid = true, FixedExpenseId = feId });
        _fixedExpenses.GetByIdAsync(feId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<FixedExpense>(new NotFoundException("fixed_expense", feId.ToString())));

        var result = await new MarkTransactionAsPaidCommandHandler(Access, _transactions, _fixedExpenses, _profiles)
            .Handle(new MarkTransactionAsPaidCommand(adminId, txId, periodId, new Money(65, 0), new DateOnly(2026, 2, 12)), CancellationToken.None);

        Assert.True(result.IsPaid);
    }

    [Fact]
    public async Task UnmarkAsPaid_ArchivedPeriod_ThrowsForbidden()
    {
        var (adminId, profileId, periodId) = SetUpOpenPeriod();
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, IsArchived = true });

        await Assert.ThrowsAsync<ForbiddenException>(() => CreateUnmarkHandler()
            .Handle(new UnmarkTransactionAsPaidCommand(adminId, Guid.NewGuid(), periodId), CancellationToken.None));
    }

    [Fact]
    public async Task UnmarkAsPaid_ResetsConfirmedReview_DropsAliasAndUnexcludes()
    {
        var (adminId, _, periodId) = SetUpOpenPeriod();
        var txId = Guid.NewGuid();
        var feId = Guid.NewGuid();
        var importedTxId = Guid.NewGuid();
        var reviewId = Guid.NewGuid();
        _transactions.UnmarkTransactionAsPaidAsync(txId, periodId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = txId, FixedExpenseId = feId });
        _reviews.ListByMatchedTransactionIdAsync(txId, Arg.Any<CancellationToken>())
            .Returns([new TransactionReview { Id = reviewId, BudgetPeriodId = periodId, TransactionId = importedTxId, MatchedTransactionId = txId, Status = "confirmed" }]);
        _transactions.GetTransactionAsync(importedTxId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = importedTxId, Name = "Netflix.com" });

        await CreateUnmarkHandler().Handle(new UnmarkTransactionAsPaidCommand(adminId, txId, periodId), CancellationToken.None);

        await _reviews.Received(1).DeleteAliasAsync(feId, "Netflix.com", Arg.Any<CancellationToken>());
        await _transactions.Received(1).SetTransactionExcludedAsync(importedTxId, periodId, false, Arg.Any<CancellationToken>());
        await _reviews.Received(1).ResetConfirmedByMatchedTransactionAsync(txId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnmarkAsPaid_NoConfirmedReviews_SkipsReset()
    {
        var (adminId, _, periodId) = SetUpOpenPeriod();
        var txId = Guid.NewGuid();
        _transactions.UnmarkTransactionAsPaidAsync(txId, periodId, Arg.Any<CancellationToken>()).Returns(new Transaction { Id = txId });
        _reviews.ListByMatchedTransactionIdAsync(txId, Arg.Any<CancellationToken>())
            .Returns([new TransactionReview { Id = Guid.NewGuid(), BudgetPeriodId = periodId, TransactionId = Guid.NewGuid(), MatchedTransactionId = txId, Status = "pending" }]);

        await CreateUnmarkHandler().Handle(new UnmarkTransactionAsPaidCommand(adminId, txId, periodId), CancellationToken.None);

        await _reviews.DidNotReceive().ResetConfirmedByMatchedTransactionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetExcluded_Viewer_ThrowsForbidden()
    {
        var (_, profileId, periodId) = SetUpOpenPeriod();
        var viewerId = Guid.NewGuid();
        _profiles.GetPersonByUserIdAsync(profileId, viewerId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 2, BudgetProfileId = profileId, UserId = viewerId, Role = "viewer" });

        await Assert.ThrowsAsync<ForbiddenException>(() => new SetTransactionExcludedCommandHandler(Access, _transactions)
            .Handle(new SetTransactionExcludedCommand(viewerId, Guid.NewGuid(), periodId, true), CancellationToken.None));
    }

    [Fact]
    public async Task SetExcluded_Collaborator_Succeeds()
    {
        var (adminId, _, periodId) = SetUpOpenPeriod();
        var txId = Guid.NewGuid();
        _transactions.SetTransactionExcludedAsync(txId, periodId, true, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = txId, IsExcluded = true });

        var result = await new SetTransactionExcludedCommandHandler(Access, _transactions)
            .Handle(new SetTransactionExcludedCommand(adminId, txId, periodId, true), CancellationToken.None);

        Assert.True(result.IsExcluded);
    }
}
