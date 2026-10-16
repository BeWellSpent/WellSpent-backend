using AutoMapper;
using MediatR;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Notifications.ListNotifications;

public sealed record ListNotificationsQuery(Guid UserId, Guid? BudgetProfileId, int Limit) : IRequest<ListNotificationsResult>;

public sealed record ListNotificationsResult(List<NotificationDto> Notifications, int UnreadCount);

public sealed class ListNotificationsQueryHandler(INotificationRepository notifications, IMapper mapper)
    : IRequestHandler<ListNotificationsQuery, ListNotificationsResult>
{
    public async Task<ListNotificationsResult> Handle(ListNotificationsQuery request, CancellationToken ct)
    {
        var limit = request.Limit > 0 ? request.Limit : NotificationConstants.DefaultListLimit;
        var notifs = await notifications.ListAsync(request.UserId, request.BudgetProfileId, limit, ct);
        var unreadCount = await notifications.GetUnreadCountAsync(request.UserId, ct);
        return new ListNotificationsResult(mapper.Map<List<NotificationDto>>(notifs), unreadCount);
    }
}
