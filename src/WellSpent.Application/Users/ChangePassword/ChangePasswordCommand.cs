using MediatR;
using Microsoft.Extensions.Logging;
using WellSpent.Application.Abstractions;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Users.ChangePassword;

public sealed record ChangePasswordCommand(Guid UserId, string CurrentPassword, string NewPassword) : IRequest<Unit>;

public sealed class ChangePasswordCommandHandler(
    IUserRepository users, IPasswordHasher passwordHasher,
    IRefreshTokenRepository refreshTokens, ILogger<ChangePasswordCommandHandler> logger)
    : IRequestHandler<ChangePasswordCommand, Unit>
{
    public async Task<Unit> Handle(ChangePasswordCommand request, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(request.UserId, ct);
        if (user.HashedPassword is null)
        {
            throw new AppValidationException("account uses OAuth login only");
        }
        if (!passwordHasher.Verify(request.CurrentPassword, user.HashedPassword))
        {
            throw new AppValidationException("current password is incorrect");
        }

        var hashed = passwordHasher.Hash(request.NewPassword);
        await users.UpdatePasswordAsync(request.UserId, hashed, ct);

        // A stolen refresh token must not survive the one remediation step a compromised user is likely to take.
        await refreshTokens.RevokeAllForUserAsync(request.UserId, ct);
        logger.LogInformation("user.change_password.refresh_tokens_revoked user_id={UserId}", request.UserId);

        return Unit.Value;
    }
}
