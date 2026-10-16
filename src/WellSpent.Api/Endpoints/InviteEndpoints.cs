using MediatR;
using WellSpent.Api.Auth;
using WellSpent.Application.Invites.AcceptBudgetInvite;
using WellSpent.Application.Invites.CancelBudgetInvite;
using WellSpent.Application.Invites.GetBudgetInvite;
using WellSpent.Application.Invites.ListBudgetInvites;
using WellSpent.Application.Invites.SendBudgetInvite;

namespace WellSpent.Api.Endpoints;

public static class InviteEndpoints
{
    public static void MapInviteEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/rest/v1/invites").WithTags("invites");

        group.MapPost("", async (SendInviteRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var invite = await sender.Send(new SendBudgetInviteCommand(
                CurrentUser.GetId(ctx), body.BudgetProfileId, body.Email, body.Role, body.BudgetPersonId), ct);
            return Results.Ok(new { invite });
        }).RequireAuthorization();

        group.MapGet("", async (Guid budgetProfileId, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var invites = await sender.Send(new ListBudgetInvitesQuery(CurrentUser.GetId(ctx), budgetProfileId), ct);
            return Results.Ok(new { invites });
        }).RequireAuthorization();

        group.MapPost("/{id:guid}/cancel", async (Guid id, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new CancelBudgetInviteCommand(CurrentUser.GetId(ctx), id), ct);
            return Results.Ok();
        }).RequireAuthorization();

        // Separate namespace from /invites/{id} (admin-id-scoped management)
        // rather than /invites/by-token/{token} — the two look structurally
        // ambiguous to an OpenAPI path matcher (same segment depth, {id} vs a
        // literal at the same position). See openapi/v1/wellspent.yaml.
        var tokenGroup = app.MapGroup("/rest/v1/invite-tokens").WithTags("invites");

        // Public — no auth required. Drives the invite preview page shown
        // before the user authenticates.
        tokenGroup.MapGet("/{token:guid}", async (Guid token, ISender sender, CancellationToken ct) =>
        {
            var invite = await sender.Send(new GetBudgetInviteQuery(token), ct);
            return Results.Ok(new { invite });
        });

        tokenGroup.MapPost("/{token:guid}/accept", async (Guid token, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var budgetProfileId = await sender.Send(new AcceptBudgetInviteCommand(CurrentUser.GetId(ctx), token), ct);
            return Results.Ok(new { budgetProfileId });
        }).RequireAuthorization();
    }

    private sealed record SendInviteRequestBody(Guid BudgetProfileId, string Email, string Role, int? BudgetPersonId);
}
