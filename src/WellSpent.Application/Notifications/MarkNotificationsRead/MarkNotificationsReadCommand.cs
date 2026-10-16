using MediatR;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Notifications.MarkNotificationsRead;

/// <summary>Empty Ids marks ALL of the user's unread notifications as read.</summary>
public sealed record MarkNotificationsReadCommand(Guid UserId, List<Guid> Ids) : IRequest<Unit>;

public sealed class MarkNotificationsReadCommandHandler(INotificationRepository notifications)
    : IRequestHandler<MarkNotificationsReadCommand, Unit>
{
    public async Task<Unit> Handle(MarkNotificationsReadCommand request, CancellationToken ct)
    {
        await notifications.MarkReadAsync(request.UserId, request.Ids, ct);
        return Unit.Value;
    }
}
