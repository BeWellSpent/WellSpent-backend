using MediatR;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Notifications.RegisterDeviceToken;

public sealed record RegisterDeviceTokenCommand(Guid UserId, string Token, string Platform) : IRequest<Unit>;

public sealed class RegisterDeviceTokenCommandHandler(INotificationRepository notifications)
    : IRequestHandler<RegisterDeviceTokenCommand, Unit>
{
    public async Task<Unit> Handle(RegisterDeviceTokenCommand request, CancellationToken ct)
    {
        if (request.Platform != "ios")
        {
            throw new AppValidationException("platform must be \"ios\"");
        }
        if (string.IsNullOrEmpty(request.Token))
        {
            throw new AppValidationException("token is required");
        }

        await notifications.UpsertDeviceTokenAsync(request.UserId, request.Platform, request.Token, ct);
        return Unit.Value;
    }
}
