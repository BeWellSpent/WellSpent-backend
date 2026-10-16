using MediatR;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Notifications.DeleteAlertSubscription;

public sealed record DeleteAlertSubscriptionCommand(Guid UserId, Guid SubscriptionId) : IRequest<Unit>;

public sealed class DeleteAlertSubscriptionCommandHandler(INotificationRepository notifications)
    : IRequestHandler<DeleteAlertSubscriptionCommand, Unit>
{
    public async Task<Unit> Handle(DeleteAlertSubscriptionCommand request, CancellationToken ct)
    {
        await notifications.DeleteSubscriptionAsync(request.SubscriptionId, request.UserId, ct);
        return Unit.Value;
    }
}
