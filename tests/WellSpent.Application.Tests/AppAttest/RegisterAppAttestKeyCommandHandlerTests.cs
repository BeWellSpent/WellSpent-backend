using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using WellSpent.Application.AppAttest;
using WellSpent.Application.AppAttest.RegisterKey;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.AppAttest;

public sealed class RegisterAppAttestKeyCommandHandlerTests
{
    private readonly TestAppAttestDevice _device = new();
    private readonly IAppAttestRepository _repo = Substitute.For<IAppAttestRepository>();

    private RegisterAppAttestKeyCommandHandler Handler(bool allowDevelopment = false) => new(_repo, new AppAttestVerifier(_device.Root),
        Options.Create(new AppAttestOptions { AppId = TestAppAttestDevice.AppId, AllowDevelopment = allowDevelopment }),
        NullLogger<RegisterAppAttestKeyCommandHandler>.Instance);

    private RegisterAppAttestKeyCommand Command(string aaguid = "appattest") =>
        new(_device.KeyId, Convert.ToBase64String(_device.Attest("ch", aaguid: aaguid)), "ch");

    public RegisterAppAttestKeyCommandHandlerTests() =>
        _repo.ConsumeChallengeAsync("ch", Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);

    [Fact]
    public async Task ValidAttestation_StoresKey()
    {
        await Handler().Handle(Command(), CancellationToken.None);

        await _repo.Received().SaveKeyAsync(Arg.Is<AppAttestKey>(k =>
            k.KeyId == _device.KeyId && k.PublicKey.SequenceEqual(_device.PublicKey) && k.Environment == AppAttestVerifier.Production),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UsedChallenge_Rejected()
    {
        _repo.ConsumeChallengeAsync("ch", Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(false);
        await Assert.ThrowsAsync<AppValidationException>(() => Handler().Handle(Command(), CancellationToken.None));
    }

    [Fact]
    public async Task DevelopmentKey_RejectedInProd_AcceptedInDev()
    {
        await Assert.ThrowsAsync<AppValidationException>(() => Handler().Handle(Command("appattestdevelop"), CancellationToken.None));
        await Handler(allowDevelopment: true).Handle(Command("appattestdevelop"), CancellationToken.None);
        await _repo.Received(1).SaveKeyAsync(Arg.Any<AppAttestKey>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task KeyIdNotMatchingCertificate_Rejected()
    {
        var other = new TestAppAttestDevice();
        await Assert.ThrowsAsync<AppValidationException>(
            () => Handler().Handle(Command() with { KeyId = other.KeyId }, CancellationToken.None));
        await _repo.DidNotReceive().SaveKeyAsync(Arg.Any<AppAttestKey>(), Arg.Any<CancellationToken>());
    }
}
