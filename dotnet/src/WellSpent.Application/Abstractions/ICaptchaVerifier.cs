namespace WellSpent.Application.Abstractions;

/// <summary>Mirrors internal/captcha's Verifier — Cloudflare Turnstile.</summary>
public interface ICaptchaVerifier
{
    Task<bool> VerifyAsync(string token, string remoteIp, CancellationToken ct);
}
