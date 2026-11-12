using System.Security.Cryptography;
using System.Text;
using WellSpent.Application.AppAttest;
using Xunit;

namespace WellSpent.Application.Tests.AppAttest;

public sealed class AppAttestVerifierTests
{
    private readonly TestAppAttestDevice _device = new();
    private static byte[] Hash(string s) => SHA256.HashData(Encoding.UTF8.GetBytes(s));

    private AttestedKey Verify(byte[] attestation, string challenge = "c1") =>
        new AppAttestVerifier(_device.Root).VerifyAttestation(attestation, Hash(challenge), _device.KeyIdBytes, TestAppAttestDevice.AppId);

    [Fact]
    public void Attestation_Valid_ReturnsDevicePublicKeyAndProduction()
    {
        var key = Verify(_device.Attest("c1"));
        Assert.Equal(_device.PublicKey, key.PublicKey);
        Assert.Equal(AppAttestVerifier.Production, key.Environment);
    }

    [Fact]
    public void Attestation_DevelopmentAaguid_ReportsDevelopment() =>
        Assert.Equal(AppAttestVerifier.Development, Verify(_device.Attest("c1", aaguid: "appattestdevelop")).Environment);

    [Fact]
    public void Attestation_OtherChallenge_FailsNonce() =>
        Assert.Contains("nonce", Assert.Throws<AppAttestVerificationException>(() => Verify(_device.Attest("c1"), "c2")).Message);

    [Fact]
    public void Attestation_OtherApp_Fails() =>
        Assert.Contains("app ID", Assert.Throws<AppAttestVerificationException>(() => Verify(_device.Attest("c1", appId: "TEAM.other"))).Message);

    [Fact]
    public void Attestation_UntrustedRoot_Fails()
    {
        var other = new TestAppAttestDevice();
        var verifier = new AppAttestVerifier(other.Root);
        Assert.Contains("chain", Assert.Throws<AppAttestVerificationException>(
            () => verifier.VerifyAttestation(_device.Attest("c1"), Hash("c1"), _device.KeyIdBytes, TestAppAttestDevice.AppId)).Message);
    }

    [Fact]
    public void Attestation_AppleRootLoads() => Assert.Contains("Apple App Attestation Root CA", AppAttestVerifier.LoadAppleRoot().Subject);

    [Fact]
    public void Attestation_Garbage_Fails() => Assert.Throws<AppAttestVerificationException>(() => Verify([1, 2, 3]));

    [Fact]
    public void Assertion_Valid_ReturnsCounter() =>
        Assert.Equal(7u, AppAttestVerifier.VerifyAssertion(_device.Assert(Hash("x"), 7), Hash("x"), _device.PublicKey, TestAppAttestDevice.AppId));

    [Fact]
    public void Assertion_OtherClientData_FailsSignature() =>
        Assert.Contains("signature", Assert.Throws<AppAttestVerificationException>(
            () => AppAttestVerifier.VerifyAssertion(_device.Assert(Hash("x"), 1), Hash("y"), _device.PublicKey, TestAppAttestDevice.AppId)).Message);

    [Fact]
    public void Assertion_OtherApp_Fails() =>
        Assert.Throws<AppAttestVerificationException>(
            () => AppAttestVerifier.VerifyAssertion(_device.Assert(Hash("x"), 1, "TEAM.other"), Hash("x"), _device.PublicKey, TestAppAttestDevice.AppId));
}
