using MediatR;
using Microsoft.Extensions.Logging;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Auth.Logout;

public sealed record LogoutCommand(string RefreshToken) : IRequest<Unit>;

/// <summary>Always succeeds — revokes the matching row if one is found, no-ops otherwise.</summary>
public sealed class LogoutCommandHandler(IRefreshTokenRepository refreshTokens, ILogger<LogoutCommandHandler> logger)
    : IRequestHandler<LogoutCommand, Unit>
{
    public async Task<Unit> Handle(LogoutCommand request, CancellationToken ct)
    {
        var hash = RefreshTokenGenerator.Hash(request.RefreshToken);
        var row = await refreshTokens.GetByHashAsync(hash, ct);
        if (row is null)
        {
            logger.LogInformation("auth.logout.unknown_token");
            return Unit.Value;
        }

        await refreshTokens.RevokeAsync(row.Id, ct);
        logger.LogInformation("auth.logout.revoked user_id={UserId} device_id={DeviceId}", row.UserId, row.DeviceId);
        return Unit.Value;
    }
}
