using MediatR;
using Microsoft.Extensions.Logging;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Auth.RefreshToken;

public sealed record RefreshTokenCommand(string RefreshToken, string DeviceId) : IRequest<RefreshTokenResult>;

public sealed record RefreshTokenResult(string AccessToken, long ExpiresIn, string RefreshToken);

/// <summary>
/// Rotates on every call: issues a new access + refresh token and revokes the
/// one just presented. Reuse of an already-revoked token is treated as theft
/// — every other active refresh token for that user is revoked too.
/// </summary>
public sealed class RefreshTokenCommandHandler(
    IRefreshTokenRepository refreshTokens, IJwtService jwt, ILogger<RefreshTokenCommandHandler> logger)
    : IRequestHandler<RefreshTokenCommand, RefreshTokenResult>
{
    public async Task<RefreshTokenResult> Handle(RefreshTokenCommand request, CancellationToken ct)
    {
        var hash = RefreshTokenGenerator.Hash(request.RefreshToken);
        var row = await refreshTokens.GetByHashAsync(hash, ct);

        // Device mismatch fails exactly like "not found" — doesn't confirm the token exists.
        if (row is null || row.DeviceId != request.DeviceId)
        {
            logger.LogWarning("auth.refresh.unknown_token device_id={DeviceId}", request.DeviceId);
            throw new UnauthorizedException("invalid refresh token");
        }

        if (row.RevokedAt is not null)
        {
            // The real payoff of rotation: a replayed, already-rotated token means theft.
            logger.LogWarning("auth.refresh.reuse_detected user_id={UserId} device_id={DeviceId}", row.UserId, row.DeviceId);
            await refreshTokens.RevokeAllForUserAsync(row.UserId, ct);
            throw new UnauthorizedException("invalid refresh token");
        }

        if (row.ExpiresAt <= DateTime.UtcNow)
        {
            logger.LogInformation("auth.refresh.expired user_id={UserId} device_id={DeviceId}", row.UserId, row.DeviceId);
            throw new UnauthorizedException("invalid refresh token");
        }

        await refreshTokens.RevokeAsync(row.Id, ct);
        var rawRefreshToken = RefreshTokenGenerator.GenerateRaw();
        var expiresAt = DateTime.UtcNow.AddSeconds(row.LifetimeSeconds);
        await refreshTokens.IssueAsync(row.UserId, row.DeviceId, RefreshTokenGenerator.Hash(rawRefreshToken), row.LifetimeSeconds, expiresAt, ct);

        var accessToken = jwt.GenerateToken(row.UserId, AuthConstants.AccessTokenLifetime);
        logger.LogInformation("auth.refresh.rotated user_id={UserId} device_id={DeviceId}", row.UserId, row.DeviceId);

        return new RefreshTokenResult(accessToken, (long)AuthConstants.AccessTokenLifetime.TotalSeconds, rawRefreshToken);
    }
}
