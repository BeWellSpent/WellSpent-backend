using Microsoft.Extensions.Options;
using Resend;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Configuration;

namespace WellSpent.Infrastructure.ExternalServices;

/// <summary>Only registered when RESEND_API_KEY is configured — see NoOpEmailSender for the alternative, matching Go's log-and-skip behavior when it's unset.</summary>
public sealed class ResendEmailSender(IResend resend, IOptions<AuthOptions> options) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string html, CancellationToken ct)
    {
        var message = new EmailMessage
        {
            From = options.Value.ResendFromEmail,
            Subject = subject,
            HtmlBody = html,
        };
        message.To.Add(to);
        await resend.EmailSendAsync(message, ct);
    }
}
