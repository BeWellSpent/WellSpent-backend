using MediatR;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Auth.ResendVerificationEmail;

public sealed record ResendVerificationEmailCommand(string Email) : IRequest<Unit>;

/// <summary>
/// Re-mints and re-sends a verification token. Always succeeds for unknown or
/// already-verified emails (no error) to avoid leaking account existence —
/// only a too-soon repeat request for a real, unverified account is rejected.
/// </summary>
public sealed class ResendVerificationEmailCommandHandler(IUserRepository users, VerificationMailer mailer)
    : IRequestHandler<ResendVerificationEmailCommand, Unit>
{
    public async Task<Unit> Handle(ResendVerificationEmailCommand request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        Domain.Entities.User user;
        try
        {
            user = await users.GetByEmailAsync(email, ct);
        }
        catch (NotFoundException)
        {
            return Unit.Value;
        }

        if (user.IsVerified)
        {
            return Unit.Value;
        }

        if (user.EmailVerificationLastSentAt is not null &&
            DateTime.UtcNow - user.EmailVerificationLastSentAt < Auth.AuthConstants.VerificationResendCooldown)
        {
            throw new AppValidationException("please wait before requesting another verification email");
        }

        await mailer.SendAsync(user, ct);
        return Unit.Value;
    }
}
