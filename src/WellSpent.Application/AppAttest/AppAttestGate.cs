using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.AppAttest;

/// <summary>An App Attest assertion sent with a protected request.</summary>
public sealed record AppAttestProof(string KeyId, string Assertion, string Challenge);

public enum AppAttestOutcome
{
    /// <summary>A valid assertion from an attested key.</summary>
    Attested,
    /// <summary>The app reported it can't attest (unsupported device, Apple's service failed).</summary>
    Unavailable,
    /// <summary>Neither proof nor an unavailable reason: not the iOS app.</summary>
    Missing,
    /// <summary>Proof that doesn't verify.</summary>
    Invalid,
}

/// <summary>Checks the App Attest proof on a protected request (#121). Never throws on a bad proof; the caller decides.</summary>
public sealed class AppAttestGate(
    IAppAttestRepository repo, IOptions<AppAttestOptions> options, ILogger<AppAttestGate> logger)
{
    public static readonly string[] UnavailableReasons = ["unsupported", "service_error"];

    public bool EnforcementEnabled => options.Value.EnforcementEnabled;

    /// <summary>The bytes the assertion signs: "action|email|challenge", email trimmed and lowercased.</summary>
    public static byte[] ClientDataHash(string action, string email, string challenge) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"{action}|{email.Trim().ToLowerInvariant()}|{challenge}"));

    public async Task<AppAttestOutcome> CheckAsync(string action, string email, AppAttestProof? proof, string? unavailable, CancellationToken ct)
    {
        if (proof is null)
        {
            if (unavailable is not null && UnavailableReasons.Contains(unavailable))
            {
                logger.LogWarning("app_attest.unavailable action={Action} reason={Reason}", action, unavailable);
                return AppAttestOutcome.Unavailable;
            }
            logger.LogInformation("app_attest.missing action={Action} unavailable={Unavailable}", action, unavailable);
            return AppAttestOutcome.Missing;
        }

        var reason = await VerifyAsync(action, email, proof, ct);
        if (reason is null)
        {
            logger.LogInformation("app_attest.attested action={Action} key_id={KeyId}", action, proof.KeyId);
            return AppAttestOutcome.Attested;
        }
        logger.LogWarning("app_attest.invalid action={Action} key_id={KeyId} reason={Reason} enforced={Enforced}",
            action, proof.KeyId, reason, EnforcementEnabled);
        return AppAttestOutcome.Invalid;
    }

    // The failed check, or null when the proof verifies.
    private async Task<string?> VerifyAsync(string action, string email, AppAttestProof proof, CancellationToken ct)
    {
        if (!await repo.ConsumeChallengeAsync(proof.Challenge, DateTime.UtcNow, ct))
        {
            return "challenge unknown, used or expired";
        }
        var key = await repo.GetKeyAsync(proof.KeyId, ct);
        if (key is null)
        {
            return "key not attested";
        }
        if (key.Environment == AppAttestVerifier.Development && !options.Value.AllowDevelopment)
        {
            return "development key";
        }

        uint counter;
        try
        {
            counter = AppAttestVerifier.VerifyAssertion(
                Convert.FromBase64String(proof.Assertion), ClientDataHash(action, email, proof.Challenge), key.PublicKey, options.Value.AppId);
        }
        catch (Exception ex) when (ex is AppAttestVerificationException or FormatException or CryptographicException)
        {
            return ex.Message;
        }

        return await repo.AdvanceCounterAsync(key.KeyId, counter, DateTime.UtcNow, ct) ? null : "counter not increasing";
    }
}
