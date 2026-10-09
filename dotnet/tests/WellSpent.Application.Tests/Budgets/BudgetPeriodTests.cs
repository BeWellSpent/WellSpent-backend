using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Budgets;
using WellSpent.Application.Budgets.CreateBudgetPeriod;
using WellSpent.Application.Budgets.GetBudgetPeriod;
using WellSpent.Application.Budgets.ListBudgetPeriods;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Budgets;

public sealed class BudgetPeriodDatesTests
{
    [Theory]
    [InlineData("weekly", "2026-01-05", "2026-01-11")]
    [InlineData("bi_weekly", "2026-01-05", "2026-01-18")]
    [InlineData("yearly", "2026-06-15", "2026-12-31")]
    [InlineData("monthly", "2026-02-10", "2026-02-28")]
    public void ComputeFirst_MatchesGoFormula(string cycle, string todayStr, string expectedEndStr)
    {
        var today = DateOnly.Parse(todayStr);
        var (start, end) = BudgetPeriodDates.ComputeFirst(cycle, today);

        var expectedStart = cycle switch
        {
            "yearly" => new DateOnly(today.Year, 1, 1),
            "monthly" => new DateOnly(today.Year, today.Month, 1),
            _ => today,
        };
        Assert.Equal(expectedStart, start);
        Assert.Equal(DateOnly.Parse(expectedEndStr), end);
    }

    [Fact]
    public void ComputeNext_StartsDayAfterPreviousEnd()
    {
        var (start, end) = BudgetPeriodDates.ComputeNext("monthly", new DateOnly(2026, 1, 31));

        Assert.Equal(new DateOnly(2026, 2, 1), start);
        Assert.Equal(new DateOnly(2026, 2, 28), end);
    }

    [Fact]
    public void ComputeFirst_MonthlyLeapFebruary()
    {
        var (_, end) = BudgetPeriodDates.ComputeFirst("monthly", new DateOnly(2028, 2, 5));
        Assert.Equal(new DateOnly(2028, 2, 29), end);
    }
}

public sealed class BudgetPeriodRolloverTests
{
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();

    [Fact]
    public async Task NoExistingPeriod_CreatesFirstPeriod_DoesNotArchiveAnything()
    {
        var profile = new BudgetProfile { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "x", Cycle = "monthly" };
        _profiles.GetLatestPeriodAsync(profile.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BudgetPeriod>(new NotFoundException("budget_period", "latest")));
        _profiles.CreatePeriodAsync(Arg.Any<BudgetPeriod>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<BudgetPeriod>());

        var period = await BudgetPeriodRollover.CreateNextPeriodAsync(_profiles, profile, CancellationToken.None);

        Assert.Equal(profile.Id, period.BudgetProfileId);
        await _profiles.DidNotReceive().ArchivePeriodAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LatestPeriodStillActive_IsIdempotent_ReturnsLatestWithoutCreating()
    {
        var profile = new BudgetProfile { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "x", Cycle = "monthly" };
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var latest = new BudgetPeriod { Id = Guid.NewGuid(), BudgetProfileId = profile.Id, StartDate = today, EndDate = today.AddDays(10) };
        _profiles.GetLatestPeriodAsync(profile.Id, Arg.Any<CancellationToken>()).Returns(latest);

        var period = await BudgetPeriodRollover.CreateNextPeriodAsync(_profiles, profile, CancellationToken.None);

        Assert.Equal(latest.Id, period.Id);
        await _profiles.DidNotReceive().CreatePeriodAsync(Arg.Any<BudgetPeriod>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LatestPeriodEnded_CreatesNextPeriod_ArchivesPrevious()
    {
        var profile = new BudgetProfile { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "x", Cycle = "monthly" };
        var ended = new BudgetPeriod
        {
            Id = Guid.NewGuid(), BudgetProfileId = profile.Id,
            StartDate = new DateOnly(2020, 1, 1), EndDate = new DateOnly(2020, 1, 31),
        };
        _profiles.GetLatestPeriodAsync(profile.Id, Arg.Any<CancellationToken>()).Returns(ended);
        _profiles.CreatePeriodAsync(Arg.Any<BudgetPeriod>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<BudgetPeriod>());

        var period = await BudgetPeriodRollover.CreateNextPeriodAsync(_profiles, profile, CancellationToken.None);

        Assert.Equal(new DateOnly(2020, 2, 1), period.StartDate);
        await _profiles.Received(1).ArchivePeriodAsync(ended.Id, Arg.Any<CancellationToken>());
    }
}

public sealed class CreateBudgetPeriodCommandHandlerTests
{
    [Fact]
    public async Task NonAdmin_ThrowsForbidden()
    {
        var profiles = Substitute.For<IBudgetProfileRepository>();
        var profileId = Guid.NewGuid();
        var viewerId = Guid.NewGuid();
        profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = Guid.NewGuid(), Name = "x" });
        profiles.GetPersonByUserIdAsync(profileId, viewerId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 1, BudgetProfileId = profileId, UserId = viewerId, Role = "viewer" });
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<BudgetMappingProfile>(), NullLoggerFactory.Instance).CreateMapper();

        await Assert.ThrowsAsync<ForbiddenException>(() => new CreateBudgetPeriodCommandHandler(new BudgetAccessGuard(profiles), profiles, mapper)
            .Handle(new CreateBudgetPeriodCommand(viewerId, profileId), CancellationToken.None));
    }
}

public sealed class ListAndGetBudgetPeriodTests
{
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();
    private static readonly IMapper Mapper = new MapperConfiguration(
        cfg => cfg.AddProfile<BudgetMappingProfile>(), NullLoggerFactory.Instance).CreateMapper();

    [Fact]
    public async Task ListBudgetPeriods_NonMember_Throws()
    {
        var profileId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = Guid.NewGuid(), Name = "x" });
        _profiles.GetPersonByUserIdAsync(profileId, strangerId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BudgetPerson>(new NotFoundException("budget_person", strangerId.ToString())));

        await Assert.ThrowsAsync<NotFoundException>(() => new ListBudgetPeriodsQueryHandler(new BudgetAccessGuard(_profiles), _profiles, Mapper)
            .Handle(new ListBudgetPeriodsQuery(strangerId, profileId), CancellationToken.None));
    }

    [Fact]
    public async Task GetBudgetPeriod_ResolvesProfileFromPeriod_ChecksMembership()
    {
        var profileId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        _profiles.GetPeriodByIdAsync(periodId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPeriod { Id = periodId, BudgetProfileId = profileId });
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = ownerId, Name = "x" });

        var result = await new GetBudgetPeriodQueryHandler(new BudgetAccessGuard(_profiles), _profiles, Mapper)
            .Handle(new GetBudgetPeriodQuery(ownerId, periodId), CancellationToken.None);

        Assert.Equal(periodId, result.Id);
    }
}
