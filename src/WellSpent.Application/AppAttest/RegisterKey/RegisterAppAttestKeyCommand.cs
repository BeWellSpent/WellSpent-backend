using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.AppAttest.RegisterKey;

public sealed record RegisterAppAttestKeyCommand(string KeyId, string Attestation, string Challenge) : IRequest;

/// <summary>Verifies a key's attestation once per install and stores its public key.</summary>
public sealed class RegisterAppAttestKeyCommandHandler(
    IAppAttestRepository repo, AppAttestVerifier verifier, IOptions<AppAttestOptions> options,
    ILogger<RegisterAppAttestKeyCommandHandler> logger) : IRequestHandler<RegisterAppAttestKeyCommand>
{
    public async Task Handle(RegisterAppAttestKeyCommand request, CancellationToken ct)
    {
        if (!await repo.ConsumeChallengeAsync(request.Challenge, DateTime.UtcNow, ct))
        {
            logger.LogWarning("app_attest.register_key.challenge_rejected key_id={KeyId}", request.KeyId);
            throw new AppValidationException("challenge expired or already used");
        }

        AttestedKey attested;
        try
        {
            attested = verifier.VerifyAttestation(
                Convert.FromBase64String(request.Attestation),
                SHA256.HashData(Encoding.UTF8.GetBytes(request.Challenge)),
                Convert.FromBase64String(request.KeyId),
                options.Value.AppId);
        }
        catch (Exception ex) when (ex is AppAttestVerificationException or FormatException or CryptographicException)
        {
            logger.LogWarning("app_attest.register_key.invalid key_id={KeyId} reason={Reason}", request.KeyId, ex.Message);
            throw new AppValidationException("couldn't verify this device");
        }

        if (attested.Environment == AppAttestVerifier.Development && !options.Value.AllowDevelopment)
        {
            logger.LogWarning("app_attest.register_key.development_rejected key_id={KeyId}", request.KeyId);
            throw new AppValidationException("couldn't verify this device");
        }

        await repo.SaveKeyAsync(new AppAttestKey
        {
            KeyId = request.KeyId,
            PublicKey = attested.PublicKey,
            SignCount = 0,
            Environment = attested.Environment,
        }, ct);
        logger.LogInformation("app_attest.register_key.success key_id={KeyId} environment={Environment}", request.KeyId, attested.Environment);
    }
}
