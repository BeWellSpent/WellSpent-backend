using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Users;
using WellSpent.Application.Users.UpdateMe;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using Xunit;

namespace WellSpent.Application.Tests.Users;

public sealed class UpdateMeCommandHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private static readonly IMapper Mapper = new MapperConfiguration(
        cfg => cfg.AddProfile<UserMappingProfile>(), NullLoggerFactory.Instance).CreateMapper();

    [Fact]
    public async Task EmptyOptionalFields_ClearedToNull_NotLeftUnchanged()
    {
        var userId = Guid.NewGuid();
        var updated = new User { Id = userId, Email = "a@example.com", Language = "en", Currency = "USD" };
        _users.UpdateAsync(userId, null, null, null, null, "0", 0, "en", "USD", Arg.Any<CancellationToken>())
            .Returns(updated);

        var command = new UpdateMeCommand(userId, "", "", "", "", 0, 0, "en", "USD");
        await new UpdateMeCommandHandler(_users, Mapper).Handle(command, CancellationToken.None);

        await _users.Received(1).UpdateAsync(userId, null, null, null, null, "0", 0, "en", "USD", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NonEmptyFields_PassedThroughAsGiven()
    {
        var userId = Guid.NewGuid();
        var updated = new User { Id = userId, Email = "a@example.com", Language = "es", Currency = "ARS" };
        _users.UpdateAsync(userId, "First", "Last", "US", "CA", "1", 3, "es", "ARS", Arg.Any<CancellationToken>())
            .Returns(updated);

        var command = new UpdateMeCommand(userId, "First", "Last", "US", "CA", 1, 3, "es", "ARS");
        var dto = await new UpdateMeCommandHandler(_users, Mapper).Handle(command, CancellationToken.None);

        Assert.Equal("es", dto.Language);
        await _users.Received(1).UpdateAsync(userId, "First", "Last", "US", "CA", "1", 3, "es", "ARS", Arg.Any<CancellationToken>());
    }
}
