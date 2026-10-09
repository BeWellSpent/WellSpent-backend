using MediatR;
using WellSpent.Api.Auth;
using WellSpent.Application.Budgets.AddBudgetPeople;
using WellSpent.Application.Budgets.CreateBudgetPeriod;
using WellSpent.Application.Budgets.CreateBudgetProfile;
using WellSpent.Application.Budgets.DeleteBudgetProfile;
using WellSpent.Application.Budgets.GetBudgetPeriod;
using WellSpent.Application.Budgets.GetBudgetProfile;
using WellSpent.Application.Budgets.ListBudgetPeople;
using WellSpent.Application.Budgets.ListBudgetPeriods;
using WellSpent.Application.Budgets.ListBudgetProfiles;
using WellSpent.Application.Budgets.RemoveBudgetPerson;
using WellSpent.Application.Budgets.SetBudgetAutoUpdatePlannedAmount;
using WellSpent.Application.Budgets.SetBudgetCarryoverEnabled;
using WellSpent.Application.Budgets.UpdateBudgetPerson;
using WellSpent.Application.Budgets.UpdateBudgetPersonRole;
using WellSpent.Application.Budgets.UpdateBudgetProfile;
using WellSpent.Application.Budgets.UpdateMyBudgetPreferences;
using WellSpent.Application.Budgets.UpdateMyFocusedViewPreference;
using WellSpent.Application.Budgets.UpdateMyManualMatchReviewPreference;

namespace WellSpent.Api.Endpoints;

public static class BudgetEndpoints
{
    public static void MapBudgetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/rest/v1/budgets").WithTags("budgets").RequireAuthorization();

        // ── Profile CRUD ─────────────────────────────────────────────────
        group.MapPost("", async (CreateBudgetProfileRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateBudgetProfileCommand(CurrentUser.GetId(ctx), body.Name, body.Cycle), ct);
            return Results.Ok(new { profile = result.Profile, period = result.Period });
        });

        group.MapGet("", async (HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var profiles = await sender.Send(new ListBudgetProfilesQuery(CurrentUser.GetId(ctx)), ct);
            return Results.Ok(new { profiles });
        });

        group.MapGet("/{id:guid}", async (Guid id, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var profile = await sender.Send(new GetBudgetProfileQuery(CurrentUser.GetId(ctx), id), ct);
            return Results.Ok(new { profile });
        });

        group.MapPut("/{id:guid}", async (Guid id, UpdateBudgetProfileRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var profile = await sender.Send(new UpdateBudgetProfileCommand(CurrentUser.GetId(ctx), id, body.Name, body.Cycle), ct);
            return Results.Ok(new { profile });
        });

        group.MapPut("/{id:guid}/carryover-enabled", async (Guid id, EnabledRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var profile = await sender.Send(new SetBudgetCarryoverEnabledCommand(CurrentUser.GetId(ctx), id, body.Enabled), ct);
            return Results.Ok(new { profile });
        });

        group.MapPut("/{id:guid}/auto-update-planned-amount", async (Guid id, EnabledRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var profile = await sender.Send(new SetBudgetAutoUpdatePlannedAmountCommand(CurrentUser.GetId(ctx), id, body.Enabled), ct);
            return Results.Ok(new { profile });
        });

        group.MapDelete("/{id:guid}", async (Guid id, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteBudgetProfileCommand(CurrentUser.GetId(ctx), id), ct);
            return Results.Ok();
        });

        // ── Period ───────────────────────────────────────────────────────
        group.MapPost("/{id:guid}/periods", async (Guid id, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var period = await sender.Send(new CreateBudgetPeriodCommand(CurrentUser.GetId(ctx), id), ct);
            return Results.Ok(new { period });
        });

        group.MapGet("/{id:guid}/periods", async (Guid id, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var periods = await sender.Send(new ListBudgetPeriodsQuery(CurrentUser.GetId(ctx), id), ct);
            return Results.Ok(new { periods });
        });

        // ── People ───────────────────────────────────────────────────────
        group.MapPost("/{id:guid}/people", async (Guid id, AddBudgetPeopleRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var people = body.People.Select(p => new NewBudgetPersonInput(p.UserName, p.UserId, p.Color ?? "")).ToList();
            var result = await sender.Send(new AddBudgetPeopleCommand(CurrentUser.GetId(ctx), id, people), ct);
            return Results.Ok(new { people = result });
        });

        group.MapGet("/{id:guid}/people", async (Guid id, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var people = await sender.Send(new ListBudgetPeopleQuery(CurrentUser.GetId(ctx), id), ct);
            return Results.Ok(new { people });
        });

        group.MapPut("/{id:guid}/people/{personId:int}", async (Guid id, int personId, UpdatePersonRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var person = await sender.Send(new UpdateBudgetPersonCommand(CurrentUser.GetId(ctx), id, personId, body.Color), ct);
            return Results.Ok(new { person });
        });

        group.MapPut("/{id:guid}/people/{personId:int}/role", async (Guid id, int personId, UpdateRoleRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var person = await sender.Send(new UpdateBudgetPersonRoleCommand(CurrentUser.GetId(ctx), id, personId, body.Role), ct);
            return Results.Ok(new { person });
        });

        group.MapDelete("/{id:guid}/people/{personId:int}", async (
            Guid id, int personId, int? replacementPersonId, Guid? replacementPaymentMethodId,
            HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new RemoveBudgetPersonCommand(
                CurrentUser.GetId(ctx), id, personId, replacementPersonId ?? 0, replacementPaymentMethodId), ct);
            return Results.Ok();
        });

        group.MapPut("/{id:guid}/my-preferences", async (Guid id, MyPreferencesRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var person = await sender.Send(new UpdateMyBudgetPreferencesCommand(
                CurrentUser.GetId(ctx), id, body.PlanChartType, body.OverviewChartType), ct);
            return Results.Ok(new { person });
        });

        group.MapPut("/{id:guid}/my-manual-match-review-preference", async (Guid id, EnabledRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var person = await sender.Send(new UpdateMyManualMatchReviewPreferenceCommand(CurrentUser.GetId(ctx), id, body.Enabled), ct);
            return Results.Ok(new { person });
        });

        group.MapPut("/{id:guid}/my-focused-view-preference", async (Guid id, EnabledRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var person = await sender.Send(new UpdateMyFocusedViewPreferenceCommand(CurrentUser.GetId(ctx), id, body.Enabled), ct);
            return Results.Ok(new { person });
        });

        // Separate namespace — GET /rest/v1/budgets/{id}/periods is the
        // profile-scoped list; a single period is looked up by its own id.
        var periodGroup = app.MapGroup("/rest/v1/budget-periods").WithTags("budgets").RequireAuthorization();
        periodGroup.MapGet("/{id:guid}", async (Guid id, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var period = await sender.Send(new GetBudgetPeriodQuery(CurrentUser.GetId(ctx), id), ct);
            return Results.Ok(new { period });
        });
    }

    private sealed record CreateBudgetProfileRequestBody(string Name, string Cycle);
    private sealed record UpdateBudgetProfileRequestBody(string Name, string Cycle);
    private sealed record EnabledRequestBody(bool Enabled);
    private sealed record UpdatePersonRequestBody(string Color);
    private sealed record UpdateRoleRequestBody(string Role);
    private sealed record MyPreferencesRequestBody(string? PlanChartType, string? OverviewChartType);
    private sealed record NewPersonRequestBody(string UserName, Guid? UserId, string? Color);
    private sealed record AddBudgetPeopleRequestBody(List<NewPersonRequestBody> People);
}
