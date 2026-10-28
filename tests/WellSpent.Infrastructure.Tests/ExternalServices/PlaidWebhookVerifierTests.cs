using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using WellSpent.Application.Abstractions;
using WellSpent.Infrastructure.ExternalServices;

namespace WellSpent.Infrastructure.Tests.ExternalServices;

/// <summary>
/// Generates real ES256 JWTs in-process (no network) to exercise
/// PlaidWebhookVerifier's full signature + anti-replay + body-hash check.
/// Only the key lookup (IPlaidClient.GetWebhookVerificationKeyAsync) is mocked.
/// </summary>
public sealed class PlaidWebhookVerifierTests
{
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"webhook_type":"TRANSACTIONS","webhook_code":"SYNC_UPDATES_AVAILABLE","item_id":"item-123"}""");

    [Fact]
    public async Task ValidSignatureAndMatchingBody_Passes()
    {
        var (ecdsa, kid) = NewKeyPair();
        var jwt = CreateJwt(ecdsa, kid, Body, DateTimeOffset.UtcNow);
        var verifier = BuildVerifier(kid, ecdsa);

        Assert.True(await verifier.VerifyAsync(jwt, Body, CancellationToken.None));
    }

    [Fact]
    public async Task TamperedBody_Fails()
    {
        var (ecdsa, kid) = NewKeyPair();
        var jwt = CreateJwt(ecdsa, kid, Body, DateTimeOffset.UtcNow);
        var verifier = BuildVerifier(kid, ecdsa);

        var tamperedBody = Encoding.UTF8.GetBytes("""{"webhook_type":"TRANSACTIONS","webhook_code":"SYNC_UPDATES_AVAILABLE","item_id":"item-999"}""");

        Assert.False(await verifier.VerifyAsync(jwt, tamperedBody, CancellationToken.None));
    }

    [Fact]
    public async Task ExpiredIat_Fails()
    {
        var (ecdsa, kid) = NewKeyPair();
        var jwt = CreateJwt(ecdsa, kid, Body, DateTimeOffset.UtcNow.AddMinutes(-10));
        var verifier = BuildVerifier(kid, ecdsa);

        Assert.False(await verifier.VerifyAsync(jwt, Body, CancellationToken.None));
    }

    [Fact]
    public async Task WrongKey_Fails()
    {
        var (signingKey, kid) = NewKeyPair();
        var (unrelatedKey, _) = NewKeyPair();
        var jwt = CreateJwt(signingKey, kid, Body, DateTimeOffset.UtcNow);

        // The verifier fetches a key under the right kid, but it's the wrong key's public half.
        var verifier = BuildVerifier(kid, unrelatedKey);

        Assert.False(await verifier.VerifyAsync(jwt, Body, CancellationToken.None));
    }

    [Fact]
    public async Task MissingVerificationHeader_Fails()
    {
        var (ecdsa, kid) = NewKeyPair();
        var verifier = BuildVerifier(kid, ecdsa);

        Assert.False(await verifier.VerifyAsync("", Body, CancellationToken.None));
    }

    private static (ECDsa Ecdsa, string Kid) NewKeyPair() => (ECDsa.Create(ECCurve.NamedCurves.nistP256), Guid.NewGuid().ToString());

    private static string CreateJwt(ECDsa signingKey, string kid, byte[] body, DateTimeOffset issuedAt)
    {
        // Disabled: JwtSecurityTokenHandler otherwise stamps "iat" with the current time
        // itself whenever Expires/IssuedAt aren't set on the descriptor, silently
        // overriding the custom (possibly stale) "iat" claim this test is trying to set.
        var handler = new JwtSecurityTokenHandler { SetDefaultTimesOnTokenCreation = false };
        var bodyHash = Convert.ToHexStringLower(SHA256.HashData(body));
        var descriptor = new SecurityTokenDescriptor
        {
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Iat] = issuedAt.ToUnixTimeSeconds(),
                ["request_body_sha256"] = bodyHash,
            },
            SigningCredentials = new SigningCredentials(new ECDsaSecurityKey(signingKey) { KeyId = kid }, SecurityAlgorithms.EcdsaSha256),
        };
        return handler.CreateEncodedJwt(descriptor);
    }

    private static PlaidWebhookVerifier BuildVerifier(string kid, ECDsa publicKeySource)
    {
        var parameters = publicKeySource.ExportParameters(false);
        var plaid = Substitute.For<IPlaidClient>();
        plaid.GetWebhookVerificationKeyAsync(kid, Arg.Any<CancellationToken>()).Returns(new PlaidWebhookVerificationKey(
            kid, "P-256", Base64UrlEncoder.Encode(parameters.Q.X), Base64UrlEncoder.Encode(parameters.Q.Y), ExpiredAt: null));
        return new PlaidWebhookVerifier(plaid, NullLogger<PlaidWebhookVerifier>.Instance);
    }
}
