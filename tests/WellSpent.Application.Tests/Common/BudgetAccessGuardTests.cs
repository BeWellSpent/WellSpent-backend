using NSubstitute;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Common;

public sealed class BudgetAccessGuardTests
{
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();
    private BudgetAccessGuard CreateGuard() => new(_profiles);

    [Fact]
    public async Task EnsureMember_Owner_Succeeds()
    {
        var profileId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = ownerId, Name = "Budget" });

        var profile = await CreateGuard().EnsureMemberAsync(profileId, ownerId, CancellationToken.None);

        Assert.Equal(profileId, profile.Id);
    }

    [Fact]
    public async Task EnsureMember_ActiveNonOwnerMember_Succeeds()
    {
        var profileId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = Guid.NewGuid(), Name = "Budget" });
        _profiles.GetPersonByUserIdAsync(profileId, memberId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 1, BudgetProfileId = profileId, UserId = memberId, Role = "viewer" });

        await CreateGuard().EnsureMemberAsync(profileId, memberId, CancellationToken.None);
    }

    [Fact]
    public async Task EnsureMember_NonMember_PropagatesNotFoundException_NotForbidden()
    {
        var profileId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = Guid.NewGuid(), Name = "Budget" });
        _profiles.GetPersonByUserIdAsync(profileId, strangerId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BudgetPerson>(new NotFoundException("budget_person", strangerId.ToString())));

        // This specific check surfaces as 404, not 403 — mirrors Go's
        // UpsertSubscription exactly (see BudgetAccessGuard's doc comment).
        await Assert.ThrowsAsync<NotFoundException>(
            () => CreateGuard().EnsureMemberAsync(profileId, strangerId, CancellationToken.None));
    }

    [Fact]
    public async Task EnsureAdmin_Owner_Succeeds()
    {
        var profileId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = ownerId, Name = "Budget" });

        await CreateGuard().EnsureAdminAsync(profileId, ownerId, CancellationToken.None);
    }

    [Fact]
    public async Task EnsureAdmin_NonOwnerAdmin_Succeeds()
    {
        var profileId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = Guid.NewGuid(), Name = "Budget" });
        _profiles.GetPersonByUserIdAsync(profileId, adminId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 1, BudgetProfileId = profileId, UserId = adminId, Role = "admin" });

        await CreateGuard().EnsureAdminAsync(profileId, adminId, CancellationToken.None);
    }

    [Fact]
    public async Task EnsureAdmin_NonAdminMember_ThrowsForbidden()
    {
        var profileId = Guid.NewGuid();
        var viewerId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = Guid.NewGuid(), Name = "Budget" });
        _profiles.GetPersonByUserIdAsync(profileId, viewerId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 1, BudgetProfileId = profileId, UserId = viewerId, Role = "viewer" });

        await Assert.ThrowsAsync<ForbiddenException>(
            () => CreateGuard().EnsureAdminAsync(profileId, viewerId, CancellationToken.None));
    }

    [Fact]
    public async Task EnsureAdmin_NonMember_ThrowsForbidden_NotNotFound()
    {
        var profileId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = Guid.NewGuid(), Name = "Budget" });
        _profiles.GetPersonByUserIdAsync(profileId, strangerId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BudgetPerson>(new NotFoundException("budget_person", strangerId.ToString())));

        // Both "not a member" and "member but not admin" collapse to the
        // same ForbiddenException — mirrors Go's Send/List/Cancel exactly.
        await Assert.ThrowsAsync<ForbiddenException>(
            () => CreateGuard().EnsureAdminAsync(profileId, strangerId, CancellationToken.None));
    }

    [Fact]
    public async Task EnsureCollaboratorOfPeriod_ClosedPeriod_IsAllowed() // #120
    {
        var profileId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, IsArchived = true });
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = ownerId, Name = "Budget" });

        var period = await CreateGuard().EnsureCollaboratorOfPeriodAsync(periodId, ownerId, CancellationToken.None);

        Assert.True(period.IsArchived);
    }

    [Fact]
    public async Task EnsureCollaboratorOfPeriod_Viewer_StillForbidden()
    {
        var profileId = Guid.NewGuid();
        var viewerId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId, IsArchived = true });
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = Guid.NewGuid(), Name = "Budget" });
        _profiles.GetPersonByUserIdAsync(profileId, viewerId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 2, BudgetProfileId = profileId, UserId = viewerId, Role = "viewer" });

        await Assert.ThrowsAsync<WellSpent.Domain.Exceptions.ForbiddenException>(() =>
            CreateGuard().EnsureCollaboratorOfPeriodAsync(periodId, viewerId, CancellationToken.None));
    }
}
