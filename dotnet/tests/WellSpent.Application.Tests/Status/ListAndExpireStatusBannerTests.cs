using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Common;
using WellSpent.Application.Status;
using WellSpent.Application.Status.ExpireStatusBanner;
using WellSpent.Application.Status.ListStatusBanners;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Status;

public sealed class ListStatusBannersQueryHandlerTests
{
    private readonly IStatusBannerRepository _banners = Substitute.For<IStatusBannerRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private static readonly IMapper Mapper = new MapperConfiguration(
        cfg => cfg.AddProfile<StatusBannerMappingProfile>(), NullLoggerFactory.Instance).CreateMapper();

    [Fact]
    public async Task NotSuperuser_Throws()
    {
        var id = Guid.NewGuid();
        _users.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(new User { Id = id, Email = "x@example.com", IsSuperuser = false });

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            new ListStatusBannersQueryHandler(_banners, new SuperuserGuard(_users), Mapper)
                .Handle(new ListStatusBannersQuery(id, 0), CancellationToken.None));
    }

    [Fact]
    public async Task ZeroLimit_UsesDefault()
    {
        var id = Guid.NewGuid();
        _users.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(new User { Id = id, Email = "x@example.com", IsSuperuser = true });
        _banners.ListAsync(StatusBannerConstants.DefaultListLimit, Arg.Any<CancellationToken>()).Returns([]);

        await new ListStatusBannersQueryHandler(_banners, new SuperuserGuard(_users), Mapper)
            .Handle(new ListStatusBannersQuery(id, 0), CancellationToken.None);

        await _banners.Received(1).ListAsync(StatusBannerConstants.DefaultListLimit, Arg.Any<CancellationToken>());
    }
}

public sealed class ExpireStatusBannerCommandHandlerTests
{
    private readonly IStatusBannerRepository _banners = Substitute.For<IStatusBannerRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private static readonly IMapper Mapper = new MapperConfiguration(
        cfg => cfg.AddProfile<StatusBannerMappingProfile>(), NullLoggerFactory.Instance).CreateMapper();

    [Fact]
    public async Task NotSuperuser_Throws()
    {
        var id = Guid.NewGuid();
        _users.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(new User { Id = id, Email = "x@example.com", IsSuperuser = false });

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            new ExpireStatusBannerCommandHandler(_banners, new SuperuserGuard(_users), Mapper)
                .Handle(new ExpireStatusBannerCommand(id, Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Success_ReturnsExpiredBanner()
    {
        var id = Guid.NewGuid();
        var bannerId = Guid.NewGuid();
        _users.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(new User { Id = id, Email = "x@example.com", IsSuperuser = true });
        var expired = new StatusBanner { Id = bannerId, Severity = "info", MessageEn = "x", EndsAt = DateTime.UtcNow };
        _banners.ExpireAsync(bannerId, Arg.Any<CancellationToken>()).Returns(expired);

        var result = await new ExpireStatusBannerCommandHandler(_banners, new SuperuserGuard(_users), Mapper)
            .Handle(new ExpireStatusBannerCommand(id, bannerId), CancellationToken.None);

        Assert.Equal(bannerId, result.Id);
    }
}
