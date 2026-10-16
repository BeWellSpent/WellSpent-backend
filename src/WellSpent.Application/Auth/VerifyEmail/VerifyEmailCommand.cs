using MediatR;
using WellSpent.Application.Abstractions;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Auth.VerifyEmail;

public sealed record VerifyEmailCommand(string Token) : IRequest<Unit>;

/// <summary>
/// Redeems a verification token minted by VerificationMailer. Errors are
/// deliberately generic (mirrors Login's invalid-credentials message) so a
/// caller can't distinguish "wrong token" from "expired token" from "never
/// existed".
/// </summary>
public sealed class VerifyEmailCommandHandler(IUserRepository users) : IRequestHandler<VerifyEmailCommand, Unit>
{
    public async Task<Unit> Handle(VerifyEmailCommand request, CancellationToken ct)
    {
        if (!Guid.TryParse(request.Token, out var token))
        {
            throw new AppValidationException("invalid or expired verification token");
        }

        Domain.Entities.User user;
        try
        {
            user = await users.GetByVerificationTokenAsync(token, ct);
        }
        catch (NotFoundException)
        {
            throw new AppValidationException("invalid or expired verification token");
        }

        if (user.EmailVerificationExpiresAt is null || user.EmailVerificationExpiresAt < DateTime.UtcNow)
        {
            throw new AppValidationException("invalid or expired verification token");
        }

        await users.MarkVerifiedAsync(user.Id, ct);
        return Unit.Value;
    }
}
