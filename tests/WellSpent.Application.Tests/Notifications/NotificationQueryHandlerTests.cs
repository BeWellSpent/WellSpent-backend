using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WellSpent.Application.Notifications;
using WellSpent.Application.Notifications.GetUnreadCount;
using WellSpent.Application.Notifications.ListAlertSubscriptions;
using WellSpent.Application.Notifications.ListNotifications;
using WellSpent.Application.Notifications.MarkNotificationsRead;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using Xunit;

namespace WellSpent.Application.Tests.Notifications;

public sealed class ListNotificationsQueryHandlerTests
{
    private readonly INotificationRepository _notifications = Substitute.For<INotificationRepository>();
    private static readonly IMapper Mapper = new MapperConfiguration(
        cfg => cfg.AddProfile<NotificationMappingProfile>(), NullLoggerFactory.Instance).CreateMapper();

    [Fact]
    public async Task ZeroLimit_UsesDefault()
    {
        var userId = Guid.NewGuid();
        _notifications.ListAsync(userId, null, NotificationConstants.DefaultListLimit, Arg.Any<CancellationToken>()).Returns([]);
        _notifications.GetUnreadCountAsync(userId, Arg.Any<CancellationToken>()).Returns(0);

        await new ListNotificationsQueryHandler(_notifications, Mapper).Handle(new ListNotificationsQuery(userId, null, 0), CancellationToken.None);

        await _notifications.Received(1).ListAsync(userId, null, NotificationConstants.DefaultListLimit, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsNotificationsAndUnreadCount()
    {
        var userId = Guid.NewGuid();
        var notif = new Notification { Id = Guid.NewGuid(), UserId = userId, AlertType = "period_created", Title = "Hi" };
        _notifications.ListAsync(userId, null, 10, Arg.Any<CancellationToken>()).Returns([notif]);
        _notifications.GetUnreadCountAsync(userId, Arg.Any<CancellationToken>()).Returns(3);

        var result = await new ListNotificationsQueryHandler(_notifications, Mapper).Handle(new ListNotificationsQuery(userId, null, 10), CancellationToken.None);

        Assert.Single(result.Notifications);
        Assert.Equal(3, result.UnreadCount);
    }
}

public sealed class MarkNotificationsReadCommandHandlerTests
{
    [Fact]
    public async Task PassesIdsThrough()
    {
        var notifications = Substitute.For<INotificationRepository>();
        var userId = Guid.NewGuid();
        var ids = new List<Guid> { Guid.NewGuid() };

        await new MarkNotificationsReadCommandHandler(notifications).Handle(new MarkNotificationsReadCommand(userId, ids), CancellationToken.None);

        await notifications.Received(1).MarkReadAsync(userId, ids, Arg.Any<CancellationToken>());
    }
}

public sealed class GetUnreadCountQueryHandlerTests
{
    [Fact]
    public async Task ReturnsRepositoryValue()
    {
        var notifications = Substitute.For<INotificationRepository>();
        var userId = Guid.NewGuid();
        notifications.GetUnreadCountAsync(userId, Arg.Any<CancellationToken>()).Returns(7);

        var result = await new GetUnreadCountQueryHandler(notifications).Handle(new GetUnreadCountQuery(userId), CancellationToken.None);

        Assert.Equal(7, result);
    }
}

public sealed class ListAlertSubscriptionsQueryHandlerTests
{
    private static readonly IMapper Mapper = new MapperConfiguration(
        cfg => cfg.AddProfile<NotificationMappingProfile>(), NullLoggerFactory.Instance).CreateMapper();

    [Fact]
    public async Task NoMembershipCheck_ScopedByQueryAlone()
    {
        var notifications = Substitute.For<INotificationRepository>();
        var userId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        notifications.ListSubscriptionsAsync(userId, profileId, Arg.Any<CancellationToken>()).Returns([]);

        var result = await new ListAlertSubscriptionsQueryHandler(notifications, Mapper)
            .Handle(new ListAlertSubscriptionsQuery(userId, profileId), CancellationToken.None);

        Assert.Empty(result);
    }
}
