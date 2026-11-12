using MediatR;
using WellSpent.Application.AppAttest.CreateChallenge;
using WellSpent.Application.AppAttest.RegisterKey;

namespace WellSpent.Api.Endpoints;

/// <summary>Public: an app attests its key before it has an account (#121).</summary>
public static class AppAttestEndpoints
{
    public static void MapAppAttestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/rest/v1/app-attest").WithTags("auth");

        group.MapPost("/challenge", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateAppAttestChallengeCommand(), ct);
            return Results.Ok(new { challenge = result.Challenge, expiresAt = result.ExpiresAt });
        });

        group.MapPost("/keys", async (AppAttestKeyRequestBody body, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new RegisterAppAttestKeyCommand(body.KeyId, body.Attestation, body.Challenge), ct);
            return Results.NoContent();
        });
    }

    private sealed record AppAttestKeyRequestBody(string KeyId, string Attestation, string Challenge);
}
