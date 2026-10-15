using MediatR;
using WellSpent.Api.Auth;
using WellSpent.Application.Status;
using WellSpent.Application.Status.CreateStatusBanner;
using WellSpent.Application.Status.ExpireStatusBanner;
using WellSpent.Application.Status.GetActiveStatusBanner;
using WellSpent.Application.Status.ListStatusBanners;

namespace WellSpent.Api.Endpoints;

public static class StatusEndpoints
{
    public static void MapStatusEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/rest/v1/status").WithTags("status");

        // Public — a signed-out visitor on a broken login screen is one of
        // the people who most needs to read it. Same path/shape as the
        // existing Go REST endpoint.
        group.MapGet("/banner", async (ISender sender, CancellationToken ct) =>
        {
            var banner = await sender.Send(new GetActiveStatusBannerQuery(), ct);
            return Results.Ok(new { banner });
        });

        // The three below are superuser-only — enforced in the handler, not
        // just the route, mirroring Go's assertSuperuser. No admin UI: these
        // are called directly during an incident.
        var operatorGroup = group.MapGroup("/banners").RequireAuthorization();

        operatorGroup.MapPost("", async (CreateStatusBannerRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var banner = await sender.Send(new CreateStatusBannerCommand(
                CurrentUser.GetId(ctx), body.Severity, body.MessageEn, body.MessageEs, body.StartsAt, body.EndsAt), ct);
            return Results.Ok(new { banner });
        });

        operatorGroup.MapGet("", async (int? limit, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var banners = await sender.Send(new ListStatusBannersQuery(CurrentUser.GetId(ctx), limit ?? 0), ct);
            return Results.Ok(new { banners });
        });

        operatorGroup.MapPost("/{id:guid}/expire", async (Guid id, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var banner = await sender.Send(new ExpireStatusBannerCommand(CurrentUser.GetId(ctx), id), ct);
            return Results.Ok(new { banner });
        });
    }

    private sealed record CreateStatusBannerRequestBody(
        string Severity, string MessageEn, string MessageEs, DateTime? StartsAt, DateTime EndsAt);
}
