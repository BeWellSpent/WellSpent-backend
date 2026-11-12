using System.Buffers.Binary;
using System.Formats.Asn1;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace WellSpent.Application.AppAttest;

/// <summary>An attestation or assertion that doesn't verify; the message names the failed check.</summary>
public sealed class AppAttestVerificationException(string message) : Exception(message);

public sealed record AttestedKey(byte[] PublicKey, string Environment);

/// <summary>Apple App Attest verification, per Apple's "Validating apps that connect to your server".</summary>
public sealed class AppAttestVerifier(X509Certificate2 root)
{
    public const string Production = "production";
    public const string Development = "development";

    private const string NonceExtensionOid = "1.2.840.113635.100.8.2";
    private static readonly byte[] ProductionAaguid = [.. "appattest"u8, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] DevelopmentAaguid = "appattestdevelop"u8.ToArray();

    /// <summary>Apple App Attestation Root CA, from apple.com/certificateauthority.</summary>
    public const string AppleRootPem = """
        -----BEGIN CERTIFICATE-----
        MIICITCCAaegAwIBAgIQC/O+DvHN0uD7jG5yH2IXmDAKBggqhkjOPQQDAzBSMSYw
        JAYDVQQDDB1BcHBsZSBBcHAgQXR0ZXN0YXRpb24gUm9vdCBDQTETMBEGA1UECgwK
        QXBwbGUgSW5jLjETMBEGA1UECAwKQ2FsaWZvcm5pYTAeFw0yMDAzMTgxODMyNTNa
        Fw00NTAzMTUwMDAwMDBaMFIxJjAkBgNVBAMMHUFwcGxlIEFwcCBBdHRlc3RhdGlv
        biBSb290IENBMRMwEQYDVQQKDApBcHBsZSBJbmMuMRMwEQYDVQQIDApDYWxpZm9y
        bmlhMHYwEAYHKoZIzj0CAQYFK4EEACIDYgAERTHhmLW07ATaFQIEVwTtT4dyctdh
        NbJhFs/Ii2FdCgAHGbpphY3+d8qjuDngIN3WVhQUBHAoMeQ/cLiP1sOUtgjqK9au
        Yen1mMEvRq9Sk3Jm5X8U62H+xTD3FE9TgS41o0IwQDAPBgNVHRMBAf8EBTADAQH/
        MB0GA1UdDgQWBBSskRBTM72+aEH/pwyp5frq5eWKoTAOBgNVHQ8BAf8EBAMCAQYw
        CgYIKoZIzj0EAwMDaAAwZQIwQgFGnByvsiVbpTKwSga0kP0e8EeDS4+sQmTvb7vn
        53O5+FRXgeLhpJ06ysC5PrOyAjEAp5U4xDgEgllF7En3VcE3iexZZtKeYnpqtijV
        oyFraWVIyd/dganmrduC1bmTBGwD
        -----END CERTIFICATE-----
        """;

    public static X509Certificate2 LoadAppleRoot() => X509Certificate2.CreateFromPem(AppleRootPem);

    /// <summary>Verifies a key's attestation and returns its public key (SubjectPublicKeyInfo) and environment.</summary>
    public AttestedKey VerifyAttestation(byte[] attestation, byte[] clientDataHash, byte[] keyId, string appId)
    {
        var map = ReadMap(attestation);
        if (map.GetValueOrDefault("fmt") as string != "apple-appattest")
        {
            throw new AppAttestVerificationException("unexpected attestation format");
        }
        var authData = map.GetValueOrDefault("authData") as byte[] ?? throw new AppAttestVerificationException("missing authData");
        var statement = map.GetValueOrDefault("attStmt") as Dictionary<string, object?> ?? throw new AppAttestVerificationException("missing attStmt");
        var x5c = (statement.GetValueOrDefault("x5c") as List<object?>)?.OfType<byte[]>().ToList();
        if (x5c is null || x5c.Count < 2)
        {
            throw new AppAttestVerificationException("missing certificate chain");
        }

        using var credCert = X509CertificateLoader.LoadCertificate(x5c[0]);
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(root);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        foreach (var der in x5c.Skip(1))
        {
            chain.ChainPolicy.ExtraStore.Add(X509CertificateLoader.LoadCertificate(der));
        }
        if (!chain.Build(credCert))
        {
            var status = string.Join(",", chain.ChainStatus.Select(s => s.Status));
            throw new AppAttestVerificationException($"certificate chain invalid: {status}");
        }

        var nonce = SHA256.HashData([.. authData, .. clientDataHash]);
        if (!CryptographicOperations.FixedTimeEquals(nonce, ReadNonceExtension(credCert)))
        {
            throw new AppAttestVerificationException("nonce mismatch");
        }

        using var ecdsa = credCert.GetECDsaPublicKey() ?? throw new AppAttestVerificationException("certificate key is not EC");
        var q = ecdsa.ExportParameters(false).Q;
        if (!SHA256.HashData([0x04, .. q.X!, .. q.Y!]).AsSpan().SequenceEqual(keyId))
        {
            throw new AppAttestVerificationException("key ID doesn't match the certified key");
        }

        if (authData.Length < 55)
        {
            throw new AppAttestVerificationException("authData too short");
        }
        CheckRpIdHash(authData, appId);
        if (BinaryPrimitives.ReadUInt32BigEndian(authData.AsSpan(33, 4)) != 0)
        {
            throw new AppAttestVerificationException("counter is not zero");
        }
        var aaguid = authData.AsSpan(37, 16);
        var environment = aaguid.SequenceEqual(ProductionAaguid) ? Production
            : aaguid.SequenceEqual(DevelopmentAaguid) ? Development
            : throw new AppAttestVerificationException("unknown aaguid");
        var credIdLength = BinaryPrimitives.ReadUInt16BigEndian(authData.AsSpan(53, 2));
        if (authData.Length < 55 + credIdLength || !authData.AsSpan(55, credIdLength).SequenceEqual(keyId))
        {
            throw new AppAttestVerificationException("credential ID doesn't match the key ID");
        }

        return new AttestedKey(ecdsa.ExportSubjectPublicKeyInfo(), environment);
    }

