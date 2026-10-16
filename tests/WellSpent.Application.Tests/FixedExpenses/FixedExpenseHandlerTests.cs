using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Common;
using WellSpent.Application.FixedExpenses;
using WellSpent.Application.FixedExpenses.CreateFixedExpense;
using WellSpent.Application.FixedExpenses.CreateFixedExpenseFromTransaction;
using WellSpent.Application.FixedExpenses.CreateInstallmentPlan;
using WellSpent.Application.FixedExpenses.DeleteFixedExpense;
using WellSpent.Application.FixedExpenses.DeleteInstallmentPlan;
using WellSpent.Application.FixedExpenses.ListFixedExpenses;
using WellSpent.Application.FixedExpenses.UpdateFixedExpense;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.FixedExpenses;

public sealed class FixedExpenseHandlerTests
{
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();
    private readonly ITransactionRepository _transactions = Substitute.For<ITransactionRepository>();
    private readonly IFixedExpenseRepository _fixedExpenses = Substitute.For<IFixedExpenseRepository>();
    private readonly ITransactionReviewRepository _reviews = Substitute.For<ITransactionReviewRepository>();
    private BudgetAccessGuard Access => new(_profiles);

    private UpdateFixedExpenseCommandHandler CreateUpdateFixedExpenseHandler() => new(
        Access, _fixedExpenses, _profiles, _transactions, _reviews, NullLogger<UpdateFixedExpenseCommandHandler>.Instance);

