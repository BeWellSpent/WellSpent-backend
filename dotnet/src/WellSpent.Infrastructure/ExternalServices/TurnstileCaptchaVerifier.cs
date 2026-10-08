using System.Net.Http.Json;
using WellSpent.Application.Abstractions;
using WellSpent.Infrastructure.Configuration;

namespace WellSpent.Infrastructure.ExternalServices;

/// <summary>Mirrors internal/captcha's Cloudflare Turnstile client.</summary>
public sealed class TurnstileCaptchaVerifier(HttpClient http, AppConfig config) : ICaptchaVerifier
{
    private const string VerifyEndpoint = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    public async Task<bool> VerifyAsync(string token, string remoteIp, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token))
        {
            // No point spending a network call on a request with nothing to verify.
            return false;
        }
        if (string.IsNullOrEmpty(config.TurnstileSecretKey))
        {
            throw new InvalidOperationException("captcha: TURNSTILE_SECRET_KEY not configured");
        }

        var form = new Dictionary<string, string>
        {
            ["secret"] = config.TurnstileSecretKey,
            ["response"] = token,
        };
        if (!string.IsNullOrEmpty(remoteIp))
        {
            form["remoteip"] = remoteIp;
        }

        var response = await http.PostAsync(VerifyEndpoint, new FormUrlEncodedContent(form), ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<TurnstileResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("captcha: empty verify response");
        return result.Success;
    }

    private sealed record TurnstileResponse(bool Success);
}
