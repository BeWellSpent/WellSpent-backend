using NSubstitute;
using WellSpent.Application.Common;
using WellSpent.Application.ExpenseAllocations;
using WellSpent.Application.ExpenseAllocations.DeleteExpenseAllocation;
using WellSpent.Application.ExpenseAllocations.ListExpenseAllocations;
using WellSpent.Application.ExpenseAllocations.UpsertExpenseAllocation;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.ExpenseAllocations;

public sealed class ExpenseAllocationHandlerTests
{
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();
    private readonly IExpenseAllocationRepository _allocations = Substitute.For<IExpenseAllocationRepository>();
    private BudgetAccessGuard Access => new(_profiles);

    private (Guid AdminId, Guid ProfileId) SetUpProfile()
    {
        var profileId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = adminId, Name = "B" });
        return (adminId, profileId);
    }

    private (Guid ViewerId, Guid ProfileId) SetUpViewer()
    {
        var (_, profileId) = SetUpProfile();
        var viewerId = Guid.NewGuid();
        _profiles.GetPersonByUserIdAsync(profileId, viewerId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 1, BudgetProfileId = profileId, UserId = viewerId, Role = "viewer" });
        return (viewerId, profileId);
    }

    private (Guid CollaboratorId, Guid ProfileId) SetUpCollaborator()
    {
        var (_, profileId) = SetUpProfile();
        var collaboratorId = Guid.NewGuid();
        _profiles.GetPersonByUserIdAsync(profileId, collaboratorId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 2, BudgetProfileId = profileId, UserId = collaboratorId, Role = "collaborator" });
        return (collaboratorId, profileId);
    }

    [Fact]
    public async Task List_Member_Succeeds()
    {
        var (adminId, profileId) = SetUpProfile();
        _allocations.ListAsync(profileId, Arg.Any<CancellationToken>())
            .Returns([new ExpenseAllocation { Id = 1, BudgetProfileId = profileId, CategoryId = 5, PlannedAmount = 50m }]);

        var result = await new ListExpenseAllocationsQueryHandler(Access, _allocations)
            .Handle(new ListExpenseAllocationsQuery(adminId, profileId), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(50m, result[0].PlannedAmount.ToDecimal());
    }

    [Fact]
    public async Task List_ViewerAllowed()
    {
        var (viewerId, profileId) = SetUpViewer();
        _allocations.ListAsync(profileId, Arg.Any<CancellationToken>()).Returns([]);

        var result = await new ListExpenseAllocationsQueryHandler(Access, _allocations)
            .Handle(new ListExpenseAllocationsQuery(viewerId, profileId), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task List_NonMember_ThrowsNotFound()
    {
        var (_, profileId) = SetUpProfile();
        var strangerId = Guid.NewGuid();
        _profiles.GetPersonByUserIdAsync(profileId, strangerId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BudgetPerson>(new NotFoundException("budget_person", strangerId.ToString())));

        await Assert.ThrowsAsync<NotFoundException>(() => new ListExpenseAllocationsQueryHandler(Access, _allocations)
            .Handle(new ListExpenseAllocationsQuery(strangerId, profileId), CancellationToken.None));
    }

    [Fact]
    public async Task Upsert_Admin_Succeeds()
    {
        var (adminId, profileId) = SetUpProfile();
        _allocations.UpsertAsync(profileId, 5, null, 75m, Arg.Any<CancellationToken>())
            .Returns(new ExpenseAllocation { Id = 1, BudgetProfileId = profileId, CategoryId = 5, PlannedAmount = 75m });

        var result = await new UpsertExpenseAllocationCommandHandler(Access, _allocations)
            .Handle(new UpsertExpenseAllocationCommand(adminId, profileId, 5, null, new Money(75, 0)), CancellationToken.None);

        Assert.Equal(75m, result.PlannedAmount.ToDecimal());
        Assert.Equal(0, result.BudgetPersonId);
    }

    [Fact]
    public async Task Upsert_CollaboratorAllowed()
    {
        var (collaboratorId, profileId) = SetUpCollaborator();
        _allocations.UpsertAsync(profileId, 5, 3, 20m, Arg.Any<CancellationToken>())
            .Returns(new ExpenseAllocation { Id = 2, BudgetProfileId = profileId, CategoryId = 5, BudgetPersonId = 3, PlannedAmount = 20m });

        var result = await new UpsertExpenseAllocationCommandHandler(Access, _allocations)
            .Handle(new UpsertExpenseAllocationCommand(collaboratorId, profileId, 5, 3, new Money(20, 0)), CancellationToken.None);

        Assert.Equal(3, result.BudgetPersonId);
        await _allocations.Received(1).UpsertAsync(profileId, 5, 3, 20m, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Upsert_ViewerForbidden()
    {
        var (viewerId, profileId) = SetUpViewer();

        await Assert.ThrowsAsync<ForbiddenException>(() => new UpsertExpenseAllocationCommandHandler(Access, _allocations)
            .Handle(new UpsertExpenseAllocationCommand(viewerId, profileId, 5, null, new Money(10, 0)), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_Admin_Succeeds()
    {
        var (adminId, profileId) = SetUpProfile();

        await new DeleteExpenseAllocationCommandHandler(Access, _allocations)
            .Handle(new DeleteExpenseAllocationCommand(adminId, 1, profileId), CancellationToken.None);

        await _allocations.Received(1).DeleteAsync(1, profileId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_CollaboratorAllowed()
    {
        var (collaboratorId, profileId) = SetUpCollaborator();

        await new DeleteExpenseAllocationCommandHandler(Access, _allocations)
            .Handle(new DeleteExpenseAllocationCommand(collaboratorId, 1, profileId), CancellationToken.None);

        await _allocations.Received(1).DeleteAsync(1, profileId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_ViewerForbidden()
    {
        var (viewerId, profileId) = SetUpViewer();

        await Assert.ThrowsAsync<ForbiddenException>(() => new DeleteExpenseAllocationCommandHandler(Access, _allocations)
            .Handle(new DeleteExpenseAllocationCommand(viewerId, 1, profileId), CancellationToken.None));

        await _allocations.DidNotReceive().DeleteAsync(Arg.Any<int>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
