using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using WellSpent.Application.Abstractions;

namespace WellSpent.Infrastructure.ExternalServices;

/// <summary>
/// Verifies Plaid's webhook JWT per Plaid's documented scheme
/// (https://plaid.com/docs/api/webhooks/webhook-verification/): the
/// "Plaid-Verification" header is an ES256 JWT whose "kid" names the public
/// key to fetch from Plaid, whose "iat" must be recent (anti-replay), and
/// whose "request_body_sha256" claim must match the raw body's hash.
/// Verified keys are cached per-kid for this process's lifetime — a cold
/// Cloud Run instance re-fetches once per key, an accepted cost.
/// </summary>
public sealed class PlaidWebhookVerifier(IPlaidClient plaid, ILogger<PlaidWebhookVerifier> logger) : IPlaidWebhookVerifier
{
    // Plaid mints a fresh "iat" per call; anything outside a few minutes can only be a replay or clock skew worth rejecting.
    private static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, ECDsaSecurityKey> _keyCache = new();

    public async Task<bool> VerifyAsync(string verificationHeader, byte[] rawBody, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(verificationHeader))
        {
            logger.LogWarning("plaid.webhook.missing_verification_header");
            return false;
        }

        var handler = new JwtSecurityTokenHandler();
        JwtSecurityToken unverified;
        try
        {
            unverified = handler.ReadJwtToken(verificationHeader);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "plaid.webhook.malformed_jwt");
            return false;
        }

        if (unverified.Header.Alg != SecurityAlgorithms.EcdsaSha256)
        {
            logger.LogWarning("plaid.webhook.unexpected_alg alg={Alg}", unverified.Header.Alg);
            return false;
        }

        var kid = unverified.Header.Kid;
        if (string.IsNullOrEmpty(kid))
        {
            logger.LogWarning("plaid.webhook.missing_kid");
            return false;
        }

        var key = await GetKeyAsync(kid, ct);
        if (key is null)
        {
            return false;
        }

        JwtSecurityToken validated;
        try
        {
            handler.ValidateToken(verificationHeader, new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = false,
                RequireExpirationTime = false,
                IssuerSigningKey = key,
                ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            }, out var rawValidated);
            validated = (JwtSecurityToken)rawValidated;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "plaid.webhook.signature_invalid kid={Kid}", kid);
            return false;
        }

        if (!IsIatRecent(validated, kid))
        {
            return false;
        }

        return HasMatchingBodyHash(validated, rawBody, kid);
    }

    private async Task<ECDsaSecurityKey?> GetKeyAsync(string kid, CancellationToken ct)
    {
        if (_keyCache.TryGetValue(kid, out var cached))
        {
            return cached;
        }

        try
        {
            var fetched = await plaid.GetWebhookVerificationKeyAsync(kid, ct);
            if (fetched.ExpiredAt is not null)
            {
                logger.LogWarning("plaid.webhook.expired_key kid={Kid}", kid);
                return null;
            }

            var key = new ECDsaSecurityKey(ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = Base64UrlEncoder.DecodeBytes(fetched.X), Y = Base64UrlEncoder.DecodeBytes(fetched.Y) },
            }))
            { KeyId = fetched.Kid };

            _keyCache[kid] = key;
            return key;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "plaid.webhook.key_fetch_failed kid={Kid}", kid);
            return null;
        }
    }

    private bool IsIatRecent(JwtSecurityToken validated, string kid)
    {
        var issuedAt = validated.Payload.IssuedAt;
        if (issuedAt == DateTime.MinValue)
        {
            logger.LogWarning("plaid.webhook.missing_iat kid={Kid}", kid);
            return false;
        }

        var age = DateTimeOffset.UtcNow - new DateTimeOffset(issuedAt, TimeSpan.Zero);
        if (age > MaxClockSkew || age < -MaxClockSkew)
        {
            logger.LogWarning("plaid.webhook.stale_iat kid={Kid} ageSeconds={AgeSeconds}", kid, age.TotalSeconds);
            return false;
        }

        return true;
    }

    private bool HasMatchingBodyHash(JwtSecurityToken validated, byte[] rawBody, string kid)
    {
        var expectedHash = validated.Payload.TryGetValue("request_body_sha256", out var claim) ? claim as string : null;
        if (string.IsNullOrEmpty(expectedHash))
        {
            logger.LogWarning("plaid.webhook.missing_body_hash_claim kid={Kid}", kid);
            return false;
        }

        var actualHash = Convert.ToHexStringLower(SHA256.HashData(rawBody));
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actualHash), Encoding.UTF8.GetBytes(expectedHash.ToLowerInvariant())))
        {
            logger.LogWarning("plaid.webhook.body_hash_mismatch kid={Kid}", kid);
            return false;
        }

        return true;
    }
}
