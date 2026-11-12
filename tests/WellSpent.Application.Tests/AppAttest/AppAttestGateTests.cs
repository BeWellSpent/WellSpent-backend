using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using WellSpent.Application.AppAttest;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using Xunit;

namespace WellSpent.Application.Tests.AppAttest;

public sealed class AppAttestGateTests
{
    private readonly TestAppAttestDevice _device = new();
    private readonly IAppAttestRepository _repo = Substitute.For<IAppAttestRepository>();

    private AppAttestGate Gate(bool allowDevelopment = false) => new(_repo,
        Options.Create(new AppAttestOptions { AppId = TestAppAttestDevice.AppId, AllowDevelopment = allowDevelopment }),
        NullLogger<AppAttestGate>.Instance);

    private AppAttestProof Proof(uint counter, string email = "a@b.com") => new(_device.KeyId,
        Convert.ToBase64String(_device.Assert(AppAttestGate.ClientDataHash("login", email, "ch"), counter)), "ch");

    private void KnownKey(string environment = AppAttestVerifier.Production)
    {
        _repo.ConsumeChallengeAsync("ch", Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
        _repo.GetKeyAsync(_device.KeyId, Arg.Any<CancellationToken>())
            .Returns(new AppAttestKey { KeyId = _device.KeyId, PublicKey = _device.PublicKey, Environment = environment, SignCount = 1 });
        _repo.AdvanceCounterAsync(_device.KeyId, Arg.Any<long>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    [Fact]
    public async Task ValidAssertion_Attested_AdvancesCounter()
    {
        KnownKey();

        Assert.Equal(AppAttestOutcome.Attested, await Gate().CheckAsync("login", " A@B.com ", Proof(2), null, CancellationToken.None));
        await _repo.Received().AdvanceCounterAsync(_device.KeyId, 2, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssertionForOtherEmail_Invalid()
    {
        KnownKey();
        Assert.Equal(AppAttestOutcome.Invalid, await Gate().CheckAsync("login", "other@b.com", Proof(2), null, CancellationToken.None));
    }

    [Fact]
    public async Task ReplayedCounter_Invalid()
    {
        KnownKey();
        _repo.AdvanceCounterAsync(_device.KeyId, Arg.Any<long>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(false);
        Assert.Equal(AppAttestOutcome.Invalid, await Gate().CheckAsync("login", "a@b.com", Proof(1), null, CancellationToken.None));
    }

    [Fact]
    public async Task UsedChallenge_Invalid_NeverLooksUpKey()
    {
        _repo.ConsumeChallengeAsync("ch", Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(false);
        Assert.Equal(AppAttestOutcome.Invalid, await Gate().CheckAsync("login", "a@b.com", Proof(2), null, CancellationToken.None));
        await _repo.DidNotReceive().GetKeyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DevelopmentKey_InvalidUnlessAllowed()
    {
        KnownKey(AppAttestVerifier.Development);
        Assert.Equal(AppAttestOutcome.Invalid, await Gate().CheckAsync("login", "a@b.com", Proof(2), null, CancellationToken.None));
        Assert.Equal(AppAttestOutcome.Attested, await Gate(allowDevelopment: true).CheckAsync("login", "a@b.com", Proof(3), null, CancellationToken.None));
    }

    [Theory]
    [InlineData("unsupported", AppAttestOutcome.Unavailable)]
    [InlineData("service_error", AppAttestOutcome.Unavailable)]
    [InlineData("anything", AppAttestOutcome.Missing)]
    [InlineData(null, AppAttestOutcome.Missing)]
    public async Task NoProof_ClassifiedByReason(string? reason, AppAttestOutcome expected) =>
        Assert.Equal(expected, await Gate().CheckAsync("login", "a@b.com", null, reason, CancellationToken.None));
}
