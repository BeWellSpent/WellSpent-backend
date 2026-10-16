using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Changelog;
using WellSpent.Application.Changelog.ListChangelog;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Changelog;

public sealed class ListChangelogQueryHandlerTests
{
    private readonly IChangelogRepository _changelog = Substitute.For<IChangelogRepository>();
    private static readonly IMapper Mapper = new MapperConfiguration(
        cfg => cfg.AddProfile<ChangelogMappingProfile>(), NullLoggerFactory.Instance).CreateMapper();

    private ListChangelogQueryHandler CreateHandler() => new(_changelog, Mapper);

    [Fact]
    public async Task UnknownComponent_Throws()
    {
        await Assert.ThrowsAsync<AppValidationException>(
            () => CreateHandler().Handle(new ListChangelogQuery(["android"], 0), CancellationToken.None));
    }

    [Fact]
    public async Task ReturnsCurrentServerVersionAlways()
    {
        _changelog.ListReleasesWithItemsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns([]);

        var result = await CreateHandler().Handle(new ListChangelogQuery([], 0), CancellationToken.None);

        Assert.Equal(ChangelogConstants.ServerVersion, result.CurrentServerVersion);
    }

    [Fact]
    public async Task TruncatesPerComponent_NotGlobally()
    {
        var now = DateTime.UtcNow;
        var releases = new List<ChangelogRelease>
        {
            new() { Id = Guid.NewGuid(), Component = "web", Version = "3.0.0", ReleasedAt = now, CreatedAt = now },
            new() { Id = Guid.NewGuid(), Component = "web", Version = "2.0.0", ReleasedAt = now.AddDays(-1), CreatedAt = now.AddDays(-1) },
            new() { Id = Guid.NewGuid(), Component = "web", Version = "1.0.0", ReleasedAt = now.AddDays(-2), CreatedAt = now.AddDays(-2) },
            new() { Id = Guid.NewGuid(), Component = "server", Version = "9.0.0", ReleasedAt = now, CreatedAt = now },
        };
        _changelog.ListReleasesWithItemsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns(releases);

        var result = await CreateHandler().Handle(new ListChangelogQuery([], 2), CancellationToken.None);

        Assert.Equal(3, result.Releases.Count); // 2 web + 1 server, not 2 total
        Assert.Equal(2, result.Releases.Count(r => r.Component == "web"));
        Assert.Equal(1, result.Releases.Count(r => r.Component == "server"));
    }

    [Fact]
    public async Task ZeroLimit_UsesDefault()
    {
        _changelog.ListReleasesWithItemsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns([]);

        await CreateHandler().Handle(new ListChangelogQuery(["web"], 0), CancellationToken.None);

        await _changelog.Received(1).ListReleasesWithItemsAsync(
            Arg.Is<IReadOnlyList<string>>(c => c.Count == 1 && c[0] == "web"), Arg.Any<CancellationToken>());
    }
}
