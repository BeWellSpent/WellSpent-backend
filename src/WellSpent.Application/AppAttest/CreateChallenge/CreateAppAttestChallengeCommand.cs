using System.Security.Cryptography;
using MediatR;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.AppAttest.CreateChallenge;

public sealed record CreateAppAttestChallengeCommand : IRequest<AppAttestChallengeResult>;

public sealed record AppAttestChallengeResult(string Challenge, DateTime ExpiresAt);

public sealed class CreateAppAttestChallengeCommandHandler(IAppAttestRepository repo)
    : IRequestHandler<CreateAppAttestChallengeCommand, AppAttestChallengeResult>
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public async Task<AppAttestChallengeResult> Handle(CreateAppAttestChallengeCommand request, CancellationToken ct)
    {
        var challenge = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var expiresAt = DateTime.UtcNow.Add(Lifetime);
        await repo.CreateChallengeAsync(challenge, expiresAt, ct);
        return new AppAttestChallengeResult(challenge, expiresAt);
    }
}
