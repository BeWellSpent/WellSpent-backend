using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Users.ChangeEmail;

/// <summary>
/// Replaces the account's address and re-opens verification for the new one.
/// Deliberately does not ask for the current password, unlike
/// ChangePasswordCommand: the JWT already proves the session, and the new
/// address grants nothing until its own verification link is redeemed.
/// </summary>
public sealed record ChangeEmailCommand(Guid UserId, string NewEmail) : IRequest<UserDto>;

public sealed class ChangeEmailCommandHandler(
    IUserRepository users,
    VerificationMailer mailer,
    IMapper mapper,
    ILogger<ChangeEmailCommandHandler> logger) : IRequestHandler<ChangeEmailCommand, UserDto>
{
    public async Task<UserDto> Handle(ChangeEmailCommand request, CancellationToken ct)
    {
        var email = EmailValidator.NormalizeAndValidate(request.NewEmail);

        var current = await users.GetByIdAsync(request.UserId, ct);
        if (current.Email == email)
        {
            // Silently succeeding here would be worse than it looks: the
            // caller is a user staring at a verification wall, and "saved"
            // with nothing arriving reads as the feature being broken.
            throw new AppValidationException("that is already your email address");
        }

        try
        {
            var existing = await users.GetByEmailAsync(email, ct);
            if (existing.Id != request.UserId)
            {
                throw new DuplicateException("user", "email", email);
            }
        }
        catch (NotFoundException)
        {
            // expected — address is free
        }

        var updated = await users.UpdateEmailAsync(request.UserId, email, ct);

        // Best-effort, matching Register: the address is already changed,
        // and the user can fall back to "resend verification email", which
        // now goes to the corrected address.
        try
        {
            await mailer.SendAsync(updated, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "user.change_email.verification_email_failed to={To}", updated.Email);
        }

        return mapper.Map<UserDto>(updated);
    }
}
