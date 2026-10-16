using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Configuration;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.Common;

/// <summary>
/// Mints an email-verification token and sends the link. Mirrors
/// internal/service/verification_mailer.go — shared by Register,
/// ResendVerificationEmail, and ChangeEmail. Whether a send failure is
/// swallowed or propagated is the caller's decision (each mirrors its Go
/// counterpart), not this class's — Register and ChangeEmail swallow, since
/// the account/address change already succeeded; ResendVerificationEmail
/// propagates, since that is the entire point of the call.
/// </summary>
public sealed class VerificationMailer(
    IUserRepository users,
    IEmailSender emailSender,
    IOptions<AuthOptions> options,
    ILogger<VerificationMailer> logger)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    public async Task SendAsync(User user, CancellationToken ct)
    {
        var token = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await users.SetEmailVerificationTokenAsync(user.Id, token, now.Add(Ttl), now, ct);

        var link = $"{options.Value.FrontendUrl.TrimEnd('/')}/en/verify-email/{token}";
        var html = $"""
            <p>Welcome to WellSpent! Please confirm your email address to finish setting up your account.</p>
            <p><a href="{link}" style="display:inline-block;padding:10px 20px;background:#1976d2;color:#fff;text-decoration:none;border-radius:4px;">Verify email</a></p>
            <p>If the button above doesn't work, copy and paste this link into your browser:</p>
            <p>{link}</p>
            <p>This link expires in 10 minutes.</p>
            """;

        await emailSender.SendAsync(user.Email, "Verify your WellSpent email address", html, ct);
        logger.LogInformation("auth.verification_email.sent to={To}", user.Email);
    }
}
