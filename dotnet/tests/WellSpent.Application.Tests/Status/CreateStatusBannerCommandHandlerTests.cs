using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Common;
using WellSpent.Application.Status;
using WellSpent.Application.Status.CreateStatusBanner;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Status;

public sealed class CreateStatusBannerCommandHandlerTests
{
    private readonly IStatusBannerRepository _banners = Substitute.For<IStatusBannerRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private static readonly IMapper Mapper = new MapperConfiguration(
        cfg => cfg.AddProfile<StatusBannerMappingProfile>(), NullLoggerFactory.Instance).CreateMapper();

    private CreateStatusBannerCommandHandler CreateHandler() => new(_banners, new SuperuserGuard(_users), Mapper);

    private Guid SetUpSuperuser()
    {
        var id = Guid.NewGuid();
        _users.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(new User { Id = id, Email = "admin@example.com", IsSuperuser = true });
        return id;
    }

    private void SetUpNonSuperuser(Guid id) =>
        _users.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(new User { Id = id, Email = "user@example.com", IsSuperuser = false });

    [Fact]
    public async Task NotSuperuser_ThrowsForbidden()
    {
        var id = Guid.NewGuid();
        SetUpNonSuperuser(id);

        await Assert.ThrowsAsync<ForbiddenException>(() => CreateHandler().Handle(
            new CreateStatusBannerCommand(id, "info", "Hello", "", null, DateTime.UtcNow.AddHours(1)), CancellationToken.None));
    }

    [Fact]
    public async Task Success_NoStartsAt_DefaultsToNow()
    {
        var id = SetUpSuperuser();
        _banners.CreateAsync(Arg.Any<StatusBanner>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<StatusBanner>());

        var before = DateTime.UtcNow;
        var result = await CreateHandler().Handle(
            new CreateStatusBannerCommand(id, "INFO", "Hello", "", null, DateTime.UtcNow.AddHours(1)), CancellationToken.None);

        Assert.True(result.StartsAt >= before);
        Assert.Equal("info", result.Severity);
    }

    [Theory]
    [InlineData("")]
    [InlineData("urgent")]
    public async Task InvalidSeverity_Throws(string severity)
    {
        var id = SetUpSuperuser();

        await Assert.ThrowsAsync<AppValidationException>(() => CreateHandler().Handle(
            new CreateStatusBannerCommand(id, severity, "Hello", "", null, DateTime.UtcNow.AddHours(1)), CancellationToken.None));
    }

    [Fact]
    public async Task EmptyMessageEn_Throws()
    {
        var id = SetUpSuperuser();

        await Assert.ThrowsAsync<AppValidationException>(() => CreateHandler().Handle(
            new CreateStatusBannerCommand(id, "info", "   ", "", null, DateTime.UtcNow.AddHours(1)), CancellationToken.None));
    }

    [Fact]
    public async Task MessageEnTooLong_Throws()
    {
        var id = SetUpSuperuser();
        var tooLong = new string('a', 301);

        await Assert.ThrowsAsync<AppValidationException>(() => CreateHandler().Handle(
            new CreateStatusBannerCommand(id, "info", tooLong, "", null, DateTime.UtcNow.AddHours(1)), CancellationToken.None));
    }

    [Fact]
    public async Task MessageEsTooLong_Throws()
    {
        var id = SetUpSuperuser();
        var tooLong = new string('a', 301);

        await Assert.ThrowsAsync<AppValidationException>(() => CreateHandler().Handle(
            new CreateStatusBannerCommand(id, "info", "Hello", tooLong, null, DateTime.UtcNow.AddHours(1)), CancellationToken.None));
    }

    [Fact]
    public async Task MissingEndsAt_Throws()
    {
        var id = SetUpSuperuser();

        await Assert.ThrowsAsync<AppValidationException>(() => CreateHandler().Handle(
            new CreateStatusBannerCommand(id, "info", "Hello", "", null, default), CancellationToken.None));
    }

    [Fact]
    public async Task EndsAtBeforeStartsAt_Throws()
    {
        var id = SetUpSuperuser();
        var starts = DateTime.UtcNow.AddHours(2);
        var ends = DateTime.UtcNow.AddHours(1);

        await Assert.ThrowsAsync<AppValidationException>(() => CreateHandler().Handle(
            new CreateStatusBannerCommand(id, "info", "Hello", "", starts, ends), CancellationToken.None));
    }

    [Fact]
    public async Task EndsAtInThePast_Throws()
    {
        var id = SetUpSuperuser();

        await Assert.ThrowsAsync<AppValidationException>(() => CreateHandler().Handle(
            new CreateStatusBannerCommand(id, "info", "Hello", "", DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(-1)), CancellationToken.None));
    }
}
