using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Budgets;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using Xunit;

namespace WellSpent.Application.Tests.Budgets;

public sealed class TaxReserveRecalculatorTests
{
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private TaxReserveRecalculator CreateRecalculator() => new(_profiles, _users, NullLogger<TaxReserveRecalculator>.Instance);

    private (Guid ProfileId, Guid OwnerId) SetUpUsProfile(int ownerPersonId = 1)
    {
        var profileId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = ownerId, Name = "x", CountryCode = "US" });
        _profiles.ListPeopleAsync(profileId, Arg.Any<CancellationToken>())
            .Returns([new BudgetPerson { Id = ownerPersonId, BudgetProfileId = profileId, UserId = ownerId, Role = "admin" }]);
        _users.GetByIdAsync(ownerId, Arg.Any<CancellationToken>())
            .Returns(new User { Id = ownerId, Email = "owner@example.com", StateCode = "CA", FilingStatus = "1" });
        return (profileId, ownerId);
    }

    [Fact]
    public async Task NonUsProfile_SkipsEntirely()
    {
        var profileId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = ownerId, Name = "x", CountryCode = "AR" });

        await CreateRecalculator().RecalculateAsync(profileId, CancellationToken.None);

        await _profiles.DidNotReceive().DeleteTaxReserveSavingsSourceAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoBeforeTaxIncome_ClearsExistingReserve_CreatesNothing()
    {
        var (profileId, _) = SetUpUsProfile();
        _profiles.ListIncomeSourcesAsync(profileId, Arg.Any<CancellationToken>()).Returns([
            new IncomeSource { Id = 1, BudgetProfileId = profileId, Name = "Freelance", BeforeTax = false, DefaultAmount = 2000m },
        ]);

        await CreateRecalculator().RecalculateAsync(profileId, CancellationToken.None);

        await _profiles.Received(1).DeleteTaxReserveSavingsSourceAsync(profileId, Arg.Any<CancellationToken>());
        await _profiles.DidNotReceive().UpsertTaxReserveSavingsSourceAsync(
            Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnattributedBeforeTaxIncome_FallsBackToOwnerPerson()
    {
        var (profileId, _) = SetUpUsProfile(ownerPersonId: 7);
        _profiles.ListIncomeSourcesAsync(profileId, Arg.Any<CancellationToken>()).Returns([
            new IncomeSource { Id = 1, BudgetProfileId = profileId, Name = "Salary", BeforeTax = true, DefaultAmount = 5000m, BudgetPersonId = null },
        ]);

        await CreateRecalculator().RecalculateAsync(profileId, CancellationToken.None);

        await _profiles.Received(1).UpsertTaxReserveSavingsSourceAsync(
            profileId, 7, Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BeforeTaxIncome_AnnualizesMonthlyAmount()
    {
        var (profileId, _) = SetUpUsProfile();
        _profiles.ListIncomeSourcesAsync(profileId, Arg.Any<CancellationToken>()).Returns([
            new IncomeSource { Id = 1, BudgetProfileId = profileId, Name = "Salary", BeforeTax = true, DefaultAmount = 10_000m, BudgetPersonId = 1 },
        ]);
        decimal? capturedAmount = null;
        _profiles.UpsertTaxReserveSavingsSourceAsync(profileId, 1, Arg.Do<decimal>(a => capturedAmount = a), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(new SavingsSource { Id = 1, BudgetProfileId = profileId, Name = "Future Tax Payment", Amount = 0 });

        await CreateRecalculator().RecalculateAsync(profileId, CancellationToken.None);

        // Annual income = 10,000 * 12 = 120,000. Monthly reserve should be a
        // small positive fraction of that (federal + CA state on 120k/yr).
        Assert.NotNull(capturedAmount);
        Assert.True(capturedAmount > 0);
        Assert.True(capturedAmount < 10_000m);
    }

    [Fact]
    public async Task LinkedPersonUsesTheirOwnStateAndFilingStatus_NotOwners()
    {
        var (profileId, ownerId) = SetUpUsProfile(ownerPersonId: 1);
        var otherUserId = Guid.NewGuid();
        _profiles.ListPeopleAsync(profileId, Arg.Any<CancellationToken>()).Returns([
            new BudgetPerson { Id = 1, BudgetProfileId = profileId, UserId = ownerId, Role = "admin" },
            new BudgetPerson { Id = 2, BudgetProfileId = profileId, UserId = otherUserId, Role = "collaborator" },
        ]);
        _users.GetByIdAsync(otherUserId, Arg.Any<CancellationToken>())
            .Returns(new User { Id = otherUserId, Email = "other@example.com", StateCode = "TX", FilingStatus = "1" });
        _profiles.ListIncomeSourcesAsync(profileId, Arg.Any<CancellationToken>()).Returns([
            new IncomeSource { Id = 1, BudgetProfileId = profileId, Name = "Salary", BeforeTax = true, DefaultAmount = 10_000m, BudgetPersonId = 2 },
        ]);
        decimal? capturedState = null;
        _profiles.UpsertTaxReserveSavingsSourceAsync(profileId, 2, Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Do<decimal>(s => capturedState = s), Arg.Any<CancellationToken>())
            .Returns(new SavingsSource { Id = 2, BudgetProfileId = profileId, Name = "Future Tax Payment", Amount = 0 });

        await CreateRecalculator().RecalculateAsync(profileId, CancellationToken.None);

        // TX has no state income tax — the linked person's own settings
        // (not the CA owner's) must be the ones used.
        Assert.Equal(0m, capturedState);
    }
}
