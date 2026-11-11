using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Budgets.AddIncomeSource;
using WellSpent.Application.Budgets.DeleteIncomeSource;
using WellSpent.Application.Budgets.ListIncomeEntries;
using WellSpent.Application.Budgets.ListIncomeSources;
using WellSpent.Application.Budgets.UpdateIncomeEntry;
using WellSpent.Application.Budgets.UpdateIncomeSource;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Budgets;

public sealed class IncomeSourceTests
{
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private WellSpent.Application.Budgets.TaxReserveRecalculator TaxReserve =>
        new(_profiles, _users, NullLogger<WellSpent.Application.Budgets.TaxReserveRecalculator>.Instance);
    private WellSpent.Application.Common.BudgetAccessGuard Access => new(_profiles);

    private (Guid AdminId, Guid ProfileId) SetUpOwner(string plan = "pro")
    {
        var profileId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = adminId, Name = "x" });
        _users.GetByIdAsync(adminId, Arg.Any<CancellationToken>())
            .Returns(new User { Id = adminId, Email = "a@example.com", Plan = plan });
        return (adminId, profileId);
    }

    [Fact]
    public async Task Add_ViewerForbidden()
    {
        var (_, profileId) = SetUpOwner();
        var viewerId = Guid.NewGuid();
        _profiles.GetPersonByUserIdAsync(profileId, viewerId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 1, BudgetProfileId = profileId, UserId = viewerId, Role = "viewer" });

        await Assert.ThrowsAsync<ForbiddenException>(() => new AddIncomeSourceCommandHandler(Access, _profiles, _users, TaxReserve)
            .Handle(new AddIncomeSourceCommand(viewerId, profileId, "Salary", "salary", new Money(5000, 0), true, null, "monthly", true), CancellationToken.None));
    }

    [Fact]
    public async Task Add_Collaborator_Allowed()
    {
        var (adminId, profileId) = SetUpOwner();
        _profiles.AddIncomeSourceAsync(Arg.Any<IncomeSource>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<IncomeSource>());

        var result = await new AddIncomeSourceCommandHandler(Access, _profiles, _users, TaxReserve)
            .Handle(new AddIncomeSourceCommand(adminId, profileId, "Salary", "salary", new Money(5000, 0), true, null, "monthly", true), CancellationToken.None);

        Assert.Equal("Salary", result.Name);
        Assert.Equal(5000, result.DefaultAmount.Units);
    }

    [Fact]
    public async Task Add_FreeTier_TwoPerSamePerson_Throws()
    {
        var (adminId, profileId) = SetUpOwner(plan: "free");
        _profiles.ListIncomeSourcesAsync(profileId, Arg.Any<CancellationToken>()).Returns([
            new IncomeSource { Id = 1, BudgetProfileId = profileId, Name = "A", BudgetPersonId = 5 },
            new IncomeSource { Id = 2, BudgetProfileId = profileId, Name = "B", BudgetPersonId = 5 },
        ]);

        await Assert.ThrowsAsync<AppValidationException>(() => new AddIncomeSourceCommandHandler(Access, _profiles, _users, TaxReserve)
            .Handle(new AddIncomeSourceCommand(adminId, profileId, "C", "salary", new Money(1000, 0), true, 5, "monthly", false), CancellationToken.None));
    }

    [Fact]
    public async Task Add_FreeTier_DifferentPerson_NotCounted()
    {
        var (adminId, profileId) = SetUpOwner(plan: "free");
        _profiles.ListIncomeSourcesAsync(profileId, Arg.Any<CancellationToken>()).Returns([
            new IncomeSource { Id = 1, BudgetProfileId = profileId, Name = "A", BudgetPersonId = 5 },
            new IncomeSource { Id = 2, BudgetProfileId = profileId, Name = "B", BudgetPersonId = 5 },
        ]);
        _profiles.AddIncomeSourceAsync(Arg.Any<IncomeSource>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<IncomeSource>());

        var result = await new AddIncomeSourceCommandHandler(Access, _profiles, _users, TaxReserve)
            .Handle(new AddIncomeSourceCommand(adminId, profileId, "C", "salary", new Money(1000, 0), true, 9, "monthly", false), CancellationToken.None);

        Assert.Equal("C", result.Name);
    }

    [Fact]
    public async Task Add_FreeTier_SupersededRow_NotCounted()
    {
        var (adminId, profileId) = SetUpOwner(plan: "free");
        // One superseded (historical) row plus one current row for the same person — only the current one should count.
        _profiles.ListIncomeSourcesAsync(profileId, Arg.Any<CancellationToken>()).Returns([
            new IncomeSource { Id = 1, BudgetProfileId = profileId, Name = "Old", BudgetPersonId = 5, EffectiveTo = new DateOnly(2026, 2, 1) },
            new IncomeSource { Id = 2, BudgetProfileId = profileId, Name = "Current", BudgetPersonId = 5 },
        ]);
        _profiles.AddIncomeSourceAsync(Arg.Any<IncomeSource>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<IncomeSource>());

        var result = await new AddIncomeSourceCommandHandler(Access, _profiles, _users, TaxReserve)
            .Handle(new AddIncomeSourceCommand(adminId, profileId, "Second", "salary", new Money(1000, 0), true, 5, "monthly", false), CancellationToken.None);

        Assert.Equal("Second", result.Name);
    }

    [Fact]
    public async Task List_NonMember_Throws()
    {
        var (_, profileId) = SetUpOwner();
        var strangerId = Guid.NewGuid();
        _profiles.GetPersonByUserIdAsync(profileId, strangerId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BudgetPerson>(new NotFoundException("budget_person", strangerId.ToString())));

        await Assert.ThrowsAsync<NotFoundException>(() => new ListIncomeSourcesQueryHandler(Access, _profiles)
            .Handle(new ListIncomeSourcesQuery(strangerId, profileId), CancellationToken.None));
    }

    [Fact]
    public async Task List_ExcludesSupersededRows()
    {
        var (adminId, profileId) = SetUpOwner();
        _profiles.ListIncomeSourcesAsync(profileId, Arg.Any<CancellationToken>()).Returns([
            new IncomeSource { Id = 1, BudgetProfileId = profileId, Name = "Old", EffectiveTo = new DateOnly(2026, 2, 1) },
            new IncomeSource { Id = 2, BudgetProfileId = profileId, Name = "Current" },
        ]);

        var result = await new ListIncomeSourcesQueryHandler(Access, _profiles)
            .Handle(new ListIncomeSourcesQuery(adminId, profileId), CancellationToken.None);

        var single = Assert.Single(result);
        Assert.Equal("Current", single.Name);
    }

    [Fact]
    public async Task Update_RecalculatesTaxReserve()
    {
        var (adminId, profileId) = SetUpOwner();
        _profiles.ListIncomeSourcesAsync(profileId, Arg.Any<CancellationToken>()).Returns([
            new IncomeSource { Id = 1, BudgetProfileId = profileId, Name = "Salary" },
        ]);
        _profiles.UpdateIncomeSourceAsync(Arg.Any<IncomeSource>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IncomeSource>());

        await new UpdateIncomeSourceCommandHandler(Access, _profiles, TaxReserve)
            .Handle(new UpdateIncomeSourceCommand(adminId, 1, profileId, "Salary", "salary", new Money(6000, 0), true, null, "monthly", true), CancellationToken.None);

        // Once for the access check, once inside TaxReserveRecalculator.
        await _profiles.Received(2).GetByIdAsync(profileId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_ScheduleUnchanged_UpdatesInPlace()
    {
        var (adminId, profileId) = SetUpOwner();
        var anchor = new DateOnly(2026, 1, 1);
        _profiles.ListIncomeSourcesAsync(profileId, Arg.Any<CancellationToken>()).Returns([
            new IncomeSource { Id = 1, BudgetProfileId = profileId, Name = "Salary", AnchorDate = anchor, EarlyArrivalToleranceDays = 2 },
        ]);
        _profiles.UpdateIncomeSourceAsync(Arg.Any<IncomeSource>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IncomeSource>());

        var result = await new UpdateIncomeSourceCommandHandler(Access, _profiles, TaxReserve)
            .Handle(new UpdateIncomeSourceCommand(adminId, 1, profileId, "Salary renamed", "salary", new Money(6000, 0), true, null, "monthly", true, anchor, 2), CancellationToken.None);

        Assert.Equal(1, result.Id);
        Assert.Equal("Salary renamed", result.Name);
        await _profiles.DidNotReceive().AddIncomeSourceAsync(Arg.Any<IncomeSource>(), Arg.Any<CancellationToken>());
        await _profiles.DidNotReceive().SetIncomeSourceEffectiveToAsync(Arg.Any<int>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_AnchorDateChanged_ForksNewRow()
    {
        var (adminId, profileId) = SetUpOwner();
        var oldAnchor = new DateOnly(2026, 1, 1);
        var newAnchor = new DateOnly(2026, 1, 15);
        var periodEnd = new DateOnly(2026, 1, 31);
        _profiles.ListIncomeSourcesAsync(profileId, Arg.Any<CancellationToken>()).Returns([
            // AdjustForWeekends: false — this test pins the fork-on-AnchorDate-change date math,
            // unrelated to and predating the weekend-adjustment feature.
            new IncomeSource { Id = 1, BudgetProfileId = profileId, Name = "Salary", AnchorDate = oldAnchor, PaymentFrequency = "monthly", AdjustForWeekends = false },
        ]);
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = adminId, Name = "x", Cycle = "monthly" });
        _profiles.GetLatestPeriodAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = Guid.NewGuid(), BudgetProfileId = profileId, EndDate = periodEnd });
        _profiles.AddIncomeSourceAsync(Arg.Any<IncomeSource>(), Arg.Any<CancellationToken>())
            .Returns(ci => { var s = ci.Arg<IncomeSource>(); s.Id = 2; return s; });

        var result = await new UpdateIncomeSourceCommandHandler(Access, _profiles, TaxReserve)
            .Handle(new UpdateIncomeSourceCommand(adminId, 1, profileId, "Salary", "salary", new Money(6000, 0), true, null, "monthly", true, newAnchor, 0), CancellationToken.None);

        Assert.Equal(2, result.Id);
        Assert.Equal(newAnchor, result.AnchorDate);
        await _profiles.Received(1).SetIncomeSourceEffectiveToAsync(1, new DateOnly(2026, 2, 1), Arg.Any<CancellationToken>());
        await _profiles.DidNotReceive().UpdateIncomeSourceAsync(Arg.Any<IncomeSource>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_ToleranceOnlyChanged_ForksNewRow()
    {
        var (adminId, profileId) = SetUpOwner();
        var anchor = new DateOnly(2026, 1, 1);
        var periodEnd = new DateOnly(2026, 1, 31);
        _profiles.ListIncomeSourcesAsync(profileId, Arg.Any<CancellationToken>()).Returns([
            new IncomeSource { Id = 1, BudgetProfileId = profileId, Name = "Salary", AnchorDate = anchor, PaymentFrequency = "monthly", EarlyArrivalToleranceDays = 0 },
        ]);
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = adminId, Name = "x", Cycle = "monthly" });
        _profiles.GetLatestPeriodAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = Guid.NewGuid(), BudgetProfileId = profileId, EndDate = periodEnd });
        _profiles.AddIncomeSourceAsync(Arg.Any<IncomeSource>(), Arg.Any<CancellationToken>())
            .Returns(ci => { var s = ci.Arg<IncomeSource>(); s.Id = 2; return s; });

        var result = await new UpdateIncomeSourceCommandHandler(Access, _profiles, TaxReserve)
            .Handle(new UpdateIncomeSourceCommand(adminId, 1, profileId, "Salary", "salary", new Money(6000, 0), true, null, "monthly", true, anchor, 3), CancellationToken.None);

        Assert.Equal(2, result.Id);
        Assert.Equal(3, result.EarlyArrivalToleranceDays);
        await _profiles.Received(1).SetIncomeSourceEffectiveToAsync(1, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_AdjustForWeekendsOnlyChanged_ForksNewRow()
    {
        var (adminId, profileId) = SetUpOwner();
        var anchor = new DateOnly(2026, 1, 1);
        var periodEnd = new DateOnly(2026, 1, 31);
        _profiles.ListIncomeSourcesAsync(profileId, Arg.Any<CancellationToken>()).Returns([
            new IncomeSource { Id = 1, BudgetProfileId = profileId, Name = "Salary", AnchorDate = anchor, PaymentFrequency = "monthly", AdjustForWeekends = false },
        ]);
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = adminId, Name = "x", Cycle = "monthly" });
        _profiles.GetLatestPeriodAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = Guid.NewGuid(), BudgetProfileId = profileId, EndDate = periodEnd });
        _profiles.AddIncomeSourceAsync(Arg.Any<IncomeSource>(), Arg.Any<CancellationToken>())
            .Returns(ci => { var s = ci.Arg<IncomeSource>(); s.Id = 2; return s; });

        var result = await new UpdateIncomeSourceCommandHandler(Access, _profiles, TaxReserve)
            .Handle(new UpdateIncomeSourceCommand(adminId, 1, profileId, "Salary", "salary", new Money(6000, 0), true, null, "monthly", true, anchor, 0, AdjustForWeekends: true), CancellationToken.None);

        Assert.Equal(2, result.Id);
        Assert.True(result.AdjustForWeekends);
        await _profiles.Received(1).SetIncomeSourceEffectiveToAsync(1, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        await _profiles.DidNotReceive().UpdateIncomeSourceAsync(Arg.Any<IncomeSource>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_ScheduleChanged_NoExistingPeriod_FallsBackToInPlaceUpdate()
    {
        var (adminId, profileId) = SetUpOwner();
        var oldAnchor = new DateOnly(2026, 1, 1);
        var newAnchor = new DateOnly(2026, 1, 15);
        _profiles.ListIncomeSourcesAsync(profileId, Arg.Any<CancellationToken>()).Returns([
            new IncomeSource { Id = 1, BudgetProfileId = profileId, Name = "Salary", AnchorDate = oldAnchor, PaymentFrequency = "monthly" },
        ]);
        _profiles.GetLatestPeriodAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BudgetPeriod>(new NotFoundException("budget_period", "latest for " + profileId)));
        _profiles.UpdateIncomeSourceAsync(Arg.Any<IncomeSource>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IncomeSource>());

        var result = await new UpdateIncomeSourceCommandHandler(Access, _profiles, TaxReserve)
            .Handle(new UpdateIncomeSourceCommand(adminId, 1, profileId, "Salary", "salary", new Money(6000, 0), true, null, "monthly", true, newAnchor, 0), CancellationToken.None);

        Assert.Equal(1, result.Id);
        await _profiles.DidNotReceive().AddIncomeSourceAsync(Arg.Any<IncomeSource>(), Arg.Any<CancellationToken>());
        await _profiles.DidNotReceive().SetIncomeSourceEffectiveToAsync(Arg.Any<int>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_MissingSource_ThrowsNotFound()
    {
        var (adminId, profileId) = SetUpOwner();
        _profiles.ListIncomeSourcesAsync(profileId, Arg.Any<CancellationToken>()).Returns([]);

        await Assert.ThrowsAsync<NotFoundException>(() => new UpdateIncomeSourceCommandHandler(Access, _profiles, TaxReserve)
            .Handle(new UpdateIncomeSourceCommand(adminId, 99, profileId, "Salary", "salary", new Money(6000, 0), true, null, "monthly", true), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_ViewerForbidden()
    {
        var (_, profileId) = SetUpOwner();
        var viewerId = Guid.NewGuid();
        _profiles.GetPersonByUserIdAsync(profileId, viewerId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 1, BudgetProfileId = profileId, UserId = viewerId, Role = "viewer" });

        await Assert.ThrowsAsync<ForbiddenException>(() => new DeleteIncomeSourceCommandHandler(Access, _profiles, TaxReserve)
            .Handle(new DeleteIncomeSourceCommand(viewerId, 1, profileId), CancellationToken.None));
    }

    [Fact]
    public async Task ListEntries_ResolvesProfileFromPeriod()
    {
        var (adminId, profileId) = SetUpOwner();
        var periodId = Guid.NewGuid();
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId });
        _profiles.ListIncomeEntriesAsync(periodId, Arg.Any<CancellationToken>()).Returns([]);

        var result = await new ListIncomeEntriesQueryHandler(Access, _profiles)
            .Handle(new ListIncomeEntriesQuery(adminId, periodId), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task UpdateEntry_Collaborator_Succeeds()
    {
        var (adminId, profileId) = SetUpOwner();
        var periodId = Guid.NewGuid();
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, IsArchived = false });
        _profiles.UpdateIncomeEntryAsync(1, periodId, 250m, Arg.Any<CancellationToken>())
            .Returns(new IncomeEntry { Id = 1, BudgetPeriodId = periodId, Amount = 250m });

        var result = await new UpdateIncomeEntryCommandHandler(Access, _profiles)
            .Handle(new UpdateIncomeEntryCommand(adminId, 1, periodId, new Money(250, 0)), CancellationToken.None);

        Assert.Equal(250, result.Amount.Units);
    }
}
