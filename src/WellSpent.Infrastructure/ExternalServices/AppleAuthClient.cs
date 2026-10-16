using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using WellSpent.Application.Abstractions;
using WellSpent.Infrastructure.Configuration;

namespace WellSpent.Infrastructure.ExternalServices;

/// <summary>Mirrors internal/auth/apple.go's AppleAuth exactly: JWKS-based identity-token verification, and an ES256 client-secret minted per call (not cached — an ECDSA sign is microseconds, and the two operations that need one happen at most once per account) for the code-exchange and revoke endpoints.</summary>
public sealed class AppleAuthClient(HttpClient http, AppConfig config) : IAppleAuthClient
{
    private const string Issuer = "https://appleid.apple.com";
    private const string JwksUrl = "https://appleid.apple.com/auth/keys";
    private const string TokenUrl = "https://appleid.apple.com/auth/token";
    private const string RevokeUrl = "https://appleid.apple.com/auth/revoke";
    private static readonly TimeSpan ClientSecretLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MinJwksRefetchInterval = TimeSpan.FromMinutes(1);

    private readonly object _lock = new();
    private Dictionary<string, RsaSecurityKey> _keys = new();
    private DateTime _fetchedAt;

    public async Task<AppleIdentity> VerifyIdentityTokenAsync(string identityToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(identityToken))
        {
            throw new ArgumentException("apple: empty identity token", nameof(identityToken));
        }

        var handler = new JsonWebTokenHandler();
        var unvalidated = handler.ReadJsonWebToken(identityToken);
        var kid = unvalidated.Kid;
        if (string.IsNullOrEmpty(kid))
        {
            throw new SecurityTokenException("apple: token has no kid header");
        }

        var key = await ResolveKeyAsync(kid, ct);

