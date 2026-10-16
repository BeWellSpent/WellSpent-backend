using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Changelog;
using WellSpent.Application.Changelog.CreateChangelogRelease;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Changelog;

public sealed class CreateChangelogReleaseCommandHandlerTests
{
    private readonly IChangelogRepository _changelog = Substitute.For<IChangelogRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private static readonly IMapper Mapper = new MapperConfiguration(
        cfg => cfg.AddProfile<ChangelogMappingProfile>(), NullLoggerFactory.Instance).CreateMapper();

    private CreateChangelogReleaseCommandHandler CreateHandler() => new(_changelog, new SuperuserGuard(_users), Mapper);

    private Guid SetUpSuperuser()
    {
        var id = Guid.NewGuid();
        _users.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(new User { Id = id, Email = "admin@example.com", IsSuperuser = true });
        return id;
    }

    private static List<CreateChangelogItemInput> OneValidItem() =>
        [new CreateChangelogItemInput("added", "Added a thing", "")];

    [Fact]
    public async Task NotSuperuser_Throws()
    {
        var id = Guid.NewGuid();
        _users.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(new User { Id = id, Email = "x@example.com", IsSuperuser = false });

        await Assert.ThrowsAsync<ForbiddenException>(() => CreateHandler().Handle(
            new CreateChangelogReleaseCommand(id, "web", "1.0.0", null, OneValidItem()), CancellationToken.None));
    }

    [Fact]
    public async Task InvalidComponent_Throws()
    {
        var id = SetUpSuperuser();

        await Assert.ThrowsAsync<AppValidationException>(() => CreateHandler().Handle(
            new CreateChangelogReleaseCommand(id, "android", "1.0.0", null, OneValidItem()), CancellationToken.None));
    }

    [Fact]
    public async Task EmptyVersion_Throws()
    {
        var id = SetUpSuperuser();

        await Assert.ThrowsAsync<AppValidationException>(() => CreateHandler().Handle(
            new CreateChangelogReleaseCommand(id, "web", "  ", null, OneValidItem()), CancellationToken.None));
    }

    [Fact]
    public async Task NoItems_Throws()
    {
        var id = SetUpSuperuser();

        await Assert.ThrowsAsync<AppValidationException>(() => CreateHandler().Handle(
            new CreateChangelogReleaseCommand(id, "web", "1.0.0", null, []), CancellationToken.None));
    }

    [Fact]
    public async Task InvalidChangeType_Throws()
    {
        var id = SetUpSuperuser();
        var items = new List<CreateChangelogItemInput> { new("removed", "x", "") };

        await Assert.ThrowsAsync<AppValidationException>(() => CreateHandler().Handle(
            new CreateChangelogReleaseCommand(id, "web", "1.0.0", null, items), CancellationToken.None));
    }

    [Fact]
    public async Task EmptyEnglishSummary_Throws()
    {
        var id = SetUpSuperuser();
        var items = new List<CreateChangelogItemInput> { new("added", "  ", "") };

        await Assert.ThrowsAsync<AppValidationException>(() => CreateHandler().Handle(
            new CreateChangelogReleaseCommand(id, "web", "1.0.0", null, items), CancellationToken.None));
    }

    [Fact]
    public async Task SummaryTooLong_Throws()
    {
        var id = SetUpSuperuser();
        var items = new List<CreateChangelogItemInput> { new("added", new string('a', 301), "") };

        await Assert.ThrowsAsync<AppValidationException>(() => CreateHandler().Handle(
            new CreateChangelogReleaseCommand(id, "web", "1.0.0", null, items), CancellationToken.None));
    }

    [Fact]
    public async Task Success_CreatesReleaseWithPositionedItems()
    {
        var id = SetUpSuperuser();
        var items = new List<CreateChangelogItemInput>
        {
            new("added", "First", ""),
            new("fixed", "Second", ""),
        };
        _changelog.CreateReleaseAsync(Arg.Any<ChangelogRelease>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<ChangelogRelease>());

        var result = await CreateHandler().Handle(new CreateChangelogReleaseCommand(id, "web", "1.0.0", null, items), CancellationToken.None);

        Assert.Equal("web", result.Component);
        Assert.Equal(2, result.Items.Count);
        await _changelog.Received(1).CreateReleaseAsync(
            Arg.Is<ChangelogRelease>(r => r.Items[0].Position == 0 && r.Items[1].Position == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DuplicateVersion_PropagatesFromRepository()
    {
        var id = SetUpSuperuser();
        _changelog.CreateReleaseAsync(Arg.Any<ChangelogRelease>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ChangelogRelease>(new DuplicateException("changelog_release", "version", "web 1.0.0")));

        await Assert.ThrowsAsync<DuplicateException>(() => CreateHandler().Handle(
            new CreateChangelogReleaseCommand(id, "web", "1.0.0", null, OneValidItem()), CancellationToken.None));
    }
}
