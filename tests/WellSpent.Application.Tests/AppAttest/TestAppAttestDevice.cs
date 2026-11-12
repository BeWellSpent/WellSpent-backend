using System.Buffers.Binary;
using System.Formats.Asn1;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace WellSpent.Application.Tests.AppAttest;

/// <summary>Stands in for Apple: a test root and intermediate, and a device key that attests and asserts like DCAppAttestService.</summary>
public sealed class TestAppAttestDevice
{
    public const string AppId = "TEAM.com.example.app";

    private readonly X509Certificate2 _intermediate;
    private readonly ECDsa _deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public X509Certificate2 Root { get; }
    public byte[] KeyIdBytes { get; }
    public string KeyId => Convert.ToBase64String(KeyIdBytes);
    public byte[] PublicKey => _deviceKey.ExportSubjectPublicKeyInfo();

    public TestAppAttestDevice()
    {
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Root = CaRequest("CN=Test App Attest Root", rootKey).CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var interKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _intermediate = CaRequest("CN=Test App Attest CA", interKey)
            .Create(Root, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddMonths(6), [1, 2, 3])
            .CopyWithPrivateKey(interKey);
        var q = _deviceKey.ExportParameters(false).Q;
        KeyIdBytes = SHA256.HashData([0x04, .. q.X!, .. q.Y!]);
    }

    public byte[] Attest(string challenge, string appId = AppId, string aaguid = "appattest", byte[]? nonceOverride = null)
    {
        var authData = new List<byte>(SHA256.HashData(Encoding.UTF8.GetBytes(appId))) { 0x40, 0, 0, 0, 0 };
        var aaguidBytes = new byte[16];
        Encoding.ASCII.GetBytes(aaguid).CopyTo(aaguidBytes, 0);
        authData.AddRange(aaguidBytes);
        authData.AddRange([0, (byte)KeyIdBytes.Length]);
        authData.AddRange(KeyIdBytes);
        var nonce = nonceOverride ?? SHA256.HashData([.. authData, .. SHA256.HashData(Encoding.UTF8.GetBytes(challenge))]);

        var leafRequest = new CertificateRequest("CN=device", _deviceKey, HashAlgorithmName.SHA256);
        var asn = new AsnWriter(AsnEncodingRules.DER);
        using (asn.PushSequence())
        using (asn.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 1, true)))
        {
            asn.WriteOctetString(nonce);
        }
        leafRequest.CertificateExtensions.Add(new X509Extension("1.2.840.113635.100.8.2", asn.Encode(), false));
        using var leaf = leafRequest.Create(_intermediate, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(1), [4, 5, 6]);

        var cbor = new CborWriter();
        cbor.WriteStartMap(3);
        cbor.WriteTextString("fmt");
        cbor.WriteTextString("apple-appattest");
        cbor.WriteTextString("attStmt");
        cbor.WriteStartMap(2);
        cbor.WriteTextString("x5c");
        cbor.WriteStartArray(2);
        cbor.WriteByteString(leaf.RawData);
        cbor.WriteByteString(_intermediate.RawData);
        cbor.WriteEndArray();
        cbor.WriteTextString("receipt");
        cbor.WriteByteString([1, 2, 3]);
        cbor.WriteEndMap();
        cbor.WriteTextString("authData");
        cbor.WriteByteString([.. authData]);
        cbor.WriteEndMap();
        return cbor.Encode();
    }

    public byte[] Assert(byte[] clientDataHash, uint counter, string appId = AppId)
    {
        var authData = new byte[37];
        SHA256.HashData(Encoding.UTF8.GetBytes(appId)).CopyTo(authData, 0);
        authData[32] = 0x40;
        BinaryPrimitives.WriteUInt32BigEndian(authData.AsSpan(33), counter);
        var signature = _deviceKey.SignData(SHA256.HashData([.. authData, .. clientDataHash]), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        var cbor = new CborWriter();
        cbor.WriteStartMap(2);
        cbor.WriteTextString("signature");
        cbor.WriteByteString(signature);
        cbor.WriteTextString("authenticatorData");
        cbor.WriteByteString(authData);
        cbor.WriteEndMap();
        return cbor.Encode();
    }

    private static CertificateRequest CaRequest(string name, ECDsa key)
    {
        var request = new CertificateRequest(name, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        return request;
    }
}
