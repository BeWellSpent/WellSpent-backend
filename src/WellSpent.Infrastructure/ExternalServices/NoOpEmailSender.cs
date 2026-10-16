using Microsoft.Extensions.Logging;
using WellSpent.Application.Abstractions;

namespace WellSpent.Infrastructure.ExternalServices;

/// <summary>Registered in place of ResendEmailSender when RESEND_API_KEY is unset — mirrors Go's "skipped: RESEND_API_KEY not set" warning-and-no-op rather than failing the caller.</summary>
public sealed class NoOpEmailSender(ILogger<NoOpEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string html, CancellationToken ct)
    {
        logger.LogWarning("email.skipped: RESEND_API_KEY not set to={To}", to);
        return Task.CompletedTask;
    }
}
