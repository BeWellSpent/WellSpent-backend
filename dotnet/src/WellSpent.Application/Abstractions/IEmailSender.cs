namespace WellSpent.Application.Abstractions;

/// <summary>Mirrors the Resend send call in internal/service/verification_mailer.go.</summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string html, CancellationToken ct);
}
