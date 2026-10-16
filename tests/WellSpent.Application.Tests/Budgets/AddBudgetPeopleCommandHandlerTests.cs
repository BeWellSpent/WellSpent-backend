using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Budgets;
using WellSpent.Application.Budgets.AddBudgetPeople;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Budgets;

public sealed class AddBudgetPeopleCommandHandlerTests
{
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private static readonly IMapper Mapper = new MapperConfiguration(
        cfg => cfg.AddProfile<BudgetMappingProfile>(), NullLoggerFactory.Instance).CreateMapper();

    private AddBudgetPeopleCommandHandler CreateHandler() => new(new BudgetAccessGuard(_profiles), _profiles, _users, Mapper);

    private (Guid AdminId, Guid ProfileId) SetUpOwner(string plan = "pro", string? countryCode = "US")
    {
        var profileId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = adminId, Name = "Shared Budget", CountryCode = countryCode });
        _users.GetByIdAsync(adminId, Arg.Any<CancellationToken>())
            .Returns(new User { Id = adminId, Email = "admin@example.com", Plan = plan, CountryCode = countryCode });
        return (adminId, profileId);
    }

    [Fact]
    public async Task NonAdmin_ThrowsForbidden()
    {
        var (_, profileId) = SetUpOwner();
        var viewerId = Guid.NewGuid();
        _profiles.GetPersonByUserIdAsync(profileId, viewerId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 1, BudgetProfileId = profileId, UserId = viewerId, Role = "viewer" });

        await Assert.ThrowsAsync<ForbiddenException>(() => CreateHandler().Handle(
            new AddBudgetPeopleCommand(viewerId, profileId, [new NewBudgetPersonInput("Jane", null, "")]), CancellationToken.None));
    }

    [Fact]
    public async Task FreeTier_OverTwoPeople_Throws()
    {
        var (adminId, profileId) = SetUpOwner(plan: "free");
        _profiles.ListPeopleAsync(profileId, Arg.Any<CancellationToken>())
            .Returns([new BudgetPerson { Id = 1, BudgetProfileId = profileId, Role = "admin", IsActive = true }]);

        await Assert.ThrowsAsync<AppValidationException>(() => CreateHandler().Handle(
            new AddBudgetPeopleCommand(adminId, profileId,
                [new NewBudgetPersonInput("Jane", null, ""), new NewBudgetPersonInput("Jo", null, "")]), CancellationToken.None));
    }

    [Fact]
    public async Task FreeTier_ExactlyTwoTotal_Succeeds()
    {
        var (adminId, profileId) = SetUpOwner(plan: "free");
        _profiles.ListPeopleAsync(profileId, Arg.Any<CancellationToken>())
            .Returns([new BudgetPerson { Id = 1, BudgetProfileId = profileId, Role = "admin", IsActive = true }]);
        _profiles.AddPersonAsync(Arg.Any<BudgetPerson>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<BudgetPerson>());

        var result = await CreateHandler().Handle(
            new AddBudgetPeopleCommand(adminId, profileId, [new NewBudgetPersonInput("Jane", null, "")]), CancellationToken.None);

        Assert.Single(result);
    }

    [Fact]
    public async Task LinkedPersonInDifferentCountry_Throws()
    {
        var (adminId, profileId) = SetUpOwner(countryCode: "US");
        var otherUserId = Guid.NewGuid();
        _users.GetByIdAsync(otherUserId, Arg.Any<CancellationToken>())
            .Returns(new User { Id = otherUserId, Email = "x@example.com", CountryCode = "AR" });

        await Assert.ThrowsAsync<AppValidationException>(() => CreateHandler().Handle(
            new AddBudgetPeopleCommand(adminId, profileId, [new NewBudgetPersonInput("Remote", otherUserId, "")]), CancellationToken.None));
    }

    [Fact]
    public async Task DuplicateName_Throws()
    {
        var (adminId, profileId) = SetUpOwner();
        _profiles.ExistsPersonAsync(profileId, "Jane", Arg.Any<CancellationToken>()).Returns(true);

        await Assert.ThrowsAsync<DuplicateException>(() => CreateHandler().Handle(
            new AddBudgetPeopleCommand(adminId, profileId, [new NewBudgetPersonInput("Jane", null, "")]), CancellationToken.None));
    }

    [Fact]
    public async Task LinkedPerson_GetsCollaboratorRole_UnlinkedGetsUnspecified()
    {
        var (adminId, profileId) = SetUpOwner();
        var linkedUserId = Guid.NewGuid();
        _users.GetByIdAsync(linkedUserId, Arg.Any<CancellationToken>())
            .Returns(new User { Id = linkedUserId, Email = "linked@example.com", CountryCode = "US" });
        var captured = new List<BudgetPerson>();
        _profiles.AddPersonAsync(Arg.Any<BudgetPerson>(), Arg.Any<CancellationToken>())
            .Returns(ci => { var p = ci.Arg<BudgetPerson>(); captured.Add(p); return p; });

        await CreateHandler().Handle(new AddBudgetPeopleCommand(adminId, profileId,
            [new NewBudgetPersonInput("Linked", linkedUserId, ""), new NewBudgetPersonInput("Placeholder", null, "")]), CancellationToken.None);

        Assert.Equal("collaborator", captured[0].Role);
        Assert.Equal("unspecified", captured[1].Role);
    }
}
