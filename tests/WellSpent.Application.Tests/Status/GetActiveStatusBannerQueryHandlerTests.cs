using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Status;
using WellSpent.Application.Status.GetActiveStatusBanner;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Status;

public sealed class GetActiveStatusBannerQueryHandlerTests
{
    private readonly IStatusBannerRepository _banners = Substitute.For<IStatusBannerRepository>();
    private static readonly IMapper Mapper = new MapperConfiguration(
        cfg => cfg.AddProfile<StatusBannerMappingProfile>(), NullLoggerFactory.Instance).CreateMapper();

    [Fact]
    public async Task ActiveBannerExists_ReturnsIt()
    {
        var banner = new StatusBanner
        {
            Id = Guid.NewGuid(), Severity = "critical", MessageEn = "Down", MessageEs = "",
            StartsAt = DateTime.UtcNow.AddHours(-1), EndsAt = DateTime.UtcNow.AddHours(1),
        };
        _banners.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(banner);

        var result = await new GetActiveStatusBannerQueryHandler(_banners, Mapper).Handle(new GetActiveStatusBannerQuery(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("critical", result!.Severity);
    }

    [Fact]
    public async Task NoActiveBanner_ReturnsNull_NotAnError()
    {
        _banners.GetActiveAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<StatusBanner>(new NotFoundException("status_banner", "active")));

        var result = await new GetActiveStatusBannerQueryHandler(_banners, Mapper).Handle(new GetActiveStatusBannerQuery(), CancellationToken.None);

        Assert.Null(result);
    }
}