    /// <summary>Verifies an assertion against the stored public key and returns its counter.</summary>
    public static uint VerifyAssertion(byte[] assertion, byte[] clientDataHash, byte[] publicKey, string appId)
    {
        var map = ReadMap(assertion);
        var signature = map.GetValueOrDefault("signature") as byte[] ?? throw new AppAttestVerificationException("missing signature");
        var authData = map.GetValueOrDefault("authenticatorData") as byte[] ?? throw new AppAttestVerificationException("missing authenticatorData");
        if (authData.Length < 37)
        {
            throw new AppAttestVerificationException("authenticatorData too short");
        }

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);
        var nonce = SHA256.HashData([.. authData, .. clientDataHash]);
        if (!ecdsa.VerifyData(nonce, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
        {
            throw new AppAttestVerificationException("signature invalid");
        }
        CheckRpIdHash(authData, appId);
        return BinaryPrimitives.ReadUInt32BigEndian(authData.AsSpan(33, 4));
    }

    private static void CheckRpIdHash(byte[] authData, string appId)
    {
        if (!authData.AsSpan(0, 32).SequenceEqual(SHA256.HashData(Encoding.UTF8.GetBytes(appId))))
        {
            throw new AppAttestVerificationException("app ID mismatch");
        }
    }

    // DER: SEQUENCE { [1] EXPLICIT OCTET STRING nonce }
    private static byte[] ReadNonceExtension(X509Certificate2 cert)
    {
        var ext = cert.Extensions[NonceExtensionOid] ?? throw new AppAttestVerificationException("missing nonce extension");
        try
        {
            var seq = new AsnReader(ext.RawData, AsnEncodingRules.DER).ReadSequence();
            return seq.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 1)).ReadOctetString();
        }
        catch (AsnContentException ex)
        {
            throw new AppAttestVerificationException($"malformed nonce extension: {ex.Message}");
        }
    }

    private static Dictionary<string, object?> ReadMap(byte[] cbor)
    {
        try
        {
            var reader = new CborReader(cbor, CborConformanceMode.Lax);
            return ReadValue(reader) as Dictionary<string, object?> ?? throw new AppAttestVerificationException("expected a CBOR map");
        }
        catch (Exception ex) when (ex is CborContentException or InvalidOperationException)
        {
            throw new AppAttestVerificationException($"malformed CBOR: {ex.Message}");
        }
    }

    private static object? ReadValue(CborReader reader)
    {
        switch (reader.PeekState())
        {
            case CborReaderState.StartMap:
                var map = new Dictionary<string, object?>();
                reader.ReadStartMap();
                while (reader.PeekState() != CborReaderState.EndMap)
                {
                    var key = reader.ReadTextString();
                    map[key] = ReadValue(reader);
                }
                reader.ReadEndMap();
                return map;
            case CborReaderState.StartArray:
                var list = new List<object?>();
                reader.ReadStartArray();
                while (reader.PeekState() != CborReaderState.EndArray)
                {
                    list.Add(ReadValue(reader));
                }
                reader.ReadEndArray();
                return list;
            case CborReaderState.ByteString:
                return reader.ReadByteString();
            case CborReaderState.TextString:
                return reader.ReadTextString();
            default:
                reader.SkipValue();
                return null;
        }
    }
}
