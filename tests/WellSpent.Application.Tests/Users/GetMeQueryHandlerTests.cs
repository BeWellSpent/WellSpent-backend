using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Users;
using WellSpent.Application.Users.GetMe;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using Xunit;

namespace WellSpent.Application.Tests.Users;

public sealed class GetMeQueryHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private static readonly IMapper Mapper = new MapperConfiguration(
        cfg => cfg.AddProfile<UserMappingProfile>(), NullLoggerFactory.Instance).CreateMapper();

    [Fact]
    public async Task Handle_ReturnsMappedDto()
    {
        var user = new User
        {
            Id = Guid.NewGuid(), Email = "a@example.com", FirstName = "A", LastName = "B",
            IsActive = true, IsVerified = true, Language = "en", Currency = "USD",
            FilingStatus = "2", TaxPaymentFrequency = 3, Plan = "pro",
        };
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var dto = await new GetMeQueryHandler(_users, Mapper).Handle(new GetMeQuery(user.Id), CancellationToken.None);

        Assert.Equal(user.Id, dto.Id);
        Assert.Equal(2, dto.FilingStatus);
        Assert.Equal(3, dto.TaxPaymentFrequency);
        Assert.Equal("pro", dto.Plan);
        Assert.True(dto.IsVerified);
    }

    [Fact]
    public async Task Handle_TestAccount_ReportsVerifiedRegardlessOfStoredFlag()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "qa@example.com", IsVerified = false, AccountType = "test" };
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var dto = await new GetMeQueryHandler(_users, Mapper).Handle(new GetMeQuery(user.Id), CancellationToken.None);

        Assert.True(dto.IsVerified);
    }

    [Fact]
    public async Task Handle_ApplePrivateRelayEmail_FlagsHasApplePrivateEmail()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "abc123@privaterelay.appleid.com" };
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var dto = await new GetMeQueryHandler(_users, Mapper).Handle(new GetMeQuery(user.Id), CancellationToken.None);

        Assert.True(dto.HasApplePrivateEmail);
    }
}
