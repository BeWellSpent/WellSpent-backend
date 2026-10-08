using MediatR;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Notifications.GetUnreadCount;

public sealed record GetUnreadCountQuery(Guid UserId) : IRequest<int>;

public sealed class GetUnreadCountQueryHandler(INotificationRepository notifications) : IRequestHandler<GetUnreadCountQuery, int>
{
    public Task<int> Handle(GetUnreadCountQuery request, CancellationToken ct) =>
        notifications.GetUnreadCountAsync(request.UserId, ct);
}
