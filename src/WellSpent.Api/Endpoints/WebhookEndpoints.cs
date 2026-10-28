using System.Text.Json;
using Going.Plaid;
using Going.Plaid.Converters;
using MediatR;
using Microsoft.Extensions.Logging;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Plaid.HandlePlaidWebhook;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Api.Endpoints;

public static class WebhookEndpoints
{
    // Plaid's webhook payloads are small JSON objects; generous ceiling against an abusive sender.
    private const int MaxBodyBytes = 1024 * 1024;

    public static void MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        // No .RequireAuthorization(): Plaid's own JWT signature (verified below) is the
        // authenticity check here, not a bearer token — same reasoning as /auth's public routes.
        var group = app.MapGroup("/webhooks").WithTags("webhooks");

        group.MapPost("/plaid", HandlePlaidWebhookAsync);
    }

    private static async Task<IResult> HandlePlaidWebhookAsync(
        HttpContext ctx, IPlaidWebhookVerifier verifier, ISender sender, ILogger<WebhookLogCategory> logger, CancellationToken ct)
    {
        var rawBody = await ReadBodyCappedAsync(ctx.Request.Body, MaxBodyBytes, ct);
        if (rawBody is null)
        {
            logger.LogWarning("plaid.webhook.body_too_large max_bytes={MaxBytes}", MaxBodyBytes);
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var verificationHeader = ctx.Request.Headers["Plaid-Verification"].ToString();
        if (!await verifier.VerifyAsync(verificationHeader, rawBody, ct))
        {
            throw new UnauthorizedException("plaid webhook signature verification failed");
        }

        var options = new JsonSerializerOptions { Converters = { new WebhookBaseConverter() } };
        var payload = JsonSerializer.Deserialize<WebhookBase>(rawBody, options);
        if (payload is null)
        {
            logger.LogWarning("plaid.webhook.empty_payload");
            return Results.Ok();
        }

        // Always 200 regardless of what the command did internally — only a transport-level failure (bad signature, above) should make Plaid retry.
        await sender.Send(new HandlePlaidWebhookCommand(payload), ct);
        return Results.Ok();
    }

    private static async Task<byte[]?> ReadBodyCappedAsync(Stream body, int maxBytes, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await body.ReadAsync(buffer, ct)) > 0)
        {
            ms.Write(buffer, 0, read);
            if (ms.Length > maxBytes)
            {
                return null;
            }
        }

        return ms.ToArray();
    }

    // Marker type for a logger category — WebhookEndpoints itself is static and can't be used as a generic type argument.
    private sealed class WebhookLogCategory;
}
