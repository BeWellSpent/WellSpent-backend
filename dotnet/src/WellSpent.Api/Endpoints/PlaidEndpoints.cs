using MediatR;
using WellSpent.Api.Auth;
using WellSpent.Application.Plaid;
using WellSpent.Application.Plaid.CreateLinkToken;
using WellSpent.Application.Plaid.DisconnectPlaid;
using WellSpent.Application.Plaid.ExchangePublicToken;
using WellSpent.Application.Plaid.GetPlaidConnections;
using WellSpent.Application.Plaid.RefreshPlaidAccounts;
using WellSpent.Application.Plaid.ResyncPlaidConnection;

namespace WellSpent.Api.Endpoints;

public static class PlaidEndpoints
{
    public static void MapPlaidEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/rest/v1/plaid").WithTags("plaid").RequireAuthorization();

        group.MapPost("/link-token", async (CreateLinkTokenRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var token = await sender.Send(new CreateLinkTokenCommand(
                CurrentUser.GetId(ctx), body.BudgetProfileId, body.ConnectionId, body.RedirectUri ?? ""), ct);
            return Results.Ok(new { linkToken = token.LinkToken, expiration = token.Expiration });
        });

        group.MapGet("/connections", async (Guid? budgetProfileId, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetPlaidConnectionsQuery(CurrentUser.GetId(ctx), budgetProfileId), ct);
            return Results.Ok(new { connections = result.Connections, warnings = result.Warnings });
        });

        group.MapPost("/connections", async (ExchangePublicTokenRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var connection = await sender.Send(new ExchangePublicTokenCommand(
                CurrentUser.GetId(ctx), body.BudgetProfileId, body.PublicToken), ct);
            return Results.Ok(new { connection });
        });

        group.MapDelete("/connections/{id:guid}", async (Guid id, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DisconnectPlaidCommand(CurrentUser.GetId(ctx), id), ct);
            return Results.Ok();
        });

        group.MapPost("/connections/{id:guid}/refresh-accounts", async (Guid id, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var connection = await sender.Send(new RefreshPlaidAccountsCommand(CurrentUser.GetId(ctx), id), ct);
            return Results.Ok(new { connection });
        });

        group.MapPost("/connections/{id:guid}/resync", async (Guid id, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var connection = await sender.Send(new ResyncPlaidConnectionCommand(CurrentUser.GetId(ctx), id), ct);
            return Results.Ok(new { connection });
        });
    }

    private sealed record CreateLinkTokenRequestBody(Guid BudgetProfileId, Guid? ConnectionId, string? RedirectUri);
    private sealed record ExchangePublicTokenRequestBody(string PublicToken, Guid BudgetProfileId);
}
