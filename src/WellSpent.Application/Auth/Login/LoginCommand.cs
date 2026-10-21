using MediatR;
using Microsoft.Extensions.Logging;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Auth.Login;

public sealed record LoginCommand(string Email, string Password, bool RememberMe, string DeviceId) : IRequest<LoginResult>;

public sealed record LoginResult(string AccessToken, long ExpiresIn, string Language, string Currency, string RefreshToken);

public sealed class LoginCommandHandler(
    IUserRepository users, IPasswordHasher passwordHasher, IJwtService jwt,
    IRefreshTokenRepository refreshTokens, ILogger<LoginCommandHandler> logger)
    : IRequestHandler<LoginCommand, LoginResult>
{
    public async Task<LoginResult> Handle(LoginCommand request, CancellationToken ct)
    {
        Domain.Entities.User user;
        try
        {
            user = await users.GetByEmailAsync(request.Email, ct);
        }
        catch (NotFoundException)
        {
            // Surfaced as a generic error to avoid email enumeration.
            throw new AppValidationException("invalid email or password");
        }

        if (!user.IsActive)
        {
            throw user.Status == "disabled"
                ? new ForbiddenException("account is deactivated — contact support to recover your account")
                : new ForbiddenException("account is inactive");
        }
        if (user.HashedPassword is null)
        {
            throw new AppValidationException("account uses OAuth login only");
        }
        if (!passwordHasher.Verify(request.Password, user.HashedPassword))
        {
            throw new AppValidationException("invalid email or password");
        }

        var refreshLifetime = request.RememberMe ? Auth.AuthConstants.RememberMeTokenLifetime : Auth.AuthConstants.DefaultTokenLifetime;
        var accessToken = jwt.GenerateToken(user.Id, Auth.AuthConstants.AccessTokenLifetime);
        var rawRefreshToken = RefreshTokenGenerator.GenerateRaw();
        await refreshTokens.IssueAsync(user.Id, request.DeviceId, RefreshTokenGenerator.Hash(rawRefreshToken),
            (int)refreshLifetime.TotalSeconds, DateTime.UtcNow.Add(refreshLifetime), ct);
        logger.LogInformation("auth.login.success user_id={UserId} device_id={DeviceId} remember_me={RememberMe}", user.Id, request.DeviceId, request.RememberMe);

        return new LoginResult(accessToken, (long)Auth.AuthConstants.AccessTokenLifetime.TotalSeconds, user.Language, user.Currency, rawRefreshToken);
    }
}