    private (Guid AdminId, Guid ProfileId) SetUpProfile()
    {
        var profileId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = adminId, Name = "B", Cycle = "monthly" });
        return (adminId, profileId);
    }

    private static FixedExpenseFields Fields(decimal amount = 60m, int dayOfMonth = 15, DateOnly? anchorDate = null, string frequencyUnit = "month") =>
        new("Rent", Money.FromDecimal(amount), 3, Guid.NewGuid(), dayOfMonth, 1, anchorDate, frequencyUnit, 1, 1, null, 0);

    // ── ListFixedExpenses ─────────────────────────────────────────────────

    [Fact]
    public async Task List_NonMember_ThrowsNotFound()
    {
        var (_, profileId) = SetUpProfile();
        var strangerId = Guid.NewGuid();
        _profiles.GetPersonByUserIdAsync(profileId, strangerId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BudgetPerson>(new NotFoundException("budget_person", strangerId.ToString())));

        // EnsureMemberAsync (unlike EnsureAdmin/EnsureCollaboratorOrAbove) does not
        // collapse a non-member's lookup failure into Forbidden — it propagates as-is.
        await Assert.ThrowsAsync<NotFoundException>(() => new ListFixedExpensesQueryHandler(Access, _fixedExpenses)
            .Handle(new ListFixedExpensesQuery(strangerId, profileId), CancellationToken.None));
    }

    [Fact]
    public async Task List_Member_ReturnsComputedNextDueDateAndPaymentsMade()
    {
        var (adminId, profileId) = SetUpProfile();
        _fixedExpenses.ListAsync(profileId, Arg.Any<CancellationToken>()).Returns([
            new FixedExpense { Id = Guid.NewGuid(), BudgetProfileId = profileId, Name = "Rent", PlannedAmount = 60m, DayOfMonth = 15, CreatedAt = new DateTime(2026, 1, 15), IntervalMonths = 1, TotalPayments = 12 },
        ]);

        var result = await new ListFixedExpensesQueryHandler(Access, _fixedExpenses)
            .Handle(new ListFixedExpensesQuery(adminId, profileId), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Rent", result[0].Name);
    }

    // ── CreateFixedExpense ────────────────────────────────────────────────

    [Fact]
    public async Task Create_Forbidden_WhenViewer()
    {
        var (_, profileId) = SetUpProfile();
        var viewerId = Guid.NewGuid();
        _profiles.GetPersonByUserIdAsync(profileId, viewerId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 1, BudgetProfileId = profileId, UserId = viewerId, Role = "viewer" });

        await Assert.ThrowsAsync<ForbiddenException>(() => new CreateFixedExpenseCommandHandler(Access, _fixedExpenses, _profiles, _transactions)
            .Handle(new CreateFixedExpenseCommand(viewerId, profileId, Fields()), CancellationToken.None));
    }

    [Fact]
    public async Task Create_DueThisMonth_SpawnsImmediately()
    {
        var (adminId, profileId) = SetUpProfile();
        var periodId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        _profiles.GetLatestPeriodAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, StartDate = new DateOnly(today.Year, today.Month, 1), EndDate = new DateOnly(today.Year, today.Month, 1).AddMonths(1).AddDays(-1) });
        _fixedExpenses.CreateAsync(Arg.Any<FixedExpense>(), Arg.Any<CancellationToken>())
            .Returns(ci => { var fe = ci.Arg<FixedExpense>(); fe.Id = Guid.NewGuid(); return fe; });
        _transactions.CreateTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Transaction>());

        var result = await new CreateFixedExpenseCommandHandler(Access, _fixedExpenses, _profiles, _transactions)
            .Handle(new CreateFixedExpenseCommand(adminId, profileId, Fields(dayOfMonth: today.Day)), CancellationToken.None);

        Assert.NotNull(result.Transaction);
        await _transactions.Received(1).CreateTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_FutureAnchorDate_SkipsImmediateSpawn()
    {
        var (adminId, profileId) = SetUpProfile();
        var periodId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var futureAnchor = today.AddMonths(2);
        _profiles.GetLatestPeriodAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, StartDate = new DateOnly(today.Year, today.Month, 1), EndDate = today });
        _fixedExpenses.CreateAsync(Arg.Any<FixedExpense>(), Arg.Any<CancellationToken>())
            .Returns(ci => { var fe = ci.Arg<FixedExpense>(); fe.Id = Guid.NewGuid(); return fe; });

        var result = await new CreateFixedExpenseCommandHandler(Access, _fixedExpenses, _profiles, _transactions)
            .Handle(new CreateFixedExpenseCommand(adminId, profileId, Fields(anchorDate: futureAnchor)), CancellationToken.None);

        Assert.Null(result.Transaction);
        await _transactions.DidNotReceive().CreateTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_NoActivePeriod_ReturnsExpenseWithNoTransaction()
    {
        var (adminId, profileId) = SetUpProfile();
        _profiles.GetLatestPeriodAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BudgetPeriod>(new NotFoundException("budget_period", "latest")));
        _fixedExpenses.CreateAsync(Arg.Any<FixedExpense>(), Arg.Any<CancellationToken>())
            .Returns(ci => { var fe = ci.Arg<FixedExpense>(); fe.Id = Guid.NewGuid(); return fe; });

        var result = await new CreateFixedExpenseCommandHandler(Access, _fixedExpenses, _profiles, _transactions)
            .Handle(new CreateFixedExpenseCommand(adminId, profileId, Fields()), CancellationToken.None);

        Assert.Null(result.Transaction);
    }

    [Fact]
    public async Task Create_WeekUnit_SpawnsMultipleOccurrencesInActivePeriod()
    {
        var (adminId, profileId) = SetUpProfile();
        var periodId = Guid.NewGuid();
        var periodStart = new DateOnly(2026, 2, 1);
        var periodEnd = new DateOnly(2026, 2, 28);
        var anchor = new DateOnly(2020, 1, 6); // a long-past Monday, always due weekly
        _profiles.GetLatestPeriodAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, StartDate = periodStart, EndDate = periodEnd });
        _fixedExpenses.CreateAsync(Arg.Any<FixedExpense>(), Arg.Any<CancellationToken>())
            .Returns(ci => { var fe = ci.Arg<FixedExpense>(); fe.Id = Guid.NewGuid(); return fe; });
        _fixedExpenses.HasTransactionOnDateAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(false);

        await new CreateFixedExpenseCommandHandler(Access, _fixedExpenses, _profiles, _transactions)
            .Handle(new CreateFixedExpenseCommand(adminId, profileId, Fields(dayOfMonth: 0, anchorDate: anchor, frequencyUnit: "week")), CancellationToken.None);

        // Feb 2026: Mondays-due weeks starting 2/2 and 2/9 and 2/16 and 2/23 fall inside [2/1, 2/28].
        await _transactions.Received(4).CreateTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>());
    }

    // ── UpdateFixedExpense ────────────────────────────────────────────────

    [Fact]
    public async Task Update_RescheduledToFuture_DeletesUnpaidTransaction()
    {
        var (adminId, profileId) = SetUpProfile();
        var feId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var farFutureAnchor = new DateOnly(2030, 1, 1);
        _profiles.GetLatestPeriodAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, StartDate = new DateOnly(2026, 2, 1), EndDate = new DateOnly(2026, 2, 28) });
        _fixedExpenses.UpdateAsync(Arg.Any<FixedExpense>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<FixedExpense>());

        await CreateUpdateFixedExpenseHandler()
            .Handle(new UpdateFixedExpenseCommand(adminId, feId, profileId, Fields(anchorDate: farFutureAnchor)), CancellationToken.None);

        await _fixedExpenses.Received(1).DeleteUnpaidTransactionsAsync(feId, profileId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_NoTransactionYet_Spawns()
    {
        var (adminId, profileId) = SetUpProfile();
        var feId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var periodStart = new DateOnly(2026, 2, 1);
        _profiles.GetLatestPeriodAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, StartDate = periodStart, EndDate = new DateOnly(2026, 2, 28) });
        _fixedExpenses.UpdateAsync(Arg.Any<FixedExpense>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<FixedExpense>());
        _fixedExpenses.GetTransactionAsync(feId, profileId, Arg.Any<CancellationToken>()).Returns((Transaction?)null);

        await CreateUpdateFixedExpenseHandler()
            .Handle(new UpdateFixedExpenseCommand(adminId, feId, profileId, Fields(dayOfMonth: 15)), CancellationToken.None);

        await _transactions.Received(1).CreateTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_PaidTransaction_UpdatesInPlaceAndDoesNotSpawn()
    {
        var (adminId, profileId) = SetUpProfile();
        var feId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        _profiles.GetLatestPeriodAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, StartDate = new DateOnly(2026, 2, 1), EndDate = new DateOnly(2026, 2, 28) });
        _fixedExpenses.UpdateAsync(Arg.Any<FixedExpense>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<FixedExpense>());
        _fixedExpenses.GetTransactionAsync(feId, profileId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = Guid.NewGuid(), IsPaid = true });

        await CreateUpdateFixedExpenseHandler()
            .Handle(new UpdateFixedExpenseCommand(adminId, feId, profileId, Fields(dayOfMonth: 15)), CancellationToken.None);

        await _fixedExpenses.Received(1).UpdatePaidTransactionFromFixedExpenseAsync(
            feId, profileId, Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        await _transactions.DidNotReceive().CreateTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_StillDue_PropagatesToUnpaidTransaction()
    {
        var (adminId, profileId) = SetUpProfile();
        var feId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        _profiles.GetLatestPeriodAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, StartDate = new DateOnly(2026, 2, 1), EndDate = new DateOnly(2026, 2, 28) });
        _fixedExpenses.UpdateAsync(Arg.Any<FixedExpense>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<FixedExpense>());
        _fixedExpenses.GetTransactionAsync(feId, profileId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = Guid.NewGuid(), IsPaid = false });

        await CreateUpdateFixedExpenseHandler()
            .Handle(new UpdateFixedExpenseCommand(adminId, feId, profileId, Fields(amount: 70m, dayOfMonth: 15)), CancellationToken.None);

        await _fixedExpenses.Received(1).UpdateTransactionFromFixedExpenseAsync(
            feId, profileId, Arg.Any<string>(), 70m, Arg.Any<int?>(), Arg.Any<Guid?>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_WeekUnit_DoesNotReconcileExistingTransactions()
    {
        var (adminId, profileId) = SetUpProfile();
        var feId = Guid.NewGuid();
        _fixedExpenses.UpdateAsync(Arg.Any<FixedExpense>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<FixedExpense>());

        await CreateUpdateFixedExpenseHandler()
            .Handle(new UpdateFixedExpenseCommand(adminId, feId, profileId, Fields(dayOfMonth: 0, frequencyUnit: "week")), CancellationToken.None);

        await _profiles.DidNotReceive().GetLatestPeriodAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _transactions.DidNotReceive().CreateTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>());
    }

    // ── DeleteFixedExpense ────────────────────────────────────────────────

    [Fact]
    public async Task Delete_WrongProfile_ThrowsForbidden()
    {
        var (adminId, profileId) = SetUpProfile();
        var feId = Guid.NewGuid();
        _fixedExpenses.GetByIdAsync(feId, Arg.Any<CancellationToken>())
            .Returns(new FixedExpense { Id = feId, BudgetProfileId = Guid.NewGuid(), Name = "Other" });

        await Assert.ThrowsAsync<ForbiddenException>(() => new DeleteFixedExpenseCommandHandler(Access, _fixedExpenses)
            .Handle(new DeleteFixedExpenseCommand(adminId, feId, profileId), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_Success_DeactivatesAndClearsUnpaidTransaction()
    {
        var (adminId, profileId) = SetUpProfile();
        var feId = Guid.NewGuid();
        _fixedExpenses.GetByIdAsync(feId, Arg.Any<CancellationToken>())
            .Returns(new FixedExpense { Id = feId, BudgetProfileId = profileId, Name = "Rent" });

        await new DeleteFixedExpenseCommandHandler(Access, _fixedExpenses)
            .Handle(new DeleteFixedExpenseCommand(adminId, feId, profileId), CancellationToken.None);

        await _fixedExpenses.Received(1).DeleteUnpaidTransactionsAsync(feId, profileId, Arg.Any<CancellationToken>());
        await _fixedExpenses.Received(1).DeactivateAsync(feId, profileId, Arg.Any<CancellationToken>());
    }

    // ── CreateInstallmentPlan ─────────────────────────────────────────────

    private (Guid AdminId, Guid ProfileId, Guid PeriodId, Transaction Tx) SetUpInstallmentFixture(bool archived = false, int transactionTypeId = 2, decimal amount = 300m)
    {
        var (adminId, profileId) = SetUpProfile();
        var periodId = Guid.NewGuid();
        var txId = Guid.NewGuid();
        var period = new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, IsArchived = archived, StartDate = new DateOnly(2026, 2, 1), EndDate = new DateOnly(2026, 2, 28) };
        var tx = new Transaction { Id = txId, BudgetPeriodId = periodId, Name = "Sofa", Amount = amount, TransactionTypeId = transactionTypeId, CategoryId = 4 };
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>()).Returns(period);
        _transactions.GetTransactionAsync(txId, Arg.Any<CancellationToken>()).Returns(tx);
        _fixedExpenses.CreateAsync(Arg.Any<FixedExpense>(), Arg.Any<CancellationToken>())
            .Returns(ci => { var fe = ci.Arg<FixedExpense>(); fe.Id = Guid.NewGuid(); return fe; });
        _profiles.GetLatestPeriodAsync(profileId, Arg.Any<CancellationToken>()).Returns(period);
        _transactions.SetInstallmentPlanAsync(txId, periodId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci => new Transaction { Id = txId, BudgetPeriodId = periodId, InstallmentFixedExpenseId = ci.ArgAt<Guid>(2), IsExcluded = true });
        return (adminId, profileId, periodId, tx);
    }

    [Fact]
    public async Task CreateInstallment_RejectsFewerThanTwoPayments()
    {
        var (adminId, _, periodId, tx) = SetUpInstallmentFixture();

        await Assert.ThrowsAsync<AppValidationException>(() => new CreateInstallmentPlanCommandHandler(Access, _profiles, _transactions, _fixedExpenses)
            .Handle(new CreateInstallmentPlanCommand(adminId, tx.Id, periodId, new DateOnly(2026, 3, 1), 1, null), CancellationToken.None));
    }

    [Fact]
    public async Task CreateInstallment_RejectsAlreadyConverted()
    {
        var (adminId, profileId) = SetUpProfile();
        var periodId = Guid.NewGuid();
        var txId = Guid.NewGuid();
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId });
        _transactions.GetTransactionAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = txId, BudgetPeriodId = periodId, InstallmentFixedExpenseId = Guid.NewGuid(), Amount = 100m });

        await Assert.ThrowsAsync<AppValidationException>(() => new CreateInstallmentPlanCommandHandler(Access, _profiles, _transactions, _fixedExpenses)
            .Handle(new CreateInstallmentPlanCommand(adminId, txId, periodId, new DateOnly(2026, 3, 1), 3, null), CancellationToken.None));
    }

    [Fact]
    public async Task CreateInstallment_RejectsFixedTransaction()
    {
        var (adminId, _, periodId, tx) = SetUpInstallmentFixture(transactionTypeId: 1);

        await Assert.ThrowsAsync<AppValidationException>(() => new CreateInstallmentPlanCommandHandler(Access, _profiles, _transactions, _fixedExpenses)
            .Handle(new CreateInstallmentPlanCommand(adminId, tx.Id, periodId, new DateOnly(2026, 3, 1), 3, null), CancellationToken.None));
    }

    [Fact]
    public async Task CreateInstallment_RejectsReceivedAmount()
    {
        var (adminId, _, periodId, tx) = SetUpInstallmentFixture(amount: -50m);

        await Assert.ThrowsAsync<AppValidationException>(() => new CreateInstallmentPlanCommandHandler(Access, _profiles, _transactions, _fixedExpenses)
            .Handle(new CreateInstallmentPlanCommand(adminId, tx.Id, periodId, new DateOnly(2026, 3, 1), 3, null), CancellationToken.None));
    }

    [Fact]
    public async Task CreateInstallment_RejectsTransactionFromAnotherPeriod()
    {
        var (adminId, profileId) = SetUpProfile();
        var periodId = Guid.NewGuid();
        var otherPeriodId = Guid.NewGuid();
        var txId = Guid.NewGuid();
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId });
        _transactions.GetTransactionAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = txId, BudgetPeriodId = otherPeriodId, Amount = 100m, TransactionTypeId = 2 });

        await Assert.ThrowsAsync<NotFoundException>(() => new CreateInstallmentPlanCommandHandler(Access, _profiles, _transactions, _fixedExpenses)
            .Handle(new CreateInstallmentPlanCommand(adminId, txId, periodId, new DateOnly(2026, 3, 1), 3, null), CancellationToken.None));
    }

    [Fact]
    public async Task CreateInstallment_AllowedOnArchivedPeriod()
    {
        var (adminId, _, periodId, tx) = SetUpInstallmentFixture(archived: true);

        var result = await new CreateInstallmentPlanCommandHandler(Access, _profiles, _transactions, _fixedExpenses)
            .Handle(new CreateInstallmentPlanCommand(adminId, tx.Id, periodId, new DateOnly(2026, 3, 1), 3, null), CancellationToken.None);

        Assert.True(result.Transaction.IsExcluded);
    }

    [Fact]
    public async Task CreateInstallment_SplitsAmountAndLinksTransaction()
    {
        var (adminId, _, periodId, tx) = SetUpInstallmentFixture(amount: 300m);

        var result = await new CreateInstallmentPlanCommandHandler(Access, _profiles, _transactions, _fixedExpenses)
            .Handle(new CreateInstallmentPlanCommand(adminId, tx.Id, periodId, new DateOnly(2026, 3, 1), 3, null), CancellationToken.None);

        Assert.Equal(100m, result.Expense.PlannedAmount.ToDecimal());
        Assert.True(result.Expense.IsInstallmentPlan);
        Assert.Equal(result.Expense.Id, result.Transaction.InstallmentFixedExpenseId);
    }

    [Fact]
    public async Task CreateInstallment_HonoursEndDateOverride()
    {
        var (adminId, _, periodId, tx) = SetUpInstallmentFixture();
        FixedExpense? created = null;
        _fixedExpenses.CreateAsync(Arg.Any<FixedExpense>(), Arg.Any<CancellationToken>())
            .Returns(ci => { created = ci.Arg<FixedExpense>(); created.Id = Guid.NewGuid(); return created; });
        var explicitEnd = new DateOnly(2026, 12, 1);

        await new CreateInstallmentPlanCommandHandler(Access, _profiles, _transactions, _fixedExpenses)
            .Handle(new CreateInstallmentPlanCommand(adminId, tx.Id, periodId, new DateOnly(2026, 3, 1), 3, explicitEnd), CancellationToken.None);

        Assert.Equal(explicitEnd, created!.EndDate);
    }

    [Fact]
    public async Task CreateInstallment_RejectsEndDateBeforeFirstPayment()
    {
        var (adminId, _, periodId, tx) = SetUpInstallmentFixture();

        await Assert.ThrowsAsync<AppValidationException>(() => new CreateInstallmentPlanCommandHandler(Access, _profiles, _transactions, _fixedExpenses)
            .Handle(new CreateInstallmentPlanCommand(adminId, tx.Id, periodId, new DateOnly(2026, 3, 1), 3, new DateOnly(2026, 1, 1)), CancellationToken.None));
    }

    // ── DeleteInstallmentPlan ─────────────────────────────────────────────

    [Fact]
    public async Task DeleteInstallment_RejectsATransactionThatIsNotAPlan()
    {
        var (adminId, profileId) = SetUpProfile();
        var periodId = Guid.NewGuid();
        var txId = Guid.NewGuid();
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId });
        _transactions.GetTransactionAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = txId, InstallmentFixedExpenseId = null });

        await Assert.ThrowsAsync<AppValidationException>(() => new DeleteInstallmentPlanCommandHandler(Access, _profiles, _transactions, _fixedExpenses)
            .Handle(new DeleteInstallmentPlanCommand(adminId, txId, periodId), CancellationToken.None));
    }

    [Fact]
    public async Task DeleteInstallment_RefusedOnceAPaymentIsPaid()
    {
        var (adminId, profileId) = SetUpProfile();
        var periodId = Guid.NewGuid();
        var txId = Guid.NewGuid();
        var feId = Guid.NewGuid();
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId });
        _transactions.GetTransactionAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = txId, InstallmentFixedExpenseId = feId });
        _transactions.ListByFixedExpenseAsync(feId, Arg.Any<CancellationToken>())
            .Returns([new Transaction { Id = Guid.NewGuid(), IsPaid = true }]);

        await Assert.ThrowsAsync<AppValidationException>(() => new DeleteInstallmentPlanCommandHandler(Access, _profiles, _transactions, _fixedExpenses)
            .Handle(new DeleteInstallmentPlanCommand(adminId, txId, periodId), CancellationToken.None));

        await _transactions.DidNotReceive().ClearInstallmentPlanAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteInstallment_RemovesPlanAndRestoresPurchase()
    {
        var (adminId, profileId) = SetUpProfile();
        var periodId = Guid.NewGuid();
        var txId = Guid.NewGuid();
        var feId = Guid.NewGuid();
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId });
        _transactions.GetTransactionAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = txId, InstallmentFixedExpenseId = feId });
        _transactions.ListByFixedExpenseAsync(feId, Arg.Any<CancellationToken>()).Returns([]);
        _transactions.ClearInstallmentPlanAsync(txId, periodId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = txId, IsExcluded = false, InstallmentFixedExpenseId = null });

        var result = await new DeleteInstallmentPlanCommandHandler(Access, _profiles, _transactions, _fixedExpenses)
            .Handle(new DeleteInstallmentPlanCommand(adminId, txId, periodId), CancellationToken.None);

        Assert.False(result.IsExcluded);
        await _transactions.Received(1).ClearInstallmentPlanAsync(txId, periodId, Arg.Any<CancellationToken>());
        await _transactions.Received(1).DeleteByFixedExpenseAsync(feId, Arg.Any<CancellationToken>());
        await _fixedExpenses.Received(1).DeactivateAsync(feId, profileId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteInstallment_AllowedOnArchivedPeriod()
    {
        var (adminId, profileId) = SetUpProfile();
        var periodId = Guid.NewGuid();
        var txId = Guid.NewGuid();
        var feId = Guid.NewGuid();
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, IsArchived = true });
        _transactions.GetTransactionAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = txId, InstallmentFixedExpenseId = feId });
        _transactions.ListByFixedExpenseAsync(feId, Arg.Any<CancellationToken>()).Returns([]);
        _transactions.ClearInstallmentPlanAsync(txId, periodId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = txId });

        await new DeleteInstallmentPlanCommandHandler(Access, _profiles, _transactions, _fixedExpenses)
            .Handle(new DeleteInstallmentPlanCommand(adminId, txId, periodId), CancellationToken.None);

        await _fixedExpenses.Received(1).DeactivateAsync(feId, profileId, Arg.Any<CancellationToken>());
    }

    // ── CreateFixedExpenseFromTransaction (deferred from B5 batch 5 to batch 7) ──

    private (Guid UserId, Guid ProfileId, Guid PeriodId, Guid TxId, Guid FeId, Guid SpawnedId) SetUpFixedFromTxFixture(
        bool archived = false, decimal amount = 15.00m, int transactionTypeId = 2, TransactionReview? existingReview = null)
    {
        var (userId, profileId) = SetUpProfile();
        var periodId = Guid.NewGuid();
        var txId = Guid.NewGuid();
        var feId = Guid.NewGuid();
        var spawnedId = Guid.NewGuid();
        var period = new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, IsArchived = archived, StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30) };

        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>()).Returns(period);
        _profiles.GetLatestPeriodAsync(profileId, Arg.Any<CancellationToken>()).Returns(period);
        // IsExcluded=true here reflects the state after SetTransactionExcludedAsync
        // runs during the confirm flow — NSubstitute doesn't simulate the
        // transition, and the handler re-fetches this same row afterward to
        // build its response, so the mock needs to already reflect the
        // post-confirm state for that final read.
        _transactions.GetTransactionAsync(txId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = txId, Name = "Netflix", Amount = amount, BudgetPeriodId = periodId, TransactionTypeId = transactionTypeId, IsExcluded = true });
        _fixedExpenses.CreateAsync(Arg.Any<FixedExpense>(), Arg.Any<CancellationToken>())
            .Returns(ci => { var fe = ci.Arg<FixedExpense>(); fe.Id = feId; return fe; });
        var spawnedTx = new Transaction { Id = spawnedId, Name = "Netflix", Amount = amount, PlannedAmount = amount, BudgetPeriodId = periodId, TransactionTypeId = 1, FixedExpenseId = feId };
        _transactions.CreateTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>()).Returns(spawnedTx);
        _transactions.GetTransactionAsync(spawnedId, Arg.Any<CancellationToken>()).Returns(spawnedTx);
        _reviews.GetByTransactionIdAsync(txId, Arg.Any<CancellationToken>()).Returns(existingReview);
        _reviews.UpsertAsync(periodId, txId, spawnedId, 100.0m, Arg.Any<CancellationToken>())
            .Returns(new TransactionReview { Id = Guid.NewGuid(), BudgetPeriodId = periodId, TransactionId = txId, MatchedTransactionId = spawnedId, MatchScore = 100.0m, Status = "pending" });
        _transactions.MarkTransactionAsPaidAsync(spawnedId, periodId, amount, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = spawnedId, FixedExpenseId = feId, IsPaid = true, BudgetPeriodId = periodId });
        _fixedExpenses.GetByIdAsync(feId, Arg.Any<CancellationToken>())
            .Returns(new FixedExpense { Id = feId, BudgetProfileId = profileId, Name = "Netflix", PlannedAmount = amount });
        _reviews.ListByMatchedTransactionIdAsync(spawnedId, Arg.Any<CancellationToken>()).Returns([]);
        _transactions.SetTransactionExcludedAsync(txId, periodId, true, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = txId, IsExcluded = true });

        return (userId, profileId, periodId, txId, feId, spawnedId);
    }

    private CreateFixedExpenseFromTransactionCommandHandler CreateFixedFromTxHandler() => new(
        Access, _profiles, _transactions, _fixedExpenses, _reviews, NullLogger<CreateFixedExpenseFromTransactionCommandHandler>.Instance);

    [Fact]
    public async Task CreateFromTransaction_CreatesAndAutoConfirmsMatch()
    {
        var (userId, _, periodId, txId, feId, spawnedId) = SetUpFixedFromTxFixture();

        var result = await CreateFixedFromTxHandler().Handle(
            new CreateFixedExpenseFromTransactionCommand(userId, txId, periodId, "", null, "month", 1, 1, 1), CancellationToken.None);

        Assert.Equal(feId, result.Expense.Id);
        Assert.True(result.Transaction.IsExcluded);
        await _reviews.Received(1).UpsertAsync(periodId, txId, spawnedId, 100.0m, Arg.Any<CancellationToken>());
        await _transactions.Received(1).MarkTransactionAsPaidAsync(spawnedId, periodId, 15.00m, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        await _transactions.Received(1).SetTransactionExcludedAsync(txId, periodId, true, Arg.Any<CancellationToken>());
        await _reviews.Received(1).UpdateStatusAsync(Arg.Any<Guid>(), "confirmed", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateFromTransaction_HonoursNameOverride()
    {
        var (userId, _, periodId, txId, _, _) = SetUpFixedFromTxFixture();
        FixedExpense? created = null;
        _fixedExpenses.CreateAsync(Arg.Any<FixedExpense>(), Arg.Any<CancellationToken>())
            .Returns(ci => { created = ci.Arg<FixedExpense>(); created.Id = Guid.NewGuid(); return created; });

        await CreateFixedFromTxHandler().Handle(
            new CreateFixedExpenseFromTransactionCommand(userId, txId, periodId, "Streaming", null, "month", 1, 1, 1), CancellationToken.None);

        Assert.Equal("Streaming", created!.Name);
    }

    [Fact]
    public async Task CreateFromTransaction_RejectsFixedTransaction()
    {
        var (userId, _, periodId, txId, _, _) = SetUpFixedFromTxFixture(transactionTypeId: 1);

        await Assert.ThrowsAsync<AppValidationException>(() => CreateFixedFromTxHandler().Handle(
            new CreateFixedExpenseFromTransactionCommand(userId, txId, periodId, "", null, "month", 1, 1, 1), CancellationToken.None));

        await _fixedExpenses.DidNotReceive().CreateAsync(Arg.Any<FixedExpense>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateFromTransaction_RejectsReceivedAmount()
    {
        var (userId, _, periodId, txId, _, _) = SetUpFixedFromTxFixture(amount: -15.00m);

        await Assert.ThrowsAsync<AppValidationException>(() => CreateFixedFromTxHandler().Handle(
            new CreateFixedExpenseFromTransactionCommand(userId, txId, periodId, "", null, "month", 1, 1, 1), CancellationToken.None));

        await _fixedExpenses.DidNotReceive().CreateAsync(Arg.Any<FixedExpense>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateFromTransaction_RejectsArchivedPeriod()
    {
        var (userId, _, periodId, txId, _, _) = SetUpFixedFromTxFixture(archived: true);

        await Assert.ThrowsAsync<AppValidationException>(() => CreateFixedFromTxHandler().Handle(
            new CreateFixedExpenseFromTransactionCommand(userId, txId, periodId, "", null, "month", 1, 1, 1), CancellationToken.None));

        await _fixedExpenses.DidNotReceive().CreateAsync(Arg.Any<FixedExpense>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateFromTransaction_RejectsAlreadyMatchedTransaction()
    {
        var existing = new TransactionReview { Id = Guid.NewGuid(), Status = "confirmed" };
        var (userId, _, periodId, txId, _, _) = SetUpFixedFromTxFixture(existingReview: existing);

        await Assert.ThrowsAsync<AppValidationException>(() => CreateFixedFromTxHandler().Handle(
            new CreateFixedExpenseFromTransactionCommand(userId, txId, periodId, "", null, "month", 1, 1, 1), CancellationToken.None));

        await _fixedExpenses.DidNotReceive().CreateAsync(Arg.Any<FixedExpense>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateFromTransaction_AllowsPreviouslyDismissedTransaction()
    {
        var existing = new TransactionReview { Id = Guid.NewGuid(), Status = "dismissed" };
        var (userId, _, periodId, txId, feId, _) = SetUpFixedFromTxFixture(existingReview: existing);

        var result = await CreateFixedFromTxHandler().Handle(
            new CreateFixedExpenseFromTransactionCommand(userId, txId, periodId, "", null, "month", 1, 1, 1), CancellationToken.None);

        Assert.Equal(feId, result.Expense.Id);
    }

    // ── UpdateFixedExpense review-refresh (HOOK completed B5 batch 7) ───────

    [Fact]
    public async Task Update_StillDue_RefreshesPendingReview_WhenStillMatches()
    {
        var (adminId, profileId) = SetUpProfile();
        var feId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var existingTxId = Guid.NewGuid();
        var reviewId = Guid.NewGuid();
        _profiles.GetLatestPeriodAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, StartDate = new DateOnly(2026, 2, 1), EndDate = new DateOnly(2026, 2, 28) });
        _fixedExpenses.UpdateAsync(Arg.Any<FixedExpense>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<FixedExpense>());
        _fixedExpenses.GetTransactionAsync(feId, profileId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = existingTxId, IsPaid = false });
        _reviews.ListByMatchedTransactionIdAsync(existingTxId, Arg.Any<CancellationToken>())
            .Returns([new TransactionReview { Id = reviewId, TransactionId = Guid.NewGuid(), MatchedTransactionId = existingTxId, Status = "pending" }]);
        _transactions.GetTransactionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = Guid.NewGuid(), Name = "Rent", Amount = 60m, CategoryId = 3 });
        _reviews.ListAliasesAsync(feId, Arg.Any<CancellationToken>()).Returns([]);

        await CreateUpdateFixedExpenseHandler().Handle(
            new UpdateFixedExpenseCommand(adminId, feId, profileId, Fields(amount: 60m, dayOfMonth: 15)), CancellationToken.None);

        await _reviews.Received(1).UpdateScoreIfPendingAsync(reviewId, Arg.Any<decimal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_StillDue_RemovesStaleReview_WhenNoLongerMatches()
    {
        var (adminId, profileId) = SetUpProfile();
        var feId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var existingTxId = Guid.NewGuid();
        var reviewId = Guid.NewGuid();
        _profiles.GetLatestPeriodAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, StartDate = new DateOnly(2026, 2, 1), EndDate = new DateOnly(2026, 2, 28) });
        _fixedExpenses.UpdateAsync(Arg.Any<FixedExpense>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<FixedExpense>());
        _fixedExpenses.GetTransactionAsync(feId, profileId, Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = existingTxId, IsPaid = false });
        _reviews.ListByMatchedTransactionIdAsync(existingTxId, Arg.Any<CancellationToken>())
            .Returns([new TransactionReview { Id = reviewId, TransactionId = Guid.NewGuid(), MatchedTransactionId = existingTxId, Status = "pending" }]);
        // The pending review's own variable transaction no longer resembles the edited template at all.
        _transactions.GetTransactionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new Transaction { Id = Guid.NewGuid(), Name = "Coffee Shop", Amount = 4m });
        _reviews.ListAliasesAsync(feId, Arg.Any<CancellationToken>()).Returns([]);

        await CreateUpdateFixedExpenseHandler().Handle(
            new UpdateFixedExpenseCommand(adminId, feId, profileId, Fields(amount: 1500m, dayOfMonth: 15)), CancellationToken.None);

        await _reviews.Received(1).DeleteIfPendingAsync(reviewId, Arg.Any<CancellationToken>());
    }
}