        var result = await handler.ValidateTokenAsync(identityToken, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = config.AppleClientId,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            IssuerSigningKey = key,
        });
        if (!result.IsValid)
        {
            throw new SecurityTokenException("apple: verify identity token", result.Exception);
        }

        var claims = result.ClaimsIdentity;
        var sub = claims.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(sub))
        {
            throw new SecurityTokenException("apple: token has no subject");
        }

        return new AppleIdentity(
            Sub: sub,
            Email: (claims.FindFirst("email")?.Value ?? "").Trim().ToLowerInvariant(),
            EmailVerified: ParseFlexBool(claims.FindFirst("email_verified")?.Value),
            IsPrivateEmail: ParseFlexBool(claims.FindFirst("is_private_email")?.Value));
    }

    // Apple has historically serialised these claims both as real JSON
    // booleans and as the strings "true"/"false" depending on the endpoint
    // and API vintage. Both encodings read back as the string "true"/"false"
    // once parsed into claims, so a case-insensitive string compare handles
    // both without a custom JSON converter.
    private static bool ParseFlexBool(string? value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    public async Task<string> ExchangeCodeAsync(string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("apple: empty authorization code", nameof(code));
        }
        var secret = BuildClientSecret();

        var body = await PostFormAsync(TokenUrl, new Dictionary<string, string>
        {
            ["client_id"] = config.AppleClientId,
            ["client_secret"] = secret,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
        }, ct);

        var payload = System.Text.Json.JsonSerializer.Deserialize<AppleTokenResponse>(body)
            ?? throw new InvalidOperationException("apple: decode token response");
        if (string.IsNullOrEmpty(payload.RefreshToken))
        {
            throw new InvalidOperationException("apple: token response contained no refresh token");
        }
        return payload.RefreshToken;
    }

    public async Task RevokeRefreshTokenAsync(string refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new ArgumentException("apple: empty refresh token", nameof(refreshToken));
        }
        var secret = BuildClientSecret();

        await PostFormAsync(RevokeUrl, new Dictionary<string, string>
        {
            ["client_id"] = config.AppleClientId,
            ["client_secret"] = secret,
            ["token"] = refreshToken,
            ["token_type_hint"] = "refresh_token",
        }, ct);
    }

    // Mints the short-lived ES256 JWT Apple accepts in place of a static
    // client secret on its token endpoints. MapClaims-equivalent (raw
    // dictionary), not RegisteredClaims: Apple documents (and every
    // published example uses) `aud` as a bare string, not a one-element array.
    private string BuildClientSecret()
    {
        if (string.IsNullOrEmpty(config.AppleKeyId) || string.IsNullOrWhiteSpace(config.ApplePrivateKey))
        {
            throw new AppleKeyNotConfiguredException();
        }

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(config.ApplePrivateKey);

        var handler = new JsonWebTokenHandler();
        var now = DateTime.UtcNow;
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Claims = new Dictionary<string, object>
            {
                ["iss"] = config.AppleTeamId,
                ["sub"] = config.AppleClientId,
                ["aud"] = Issuer,
            },
            IssuedAt = now,
            Expires = now.Add(ClientSecretLifetime),
            SigningCredentials = new SigningCredentials(new ECDsaSecurityKey(ecdsa) { KeyId = config.AppleKeyId }, SecurityAlgorithms.EcdsaSha256),
        });
    }

    // Returns the cached RSA key for kid, refetching the key set once if the
    // kid is unknown (Apple rotates keys without notice). minJwksRefetchInterval
    // floors how often an unrecognised kid can force a refetch.
    private async Task<RsaSecurityKey> ResolveKeyAsync(string kid, CancellationToken ct)
    {
        lock (_lock)
        {
            if (_keys.TryGetValue(kid, out var cached))
            {
                return cached;
            }
        }

        bool shouldFetch;
        lock (_lock)
        {
            shouldFetch = _fetchedAt == default || DateTime.UtcNow - _fetchedAt >= MinJwksRefetchInterval;
        }
        if (shouldFetch)
        {
            await RefreshKeysAsync(ct);
        }

        lock (_lock)
        {
            return _keys.TryGetValue(kid, out var key)
                ? key
                : throw new SecurityTokenException($"apple: unknown key id \"{kid}\"");
        }
    }

    private async Task RefreshKeysAsync(CancellationToken ct)
    {
        var response = await http.GetAsync(JwksUrl, ct);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JwksResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("apple: decode jwks");

        var parsed = new Dictionary<string, RsaSecurityKey>();
        foreach (var k in payload.Keys)
        {
            if (k.Kty != "RSA" || string.IsNullOrEmpty(k.Kid))
            {
                continue;
            }
            try
            {
                var rsa = RSA.Create();
                rsa.ImportParameters(new RSAParameters
                {
                    Modulus = Base64UrlDecode(k.N),
                    Exponent = Base64UrlDecode(k.E),
                });
                parsed[k.Kid] = new RsaSecurityKey(rsa) { KeyId = k.Kid };
            }
            catch
            {
                // One malformed entry shouldn't invalidate the rest of the set.
            }
        }
        if (parsed.Count == 0)
        {
            throw new InvalidOperationException("apple: jwks contained no usable RSA keys");
        }

        lock (_lock)
        {
            _keys = parsed;
            _fetchedAt = DateTime.UtcNow;
        }
    }

    private static byte[] Base64UrlDecode(string input) =>
        Base64UrlEncoder.DecodeBytes(input);

    private async Task<string> PostFormAsync(string url, Dictionary<string, string> form, CancellationToken ct)
    {
        var response = await http.PostAsync(url, new FormUrlEncodedContent(form), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            // Apple returns {"error":"invalid_grant"} and friends; surfacing
            // the body makes a misconfigured key or a reused code diagnosable.
            throw new HttpRequestException($"apple: {url} returned {(int)response.StatusCode}: {body.Trim()}");
        }
        return body;
    }

    private sealed record AppleTokenResponse([property: JsonPropertyName("refresh_token")] string RefreshToken);

    private sealed record JwksResponse(List<AppleJwk> Keys);

    private sealed record AppleJwk(string Kty, string Kid, string N, string E);
}
