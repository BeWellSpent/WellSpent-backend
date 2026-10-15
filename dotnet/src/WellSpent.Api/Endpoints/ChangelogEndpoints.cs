using MediatR;
using WellSpent.Api.Auth;
using WellSpent.Application.Changelog.CreateChangelogRelease;
using WellSpent.Application.Changelog.ListChangelog;

namespace WellSpent.Api.Endpoints;

public static class ChangelogEndpoints
{
    public static void MapChangelogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/rest/v1/changelog").WithTags("changelog").RequireAuthorization();

        // Authenticated but not superuser-gated — the reader-facing call
        // behind the "what's new" prompt and the Help browser. Same
        // path/shape as the existing Go REST endpoint.
        group.MapGet("", async (string[]? component, int? limitPerComponent, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListChangelogQuery(component ?? [], limitPerComponent ?? 0), ct);
            return Results.Ok(new { releases = result.Releases, currentServerVersion = result.CurrentServerVersion });
        });

        // Superuser-only, enforced in the handler. No admin UI — releases are
        // normally published via scripts/changelog.sh; this is the
        // equivalent direct call for the new backend.
        group.MapPost("/releases", async (CreateReleaseRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var items = body.Items.Select(i => new CreateChangelogItemInput(i.ChangeType, i.SummaryEn, i.SummaryEs)).ToList();
            var release = await sender.Send(new CreateChangelogReleaseCommand(
                CurrentUser.GetId(ctx), body.Component, body.Version, body.ReleasedAt, items), ct);
            return Results.Ok(new { release });
        });
    }

    private sealed record CreateReleaseItemBody(string ChangeType, string SummaryEn, string SummaryEs);

    private sealed record CreateReleaseRequestBody(
        string Component, string Version, DateTime? ReleasedAt, List<CreateReleaseItemBody> Items);
}
